using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

public static class BLZ
{
    public static readonly int BLZ2_HEADER = 0x327a6c62; // "blz2" in little-endian

    /// <summary>
    /// Checks if the data has a BLZ2 header.
    /// </summary>
    public static bool IsBLZ2(byte[] data)
    {
        if (data.Length < 4)
        {
            return false;
        }

        using (var ms = new MemoryStream(data))
        using (var br = new BinaryReader(ms))
        {
            int magic = br.ReadInt32();
            return magic == BLZ2_HEADER;
        }
    }

    /// <summary>
    /// Decompresses BLZ2 data.
    /// </summary>
    public static byte[] DecompressBLZ2(byte[] blz2Data)
    {
        using (var ms = new MemoryStream(blz2Data))
        using (var br = new BinaryReader(ms))
        {
            int magic = br.ReadInt32();
            if (magic != BLZ2_HEADER)
            {
                throw new InvalidDataException("Not a valid BLZ2 file.");
            }

            var blocks = new List<byte[]>();
            while (ms.Position < ms.Length)
            {
                int blockSize = br.ReadUInt16();
                byte[] blockData = br.ReadBytes(blockSize);
                byte[] decompressedBlock = DecompressBlock(blockData);
                blocks.Add(decompressedBlock);
            }

            // Combine all blocks into a single byte array
            using (var resultMs = new MemoryStream())
            {
                foreach (var block in blocks)
                {
                    resultMs.Write(block, 0, block.Length);
                }
                return resultMs.ToArray();
            }
        }
    }

    /// <summary>
    /// Decompresses a single block of BLZ2 data.
    /// </summary>
    private static byte[] DecompressBlock(byte[] blockData)
    {
        using (var ms = new MemoryStream(blockData))
        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
        using (var resultMs = new MemoryStream())
        {
            ds.CopyTo(resultMs);
            return resultMs.ToArray();
        }
    }
}