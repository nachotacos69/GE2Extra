using ComponentAce.Compression.Libs.zlib;
using System;
using System.Collections.Generic;
using System.IO;

namespace PRES_PROT
{
    public static class BLZ4
    {
        private static readonly int BLZ4_HEADER = 0x347A6C62;

        public static bool IsBLZ4(byte[] data)
        {
            return data.Length >= 4 && BitConverter.ToInt32(data, 0) == BLZ4_HEADER;
        }

        public static byte[] DecompressBLZ4(byte[] blz4File)
        {
            BLZ4FileDecompresser decompresser = new BLZ4FileDecompresser(blz4File);
            byte[] result = decompresser.GetByteResult();
            decompresser.Dispose();
            return result;
        }

        private class BLZ4FileDecompresser : IDisposable
        {
            private readonly List<byte[]> blockList = new List<byte[]>();
            private bool isUncompressed = false;
            private bool disposedValue = false;

            public BLZ4FileDecompresser(byte[] input)
            {
                if (input.Length <= 34) throw new FileLoadException($"BLZ4 Error: Invalid file length {input.Length}");

                using (MemoryStream ms = new MemoryStream(input))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    if (br.ReadInt32() != BLZ4_HEADER) throw new FileLoadException("BLZ4 Error: Invalid header");

                    br.ReadInt32(); // Unpacked size (unused)
                    br.ReadInt64(); // Reserved (unused)
                    br.ReadBytes(16); // MD5 hash (unused)

                    while (br.BaseStream.Position < input.Length)
                    {
                        int chunkSize = br.ReadUInt16();
                        blockList.Add(chunkSize == 0
                            ? br.ReadBytes((int)(input.Length - br.BaseStream.Position))
                            : br.ReadBytes(chunkSize));

                        if (chunkSize == 0) isUncompressed = true;
                    }

                    if (blockList.Count == 0) throw new FileLoadException("BLZ4 Error: No data blocks found");
                }
            }

            public byte[] GetByteResult()
            {
                if (isUncompressed) return blockList[0];

                List<byte[]> decompressedBlocks = new List<byte[]>();
                foreach (var block in blockList)
                {
                    DecompressBlock(block, out byte[] decompressed);
                    decompressedBlocks.Add(decompressed);
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    foreach (var block in decompressedBlocks) ms.Write(block, 0, block.Length);
                    return ms.ToArray();
                }
            }

            private static void DecompressBlock(byte[] input, out byte[] output)
            {
                using (MemoryStream ms = new MemoryStream())
                using (ZOutputStream zStream = new ZOutputStream(ms))
                {
                    zStream.Write(input, 0, input.Length);
                    zStream.finish();
                    output = ms.ToArray();
                }
            }

            public void Dispose()
            {
                if (!disposedValue)
                {
                    disposedValue = true;
                }
            }
        }
    }
}
