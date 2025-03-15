using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace RES_PACKER
{
    public class PackRES_EX
    {
        private readonly string inputFile;
        private readonly string outputFolder;
        private readonly string jsonFile;
        private readonly string repackedFile;
        private readonly string tempFile;
        private readonly Dictionary<string, List<(long StartOffset, long EndOffset)>> usedOffsets;
        private readonly Dictionary<(string RdpFile, long OriginalOffset, string ContentHash), long> processedRdpFiles;
        private readonly string rdpInputRoot;  // Default input location for RDP files (.\)
        private readonly string rdpOutputRoot; // Output location for RDP files (.\repacked)

        public PackRES_EX(string inputFile, string outputFolder, Dictionary<string, List<(long StartOffset, long EndOffset)>> sharedOffsets)
        {
            this.inputFile = inputFile;
            this.outputFolder = outputFolder;
            this.jsonFile = Path.ChangeExtension(inputFile, ".json");
            this.repackedFile = Path.Combine(outputFolder, Path.GetFileName(inputFile));
            this.tempFile = Path.Combine(outputFolder, $"temp_{Path.GetFileName(inputFile)}");
            this.usedOffsets = sharedOffsets;
            this.processedRdpFiles = new Dictionary<(string, long, string), long>();
            this.rdpInputRoot = Directory.GetCurrentDirectory(); // Default to .\ for reading original RDP files
            this.rdpOutputRoot = Path.Combine(rdpInputRoot, "repacked"); // Default to .\repacked for writing
            Directory.CreateDirectory(rdpOutputRoot); // Ensure repacked folder exists
        }

        public void Pack()
        {
            Console.WriteLine($"[Debug] Starting packing for '{inputFile}' into '{repackedFile}' via temp file '{tempFile}'");

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file '{inputFile}' is missing. Packing aborted.");
                return;
            }

            if (!File.Exists(jsonFile))
            {
                if (inputFile.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Warning: JSON file '{jsonFile}' not found. Skipping '{inputFile}' as it may be unmodified.");
                    return;
                }
                Console.WriteLine($"Error: JSON file '{jsonFile}' not found for RTBL. Packing aborted.");
                return;
            }

            Directory.CreateDirectory(outputFolder);

            try
            {
                using (var sourceStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var tempStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    sourceStream.CopyTo(tempStream);
                }
                Console.WriteLine($"[Debug] Copied '{inputFile}' to temporary file '{tempFile}'");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error copying '{inputFile}' to '{tempFile}': {ex.Message}");
                return;
            }

            dynamic jsonData = JsonConvert.DeserializeObject(File.ReadAllText(jsonFile));
            Console.WriteLine($"[Debug] Loaded JSON data from '{jsonFile}'");

            if (inputFile.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
            {
                ProcessRES(jsonData);
            }
            else if (inputFile.EndsWith(".rtbl", StringComparison.OrdinalIgnoreCase))
            {
                ProcessRTBL(jsonData);
            }
            else
            {
                Console.WriteLine($"Error: Unsupported file type '{inputFile}'. Packing aborted.");
                CleanupTempFile();
                return;
            }

            try
            {
                if (File.Exists(repackedFile))
                {
                    File.Delete(repackedFile);
                    Console.WriteLine($"[Debug] Deleted existing '{repackedFile}' to allow overwrite");
                }
                File.Move(tempFile, repackedFile);
                Console.WriteLine($"Packing completed. Output saved to '{repackedFile}'.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error replacing '{repackedFile}' with '{tempFile}': {ex.Message}");
                CleanupTempFile();
                return;
            }
        }

        private void CleanupTempFile()
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                    Console.WriteLine($"[Debug] Cleaned up temporary file '{tempFile}'");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Warning: Failed to delete temporary file '{tempFile}': {ex.Message}");
                }
            }
        }

        private void ProcessRTBL(dynamic jsonData)
        {
            if (jsonData.Entries == null)
            {
                Console.WriteLine($"Error: No 'Entries' found in '{jsonFile}'. Skipping.");
                CleanupTempFile();
                return;
            }

            int entryIndex = 0;
            foreach (var entry in jsonData.Entries)
            {
                string offsetType = entry.OffsetType.ToString();
                long originalOffset = Convert.ToInt64(entry.Offset);
                int originalCSize = Convert.ToInt32(entry.CSize);
                int originalDSize = Convert.ToInt32(entry.DSize);
                long tocOffset = Convert.ToInt64(entry.TOCOffset);
                string filePath = entry.Filename?.ToString();

                Console.WriteLine($"[Debug] Entry {entryIndex}: OffsetType='{offsetType}', TOCOffset=0x{tocOffset:X}, Filename='{filePath}'");

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Console.WriteLine($"[Debug] File '{filePath}' not found or empty. Skipping.");
                    entryIndex++;
                    continue;
                }

                byte[] newData = File.ReadAllBytes(filePath);
                bool compression = originalCSize != originalDSize;
                byte[] newCompressedData = compression ? Compression.LeCompression(newData) : newData;
                int newCSize = newCompressedData.Length;
                int newDSize = newData.Length;

                long newOffset = originalOffset;
                if (offsetType.StartsWith("RDP"))
                {
                    string rdpFile = GetRdpFileName(offsetType);
                    string rdpInputPath = Path.Combine(rdpInputRoot, rdpFile); // Look in .\
                    string rdpOutputPath = Path.Combine(rdpOutputRoot, rdpFile); // Write to .\repacked
                    long baseOffset = (originalOffset & 0xFFFFFFF) * 0x800;

                    if (!File.Exists(rdpInputPath))
                    {
                        Console.WriteLine($"Error: Original RDP file '{rdpInputPath}' not found. Skipping entry.");
                        entryIndex++;
                        continue;
                    }

                    // Copy original RDP to repacked folder if not already done
                    if (!File.Exists(rdpOutputPath))
                    {
                        File.Copy(rdpInputPath, rdpOutputPath);
                        Console.WriteLine($"[Debug] Copied original '{rdpInputPath}' to '{rdpOutputPath}'");
                    }

                    string contentHash = ComputeHash(newCompressedData);
                    var key = (rdpFile, originalOffset, contentHash);
                    if (processedRdpFiles.TryGetValue(key, out long reusedOffset))
                    {
                        newOffset = reusedOffset;
                        Console.WriteLine($"[Debug] Reusing offset 0x{newOffset:X} for '{filePath}' (matches previous file at original offset 0x{originalOffset:X})");
                    }
                    else if (newCSize > originalCSize || !CheckOffsetAvailability(rdpFile, baseOffset, baseOffset + newCSize))
                    {
                        newOffset = FindNewRdpOffset(rdpFile, newCSize, rdpOutputPath);
                        string filename = Path.GetFileName(filePath);
                        Console.WriteLine($"[Debug] {filename} Reassigned to 0x{newOffset:X} with a total of: {newCSize} bytes || Written to '{rdpFile}'");

                        try
                        {
                            using (var fs = new FileStream(rdpOutputPath, FileMode.Open, FileAccess.Write))
                            {
                                long writeOffset = (newOffset & 0xFFFFFFF) * 0x800;
                                if (writeOffset + newCSize > fs.Length)
                                {
                                    fs.SetLength(writeOffset + newCSize);
                                }
                                fs.Seek(writeOffset, SeekOrigin.Begin);
                                fs.Write(newCompressedData, 0, newCSize);
                                usedOffsets[rdpFile].Add((writeOffset, writeOffset + newCSize));
                                Console.WriteLine($"[Debug] Wrote {newCSize} bytes to '{rdpOutputPath}' at 0x{writeOffset:X}");
                            }
                            processedRdpFiles[key] = newOffset;
                        }
                        catch (IOException ex)
                        {
                            Console.WriteLine($"Error writing to '{rdpOutputPath}': {ex.Message}");
                            CleanupTempFile();
                            return;
                        }
                    }
                    else
                    {
                        long writeOffset = baseOffset;
                        using (var fs = new FileStream(rdpOutputPath, FileMode.Open, FileAccess.Write))
                        {
                            fs.Seek(writeOffset, SeekOrigin.Begin);
                            fs.Write(newCompressedData, 0, newCSize);
                            usedOffsets[rdpFile].Add((writeOffset, writeOffset + newCSize));
                            Console.WriteLine($"[Debug] Wrote {newCSize} bytes to '{rdpOutputPath}' at original offset 0x{writeOffset:X}");
                        }
                        processedRdpFiles[key] = originalOffset;
                    }
                }

                try
                {
                    using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Write))
                    using (var writer = new BinaryWriter(fs))
                    {
                        writer.BaseStream.Seek(tocOffset, SeekOrigin.Begin);
                        writer.Write((uint)newOffset);
                        writer.Write((uint)newCSize);
                        writer.Write((uint)entry.NameOffset);
                        writer.Write((uint)entry.ChunkName);
                        writer.Write(new byte[12]);
                        writer.Write((uint)newDSize);
                        Console.WriteLine($"[Debug] Updated TOC at 0x{tocOffset:X}: Offset=0x{newOffset:X}, CSize={newCSize}, DSize={newDSize}");
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error updating TOC in '{tempFile}': {ex.Message}");
                    CleanupTempFile();
                    return;
                }

                entryIndex++;
            }
        }

        private void ProcessRES(dynamic jsonData)
        {
            int groupOffset = jsonData.GroupOffset;
            int groupCount = jsonData.GroupCount;
            int chunkDatasOffset = jsonData.ChunkDatasOffset;

            List<int> groups = new List<int>();
            foreach (var group in jsonData.Groups)
            {
                groups.Add((int)group);
            }
            List<int> validGroups = new List<int>();
            for (int i = 0; i < groups.Count; i += 2)
            {
                if (i + 1 < groups.Count && groups[i + 1] > 0)
                {
                    validGroups.Add(groups[i]);
                    validGroups.Add(groups[i + 1]);
                }
            }

            try
            {
                using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(fs))
                {
                    int fileIndex = 0;
                    int validFileIndex = 0;
                    foreach (var file in jsonData.Files)
                    {
                        string offsetType = file.OffsetType.ToString();
                        if (offsetType == "dummy")
                        {
                            fileIndex++;
                            continue;
                        }

                        int tocOffset = GetTocOffset(validGroups, ref validFileIndex);
                        if (tocOffset == -1)
                        {
                            Console.WriteLine($"[Debug] No valid group for file index {fileIndex}. Skipping.");
                            fileIndex++;
                            continue;
                        }

                        ProcessFileEntry(writer, file, fileIndex, tocOffset);
                        fileIndex++;
                        validFileIndex++;
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error processing RES file '{tempFile}': {ex.Message}");
                CleanupTempFile();
            }
        }

        private int GetTocOffset(List<int> validGroups, ref int validFileIndex)
        {
            int totalEntries = 0;
            for (int i = 0; i < validGroups.Count; i += 2)
            {
                int entryOffset = validGroups[i];
                int entryCount = validGroups[i + 1];
                if (validFileIndex < totalEntries + entryCount)
                {
                    int localIndex = validFileIndex - totalEntries;
                    return entryOffset + localIndex * 32;
                }
                totalEntries += entryCount;
            }
            return -1;
        }

        private void ProcessFileEntry(BinaryWriter writer, dynamic file, int fileIndex, int tocOffset)
        {
            bool isRdp = Convert.ToBoolean(file.RDP);
            long offset = Convert.ToInt64(file.Offset);
            int cSize = Convert.ToInt32(file.CSize);
            int dSize = Convert.ToInt32(file.DSize);
            int nameOffset = Convert.ToInt32(file.OffsetName);
            string filePath = file.FileName?.ToString() ?? "";
            string displayName = file.ElementName != null && file.ElementName.Count > 0 ? file.ElementName[0].ToString() : filePath;
            uint chunkName = file.ChunkName != null ? Convert.ToUInt32(file.ChunkName) : ReadOriginalChunkName(tocOffset);

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine($"[Debug] File '{filePath}' not found. Skipping.");
                writer.BaseStream.Seek(tocOffset, SeekOrigin.Begin);
                UpdateTOCEntry(writer, offset, cSize, dSize, nameOffset, chunkName, isRdp);
                return;
            }

            byte[] newData = File.ReadAllBytes(filePath);
            bool compression = Convert.ToBoolean(file.Compression);
            byte[] newCompressedData = compression ? Compression.LeCompression(newData) : newData;
            int newCSize = newCompressedData.Length;
            int newDSize = newData.Length;

            long newOffset = offset;
            if (isRdp)
            {
                string rdpFile = GetRdpFileName(file.OffsetType.ToString());
                string rdpInputPath = Path.Combine(rdpInputRoot, rdpFile); // Look in .\
                string rdpOutputPath = Path.Combine(rdpOutputRoot, rdpFile); // Write to .\repacked
                long baseOffset = (offset & 0xFFFFFFF) * 0x800;

                if (!File.Exists(rdpInputPath))
                {
                    Console.WriteLine($"Error: Original RDP file '{rdpInputPath}' not found. Skipping entry.");
                    return;
                }

                // Copy original RDP to repacked folder if not already done
                if (!File.Exists(rdpOutputPath))
                {
                    File.Copy(rdpInputPath, rdpOutputPath);
                    Console.WriteLine($"[Debug] Copied original '{rdpInputPath}' to '{rdpOutputPath}'");
                }

                string contentHash = ComputeHash(newCompressedData);
                var key = (rdpFile, offset, contentHash);
                if (processedRdpFiles.TryGetValue(key, out long reusedOffset))
                {
                    newOffset = reusedOffset;
                    Console.WriteLine($"[Debug] Reusing offset 0x{newOffset:X} for '{filePath}' (matches previous file at original offset 0x{offset:X})");
                }
                else if (newCSize > cSize || !CheckOffsetAvailability(rdpFile, baseOffset, baseOffset + newCSize))
                {
                    newOffset = FindNewRdpOffset(rdpFile, newCSize, rdpOutputPath);
                    string filename = Path.GetFileName(filePath);
                    Console.WriteLine($"[Debug] {filename} Reassigned to 0x{newOffset:X} with a total of: {newCSize} bytes || Written to '{rdpFile}'");

                    try
                    {
                        using (var fs = new FileStream(rdpOutputPath, FileMode.Open, FileAccess.Write))
                        {
                            long writeOffset = (newOffset & 0xFFFFFFF) * 0x800;
                            if (writeOffset + newCSize > fs.Length)
                            {
                                fs.SetLength(writeOffset + newCSize);
                            }
                            fs.Seek(writeOffset, SeekOrigin.Begin);
                            fs.Write(newCompressedData, 0, newCSize);
                            usedOffsets[rdpFile].Add((writeOffset, writeOffset + newCSize));
                        }
                        processedRdpFiles[key] = newOffset;
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Error writing to '{rdpOutputPath}': {ex.Message}");
                        CleanupTempFile();
                        return;
                    }
                }
                else
                {
                    long writeOffset = baseOffset;
                    using (var fs = new FileStream(rdpOutputPath, FileMode.Open, FileAccess.Write))
                    {
                        fs.Seek(writeOffset, SeekOrigin.Begin);
                        fs.Write(newCompressedData, 0, newCSize);
                        usedOffsets[rdpFile].Add((writeOffset, writeOffset + newCSize));
                        Console.WriteLine($"[Debug] Wrote {newCSize} bytes to '{rdpOutputPath}' at original offset 0x{writeOffset:X}");
                    }
                    processedRdpFiles[key] = offset;
                }
            }
            else
            {
                long writeOffset = offset & 0xFFFFFFF;
                if (writeOffset + newCSize > writer.BaseStream.Length)
                {
                    writer.BaseStream.SetLength((writeOffset + newCSize + 0xF) & ~0xF);
                }
                writer.BaseStream.Seek(writeOffset, SeekOrigin.Begin);
                writer.Write(newCompressedData);
                PadToAlignment(writer, newCSize);
            }

            writer.BaseStream.Seek(tocOffset, SeekOrigin.Begin);
            UpdateTOCEntry(writer, newOffset, newCSize, newDSize, nameOffset, chunkName, isRdp);
        }

        private uint ReadOriginalChunkName(int tocOffset)
        {
            using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                fs.Seek(tocOffset + 12, SeekOrigin.Begin);
                return reader.ReadUInt32();
            }
        }

        private long FindNewRdpOffset(string rdpFile, int size, string rdpPath)
        {
            long offset = 32;
            const long maxOffset = 0xFFFFFFF * 2048L;
            long fileSize;

            using (var fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Read))
            {
                fileSize = fs.Length;
            }

            while (true)
            {
                long alignedOffset = (offset + 0x7FF) & ~0x7FF;
                long endOffset = alignedOffset + size;

                if (CheckOffsetAvailability(rdpFile, alignedOffset, endOffset))
                {
                    return (alignedOffset / 0x800) | (GetRdpEnumerator(rdpFile) << 28);
                }

                if (endOffset > fileSize || alignedOffset >= maxOffset)
                {
                    long newSize = Math.Max(endOffset, fileSize + 0x100000);
                    using (var fs = new FileStream(rdpPath, FileMode.Open, FileAccess.Write))
                    {
                        if (newSize > fs.Length)
                        {
                            fs.SetLength(newSize);
                            Console.WriteLine($"[Debug] Resized '{rdpPath}' to {newSize} bytes to accommodate new data");
                        }
                    }
                    fileSize = newSize;
                }

                string filename = Path.GetFileName(rdpPath);
                Console.WriteLine($"Warning [{filename}]: Attempted writing used offset at 0x{alignedOffset:X}-0x{endOffset:X}. Reassigning to available offsets...");
                offset += 0x800;

                if (offset >= maxOffset)
                {
                    throw new InvalidOperationException($"No available offsets found in '{rdpFile}' after resizing to {fileSize} bytes.");
                }
            }
        }

        private bool CheckOffsetAvailability(string rdpFile, long startOffset, long endOffset)
        {
            if (!usedOffsets.ContainsKey(rdpFile)) return true;
            foreach (var (usedStart, usedEnd) in usedOffsets[rdpFile])
            {
                if (startOffset < usedEnd && endOffset > usedStart)
                {
                    return false;
                }
            }
            return true;
        }

        private void UpdateTOCEntry(BinaryWriter writer, long offset, int cSize, int dSize, int nameOffset, uint chunkName, bool isRdp)
        {
            writer.Write((uint)offset);
            writer.Write((uint)cSize);
            writer.Write((uint)nameOffset);
            writer.Write(chunkName);
            writer.Write(new byte[12]);
            writer.Write((uint)dSize);
        }

        private void PadToAlignment(BinaryWriter writer, int dataSize)
        {
            int padding = (0x10 - (dataSize % 0x10)) % 0x10;
            for (int i = 0; i < padding; i++)
            {
                writer.Write((byte)0);
            }
        }

        private string GetRdpFileName(string offsetType)
        {
            switch (offsetType)
            {
                case "RDP Package File": return "package.rdp";
                case "RDP Data File": return "data.rdp";
                case "RDP Patch File": return "patch.rdp";
                default: throw new ArgumentException($"Unknown OffsetType: {offsetType}");
            }
        }

        private int GetRdpEnumerator(string rdpFile)
        {
            switch (rdpFile)
            {
                case "package.rdp": return 0x4;
                case "data.rdp": return 0x5;
                case "patch.rdp": return 0x6;
                default: throw new ArgumentException($"Unknown RDP file: {rdpFile}");
            }
        }

        private string ComputeHash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(data);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}