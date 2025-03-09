using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace RES_PACKER
{
    public class UnpackRES
    {
        private readonly byte[] resFileData;
        private readonly string resFileName;
        private readonly string outputBaseFolder; // Parent folder where the .res file resides
        private readonly string contentFolder; // Subfolder for this .res file's contents
        private readonly DataHelper dataHelper;
        private Dictionary<string, int> fileNameCount = new Dictionary<string, int>();
        private static List<string> resFileLocations = new List<string>();
        private static HashSet<string> processedResFiles = new HashSet<string>(); // Track processed .res files

        public UnpackRES(byte[] resFileData, string resFileName, string outputFolder, DataHelper dataHelper)
        {
            this.resFileData = resFileData;
            this.resFileName = resFileName;
            this.outputBaseFolder = Path.GetDirectoryName(resFileName); // Use parent directory of .res file
            this.dataHelper = dataHelper;

            // Content folder is only the outputFolder for the root .res, otherwise use the existing folder
            string resFileNameWithoutExt = Path.GetFileNameWithoutExtension(resFileName);
            this.contentFolder = outputFolder.EndsWith(resFileNameWithoutExt, StringComparison.OrdinalIgnoreCase)
                ? outputFolder
                : Path.Combine(outputFolder, resFileNameWithoutExt);
            Directory.CreateDirectory(contentFolder);

            string relativePath = GetRelativePath(resFileName);
            if (!resFileLocations.Contains(relativePath))
                resFileLocations.Add(relativePath);
        }

        public void ExtractFile(TOC.TOCEntry entry)
        {
            bool isDummy = entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize > 0;
            if (isDummy) // Fixed from "if (is PUSH)"
            {
                Console.WriteLine($"Detected dummy entry: DSize = {entry.DSize}");
                dataHelper.AddDummyFileEntry(entry);
                return;
            }

            if (entry.OffsetType == "NoSet (External RDP Exclusion)" || entry.OffsetType == "BIN/MODULE (External File)")
            {
                Console.WriteLine($"Skipping extraction for {entry.Name} ({entry.OffsetType}), but adding to JSON");
                dataHelper.AddFileEntry(entry, null, false);
                return;
            }

            string fileName = GenerateFileName(entry);
            string fullPath = GenerateFullPath(entry, fileName);
            fullPath = HandleDuplicateFileName(fullPath, entry);

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string relativePath = GetRelativePath(fullPath);
            Console.WriteLine($"Extracting {entry.Name} to {relativePath} (OffsetType: {entry.OffsetType})");

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

            if (Path.GetExtension(fullPath).ToLower() == ".res" && !processedResFiles.Contains(fullPath))
            {
                Console.WriteLine($"Found nested .res file: {relativePath}. Processing...");
                processedResFiles.Add(fullPath); // Mark as processed
                byte[] nestedResData = File.ReadAllBytes(fullPath);
                string nestedOutputFolder = Path.GetDirectoryName(fullPath);
                PRES nestedProcessor = new PRES();
                nestedProcessor.ProcessRES(nestedResData, fullPath, nestedOutputFolder);
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
                    return Path.Combine(contentFolder, entry.SubPath);
                }
                return Path.Combine(contentFolder, entry.SubPath, fileName);
            }
            else if (entry.Path != null)
            {
                return Path.Combine(contentFolder, entry.Path, fileName);
            }
            return Path.Combine(contentFolder, fileName);
        }

        private string HandleDuplicateFileName(string fullPath, TOC.TOCEntry entry)
        {
            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            string uniquePath = fullPath;

            if (File.Exists(fullPath))
            {
                // Check if the existing file has the same content
                byte[] newData = entry.OffsetType.StartsWith("RDP")
                    ? GetRDPData(entry)
                    : GetCurrentData(entry);
                if (newData != null && File.Exists(fullPath))
                {
                    byte[] existingData = File.ReadAllBytes(fullPath);
                    if (AreByteArraysEqual(newData, existingData))
                    {
                        Console.WriteLine($"Skipping duplicate extraction for {fullPath} (identical content)");
                        return fullPath; // No need to overwrite or create a duplicate
                    }
                }

                if (!fileNameCount.ContainsKey(fullPath))
                    fileNameCount[fullPath] = 0;

                int count = ++fileNameCount[fullPath];
                uniquePath = Path.Combine(directory, $"{fileName}_{count:0000}{extension}");
            }

            return uniquePath;
        }

        private byte[] GetCurrentData(TOC.TOCEntry entry)
        {
            if (entry.ActualOffset + entry.CSize > (ulong)resFileData.Length)
                return null;
            byte[] data = new byte[entry.CSize];
            Array.Copy(resFileData, (long)entry.ActualOffset, data, 0, (int)entry.CSize);
            return data;
        }

        private byte[] GetRDPData(TOC.TOCEntry entry)
        {
            string rdpFileName = GetRDPFileName(entry.OffsetType);
            string rdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFileName);
            if (!File.Exists(rdpPath))
                return null;

            uint actualOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
            using (FileStream fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                if (actualOffset + entry.CSize > (ulong)fs.Length)
                    return null;
                fs.Seek((long)actualOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.CSize];
                fs.Read(data, 0, (int)entry.CSize);
                return data;
            }
        }

        private bool AreByteArraysEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
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

        private string GetRelativePath(string fullPath)
        {
            string currentDir = Directory.GetCurrentDirectory();
            if (fullPath.StartsWith(currentDir))
            {
                int startIndex = currentDir.Length;
                if (startIndex < fullPath.Length)
                    return "." + fullPath.Substring(startIndex);
            }
            return fullPath;
        }

        public static void SaveResIndex()
        {
            string resIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "RESINDEX.json");
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(resFileLocations, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(resIndexPath, json);
            Console.WriteLine($"Saved RESINDEX.json to {resIndexPath}");
        }
    }
}