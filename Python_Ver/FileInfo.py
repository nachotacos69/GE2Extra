import struct

class FileInfo:
    def __init__(self, data, offset):
        self.file_name_offset = None
        self.file_ext_offset = None
        self.file_name = None
        self.file_ext = None

        self.parse(data, offset)

    def parse(self, data, offset):
        # Convert offsets from Little Endian to Big Endian
        self.file_name_offset = int.from_bytes(data[offset:offset + 4], "little")
        self.file_ext_offset = int.from_bytes(data[offset + 4:offset + 8], "little")

        # Extract the filename and extension as strings
        self.file_name = self.extract_string(data, self.file_name_offset)
        self.file_ext = self.extract_string(data, self.file_ext_offset)

    def extract_string(self, data, offset):
        string_data = bytearray()
        while offset < len(data) and data[offset] != 0:
            string_data.append(data[offset])
            offset += 1
        return string_data.decode("utf-8")  # UTF-8 encoding
