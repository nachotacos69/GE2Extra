﻿using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace RES_PACKER
{
    class Deflate
    {
        public const uint BLZ2_MAGIC = 0x327A6C62; // "blz2" in little-endian

        public static byte[] DecompressBLZ2(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length < 6 || BitConverter.ToUInt32(compressedData, 0) != BLZ2_MAGIC)
            {
                return null; // Invalid data or not BLZ2
            }

            try
            {
                using (MemoryStream input = new MemoryStream(compressedData))
                using (MemoryStream output = new MemoryStream())
                {
                    input.Seek(4, SeekOrigin.Begin); // Skip header

                    List<byte[]> blocks = new List<byte[]>();
                    while (input.Position < input.Length)
                    {
                        ushort compressedSize = BitConverter.ToUInt16(compressedData, (int)input.Position);
                        input.Seek(2, SeekOrigin.Current); // Skip size field

                        if (input.Position + compressedSize > input.Length)
                        {
                            return null; // Invalid size
                        }

                        byte[] block = new byte[compressedSize];
                        input.Read(block, 0, compressedSize);

                        using (MemoryStream blockStream = new MemoryStream(block))
                        using (DeflateStream deflateStream = new DeflateStream(blockStream, CompressionMode.Decompress))
                        using (MemoryStream blockOutput = new MemoryStream())
                        {
                            deflateStream.CopyTo(blockOutput);
                            blocks.Add(blockOutput.ToArray());
                        }
                    }

                    if (blocks.Count == 0)
                        return null;

                    if (blocks.Count == 1)
                    {
                        return blocks[0];
                    }

                    int totalLength = 0;
                    foreach (var block in blocks)
                        totalLength += block.Length;

                    byte[] decompressed = new byte[totalLength];
                    int currentPos = 0;

                    for (int i = 1; i < blocks.Count; i++)
                    {
                        Array.Copy(blocks[i], 0, decompressed, currentPos, blocks[i].Length);
                        currentPos += blocks[i].Length;
                    }
                    Array.Copy(blocks[0], 0, decompressed, currentPos, blocks[0].Length);

                    return decompressed;
                }
            }
            catch (Exception)
            {
                return null; // Decompression failed
            }
        }
    }
}