using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace RES_PACKER
{
    public class DataHelper
    {
        public class ResFileData
        {
            public string Filename { get; set; }
            public int Header { get; set; }
            public int GroupOffset { get; set; }
            public byte GroupCount { get; set; }
            public byte GroupVersion { get; set; }
            public short Checksum { get; set; }
            public int Version { get; set; }
            public uint ChunkDatasOffset { get; set; }
            public uint SideloadResOffset { get; set; }
            public uint SideloadResSize { get; set; }
            public int TotalFiles { get; set; }
            public List<int> Groups { get; set; } = new List<int>();
            public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        }

        public class RDPFileEntry
        {
            public string Name { get; set; }
            public uint Offset { get; set; }
            public uint CSize { get; set; }
        }

        public class FileEntry
        {
            public Dictionary<string, bool> UpdatePointer { get; set; } = new Dictionary<string, bool>
            {
                { "offset", true },
                { "csize", true },
                { "nameOffset", true },
                { "dSize", false }
            };
            public string OffsetType { get; set; }
            public string TOC_Offset { get; set; }
            public uint Offset { get; set; }
            public bool RDP { get; set; }
            public uint CSize { get; set; }
            public uint OffsetName { get; set; }
            public long RTBL_OffsetName { get; set; }
            public uint ChunkName { get; set; }
            public uint DSize { get; set; }
            public List<string> ElementName { get; set; } = new List<string> { null, null, null };
            public string FileName { get; set; }
            public bool Compression { get; set; }
        }

        private readonly ResFileData data = new ResFileData();
        private readonly string resFilePath;
        private static readonly List<RDPFileEntry> packageFiles = new List<RDPFileEntry>();
        private static readonly List<RDPFileEntry> dataFiles = new List<RDPFileEntry>();
        private static readonly List<RDPFileEntry> patchFiles = new List<RDPFileEntry>();

        public DataHelper(string resFilePath)
        {
            this.resFilePath = resFilePath;
            data.Filename = GetRelativePath(resFilePath);
        }

        public void SetHeaderData(PRES pres)
        {
            data.Header = PRES.HEADER_MAGIC;
            data.GroupOffset = pres.GroupOffset;
            data.GroupCount = pres.GroupCount;
            data.GroupVersion = pres.GroupVersion;
            data.Checksum = pres.Checksum;
            data.Version = pres.Version;
            data.ChunkDatasOffset = pres.ChunkDatasOffset;
            data.SideloadResOffset = pres.SideloadResOffset;
            data.SideloadResSize = pres.SideloadResSize;
        }

        public void AddGroup(uint entryOffset, uint entryCount)
        {
            data.Groups.Add((int)entryOffset);
            data.Groups.Add((int)entryCount);
        }

        public void AddFileEntry(TOC.TOCEntry entry, string extractedFilePath, bool isCompressed)
        {
            var fileEntry = new FileEntry
            {
                OffsetType = entry.OffsetType,
                Offset = entry.Offset,
                RDP = entry.OffsetType.StartsWith("RDP"),
                CSize = entry.CSize,
                OffsetName = entry.NameOffset,
                ChunkName = entry.ChunkName,
                DSize = entry.DSize,
                FileName = extractedFilePath != null ? EnsureRelativePath(extractedFilePath) : null,
                Compression = isCompressed
            };

            if (entry.Name != null)
            {
                fileEntry.ElementName[0] = entry.Name;
                fileEntry.ElementName[1] = entry.Type ?? "";
                fileEntry.ElementName[2] = entry.Path ?? "";
                if (entry.SubPath != null) fileEntry.FileName = EnsureRelativePath(Path.Combine(Path.GetDirectoryName(resFilePath), entry.SubPath, $"{entry.Name}.{entry.Type}"));
            }

            data.Files.Add(fileEntry);
            data.TotalFiles = data.Files.Count;

            if (fileEntry.RDP && extractedFilePath != null)
                AddRDPFileEntry(entry.Offset, entry.CSize, extractedFilePath);
        }

        public void AddFileEntry(RTBL.RTBLEntry entry, string extractedFilePath, bool isCompressed)
        {
            var fileEntry = new FileEntry
            {
                UpdatePointer = new Dictionary<string, bool>
                {
                    { "offset", true },
                    { "csize", true },
                    { "nameOffset", false },
                    { "dSize", false }
                },
                OffsetType = entry.OffsetType,
                TOC_Offset = entry.TOCOffset.ToString(),
                Offset = entry.Offset,
                RDP = entry.OffsetType.StartsWith("RDP"),
                CSize = entry.CSize,
                RTBL_OffsetName = entry.TOCOffset + entry.NameOffset,
                ChunkName = entry.ChunkName,
                DSize = entry.DSize,
                FileName = extractedFilePath != null ? EnsureRelativePath(extractedFilePath) : null,
                Compression = isCompressed
            };

            if (entry.Name != null)
            {
                fileEntry.ElementName[0] = entry.Name;
                fileEntry.ElementName[1] = entry.Type ?? "res";
                fileEntry.ElementName[2] = null;
            }

            data.Files.Add(fileEntry);
            data.TotalFiles = data.Files.Count;

            if (fileEntry.RDP && extractedFilePath != null)
                AddRDPFileEntry(entry.Offset, entry.CSize, extractedFilePath);
        }

        public void AddDummyFileEntry(TOC.TOCEntry entry)
        {
            var fileEntry = new FileEntry
            {
                UpdatePointer = new Dictionary<string, bool>
                {
                    { "offset", false },
                    { "csize", false },
                    { "nameOffset", false },
                    { "dSize", true }
                },
                OffsetType = "dummy",
                Offset = 0,
                RDP = false,
                CSize = 0,
                OffsetName = 0,
                ChunkName = 0,
                DSize = entry.DSize,
                FileName = null,
                Compression = false
            };

            data.Files.Add(fileEntry);
            data.TotalFiles = data.Files.Count;
        }

        private void AddRDPFileEntry(uint offset, uint cSize, string extractedFilePath)
        {
            byte enumerator = (byte)(offset >> 28);
            uint actualOffset = (offset & 0x0FFFFFFF) * 0x800;
            string formattedPath = EnsureRelativePath(extractedFilePath);
            var rdpEntry = new RDPFileEntry
            {
                Name = formattedPath,
                Offset = actualOffset,
                CSize = cSize
            };

            lock (packageFiles)
            {
                if (enumerator == 0x4 && !packageFiles.Any(f => f.Name == formattedPath && f.Offset == actualOffset))
                    packageFiles.Add(rdpEntry);
                else if (enumerator == 0x5 && !dataFiles.Any(f => f.Name == formattedPath && f.Offset == actualOffset))
                    dataFiles.Add(rdpEntry);
                else if (enumerator == 0x6 && !patchFiles.Any(f => f.Name == formattedPath && f.Offset == actualOffset))
                    patchFiles.Add(rdpEntry);
            }
        }

        public void SaveToJson()
        {
            string jsonFileName = Path.Combine(Path.GetDirectoryName(resFilePath), Path.GetFileNameWithoutExtension(resFilePath) + ".json");
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(jsonFileName, json);
            Console.WriteLine($"Saved RES file data to {jsonFileName}");
        }

        public static void SaveRDPFiles()
        {
            SaveRDPFile("PACKAGEFiles.json", packageFiles, "package.rdp");
            SaveRDPFile("DATAFiles.json", dataFiles, "data.rdp");
            SaveRDPFile("PATCHFiles.json", patchFiles, "patch.rdp");

            GC.Collect();
            Console.WriteLine("Flushed RDP file data from memory");
        }

        private static void SaveRDPFile(string fileName, List<RDPFileEntry> files, string rdpSource)
        {
            if (files.Count == 0) return;

            lock (files)
            {
                var sortedFiles = files.OrderBy(f => f.Offset).ToList();
                var rdpData = new { Files = sortedFiles };
                string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                string json = JsonConvert.SerializeObject(rdpData, Formatting.Indented);
                File.WriteAllText(jsonPath, json);
                Console.WriteLine($"Saved {rdpSource} file data to {jsonPath}");
            }
        }

        private string GetRelativePath(string fullPath)
        {
            string currentDir = Directory.GetCurrentDirectory();
            if (fullPath.StartsWith(currentDir))
            {
                int startIndex = currentDir.Length;
                if (startIndex < fullPath.Length)
                    return ".\\" + fullPath.Substring(startIndex).Replace("/", "\\").TrimStart('\\');
            }
            return fullPath.Replace("/", "\\");
        }

        private string EnsureRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;

            string currentDir = Directory.GetCurrentDirectory();
            string relativePath;
            if (fullPath.StartsWith(currentDir))
            {
                int startIndex = currentDir.Length;
                relativePath = fullPath.Substring(startIndex).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                relativePath = fullPath;
            }
            return ".\\" + relativePath.Replace("/", "\\").TrimStart('\\');
        }
    }
}