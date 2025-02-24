using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Decompression
{
    public class Decompress
    {
        // Constants
        private const uint ExpectedHeader = 0x327a6c62; // Little-endian "blz2" (0x62 0x6c 0x7a 0x32)
        private const int CompressedSizeLength = 2; // 2 bytes for UInt16

        
        /// Processes the input file, decompresses all blocks, rearranges them, and saves the output.
        
        
        public void ProcessFile(string filePath)
        {
            List<byte[]> decompressedBlocks = new List<byte[]>();

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    // Read and validate header (4 bytes)
                    uint header = reader.ReadUInt32();
                    if (header != ExpectedHeader)
                    {
                        throw new InvalidDataException("Invalid file header. Expected 'blz2' (0x327a6c62).");
                    }

                    int blockNumber = 0;
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        // Read compressed size (UInt16, 2 bytes)
                        byte[] compressedSizeBytes = reader.ReadBytes(CompressedSizeLength);
                        if (compressedSizeBytes.Length != CompressedSizeLength)
                        {
                            throw new InvalidDataException("Invalid compressed size.");
                        }

                        ushort compressedSize = BitConverter.ToUInt16(compressedSizeBytes, 0);

                        // Read compressed block
                        byte[] compressedBlock = reader.ReadBytes(compressedSize);
                        if (compressedBlock.Length != compressedSize)
                        {
                            throw new InvalidDataException("Compressed block size mismatch.");
                        }

                        // Decompress the block
                        byte[] decompressedBlock = DecompressBlock(compressedBlock);
                        decompressedBlocks.Add(decompressedBlock);

                        Console.WriteLine($"Decompressed block {blockNumber} with size {decompressedBlock.Length} bytes.");
                        blockNumber++;
                    }
                }
            }

            // Rearrange blocks: 01234567 -> 12345670
            if (decompressedBlocks.Count > 1)
            {
                byte[] firstBlock = decompressedBlocks[0];
                decompressedBlocks.RemoveAt(0);
                decompressedBlocks.Add(firstBlock);
                Console.WriteLine("Blocks rearranged :)");
            }

            // Combine all decompressed blocks into one
            byte[] combinedData = CombineBlocks(decompressedBlocks);

            // Write combined data to a file
            string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_decom" + Path.GetExtension(filePath);
            File.WriteAllBytes(outputFileName, combinedData);

            Console.WriteLine($"All blocks combined and saved as {outputFileName}.");
        }

        
        /// Decompresses a single block using DeflateStream.
       
        private byte[] DecompressBlock(byte[] compressedBlock)
        {
            using (MemoryStream compressedStream = new MemoryStream(compressedBlock))
            {
                using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                {
                    using (MemoryStream decompressedStream = new MemoryStream())
                    {
                        deflateStream.CopyTo(decompressedStream);
                        return decompressedStream.ToArray();
                    }
                }
            }
        }


        /// Combines multiple decompressed blocks into a single byte array.
        private byte[] CombineBlocks(List<byte[]> blocks)
        {
            int totalSize = 0;
            foreach (byte[] block in blocks)
            {
                totalSize += block.Length;
            }

            byte[] combinedData = new byte[totalSize];
            int offset = 0;
            foreach (byte[] block in blocks)
            {
                Buffer.BlockCopy(block, 0, combinedData, offset, block.Length);
                offset += block.Length;
            }

            return combinedData;
        }
    }
}