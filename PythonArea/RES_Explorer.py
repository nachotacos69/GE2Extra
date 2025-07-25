import sys
import struct
import os
import zlib
import io
import hashlib
import shutil
import traceback
import collections
from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QPushButton, QTreeWidget, QTreeWidgetItem, QFileDialog,
    QSplitter, QHeaderView, QMessageBox, QAbstractScrollArea,
    QMenu, QAction, QProgressDialog, QDialog,
    QDialogButtonBox, QListWidget, QListWidgetItem, QAbstractItemView,
    QComboBox, QGroupBox, QCheckBox, QLabel
)
from PyQt5.QtCore import Qt, QByteArray, QThread, pyqtSignal
from PyQt5.QtGui import QFont, QFontMetrics, QPainter, QCursor, QColor, QKeySequence

# --- HELPERS AND CONSTANTS ---

MAGIC_HEADER = 0x73657250
BLZ2_HEADER = b'blz2'
BLZ4_HEADER = b'blz4'
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

COUNTRY_TYPES_3 = ["English", "French", "Italian"]
COUNTRY_TYPES_6 = ["English", "French", "Italian", "Deutsch", "Espanol", "Russian"]


class TempHandler:
    """Manages temporary directories for extracted and nested files."""
    def __init__(self):
        self.base_dir = os.path.join(SCRIPT_DIR, 'temp_data')
        self.level_stack = []
        self.clear_all()

    def _sanitize(self, name):
        """Sanitizes a string to be a valid directory name."""
        return "".join(c for c in name if c.isalnum() or c in (' ', '.', '_', '-')).rstrip()

    def _on_rm_error(self, func, path, exc_info):
        """Error handler for shutil.rmtree to handle read-only files on Windows."""
        import stat
        if not os.access(path, os.W_OK):
            os.chmod(path, stat.S_IWUSR)
            func(path)
        else:
            raise exc_info[1]

    def push_level(self, name):
        """Adds a new directory level for a newly opened file."""
        sanitized_name = self._sanitize(name)
        self.level_stack.append(sanitized_name)
        os.makedirs(self.get_current_session_dir(), exist_ok=True)

    def pop_level_and_cleanup(self):
        """Removes a directory level and cleans up its corresponding folder."""
        if self.level_stack:
            dir_to_remove = self.get_current_session_dir()
            self.level_stack.pop()
            if os.path.isdir(dir_to_remove):
                try:
                    shutil.rmtree(dir_to_remove, onerror=self._on_rm_error)
                except Exception as e:
                    print(f"Warning: Failed to remove temp level directory '{dir_to_remove}'. Reason: {e}")

    def get_current_session_dir(self):
        """Gets the full path for the current level of nesting."""
        return os.path.join(self.base_dir, *self.level_stack)

    def clear_all(self):
        """Clears the entire temporary data directory."""
        if os.path.exists(self.base_dir):
            try:
                shutil.rmtree(self.base_dir, onerror=self._on_rm_error)
            except Exception as e:
                print(f"Error: Failed to clean up temp directory '{self.base_dir}'. Reason: {e}")
        try:
            os.makedirs(self.base_dir, exist_ok=True)
        except Exception as e:
            print(f"Fatal Error: Could not create temp directory '{self.base_dir}'. Reason: {e}")


# --- DATA PARSING CLASSES ---

class ResHeader:
    """Parses the header of a standard RES file."""
    def __init__(self, file_data):
        if len(file_data) < 32: raise ValueError("File data is too short for a valid header.")
        header_struct = struct.unpack('<I I B I 3x I 12x', file_data[:32])
        self.magic, self.group_offset, self.group_count, self.unk1, self.configs_offset = header_struct
        if self.magic != MAGIC_HEADER: raise ValueError(f"Invalid magic header: expected {hex(MAGIC_HEADER)}, got {hex(self.magic)}")

class LocalizedResHeader:
    """Parses the header of a localized RES file."""
    def __init__(self, file_data):
        if len(file_data) < 32: raise ValueError("File data is too short for a localized header.")
        header_struct = struct.unpack('<IIIII8xI', file_data[:32])
        self.magic, self.magic1, self.magic2, self.magic3, self.conf_length, self.country = header_struct
        if self.magic != MAGIC_HEADER:
            print(f"Warning: Magic header is non-standard: {hex(self.magic)}")

class ResDataSet:
    """Parses the dataset entries which point to fileset groups."""
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
    """Parses the fileset entries which describe individual files."""
    def __init__(self, file_data, datasets, fileset_start=0x60):
        self.filesets = []
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
                'is_compressed': False
            })

    def _read_name_info(self, file_data, offset_name, chunk_name):
        """Reads the name, type, and directory strings for a fileset entry."""
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

# --- PARSING AND DECOMPRESSION UTILITIES ---

def parse_rtbl_data(file_data):
    """Parses the fileset entries from an RTBL file."""
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
            if end_pos != -1: name_info['name'] = file_data[pos:end_pos].decode('utf-8', errors='ignore')
            pos = end_pos + 1
            end_pos = file_data.find(b'\x00', pos)
            if end_pos != -1: name_info['type'] = file_data[pos:end_pos].decode('utf-8', errors='ignore')
            
        filesets.append({
            'raw_offset': raw_offset, 'real_offset': real_offset, 'size': size,
            'unpack_size': unpack_size, 'address_mode': address_mode,
            'offset_name': offset_name, 'chunk_name': chunk_name,
            'name': name_info['name'], 'type': name_info['type'],
            'directories': name_info['directories'], 'skip_reason': skip_reason,
            'is_compressed': False
        })
        offset += 32
    return filesets

def _decompress_blz2(chunk_data):
    """Decompresses a BLZ2 compressed data chunk."""
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
    """Decompresses a BLZ4 compressed data chunk."""
    with io.BytesIO(chunk_data) as f:
        magic_header_bytes = f.read(4)
        if magic_header_bytes != BLZ4_HEADER:
            raise ValueError(f"Invalid BLZ4 magic number: {magic_header_bytes!r} != {BLZ4_HEADER!r}")
        if len(chunk_data) < 32:
            raise ValueError(f"Input data length {len(chunk_data)} is too short for BLZ4 format")

        unpack_size = struct.unpack('<I', f.read(4))[0]
        f.read(8)
        md5 = f.read(16)
        block_data = []
        while True:
            chunk_size_bytes = f.read(2)
            if not chunk_size_bytes: break
            if len(chunk_size_bytes) < 2: raise ValueError("Incomplete chunk size data at end of stream")
            chunk_size = struct.unpack('<H', chunk_size_bytes)[0]
            if chunk_size == 0:
                final_block = f.read()
                if final_block: block_data.append(final_block)
                break
            else:
                block = f.read(chunk_size)
                if len(block) < chunk_size: raise ValueError(f"Incomplete block: expected {chunk_size}, got {len(block)}")
                block_data.append(block)
        if not block_data: raise ValueError("No data blocks found in BLZ4 data")

    if len(block_data) > 1:
        real_list = block_data[1:] + block_data[:1]
    else:
        real_list = block_data

    decompressed_blocks = []
    for block in real_list:
        try:
            decompressed = zlib.decompress(block)
            decompressed_blocks.append(decompressed)
        except zlib.error as e:
            try:
                decompressed = zlib.decompress(block, wbits=-15)
                decompressed_blocks.append(decompressed)
            except zlib.error: raise ValueError(f"Failed to decompress block: {e}")

    result = b''.join(decompressed_blocks)
    computed_md5 = hashlib.md5(result).digest()
    if computed_md5 != md5: print("Warning: BLZ4 MD5 checksum mismatch. Output may be corrupted.")
    if len(result) != unpack_size: print(f"Warning: BLZ4 unpack size mismatch. Expected {unpack_size}, got {len(result)}.")
    return result

def get_source_path(fileset, current_file_path):
    """Determines the source .rdp file based on the fileset's address mode."""
    address_mode = fileset['address_mode']
    if address_mode in (0x40, 0x50, 0x60):
        rdp_map = {0x40: 'package.rdp', 0x50: 'data.rdp', 0x60: 'patch.rdp'}
        rdp_file = rdp_map.get(address_mode)
        rdp_path = os.path.join(os.path.dirname(current_file_path), rdp_file)
        if not os.path.exists(rdp_path):
            rdp_path = os.path.join(SCRIPT_DIR, rdp_file)
        if not os.path.exists(rdp_path): raise FileNotFoundError(f"RDP file '{rdp_file}' not found.")
        return rdp_path
    return current_file_path

def get_raw_file_chunk(fileset, current_file_path):
    """Reads the raw (potentially compressed) data chunk for a fileset from disk."""
    real_offset, size = fileset['real_offset'], fileset['size']
    if real_offset is None or size == 0: return b''
    source_file = get_source_path(fileset, current_file_path)
    with open(source_file, 'rb') as f:
        f.seek(real_offset)
        chunk_data = f.read(size)
        if len(chunk_data) != size: raise IOError("Could not read the complete file chunk.")
        return chunk_data

def get_decompressed_data(chunk_data):
    """Decompresses data if it has a known compression header, otherwise returns it as is."""
    if chunk_data.startswith(BLZ2_HEADER): return _decompress_blz2(chunk_data)
    if chunk_data.startswith(BLZ4_HEADER): return _decompress_blz4(chunk_data)
    return chunk_data


# --- QT WIDGETS AND DIALOGS ---

class HexEditor(QAbstractScrollArea):
    """A simple hex editor widget."""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFocusPolicy(Qt.StrongFocus)
        self.setFont(QFont("Courier", 10))
        self._data = QByteArray()
        self.bytes_per_line = 16
        self.selection_start, self.selection_end = -1, -1
        self.drag_selection_start = -1
        fm = self.fontMetrics()
        self.char_width, self.char_height = fm.horizontalAdvance('0'), fm.height()
        self.address_width = self.char_width * 9
        self.hex_width = self.char_width * (self.bytes_per_line * 3)
        self.ascii_width = self.char_width * self.bytes_per_line
        self.gap = self.char_width * 2
        self.viewport().setCursor(Qt.IBeamCursor)

    def setData(self, data):
        self._data = QByteArray(data)
        self.selection_start, self.selection_end = -1, -1
        self.drag_selection_start = -1
        self.verticalScrollBar().setRange(0, max(0, (len(self._data) - 1) // self.bytes_per_line))
        self.verticalScrollBar().setValue(0)
        self.viewport().update()

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
            painter.drawText(0, line_top_y, self.address_width - self.char_width, self.char_height, Qt.AlignRight, f"{address:08X}")
            for i in range(self.bytes_per_line):
                byte_pos = address + i
                if byte_pos >= len(self._data): break
                byte_val = ord(self._data[byte_pos])
                hex_x = self.address_width + i * 3 * self.char_width
                ascii_x = self.address_width + self.hex_width + self.gap + i * self.char_width
                if self.selection_start != -1 and self.selection_start <= byte_pos < self.selection_end:
                    painter.fillRect(hex_x, line_top_y, self.char_width * 2, self.char_height, QColor(0, 120, 215, 150))
                    painter.fillRect(ascii_x, line_top_y, self.char_width, self.char_height, QColor(0, 120, 215, 150))
                painter.drawText(hex_x, line_top_y + fm.ascent(), f"{byte_val:02X}")
                char = chr(byte_val) if 32 <= byte_val <= 126 else '.'
                painter.drawText(ascii_x, line_top_y + fm.ascent(), char)

    def keyPressEvent(self, event):
        if event.matches(QKeySequence.Copy) and self.selection_start != -1 and self.selection_end > self.selection_start:
            QApplication.clipboard().setText(self._data.mid(self.selection_start, self.selection_end - self.selection_start).toHex().data().decode())
        else: super().keyPressEvent(event)

    def mousePressEvent(self, event):
        byte_pos = self.pos_to_byte(event.pos())
        if byte_pos is not None:
            if event.modifiers() & Qt.ShiftModifier and self.selection_start != -1:
                current_anchor = self.drag_selection_start if self.drag_selection_start != -1 else self.selection_start
                if byte_pos < current_anchor:
                    self.selection_start = byte_pos
                    self.selection_end = current_anchor + 1
                else:
                    self.selection_start = current_anchor
                    self.selection_end = byte_pos + 1
            else:
                self.drag_selection_start = byte_pos
                self.selection_start = byte_pos
                self.selection_end = byte_pos + 1
        else:
            self.drag_selection_start = -1
            self.selection_start = -1
            self.selection_end = -1
        self.viewport().update()

    def mouseMoveEvent(self, event):
        if event.buttons() == Qt.LeftButton and self.drag_selection_start != -1:
            end_pos = self.pos_to_byte(event.pos())
            if end_pos is not None:
                start = self.drag_selection_start
                if end_pos >= start:
                    self.selection_start = start
                    self.selection_end = end_pos + 1
                else:
                    self.selection_start = end_pos
                    self.selection_end = start + 1
                self.viewport().update()

    def pos_to_byte(self, pos):
        line = self.verticalScrollBar().value() + (pos.y() // self.char_height)
        col = -1
        if self.address_width <= pos.x() < self.address_width + self.hex_width:
            col = (pos.x() - self.address_width) // (self.char_width * 3)
        elif self.address_width + self.hex_width + self.gap <= pos.x():
            col = (pos.x() - self.address_width - self.hex_width - self.gap) // self.char_width
        else:
            return None
        byte_pos = line * self.bytes_per_line + col
        return byte_pos if 0 <= byte_pos < len(self._data) else None

class HeaderSelectionDialog(QDialog):
    """A dialog to select header type and, if applicable, languages."""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Select Header Options")
        self.setMinimumWidth(350)
        
        main_layout = QVBoxLayout(self)
        
        # Header Type Selection
        header_layout = QHBoxLayout()
        header_layout.addWidget(QLabel("Header Format:"))
        self.header_combo = QComboBox()
        self.header_combo.addItems(["Original", "Localized"])
        self.header_combo.currentIndexChanged.connect(self.on_header_type_changed)
        header_layout.addWidget(self.header_combo)
        main_layout.addLayout(header_layout)
        
        # Language Selection
        self.lang_groupbox = QGroupBox("Select Languages to Load (if none selected, all will be loaded)")
        lang_layout = QVBoxLayout()
        self.lang_checkboxes = []
        for lang in COUNTRY_TYPES_6:
            checkbox = QCheckBox(lang)
            checkbox.setChecked(True)
            self.lang_checkboxes.append(checkbox)
            lang_layout.addWidget(checkbox)
        self.lang_groupbox.setLayout(lang_layout)
        main_layout.addWidget(self.lang_groupbox)
        
        # Dialog Buttons
        self.buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        self.buttons.accepted.connect(self.accept)
        self.buttons.rejected.connect(self.reject)
        main_layout.addWidget(self.buttons)
        
        # Initial state
        self.on_header_type_changed(0)

    def on_header_type_changed(self, index):
        """Show or hide the language selection box based on the header type."""
        is_localized = self.header_combo.currentText() == "Localized"
        self.lang_groupbox.setVisible(is_localized)

    def get_selected_header_type(self):
        """Returns the chosen header type string."""
        return self.header_combo.currentText()

    def get_selected_languages(self):
        """Returns a list of the checked language names."""
        if self.header_combo.currentText() != "Localized":
            return []
        return [cb.text() for cb in self.lang_checkboxes if cb.isChecked()]


# --- QT WORKER THREADS ---

class DataLoader(QThread):
    """Worker thread to decompress and load file data for the hex editor."""
    dataLoaded = pyqtSignal(object, QByteArray)
    errorOccurred = pyqtSignal(str)
    
    def __init__(self):
        super().__init__()
        self.item_key = None
        self.temp_file_path = None
        
    def run(self):
        try:
            if not self.temp_file_path or not os.path.exists(self.temp_file_path):
                raise FileNotFoundError("Pre-extracted file not found. Preloader may still be running.")
            with open(self.temp_file_path, 'rb') as f:
                chunk_data = f.read()
            decompressed_data = get_decompressed_data(chunk_data)
            self.dataLoaded.emit(self.item_key, QByteArray(decompressed_data))
        except Exception as e:
            traceback.print_exc()
            self.errorOccurred.emit(str(e))

class PreloaderThread(QThread):
    """Worker thread to pre-extract all raw file chunks to the temp directory."""
    progressUpdated = pyqtSignal(int, int, str)
    preloadingComplete = pyqtSignal()
    
    def __init__(self, files_to_preload, source_file_path, temp_session_dir, path_map):
        super().__init__()
        self.files_to_preload = files_to_preload
        self.source_file_path = source_file_path
        self.temp_session_dir = temp_session_dir
        self.path_map = path_map
        self.is_running = True

    def run(self):
        total_files = len(self.files_to_preload)
        for i, (fileset, key, index) in enumerate(self.files_to_preload):
            if not self.is_running: break
            
            filename = f"{fileset['name']}.{fileset['type']}" if fileset['type'] else fileset['name'] or f"Unnamed_File_{index}"
            self.progressUpdated.emit(i, total_files, filename)
            if fileset['skip_reason']: continue
            
            try:
                raw_chunk = get_raw_file_chunk(fileset, self.source_file_path)
                if not raw_chunk: continue
                
                country_key = key if key != 'single' else '_root'
                country_dir = os.path.join(self.temp_session_dir, country_key)
                dir_path = os.path.join(country_dir, *fileset['directories'])
                os.makedirs(dir_path, exist_ok=True)
                
                temp_path = os.path.join(dir_path, f"{index}_{filename}")
                with open(temp_path, 'wb') as f:
                    f.write(raw_chunk)
                self.path_map[(key, index)] = temp_path
            except Exception as e:
                print(f"--- Preloading Error for '{filename}' ---\n{traceback.format_exc()}\n------------------------")
        self.preloadingComplete.emit()

    def stop(self):
        self.is_running = False


# --- MAIN APPLICATION WINDOW ---

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("RES Explorer")
        self.setGeometry(100, 100, 1200, 800)
        
        # Application state
        self.file_history = []
        self.current_file_path = None
        self.parsed_data = {}
        self.temp_path_map = collections.OrderedDict()
        self.root_header_type = None
        self.selected_languages = []
        
        # Handlers and Threads
        self.temp_handler = TempHandler()
        self.preloader_thread = None
        self.data_loader = DataLoader()
        self.data_loader.dataLoaded.connect(self.on_data_loaded)
        self.data_loader.errorOccurred.connect(self.on_data_load_error)
        
        self.setup_ui()

    def setup_ui(self):
        """Initializes the main user interface components."""
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
        self.tree.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self.show_context_menu)
        splitter.addWidget(self.tree)
        
        self.hex_editor = HexEditor()
        splitter.addWidget(self.hex_editor)
        splitter.setSizes([400, 800])

    def open_file(self):
        """Opens a file dialog to select a RES or RTBL file."""
        path, _ = QFileDialog.getOpenFileName(self, "Open File", "", "RES Files (*.res);;RTBL Files (*.rtbl);;All Files (*)")
        if not path: return
        
        header_type = 'Original'
        selected_langs = []

        if path.lower().endswith('.res'):
            dialog = HeaderSelectionDialog(self)
            if not dialog.exec_() == QDialog.Accepted:
                return
            header_type = dialog.get_selected_header_type()
            selected_langs = dialog.get_selected_languages()
        
        self.root_header_type = header_type
        self.selected_languages = selected_langs
        self.stop_preloader()
        self.temp_handler.clear_all()
        self.file_history.clear()
        self.load_file(path, header_type=header_type)

    def load_file(self, path, is_nested=False, header_type='Original', nested_data=None, temp_level_name=None, is_going_back=False):
        """
        Loads and parses a file. Manages file history and temporary directories.
        """
        try:
            file_data = nested_data if nested_data is not None else open(path, 'rb').read()
            
            if not is_going_back:
                level_name = temp_level_name if temp_level_name else os.path.basename(path)
                self.temp_handler.push_level(level_name)
                # Store language selection in history
                self.file_history.append((path, header_type, self.selected_languages))

            self.current_file_path = path
            self.update_ui(file_data, header_type)
            self.back_btn.setEnabled(len(self.file_history) > 1)
            self.start_preloader()
        except Exception:
            QMessageBox.critical(self, "File Load Error", f"Error loading {path}:\n{traceback.format_exc()}")
            if not is_going_back:
                self.go_back()

    def update_ui(self, file_data, header_type):
        """Clears and repopulates the UI tree based on the parsed file data."""
        self.tree.clear()
        self.hex_editor.setData(b'')
        self.parsed_data = {'type': header_type, 'filesets': [], 'filesets_by_country': {}}
        
        file_name = os.path.basename(self.current_file_path)
        root_item = QTreeWidgetItem(self.tree, [file_name])
        root_item.setExpanded(True)
        
        try:
            if file_name.lower().endswith('.rtbl'):
                self._update_ui_rtbl(root_item, file_data)
            elif header_type == 'Original':
                self._update_ui_original(root_item, file_data)
            elif header_type == 'Localized':
                self._update_ui_localized(root_item, file_data)
        except Exception:
            QMessageBox.critical(self, "Parsing Error", f"Error parsing {file_name}:\n{traceback.format_exc()}")

    def _update_ui_original(self, root_item, file_data):
        """Populate UI for a standard RES file."""
        header = ResHeader(file_data)
        self.parsed_data['header'] = header
        header_item = QTreeWidgetItem(root_item, ["Header"])
        QTreeWidgetItem(header_item, ["Magic", f"{header.magic:#010x}"])
        QTreeWidgetItem(header_item, ["Group Offset", f"{header.group_offset:#010x}"])
        QTreeWidgetItem(header_item, ["Group Count", str(header.group_count)])
        QTreeWidgetItem(header_item, ["Configs Offset", f"{header.configs_offset:#010x}"])
        header_item.setExpanded(True)
        
        dataset = ResDataSet(file_data, header.group_count, header.group_offset)
        fileset_obj = ResFileSet(file_data, dataset.datasets)
        self.parsed_data['filesets'] = fileset_obj.filesets
        
        fileset_root = QTreeWidgetItem(root_item, ["Fileset"])
        self.populate_fileset_tree(fileset_root, fileset_obj.filesets, 'single')
        fileset_root.setExpanded(True)

    def _update_ui_localized(self, root_item, file_data):
        """Populate UI for a localized RES file."""
        header = LocalizedResHeader(file_data)
        self.parsed_data['header'] = header
        header_item = QTreeWidgetItem(root_item, ["Localized Header"])
        QTreeWidgetItem(header_item, ["Magic", f"{header.magic:#010x}"])
        QTreeWidgetItem(header_item, ["Config Length", f"{header.conf_length:#010x}"])
        QTreeWidgetItem(header_item, ["Country", str(header.country)])
        header_item.setExpanded(True)

        if header.country in [3, 6]:
            all_countries = COUNTRY_TYPES_3 if header.country == 3 else COUNTRY_TYPES_6
            
            # Use the language list stored in the instance. If it's empty, use all.
            selected_countries = self.selected_languages if self.selected_languages else all_countries

            countries_root = QTreeWidgetItem(root_item, ["Countries"])
            countries_root.setExpanded(True)
            country_struct_offset = 32
            
            for country_name in all_countries:
                cdata_offset, cdata_size = struct.unpack('<II', file_data[country_struct_offset:country_struct_offset+8])
                country_struct_offset += 8
                
                if country_name not in selected_countries:
                    country_item = QTreeWidgetItem(countries_root, [f"{country_name} (Skipped)"])
                    country_item.setForeground(0, Qt.gray)
                    continue

                country_item = QTreeWidgetItem(countries_root, [country_name])
                if cdata_offset == 0 and cdata_size == 0:
                    QTreeWidgetItem(country_item, ["Status", "Empty"])
                    country_item.setForeground(0, Qt.gray)
                    continue
                
                QTreeWidgetItem(country_item, ["DataSet Offset", f"{cdata_offset:#010x}"])
                QTreeWidgetItem(country_item, ["DataSet Size", str(cdata_size)])
                
                dataset = ResDataSet(file_data, 8, cdata_offset)
                fileset_start = cdata_offset + 64
                fileset_obj = ResFileSet(file_data, dataset.datasets, fileset_start)
                self.parsed_data['filesets_by_country'][country_name] = fileset_obj.filesets
                self.populate_fileset_tree(country_item, fileset_obj.filesets, country_name)
                country_item.setExpanded(True)

        elif header.country == 1:
            dataset = ResDataSet(file_data, 8, header.conf_length)
            fileset_start = header.conf_length + 64
            fileset_obj = ResFileSet(file_data, dataset.datasets, fileset_start)
            self.parsed_data['filesets'] = fileset_obj.filesets
            fileset_root = QTreeWidgetItem(root_item, ["Fileset (Direct)"])
            self.populate_fileset_tree(fileset_root, fileset_obj.filesets, 'single')
            fileset_root.setExpanded(True)
        else:
            QMessageBox.warning(self, "Unsupported Country Code", f"Unsupported country code: {header.country}")

    def _update_ui_rtbl(self, root_item, file_data):
        """Populate UI for an RTBL file."""
        filesets = parse_rtbl_data(file_data)
        self.parsed_data['filesets'] = filesets
        fileset_root = QTreeWidgetItem(root_item, ["Fileset"])
        self.populate_fileset_tree(fileset_root, filesets, 'single')
        fileset_root.setExpanded(True)

    def populate_fileset_tree(self, parent_item, filesets, key):
        """Populates a parent tree item with fileset details."""
        for i, fs in enumerate(filesets):
            filename = f"{fs['name']}.{fs['type']}" if fs['type'] else fs['name'] or f"Unnamed File {i}"
            display_path = os.path.join(*(fs['directories'] + [filename])) if fs['directories'] else filename
            file_item = QTreeWidgetItem(parent_item, [display_path])
            file_item.setData(0, Qt.UserRole, (key, i))
            
            is_compressed_str = "N/A"
            if fs['real_offset'] is not None and fs['size'] > 4:
                try:
                    chunk_header = get_raw_file_chunk({'real_offset': fs['real_offset'], 'size': 4, 'address_mode': fs['address_mode']}, self.current_file_path)
                    if chunk_header.startswith(BLZ2_HEADER): is_compressed_str = "Yes (BLZ2)"
                    elif chunk_header.startswith(BLZ4_HEADER): is_compressed_str = "Yes (BLZ4)"
                    else: is_compressed_str = "No"
                except (IOError, FileNotFoundError, TypeError, ValueError):
                    is_compressed_str = "Unknown"

            QTreeWidgetItem(file_item, ["Address Mode", f"{fs['address_mode']:#04x}"])
            QTreeWidgetItem(file_item, ["Real Offset", f"{fs['real_offset']:#010x}" if fs['real_offset'] is not None else "N/A"])
            QTreeWidgetItem(file_item, ["Size", str(fs['size'])])
            QTreeWidgetItem(file_item, ["Unpacked Size", str(fs['unpack_size'])])
            QTreeWidgetItem(file_item, ["Compressed", is_compressed_str])
            
            if fs['skip_reason']:
                QTreeWidgetItem(file_item, ["Status", f"Skipped ({fs['skip_reason']})"])
                file_item.setForeground(0, Qt.gray)

    def _get_fileset_from_item(self, item):
        """Retrieves the fileset data and its key from a tree widget item."""
        item_data = item.data(0, Qt.UserRole)
        if item_data is None: return None
        key, index = item_data
        try:
            if key == 'single': return self.parsed_data['filesets'][index], item_data
            else: return self.parsed_data['filesets_by_country'][key][index], item_data
        except (IndexError, KeyError): return None

    def on_tree_item_clicked(self, item, column):
        """Handles clicks on fileset items to load their data into the hex editor."""
        result = self._get_fileset_from_item(item)
        if result is None: return
        fileset, item_key = result
        
        if fileset['skip_reason']:
            self.hex_editor.setData(f"File skipped: {fileset['skip_reason']}".encode())
            return
        
        temp_path = self.temp_path_map.get(item_key)
        if temp_path and os.path.exists(temp_path):
            self.hex_editor.setData(b"Loading...")
            self.data_loader.item_key = item_key
            self.data_loader.temp_file_path = temp_path
            self.data_loader.start()
        else:
            self.hex_editor.setData(b"Awaiting preloader...")

    def on_tree_item_double_clicked(self, item, column):
        """Handles double-clicks to open nested RES/RTBL files."""
        result = self._get_fileset_from_item(item)
        if result is None: return
        fileset, (key, index) = result
        
        file_type = fileset.get('type', '').lower()
        if file_type in ('res', 'rtbl'):
            try:
                raw_chunk = get_raw_file_chunk(fileset, self.current_file_path)
                nested_data = get_decompressed_data(raw_chunk)
                if not nested_data:
                    QMessageBox.warning(self, "Empty File", f"Nested file '{fileset['name']}' is empty.")
                    return
                
                sanitized_name = "".join(c for c in fileset['name'] if c.isalnum() or c in (' ', '.', '_', '-')).rstrip()
                temp_filename = f"__nested_{sanitized_name}_{index}.{file_type}"
                temp_path = os.path.join(self.temp_handler.get_current_session_dir(), temp_filename)
                with open(temp_path, 'wb') as f: f.write(nested_data)
                
                header_type = 'RTBL' if file_type == 'rtbl' else self.root_header_type
                if not header_type:
                    QMessageBox.critical(self, "Error", "Could not determine header type for nested file.")
                    return

                temp_level_name, _ = os.path.splitext(temp_filename)
                self.load_file(temp_path, is_nested=True, header_type=header_type, nested_data=nested_data, temp_level_name=temp_level_name)
            except Exception:
                QMessageBox.critical(self, "Error", f"Could not open nested file:\n\n{traceback.format_exc()}")

    def on_data_loaded(self, item_key, data):
        """Callback for when the DataLoader finishes loading data."""
        current_item = self.tree.currentItem()
        if current_item and current_item.data(0, Qt.UserRole) == item_key:
            self.hex_editor.setData(data)
            
    def on_data_load_error(self, error_message):
        """Callback for when the DataLoader encounters an error."""
        self.hex_editor.setData(f"Error: {error_message}".encode())
        QMessageBox.warning(self, "Data Load Error", error_message)

    def go_back(self):
        """Navigates to the previously opened file."""
        if len(self.file_history) > 1:
            self.stop_preloader()
            self.temp_handler.pop_level_and_cleanup()
            self.file_history.pop()
            
            # Restore state from history, including language selection
            path, header_type, self.selected_languages = self.file_history[-1]
            
            try:
                self.load_file(path, is_nested=len(self.file_history) > 1, header_type=header_type, is_going_back=True)
            except Exception as e:
                QMessageBox.critical(self, "Navigation Error", f"Error going back to {path}:\n{e}")
        
        self.back_btn.setEnabled(len(self.file_history) > 1)

    def show_context_menu(self, pos):
        """Shows the right-click context menu on the tree view."""
        menu = QMenu()
        extract_action = menu.addAction("Extract Selected")
        extract_action.setEnabled(any(self._get_fileset_from_item(item) for item in self.tree.selectedItems()))
        
        action = menu.exec_(self.tree.mapToGlobal(pos))
        if action == extract_action:
            self.extract_selected()

    def extract_selected(self):
        """Extracts selected files to a user-chosen directory."""
        files_to_extract = [self._get_fileset_from_item(it) for it in self.tree.selectedItems() if self._get_fileset_from_item(it)]
        if not files_to_extract: return
        
        output_dir = QFileDialog.getExistingDirectory(self, "Select Output Directory")
        if not output_dir: return
        
        progress = QProgressDialog("Extracting files...", "Cancel", 0, len(files_to_extract), self)
        progress.setWindowModality(Qt.WindowModal)
        
        for i, (fileset, item_key) in enumerate(files_to_extract):
            progress.setValue(i)
            if progress.wasCanceled(): break
            
            filename = f"{fileset['name']}.{fileset['type']}" if fileset['type'] else fileset['name'] or f"Unnamed_File_{item_key[1]}"
            progress.setLabelText(f"Extracting: {filename}")
            
            try:
                temp_path = self.temp_path_map.get(item_key)
                raw_chunk = open(temp_path, 'rb').read() if temp_path and os.path.exists(temp_path) else get_raw_file_chunk(fileset, self.current_file_path)
                final_data = get_decompressed_data(raw_chunk)
                
                rel_path = os.path.join(*fileset['directories']) if fileset['directories'] else ''
                final_dir = os.path.join(output_dir, rel_path)
                os.makedirs(final_dir, exist_ok=True)
                
                with open(os.path.join(final_dir, filename), 'wb') as f:
                    f.write(final_data)
            except Exception as e:
                QMessageBox.warning(self, "Extraction Error", f"Could not extract '{filename}'.\nReason: {e}")
        
        progress.setValue(len(files_to_extract))

    def start_preloader(self):
        """Starts the background thread to pre-extract all file chunks."""
        self.stop_preloader()
        self.temp_path_map.clear()
        
        files_to_preload = []
        if self.parsed_data.get('filesets'):
            files_to_preload.extend([(fs, 'single', i) for i, fs in enumerate(self.parsed_data['filesets'])])
        if self.parsed_data.get('filesets_by_country'):
            for country, filesets in self.parsed_data['filesets_by_country'].items():
                files_to_preload.extend([(fs, country, i) for i, fs in enumerate(filesets)])
        
        if not files_to_preload: return

        self.progress = QProgressDialog("Pre-extracting file chunks...", "Cancel", 0, len(files_to_preload), self)
        self.progress.setWindowModality(Qt.WindowModal)
        self.progress.canceled.connect(self.stop_preloader)

        self.preloader_thread = PreloaderThread(files_to_preload, self.current_file_path, self.temp_handler.get_current_session_dir(), self.temp_path_map)
        self.preloader_thread.progressUpdated.connect(lambda i, total, name: (self.progress.setLabelText(f"({i+1}/{total}) Reading: {name}"), self.progress.setValue(i+1)))
        self.preloader_thread.preloadingComplete.connect(self.progress.close)
        self.preloader_thread.start()

    def stop_preloader(self):
        """Stops the preloader thread if it is running."""
        if self.preloader_thread and self.preloader_thread.isRunning():
            self.preloader_thread.stop()
            self.preloader_thread.wait()
            self.preloader_thread = None

    def closeEvent(self, event):
        """Handles the main window close event to clean up resources."""
        self.stop_preloader()
        self.temp_handler.clear_all()
        event.accept()

if __name__ == '__main__':
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())
