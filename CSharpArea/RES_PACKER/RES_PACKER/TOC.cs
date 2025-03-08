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
                    case 0x0:
                        OffsetType = "BIN/MODULE (External File)";
                        break;
                    case 0x4:
                        OffsetType = "RDP Package File";
                        break;
                    case 0x5:
                        OffsetType = "RDP Data File";
                        break;
                    case 0x6:
                        OffsetType = "RDP Patch File";
                        break;
                    case 0xC:
                        OffsetType = "Current RES File";
                        break;
                    case 0xD:
                        OffsetType = "Current RES File";
                        break;
                    default:
                        OffsetType = "NoSet (External RDP Exclusion)";
                        break;
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

        public TOC(byte[] fileData, string resFileName, string outputFolder, DataHelper dataHelper)
        {
            this.fileData = fileData;
            this.unpacker = new UnpackRES(fileData, resFileName, outputFolder, dataHelper);
            this.dataHelper = dataHelper;
        }

        public void ProcessGroup(GroupData group)
        {
            if (group.EntryCount == 0 || group.EntryOffset == 0)
                return;

            using (MemoryStream ms = new MemoryStream(fileData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                if (group.EntryOffset >= ms.Length)
                {
                    Console.WriteLine($"Error: Group EntryOffset 0x{group.EntryOffset:X8} exceeds file length 0x{ms.Length:X8}");
                    return;
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

                    // Only skip if ALL fields, including DSize, are zero
                    if (entry.Offset == 0 && entry.CSize == 0 && entry.NameOffset == 0 && entry.ChunkName == 0 && entry.DSize == 0)
                    {
                        Console.WriteLine($"TOC Entry {i + 1}: Empty (fully zeroed)");
                        ms.Position = entryStart + 32;
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
                    unpacker.ExtractFile(entry); // Process all entries, including dummies
                    ms.Position = entryStart + 32;
                }
            }
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