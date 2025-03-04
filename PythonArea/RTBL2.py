import struct
import os
import zlib
import PRES_Loader  # Import the main script to process .res files

def read_string_direct(data, start_pos):
    """Read a UTF-8 string from a byte array until null byte."""
    bytes_read = b''
    pos = start_pos
    while pos < len(data):
        byte = data[pos:pos + 1]
        if byte == b'\x00':
            break
        bytes_read += byte
        pos += 1
    return bytes_read.decode('utf-8', errors='ignore') or None, pos + 1

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

def generate_filename(name, type_str, output_dir, used_names):
    """Generate file path for .rtbl files (no paths)."""
    base_filename = name
    if type_str:
        base_filename += f".{type_str}"
    
    base_path = output_dir
    os.makedirs(base_path, exist_ok=True)
    
    final_path = os.path.join(base_path, base_filename)
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

def read_rtbl_file(file_path):
    output_dir = os.path.splitext(file_path)[0]
    used_names = set()
    nested_res_files = []  # To collect nested .res files
    
    try:
        with open(file_path, 'rb') as f:
            file_size = os.path.getsize(file_path)
            offset = 0
            toc_count = 0
            
            #DEBUG print(f"File: {file_path}")
            #DEBUG print("TOC Entries:")
            
            while offset < file_size:
                f.seek(offset)
                check_bytes = f.read(16)
                
                if check_bytes == b'\x00' * 16:
                    offset += 16
                    continue
                
                if offset + 32 > file_size:
                    print(f"  Warning: Incomplete TOC at {hex(offset)}")
                    break
                
                toc_data = check_bytes + f.read(16)
                toc_offset, toc_csize, toc_nameoffset, toc_chunkname = struct.unpack('<IIII', toc_data[:16])
                
                if toc_nameoffset != 0x20:
                    offset += 16
                    continue
                
                toc_count += 1
                effective_offset, end_offset, offset_type, is_compressed = process_offset(f, toc_offset, toc_csize)
                """ DEBUG
                print(f"  TOC {toc_count} @ {hex(offset)}:")
                print(f"    Raw Offset: {hex(toc_offset)} ({toc_offset})")
                print(f"    Start Offset: {hex(effective_offset)} ({effective_offset})")
                print(f"    CSIZE: {toc_csize} ({hex(toc_csize)})")
                print(f"    End Offset: {hex(end_offset)} ({end_offset})")
                print(f"    Offset Type: {offset_type}")
                
                if is_compressed:
                    print(f"    Compression: blz2 detected")
                """
                name_offset = offset + 0x20
                f.seek(name_offset)
                name_data = f.read(64)
                
                name_start = name_data[12:]
                name, next_pos = read_string_direct(name_start, 0)
                type_str = None
                if name and next_pos < len(name_start):
                    type_str, _ = read_string_direct(name_start, next_pos)
                
                # if name:
                    ###DEBUG print(f"    Name Data:")
                    ###DEBUG print(f"      Name: {name}")
                #    if type_str:
                        ###DEBUG print(f"      Type: {type_str}")
                    
                    if offset_type in ["Current (Inside RES)", "PACKAGE", "DATA", "PATCH"]:
                        rdp_files = {"PACKAGE": "package.rdp", "DATA": "data.rdp", "PATCH": "patch.rdp"}
                        src_file = f if offset_type == "Current (Inside RES)" else None
                        if offset_type in rdp_files:
                            rdp_path = rdp_files[offset_type]
                            if not os.path.exists(rdp_path):
                                #DEBUG print(f"      Extraction skipped: {rdp_path} not found")
                                offset += 16
                                continue
                            src_file = open(rdp_path, 'rb')
                        
                        output_path = generate_filename(name, type_str, output_dir, used_names)
                        if toc_csize > 0 and effective_offset != 0:
                            success = extract_data(src_file, effective_offset, end_offset, output_path, is_compressed)
                            if success and is_compressed:
                                print(f"Extracted and decompressed to: {output_path}")
                            elif success:
                                print(f"Extracted to: {output_path}")
                            else:
                                print(f"Extracted (compressed, decompression failed): {output_path}")
                            # Check for .res files
                            if output_path.lower().endswith('.res'):
                                #DEBUG print(f"Detected nested .res file: {output_path}, processing with PRES_Loader.py...")
                                PRES_Loader.read_res_file(output_path)  # Call main script
                                nested_res_files.append(output_path)
                        else:
                            with open(output_path, 'wb') as out_f:
                                pass
                            print(f"Created empty file: {output_path}")
                        
                        if src_file != f:
                            src_file.close()
                    else:
                        print(f"      Extraction skipped: {offset_type}")
                
                offset += 32
            
            return nested_res_files  # Return list of nested .res files

    except FileNotFoundError:
        print(f"Error: File '{file_path}' not found.")
        return nested_res_files
    except Exception as e:
        print(f"Error reading file: {str(e)}")
        return nested_res_files

if __name__ == "__main__":
    read_rtbl_file("[rtbl file input not needed, since this is now a module]")