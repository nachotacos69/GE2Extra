using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace PRES_PROT
{
    class RTBL
    {
        public static void ProcessRTBL(string filePath)
        {
            using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read)))
            {
                Console.WriteLine($"Processing RTBL: {filePath}");
                long fileSize = reader.BaseStream.Length;
                long position = 0;

                string outputFolder = SanitizePath(Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath)));
                Directory.CreateDirectory(outputFolder);

                List<string> extractedResFiles = new List<string>();

                while (position < fileSize)
                {
                    reader.BaseStream.Seek(position, SeekOrigin.Begin);
                    byte[] firstChunk = reader.ReadBytes(16);

                    if (IsPadding(firstChunk))
                    {
                        position += 16;
                        continue;
                    }

                    // Collect TOC data before 16-byte zero padding
                    MemoryStream tocData = new MemoryStream();
                    tocData.Write(firstChunk, 0, 16);

                    while (position + 16 < fileSize)
                    {
                        byte[] nextChunk = reader.ReadBytes(16);
                        if (IsPadding(nextChunk))
                        {
                            position += 16;
                            break;
                        }
                        tocData.Write(nextChunk, 0, 16);
                        position += 16;
                    }

                    byte[] tocBytes = tocData.ToArray();
                    if (tocBytes.Length < 32)
                        continue;

                    using (BinaryReader tocReader = new BinaryReader(new MemoryStream(tocBytes)))
                    {
                        /* same 
                         */
                        uint RTOC_OFF = BitConverter.ToUInt32(tocReader.ReadBytes(4), 0);
                        uint RTOC_CSIZE = BitConverter.ToUInt32(tocReader.ReadBytes(4), 0);
                        uint RTOC_NAME = BitConverter.ToUInt32(tocReader.ReadBytes(4), 0);
                        uint RTOC_NAMEVAL = BitConverter.ToUInt32(tocReader.ReadBytes(4), 0);
                        tocReader.BaseStream.Seek(28, SeekOrigin.Begin);
                        uint RTOC_DSIZE = BitConverter.ToUInt32(tocReader.ReadBytes(4), 0);

                        long tagOffset = position - tocBytes.Length;
                        long nameOffset = tagOffset + RTOC_NAME;

                        uint marker = (RTOC_OFF & 0xF0000000) >> 28;
                        uint adjustedOffset = RTOC_OFF & 0x0FFFFFFF;

                        string markerType = "UNK";
                        string rdpFileName = "";
                        if (marker == 0x4) { markerType = "package.rdp"; rdpFileName = "package.rdp"; }
                        else if (marker == 0x5) { markerType = "data.rdp"; rdpFileName = "data.rdp"; }
                        else if (marker == 0x6) { markerType = "patch.rdp"; rdpFileName = "patch.rdp"; }
                        else if (marker == 0x3) { markerType = "NoSet (Existing File, Refer to NoSetPath)"; }
                        else if (marker == 0xC || marker == 0xD) { markerType = "Current File (No Changes)"; }

                        long absoluteOffset = (marker == 0x4 || marker == 0x5 || marker == 0x6) ? adjustedOffset * 0x800 : adjustedOffset;
                        long endOffset = absoluteOffset + RTOC_CSIZE;

                        Console.WriteLine($"[RTBL TAG @ 0x{tagOffset:X}]");

                        string name = "", type = "";
                        if (RTOC_NAMEVAL > 0)
                        {
                            reader.BaseStream.Seek(nameOffset, SeekOrigin.Begin);
                            reader.ReadBytes(12);
                            name = ReadString(reader);
                            type = ReadString(reader);
                        }

                        string fullFileName = name + (string.IsNullOrEmpty(type) ? "" : "." + type);
                        fullFileName = SanitizeFileName(fullFileName);

                        Console.WriteLine($"  -> Name: {fullFileName}");

                        string extractedPath = "";
                        if (!string.IsNullOrEmpty(rdpFileName))
                        {
                            extractedPath = ExtractFromRDP(rdpFileName, absoluteOffset, RTOC_CSIZE, outputFolder, fullFileName);
                        }

                        if (!string.IsNullOrEmpty(extractedPath) && extractedPath.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                        {
                            extractedResFiles.Add(extractedPath);
                        }
                    }
                }

                // Process all extracted .res files
                foreach (string resFile in extractedResFiles)
                {
                    PRES.ProcessFile(resFile);
                }
            }
        }

        private static string ExtractFromRDP(string rdpFile, long offset, uint size, string outputFolder, string fileName)
        {
            string rtblDir = Path.GetDirectoryName(outputFolder);
            string rdpPath = Path.Combine(rtblDir, rdpFile);

            if (!File.Exists(rdpPath))
            {
                rdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFile);
            }

            if (!File.Exists(rdpPath))
            {
                Console.WriteLine($"  -> ERROR: RDP file '{rdpFile}' not found.");
                return "";
            }

            // Ensure valid directory
            Directory.CreateDirectory(outputFolder);

            string outputPath = GetUniqueFileName(Path.Combine(outputFolder, fileName));

            using (FileStream rdpStream = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader rdpReader = new BinaryReader(rdpStream))
            {
                rdpStream.Seek(offset, SeekOrigin.Begin);
                byte[] extractedData = rdpReader.ReadBytes((int)size);

                File.WriteAllBytes(outputPath, extractedData);
                Console.WriteLine($"  --> Extracted: {outputPath}");
                return outputPath;
            }
        }

        private static string GetUniqueFileName(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 0;

            while (File.Exists(filePath))
            {
                counter++;
                string uniqueName = $"{fileName}_{counter:D4}{extension}";
                filePath = Path.Combine(directory, uniqueName);
            }

            return filePath;
        }

        private static bool IsPadding(byte[] data)
        {
            foreach (byte b in data)
            {
                if (b != 0) return false;
            }
            return true;
        }

        private static string ReadString(BinaryReader reader)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        private static string SanitizePath(string path)
        {
            return string.Join("_", path.Split(Path.GetInvalidPathChars()));
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        }
    }
}
