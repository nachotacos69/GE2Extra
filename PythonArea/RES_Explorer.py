import sys
import struct
import os
import zlib
import io
import hashlib
import shutil
import traceback
from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QPushButton, QTreeWidget, QTreeWidgetItem, QFileDialog,
    QSplitter, QHeaderView, QMessageBox, QAbstractScrollArea,
    QMenu, QAction, QProgressDialog
)
from PyQt5.QtCore import Qt, QByteArray, QThread, pyqtSignal
from PyQt5.QtGui import QFont, QFontMetrics, QPainter, QCursor, QColor, QKeySequence

MAGIC_HEADER = 0x73657250
BLZ2_HEADER = b'blz2'
BLZ4_HEADER = b'blz4'
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

class ResHeader:
    def __init__(self, file_data):
        if len(file_data) < 32: raise ValueError("File data is too short for a valid header.")
        header_struct = struct.unpack('<I I B I 3x I 12x', file_data[:32])
        self.magic, self.group_offset, self.group_count, self.unk1, self.configs_offset = header_struct
        if self.magic != MAGIC_HEADER: raise ValueError(f"Invalid magic header: expected {hex(MAGIC_HEADER)}, got {hex(self.magic)}")

class ResDataSet:
    def __init__(self, file_data, group_count, group_offset):
        self.datasets = []
        for i in range(group_count):
            offset = group_offset + (i * 8)
            if offset + 8 > len(file_data):
                print(f"Warning: Incomplete dataset entry at index {i}")
                continue
            dataset_offset, dataset_count = struct.unpack('<I I', file_data[offset:offset+8])
            self.datasets.append({'offset': dataset_offset, 'count': dataset_count})

class ResFileSet:
    def __init__(self, file_data, datasets):
        self.filesets = []
        fileset_start = 0x60
        total_fileset_count = sum(d['count'] for d in datasets)
        for i in range(total_fileset_count):
            offset = fileset_start + (i * 32)
            if offset + 32 > len(file_data):
                print(f"Warning: Incomplete fileset entry at index {i}")
                continue
            fs_struct = struct.unpack('<I I I I 12x I', file_data[offset:offset+32])
            raw_offset, size, offset_name, chunk_name, unpack_size = fs_struct
            address_mode = (raw_offset & 0xFF000000) >> 24
            real_offset, skip_reason = None, None
            if address_mode == 0x00: skip_reason = "Unknown address mode (0x00)"
            elif address_mode == 0x30: skip_reason = "DataSet file (0x30)"
            elif address_mode in (0xC0, 0xD0): real_offset = raw_offset & 0x00FFFFFF
            elif address_mode in (0x40, 0x50, 0x60): real_offset = (raw_offset & 0x00FFFFFF) * 0x800
            if raw_offset == 0 and size == 0 and offset_name == 0 and chunk_name == 0 and unpack_size != 0:
                skip_reason = "Dummy fileset"
            name_info = self._read_name_info(file_data, offset_name, chunk_name)
            self.filesets.append({
                'raw_offset': raw_offset, 'real_offset': real_offset, 'size': size,
                'unpack_size': unpack_size, 'address_mode': address_mode,
                'offset_name': offset_name, 'chunk_name': chunk_name,
                'name': name_info['name'], 'type': name_info['type'],
                'directories': name_info['directories'], 'skip_reason': skip_reason,
                'is_compressed': False # Will be determined later
            })

    def _read_name_info(self, file_data, offset_name, chunk_name):
        name_info = {'name': '', 'type': '', 'directories': []}
        if offset_name == 0 or chunk_name == 0: return name_info
        pointers = []
        for i in range(chunk_name):
            pointer_offset = offset_name + (i * 4)
            if pointer_offset + 4 > len(file_data): continue
            pointers.append(struct.unpack('<I', file_data[pointer_offset:pointer_offset+4])[0])
        for i, pointer in enumerate(pointers):
            if pointer == 0 or pointer >= len(file_data): continue
            end_pos = file_data.find(b'\x00', pointer)
            if end_pos == -1: end_pos = len(file_data)
            string = file_data[pointer:end_pos].decode('utf-8', errors='ignore')
            if i == 0: name_info['name'] = string
            elif i == 1: name_info['type'] = string
            else: name_info['directories'].append(string)
        return name_info

def parse_rtbl_data(file_data):
    filesets = []
    offset = 0
    while offset + 32 <= len(file_data):
        if file_data[offset:offset+16] == b'\x00' * 16:
            offset += 16
            continue
        fs_struct = struct.unpack('<I I I I 12x I', file_data[offset:offset+32])
        raw_offset, size, offset_name, chunk_name, unpack_size = fs_struct
        if offset_name != 0x20:
            offset += 16
            continue
        address_mode = (raw_offset & 0xFF000000) >> 24
        real_offset, skip_reason = None, None
        if address_mode == 0x00: skip_reason = "Unknown address mode (0x00)"
        elif address_mode == 0x30: skip_reason = "DataSet file (0x30)"
        elif address_mode in (0xC0, 0xD0): real_offset = raw_offset & 0x00FFFFFF
        elif address_mode in (0x40, 0x50, 0x60): real_offset = (raw_offset & 0x00FFFFFF) * 0x800
        if raw_offset == 0 and size == 0 and offset_name == 0 and chunk_name == 0 and unpack_size != 0:
            skip_reason = "Dummy fileset"
        name_info = {'name': '', 'type': '', 'directories': []}
        if chunk_name > 0:
            name_offset = offset + 32 + (chunk_name * 4)
            pos = name_offset
            end_pos = file_data.find(b'\x00', pos)
            if end_pos != -1:
                name_info['name'] = file_data[pos:end_pos].decode('utf-8', errors='ignore')
                pos = end_pos + 1
            end_pos = file_data.find(b'\x00', pos)
            if end_pos != -1:
                name_info['type'] = file_data[pos:end_pos].decode('utf-8', errors='ignore')
        filesets.append({
            'raw_offset': raw_offset, 'real_offset': real_offset, 'size': size,
            'unpack_size': unpack_size, 'address_mode': address_mode,
            'offset_name': offset_name, 'chunk_name': chunk_name,
            'name': name_info['name'], 'type': name_info['type'],
            'directories': name_info['directories'], 'skip_reason': skip_reason,
            'is_compressed': False # Will be determined later
        })
        offset += 32
    return filesets

def _decompress_blz2(chunk_data):
    decompressed_blocks = []
    with io.BytesIO(chunk_data) as f:
        if f.read(4) != BLZ2_HEADER: raise ValueError("Invalid BLZ2 header")
        while True:
            size_bytes = f.read(2)
            if not size_bytes: break
            compressed_size = struct.unpack('<H', size_bytes)[0]
            if compressed_size == 0: continue
            compressed_data = f.read(compressed_size)
            if len(compressed_data) != compressed_size: raise ValueError("Incomplete compressed block")
            decompressor = zlib.decompressobj(wbits=-15)
            decompressed_blocks.append(decompressor.decompress(compressed_data))
    if len(decompressed_blocks) > 1:
        decompressed_blocks = decompressed_blocks[1:] + decompressed_blocks[:1]
    return b''.join(decompressed_blocks)

def _decompress_blz4(chunk_data):
    if len(chunk_data) < 32:
        raise ValueError(f"Input data length {len(chunk_data)} is too short for BLZ4 format")

    block_data = []
    with io.BytesIO(chunk_data) as f:
        magic = f.read(4)
        if magic != BLZ4_HEADER:
            raise ValueError(f"Invalid BLZ4 magic number: {magic!r} != {BLZ4_HEADER!r}")
        unpack_size = struct.unpack('<I', f.read(4))[0]
        f.read(8) # Skip padding
        md5 = f.read(16)
        while f.tell() < len(chunk_data):
            chunk_size_bytes = f.read(2)
            if not chunk_size_bytes: break
            if len(chunk_size_bytes) < 2: raise ValueError("Incomplete chunk size data")
            chunk_size = struct.unpack('<H', chunk_size_bytes)[0]
            if chunk_size == 0:
                final_block = f.read()
                if final_block: block_data.append(final_block)
                break
            else:
                block = f.read(chunk_size)
                if len(block) < chunk_size:
                    raise ValueError(f"Expected {chunk_size} bytes for block, got {len(block)}")
                block_data.append(block)
        if not block_data:
            raise ValueError("No data blocks found in BLZ4 data")
    if len(block_data) > 1:
        real_list = block_data[-1:] + block_data[:-1]
    else:
        real_list = block_data
    decompressed_blocks = []
    for block in real_list:
        try:
            decompressed = zlib.decompress(block, wbits=-15)
            decompressed_blocks.append(decompressed)
        except zlib.error as e:
            raise ValueError(f"Failed to decompress block: {e}")
    result = b''.join(decompressed_blocks)
    computed_md5 = hashlib.md5(result).digest()
    if computed_md5 != md5:
        print(f"Warning: BLZ4 MD5 checksum mismatch for file. Output may be corrupted.")
    if len(result) != unpack_size:
        print(f"Warning: BLZ4 unpack size mismatch. Expected {unpack_size}, got {len(result)}.")
    return result

def get_source_path(fileset, current_file_path):
    address_mode = fileset['address_mode']
    if address_mode in (0x40, 0x50, 0x60):
        rdp_map = {0x40: 'package.rdp', 0x50: 'data.rdp', 0x60: 'patch.rdp'}
        rdp_file = rdp_map.get(address_mode)
        rdp_path = os.path.join(os.path.dirname(current_file_path), rdp_file)
        if not os.path.exists(rdp_path):
            rdp_path = os.path.join(SCRIPT_DIR, rdp_file)
        if not os.path.exists(rdp_path):
            raise FileNotFoundError(f"RDP file '{rdp_file}' not found in the current directory or script directory.")
        return rdp_path
    return current_file_path

def get_raw_file_chunk(fileset, current_file_path):
    real_offset, size = fileset['real_offset'], fileset['size']
    if real_offset is None or size == 0: return b''
    source_file = get_source_path(fileset, current_file_path)
    with open(source_file, 'rb') as f:
        f.seek(real_offset)
        chunk_data = f.read(size)
        if len(chunk_data) != size: raise IOError("Could not read the complete file chunk.")
        return chunk_data

def get_decompressed_data(chunk_data):
    if chunk_data.startswith(BLZ2_HEADER):
        return _decompress_blz2(chunk_data)
    if chunk_data.startswith(BLZ4_HEADER):
        return _decompress_blz4(chunk_data)
    return chunk_data

class HexEditor(QAbstractScrollArea):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFocusPolicy(Qt.StrongFocus)
        self.setFont(QFont("Courier", 10))
        self._data = QByteArray()
        self.bytes_per_line = 16
        self.selection_start = -1
        self.selection_end = -1
        fm = self.fontMetrics()
        self.char_width = fm.horizontalAdvance('0')
        self.char_height = fm.height()
        self.address_width = self.char_width * 9
        self.hex_width = self.char_width * (self.bytes_per_line * 3)
        self.ascii_width = self.char_width * self.bytes_per_line
        self.gap = self.char_width * 2
        self.viewport().setCursor(Qt.IBeamCursor)

    def setData(self, data):
        self._data = QByteArray(data)
        self.selection_start = -1
        self.selection_end = -1
        self.verticalScrollBar().setRange(0, (len(self._data) // self.bytes_per_line))
        self.verticalScrollBar().setValue(0)
        self.viewport().update()

    def data(self):
        return self._data

    def paintEvent(self, event):
        painter = QPainter(self.viewport())
        painter.setFont(self.font())
        fm = self.fontMetrics()
        first_line_idx = self.verticalScrollBar().value()
        last_line_idx = first_line_idx + (self.viewport().height() // self.char_height) + 1
        for line_idx in range(first_line_idx, last_line_idx):
            line_top_y = (line_idx - first_line_idx) * self.char_height
            address = line_idx * self.bytes_per_line
            if address >= len(self._data): break
            text_baseline_y = line_top_y + fm.ascent()
            painter.drawText(0, line_top_y, self.address_width - self.char_width, self.char_height, Qt.AlignRight, f"{address:08X}")
            for i in range(self.bytes_per_line):
                byte_pos = address + i
                if byte_pos >= len(self._data): break
                byte_val = self._data[byte_pos]
                if isinstance(byte_val, (bytes, str)): byte_val = ord(byte_val)
                hex_x = self.address_width + i * 3 * self.char_width
                ascii_x = self.address_width + self.hex_width + self.gap + i * self.char_width
                if self.selection_start <= byte_pos < self.selection_end:
                    painter.fillRect(hex_x, line_top_y, self.char_width * 2, self.char_height, QColor(0, 120, 215, 150))
                    painter.fillRect(ascii_x, line_top_y, self.char_width, self.char_height, QColor(0, 120, 215, 150))
                painter.drawText(hex_x, text_baseline_y, f"{byte_val:02X}")
                char = chr(byte_val) if 32 <= byte_val <= 126 else '.'
                painter.drawText(ascii_x, text_baseline_y, char)

    def keyPressEvent(self, event):
        if event.matches(QKeySequence.Copy):
            if self.selection_start != -1 and self.selection_end > self.selection_start:
                selected_data = self._data.mid(self.selection_start, self.selection_end - self.selection_start)
                QApplication.clipboard().setText(selected_data.toHex().data().decode())
            return
        super().keyPressEvent(event)

    def mousePressEvent(self, event):
        pos = self.pos_to_byte(event.pos())
        if pos is not None:
            self.selection_start = pos
            self.selection_end = pos + 1
            self.viewport().update()

    def mouseMoveEvent(self, event):
        if event.buttons() == Qt.LeftButton:
            pos = self.pos_to_byte(event.pos())
            if pos is not None:
                self.selection_end = pos + 1
                self.viewport().update()

    def pos_to_byte(self, pos):
        first_line = self.verticalScrollBar().value()
        line = first_line + (pos.y() // self.char_height)
        if self.address_width <= pos.x() < self.address_width + self.hex_width:
            col = (pos.x() - self.address_width) // (self.char_width * 3)
        elif self.address_width + self.hex_width + self.gap <= pos.x():
            col = (pos.x() - self.address_width - self.hex_width - self.gap) // self.char_width
        else:
            return None
        byte_pos = line * self.bytes_per_line + col
        return byte_pos if 0 <= byte_pos < len(self._data) else None

class DataLoader(QThread):
    dataLoaded = pyqtSignal(object, QByteArray)
    errorOccurred = pyqtSignal(str)
    def __init__(self):
        super().__init__()
        self.fileset = None
        self.current_file_path = None
    def run(self):
        try:
            chunk_data = get_raw_file_chunk(self.fileset, self.current_file_path)
            decompressed_data = get_decompressed_data(chunk_data)
            self.dataLoaded.emit(self.fileset, QByteArray(decompressed_data))
        except Exception as e:
            self.errorOccurred.emit(str(e))

class PreloaderThread(QThread):
    def __init__(self, filesets, file_path, cache_dict):
        super().__init__()
        self.filesets = filesets
        self.current_file_path = file_path
        self.data_cache = cache_dict
        self.is_running = True

    def run(self):
        for i, fileset in enumerate(self.filesets):
            if not self.is_running:
                break
            if not fileset['skip_reason'] and i not in self.data_cache:
                try:
                    chunk_data = get_raw_file_chunk(fileset, self.current_file_path)
                    decompressed_data = get_decompressed_data(chunk_data)
                    self.data_cache[i] = QByteArray(decompressed_data)
                except Exception as e:
                    print(f"Preloading error for '{fileset.get('name', 'N/A')}': {e}")
    def stop(self):
        self.is_running = False


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("RES Explorer")
        self.setGeometry(100, 100, 1200, 800)
        self.file_history = []
        self.current_file_path = None
        self.parsed_data = {}
        self.data_cache = {}
        self.preloader_thread = None
        self.data_loader = DataLoader()
        self.data_loader.dataLoaded.connect(self.on_data_loaded)
        self.data_loader.errorOccurred.connect(self.on_data_load_error)
        self.setup_ui()

    def setup_ui(self):
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        top_bar_layout = QHBoxLayout()
        self.open_btn = QPushButton("Open RES File")
        self.open_btn.clicked.connect(self.open_file)
        self.back_btn = QPushButton("Back")
        self.back_btn.clicked.connect(self.go_back)
        self.back_btn.setEnabled(False)
        top_bar_layout.addWidget(self.open_btn)
        top_bar_layout.addWidget(self.back_btn)
        top_bar_layout.addStretch()
        main_layout.addLayout(top_bar_layout)
        splitter = QSplitter(Qt.Horizontal)
        main_layout.addWidget(splitter)
        self.tree = QTreeWidget()
        self.tree.setHeaderLabels(["Name", "Value"])
        self.tree.header().setSectionResizeMode(0, QHeaderView.ResizeToContents)
        self.tree.itemClicked.connect(self.on_tree_item_clicked)
        self.tree.itemDoubleClicked.connect(self.on_tree_item_double_clicked)
        self.tree.setSelectionMode(QTreeWidget.ExtendedSelection)
        self.tree.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self.show_context_menu)
        splitter.addWidget(self.tree)
        self.hex_editor = HexEditor()
        splitter.addWidget(self.hex_editor)
        splitter.setSizes([400, 800])

    def open_file(self):
        path, _ = QFileDialog.getOpenFileName(self, "Open File", "", "RES Files (*.res);;All Files (*)")
        if path:
            self.load_file(path)

    def load_file(self, path, is_nested=False):
        try:
            with open(path, 'rb') as f: file_data = f.read()
            if not is_nested:
                self.file_history.clear()
                self.data_cache.clear()
            self.file_history.append(path)
            self.current_file_path = path
            self.update_ui(file_data)
            self.back_btn.setEnabled(len(self.file_history) > 1)
            self.start_preloader()
        except Exception as e:
            QMessageBox.critical(self, "File Load Error", f"Error loading file {path}:\n{e}")
            self.go_back()

    def update_ui(self, file_data):
        self.tree.clear()
        self.hex_editor.setData(b'')
        self.parsed_data = {}
        file_name = os.path.basename(self.current_file_path)
        root_item = QTreeWidgetItem(self.tree, [file_name])
        root_item.setExpanded(True)
        try:
            if file_name.lower().endswith('.res'):
                header = ResHeader(file_data)
                dataset = ResDataSet(file_data, header.group_count, header.group_offset)
                fileset = ResFileSet(file_data, dataset.datasets)
                self.parsed_data['filesets'] = fileset.filesets
                self.parsed_data['header'] = header
                header_item = QTreeWidgetItem(root_item, ["Header"])
                QTreeWidgetItem(header_item, ["Magic", f"{header.magic:#010x}"])
                QTreeWidgetItem(header_item, ["Group Offset", f"{header.group_offset:#010x}"])
                QTreeWidgetItem(header_item, ["Group Count", str(header.group_count)])
                QTreeWidgetItem(header_item, ["Configs Offset", f"{header.configs_offset:#010x}"])
                header_item.setExpanded(True)
                dataset_root = QTreeWidgetItem(root_item, [f"DataSet ({sum(d['count'] for d in dataset.datasets)} total)"])
                for i, ds in enumerate(dataset.datasets):
                    QTreeWidgetItem(dataset_root, [f"DataSet ({i})", f"Offset: {ds['offset']:#010x} | Count: {ds['count']}"])
                fileset_root = QTreeWidgetItem(root_item, ["Fileset"])
                self.populate_fileset_tree(fileset_root, fileset.filesets)
                fileset_root.setExpanded(True)
            elif file_name.lower().endswith('.rtbl'):
                filesets = parse_rtbl_data(file_data)
                self.parsed_data['filesets'] = filesets
                fileset_root = QTreeWidgetItem(root_item, ["Fileset"])
                self.populate_fileset_tree(fileset_root, filesets)
                fileset_root.setExpanded(True)
        except Exception as e:
            QMessageBox.critical(self, "Parsing Error", f"Error parsing file {file_name}:\n{e}")

    def populate_fileset_tree(self, parent_item, filesets):
        for i, fs in enumerate(filesets):
            filename = f"{fs['name']}.{fs['type']}" if fs['type'] else fs['name'] or f"Unnamed File {i}"
            display_path = os.path.join(*(fs['directories'] + [filename])) if fs['directories'] else filename
            file_item = QTreeWidgetItem(parent_item, [display_path])
            file_item.setData(0, Qt.UserRole, i)
            is_compressed_str = "N/A"
            try:
                chunk_header = get_raw_file_chunk({'real_offset': fs['real_offset'], 'size': 4, 'address_mode': fs['address_mode']}, self.current_file_path)
                if chunk_header == BLZ2_HEADER:
                    is_compressed_str = "Yes (BLZ2)"
                    fs['is_compressed'] = True
                elif chunk_header == BLZ4_HEADER:
                    is_compressed_str = "Yes (BLZ4)"
                    fs['is_compressed'] = True
                else:
                    is_compressed_str = "No"
                    fs['is_compressed'] = False
            except (IOError, FileNotFoundError, TypeError):
                pass
            QTreeWidgetItem(file_item, ["Address Mode", f"{fs['address_mode']:#04x}"])
            QTreeWidgetItem(file_item, ["Real Offset", f"{fs['real_offset']:#010x}" if fs['real_offset'] is not None else "N/A"])
            QTreeWidgetItem(file_item, ["Size", str(fs['size'])])
            QTreeWidgetItem(file_item, ["Unpacked Size", str(fs['unpack_size'])])
            QTreeWidgetItem(file_item, ["Compressed", is_compressed_str])
            if fs['skip_reason']:
                QTreeWidgetItem(file_item, ["Status", f"Skipped ({fs['skip_reason']})"])
                file_item.setForeground(0, Qt.gray)

    def on_tree_item_clicked(self, item, column):
        fileset_index = item.data(0, Qt.UserRole)
        if fileset_index is None: return
        fileset = self.parsed_data['filesets'][fileset_index]
        if fileset['skip_reason']:
            self.hex_editor.setData(f"File skipped: {fileset['skip_reason']}".encode())
            return
        if fileset_index in self.data_cache:
            self.hex_editor.setData(self.data_cache[fileset_index])
        else:
            self.hex_editor.setData(b"Loading...")
            self.data_loader.fileset = fileset
            self.data_loader.current_file_path = self.current_file_path
            self.data_loader.start()

    def on_tree_item_double_clicked(self, item, column):
        fileset_index = item.data(0, Qt.UserRole)
        if fileset_index is None: return
        fileset = self.parsed_data['filesets'][fileset_index]
        file_type = fileset.get('type', '').lower()
        if file_type in ('res', 'rtbl'):
            try:
                raw_chunk = get_raw_file_chunk(fileset, self.current_file_path)
                nested_data = get_decompressed_data(raw_chunk)
                if not nested_data:
                    QMessageBox.warning(self, "Empty File", f"Nested file '{fileset['name']}' is empty.")
                    return
                temp_dir = os.path.join(SCRIPT_DIR, 'temp_nested')
                os.makedirs(temp_dir, exist_ok=True)
                temp_path = os.path.join(temp_dir, f"{fileset['name']}_{fileset_index}.{file_type}")
                with open(temp_path, 'wb') as f: f.write(nested_data)
                self.load_file(temp_path, is_nested=True)
            except Exception as e:
                QMessageBox.critical(self, "Error", f"Could not open nested file:\n\n{str(e)}")

    def on_data_loaded(self, fileset, data):
        try:
            fileset_index = self.parsed_data['filesets'].index(fileset)
            self.data_cache[fileset_index] = data
            current_item = self.tree.currentItem()
            if current_item and current_item.data(0, Qt.UserRole) == fileset_index:
                self.hex_editor.setData(data)
        except (ValueError, AttributeError):
            pass

    def on_data_load_error(self, error_message):
        self.hex_editor.setData(f"Error: {error_message}".encode())
        QMessageBox.warning(self, "Data Load Error", error_message)

    def go_back(self):
        if len(self.file_history) > 1:
            self.stop_preloader()
            last_path = self.file_history.pop()
            if 'temp_nested' in last_path and os.path.exists(last_path):
                try:
                    os.remove(last_path)
                except OSError as e:
                    print(f"Warning: Could not remove temp file {last_path}: {e}")
            self.current_file_path = self.file_history[-1]
            try:
                with open(self.current_file_path, 'rb') as f: file_data = f.read()
                self.update_ui(file_data)
                self.start_preloader()
            except Exception as e:
                QMessageBox.critical(self, "Navigation Error", f"Error going back to {self.current_file_path}:\n{e}")
            self.back_btn.setEnabled(len(self.file_history) > 1)

    def show_context_menu(self, pos):
        menu = QMenu()
        selected_items = self.tree.selectedItems()
        extract_action = menu.addAction("Extract Selected")
        extract_action.setEnabled(any(item.data(0, Qt.UserRole) is not None for item in selected_items))
        extract_folder_action = menu.addAction("Extract as Folder...")
        is_extractable_as_folder = False
        if len(selected_items) == 1:
            item = selected_items[0]
            if item.parent() is None:
                is_extractable_as_folder = True
            else:
                idx = item.data(0, Qt.UserRole)
                if idx is not None:
                    fileset = self.parsed_data['filesets'][idx]
                    if fileset.get('type', '').lower() in ('res', 'rtbl'):
                        is_extractable_as_folder = True
        extract_folder_action.setEnabled(is_extractable_as_folder)
        action = menu.exec_(self.tree.mapToGlobal(pos))
        if action == extract_action:
            self.extract_selected(selected_items)
        elif action == extract_folder_action:
            self.extract_as_folder(selected_items[0])

    def extract_selected(self, items):
        files_to_extract = [self.parsed_data['filesets'][it.data(0, Qt.UserRole)] for it in items if it.data(0, Qt.UserRole) is not None]
        if not files_to_extract: return
        output_dir = QFileDialog.getExistingDirectory(self, "Select Output Directory")
        if not output_dir: return
        self._perform_extraction(files_to_extract, self.current_file_path, output_dir)

    def extract_as_folder(self, item):
        output_dir = QFileDialog.getExistingDirectory(self, "Select Parent Directory for Extraction")
        if not output_dir: return
        filesets_to_extract = []
        source_path_for_extraction = self.current_file_path
        folder_name = ""
        temp_path = None
        try:
            if item.parent() is None:
                filesets_to_extract = self.parsed_data.get('filesets', [])
                folder_name, _ = os.path.splitext(os.path.basename(self.current_file_path))
            else:
                fileset_index = item.data(0, Qt.UserRole)
                if fileset_index is None: return
                fileset = self.parsed_data['filesets'][fileset_index]
                folder_name = fileset['name']
                raw_chunk = get_raw_file_chunk(fileset, self.current_file_path)
                nested_data = get_decompressed_data(raw_chunk)
                if not nested_data:
                    QMessageBox.warning(self, "Empty File", f"Nested file '{fileset['name']}' is empty and cannot be extracted.")
                    return
                temp_dir = os.path.join(SCRIPT_DIR, 'temp_nested')
                os.makedirs(temp_dir, exist_ok=True)
                nested_file_type = fileset.get('type', 'res').lower()
                temp_path = os.path.join(temp_dir, f"temp_extract_{fileset_index}.{nested_file_type}")
                with open(temp_path, 'wb') as f: f.write(nested_data)
                if nested_file_type == 'res':
                    header = ResHeader(nested_data)
                    dataset = ResDataSet(nested_data, header.group_count, header.group_offset)
                    nested_fileset_obj = ResFileSet(nested_data, dataset.datasets)
                    filesets_to_extract = nested_fileset_obj.filesets
                elif nested_file_type == 'rtbl':
                    filesets_to_extract = parse_rtbl_data(nested_data)
                source_path_for_extraction = temp_path
            if not filesets_to_extract:
                QMessageBox.information(self, "Nothing to Extract", f"The file '{folder_name}' contains no extractable entries.")
                return
            self._perform_extraction(filesets_to_extract, source_path_for_extraction, output_dir, base_name=folder_name)
        except Exception as e:
            traceback.print_exc()
            QMessageBox.critical(self, "Extraction Error", f"An unexpected error occurred while preparing to extract the folder:\n\n{e}")
        finally:
            if temp_path and os.path.exists(temp_path):
                os.remove(temp_path)

    def _perform_extraction(self, files_to_extract, source_path, output_dir, base_name=""):
        final_output_dir = os.path.join(output_dir, base_name) if base_name else output_dir
        os.makedirs(final_output_dir, exist_ok=True)
        progress = QProgressDialog(f"Extracting to '{os.path.basename(final_output_dir)}'...", "Cancel", 0, len(files_to_extract), self)
        progress.setWindowModality(Qt.WindowModal)
        
        for i, fileset in enumerate(files_to_extract):
            progress.setValue(i)
            filename = f"{fileset['name']}.{fileset['type']}" if fileset['type'] else fileset['name'] or f"Unnamed_File_{i}"
            
            if progress.wasCanceled():
                break

            if fileset['skip_reason']:
                progress.setLabelText(f"Skipping: {filename} ({fileset['skip_reason']})")
                QApplication.processEvents()
                continue

            progress.setLabelText(f"Extracting: {filename}")
            QApplication.processEvents()

            try:
                raw_chunk = get_raw_file_chunk(fileset, source_path)
                final_data = get_decompressed_data(raw_chunk)
                
                relative_path = os.path.join(*fileset['directories']) if fileset['directories'] else ''
                current_final_dir = os.path.join(final_output_dir, relative_path)
                os.makedirs(current_final_dir, exist_ok=True)
                
                output_path = os.path.join(current_final_dir, filename)
                
                counter = 1
                base, ext = os.path.splitext(output_path)
                while os.path.exists(output_path):
                    output_path = f"{base}_{counter:04d}{ext}"
                    counter += 1
                with open(output_path, 'wb') as f: f.write(final_data)
            except Exception as e:
                print(f"Error extracting '{filename}': {e}")
                QMessageBox.warning(self, "Extraction Error", f"Could not extract '{filename}'.\nReason: {e}")
        
        progress.setValue(len(files_to_extract))
        progress.setLabelText("Extraction complete.")

    def start_preloader(self):
        self.stop_preloader()
        if 'filesets' in self.parsed_data:
            self.preloader_thread = PreloaderThread(self.parsed_data['filesets'], self.current_file_path, self.data_cache)
            self.preloader_thread.start()

    def stop_preloader(self):
        if self.preloader_thread and self.preloader_thread.isRunning():
            self.preloader_thread.stop()
            self.preloader_thread.wait()
            self.preloader_thread = None

    def closeEvent(self, event):
        self.stop_preloader()
        temp_dir = os.path.join(SCRIPT_DIR, 'temp_nested')
        if os.path.exists(temp_dir):
            try:
                shutil.rmtree(temp_dir)
            except OSError as e:
                print(f"Could not remove temp directory {temp_dir}: {e}")
        event.accept()

if __name__ == '__main__':
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())