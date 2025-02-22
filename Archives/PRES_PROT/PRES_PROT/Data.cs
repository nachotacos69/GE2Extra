using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PRES_PROT
{
    public class Data
    {
        public static void ExtractFiles(string inputFilePath, List<TOCEntry> tocEntries)
        {
            string outputFolder = Path.Combine(Path.GetDirectoryName(inputFilePath), Path.GetFileNameWithoutExtension(inputFilePath));
            Directory.CreateDirectory(outputFolder);

            using (FileStream inputFile = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(inputFile))
            {
                foreach (var entry in tocEntries)
                {
                    if (!string.IsNullOrEmpty(entry.NoSetPath))
                    {
                        HandleNoSetPath(entry, outputFolder);
                        continue;
                    }

                    if (entry.TOC_OFF == 0 || entry.TOC_CSIZE == 0)
                    {
                        GenerateEmptyFile(outputFolder, entry);
                        continue;
                    }

                    string filePath = GenerateFilePath(outputFolder, entry);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    if (entry.IsRDP)
                    {
                        ExtractFromRDP(entry, filePath);
                    }
                    else
                    {
                        ExtractFromCurrentFile(reader, entry, filePath);
                    }
                }
            }

            ProcessNestedRESFiles(outputFolder);
        }

        private static void ExtractFromCurrentFile(BinaryReader reader, TOCEntry entry, string filePath)
        {
            reader.BaseStream.Seek(entry.AbsoluteOffset, SeekOrigin.Begin);
            byte[] data = reader.ReadBytes((int)entry.TOC_CSIZE);

            // Check if the file has a BLZ2 header
            if (data.Length >= 4 && BLZ.IsBLZ2(data))
            {
                Console.WriteLine($"Decompressing BLZ2 file: {filePath}");
                data = BLZ.DecompressBLZ2(data);
            }
            // Check if the file has a BLZ4 header
            else if (data.Length >= 4 && BLZ4.IsBLZ4(data))
            {
                Console.WriteLine($"Decompressing BLZ4 file: {filePath}");
                data = BLZ4.DecompressBLZ4(data);
            }

            File.WriteAllBytes(filePath, data);
            Console.WriteLine($"Extracted: {filePath}");
        }


        private static void ExtractFromRDP(TOCEntry entry, string filePath)
        {
            string scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string rdpFilePath = Path.Combine(scriptDirectory, entry.RDPFileName);

            if (!File.Exists(rdpFilePath))
            {
                Console.WriteLine($"Missing RDP file: {rdpFilePath}");
                return;
            }

            using (FileStream rdpStream = new FileStream(rdpFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader rdpReader = new BinaryReader(rdpStream))
            {
                rdpReader.BaseStream.Seek(entry.RDPAbsoluteOffset, SeekOrigin.Begin);
                byte[] data = rdpReader.ReadBytes((int)entry.TOC_CSIZE);

                File.WriteAllBytes(filePath, data);
                Console.WriteLine($"Extracted from RDP: {filePath}");
            }
        }

        private static void GenerateEmptyFile(string outputFolder, TOCEntry entry)
        {
            string filePath = GenerateFilePath(outputFolder, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            File.WriteAllBytes(filePath, Array.Empty<byte>());
            Console.WriteLine($"Generated empty file: {filePath}");
        }

        private static string GenerateFilePath(string outputFolder, TOCEntry entry)
        {
            string subPath = entry.Path ?? "";

            // Apply Path2 if it exists
            if (!string.IsNullOrEmpty(entry.Path2))
            {
                string lastPathPart = Path.GetFileName(entry.Path2); // Get last part of Path2
                string expectedFileName = string.IsNullOrEmpty(entry.Type) ? entry.Name : $"{entry.Name}.{entry.Type}";

                // If the last part of Path2 matches Name + Type, treat it as a file and avoid extra nesting
                if (string.Equals(lastPathPart, expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    subPath = Path.GetDirectoryName(entry.Path2) ?? ""; // Use only the directory part of Path2
                }
                else
                {
                    subPath = string.IsNullOrEmpty(entry.Path) ? entry.Path2 : Path.Combine(entry.Path2, entry.Path);
                }
            }

            string fileName = string.IsNullOrEmpty(entry.Type) ? entry.Name : $"{entry.Name}.{entry.Type}";
            string fullPath = Path.Combine(outputFolder, subPath, fileName);

            return HandleDuplicateFileName(fullPath);
        }


        private static void HandleNoSetPath(TOCEntry entry, string outputFolder)
        {
            string actualPath = entry.NoSetPath.StartsWith("PATH=") ? entry.NoSetPath.Substring(5) : entry.NoSetPath;
            string baseDirectory = "."; // Search in the main directory
            string sourcePath = Path.Combine(baseDirectory, actualPath);

            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"NoSetPath file not found: {sourcePath}");
                return;
            }

            string destinationDirectory = !string.IsNullOrEmpty(entry.Path2)
                ? Path.Combine(outputFolder, entry.Path2, Path.GetDirectoryName(actualPath))
                : Path.Combine(outputFolder, Path.GetDirectoryName(actualPath));

            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(actualPath));

            File.Copy(sourcePath, destinationPath, true);
            Console.WriteLine($"Copied NoSetPath file: {destinationPath}");
        }

        private static void ProcessNestedRESFiles(string outputFolder)
        {
            List<string> resFiles = Directory.GetFiles(outputFolder, "*.res", SearchOption.AllDirectories)
                                             .OrderBy(f => f)
                                             .ToList();

            foreach (string resFile in resFiles)
            {
                Console.WriteLine($"Processing nested RES file: {resFile}");
                PRES.ProcessFile(resFile);
            }
        }

        private static string HandleDuplicateFileName(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            int counter = 0;
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileName}_{counter:D4}{extension}");
                counter++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }

    public class TOCEntry
    {
        public uint TOC_OFF;
        public uint TOC_CSIZE;
        public string Name;
        public string Type;
        public string Path;
        public string Path2;
        public string NoSetPath;
        public uint AbsoluteOffset;
        public bool IsRDP;
        public string RDPFileName;
        public uint RDPAbsoluteOffset;
    }
}
