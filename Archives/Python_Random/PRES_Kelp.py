## Date Created: Thursday, June 20 2024
## Note: Older Version with nestled .res handling
## No RTBL handling

import os
import sys
import struct
import zlib
from io import BytesIO


def read_null_terminated_string(f, offset):
    """ Reads a null-terminated UTF-8 string from the given file offset """
    f.seek(offset)
    string_bytes = bytearray()
    while True:
        byte = f.read(1)
        if byte == b'\x00' or not byte:
            break
        string_bytes.extend(byte)
    return string_bytes.decode('utf-8', errors='ignore')


def process_toc_offset(toc_off, compressed_size):
    """ Processes TOC Offset, removes markers, calculates absolute offset and range """
    marker = (toc_off & 0xF0000000) >> 28  # Extract marker (first 4 bits)
    actual_offset = toc_off & 0x0FFFFFFF  # Remove marker bits

    rdp_offset = None
    marker_name = None
    is_rdp = False

    if marker == 0x4:
        marker_name = "package.rdp"
        rdp_offset = actual_offset * 0x800
        is_rdp = True
    elif marker == 0x5:
        marker_name = "data.rdp"
        rdp_offset = actual_offset * 0x800
        is_rdp = True
    elif marker == 0x6:
        marker_name = "patch.rdp"
        rdp_offset = actual_offset * 0x800
        is_rdp = True
    elif marker in [0xC, 0xD]:
        marker_name = "current file"

    absolute_offset = rdp_offset if is_rdp else "N/A"
    toc_end_offset = (absolute_offset + compressed_size) if is_rdp else f"{hex(actual_offset)} - {hex(actual_offset + compressed_size)}"

    return {
        "Original Offset": hex(toc_off),
        "Marker": marker,
        "Marker Name": marker_name,
        "Base Offset": hex(actual_offset),
        "Absolute RDP Offset": hex(absolute_offset) if is_rdp else "N/A",
        "TOC Offset Range": f"[RDP] | {hex(absolute_offset)} - {hex(toc_end_offset)}" if is_rdp else toc_end_offset,
        "Is RDP": is_rdp
    }


def sanitize_filename(filename):
    """ Ensures a valid filename, preserving valid path separators """
    return filename.replace(":", "_").replace("*", "_").replace("\\", "/")


def decompress_blz2(data, compressed_size, decompressed_size):
    """Decompresses BLZ2-compressed data with chunked support for large files."""
    buffer = BytesIO(data)

    # Validate BLZ2 magic header
    if buffer.read(4) != b"blz2":
        raise ValueError("Invalid BLZ2 format detected.")

    decompressed_data = bytearray()

    if decompressed_size >= 0xFFFF:
        # Large file decompression (≥ 64KB)
        while buffer.tell() < compressed_size:
            chunk_size = int.from_bytes(buffer.read(2), "little")
            decompressed_data.extend(zlib.decompress(buffer.read(chunk_size), wbits=-15))
    else:
        # Small file decompression (< 64KB)
        chunk_size = int.from_bytes(buffer.read(2), "little")
        decompressed_data.extend(zlib.decompress(buffer.read(chunk_size), wbits=-15))

    return bytes(decompressed_data)


def extract_file(f, output_dir, entry):
    """ Extracts and decompresses a file from the .res archive """
    base_offset = int(entry["Processed TOC"]["Base Offset"], 16)
    compressed_size = entry["Compressed Size"]
    decompressed_size = entry["Data Size"]
    absolute_rdp_offset = entry["Processed TOC"]["Absolute RDP Offset"]

    # Determine filename
    name = entry.get("Name", "unknown")
    type_ = entry.get("Type", "bin")
    filename = f"{name}.{type_}"

    # Determine folder path using Path2
    folder_path = entry.get("Path2") or entry.get("Path")
    if folder_path:
        folder_path = sanitize_filename(folder_path.strip("/"))
        full_output_path = os.path.join(output_dir, folder_path)

        # If the last part of Path2 matches filename, treat it as the actual file
        path_parts = folder_path.split("/")
        if path_parts and path_parts[-1] == filename:
            filename = path_parts.pop()
            full_output_path = os.path.join(output_dir, "/".join(path_parts))

        os.makedirs(full_output_path, exist_ok=True)
    else:
        full_output_path = output_dir

    # Handle duplicate filenames
    counter = 0
    base_filename = filename
    while os.path.exists(os.path.join(full_output_path, filename)):
        filename = f"{os.path.splitext(base_filename)[0]}_{counter:04d}{os.path.splitext(base_filename)[1]}"
        counter += 1

    filepath = os.path.join(full_output_path, filename)

    # If entry is an RDP, extract from RDP file
    if entry["Processed TOC"]["Is RDP"]:
        rdp_file = entry["Processed TOC"]["Marker Name"]
        if not os.path.exists(rdp_file):
            print(f"  [ERROR] RDP file {rdp_file} not found, skipping {filename}")
            return

        with open(rdp_file, "rb") as rdp_f:
            rdp_f.seek(int(absolute_rdp_offset, 16))
            data = rdp_f.read(compressed_size)
    else:
        f.seek(base_offset)
        data = f.read(compressed_size)

    # Check if the file is BLZ2 compressed
    if data[:4] == b'blz2':
        print(f"  [DECOMPRESSING] {filename} (BLZ2 detected)")
        decompressed_data = decompress_blz2(data, compressed_size, decompressed_size)
        if decompressed_data:
            with open(filepath, "wb") as out_f:
                out_f.write(decompressed_data)
            print(f"  [EXTRACTED] {filepath}")
        else:
            print(f"  [ERROR] Failed to decompress {filename}")
    else:
        with open(filepath, "wb") as out_f:
            out_f.write(data)
        print(f"  [EXTRACTED] {filepath}")


def read_res_file(file_path):
    """ Reads a .res file and extracts its contents """
    with open(file_path, "rb") as f:
        # Read Header
        f.seek(4)  # Skip first 4 bytes (header)
        group_offset = struct.unpack("<I", f.read(4))[0]
        group_count = struct.unpack("<B", f.read(1))[0]
        f.seek(2, 1)  # Skip group_version (1 byte) and checksum (2 bytes)
        version = struct.unpack("<I", f.read(4))[0]

        # Read Group Data
        f.seek(group_offset)
        group_entries = [(struct.unpack("<II", f.read(8))) for _ in range(group_count)]

        # Read TOC based on Group Entries
        toc_entries = []
        for toc_offset, toc_count in group_entries:
            f.seek(toc_offset)
            for _ in range(toc_count):
                toc_data = f.read(32)
                if toc_data[:16] == b'\x00' * 16:
                    continue

                toc_off, toc_csize, toc_name, toc_nameval, toc_dsize = struct.unpack("<IIII12xI", toc_data)
                toc_info = process_toc_offset(toc_off, toc_csize)

                toc_entries.append({
                    "TOC Offset": toc_off,
                    "Compressed Size": toc_csize,
                    "Name Offset": toc_name,
                    "Name Value": toc_nameval,
                    "Data Size": toc_dsize,
                    "Processed TOC": toc_info
                })
        # Read Name Data
        for entry in toc_entries:
            if entry["Name Offset"] and entry["Name Value"]:
                f.seek(entry["Name Offset"])
                name_entries = [struct.unpack("<I", f.read(4))[0] for _ in range(entry["Name Value"])]

                entry["Name"] = read_null_terminated_string(f, name_entries[0]) if len(name_entries) > 0 else ""
                entry["Type"] = read_null_terminated_string(f, name_entries[1]) if len(name_entries) > 1 else ""
                entry["Path"] = read_null_terminated_string(f, name_entries[2]) if len(name_entries) > 2 else ""
                entry["Path2"] = read_null_terminated_string(f, name_entries[3]) if len(name_entries) > 3 else ""
        # Extract files
        output_dir = os.path.splitext(file_path)[0]
        os.makedirs(output_dir, exist_ok=True)

        for entry in toc_entries:
            extract_file(f, output_dir, entry)

        # Find new .res files and extract them
        res_files = sorted([os.path.join(output_dir, f) for f in os.listdir(output_dir) if f.endswith(".res")])
        for res in res_files:
            read_res_file(res)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python script.py <file.res>")
        sys.exit(1)

    res_file = sys.argv[1]
    if not os.path.exists(res_file):
        print(f"[ERROR] File {res_file} not found.")
        sys.exit(1)

    read_res_file(res_file)
