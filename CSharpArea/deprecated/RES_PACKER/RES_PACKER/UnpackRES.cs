using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RES_PACKER
{
    public class UnpackRES
    {
        private byte[] resFileData;
        private readonly string resFileName;
        private readonly string outputBaseFolder;
        private readonly string contentFolder;
        private readonly DataHelper dataHelper;
        private Dictionary<string, int> fileNameCount = new Dictionary<string, int>();
        public static List<string> resFiles = new List<string>();
        public static List<string> rtblFiles = new List<string>();
        private static HashSet<string> processedResFiles = new HashSet<string>();
        private Queue<(string path, byte[] data, string outputFolder, uint entryCount)> nestedFilesQueue = new Queue<(string, byte[], string, uint)>();
        private static Dictionary<string, (string path, bool isProcessed)> extractedFilePaths = new Dictionary<string, (string, bool)>();

        public UnpackRES(byte[] resFileData, string resFileName, string outputFolder, DataHelper dataHelper)
        {
            this.resFileData = resFileData;
            this.resFileName = resFileName ?? throw new ArgumentNullException(nameof(resFileName));
            this.outputBaseFolder = Path.GetDirectoryName(resFileName);
            this.dataHelper = dataHelper ?? throw new ArgumentNullException(nameof(dataHelper));

            string resFileNameWithoutExt = Path.GetFileNameWithoutExtension(resFileName);
            this.contentFolder = outputFolder.EndsWith(resFileNameWithoutExt, StringComparison.OrdinalIgnoreCase)
                ? outputFolder
                : Path.Combine(outputFolder, resFileNameWithoutExt);
            Directory.CreateDirectory(contentFolder);

            string relativePath = GetRelativePath(resFileName);
            string extension = Path.GetExtension(resFileName).ToLower();
            if (extension == ".res" && !resFiles.Contains(relativePath))
            {
                resFiles.Add(relativePath);
            }
            else if (extension == ".rtbl" && !rtblFiles.Contains(relativePath))
            {
                rtblFiles.Add(relativePath);
            }

            Console.WriteLine("[Debug] Initialized UnpackRES for {0}, resFileData.Length = {1}", resFileName, resFileData?.Length ?? 0);
        }

        public void ExtractFile(TOC.TOCEntry entry)
        {
            bool isDummy = entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize > 0;
            if (isDummy)
            {
                Console.WriteLine("Detected dummy entry: DSize = {0}", entry.DSize);
                dataHelper.AddDummyFileEntry(entry);
                return;
            }

            if (entry.OffsetType == "NoSet (External RDP Exclusion)" || entry.OffsetType == "BIN/MODULE (External File)")
            {
                Console.WriteLine("Skipping extraction for {0} ({1}), but adding to JSON", entry.Name, entry.OffsetType);
                dataHelper.AddFileEntry(entry, null, false);
                return;
            }

            string fileName = GenerateFileName(entry);
            string fullPath = GenerateFullPath(entry, fileName);
            Console.WriteLine("[Debug] Initial fullPath for {0}: {1}", entry.Name, fullPath);

            fullPath = HandleDuplicateFileName(fullPath, entry.Offset, entry.CSize);
            Console.WriteLine("[Debug] Final fullPath after duplicate handling for {0}: {1}", entry.Name, fullPath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string relativePath = GetRelativePath(fullPath);
            Console.WriteLine("Extracting {0} to {1} (OffsetType: {2})", entry.Name, relativePath, entry.OffsetType);

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
            }

            if (extractedData != null)
            {
                string extension = Path.GetExtension(fullPath).ToLower();
                if (extension == ".rtbl")
                {
                    Console.WriteLine("Found RTBL file: {0} (Data length: {1} bytes), deferring to RTBL.cs", relativePath, extractedData.Length);
                    lock (rtblFiles)
                    {
                        if (!rtblFiles.Contains(relativePath))
                            rtblFiles.Add(relativePath);
                    }
                }
                else if (extension == ".res")
                {
                    QueueNestedFile(fullPath, GetNestedEntryCount(fullPath));
                    ProcessNestedFiles();
                }
            }
            else
            {
                Console.WriteLine("Warning: Failed to extract data for {0}", relativePath);
            }
        }

        private void QueueNestedFile(string fullPath, uint entryCount)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                Console.WriteLine("Warning: Attempted to queue a null or empty file path.");
                return;
            }

            string extension = Path.GetExtension(fullPath).ToLower();
            if (extension != ".res")
                return;

            lock (extractedFilePaths)
            {
                if (extractedFilePaths.TryGetValue(fullPath, out var fileInfo) && fileInfo.isProcessed)
                {
                    Console.WriteLine("Skipping queue for already processed file: {0}", GetRelativePath(fullPath));
                    return;
                }

                if (processedResFiles.Contains(fullPath))
                {
                    Console.WriteLine("Skipping queue for already processed path: {0}", GetRelativePath(fullPath));
                    return;
                }

                Console.WriteLine("Queueing nested file: {0} (EntryCount: {1})", GetRelativePath(fullPath), entryCount);

                byte[] fileData;
                try
                {
                    fileData = File.ReadAllBytes(fullPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading nested file {0}: {1}", fullPath, ex.Message);
                    return;
                }

                string outputFolder = Path.GetDirectoryName(fullPath) ?? contentFolder;
                nestedFilesQueue.Enqueue((fullPath, fileData, outputFolder, entryCount));
                processedResFiles.Add(fullPath);

                if (!extractedFilePaths.ContainsKey(fullPath))
                {
                    extractedFilePaths[fullPath] = (fullPath, false);
                }
            }
        }

        public void ProcessNestedFiles()
        {
            while (nestedFilesQueue.Count > 0)
            {
                var nestedFile = nestedFilesQueue.Dequeue();
                string fullPath = nestedFile.path;
                byte[] fileData = nestedFile.data;
                string outputFolder = nestedFile.outputFolder;
                uint entryCount = nestedFile.entryCount;

                lock (extractedFilePaths)
                {
                    if (extractedFilePaths.TryGetValue(fullPath, out var fileInfo) && fileInfo.isProcessed)
                    {
                        Console.WriteLine("Skipping already processed file: {0}", GetRelativePath(fullPath));
                        continue;
                    }
                }

                Console.WriteLine("Processing nested file: {0} (EntryCount: {1})", GetRelativePath(fullPath), entryCount);
                PRES nestedProcessor = new PRES();
                nestedProcessor.ProcessRES(fileData, fullPath, outputFolder);

                lock (extractedFilePaths)
                {
                    extractedFilePaths[fullPath] = (fullPath, true);
                    Console.WriteLine("Marked as processed: {0}", GetRelativePath(fullPath));
                }
            }

            resFileData = null;
            GC.Collect();
            Console.WriteLine("Flushed memory for {0}", GetRelativePath(resFileName));
        }

        private uint GetNestedEntryCount(string filePath)
        {
            if (!File.Exists(filePath))
                return 0;

            byte[] fileData = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(fileData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                if (ms.Length < 16) return 0;

                int magic = reader.ReadInt32();
                if (magic != PRES.HEADER_MAGIC) return 0;

                int groupOffset = reader.ReadInt32();
                byte groupCount = reader.ReadByte();
                if (groupCount == 0 || groupOffset >= ms.Length) return 0;

                ms.Position = groupOffset;
                if (ms.Position + 8 > ms.Length) return 0;

                reader.ReadUInt32(); // EntryOffset
                return reader.ReadUInt32(); // EntryCount
            }
        }

        private string GenerateFileName(TOC.TOCEntry entry) => entry.Type != null ? $"{entry.Name}.{entry.Type}" : entry.Name ?? "unnamed";

        private string GenerateFullPath(TOC.TOCEntry entry, string fileName)
        {
            if (entry.SubPath != null)
            {
                string[] subPathParts = entry.SubPath.Split('/');
                string lastPart = subPathParts[subPathParts.Length - 1];
                if (lastPart == fileName)
                    return Path.Combine(contentFolder, entry.SubPath);
                return Path.Combine(contentFolder, entry.SubPath, fileName);
            }
            else if (entry.Path != null)
            {
                return Path.Combine(contentFolder, entry.Path, fileName);
            }
            return Path.Combine(contentFolder, fileName);
        }

        private string HandleDuplicateFileName(string fullPath, uint offset, uint csize)
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
                Console.WriteLine("File exists, renaming to: {0}", GetRelativePath(uniquePath));
            }

            return uniquePath;
        }

        private byte[] ExtractFromCurrent(TOC.TOCEntry entry, string outputPath, string relativePath)
        {
            ulong actualOffset = entry.Offset & 0x0FFFFFFF;
            ulong endOffset = actualOffset + entry.CSize;

            // Log the file size and expected range for debugging
            Console.WriteLine("[Debug] resFileData.Length = {0}, Extracting Offset: 0x{1:X8}, CSize: {2}, End Offset: 0x{3:X8}", 
                resFileData?.Length ?? 0, actualOffset, entry.CSize, endOffset);

            if (resFileData == null || endOffset > (ulong)resFileData.Length)
            {
                Console.WriteLine("Warning: Data exceeds resFileData size (Offset: 0x{0:X8}, CSize: {1}, resFileData.Length: {2}). Attempting to read from disk.", 
                    actualOffset, entry.CSize, resFileData?.Length ?? 0);

                if (!File.Exists(resFileName))
                {
                    Console.WriteLine("Error: RES file {0} not found on disk.", resFileName);
                    return null;
                }

                try
                {
                    using (FileStream fs = new FileStream(resFileName, FileMode.Open, FileAccess.Read))
                    {
                        if (endOffset > (ulong)fs.Length)
                        {
                            Console.WriteLine("Error: Data exceeds RES file size on disk (Offset: 0x{0:X8}, CSize: {1}, File Length: {2})", 
                                actualOffset, entry.CSize, fs.Length);
                            return null;
                        }

                        fs.Seek((long)actualOffset, SeekOrigin.Begin);
                        byte[] data = new byte[entry.CSize];
                        int bytesRead = fs.Read(data, 0, (int)entry.CSize);
                        if (bytesRead != entry.CSize)
                        {
                            Console.WriteLine("Error: Incomplete read from disk for {0} (Expected {1} bytes, Read {2} bytes)", 
                                relativePath, entry.CSize, bytesRead);
                            return null;
                        }

                        WriteFile(data, outputPath, relativePath, entry, false);
                        return data;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading from disk for {0}: {1}", relativePath, ex.Message);
                    return null;
                }
            }

            byte[] extractedData = new byte[entry.CSize];
            Array.Copy(resFileData, (long)actualOffset, extractedData, 0, (int)entry.CSize);
            WriteFile(extractedData, outputPath, relativePath, entry, false);
            return extractedData;
        }

        private byte[] ExtractFromRDP(TOC.TOCEntry entry, string outputPath, string relativePath)
        {
            byte enumerator = (byte)(entry.Offset >> 28);
            string rdpFileName = null;

            if (enumerator == 0x4)
                rdpFileName = "package.rdp";
            else if (enumerator == 0x5)
                rdpFileName = "data.rdp";
            else if (enumerator == 0x6)
                rdpFileName = "patch.rdp";

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
                WriteFile(data, outputPath, relativePath, entry, true);
                return data;
            }
        }

        private void WriteFile(byte[] data, string outputPath, string relativePath, TOC.TOCEntry entry, bool isRDP)
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
                    return;
                }
            }
            else
            {
                File.WriteAllBytes(outputPath, data);
                Console.WriteLine("Extracted: {0} ({1} bytes)", relativePath, data.Length);
                dataHelper.AddFileEntry(entry, outputPath, false);
            }

            lock (extractedFilePaths)
            {
                if (!extractedFilePaths.ContainsKey(outputPath))
                {
                    extractedFilePaths[outputPath] = (outputPath, false);
                    Console.WriteLine("Stored extracted path: {0} (Unprocessed)", GetRelativePath(outputPath));
                }
            }
        }

        private static string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return "";

            string currentDir = Directory.GetCurrentDirectory();
            if (fullPath.StartsWith(currentDir))
            {
                int startIndex = currentDir.Length;
                if (startIndex < fullPath.Length)
                    return ".\\" + fullPath.Substring(startIndex).Replace("/", "\\");
            }
            return fullPath.Replace("/", "\\");
        }

        public static void SaveAllIndexes()
        {
            string resIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "RESINDEX.json");
            var indexData = new
            {
                RES_LIST = resFiles,
                RTBL_LIST = rtblFiles
            };
            string json = JsonConvert.SerializeObject(indexData, Formatting.Indented);
            File.WriteAllText(resIndexPath, json);
            Console.WriteLine("Saved RESINDEX.json to {0} (RES: {1}, RTBL: {2})", resIndexPath, resFiles.Count, rtblFiles.Count);

            DataHelper.SaveRDPFiles();
        }
    }
}