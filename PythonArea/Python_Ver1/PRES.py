import struct
from EntryGroup import EntryGroup
from FileEntry import FileEntry
import os
import json


class PRESFile:
    def __init__(self, file_path, rdp_files):
        self.file_path = file_path
        self.rdp_files = {k: v for k, v in rdp_files.items()} 
        self.rdp_file_handles = {}  
        self.data = None
        with open(file_path, "rb") as f:
            self.data = f.read()
        self.header = None
        self.group_offset = None
        self.group_count = None
        self.group_version = None
        self.unknown_field = None
        self.version = None
        self.entry_groups = []

    def parse_header(self):
        self.header = self.data[:4].decode('ascii')
        if self.header != "Pres":
            raise ValueError("Invalid file format. Expected 'Pres' header.")
        self.group_offset, = struct.unpack_from("<I", self.data, 0x04)
        self.group_count, = struct.unpack_from("<B", self.data, 0x08)
        self.group_version, = struct.unpack_from("<B", self.data, 0x09)
        self.unknown_field, = struct.unpack_from("<H", self.data, 0x0A)
        self.version, = struct.unpack_from("<I", self.data, 0x0C)

    def parse_entry_groups(self):
        if self.group_offset is None:
            raise ValueError("Group offset not initialized. Call parse_header first.")
        for i in range(self.group_count):
            offset = self.group_offset + (i * 8)
            entry_offset, entry_count = struct.unpack_from("<II", self.data, offset)
            self.entry_groups.append(EntryGroup(entry_offset, entry_count))

    def get_rdp_data(self, file_key, offset, size):
        if file_key not in self.rdp_files:
            raise ValueError(f"RDP file '{file_key}' not found.")
        if file_key not in self.rdp_file_handles:
            self.rdp_file_handles[file_key] = open(self.rdp_files[file_key], "rb")
        file_handle = self.rdp_file_handles[file_key]
        file_handle.seek(offset)
        return file_handle.read(size)

    def handle_nested_pres(self, file_path, output_folder):
        try:
            nested_pres = PRESFile(file_path, self.rdp_files)
            nested_pres.parse_header()
            nested_pres.parse_entry_groups()
            nested_pres.extract_files(output_folder)
        except Exception as e:
            print(f"[ERROR] Failed to process nested PRES file {file_path}: {e}")

    def extract_files(self, output_folder):
        print(f"Starting extraction to: {output_folder}")
        os.makedirs(output_folder, exist_ok=True)

        
        blt_list_path = os.path.join(os.path.dirname(__file__), "LIST_BLT.DATA")
        with open(blt_list_path, "w") as blt_list_file:
            metadata = {
                "Filename": self.file_path,
                "Magic": [ord(char) for char in self.header],
                "GroupOffset": self.group_offset,
                "GroupCount": self.group_count,
                "TotalFile": sum(group.entry_count for group in self.entry_groups),
                "EntryGroups": [group.entry_count for group in self.entry_groups],
                "Files": []
            }

            for group_index, group in enumerate(self.entry_groups):
                print(f"\nProcessing Entry Group {group_index + 1}: Offset = 0x{group.entry_offset:X}, Count = {group.entry_count}")
                if group.entry_offset == 0:
                    print(f"[INFO] Entry Group {group_index + 1} has no files. Skipping...")
                    continue

                for entry_index in range(group.entry_count):
                    entry_offset = group.entry_offset + (entry_index * 0x20)
                    try:
                        file_entry = FileEntry(self.data, entry_offset, self.get_rdp_data)
                        file_result = file_entry.extract_and_save(self.data, output_folder, self.handle_nested_pres, blt_list_file)

                        if file_result:
                            metadata["Files"].append({
                                "Location": file_entry.address_mode,
                                "Offset": file_entry.file_offset,
                                "Size": file_entry.compressed_size,
                                "DecompressedSize": file_entry.decompressed_size,
                                "MaxSize": file_entry.decompressed_size,
                                "ElementName": os.path.splitext(file_result["name"])[1][1:],
                                "FileName": file_result["name"],
                                "Compression": file_entry.compressed_size != file_entry.decompressed_size
                            })
                            print(f"[SUCCESS] Extracted {file_result['name']}")
                        else:
                            print(f"[IGNORED] Skipped file at Entry {entry_index + 1} in Entry Group {group_index + 1}")
                    except PermissionError as e:
                        print(f"[IGNORED] Permission issue for Entry {entry_index + 1} in Entry Group {group_index + 1}: {str(e)}")
                    except Exception as e:
                        print(f"[ERROR] Failed to process Entry {entry_index + 1} in Entry Group {group_index + 1}: {str(e)}")
                        continue

            metadata_path = os.path.splitext(self.file_path)[0] + ".json"
            print(f"\nSaving metadata to {metadata_path}")
            with open(metadata_path, "w") as meta_file:
                json.dump(metadata, meta_file, indent=4)

        self.close_rdp_files()

    def close_rdp_files(self):
        for handle in self.rdp_file_handles.values():
            handle.close()
        self.rdp_file_handles.clear()

    def debug_print(self):
        print(f"Header: {self.header}")
        print(f"Group Offset: 0x{self.group_offset:X}")
        print(f"Group Count: {self.group_count}")
        print(f"Group Version: {self.group_version}")
        print(f"Unknown Field: 0x{self.unknown_field:X}")
        print(f"Version: {self.version}")
        for i, group in enumerate(self.entry_groups):
            print(f" Entry Group {i}: Offset = 0x{group.entry_offset:X}, Count = {group.entry_count}")
