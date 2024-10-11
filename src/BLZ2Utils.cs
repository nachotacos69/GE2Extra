//original by haojun

using ComponentAce.Compression.Libs.zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace GE2
{
    public static class BLZ2Utils
    {
        public static readonly int BLZ2_HEADER = 0x327a6c62;

        public static bool IsBLZ2(byte[] data)
        {
            if (data.Length < 4)
            {
                return false;
            }

            using (MemoryStream inputMs = new MemoryStream(data))
            {
                using (BinaryReader inputBr = new BinaryReader(inputMs))
                {
                    int magic = inputBr.ReadInt32();

                    return magic == BLZ2_HEADER;
                }
            }
        }

        public static byte[] UnpackBLZ2Data(byte[] blz2File)
        {
            using (var bfd = new BLZ2FileDecompresser(blz2File))
            {
                return bfd.GetByteResult();
            }
        }

        public static byte[] DoMicrosoftDeflateData(byte[] inData, CompressionMode mode)
        {
            using (MemoryStream inputStream = new MemoryStream(inData))
            {
                using (MemoryStream resultStream = new MemoryStream())
                {
                    if (mode == CompressionMode.Compress)
                    {
                        using (DeflateStream deflateStream = new DeflateStream(resultStream, mode))
                        {
                            inputStream.CopyTo(deflateStream);
                        }
                    }
                    else
                    {
                        using (DeflateStream deflateStream = new DeflateStream(inputStream, mode))
                        {
                            deflateStream.CopyTo(resultStream);
                        }
                    }

                    return resultStream.ToArray();
                }
            }
        }

        public static byte[] PackBLZ2Data(byte[] originFile)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    byte[] fileData = originFile;

                    bw.Write(BLZ2_HEADER);
                    bw.Flush();

                    if (fileData.Length <= 0xff)
                    {
                        bw.Write(Convert.ToUInt16(fileData.Length));
                        bw.Write(fileData);

                        Console.WriteLine($"BLZ2:Header:{BLZ2_HEADER}, Length:{fileData.Length}, No Need Compress.");
                    }
                    else
                    {
                        // Assuming BLZ4Utils.SplitBytes is replaced with a method to split the data
                        var splitData = SplitBytes(fileData, 0xff); // Replace SplitBytes method if needed
                        byte[] compress;

                        if (splitData.Count > 1)
                        {
                            compress = DoMicrosoftDeflateData(splitData[splitData.Count - 1], CompressionMode.Compress);
                            bw.Write(Convert.ToUInt16(compress.Length));
                            bw.Write(compress);

                            for (int i = 0; i < splitData.Count - 1; i++)
                            {
                                compress = DoMicrosoftDeflateData(splitData[i], CompressionMode.Compress);
                                bw.Write(Convert.ToUInt16(compress.Length));
                                bw.Write(compress);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < splitData.Count; i++)
                            {
                                compress = DoMicrosoftDeflateData(splitData[i], CompressionMode.Compress);
                                Console.WriteLine($"Compress Data; {splitData[i].Length} To {compress.Length}. ({i + 1}/{splitData.Count})");

                                bw.Write(Convert.ToUInt16(compress.Length));
                                bw.Write(compress);
                            }
                        }

                        Console.WriteLine($"BLZ2:Header:{BLZ2_HEADER}, Block Count:{splitData.Count}.");
                    }

                    byte[] result = ms.ToArray();

                    if (!IsBLZ2(result))
                    {
                        throw new InvalidDataException("BLZ2 Build Error!");
                    }

                    return result;
                }
            }
        }

        private static List<byte[]> SplitBytes(byte[] fileData, int maxChunkSize)
        {
            List<byte[]> result = new List<byte[]>();
            for (int i = 0; i < fileData.Length; i += maxChunkSize)
            {
                int chunkSize = Math.Min(maxChunkSize, fileData.Length - i);
                byte[] chunk = new byte[chunkSize];
                Buffer.BlockCopy(fileData, i, chunk, 0, chunkSize);
                result.Add(chunk);
            }

            return result;
        }

        protected internal class BLZ2FileDecompresser : IDisposable
        {
            int magic = BLZ2_HEADER;
            bool isSingle = false;
            List<byte[]> blockList = new List<byte[]>();
            private bool disposedValue;

            public BLZ2FileDecompresser(byte[] input)
            {
                if (input.Length <= 4 + 2)
                {
                    throw new FileLoadException($"DecompressBLZ2: input.length:{input.Length} <= 32 + 2");
                }

                using (MemoryStream inputMs = new MemoryStream(input))
                {
                    using (BinaryReader inputBr = new BinaryReader(inputMs))
                    {
                        int magic = inputBr.ReadInt32();

                        if (magic != this.magic)
                        {
                            throw new FileLoadException($"DecompressBLZ2: magic:{magic.ToString("X8")} != {BLZ2_HEADER.ToString("X8")}");
                        }

                        while (inputBr.BaseStream.Position < input.Length)
                        {
                            int chunkSize = inputBr.ReadUInt16();
                            blockList.Add(inputBr.ReadBytes(chunkSize));
                        }

                        isSingle = blockList.Count == 1;
                    }
                }
            }

            public byte[] GetByteResult()
            {
                if (isSingle)
                {
                    return DoMicrosoftDeflateData(blockList[0], CompressionMode.Decompress);
                }
                else
                {
                    LinkedList<byte[]> realList = new LinkedList<byte[]>();

                    for (int i = 1; i < blockList.Count; i++)
                    {
                        realList.AddLast(blockList[i]);
                    }
                    realList.AddLast(blockList[0]);

                    LinkedList<byte[]> realBlockList = new LinkedList<byte[]>();

                    foreach (var block in realList)
                    {
                        realBlockList.AddLast(DoMicrosoftDeflateData(block, CompressionMode.Decompress));
                    }

                    return realBlockList.SelectMany(b => b).ToArray();
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }
}
