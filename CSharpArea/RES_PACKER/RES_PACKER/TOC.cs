using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace RES_PACKER
{
    public class TOC
    {
        public class TOCEntry
        {
            public uint Offset { get; }
            public uint CSize { get; }
            public uint NameOffset { get; }
            public uint ChunkName { get; }
            public uint DSize { get; }
            public ulong ActualOffset => Offset & 0x0FFFFFFF;
            public string OffsetType { get; private set; }
            public ulong EndOffset => ActualOffset + CSize;
            public string Name { get; private set; }
            public string Type { get; private set; }
            public string Path { get; private set; }
            public string SubPath { get; private set; }
            public string ExtraPath { get; private set; }

            public TOCEntry(uint offset, uint csize, uint nameOffset, uint chunkName, uint dsize)
            {
                Offset = offset;
                CSize = csize;
                NameOffset = nameOffset;
                ChunkName = chunkName;
                DSize = dsize;

                byte enumerator = (byte)(Offset >> 28);
                switch (enumerator)
                {
                    case 0x0: OffsetType = "BIN/MODULE (External File)"; break;
                    case 0x4: OffsetType = "RDP Package File"; break;
                    case 0x5: OffsetType = "RDP Data File"; break;
                    case 0x6: OffsetType = "RDP Patch File"; break;
                    case 0xC: OffsetType = "Current RES File"; break;
                    case 0xD: OffsetType = "Current RES File"; break;
                    default: OffsetType = "NoSet (External RDP Exclusion)"; break;
                }
            }

            public void SetNames(string name, string type, string path = null, string subPath = null, string extraPath = null)
            {
                Name = name;
                Type = type;
                Path = path;
                SubPath = subPath;
                ExtraPath = extraPath;
            }
        }

        private readonly byte[] fileData;
        private readonly UnpackRES unpacker;
        private readonly DataHelper dataHelper;
        private readonly string outputFolder;

        public TOC(byte[] fileData, string resFileName, string outputFolder, DataHelper dataHelper)
        {
            this.fileData = fileData ?? throw new ArgumentNullException(nameof(fileData));
            this.unpacker = new UnpackRES(fileData, resFileName, outputFolder, dataHelper);
            this.dataHelper = dataHelper ?? throw new ArgumentNullException(nameof(dataHelper));
            this.outputFolder = outputFolder;
        }

        public (List<TOCEntry> entries, List<(string path, uint entryCount)> nestedFiles) ProcessGroup(GroupData group)
        {
            List<TOCEntry> entries = new List<TOCEntry>();
            List<(string path, uint entryCount)> nestedFiles = new List<(string, uint)>(); // Preserve order

            if (group.EntryCount == 0 || group.EntryOffset == 0)
                return (entries, nestedFiles);

            using (MemoryStream ms = new MemoryStream(fileData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                if (group.EntryOffset >= ms.Length)
                {
                    Console.WriteLine($"Error: Group EntryOffset 0x{group.EntryOffset:X8} exceeds file length 0x{ms.Length:X8}");
                    return (entries, nestedFiles);
                }

                ms.Position = group.EntryOffset;
                Console.WriteLine($"=====TOC Group (Offset: 0x{group.EntryOffset:X8})=====");

                for (uint i = 0; i < group.EntryCount; i++)
                {
                    long entryStart = ms.Position;
                    if (entryStart + 32 > ms.Length)
                    {
                        Console.WriteLine($"Error: Not enough data for TOC entry {i + 1} at 0x{entryStart:X8} (need 32 bytes, file length 0x{ms.Length:X8})");
                        break;
                    }

                    TOCEntry entry = ReadTOCEntry(reader);
                    if (entry == null)
                    {
                        Console.WriteLine($"Error: Failed to read TOC entry {i + 1} at 0x{entryStart:X8}");
                        break;
                    }

                    bool isDummy = entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize > 0;
                    bool isFullyZeroed = entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize == 0;

                    if (isFullyZeroed)
                    {
                        Console.WriteLine($"TOC Entry {i + 1}: Empty (fully zeroed)");
                        ms.Position = entryStart + 32;
                        continue;
                    }

                    if (isDummy)
                    {
                        Console.WriteLine($"Detected dummy entry: DSize = {entry.DSize}");
                        dataHelper.AddDummyFileEntry(entry);
                        ms.Position = entryStart + 32;
                        entries.Add(entry);
                        continue;
                    }

                    if (entry.NameOffset > 0 && entry.ChunkName > 0)
                    {
                        if (!ProcessNameData(reader, entry))
                        {
                            Console.WriteLine($"Error: Failed to process name data for TOC entry {i + 1} at 0x{entryStart:X8}");
                            break;
                        }
                    }

                    PrintTOCEntry(i + 1, entry);
                    entries.Add(entry);
                    unpacker.ExtractFile(entry); // Extract immediately

                    // Track nested files in order
                    string fullPath = GenerateFullPath(entry);
                    string extension = Path.GetExtension(fullPath).ToLower();
                    if (extension == ".res" || extension == ".rtbl")
                    {
                        nestedFiles.Add((fullPath, GetNestedEntryCount(fullPath)));
                    }

                    ms.Position = entryStart + 32;
                }
            }

            return (entries, nestedFiles);
        }

        private uint GetNestedEntryCount(string filePath)
        {
            if (!File.Exists(filePath))
                return 0;

            byte[] fileData = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(fileData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                if (ms.Length < 16) // Header size up to GroupCount
                    return 0;

                int magic = reader.ReadInt32();
                if (magic != PRES.HEADER_MAGIC)
                    return 0; // Not a valid .res file

                int groupOffset = reader.ReadInt32();
                byte groupCount = reader.ReadByte();
                if (groupCount == 0 || groupOffset >= ms.Length)
                    return 0;

                ms.Position = groupOffset;
                if (ms.Position + 8 > ms.Length)
                    return 0;

                uint entryOffset = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();
                return entryCount;
            }
        }

        private string GenerateFullPath(TOCEntry entry)
        {
            string fileName = entry.Type != null ? $"{entry.Name}.{entry.Type}" : entry.Name ?? "unnamed";
            if (entry.SubPath != null)
            {
                string[] subPathParts = entry.SubPath.Split('/');
                string lastPart = subPathParts[subPathParts.Length - 1];
                if (lastPart == fileName)
                    return Path.Combine(outputFolder, entry.SubPath);
                return Path.Combine(outputFolder, entry.SubPath, fileName);
            }
            else if (entry.Path != null)
            {
                return Path.Combine(outputFolder, entry.Path, fileName);
            }
            return Path.Combine(outputFolder, fileName);
        }

        private TOCEntry ReadTOCEntry(BinaryReader reader)
        {
            try
            {
                uint offset = reader.ReadUInt32();
                uint csize = reader.ReadUInt32();
                uint nameOffset = reader.ReadUInt32();
                uint chunkName = reader.ReadUInt32();
                reader.ReadBytes(12); // Skip padding
                uint dsize = reader.ReadUInt32();
                return new TOCEntry(offset, csize, nameOffset, chunkName, dsize);
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        private bool ProcessNameData(BinaryReader reader, TOCEntry entry)
        {
            if (entry.NameOffset >= fileData.Length)
                return false;

            reader.BaseStream.Position = entry.NameOffset;
            if (reader.BaseStream.Position + 20 > reader.BaseStream.Length)
                return false;

            uint nameOffset = reader.ReadUInt32();
            uint typeOffset = reader.ReadUInt32();
            uint pathOffset = reader.ReadUInt32();
            uint subPathOffset = reader.ReadUInt32();
            uint extraPathOffset = reader.ReadUInt32();

            string name = ReadString(reader, nameOffset);
            string type = null, path = null, subPath = null, extraPath = null;

            switch (entry.ChunkName)
            {
                case 1:
                    break;
                case 3:
                    type = ReadString(reader, typeOffset);
                    if (IsValidPathOffset(reader, pathOffset))
                        path = ReadString(reader, pathOffset);
                    break;
                case 4:
                    type = ReadString(reader, typeOffset);
                    if (IsValidPathOffset(reader, subPathOffset))
                        subPath = ReadString(reader, subPathOffset);
                    break;
                case 5:
                    type = ReadString(reader, typeOffset);
                    if (IsValidPathOffset(reader, subPathOffset))
                        subPath = ReadString(reader, subPathOffset);
                    if (IsValidPathOffset(reader, extraPathOffset))
                        extraPath = ReadString(reader, extraPathOffset);
                    break;
            }

            entry.SetNames(name, type, path, subPath, extraPath);
            return true;
        }

        private bool IsValidPathOffset(BinaryReader reader, uint offset)
        {
            if (offset == 0 || offset >= fileData.Length)
                return false;

            long originalPosition = reader.BaseStream.Position;
            reader.BaseStream.Position = offset;
            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
            {
                reader.BaseStream.Position = originalPosition;
                return false;
            }

            byte first = reader.ReadByte();
            byte second = reader.ReadByte();
            bool isValid = !(first == 0 && second == 0) && first != 0;
            reader.BaseStream.Position = originalPosition;
            return isValid;
        }

        private string ReadString(BinaryReader reader, uint offset)
        {
            if (offset == 0 || offset >= fileData.Length)
                return null;

            reader.BaseStream.Position = offset;
            var bytes = new List<byte>();
            byte b;
            while (reader.BaseStream.Position < reader.BaseStream.Length && (b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : null;
        }

        private void PrintTOCEntry(uint index, TOCEntry entry)
        {
            Console.WriteLine($"TOC Entry {index}:");
            Console.WriteLine($"  Raw Offset: 0x{entry.Offset:X8}");
            Console.WriteLine($"  Offset Type: {entry.OffsetType}");
            Console.WriteLine($"  Actual Offset: 0x{entry.ActualOffset:X8}");
            Console.WriteLine($"  CSize: {entry.CSize} (0x{entry.CSize:X8})");
            Console.WriteLine($"  End Offset: 0x{entry.EndOffset:X8}");
            Console.WriteLine($"  NameOffset: 0x{entry.NameOffset:X8}");
            Console.WriteLine($"  ChunkName: {entry.ChunkName}");
            Console.WriteLine($"  DSize: {entry.DSize}");

            if (entry.Name != null)
            {
                Console.WriteLine($"  Name: {entry.Name}");
                if (entry.Type != null) Console.WriteLine($"  Type: {entry.Type}");
                if (entry.Path != null) Console.WriteLine($"  Path: {entry.Path}");
                if (entry.SubPath != null) Console.WriteLine($"  SubPath: {entry.SubPath}");
                if (entry.ExtraPath != null) Console.WriteLine($"  ExtraPath: {entry.ExtraPath}");
            }
        }
    }
}