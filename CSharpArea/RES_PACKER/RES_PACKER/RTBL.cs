using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RES_PACKER
{
    public class RTBL
    {
        public class RTBLEntry
        {
            public uint Offset { get; }
            public uint CSize { get; }
            public uint NameOffset { get; }
            public uint ChunkName { get; }
            public uint DSize { get; }
            public ulong ActualOffset => Offset & 0x0FFFFFFF;
            public string OffsetType { get; private set; }
            public ulong EndOffset => ActualOffset + CSize;
            public string OriginalName { get; private set; } // New property for original name
            public string Name { get; private set; } // Modified name for extraction
            public string Type { get; private set; }
            public long TOCOffset { get; }
            public string Filename { get; set; }

            public RTBLEntry(uint offset, uint csize, uint nameOffset, uint chunkName, uint dsize, long tocOffset)
            {
                Offset = offset;
                CSize = csize;
                NameOffset = nameOffset;
                ChunkName = chunkName;
                DSize = dsize;
                TOCOffset = tocOffset;

                byte enumerator = (byte)(Offset >> 28);
                if (enumerator == 0x0) OffsetType = "BIN/MODULE (External File)";
                else if (enumerator == 0x4) OffsetType = "RDP Package File";
                else if (enumerator == 0x5) OffsetType = "RDP Data File";
                else if (enumerator == 0x6) OffsetType = "RDP Patch File";
                else if (enumerator == 0xC || enumerator == 0xD) OffsetType = "Current RES File";
                else OffsetType = "NoSet (External RDP Exclusion)";
            }

            public void SetNames(string originalName, string type, string filename = null)
            {
                OriginalName = originalName; // Store original name
                Name = originalName; // Default to original, will be updated if suffixed
                Type = type;
                Filename = filename;
            }

            public void SetModifiedName(string modifiedName)
            {
                Name = modifiedName; // Update Name for extraction/filename purposes
            }
        }

        private readonly byte[] fileData;
        private readonly string rtblFileName;
        private readonly string outputFolder;
        private readonly DataHelper dataHelper;
        private readonly List<RTBLEntry> entries = new List<RTBLEntry>();

        public RTBL(byte[] fileData, string rtblFileName, string outputFolder, DataHelper dataHelper)
        {
            this.fileData = fileData;
            this.rtblFileName = rtblFileName;
            this.outputFolder = outputFolder;
            this.dataHelper = dataHelper;
        }

        public List<RTBLEntry> ProcessRTBL()
        {
            Dictionary<string, int> nameCount = new Dictionary<string, int>();

            using (MemoryStream ms = new MemoryStream(fileData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                long offset = 0;
                int tocIndex = 0;

                while (offset < ms.Length)
                {
                    if (offset + 16 <= ms.Length && IsZeroBlock(reader, offset, 16))
                    {
                        Console.WriteLine("Skipping padding at 0x{0:X}", offset);
                        offset += 16;
                        continue;
                    }

                    if (offset + 32 > ms.Length)
                    {
                        Console.WriteLine("Not enough data for TOC at 0x{0:X}, ending", offset);
                        break;
                    }

                    Console.WriteLine("Processing TOC #{0} at offset 0x{1:X}", tocIndex++, offset);
                    reader.BaseStream.Position = offset;
                    RTBLEntry entry = ReadRTBLEntry(reader, offset);
                    if (entry == null)
                    {
                        Console.WriteLine("Failed to read TOC at 0x{0:X}, skipping", offset);
                        offset += 32;
                        continue;
                    }

                    if (entry.CSize == 0 && entry.Offset == 0 && entry.NameOffset == 0)
                    {
                        Console.WriteLine("Invalid TOC entry at 0x{0:X} (all zeros), skipping", offset);
                        offset += 32;
                        continue;
                    }

                    if (ProcessNameData(reader, entry, nameCount))
                    {
                        entries.Add(entry);
                        PrintRTBLEntry(entries.Count, entry);
                        ExtractRTBLEntry(entry);
                    }
                    else
                    {
                        Console.WriteLine("Failed to process name data for TOC at 0x{0:X}", offset);
                    }

                    offset += 32;
                    while (offset + 16 <= ms.Length)
                    {
                        if (IsZeroBlock(reader, offset, 16))
                        {
                            Console.WriteLine("Found end padding at 0x{0:X}", offset);
                            offset += 16;
                            break;
                        }
                        offset += 16;
                    }
                }
            }

            SaveRTBLToJson();
            return entries;
        }

        public static void ProcessRTBLFromIndex(string outputFolder, DataHelper dataHelper)
        {
            Console.WriteLine("Starting RTBL processing from RESINDEX.json...");
            string resIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "RESINDEX.json");
            if (!File.Exists(resIndexPath))
            {
                Console.WriteLine("Error: RESINDEX.json not found at {0}", resIndexPath);
                return;
            }

            string jsonContent = File.ReadAllText(resIndexPath);
            var indexData = JsonConvert.DeserializeAnonymousType(jsonContent, new { RTBL_LIST = new List<string>() });
            if (indexData?.RTBL_LIST == null || indexData.RTBL_LIST.Count == 0)
            {
                Console.WriteLine("Warning: No RTBL_LIST found in RESINDEX.json");
                return;
            }

            Console.WriteLine("Found {0} RTBL files in RESINDEX.json", indexData.RTBL_LIST.Count);
            foreach (string rtblRelativePath in indexData.RTBL_LIST)
            {
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), rtblRelativePath.TrimStart('.', '\\'));
                string rtblOutputFolder = Path.Combine(Path.GetDirectoryName(fullPath), Path.GetFileNameWithoutExtension(fullPath));

                if (!File.Exists(fullPath))
                {
                    Console.WriteLine("Error: RTBL file not found at {0}", fullPath);
                    continue;
                }

                byte[] fileData;
                try
                {
                    fileData = File.ReadAllBytes(fullPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading RTBL file {0}: {1}", rtblRelativePath, ex.Message);
                    continue;
                }

                Console.WriteLine("Processing RTBL file: {0} into folder: {1} (Data length: {2} bytes)", rtblRelativePath, rtblOutputFolder, fileData.Length);
                Directory.CreateDirectory(rtblOutputFolder);
                RTBL rtblProcessor = new RTBL(fileData, fullPath, rtblOutputFolder, dataHelper);
                rtblProcessor.ProcessRTBL();
            }

            Console.WriteLine("Finished processing RTBL files from RESINDEX.json");
        }

        private void ExtractRTBLEntry(RTBLEntry entry)
        {
            string fileName = $"{entry.Name}.{entry.Type}"; // Use modified Name here
            string fullPath = Path.Combine(outputFolder, fileName);
            string relativePath = GetRelativePath(fullPath);

            byte[] extractedData = null;
            if (entry.OffsetType.StartsWith("RDP"))
            {
                extractedData = ExtractFromRDP(entry, fullPath, relativePath);
            }
            else if (entry.OffsetType == "Current RES File")
            {
                extractedData = ExtractFromCurrent(entry, fullPath, relativePath);
            }
            else
            {
                Console.WriteLine("No extraction method for {0}", entry.OffsetType);
                return;
            }

            if (extractedData != null)
            {
                WriteFile(extractedData, fullPath, relativePath, entry);
                entry.Filename = relativePath; // Update Filename after extraction
                if (Path.GetExtension(fullPath).ToLower() == ".res")
                {
                    lock (UnpackRES.resFiles)
                    {
                        if (!UnpackRES.resFiles.Contains(relativePath))
                            UnpackRES.resFiles.Add(relativePath);
                    }
                }
            }
            else
            {
                Console.WriteLine("Warning: Failed to extract data for {0}", relativePath);
            }
        }

        private byte[] ExtractFromCurrent(RTBLEntry entry, string outputPath, string relativePath)
        {
            if (entry.ActualOffset + entry.CSize > (ulong)fileData.Length)
            {
                Console.WriteLine("Error: Data exceeds RTBL file size (Offset: 0x{0:X8}, CSize: {1})", entry.ActualOffset, entry.CSize);
                return null;
            }

            byte[] data = new byte[entry.CSize];
            Array.Copy(fileData, (long)entry.ActualOffset, data, 0, (int)entry.CSize);
            return data;
        }

        private byte[] ExtractFromRDP(RTBLEntry entry, string outputPath, string relativePath)
        {
            byte enumerator = (byte)(entry.Offset >> 28);
            string rdpFileName = null;

            if (enumerator == 0x4) rdpFileName = "package.rdp";
            else if (enumerator == 0x5) rdpFileName = "data.rdp";
            else if (enumerator == 0x6) rdpFileName = "patch.rdp";

            if (rdpFileName == null) return null;

            string rdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFileName);
            if (!File.Exists(rdpPath))
            {
                Console.WriteLine("Error: RDP file {0} not found", rdpFileName);
                return null;
            }

            uint actualOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
            using (FileStream fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                if (actualOffset + entry.CSize > (ulong)fs.Length)
                {
                    Console.WriteLine("Error: Data exceeds RDP file size (Offset: 0x{0:X8}, CSize: {1}, File Length: {2})", actualOffset, entry.CSize, fs.Length);
                    return null;
                }

                fs.Seek((long)actualOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.CSize];
                fs.Read(data, 0, (int)entry.CSize);
                return data;
            }
        }

        private void WriteFile(byte[] data, string outputPath, string relativePath, RTBLEntry entry)
        {
            if (data == null || string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("Error: Invalid data or output path for {0}", relativePath);
                return;
            }

            bool isCompressed = data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Deflate.BLZ2_MAGIC;
            if (isCompressed)
            {
                byte[] decompressedData = Deflate.DecompressBLZ2(data);
                if (decompressedData != null)
                {
                    File.WriteAllBytes(outputPath, decompressedData);
                    Console.WriteLine("Extracted: {0} (decompressed, {1} bytes)", relativePath, decompressedData.Length);
                    dataHelper.AddFileEntry(entry, outputPath, true);
                }
                else
                {
                    Console.WriteLine("Error: Failed to decompress data for {0}", relativePath);
                }
            }
            else
            {
                File.WriteAllBytes(outputPath, data);
                Console.WriteLine("Extracted: {0} ({1} bytes)", relativePath, data.Length);
                dataHelper.AddFileEntry(entry, outputPath, false);
            }
        }

        private void SaveRTBLToJson()
        {
            string jsonFileName = Path.Combine(Path.GetDirectoryName(rtblFileName), Path.GetFileNameWithoutExtension(rtblFileName) + ".json");
            var rtblData = new
            {
                Filename = GetRelativePath(rtblFileName),
                Entries = entries.Select(e => new
                {
                    Offset = e.Offset,
                    CSize = e.CSize,
                    NameOffset = e.NameOffset,
                    ChunkName = e.ChunkName,
                    DSize = e.DSize,
                    OffsetType = e.OffsetType,
                    Name = e.OriginalName, // Use OriginalName in JSON
                    Type = e.Type,
                    TOCOffset = e.TOCOffset,
                    Filename = e.Filename
                }).ToList()
            };
            string json = JsonConvert.SerializeObject(rtblData, Formatting.Indented);
            File.WriteAllText(jsonFileName, json);
            Console.WriteLine($"Saved RTBL data to {jsonFileName}");
        }

        private RTBLEntry ReadRTBLEntry(BinaryReader reader, long tocOffset)
        {
            try
            {
                uint offset = reader.ReadUInt32();
                uint csize = reader.ReadUInt32();
                uint nameOffset = reader.ReadUInt32();
                uint chunkName = reader.ReadUInt32();
                reader.ReadBytes(12);
                uint dsize = reader.ReadUInt32();
                return new RTBLEntry(offset, csize, nameOffset, chunkName, dsize, tocOffset);
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        private bool ProcessNameData(BinaryReader reader, RTBLEntry entry, Dictionary<string, int> nameCount)
        {
            long nameStart = entry.TOCOffset + entry.NameOffset;
            if (nameStart + 12 >= fileData.Length)
            {
                Console.WriteLine("Name offset 0x{0:X} out of bounds (file size: 0x{1:X})", nameStart, fileData.Length);
                return false;
            }

            reader.BaseStream.Position = nameStart + 12;
            string originalName = ReadString(reader);
            if (string.IsNullOrEmpty(originalName) || ContainsInvalidPathChars(originalName))
            {
                Console.WriteLine("Invalid or empty name '{0}' at offset 0x{1:X}", originalName ?? "", nameStart + 12);
                return false;
            }

            string type = ReadString(reader);
            if (string.IsNullOrEmpty(type))
                type = "res";
            else if (ContainsInvalidPathChars(type))
            {
                Console.WriteLine("Invalid type '{0}' at offset 0x{1:X}", type, reader.BaseStream.Position);
                return false;
            }

            // Set original name and type
            entry.SetNames(originalName, type);

            // Handle duplicate names for extraction
            string modifiedName = originalName;
            if (nameCount.ContainsKey(originalName))
            {
                nameCount[originalName]++;
                modifiedName = string.Format("{0}_{1:0000}", originalName, nameCount[originalName]);
                entry.SetModifiedName(modifiedName); // Update Name for extraction
            }
            else
            {
                nameCount[originalName] = 0;
            }

            return true;
        }

        private bool ContainsInvalidPathChars(string text)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return text.Any(c => invalidChars.Contains(c));
        }

        private bool IsZeroBlock(BinaryReader reader, long offset, int length)
        {
            if (offset + length > reader.BaseStream.Length)
                return false;

            reader.BaseStream.Position = offset;
            for (int i = 0; i < length; i++)
                if (reader.ReadByte() != 0)
                    return false;
            return true;
        }

        private string ReadString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte b = reader.ReadByte();
                if (b == 0) break;
                bytes.Add(b);
            }
            return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : null;
        }

        private void PrintRTBLEntry(int index, RTBLEntry entry)
        {
            Console.WriteLine("[TAGGED @ 0x{0:X}]", entry.TOCOffset);
            Console.WriteLine("RTBL Entry {0}:", index);
            Console.WriteLine("  Original Name: {0}", entry.OriginalName); // Display original name
            Console.WriteLine("  Modified Name: {0}", entry.Name);       // Display modified name
            Console.WriteLine("  Type: {0}", entry.Type);
            Console.WriteLine("  Offset Type: {0}", entry.OffsetType);
            Console.WriteLine("  Actual Offset: 0x{0:X8}", entry.ActualOffset);
            Console.WriteLine("  CSize: {0}", entry.CSize);
            Console.WriteLine("  DSize: {0}", entry.DSize);
            if (!string.IsNullOrEmpty(entry.Filename))
                Console.WriteLine("  Filename: {0}", entry.Filename);
        }

        private string GetRelativePath(string fullPath)
        {
            string currentDir = Directory.GetCurrentDirectory();
            if (fullPath.StartsWith(currentDir))
            {
                int startIndex = currentDir.Length;
                if (startIndex < fullPath.Length)
                    return ".\\" + fullPath.Substring(startIndex).Replace("/", "\\");
            }
            return fullPath.Replace("/", "\\");
        }
    }
}