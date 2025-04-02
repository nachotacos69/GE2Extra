using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace RES_UNPACKER
{
    class UnpackRES
    {
        private readonly byte[] resFileData;
        private readonly string outputBaseFolder;
        private Dictionary<string, int> fileNameCount = new Dictionary<string, int>();
        private HashSet<string> processedFiles = new HashSet<string>();

        public UnpackRES(byte[] resFileData, string resFileName, string outputFolder)
        {
            this.resFileData = resFileData;
            this.outputBaseFolder = outputFolder;
            Directory.CreateDirectory(outputBaseFolder);
            processedFiles.Add(Path.GetFileName(resFileName));
        }

        public void ExtractFile(TOC.TOCEntry entry)
        {
            if (entry.OffsetType == "NoSet (External RDP Exclusion)" || entry.OffsetType == "BIN/MODULE (Or Empty)")
            {
                Console.WriteLine($"Skipping extraction for {entry.Name} ({entry.OffsetType})");
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
                Console.WriteLine($"Error: Data for {entry.Name} exceeds RES file size");
                return;
            }

            byte[] data = new byte[entry.CSize];
            Array.Copy(resFileData, (long)entry.ActualOffset, data, 0, (int)entry.CSize);

            if (data.Length >= 4)
            {
                uint magic = BitConverter.ToUInt32(data, 0);
                byte[] decompressedData = null;

                if (magic == 0x327A6C62) // BLZ2
                {
                    decompressedData = Deflate.DecompressBLZ2(data);
                }
                else if (magic == 0x347A6C62) // BLZ4
                {
                    decompressedData = Deflate.DecompressBLZ4(data);
                }

                if (decompressedData != null)
                {
                    File.WriteAllBytes(outputPath, decompressedData);
                    Console.WriteLine($"Extracted: {relativePath} (decompressed)");
                    return;
                }
                else if (magic == 0x327A6C62 || magic == 0x347A6C62)
                {
                    Console.WriteLine($"Error: Failed to decompress {entry.Name}");
                    return;
                }
            }

            // If not compressed or decompression failed, write raw data
            File.WriteAllBytes(outputPath, data);
            Console.WriteLine($"Extracted: {relativePath}");
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

            using (FileStream fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                if (entry.ActualOffset + entry.CSize > (ulong)fs.Length)
                {
                    Console.WriteLine($"Error: Data for {entry.Name} exceeds RDP file size");
                    return;
                }

                fs.Seek((long)entry.ActualOffset, SeekOrigin.Begin);
                byte[] data = new byte[entry.CSize];
                int bytesRead = fs.Read(data, 0, (int)entry.CSize);

                if (bytesRead != entry.CSize)
                {
                    Console.WriteLine($"Error: Incomplete read for {entry.Name}");
                    return;
                }

                if (data.Length >= 4)
                {
                    uint magic = BitConverter.ToUInt32(data, 0);
                    byte[] decompressedData = null;

                    if (magic == 0x327A6C62) // BLZ2
                    {
                        decompressedData = Deflate.DecompressBLZ2(data);
                    }
                    else if (magic == 0x347A6C62) // BLZ4
                    {
                        decompressedData = Deflate.DecompressBLZ4(data);
                    }

                    if (decompressedData != null)
                    {
                        File.WriteAllBytes(outputPath, decompressedData);
                        Console.WriteLine($"Extracted: {relativePath} (decompressed)");
                        return;
                    }
                    else if (magic == 0x327A6C62 || magic == 0x347A6C62)
                    {
                        Console.WriteLine($"Error: Failed to decompress {entry.Name}");
                        return;
                    }
                }

                // If not compressed or decompression failed, write raw data
                File.WriteAllBytes(outputPath, data);
                Console.WriteLine($"Extracted: {relativePath}");
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

        private void ProcessNestedFile(string filePath, string relativePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            string fileName = Path.GetFileName(filePath);

            if (processedFiles.Contains(fileName))
                return;

            processedFiles.Add(fileName);

            if (extension == ".res")
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                if (fileData.Length < 4 || BitConverter.ToUInt32(fileData, 0) != 0x73657250) // Check "Pres" magic
                    return;

                Console.WriteLine($"Processing nested RES: {relativePath}");
                string nestedOutputFolder = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(fileName));
                PRES nestedPres = new PRES();
                nestedPres.ProcessRES(fileData, fileName, nestedOutputFolder);
            }
            else if (extension == ".rtbl")
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Console.WriteLine($"Processing RTBL: {relativePath}");
                string rtblOutputFolder = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(fileName));
                RTBL rtblProcessor = new RTBL(fileData, rtblOutputFolder);
                List<string> extractedResFiles = rtblProcessor.ProcessRTBL();

                foreach (string resFile in extractedResFiles)
                {
                    string resRelativePath = "." + resFile.Substring(Directory.GetCurrentDirectory().Length);
                    ProcessNestedFile(resFile, resRelativePath);
                }
            }
        }

        public void ProcessAllNestedFiles()
        {
            string[] files = Directory.GetFiles(outputBaseFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".res") || f.EndsWith(".rtbl"))
                .OrderBy(f => f)
                .ToArray();

            foreach (string file in files)
            {
                string relativePath = "." + file.Substring(Directory.GetCurrentDirectory().Length);
                ProcessNestedFile(file, relativePath);
            }
        }
    }
}