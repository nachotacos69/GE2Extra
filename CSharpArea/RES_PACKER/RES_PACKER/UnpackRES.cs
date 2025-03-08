using System;
using System.IO;
using System.Collections.Generic;

namespace RES_PACKER
{
    public class UnpackRES
    {
        private readonly byte[] resFileData;
        private readonly string outputBaseFolder;
        private readonly DataHelper dataHelper;
        private Dictionary<string, int> fileNameCount = new Dictionary<string, int>();

        public UnpackRES(byte[] resFileData, string resFileName, string outputFolder, DataHelper dataHelper)
        {
            this.resFileData = resFileData;
            this.outputBaseFolder = outputFolder;
            this.dataHelper = dataHelper;
            Directory.CreateDirectory(outputBaseFolder);
        }

        public void ExtractFile(TOC.TOCEntry entry)
        {
            // Check for dummy entry
            bool isDummy = entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize > 0;
            if (isDummy)
            {
                Console.WriteLine($"Detected dummy entry: DSize = {entry.DSize}");
                dataHelper.AddDummyFileEntry(entry); // Add dummy to JSON
                return;
            }

            // Skip extraction for external/no-set entries, but still add to JSON
            if (entry.OffsetType == "NoSet (External RDP Exclusion)" || entry.OffsetType == "BIN/MODULE (External File)")
            {
                Console.WriteLine($"Skipping extraction for {entry.Name} ({entry.OffsetType}), but adding to JSON");
                dataHelper.AddFileEntry(entry, null, false); // No file path, not compressed
                return;
            }

            string fileName = GenerateFileName(entry);
            string fullPath = GenerateFullPath(entry, fileName);
            fullPath = HandleDuplicateFileName(fullPath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string relativePath = "." + fullPath.Substring(Directory.GetCurrentDirectory().Length);

            if (entry.OffsetType.StartsWith("RDP"))
            {
                ExtractFromRDP(entry, fullPath, relativePath);
            }
            else if (entry.OffsetType == "Current RES File")
            {
                ExtractFromCurrent(entry, fullPath, relativePath);
            }
            else
            {
                Console.WriteLine($"No extraction method for {entry.OffsetType}");
            }
        }

        private string GenerateFileName(TOC.TOCEntry entry)
        {
            if (entry.Type != null)
                return $"{entry.Name}.{entry.Type}";
            return entry.Name;
        }

        private string GenerateFullPath(TOC.TOCEntry entry, string fileName)
        {
            if (entry.SubPath != null)
            {
                string[] subPathParts = entry.SubPath.Split('/');
                string lastPart = subPathParts[subPathParts.Length - 1];
                if (lastPart == fileName)
                {
                    return Path.Combine(outputBaseFolder, entry.SubPath);
                }
                return Path.Combine(outputBaseFolder, entry.SubPath, fileName);
            }
            else if (entry.Path != null)
            {
                return Path.Combine(outputBaseFolder, entry.Path, fileName);
            }
            return Path.Combine(outputBaseFolder, fileName);
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

        private void ExtractFromCurrent(TOC.TOCEntry entry, string outputPath, string relativePath)
        {
            if (entry.ActualOffset + entry.CSize > (ulong)resFileData.Length)
            {
                Console.WriteLine($"Error: Data for {entry.Name} exceeds RES file size (Offset: 0x{entry.ActualOffset:X8}, CSize: {entry.CSize})");
                return;
            }

            byte[] data = new byte[entry.CSize];
            Array.Copy(resFileData, (long)entry.ActualOffset, data, 0, (int)entry.CSize);

            bool isCompressed = data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Deflate.BLZ2_MAGIC;

            if (isCompressed)
            {
                byte[] decompressedData = Deflate.DecompressBLZ2(data);
                if (decompressedData != null)
                {
                    File.WriteAllBytes(outputPath, decompressedData);
                    Console.WriteLine($"Extracted: {relativePath} (decompressed)");
                    dataHelper.AddFileEntry(entry, outputPath, true);
                }
                else
                {
                    Console.WriteLine($"Error: Failed to decompress {entry.Name}");
                }
            }
            else
            {
                File.WriteAllBytes(outputPath, data);
                Console.WriteLine($"Extracted: {relativePath}");
                dataHelper.AddFileEntry(entry, outputPath, false);
            }
        }

        private void ExtractFromRDP(TOC.TOCEntry entry, string outputPath, string relativePath)
        {
            string rdpFileName = GetRDPFileName(entry.OffsetType);
            string rdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFileName);

            if (!File.Exists(rdpPath))
            {
                Console.WriteLine($"Error: RDP file {rdpFileName} not found for {entry.Name}");
                return;
            }

            uint actualOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
            Console.WriteLine($"Debug: {entry.Name} - Original Offset: 0x{entry.Offset:X8}, Actual RDP Offset: 0x{actualOffset:X8}, CSize: {entry.CSize}");

            using (FileStream fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                if (actualOffset + entry.CSize > (ulong)fs.Length)
                {
                    Console.WriteLine($"Error: Data for {entry.Name} exceeds RDP file size (Offset: 0x{actualOffset:X8}, CSize: {entry.CSize}, File Length: {fs.Length})");
                    return;
                }

                fs.Seek((long)actualOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.CSize];
                int bytesRead = fs.Read(data, 0, (int)entry.CSize);

                if (bytesRead != entry.CSize)
                {
                    Console.WriteLine($"Error: Incomplete read for {entry.Name} (Expected: {entry.CSize}, Read: {bytesRead})");
                    return;
                }

                bool isCompressed = data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Deflate.BLZ2_MAGIC;

                if (isCompressed)
                {
                    byte[] decompressedData = Deflate.DecompressBLZ2(data);
                    if (decompressedData != null)
                    {
                        File.WriteAllBytes(outputPath, decompressedData);
                        Console.WriteLine($"Extracted: {relativePath} (decompressed)");
                        dataHelper.AddFileEntry(entry, outputPath, true);
                    }
                    else
                    {
                        Console.WriteLine($"Error: Failed to decompress {entry.Name}");
                    }
                }
                else
                {
                    File.WriteAllBytes(outputPath, data);
                    Console.WriteLine($"Extracted: {relativePath}");
                    dataHelper.AddFileEntry(entry, outputPath, false);
                }
            }
        }

        private string GetRDPFileName(string offsetType)
        {
            switch (offsetType)
            {
                case "RDP Package File": return "package.rdp";
                case "RDP Data File": return "data.rdp";
                case "RDP Patch File": return "patch.rdp";
                default: return null;
            }
        }
    }
}