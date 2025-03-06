using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace RES_UNPACKER
{
    class RTBL
    {
        private readonly byte[] rtblData;
        private readonly string outputFolder;
        private Dictionary<string, int> fileNameCount = new Dictionary<string, int>();

        public RTBL(byte[] rtblData, string outputFolder)
        {
            this.rtblData = rtblData;
            this.outputFolder = outputFolder;
            Directory.CreateDirectory(outputFolder);
        }

        public List<string> ProcessRTBL()
        {
            List<string> extractedResFiles = new List<string>(); // Track extracted .res files

            using (MemoryStream ms = new MemoryStream(rtblData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                long position = 0;
                while (position + 32 <= rtblData.Length) // Ensure we can read a full 32-byte TOC
                {
                    ms.Position = position;
                    TOC.TOCEntry entry = ReadTOCEntry(reader, position);
                    if (entry != null)
                    {
                        string extractedPath = ExtractRTBLFile(entry);
                        if (extractedPath != null && Path.GetExtension(extractedPath).ToLower() == ".res")
                        {
                            extractedResFiles.Add(extractedPath);
                        }
                        position += 32; // Move to next potential TOC
                    }
                    else
                    {
                        // If no valid TOC entry, move forward and try next position
                        position++;
                       Console.WriteLine($"[DEBUG] Skipped invalid TOC @ 0x{position - 1:X8}");
                    }

                    // Skip zero padding blocks
                    while (position + 16 <= rtblData.Length)
                    {
                        ms.Position = position;
                        bool isZeroes = true;
                        for (int i = 0; i < 16; i++)
                        {
                            if (reader.ReadByte() != 0)
                            {
                                isZeroes = false;
                                break;
                            }
                        }
                        if (isZeroes)
                        {
                            position += 16;
                        }
                        else
                        {
                            break; // Found non-zero data, try reading TOC from here
                        }
                    }
                }
            }
            Console.WriteLine($" [DEBUG] Finished processing RTBL: Extracted all entries.");
            return extractedResFiles;
        }

        private TOC.TOCEntry ReadTOCEntry(BinaryReader reader, long position)
        {
            uint offset = reader.ReadUInt32();
            uint cSize = reader.ReadUInt32();
            uint nameOffset = reader.ReadUInt32(); // Should be 0x20
            uint chunkName = reader.ReadUInt32();
            reader.ReadBytes(12); // Padding
            uint dSize = reader.ReadUInt32();

            if (nameOffset != 0x20)
                return null; // Invalid RTBL TOC

            long namePosition = position + 0x20;
            if (namePosition + 12 > rtblData.Length)
                return null;

            reader.BaseStream.Position = namePosition;
            reader.ReadBytes(12); // Skip 12 bytes (0xC) of name structure offsets

            string name = ReadString(reader, reader.BaseStream.Position);
            string type = null;
            if (name != null)
            {
                long typePosition = reader.BaseStream.Position;
                type = ReadString(reader, typePosition);
            }

            if (string.IsNullOrEmpty(name))
                return null;

            TOC.TOCEntry entry = new TOC.TOCEntry(offset, cSize, nameOffset, chunkName, dSize);
            entry.SetNames(name, type);
            Console.WriteLine($"[DEBUG] TAG @ 0x{position:X8}: Name: {name}, Type: {type}");
            return entry;
        }

        private string ReadString(BinaryReader reader, long offset)
        {
            if (offset >= rtblData.Length)
                return null;

            reader.BaseStream.Position = offset;
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0 && reader.BaseStream.Position < rtblData.Length)
            {
                if (!IsValidFileNameChar((char)b))
                    return null; // Skip invalid filenames
                bytes.Add(b);
            }
            return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : null;
        }

        private bool IsValidFileNameChar(char c)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return !Array.Exists(invalidChars, invalid => invalid == c);
        }

        private string ExtractRTBLFile(TOC.TOCEntry entry)
        {
            string baseFileName = entry.Type != null ? $"{entry.Name}.{entry.Type}" : entry.Name;
            string fullPath = Path.Combine(outputFolder, baseFileName);
            fullPath = HandleDuplicateFileName(fullPath);
            string relativePath = "." + fullPath.Substring(Directory.GetCurrentDirectory().Length);

            byte enumerator = (byte)(entry.Offset >> 28);
            ulong actualOffset = entry.Offset & 0x0FFFFFFF;

            switch (enumerator)
            {
                case 0x0: // BIN/MODULE, external file, skip
                    Console.WriteLine($"[DEBUG] Skipping extraction for {entry.Name} (External file)");
                    return null;
                case 0x4:
                case 0x5:
                case 0x6: // RDP files
                    actualOffset *= 0x800;
                    ExtractFromRDP(entry, fullPath, relativePath, actualOffset);
                    return fullPath;
                case 0xC:
                case 0xD: // Current file
                    ExtractFromCurrent(entry, fullPath, relativePath, actualOffset);
                    return fullPath;
                default:
                    Console.WriteLine($"[DEBUG] Skipping extraction for {entry.Name} (Unsupported offset type: 0x{enumerator:X})");
                    return null;
            }
        }

        private string HandleDuplicateFileName(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            string uniquePath = fullPath;

            if (File.Exists(fullPath))
            {
                if (!fileNameCount.ContainsKey(fullPath))
                    fileNameCount[fullPath] = 0;

                int count = ++fileNameCount[fullPath];
                uniquePath = Path.Combine(directory, $"{fileName}_{count:0000}{extension}");
            }

            return uniquePath;
        }

        private void ExtractFromCurrent(TOC.TOCEntry entry, string outputPath, string relativePath, ulong actualOffset)
        {
            if (actualOffset + entry.CSize > (ulong)rtblData.Length)
            {
                Console.WriteLine($"[DEBUG] Error: Data for {entry.Name} exceeds RTBL file size");
                return;
            }

            byte[] data = new byte[entry.CSize];
            Array.Copy(rtblData, (long)actualOffset, data, 0, (int)entry.CSize);

            if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x327A6C62) // blz2
            {
                byte[] decompressedData = Deflate.DecompressBLZ2(data);
                if (decompressedData != null)
                {
                    File.WriteAllBytes(outputPath, decompressedData);
                    Console.WriteLine($"[DEBUG] Extracted: {relativePath} (decompressed)");
                }
                else
                {
                    Console.WriteLine($"Error: Failed to decompress {entry.Name}");
                }
            }
            else
            {
                File.WriteAllBytes(outputPath, data);
                Console.WriteLine($"[DEBUG] Extracted: {relativePath}");
            }
        }

        private void ExtractFromRDP(TOC.TOCEntry entry, string outputPath, string relativePath, ulong actualOffset)
        {
            string rdpFileName = GetRDPFileName(entry.Offset);
            string rdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFileName);

            if (!File.Exists(rdpPath))
            {
                Console.WriteLine($"[DEBUG] Error: RDP file {rdpFileName} not found for {entry.Name}");
                return;
            }

            using (FileStream fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                if (actualOffset + entry.CSize > (ulong)fs.Length)
                {
                    Console.WriteLine($"[DEBUG] Error: Data for {entry.Name} exceeds RDP file size");
                    return;
                }

                fs.Seek((long)actualOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.CSize];
                int bytesRead = fs.Read(data, 0, (int)entry.CSize);

                if (bytesRead != entry.CSize)
                {
                    Console.WriteLine($"[DEBUG] Error: Incomplete read for {entry.Name}");
                    return;
                }

                if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x327A6C62) // blz2
                {
                    byte[] decompressedData = Deflate.DecompressBLZ2(data);
                    if (decompressedData != null)
                    {
                        File.WriteAllBytes(outputPath, decompressedData);
                        Console.WriteLine($"[DEBUG] Extracted: {relativePath} (decompressed)");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Error: Failed to decompress {entry.Name}");
                    }
                }
                else
                {
                    File.WriteAllBytes(outputPath, data);
                    Console.WriteLine($"[DEBUG] Extracted: {relativePath}");
                }
            }
        }

        private string GetRDPFileName(uint offset)
        {
            byte enumerator = (byte)(offset >> 28);
            switch (enumerator)
            {
                case 0x4: return "package.rdp";
                case 0x5: return "data.rdp";
                case 0x6: return "patch.rdp";
                default: return null;
            }
        }
    }
}