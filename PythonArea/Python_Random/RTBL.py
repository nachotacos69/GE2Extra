# handles proper RTBL file reading

import struct
import os
import sys

def read_uint32(file):
    """Reads 4 bytes and returns an unsigned integer."""
    return struct.unpack("<I", file.read(4))[0]

def is_padding(data):
    """Checks if 16 bytes are all zeroes (padding)."""
    return data == b'\x00' * 16

def read_utf8_string(file, offset, max_length=32):
    """Reads a UTF-8 string from a given offset, stopping at 0x00 or padding."""
    file.seek(offset)
    raw_data = file.read(max_length)
    return raw_data.split(b'\x00')[0].decode('utf-8', errors='ignore')

def align_to_16(offset):
    """Aligns an offset to the nearest previous 16-byte boundary."""
    return offset & ~0xF

def get_real_offset(toc_off):
    """Removes marker bits (0xF0000000) to get the real address."""
    return toc_off & 0x0FFFFFFF

def identify_rdp_file(marker):
    """Identifies RDP file type based on the TOC marker."""
    rdp_types = {0x4: "package.rdp", 0x5: "data.rdp", 0x6: "patch.rdp", 0xC: "current file", 0xD: "current file"}
    return rdp_types.get(marker, None)

def extract_data(rdp_filename, output_folder, file_name, absolute_offset, size):
    """Extracts data from the specified RDP file using absolute offset and compressed size."""
    if not rdp_filename:
        return  # Skip extraction if no valid RDP file

    output_path = os.path.join(output_folder, file_name)
    
    # Handle duplicate names by appending _0000, _0001, etc.
    base_name, ext = os.path.splitext(output_path)
    count = 0
    while os.path.exists(output_path):
        output_path = f"{base_name}_{count:04}{ext}"
        count += 1

    with open(rdp_filename, "rb") as rdp_file, open(output_path, "wb") as out_file:
        rdp_file.seek(absolute_offset)
        out_file.write(rdp_file.read(size))
    
    print(f"Extracted: {output_path}")

def parse_rtbl(filename):
    """Parses an .rtbl file and extracts files based on TOC information."""
    output_folder = os.path.splitext(filename)[0]  # Create folder using input filename
    os.makedirs(output_folder, exist_ok=True)

    with open(filename, "rb") as file:
        file_size = file.seek(0, 2)  # Get file size
        file.seek(0)  # Reset position

        while file.tell() < file_size:
            start_pos = file.tell()
            chunk = file.read(16)

            # If first 16 bytes are all zeroes, keep reading in 16-byte increments
            if is_padding(chunk):
                continue
            
            # Read the second 16 bytes to complete the 32-byte TOC structure
            chunk += file.read(16)

            # Extract TOC values
            file.seek(start_pos)
            raw_toc_off = read_uint32(file)
            toc_csize = read_uint32(file)
            rtoc_name = read_uint32(file)  # Offset to name
            rtoc_nameval = read_uint32(file)  # This should define the name length
            file.read(12)  # Skip padding
            toc_dsize = read_uint32(file)

            # Extract marker from TOC_OFF and get the real address
            marker = (raw_toc_off >> 28) & 0xF  # First 4 bits
            toc_off = get_real_offset(raw_toc_off)

            # Check if it's an RDP file and compute absolute offset if needed
            rdp_type = identify_rdp_file(marker)
            absolute_offset = toc_off * 0x800 if rdp_type else toc_off

            # Compute TOC_OFF End Offset
            toc_end_offset = toc_off + toc_csize

            # Ensure `RTOC_NAMEVAL` is used correctly to determine name structure size
            name_struct_size = min(4, rtoc_nameval)  # Can be 1-4 entries (Name, Type, Path, Path2)

            # Scan until 16-byte padding to capture the full RTOC area
            rtoc_area_start = file.tell()
            while file.tell() < file_size:
                check_padding = file.read(16)
                if is_padding(check_padding):
                    break  # Stop at padding
            rtoc_area_end = file.tell()
            
            # Simulate zero-based offsets for the Name Table (tbh it should go name_offset + 0x20 + whatever the stuff you have - 0x20. sadly i can't do it properly)
            simulated_rtoc_base = rtoc_area_start  # Start of the Name Table area
            name_offset = simulated_rtoc_base + (rtoc_name - 0x20)

            # Read Name Table Entries (max 4 fields)
            file.seek(name_offset)
            name_entries = [read_uint32(file) for _ in range(name_struct_size)]

            # Read actual names
            names = []
            for entry in name_entries:
                if entry == 0:
                    names.append("None/Direct")  # Blank Name/Path
                else:
                    names.append(read_utf8_string(file, simulated_rtoc_base + (entry - 0x20), 32))

            # Ensure TOC alignment before scanning the next one
            next_toc_offset = align_to_16(file.tell())

            # Generate output filename
            file_name = f"{names[0]}.{names[1]}" if names[0] != "None/Direct" and names[1] != "None/Direct" else None

            # Output parsed data
            print(f"TOC @ {start_pos:08X}")
            print(f"  Offset: {raw_toc_off:08X} (Real: {toc_off:08X})")
            if rdp_type:
                print(f"  RDP Type: {rdp_type}")
                print(f"  Absolute Offset: {absolute_offset:08X}")
            print(f"  Compressed Size: {toc_csize}")
            print(f"  End Offset: {toc_end_offset:08X}")
            print(f"  Name Offset: {rtoc_name:08X} (Actual: {name_offset:08X})")
            print(f"  Name Length: {rtoc_nameval * 4}")
            print(f"  Data Size: {toc_dsize}")
            print(f"  Name: {names[0] if len(names) > 0 else 'N/A'}")
            print(f"  Type: {names[1] if len(names) > 1 else 'N/A'}")
            print(f"  Path: {names[2] if len(names) > 2 else 'None/Direct'}")
            print(f"  Path2: {names[3] if len(names) > 3 else 'N/A'}")
            print("-" * 50)

            # Extract file if conditions are met
            if file_name and rdp_type:
                extract_data(rdp_type, output_folder, file_name, absolute_offset, toc_csize)

            # Move to the next TOC, ensuring 16-byte alignment
            file.seek(next_toc_offset)

# Usage
if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: rtbl.py <rtbl_file>")
        sys.exit(1)

    rtbl_file = sys.argv[1]
    parse_rtbl(rtbl_file)