# Required Script: RTBL.py
# Supports nestled .res extraction + rtbl handling
# Update 1: released


import os
import struct
import shutil
import subprocess
from io import BytesIO
import zlib

def read_uint32(f):
    return struct.unpack('<I', f.read(4))[0]

def read_uint16(f):
    return struct.unpack('<H', f.read(2))[0]

def read_bytes(f, size):
    return f.read(size)

def read_string(f, offset):
    if offset == 0:
        return "N/A"
    current = f.tell()
    f.seek(offset)
    result = b""
    while True:
        char = f.read(1)
        if char == b'\x00' or not char:
            break
        result += char
    f.seek(current)
    return result.decode('utf-8', errors='ignore') or "N/A"

def blz_decompress(data):
    """Decompresses BLZ2-compressed data, supporting both small and large files."""
    data = BytesIO(data)
    magic = data.read(4)
    if magic != b"blz2":
        raise ValueError("Data is not in BLZ2 format.")

    decompressed = BytesIO()

    try:
        if len(data.getvalue()) >= 0xFFFF:
            # **Flush-based decompression for large files**
            while data.tell() < len(data.getvalue()):
                size_bytes = data.read(2)
                if not size_bytes:
                    break
                size = int.from_bytes(size_bytes, "little")
                chunk = zlib.decompress(data.read(size), wbits=-15)
                decompressed.write(chunk)
        else:
            # **Regular decompression for small files**
            size = int.from_bytes(data.read(2), "little")
            decompressed.write(zlib.decompress(data.read(size), wbits=-15))

    except Exception as e:
        raise ValueError(f"BLZ2 decompression failed: {e}")

    return decompressed.getvalue()



def resolve_toc_offset(toc_off):
    marker = toc_off & 0xF0000000
    resolved_offset = toc_off & 0x0FFFFFFF

    marker_map = {
        0x30000000: "NoSet",
        0x40000000: "package.rdp",
        0x50000000: "data.rdp",
        0x60000000: "patch.rdp",
        0xC0000000: "Current File",
        0xD0000000: "Current File"
    }

    marker_name = marker_map.get(marker, "Unknown")
    
    if marker_name in ["package.rdp", "data.rdp", "patch.rdp"]:
        resolved_offset *= 0x800  # Convert to absolute offset for RDP files

    return marker_name, resolved_offset

def generate_output_path(base_dir, path):
    if path == "N/A":
        return base_dir
    output_path = os.path.join(base_dir, path.replace("/", os.sep))
    os.makedirs(output_path, exist_ok=True)  # Ensure subdirectories exist
    return output_path


def extract_chunk(f, offset, size, output_path, filename):
    if filename == "N/A":  
        print(f"    Skipping extraction: Invalid filename")
        return  

    f.seek(offset)
    data = f.read(size)

    os.makedirs(output_path, exist_ok=True)
    output_file = os.path.join(output_path, filename)

    if os.path.exists(output_file):
        base, ext = os.path.splitext(output_file)
        counter = 0
        while os.path.exists(f"{base}_{counter:04}{ext}"):
            counter += 1
        output_file = f"{base}_{counter:04}{ext}"

    # **BLZ2 Decompression**
    if data[:4] == b'blz2':
        print(f"    Detected BLZ2 compressed file: {filename}, decompressing...")
        try:
            data = blz_decompress(data)
            print(f"    Successfully decompressed BLZ2 file: {filename}")
        except Exception as e:
            print(f"    Error decompressing BLZ2 data: {e}")

    with open(output_file, "wb") as out_f:
        out_f.write(data)

    print(f"    Extracted: {output_file}")

    # **RES Archive Handling (Depth-First)**
    if filename.lower().endswith(".res"):
        print(f"    Detected another .res archive: {output_file}, extracting it first...")
        try:
            parse_res(output_file)  # Process this .res archive first before moving on
        except Exception as e:
            print(f"    Error extracting .res archive: {output_file} -> {e}")
        return  # **Return immediately so we process nested .res files first**

    # **RTBL Handling**
    if filename.lower().endswith(".rtbl"):  
        print(f"    Detected RTBL file: {output_file}, calling rtbl.py...")
        try:
            subprocess.run(["python", "rtbl.py", output_file], check=True)
            print(f"    Successfully processed RTBL file: {output_file}")
        except subprocess.CalledProcessError as e:
            print(f"    Error processing RTBL file: {output_file} -> {e}")




def parse_res(filename):
    base_output_dir = os.path.splitext(filename)[0]

    with open(filename, 'rb') as f:
        header = read_uint32(f)
        group_offset = read_uint32(f)
        group_count = struct.unpack('B', f.read(1))[0]
        group_version = struct.unpack('B', f.read(1))[0]
        checksum = read_uint16(f)
        version = read_uint32(f)
        chunk_data = (read_uint32(f), read_uint32(f), read_uint32(f), read_uint32(f))

        print(f"Header: {header}")
        print(f"Group Offset: 0x{group_offset:X}")
        print(f"Group Count: {group_count}")
        print(f"Group Version: {group_version}")
        print(f"Checksum: {checksum}")
        print(f"Version: {version}")
        print(f"Chunk Data: {chunk_data}")

        f.seek(group_offset)
        group_data = []
        print("\n[Group Data]")
        for i in range(1, group_count + 1):
            data_offset = read_uint32(f)
            entry_count = read_uint32(f)
            group_data.append((data_offset, entry_count))
            print(f"Entry {i}: Data Offset = 0x{data_offset:X}, Count = {entry_count}")

        for entry_idx, (entry_data, entry_count) in enumerate(group_data, start=1):
            if entry_count == 0:
                print(f"\n[TOC @ Entry {entry_idx} / {entry_count}]")
                continue

            f.seek(entry_data)
            toc_entries = []
            for _ in range(entry_count):
                toc = {
                    "TOC_OFF": read_uint32(f),
                    "TOC_CSIZE": read_uint32(f),
                    "TOC_NAME": read_uint32(f),
                    "TOC_NAMEVAL": read_uint32(f),
                    "PADDING": read_bytes(f, 12),
                    "TOC_DSIZE": read_uint32(f)
                }
                toc_entries.append(toc)

            print(f"\n[TOC @ Entry {entry_idx} / {entry_count}]")
            for toc_idx, toc in enumerate(toc_entries, start=1):
                marker, resolved_offset = resolve_toc_offset(toc["TOC_OFF"])
                end_offset = resolved_offset + toc["TOC_CSIZE"]

                print(f"  TOC: {toc_idx}")
                print(f"    TOC_OFF: 0x{toc['TOC_OFF']:X} (Marker: {marker}) -> Resolved Offset: 0x{resolved_offset:X}")
                print(f"    TOC_CSIZE: {toc['TOC_CSIZE']} (End Offset: 0x{end_offset:X})")
                print(f"    TOC_NAME: 0x{toc['TOC_NAME']:X}")
                print(f"    TOC_NAMEVAL: {toc['TOC_NAMEVAL']}")
                print(f"    TOC_DSIZE: {toc['TOC_DSIZE']}")

                f.seek(toc["TOC_NAME"])
                name_struct = {
                    "Name": read_uint32(f),
                    "Type": read_uint32(f),
                    "Path": read_uint32(f),
                    "Path2": read_uint32(f),
                    "NoSetPath": read_uint32(f),
                }

                name_fields = ["Name", "Type", "Path", "Path2", "NoSetPath"]
                valid_fields = name_fields[:toc["TOC_NAMEVAL"]]

                print("    [TOC Name Structure]")
                toc_name_data = {}
                for field in name_fields:
                    if field in valid_fields:
                        value = read_string(f, name_struct[field])
                    else:
                        value = "N/A"
                    toc_name_data[field] = value
                    print(f"      {field}: {value}")

                filename_parts = [toc_name_data["Name"]]
                if toc_name_data["Type"] != "N/A":
                    filename_parts.append(toc_name_data["Type"])

                filename = ".".join(filter(lambda x: x != "N/A", filename_parts)) or "N/A"
                final_output_path = os.path.join(base_output_dir, toc_name_data["Path2"]) if toc_name_data["Path2"] != "N/A" else base_output_dir
                output_dir = generate_output_path(final_output_path, toc_name_data["Path"])


                if marker == "NoSet" and toc_name_data["NoSetPath"].startswith("PATH="):
                    source_path = toc_name_data["NoSetPath"][5:]  # Strip "PATH="
                    
                    if os.path.exists(source_path) and os.path.isfile(source_path):
                        noset_output_dir = generate_output_path(base_output_dir, toc_name_data["Path2"])
                        final_output_dir = os.path.join(noset_output_dir, os.path.dirname(source_path))
                        os.makedirs(final_output_dir, exist_ok=True)  # Ensure full path exists

                        dest_path = os.path.join(final_output_dir, os.path.basename(source_path))

                        # Prevent overwriting existing files
                        if os.path.exists(dest_path):
                            base, ext = os.path.splitext(dest_path)
                            counter = 0
                            while os.path.exists(f"{base}_{counter:04}{ext}"):
                                counter += 1
                            dest_path = f"{base}_{counter:04}{ext}"

                        # Use shutil to copy the file properly
                        try:
                            shutil.copy(source_path, dest_path)
                            print(f"    Copied: {source_path} -> {dest_path}")
                        except Exception as e:
                            print(f"    Failed to copy {source_path}: {e}")
                    else:
                        print(f"    Source file missing or invalid: {source_path}")


                elif marker in ["package.rdp", "data.rdp", "patch.rdp"]:
                    rdp_file = marker
                    if os.path.exists(rdp_file):
                        extract_chunk(open(rdp_file, "rb"), resolved_offset, toc["TOC_CSIZE"], output_dir, filename)

                elif marker == "Current File":
                    extract_chunk(f, resolved_offset, toc["TOC_CSIZE"], output_dir, filename)

if __name__ == "__main__":
    parse_res("system.res") #use any .res file