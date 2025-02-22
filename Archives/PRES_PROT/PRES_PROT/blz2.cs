using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;


// ORIGINAL BY HAOJUN/RANDERION: https://github.com/HaoJun0823/GECV-OLD
// edited a lot due to the block order is odd when i tried using the original
// so i kinda stripped down the entire code into this.

public static class BLZ
{
    public static readonly int BLZ2_HEADER = 0x327a6c62; // "blz2" in little-endian

    
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

            // Reorder blocks: Move the first block to the end
            if (blocks.Count > 1)
            {
                var firstBlock = blocks[0];
                blocks.RemoveAt(0);
                blocks.Add(firstBlock);
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