import zlib
from io import BytesIO

def blz_decompress(data, csize, dsize):
    data = BytesIO(data)
    magic = data.read(4)
    if magic != b"blz2":
        raise ValueError("Data is not in BLZ2 format.")

    decom = b""
    if dsize >= 0xFFFF:
        size = int.from_bytes(data.read(2), "little")
        ekor = zlib.decompress(data.read(size), -15)
        while data.tell() < csize:
            size = int.from_bytes(data.read(2), "little")
            decom += zlib.decompress(data.read(size), -15)
        return decom + ekor
    else:
        size = int.from_bytes(data.read(2), "little")
        decom = zlib.decompress(data.read(size), -15)
        return decom
