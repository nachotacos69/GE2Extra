import struct
import os
import zlib
import io
import hashlib

# Constant for magic header verification
MAGIC_HEADER = 0x73657250
BLZ2_HEADER = b'blz2'
BLZ4_HEADER = 0x347a6c62

# Get the directory of the script. idk why i did this. but yeah... cool
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

class Header:
    # Here, this processes the .res file's header
    def __init__(self, file_data):
        # magic (4), 
        # group offset (4), 
        # group count (1), 
        # UNK1 (4), 
        # padding (3), 
        # configs (4), 
        # padding (12)
        header_struct = struct.unpack('<I I B I 3x I 12x', file_data[:32])
        self.magic = header_struct[0]
        self.group_offset = header_struct[1]
        self.group_count = header_struct[2]
        self.unk1 = header_struct[3]
        self.configs_offset = header_struct[4]
        
        # Verify magic header
        if self.magic != MAGIC_HEADER:
            raise ValueError(f"Invalid magic header: expected {hex(MAGIC_HEADER)}, got {hex(self.magic)}")

class DataSet:
    # Dataset is default 64 bytes
    def __init__(self, file_data, group_count, group_offset):
        self.datasets = []
        # Each dataset is 8 bytes per group: offset (4) + count (4)
        for i in range(group_count):
            offset = group_offset + (i * 8) # i = group count. thus 8 * 8 = 64
            dataset_struct = struct.unpack('<I I', file_data[offset:offset+8])
            dataset_offset = dataset_struct[0]
            dataset_count = dataset_struct[1]
            self.datasets.append({'offset': dataset_offset, 'count': dataset_count})

class FileSet:
    # The Stuff
    def __init__(self, file_data, datasets, input_file, output_dir, base_output_dir):
        self.filesets = []
        self.input_file = input_file
        self.output_dir = output_dir
        self.base_output_dir = base_output_dir  # Main .res file's output directory
        self.rdp_files = {
            0x40: 'package.rdp',
            0x50: 'data.rdp',
            0x60: 'patch.rdp'
        }
        self.nested_res_files = []  # Store paths of extracted .res files
        # Start reading filesets at 0x60
        fileset_start = 0x60
        total_fileset_count = sum(dataset['count'] for dataset in datasets) # gets all dataset counts
        fileset_end = fileset_start + (total_fileset_count * 32) # 0x60 + total dataset counts multiply by 32 = fileset_end range

        # Read each fileset (32 bytes)
        for i in range(total_fileset_count):
            offset = fileset_start + (i * 32) # total dataset count multiply by 32
            fileset_struct = struct.unpack('<I I I I 12x I', file_data[offset:offset+32]) #reads each fileset 32 bytes per entry
            raw_offset = fileset_struct[0] # offset that isn't touched on multiplication or remove it's address_mode
            size = fileset_struct[1] # refers to the fileset's size range used on that specific given pointer location. 
            offset_name = fileset_struct[2] # location of names but still has to go through name pointers
            chunk_name = fileset_struct[3] # defines how many pointers on that offset_name
            unpack_size = fileset_struct[4] # refers to the compressed file's true size

            # Handle offset based on address mode
            address_mode = (raw_offset & 0xFF000000) >> 24 # removes the address_mode
            real_offset = None
            skip_reason = None

            if address_mode == 0x00:
                skip_reason = "Unknown address mode (0x00)"
            elif address_mode == 0x30:
                # skipped, already existing file within those `data_` prefix folders (FOR PSVITA ONLY).
                skip_reason = "DataSet file (0x30)"
            elif address_mode in (0xC0, 0xD0):
                # this address_mode is based on the file's current location.
                real_offset = raw_offset & 0x00FFFFFF 
            elif address_mode in (0x40, 0x50, 0x60):
                # this address_mode is based on the file's current location within a specific RDP file.
                temp_offset = raw_offset & 0x00FFFFFF 
                real_offset = temp_offset * 0x800 # could have used Left Shift 11, but this will do

            # Check for dummy fileset
            if raw_offset == 0 and size == 0 and offset_name == 0 and chunk_name == 0 and unpack_size != 0:
                skip_reason = "Dummy fileset"

            # Read name and directory
            name_info = self._read_name_info(file_data, offset_name, chunk_name)
            fileset_data = {
                'raw_offset': raw_offset,
                'real_offset': real_offset, # result after trimmed/multiplied
                'size': size,
                'offset_name': offset_name,
                'chunk_name': chunk_name,
                'unpack_size': unpack_size,
                'address_mode': address_mode,
                'name': name_info['name'],
                'type': name_info['type'],
                'directories': name_info['directories']
            }

            self.filesets.append((fileset_data, skip_reason))

    def _read_name_info(self, file_data, offset_name, chunk_name):
        # Read name, type, and directories from offset_name."""
        name_info = {'name': '', 'type': '', 'directories': []}
        if offset_name == 0 or chunk_name == 0:
            return name_info

        pointer_count = chunk_name
        pointers = []
        for i in range(pointer_count):
            pointer_offset = offset_name + (i * 4) # chunk_name value multiply by 4
            pointer = struct.unpack('<I', file_data[pointer_offset:pointer_offset+4])[0]
            pointers.append(pointer)

        for i, pointer in enumerate(pointers): # here, it finds the strings based on the separated pointers found within offset_name
            if pointer == 0:
                continue
            string = ''
            pos = pointer
            while pos < len(file_data): 
                char = file_data[pos]
                if char == 0:
                    break
                string += chr(char)
                pos += 1
            if i == 0:
                name_info['name'] = string
            elif i == 1:
                name_info['type'] = string
            else:
                name_info['directories'].append(string)

        return name_info

    def _get_unique_filepath(self, base_path, filename, is_decompressed=False):
        base, ext = os.path.splitext(filename)
        if is_decompressed:
            base = f"{base}"
        filepath = os.path.join(base_path, f"{base}{ext}")
        if not os.path.exists(filepath):
            return filepath

        counter = 1
        while True:
            new_filename = f"{base}_{counter:04d}{ext}" # handles duplicates
            new_filepath = os.path.join(base_path, new_filename)
            if not os.path.exists(new_filepath):
                return new_filepath
            counter += 1

    def _decompress_blz2(self, chunk_data):
        #BLZ2 Decompression Procedures
        decompressed_blocks = []
        block_count = 0

        with io.BytesIO(chunk_data) as f:
            # Check header
            header = f.read(4)
            if header != BLZ2_HEADER:
                raise ValueError(f"Invalid BLZ2 header: expected {BLZ2_HEADER}, got {header}")

            # Process blocks
            while True:
                size_bytes = f.read(2)
                if not size_bytes:
                    break
                if len(size_bytes) != 2:
                    raise ValueError(f"Failed to read compressed block size at block {block_count + 1}")
                compressed_size = struct.unpack('<H', size_bytes)[0]

                compressed_data = f.read(compressed_size)
                if len(compressed_data) != compressed_size:
                    raise ValueError(f"Incomplete compressed block {block_count + 1}: expected {compressed_size} bytes, got {len(compressed_data)}")

                decompressor = zlib.decompressobj(wbits=-15)
                decompressed_data = decompressor.decompress(compressed_data)
                decompressed_blocks.append(decompressed_data)

                if decompressor.unused_data:
                    print(f"Warning: Unused data after decompression in block {block_count + 1}")
                if not decompressor.eof:
                    print(f"Warning: Decompression may be incomplete in block {block_count + 1}")

                block_count += 1

            if block_count == 0:
                raise ValueError("No compressed blocks found in BLZ2 data")

        # Reorder blocks: move first block to the end if multiple
        if block_count > 1:
            decompressed_blocks = decompressed_blocks[1:] + decompressed_blocks[:1]

        return b''.join(decompressed_blocks)

    def _decompress_blz4(self, chunk_data):
        # BLZ4 Decompression Procedures
        if len(chunk_data) <= 32 + 2:
            raise ValueError(f"Input data length {len(chunk_data)} is too short for BLZ4 format")

        block_data = []
        with io.BytesIO(chunk_data) as f:
            # Read magic header
            magic = struct.unpack('<I', f.read(4))[0]
            if magic != BLZ4_HEADER:
                raise ValueError(f"Invalid BLZ4 magic number: {hex(magic)} != {hex(BLZ4_HEADER)}")

            # Read metadata
            unpack_size = struct.unpack('<I', f.read(4))[0]
            padding = struct.unpack('<Q', f.read(8))[0]
            md5 = f.read(16)

            # Read data blocks
            while f.tell() < len(chunk_data):
                chunk_size_bytes = f.read(2)
                if len(chunk_size_bytes) < 2:
                    raise ValueError("Incomplete chunk size data")
                chunk_size = struct.unpack('<H', chunk_size_bytes)[0]
                if chunk_size == 0:
                    block_data.append(f.read(len(chunk_data) - f.tell()))
                    break
                else:
                    block = f.read(chunk_size)
                    if len(block) < chunk_size:
                        raise ValueError(f"Expected {chunk_size} bytes for block, got {len(block)}")
                    block_data.append(block)

            if not block_data:
                raise ValueError("No data blocks found in BLZ4 data")

        # Reorder blocks: last block first
        real_list = block_data[1:] + [block_data[0]]

        # Decompress blocks
        decompressed_blocks = []
        for block in real_list:
            try:
                decompressed = zlib.decompress(block)
                decompressed_blocks.append(decompressed)
            except zlib.error as e:
                raise ValueError(f"Failed to decompress block: {e}")

        result = b''.join(decompressed_blocks)

        # Verify MD5
        computed_md5 = hashlib.md5(result).digest()
        if computed_md5 != md5:
            print("Warning: BLZ4 MD5 checksum mismatch. Output may be corrupted.")

        return result

    def extract_files(self):
        # Extraction Procedures
        os.makedirs(self.output_dir, exist_ok=True)

        for fileset, skip_reason in self.filesets:
            address_mode = fileset['address_mode'] # checks the source
            real_offset = fileset['real_offset'] # uses the real offset. the result of the original offset being trimmed, and processed
            size = fileset['size'] # get's the size value, and uses it as the main part of collecting data
            name = fileset['name']
            file_type = fileset['type']
            directories = fileset['directories'] # name, type, directories (if any) are collected)
            offset_name = fileset['offset_name'] # checks offset_name
            chunk_name = fileset['chunk_name'] # checks chunk_name

            # Construct output.
            # this mixes name+type (name and format) and directories.. if there's any
            relative_path = os.path.join(*directories) if directories else ''
            filename = f"{name}.{file_type}" if file_type else name
            is_decompressed = False

            # Construct display path relative
            display_path = os.path.normpath(os.path.join(self.output_dir, relative_path, filename))
            display_path = os.path.relpath(display_path, start=os.path.dirname(self.base_output_dir))
            display_path = f".\\{display_path}"

            # Handle skip cases. basic lines of code.
            if skip_reason:
                skip_name = 'dummy' if skip_reason == "Dummy fileset" else (name or 'dummy')
                skip_display_path = os.path.normpath(os.path.join(self.output_dir, relative_path, skip_name))
                skip_display_path = os.path.relpath(skip_display_path, start=os.path.dirname(self.base_output_dir))
                skip_display_path = f".\\{skip_display_path}"
                print(f"Skipping: {skip_display_path}")
                continue

            # Create directories only for files that will be extracted
            os.makedirs(os.path.join(self.output_dir, relative_path), exist_ok=True)

            # Handle empty files
            if (offset_name != 0 and chunk_name != 0 and (real_offset is None or size == 0)):
                try:
                    output_path = self._get_unique_filepath(os.path.join(self.output_dir, relative_path), filename)
                    with open(output_path, 'wb') as f:
                        pass
                    output_display_path = os.path.relpath(output_path, start=os.path.dirname(self.base_output_dir))
                    print(f"Extracting: .\\{output_display_path}")
                    if file_type == 'res':
                        self.nested_res_files.append(output_path)
                except Exception as e:
                    print(f"Skipping: {display_path} (Extraction error: {str(e)})")
                continue

            # Skip if no valid offset
            if real_offset is None:
                print(f"Skipping: {display_path} (Invalid offset)")
                continue

            # Determine and verify source
            source_file = self.input_file
            if address_mode in (0x40, 0x50, 0x60):
                rdp_file = self.rdp_files.get(address_mode)
                rdp_path = os.path.join(SCRIPT_DIR, rdp_file)
                if not os.path.exists(rdp_path):
                    print(f"Skipping: {display_path} (RDP file {rdp_file} not found)")
                    continue
                source_file = rdp_path

            # Extract and process chunk
            try:
                with open(source_file, 'rb') as f:
                    f.seek(real_offset)
                    chunk_data = f.read(size)
                    if size > 0 and len(chunk_data) != size:
                        print(f"Skipping: {display_path} (Chunk size mismatch)")
                        continue

                    # Check for BLZ2/BLZ4 compression
                    final_data = chunk_data
                    if size >= 4:
                        header = chunk_data[:4]
                        if header == BLZ2_HEADER:
                            try:
                                final_data = self._decompress_blz2(chunk_data)
                                is_decompressed = True
                            except Exception as e:
                                print(f"Skipping: {display_path} (BLZ2 decompression error: {str(e)})")
                                continue
                        elif struct.unpack('<I', header)[0] == BLZ4_HEADER:
                            try:
                                final_data = self._decompress_blz4(chunk_data)
                                is_decompressed = True
                            except Exception as e:
                                print(f"Skipping: {display_path} (BLZ4 decompression error: {str(e)})")
                                continue

                    # Write data
                    output_path = self._get_unique_filepath(os.path.join(self.output_dir, relative_path), filename, is_decompressed)
                    with open(output_path, 'wb') as f:
                        f.write(final_data)
                    output_display_path = os.path.relpath(output_path, start=os.path.dirname(self.base_output_dir))
                    print(f"Extracting: .\\{output_display_path}")


                    if file_type == 'res' or os.path.splitext(output_path)[1].lower() == '.res':
                        self.nested_res_files.append(output_path)

            except Exception as e:
                print(f"Skipping: {display_path} (Extraction error: {str(e)})")

        return self.nested_res_files

def parse_res_file(file_path, base_output_dir=None):

    if not os.path.exists(file_path):
        raise FileNotFoundError(f"File {file_path} not found")

    output_dir = os.path.splitext(file_path)[0]
    # Use base_output_dir from main .res file, or set it for the first call
    if base_output_dir is None:
        base_output_dir = output_dir
    
    with open(file_path, 'rb') as f:
        file_data = f.read()

    try:
        # Parse header
        header = Header(file_data)

        # Parse datasets
        dataset = DataSet(file_data, header.group_count, header.group_offset)

        # Parse and extract filesets
        fileset = FileSet(file_data, dataset.datasets, file_path, output_dir, base_output_dir)
        nested_res_files = fileset.extract_files()

        # Process nested .res files
        for nested_res in nested_res_files:
            parse_res_file(nested_res, base_output_dir)

    except Exception as e:
        print(f"Error processing {file_path}: {str(e)}")

if __name__ == '__main__':
    # Replace 'system.res' with whatever .res file you want to process
    try:
        parse_res_file('system.res')
    except Exception as e:
        print(f"Error: {e}")