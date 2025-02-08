import struct
import sys
import os
import zlib
from io import BytesIO

def read_null_terminated_string(file, offset):
    """Reads a null-terminated string from the given offset."""
    file.seek(offset)
    result = bytearray()
    while True:
        byte = file.read(1)
        if byte == b'\x00' or not byte:
            break
        result.extend(byte)
    return result.decode('utf-8', errors='ignore')

def get_absolute_offset(offset):
    """Determines the absolute offset based on the legend."""
    legend = (offset & 0xF0000000) >> 28  # Extract the first nibble (legend)
    offset &= 0x0FFFFFFF  # Remove the legend to get the base offset

    if legend == 0xC:  # Current file
        return offset, None
    elif legend == 0xD:  # Non-obtainable (treat like 0xC)
        return offset, None
    elif legend == 0x4:  # package.rdp
        return offset * 0x800, "package.rdp"
    elif legend == 0x5:  # data.rdp
        return offset * 0x800, "data.rdp"
    elif legend == 0x6:  # patch.rdp
        return offset * 0x800, "patch.rdp"
    elif legend == 0x0:  # Unknown legend
        return None, None
    else:
        raise ValueError(f"Unknown legend: 0x{legend:X}")

def get_unique_filename(output_file):
    """Generates a unique filename by adding a suffix if the file already exists."""
    base_name, ext = os.path.splitext(output_file)
    counter = 0
    while os.path.exists(output_file):
        counter += 1
        output_file = f"{base_name}_{counter:04d}{ext}"
    return output_file

def blz_decompress(data, compressed_size, decompressed_size):
    """Decompresses BLZ2-compressed data."""
    data = BytesIO(data)
    magic = data.read(4)
    if magic != b"blz2":
        raise ValueError("Data is not in BLZ2 format.")

    decom = b""
    if decompressed_size >= 0xFFFF:
        # Handle large files (>= 64 KB)
        size = int.from_bytes(data.read(2), "little")
        ekor = zlib.decompress(data.read(size), wbits=-15)
        while data.tell() < compressed_size:
            size = int.from_bytes(data.read(2), "little")
            decom += zlib.decompress(data.read(size), wbits=-15)
        return decom + ekor
    else:
        # Handle small files (< 64 KB)
        size = int.from_bytes(data.read(2), "little")
        decom = zlib.decompress(data.read(size), wbits=-15)
        return decom

def extract_file(output_folder, path, name, name_type, offset, compressed_size, decompressed_size, external_file=None):
    """Extracts a file and saves it to the specified output folder."""
    # Create the output directory based on the path
    if path and not path.startswith('\x00\x00'):
        # Split the path into components
        path_components = path.split('/')
        
        # Check if the last component has a dot (.) and matches the name and name type
        if '.' in path_components[-1]:
            # Treat the last component as a file
            output_dir = os.path.join(output_folder, *path_components[:-1])
            output_file = os.path.join(output_dir, path_components[-1])
        else:
            # Treat the last component as a directory
            output_dir = os.path.join(output_folder, *path_components)
            output_file = os.path.join(output_dir, f"{name}.{name_type}" if name_type else name)
    else:
        # If no path, use the name and name type directly
        output_dir = output_folder
        output_file = os.path.join(output_dir, f"{name}.{name_type}" if name_type else name)

    # Create the output directory
    os.makedirs(output_dir, exist_ok=True)

    # Ensure the filename is unique
    output_file = get_unique_filename(output_file)

    # Read the data (if offset is valid)
    if offset is not None:
        if external_file:
            with open(external_file, 'rb') as ext_file:
                ext_file.seek(offset)
                data = ext_file.read(compressed_size)
        else:
            with open(filename, 'rb') as file:
                file.seek(offset)
                data = file.read(compressed_size)
        
        # Handle BLZ2 compression
        if data[:4] == b'blz2':
            try:
                data = blz_decompress(data, compressed_size, decompressed_size)
                print(f"      Note: Decompressed BLZ2 data")
            except Exception as e:
                print(f"      Error decompressing BLZ2 data: {e}")
    else:
        # Generate an empty file for unknown legends or zero compressed size
        data = b""
        print(f"      Note: Unknown file, generating as empty")

    # Save the data to the output file
    with open(output_file, 'wb') as out_file:
        out_file.write(data)

    print(f"Extracted: {output_file}")

def scan_rtbl_file(filename, output_folder):
    """Scans an .rtbl file for ToC entries and extracts files."""
    try:
        with open(filename, 'rb') as file:
            file_size = os.path.getsize(filename)
            current_offset = 0

            while current_offset < file_size:
                # Seek to the current offset
                file.seek(current_offset)

                # Read 32 bytes (potential ToC entry)
                toc_entry_data = file.read(32)

                if len(toc_entry_data) < 32:
                    break  # End of file

                # Unpack the ToC entry
                try:
                    toc_offset, compressed_size, name_offset, name_count, decompressed_size = struct.unpack('<IIII12xI', toc_entry_data)
                except struct.error:
                    current_offset += 1
                    continue  # Skip invalid entries

                # Check if the name offset is 0x20
                if name_offset == 0x20:
                    # Seek to the name structure (current offset + 0x20)
                    file.seek(current_offset + 0x20)

                    # Read the name structure (Name Count * 4 bytes)
                    name_structure_size = name_count * 4
                    name_structure_data = file.read(name_structure_size)

                    # Parse each Name Structure entry
                    name, name_type, path, path2 = None, None, None, None
                    for k in range(name_count):
                        # Extract 4 bytes for each Name Structure entry
                        name_entry_offset = struct.unpack('<I', name_structure_data[k * 4 : (k + 1) * 4])[0]
                        
                        # Read the null-terminated string at the offset
                        name_string = read_null_terminated_string(file, current_offset + name_entry_offset)
                        
                        # Assign values based on index
                        if k == 0:
                            name = name_string
                        elif k == 1:
                            name_type = name_string
                        elif k == 2:
                            path = name_string
                        elif k == 3:
                            path2 = name_string

                    # Print the ToC entry details
                    print(f"Found ToC Entry at 0x{current_offset:08X}:")
                    print(f"  Offset: {toc_offset} (0x{toc_offset:08X})")
                    print(f"  Compressed Size: {compressed_size}")
                    print(f"  Name Offset: {name_offset} (0x{name_offset:08X})")
                    print(f"  Name Count: {name_count}")
                    print(f"  Decompressed Size: {decompressed_size}")
                    print(f"  Name Structure:")
                    print(f"    Name: {name}")
                    print(f"    Name Type: {name_type}")
                    if path and not path.startswith('\x00\x00'):
                        print(f"    Path: {path}")
                    else:
                        print(f"    Path: NO PATH")
                    if path2 and not path2.startswith('\x00\x00'):
                        print(f"    Path2: {path2} (preferred over Path)")
                    else:
                        print(f"    Path2: NO PATH")

                    # Determine the actual path to use
                    actual_path = path2 if path2 and not path2.startswith('\x00\x00') else path

                    # Get the absolute offset and external file (if any)
                    try:
                        absolute_offset, external_file = get_absolute_offset(toc_offset)
                    except ValueError as e:
                        print(f"      Note: {e}, generating as empty")
                        absolute_offset, external_file = None, None

                    # Extract the file (even if compressed size is 0)
                    extract_file(output_folder, actual_path, name, name_type, absolute_offset, compressed_size, decompressed_size, external_file)

                # Move to the next potential ToC entry
                current_offset += 32

    except FileNotFoundError:
        print(f"Error: File '{filename}' not found.")
    except Exception as e:
        print(f"Error reading file: {e}")

def read_res_file(filename, output_folder):
    try:
        with open(filename, 'rb') as file:
            # Read the first 16 bytes (header section)
            header_data = file.read(16)
            
            # Unpack the header data
            header, group_offset, group_count, group_version, checksum, version = struct.unpack('<4sIBBhi', header_data)
            
            # Convert header to a readable string
            header_str = header.decode('ascii', errors='ignore').strip('\x00')
            
            # Print the parsed header data
            print(f"Header: {header_str}")
            print(f"Group Offset: {group_offset} (0x{group_offset:08X})")
            print(f"Group Count: {group_count}")
            print(f"Group Version: {group_version}")
            print(f"Checksum: {checksum}")
            print(f"Version: {version}")
            
            # Seek to the Group Offset
            file.seek(group_offset)
            
            # Read the EntryGroups table
            entry_groups_size = group_count * 8  # Each EntryGroup is 8 bytes
            entry_groups_data = file.read(entry_groups_size)
            
            # Parse each EntryGroup
            print("\nEntryGroups:")
            for i in range(group_count):
                # Extract 8 bytes for each EntryGroup
                entry_group_data = entry_groups_data[i * 8 : (i + 1) * 8]
                
                # Unpack the EntryGroup (4 bytes Offset, 4 bytes Value)
                offset, value = struct.unpack('<ii', entry_group_data)
                
                # Check if the EntryGroup is empty (Offset = 0 and Value = 0)
                if offset == 0 and value == 0:
                    print(f"EntryGroup {i + 1}: No Entry")
                else:
                    # Print the EntryGroup details
                    print(f"EntryGroup {i + 1}: Offset = {offset} (0x{offset:08X}), Value = {value}")
                    
                    # Seek to the ToC offset
                    file.seek(offset)
                    
                    # Read the ToC data (Value * 32 bytes)
                    toc_size = value * 32
                    toc_data = file.read(toc_size)
                    
                    # Parse each ToC entry
                    print(f"  ToC Data at 0x{offset:08X}:")
                    for j in range(value):
                        # Extract 32 bytes for each ToC entry
                        toc_entry_data = toc_data[j * 32 : (j + 1) * 32]
                        
                        # Unpack the ToC entry
                        toc_offset, compressed_size, name_offset, name_count, decompressed_size = struct.unpack('<IIII12xI', toc_entry_data)
                        
                        # Print the ToC entry details
                        print(f"    ToC Entry {j + 1}:")
                        print(f"      Offset: {toc_offset} (0x{toc_offset:08X})")
                        print(f"      Compressed Size: {compressed_size}")
                        print(f"      Name Offset: {name_offset} (0x{name_offset:08X})")
                        print(f"      Name Count: {name_count}")
                        print(f"      Decompressed Size: {decompressed_size}")
                        
                        # Parse the Name Structure
                        if name_offset != 0 and name_count > 0:
                            print("      Name Structure:")
                            file.seek(name_offset)
                            
                            # Read the Name Structure (Name Count * 4 bytes)
                            name_structure_size = name_count * 4
                            name_structure_data = file.read(name_structure_size)
                            
                            # Parse each Name Structure entry
                            name, name_type, path, path2 = None, None, None, None
                            for k in range(name_count):
                                # Extract 4 bytes for each Name Structure entry
                                name_entry_offset = struct.unpack('<I', name_structure_data[k * 4 : (k + 1) * 4])[0]
                                
                                # Read the null-terminated string at the offset
                                name_string = read_null_terminated_string(file, name_entry_offset)
                                
                                # Assign values based on index
                                if k == 0:
                                    name = name_string
                                elif k == 1:
                                    name_type = name_string
                                elif k == 2:
                                    path = name_string
                                elif k == 3:
                                    path2 = name_string

                            # Print the Name Structure entries
                            print(f"        Name: {name}")
                            print(f"        Name Type: {name_type}")
                            if path and not path.startswith('\x00\x00'):
                                print(f"        Path: {path}")
                            else:
                                print(f"        Path: NO PATH")
                            if path2 and not path2.startswith('\x00\x00'):
                                print(f"        Path2: {path2} (preferred over Path)")
                            else:
                                print(f"        Path2: NO PATH")

                            # Determine the actual path to use
                            actual_path = path2 if path2 and not path2.startswith('\x00\x00') else path

                            # Get the absolute offset and external file (if any)
                            try:
                                absolute_offset, external_file = get_absolute_offset(toc_offset)
                            except ValueError as e:
                                print(f"      Note: {e}, generating as empty")
                                absolute_offset, external_file = None, None

                            # Extract the file (even if compressed size is 0)
                            extract_file(output_folder, actual_path, name, name_type, absolute_offset, compressed_size, decompressed_size, external_file)

    except FileNotFoundError:
        print(f"Error: File '{filename}' not found.")
    except Exception as e:
        print(f"Error reading file: {e}")

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: script.py [filename] [output_folder]")
    else:
        filename = sys.argv[1]
        output_folder = sys.argv[2]
        
        # Check if the file is an .rtbl file
        if filename.endswith('.rtbl'):
            scan_rtbl_file(filename, output_folder)
        else:
            read_res_file(filename, output_folder)