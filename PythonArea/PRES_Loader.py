import struct
import os
import zlib
import RTBL2  # Import the RTBL2.py module

def read_string(f, offset):
    """Read a UTF-8 string from the given offset until null byte, if valid."""
    f.seek(offset)
    if f.read(2) == b'\x00\x00':
        return None
    f.seek(offset)
    bytes_read = b''
    while True:
        byte = f.read(1)
        if byte == b'\x00' or not byte:
            break
        bytes_read += byte
    return bytes_read.decode('utf-8', errors='ignore') or None

def process_offset(f, offset, csize):
    """Process TOC.Offset and calculate effective offset and end offset."""
    enumerator = (offset >> 28) & 0xF
    base_offset = offset & 0x0FFFFFFF
    
    if enumerator == 0x0:
        return 0x00000000, 0, "Outsider (External File)", False
    elif enumerator == 0x3:
        return 0x30000000, 0, "NoSet (External Exclusion)", False
    elif enumerator in {0x4, 0x5, 0x6}:
        effective_offset = base_offset * 0x800
        offset_type = {0x4: "PACKAGE", 0x5: "DATA", 0x6: "PATCH"}[enumerator]
    elif enumerator in {0xC, 0xD}:
        effective_offset = base_offset
        offset_type = "Current (Inside RES)"
    else:
        effective_offset = offset
        offset_type = "Unknown"
    
    end_offset = effective_offset + csize if effective_offset != 0 else 0
    is_compressed = False
    if effective_offset != 0 and f:
        try:
            f.seek(effective_offset)
            magic = f.read(4)
            is_compressed = magic == b'blz2'
        except:
            pass
    return effective_offset, end_offset, offset_type, is_compressed

def generate_filename(name, type_str, path, subpath, extrapath, output_dir, used_names, chunkname):
    """Generate file path and handle duplicates, respecting CHUNKNAME rules."""
    base_filename = name
    if type_str:
        base_filename += f".{type_str}"
    
    base_path = output_dir
    active_path = subpath if chunkname == 4 and subpath else path
    
    if active_path:
        path_parts = active_path.split('/')
        last_part = path_parts[-1]
        if last_part in {name, base_filename}:
            base_path = os.path.join(base_path, *path_parts[:-1])
            filename = last_part
        else:
            base_path = os.path.join(base_path, *path_parts)
            filename = base_filename
    else:
        filename = base_filename
    
    if extrapath and not (active_path and active_path.endswith(f"/{name}") or active_path and active_path.endswith(f"/{base_filename}")):
        base_path = os.path.join(base_path, extrapath)
    
    os.makedirs(base_path, exist_ok=True)
    
    final_path = os.path.join(base_path, filename)
    counter = 0
    base_final_path = final_path
    while final_path in used_names or os.path.exists(final_path):
        counter += 1
        final_path = f"{base_final_path.rsplit('.', 1)[0]}_{counter:04d}.{type_str}" if type_str else f"{base_final_path}_{counter:04d}"
    used_names.add(final_path)
    
    return final_path

def decompress_blz2(data):
    """Decompress BLZ2 data (single or multi-block)."""
    if not data.startswith(b'blz2'):
        return data
    
    decompressed_blocks = []
    pos = 4
    
    while pos < len(data):
        if pos + 2 > len(data):
            raise ValueError("Incomplete BLZ2 block size")
        comp_size = struct.unpack('<H', data[pos:pos + 2])[0]
        pos += 2
        
        if pos + comp_size > len(data):
            raise ValueError("BLZ2 block exceeds data length")
        comp_block = data[pos:pos + comp_size]
        pos += comp_size
        
        decomp_block = zlib.decompress(comp_block, wbits=-15)
        decompressed_blocks.append(decomp_block)
    
    if len(decompressed_blocks) == 1:
        return decompressed_blocks[0]
    elif len(decompressed_blocks) > 1:
        reordered = decompressed_blocks[1:] + [decompressed_blocks[0]]
        return b''.join(reordered)
    return b''

def extract_data(f, start_offset, end_offset, output_path, is_compressed):
    """Extract and optionally decompress data."""
    f.seek(start_offset)
    data_size = end_offset - start_offset
    data = f.read(data_size)
    
    if is_compressed:
        try:
            decompressed_data = decompress_blz2(data)
            with open(output_path, 'wb') as out_f:
                out_f.write(decompressed_data)
            return True
        except Exception as e:
            print(f"      Decompression failed: {str(e)}")
            with open(output_path, 'wb') as out_f:
                out_f.write(data)
            return False
    else:
        with open(output_path, 'wb') as out_f:
            out_f.write(data)
        return True

def read_res_file(file_path, processed_files=None):
    if processed_files is None:
        processed_files = set()  # Track processed .res files to avoid infinite loops
    
    if file_path in processed_files:
        print(f"Skipping already processed file: {file_path}")
        return []
    
    processed_files.add(file_path)
    output_dir = os.path.splitext(file_path)[0]
    used_names = set()
    nested_res_files = []  # Collect nested .res files
    
    try:
        with open(file_path, 'rb') as f:
            header = struct.unpack('<I', f.read(4))[0]
            if header != 0x73657250:
                print(f"Invalid .RES file: Header {hex(header)}.")
                return nested_res_files
            
            group_offset = struct.unpack('<I', f.read(4))[0]
            group_count = struct.unpack('<B', f.read(1))[0]
            group_version = struct.unpack('<B', f.read(1))[0]
            checksum = struct.unpack('<H', f.read(2))[0]
            version = struct.unpack('<I', f.read(4))[0]
            chunk_datas_offset = struct.unpack('<I', f.read(4))[0]
            sideload_res_offset = struct.unpack('<I', f.read(4))[0]
            sideload_res_size = struct.unpack('<I', f.read(4))[0]
            """ Debug 1
            print(f"File: {file_path}")
            print(f"Header: {hex(header)} ('Pers' in ASCII)")
            print(f"Group Offset: {hex(group_offset)} ({group_offset})")
            print(f"Group Count: {group_count}")
            print(f"Group Version: {group_version}")
            print(f"Checksum: {hex(checksum)}")
            print(f"Version: {version}")
            print(f"Chunk Datas Offset: {hex(chunk_datas_offset)} ({chunk_datas_offset})")
            print(f"Sideload RES Offset: {hex(sideload_res_offset)} ({sideload_res_offset})")
            print(f"Sideload RES Size: {sideload_res_size} bytes")
            """            
            f.seek(group_offset)
            group_raw_data = f.read(group_count * 8)
            
            if len(group_raw_data) != group_count * 8:
                print(f"Warning: Expected {group_count * 8} bytes for groups, got {len(group_raw_data)}")
                return nested_res_files
            
            #debug print("\nGroup Data:")
            for i in range(group_count):
                group_chunk = group_raw_data[i * 8:(i + 1) * 8]
                if group_chunk == b'\x00' * 8:
                    print(f"Group {i + 1}: [empty]")
                else:
                    entry_offset, entry_count = struct.unpack('<II', group_chunk)
                    """ Debug 2
                    print(f"Group {i + 1}:")
                    print(f"  Entry Offset: {hex(entry_offset)} ({entry_offset})")
                    print(f"  Entry Count: {entry_count}")
                    """
                    f.seek(entry_offset)
                    toc_raw_data = f.read(entry_count * 32)
                    
                    if len(toc_raw_data) != entry_count * 32:
                        print(f"  Warning: Expected {entry_count * 32} bytes for TOC, got {len(toc_raw_data)}")
                        continue
                    
                    #debug print("  TOC Entries:")
                    for j in range(entry_count):
                        toc_chunk = toc_raw_data[j * 32:(j + 1) * 32]
                        if toc_chunk[:16] == b'\x00' * 16:
                            #print(f"    TOC {j + 1}: [empty]")
                            continue
                        
                        toc_offset, toc_csize, toc_nameoffset, toc_chunkname, _, toc_dsize = struct.unpack('<IIII12sI', toc_chunk)
                        effective_offset, end_offset, offset_type, is_compressed = process_offset(f, toc_offset, toc_csize)
                        """ Debug 3
                        print(f"    TOC {j + 1}:")
                        print(f"      Raw Offset: {hex(toc_offset)} ({toc_offset})")
                        print(f"      Start Offset: {hex(effective_offset)} ({effective_offset})")
                        print(f"      CSIZE: {toc_csize} ({hex(toc_csize)})")
                        print(f"      End Offset: {hex(end_offset)} ({end_offset})")
                        print(f"      Offset Type: {offset_type}")
                        
                        if is_compressed:
                            print(f"Compression: blz2 detected")
                        
                        print(f"      NAMEOFFSET: {hex(toc_nameoffset)} ({toc_nameoffset})")
                        print(f"      CHUNKNAME: {toc_chunkname}")
                        print(f"      DSIZE: {toc_dsize}")
                        """
                        name, type_str, path, subpath, extrapath = None, None, None, None, None
                        if toc_nameoffset != 0 and toc_chunkname > 0:
                            f.seek(toc_nameoffset)
                            name_chunk = f.read(min(toc_chunkname, 5) * 4)
                            fields = struct.unpack(f'<{"I" * min(toc_chunkname, 5)}', name_chunk)
                            str_name = fields[0] if toc_chunkname >= 1 else 0
                            str_type = fields[1] if toc_chunkname >= 3 else 0
                            str_path = fields[2] if toc_chunkname >= 3 else 0
                            str_subpath = fields[3] if toc_chunkname >= 4 else 0
                            str_extrapath = fields[4] if toc_chunkname >= 5 else 0
                            
                            name = read_string(f, str_name) if str_name else None
                            type_str = read_string(f, str_type) if str_type and toc_chunkname >= 3 else None
                            path = read_string(f, str_path) if str_path and toc_chunkname >= 3 else None
                            subpath = read_string(f, str_subpath) if str_subpath and toc_chunkname >= 4 else None
                            extrapath = read_string(f, str_extrapath) if str_extrapath and toc_chunkname >= 5 else None
                            
                            final_path = subpath if toc_chunkname == 4 and subpath else path
                            """ Debug 4
                            if any([name, type_str, final_path, subpath, extrapath]):
                                #print("      Name Data:")
                                if name:
                                    print(f"        Name: {name}")
                                if type_str and toc_chunkname >= 3:
                                    print(f"        Type: {type_str}")
                                if final_path and toc_chunkname >= 3:
                                    print(f"        Path: {final_path}")
                                if subpath and toc_chunkname >= 4 and final_path != subpath:
                                    print(f"        Subpath: {subpath}")
                                if extrapath and toc_chunkname >= 5:
                                    print(f"        Extrapath: {extrapath}")
                            """
                        if offset_type in ["Current (Inside RES)", "PACKAGE", "DATA", "PATCH"] and name:
                            rdp_files = {"PACKAGE": "package.rdp", "DATA": "data.rdp", "PATCH": "patch.rdp"}
                            src_file = f if offset_type == "Current (Inside RES)" else None
                            if offset_type in rdp_files:
                                rdp_path = rdp_files[offset_type]
                                if not os.path.exists(rdp_path):
                                    print(f"      Extraction skipped: {rdp_path} not found")
                                    continue
                                src_file = open(rdp_path, 'rb')
                            
                            output_path = generate_filename(name, type_str, path, subpath, extrapath, output_dir, used_names, toc_chunkname)
                            if toc_csize > 0 and effective_offset != 0:
                                success = extract_data(src_file, effective_offset, end_offset, output_path, is_compressed)
                                if success and is_compressed:
                                    print(f"Extracted and decompressed to: {output_path}")
                                elif success:
                                    print(f"Extracted to: {output_path}")
                                else:
                                    print(f"Extracted (compressed, decompression failed): {output_path}")
                                # Check for .rtbl or .res files
                                if output_path.lower().endswith('.rtbl'):
                                    print(f"[RTBL] Detected .rtbl file: {output_path}, processing...")
                                    RTBL2.read_rtbl_file(output_path)
                                elif output_path.lower().endswith('.res'):
                                    # print(f"[RES] Detected nested .res file: {output_path}")
                                    nested_res_files.append(output_path)
                            else:
                                with open(output_path, 'wb') as out_f:
                                    pass
                                print(f"Created empty file: {output_path}")
                            
                            if src_file != f:
                                src_file.close()
                        elif offset_type in ["NoSet (External Exclusion)", "Outsider (External File)"]:
                            print(f"Extraction skipped: {offset_type}")
                        else:
                            print(f"Extraction skipped: Invalid offset type")
            
            # Process nested .res files after initial extraction
            if nested_res_files:
                print("\nProcessing nested .res files:")
                nested_res_files.sort()  # Sort alphabetically
                for nested_res in nested_res_files:
                    print(f"\nProcessing nested file: {nested_res}")
                    more_nested = read_res_file(nested_res, processed_files)
                    nested_res_files.extend(more_nested)  # Add any deeper nested .res files
                
            return nested_res_files

    except FileNotFoundError:
        print(f"Error: File '{file_path}' not found.")
        return nested_res_files
    except Exception as e:
        print(f"Error reading file: {str(e)}")
        return nested_res_files

if __name__ == "__main__":
    read_res_file("system.res")