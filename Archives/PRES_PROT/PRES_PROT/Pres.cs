using System;
using System.IO;
using System.Collections.Generic;

class Pres
{
    class GroupEntry
    {
        public uint DataOffset;
        public uint Count;
    }

    public static void ParseResFile(string filePath, string outputDirectory)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found: " + filePath);
            return;
        }

        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read)))
        {
            // Read Header
            int header = reader.ReadInt32();
            int groupOffset = reader.ReadInt32();
            byte groupCount = reader.ReadByte();
            byte groupVersion = reader.ReadByte();
            ushort checksum = reader.ReadUInt16();
            int version = reader.ReadInt32();

            uint chunkData1 = reader.ReadUInt32();
            uint chunkData2 = reader.ReadUInt32();
            uint chunkData3 = reader.ReadUInt32();
            uint chunkData4 = reader.ReadUInt32();

            // Print Header Data
            //debug       Console.WriteLine("[Header]");
            //debug        Console.WriteLine($"Header: {header}");
            //debug       Console.WriteLine($"Group Offset: 0x{groupOffset:X}");
            //debug       Console.WriteLine($"Group Count: {groupCount}");
            //debug       Console.WriteLine($"Group Version: {groupVersion}");
            //debug        Console.WriteLine($"Checksum: 0x{checksum:X}");
            //debug       Console.WriteLine($"Version: {version}");
            //debug       Console.WriteLine($"ChunkData1 Offset: 0x{chunkData1:X}");
            //debug       Console.WriteLine($"ChunkData2 Offset: 0x{chunkData2:X}");
            //debug       Console.WriteLine($"ChunkData3: {chunkData3}");
            //debug       Console.WriteLine($"ChunkData4: {chunkData4}");

            // Read Group Data first and store in a list
            List<GroupEntry> groupEntries = new List<GroupEntry>();

            if (groupOffset > 0)
            {
                reader.BaseStream.Seek(groupOffset, SeekOrigin.Begin);
                //debug     Console.WriteLine("\n[Group Data]");

                for (int i = 0; i < groupCount; i++)
                {
                    uint GD_EntryData = reader.ReadUInt32();
                    uint GD_EntryCount = reader.ReadUInt32();
                    groupEntries.Add(new GroupEntry { DataOffset = GD_EntryData, Count = GD_EntryCount });
                }
            }

            // Now print all collected group entries
            for (int i = 0; i < groupEntries.Count; i++)
            {
                //debug    Console.WriteLine($"Entry {i}: Data Offset: 0x{groupEntries[i].DataOffset:X}, Count: {groupEntries[i].Count}\n\n");
            }

            // Now process TOCs using the collected entries
            foreach (var entry in groupEntries)
            {
                if (entry.DataOffset > 0 && entry.Count > 0)
                {
                    TOC.ParseTOC(reader, entry.DataOffset, entry.Count, outputDirectory);
                }
            }
        }
    }
}
