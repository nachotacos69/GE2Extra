using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

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
            public uint Offset { get; set; }
            public bool RDP { get; set; }
            public uint CSize { get; set; }
            public uint OffsetName { get; set; }
            public uint ChunkName { get; set; }
            public uint DSize { get; set; }
            public List<string> ElementName { get; set; } = new List<string> { null, null, null }; // [name, type, path]
            public string FileName { get; set; }
            public bool Compression { get; set; }
        }

        private readonly ResFileData data = new ResFileData();

        public DataHelper(string resFileName)
        {
            data.Filename = ".\\" + Path.GetFileName(resFileName);
        }

        public void SetHeaderData(PRES pres)
        {
            data.Header = PRES.HEADER_MAGIC; // 0x73657250 = 1936028240 in decimal
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
                FileName = extractedFilePath != null && extractedFilePath.StartsWith(Directory.GetCurrentDirectory())
                    ? "." + extractedFilePath.Substring(Directory.GetCurrentDirectory().Length)
                    : extractedFilePath,
                Compression = isCompressed
            };

            if (entry.Name != null)
            {
                fileEntry.ElementName[0] = entry.Name;
                fileEntry.ElementName[1] = entry.Type ?? "";
                fileEntry.ElementName[2] = entry.Path ?? "";
                if (entry.SubPath != null) fileEntry.FileName = "\\" + entry.SubPath;
            }

            data.Files.Add(fileEntry);
            data.TotalFiles = data.Files.Count;
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
                FileName = null, // No file extracted
                Compression = false
            };

            data.Files.Add(fileEntry);
            data.TotalFiles = data.Files.Count;
        }

        public void SaveToJson()
        {
            string jsonFileName = Path.ChangeExtension(data.Filename, ".json");
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(jsonFileName, json);
            Console.WriteLine($"Saved RES file data to {jsonFileName}");
        }
    }
}