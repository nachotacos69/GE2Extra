using System;
using System.IO;
using System.Collections.Generic;

namespace RES_PACKER
{
    public class PRES
    {
        public const int HEADER_MAGIC = 0x73657250;

        public int GroupOffset { get; private set; }
        public byte GroupCount { get; private set; }
        public byte GroupVersion { get; private set; }
        public short Checksum { get; private set; }
        public int Version { get; private set; }
        public uint ChunkDatasOffset { get; private set; }
        public uint SideloadResOffset { get; private set; }
        public uint SideloadResSize { get; private set; }
        public List<GroupData> Groups { get; private set; } = new List<GroupData>();
        private static bool isRootProcessed = false;

        public void ProcessRES(byte[] fileData, string fileName, string outputFolder)
        {
            DataHelper dataHelper = new DataHelper(fileName);

            try
            {
                using (MemoryStream ms = new MemoryStream(fileData))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    if (ms.Length < 4)
                        throw new Exception("File too short to contain magic number");

                    int magic = reader.ReadInt32();
                    if (magic != HEADER_MAGIC)
                        throw new Exception("Invalid RES file: Magic number mismatch");

                    if (ms.Position + 28 > ms.Length)
                        throw new Exception("File too short to contain full header");

                    GroupOffset = reader.ReadInt32();
                    GroupCount = reader.ReadByte();
                    GroupVersion = reader.ReadByte();
                    Checksum = reader.ReadInt16();
                    Version = reader.ReadInt32();
                    ChunkDatasOffset = reader.ReadUInt32();
                    SideloadResOffset = reader.ReadUInt32();
                    SideloadResSize = reader.ReadUInt32();

                    dataHelper.SetHeaderData(this);

                    PrintHeaderInfo();

                    if (GroupCount > 0 && GroupOffset > 0)
                    {
                        if (GroupOffset >= ms.Length)
                            throw new Exception($"GroupOffset (0x{GroupOffset:X8}) exceeds file length (0x{ms.Length:X8})");

                        ms.Position = GroupOffset;
                        ProcessGroups(reader, GroupCount, dataHelper);
                        Groups.Sort((a, b) => b.EntryCount.CompareTo(a.EntryCount));
                        PrintGroups();

                        TOC toc = new TOC(fileData, fileName, outputFolder, dataHelper);
                        UnpackRES unpacker = new UnpackRES(fileData, fileName, outputFolder, dataHelper);

                        foreach (var group in Groups)
                        {
                            if (group.EntryCount > 0 && group.EntryOffset > 0)
                            {
                                if (group.EntryOffset >= ms.Length)
                                    throw new Exception($"EntryOffset (0x{group.EntryOffset:X8}) exceeds file length (0x{ms.Length:X8})");

                                toc.ProcessGroup(group);
                                unpacker.ProcessNestedFiles();
                            }
                        }

                        dataHelper.SaveToJson();
                    }

                    if (!isRootProcessed && fileName.EndsWith("system.res", StringComparison.OrdinalIgnoreCase))
                    {
                        isRootProcessed = true;
                        Console.WriteLine("Saving initial indexes before RTBL processing...");
                        UnpackRES.SaveAllIndexes(); // Save initial RESINDEX.json with .res and .rtbl lists
                        Console.WriteLine("Processing RTBL files from RESINDEX...");
                        RTBL.ProcessRTBLFromIndex(outputFolder, dataHelper);
                        Console.WriteLine("Saving updated indexes after RTBL processing...");
                        UnpackRES.SaveAllIndexes(); // Save again with updated .res from RTBL
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RES file '{fileName}': {ex.Message}");
                throw;
            }
            finally
            {
                Console.WriteLine("RES file unpacking completed.");
            }
        }

        private void ProcessGroups(BinaryReader reader, byte groupCount, DataHelper dataHelper)
        {
            for (int i = 0; i < groupCount; i++)
            {
                if (reader.BaseStream.Position + 8 > reader.BaseStream.Length)
                    throw new Exception($"Not enough data to read group {i + 1} (need 8 bytes, position 0x{reader.BaseStream.Position:X8})");

                uint entryOffset = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();
                Groups.Add(new GroupData(entryOffset, entryCount));
                dataHelper.AddGroup(entryOffset, entryCount);
            }
        }

        private void PrintHeaderInfo()
        {
            Console.WriteLine("=====Header=====");
            Console.WriteLine($"Group Offset: 0x{GroupOffset:X8}");
            Console.WriteLine($"Group Count: {GroupCount}");
            Console.WriteLine($"Group Version: {GroupVersion}");
            Console.WriteLine($"Checksum: 0x{Checksum:X4}");
            Console.WriteLine($"Version: {Version}");
            Console.WriteLine($"Chunk Datas Offset: 0x{ChunkDatasOffset:X8}");
            Console.WriteLine($"Sideload RES Offset: 0x{SideloadResOffset:X8}");
            Console.WriteLine($"Sideload RES Size: {SideloadResSize}");
        }

        private void PrintGroups()
        {
            Console.WriteLine("=====Groups=====");
            for (int i = 0; i < Groups.Count; i++)
            {
                Console.WriteLine($"Group {i + 1}:");
                if (Groups[i].EntryCount == 0 && Groups[i].EntryOffset == 0)
                {
                    Console.WriteLine("  Empty");
                }
                else
                {
                    Console.WriteLine($"  Entry Offset: 0x{Groups[i].EntryOffset:X8}");
                    Console.WriteLine($"  Entry Count: {Groups[i].EntryCount}");
                }
            }
        }
    }

    public class GroupData
    {
        public uint EntryOffset { get; }
        public uint EntryCount { get; }

        public GroupData(uint entryOffset, uint entryCount)
        {
            EntryOffset = entryOffset;
            EntryCount = entryCount;
        }
    }
}