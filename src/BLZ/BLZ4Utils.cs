// original by haojun

using ComponentAce.Compression.Libs.zlib;
using System;
using System.Collections.Generic;
using System.IO;

namespace PRES_AREA.BLZ4
{
    public static class BLZ4Utils
    {
        public static readonly int BLZ4_HEADER = 0x347a6c62;

        public static bool IsBLZ4(byte[] data)
        {
            if (data.Length < 4) return false;
            return BitConverter.ToInt32(data, 0) == BLZ4_HEADER;
        }

        public static byte[] UnpackBLZ4Data(byte[] blz4File)
        {
            using (MemoryStream inputMs = new MemoryStream(blz4File))
            using (BinaryReader inputBr = new BinaryReader(inputMs))
            {
                int magic = inputBr.ReadInt32();
                if (magic != BLZ4_HEADER)
                    throw new FileLoadException($"Invalid BLZ4 header: {magic:X8}");

                int unpackedSize = inputBr.ReadInt32();
                inputBr.ReadInt64(); // Skip zero padding
                byte[] md5 = inputBr.ReadBytes(16);

                List<byte[]> blockList = new List<byte[]>();
                bool isUncompressed = false;

                while (inputBr.BaseStream.Position < blz4File.Length)
                {
                    int chunkSize = inputBr.ReadUInt16();
                    if (chunkSize == 0)
                    {
                        blockList.Add(inputBr.ReadBytes((int)(blz4File.Length - inputBr.BaseStream.Position)));
                        isUncompressed = true;
                    }
                    else
                    {
                        blockList.Add(inputBr.ReadBytes(chunkSize));
                    }
                }

                if (isUncompressed) return blockList[0];

                List<byte> result = new List<byte>();
                foreach (var block in blockList)
                {
                    byte[] decompressed;
                    DecompressData(block, out decompressed);
                    result.AddRange(decompressed);
                }

                return result.ToArray();
            }
        }

        private static void DecompressData(byte[] input, out byte[] output)
        {
            using (MemoryStream outMs = new MemoryStream())
            using (ZOutputStream outZStream = new ZOutputStream(outMs))
            using (MemoryStream inMs = new MemoryStream(input))
            {
                CopyStream(inMs, outZStream);
                outZStream.finish();
                output = outMs.ToArray();
            }
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[1024];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }
    }
}
