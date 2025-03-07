using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using ComponentAce.Compression.Libs.zlib;

namespace RES_UNPACKER
{
    class Deflate
    {
        private const uint BLZ2_MAGIC = 0x327A6C62; // "blz2" in little-endian
        private const uint BLZ4_MAGIC = 0x347A6C62; // "blz4" in little-endian

        public static byte[] DecompressBLZ2(byte[] compressedData)
/*
 * as for this one, it's a just a basic zlib compression with no header. so you'll need the raw block
 * sturcture (in order)
 * [Header or BLZ2_MAGIC]
 * [2 bytes of `compressedSize` value]
 * [compressed blocks, block size matches with 2 bytes compressed size value]
*/
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

/* In most cases, you'll encounter multiple blocks. the structure is the same as for those single block but stacked in some order
* [header][2 bytes compressedSize][compressed block] || [another compressedSize][another compressed block]
* In case of scenario encountering a of something like this, you will need to arrange those blocks in proper manner
* for compressed, block order should be: 01234567 || for decompression, block order should be: 12345670
* Failure to do so will make the file jumble, the header of a will be sometimes positioned at 0x10000 or 0x20000
*/
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

/*
 * BLZ4 decompression uses `ComponentAce.Compression.Libs.zlib`. rather than your regular DeflateStream
 * sturcture (in order)
 * [Header or BLZ4_MAGIC]
 * [4 bytes of unpack size]
 * [8 bytes of zeroes]
 * [16 bytes of MD5]
 * [2 bytes compressed size value]
 * [compressed blocks, block size matches with 2 bytes compressed size value]
 * if im correct, this is the correct structure, for now, since i mostly focused on the PSP version of the game.
*/

        public static byte[] DecompressBLZ4(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length < 32 + 2 || BitConverter.ToUInt32(compressedData, 0) != BLZ4_MAGIC)
            {
                return null; // Invalid data or not BLZ4
            }

            try
            {
                using (MemoryStream input = new MemoryStream(compressedData))
                using (BinaryReader reader = new BinaryReader(input))
                {
                    int magic = reader.ReadInt32();
                    if (magic != BLZ4_MAGIC)
                        return null;

                    int unpackedSize = reader.ReadInt32(); // Not used in decompression, but present
                    reader.ReadInt64(); // Skip zero padding
                    reader.ReadBytes(16); // Skip MD5

                    List<byte[]> blockList = new List<byte[]>();
                    bool isUncompressed = false;

                    // Read all blocks in original order (0, 1, 2, ..., n-1)
                    while (input.Position < input.Length)
                    {
                        ushort chunkSize = reader.ReadUInt16();
                        if (chunkSize == 0)
                        {
                            // Uncompressed data: read remaining bytes
                            int remainingLength = (int)(input.Length - input.Position);
                            if (remainingLength <= 0)
                                return null;

                            byte[] uncompressedBlock = reader.ReadBytes(remainingLength);
                            blockList.Add(uncompressedBlock);
                            isUncompressed = true;
                            break;
                        }
                        else
                        {
                            if (input.Position + chunkSize > input.Length)
                                return null; // Invalid chunk size

                            byte[] compressedBlock = reader.ReadBytes(chunkSize);
                            blockList.Add(compressedBlock);
                        }
                    }

                    if (blockList.Count == 0)
                        return null;

                    if (isUncompressed)
                    {
                        return blockList[0]; // Single uncompressed block
                    }

                    // Decompress each block in original order
                    List<byte[]> decompressedBlocks = new List<byte[]>();
                    foreach (var block in blockList)
                    {
                        byte[] decompressedBlock;
                        DecompressZlibBlock(block, out decompressedBlock);
                        if (decompressedBlock == null)
                            return null; // Decompression failed
                        decompressedBlocks.Add(decompressedBlock);
                    }

                    // Rearrange blocks: 1, 2, ..., n-1, 0
                    if (decompressedBlocks.Count == 1)
                    {
                        return decompressedBlocks[0];
                    }

                    int totalLength = 0;
                    foreach (var block in decompressedBlocks)
                        totalLength += block.Length;

                    byte[] result = new byte[totalLength];
                    int currentPos = 0;

                    // Copy blocks 1 through n-1
                    for (int i = 1; i < decompressedBlocks.Count; i++)
                    {
                        Array.Copy(decompressedBlocks[i], 0, result, currentPos, decompressedBlocks[i].Length);
                        currentPos += decompressedBlocks[i].Length;
                    }
                    // Append block 0
                    Array.Copy(decompressedBlocks[0], 0, result, currentPos, decompressedBlocks[0].Length);

                    return result;
                }
            }
            catch (Exception)
            {
                return null; // Decompression failed
            }
        }

        private static void DecompressZlibBlock(byte[] inData, out byte[] outData)
        {
            using (MemoryStream outMemoryStream = new MemoryStream())
            using (ZOutputStream outZStream = new ZOutputStream(outMemoryStream))
            using (Stream inMemoryStream = new MemoryStream(inData))
            {
                CopyStream(inMemoryStream, outZStream);
                outZStream.finish();
                outData = outMemoryStream.ToArray();
            }
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[1000000]; // 1MB buffer, as per original
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
            output.Flush();
        }
    }
}