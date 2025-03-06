import os
from FileInfo import FileInfo  
from decompression import blz_decompress  


class FileEntry:
    def __init__(self, data, offset, get_rdp_data):
        self.file_offset = None
        self.address_mode = None
        self.compressed_size = None
        self.info_offset = None
        self.info_count = None
        self.decompressed_size = None
        self.get_rdp_data = get_rdp_data  

        self.parse(data, offset)

    def parse(self, data, offset):
        """
        Comments:
        Parsing FileEntry structure from the data at the given offset.
        """
        # Read UINT24 (offset) + AddressMode (1 byte)
        self.file_offset = int.from_bytes(data[offset:offset + 3], "little")  # UINT24 for offset
        self.address_mode = data[offset + 3]  # AddressMode (4th byte)

        # Parse remaining fields
        self.compressed_size = int.from_bytes(data[offset + 4:offset + 8], "little")
        self.info_offset = int.from_bytes(data[offset + 8:offset + 12], "little")
        self.info_count = int.from_bytes(data[offset + 12:offset + 16], "little")
        self.decompressed_size = int.from_bytes(data[offset + 28:offset + 32], "little")

    def get_actual_data(self, data):
        """
        Comments:
        Retrieve actual file data, switching to the appropriate RDP file when needed.
        """
        rdp_file_key = {
            0x40: "package",  # AddressMode 0x40 -> package.rdp
            0x50: "data",     # AddressMode 0x50 -> data.rdp
            0x60: "patch",    # AddressMode 0x60 -> patch.rdp
        }.get(self.address_mode)

        if rdp_file_key:
            # Calculate offset in the RDP file (big brain)
            actual_offset = self.file_offset * 0x800
            return self.get_rdp_data(rdp_file_key, actual_offset, self.compressed_size)
        else:
            # Default to using the data in the .res file
            return data[self.file_offset:self.file_offset + self.compressed_size]

    def resolve_duplicate_filename(self, output_folder, file_name):
        """
        Resolve duplicate filenames by appending a numeric suffix.
        """
        base_name, ext = os.path.splitext(file_name)
        counter = 0
        resolved_name = file_name

        while os.path.exists(os.path.join(output_folder, resolved_name)):
            resolved_name = f"{base_name}_{counter:04d}{ext}"
            counter += 1

        return resolved_name

    def extract_and_save(self, data, output_folder, handle_nested_pres, blt_list_file_path):
        """
        Commentary here lmao:
        Extracts and saves the file to the specified folder.
        If the file is a `.res`, it processes it as a nested PRES archive.
        Tracks `.blt` files with an `AG_` prefix and logs their details to the list file. this is just a silly thing
        """
        try:
            # Retrieve the actual data
            file_data = self.get_actual_data(data)

            # Decompress the files
            if file_data[:4] == b"blz2":
                file_data = blz_decompress(file_data, self.compressed_size, self.decompressed_size)

            # Extract file info
            file_info = FileInfo(data, self.info_offset)
            file_name = f"{file_info.file_name}.{file_info.file_ext}"
            file_name = self.resolve_duplicate_filename(output_folder, file_name)

            output_path = os.path.join(output_folder, file_name)

            # Save the file
            with open(output_path, "wb") as f:
                f.write(file_data)

          
            if file_name.lower().endswith(".blt") and file_name.startswith("AG_"):
                with open(blt_list_file_path, "a") as blt_list_file:  # Append to prevent overwriting
                    blt_details = (
                        f"[Name: {file_name}]==> "
                        f"Pointer: 0x{self.file_offset:06X} || 0x{self.file_offset:06X} || "
                        f"0x{self.address_mode:02X} || 0x{self.compressed_size:X} || "
                        f"0x{self.info_offset:X} || 0x{self.info_count:X} || "
                        f"0x{self.decompressed_size:X}\n"
                    )
                    blt_list_file.write(blt_details)

            
            if file_name.lower().endswith(".res"):
                nested_output_folder = os.path.join(output_folder, os.path.splitext(file_name)[0])
                os.makedirs(nested_output_folder, exist_ok=True)
                print(f"[INFO] Detected nested PRES file: {file_name}. Processing as a PRES archive...")
                handle_nested_pres(output_path, nested_output_folder)

            return {"name": file_name, "size": len(file_data)}

        except Exception as e:
            print(f"[ERROR] Failed to extract file at offset 0x{self.file_offset:X}: {e}")
            return None
