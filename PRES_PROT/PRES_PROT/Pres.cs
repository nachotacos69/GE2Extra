using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PRES_PROT
{
    public class PRES
    {
        public static void ProcessFile(string filePath)
        {
            try
            {
                // Ensure the file exists
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    return;
                }

                // Use a fresh FileStream and BinaryReader for each file
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    Console.WriteLine($"Reading RES structure: {filePath}");

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

                    /*=====DEBUG PRINT=====
                    Console.WriteLine($"Header: {header}");
                    Console.WriteLine($"Group Offset: 0x{groupOffset:X}");
                    Console.WriteLine($"Group Count: {groupCount}");
                    Console.WriteLine($"Group Version: {groupVersion}");
                    Console.WriteLine($"Checksum: {checksum}");
                    Console.WriteLine($"Version: {version}");
                    Console.WriteLine($"Chunk Data 1: 0x{chunkData1:X}");
                    Console.WriteLine($"Chunk Data 2: 0x{chunkData2:X}");
                    Console.WriteLine($"Chunk Data 3: {chunkData3}");
                    Console.WriteLine($"Chunk Data 4: {chunkData4}");
                    =====DEBUG PRINT===== */

                    List<(uint entryData, uint entryCount)> entryList = ProcessGroupData(reader, groupOffset, groupCount);
                    List<TOCEntry> tocEntries = new List<TOCEntry>();

                    foreach (var (entryData, entryCount) in entryList)
                    {
                        ProcessTOC(reader, entryData, entryCount, tocEntries);
                    }

                    // Extract files and process nested archives
                    Data.ExtractFiles(filePath, tocEntries);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
            }
        }

        private static List<(uint entryData, uint entryCount)> ProcessGroupData(BinaryReader reader, int groupOffset, byte groupCount)
        {
            List<(uint entryData, uint entryCount)> entryList = new List<(uint, uint)>();

            reader.BaseStream.Seek(groupOffset, SeekOrigin.Begin);
            Console.WriteLine("\nProcessing Group Data...");

            for (int i = 0; i < groupCount; i++)
            {
                uint entryData = reader.ReadUInt32(); // finds a valid Offset
                uint entryCount = reader.ReadUInt32();  // finds a valid Count

                if (entryData == 0 && entryCount == 0)
                {
                    Console.WriteLine($"Entry {i}: Empty");
                }
                else
                {
                    Console.WriteLine($"Entry {i}: Data Offset = 0x{entryData:X}, Count = {entryCount}");
                    entryList.Add((entryData, entryCount));
                }
            }

            return entryList;
        }

        private static void ProcessTOC(BinaryReader reader, uint tocOffset, uint tocCount, List<TOCEntry> tocEntries)
        {
            reader.BaseStream.Seek(tocOffset, SeekOrigin.Begin);
            //DEBUG PRINT    Console.WriteLine($"\nProcessing TOC at offset 0x{tocOffset:X} with {tocCount} entries...");

            int totalBytes = (int)(tocCount * 32);
            byte[] tocData = reader.ReadBytes(totalBytes);

            for (uint i = 0; i < tocCount; i++)
            {
                int entryStart = (int)(i * 32); // entryCount multiply by 32 bytes
                byte[] entryBytes = new byte[32];
                Array.Copy(tocData, entryStart, entryBytes, 0, 32);

                bool isEmpty = true;
                for (int j = 0; j < 16; j++)
                {
                    if (entryBytes[j] != 0)
                    {
                        isEmpty = false;
                        break;
                    }
                }

                if (isEmpty)
                {
                    //DEBUG PRINT        Console.WriteLine($"TOC Entry {i}: Skipped (Empty)");
                    continue;
                }

                using (MemoryStream ms = new MemoryStream(entryBytes))
                using (BinaryReader tocReader = new BinaryReader(ms))
                {
                    uint tocOffRaw = tocReader.ReadUInt32();
                    uint tocCSize = tocReader.ReadUInt32();
                    uint tocName = tocReader.ReadUInt32();
                    uint tocNameVal = tocReader.ReadUInt32();
                    tocReader.BaseStream.Seek(12, SeekOrigin.Current);
                    uint tocDSize = tocReader.ReadUInt32();

                    uint marker = tocOffRaw & 0xF0000000;
                    uint tocOff = tocOffRaw & 0x0FFFFFFF;

                    string markerType = GetMarkerType(marker);
                    uint rdpOffset = (markerType.Contains(".rdp")) ? tocOff * 0x800 : tocOff;

                    TOCEntry entry = new TOCEntry
                    {
                        TOC_OFF = tocOffRaw,
                        TOC_CSIZE = tocCSize,
                        Name = "",
                        Type = "",
                        Path = "",
                        Path2 = "",
                        NoSetPath = "",
                        AbsoluteOffset = tocOff,
                        IsRDP = markerType.Contains(".rdp"),
                        RDPFileName = markerType.Contains("package") ? "package.rdp" :
                                      markerType.Contains("data") ? "data.rdp" :
                                      markerType.Contains("patch") ? "patch.rdp" : null,
                        RDPAbsoluteOffset = rdpOffset
                    };

                    if (tocName != 0 && tocNameVal > 0)
                    {
                        ProcessNameStructure(reader, tocName, tocNameVal, ref entry);
                    }

                    tocEntries.Add(entry);
                }
            }
        }

        private static string GetMarkerType(uint marker)
        {
            if (marker == 0x30000000) return "NoSet";
            if (marker == 0x40000000) return "package.rdp";
            if (marker == 0x50000000) return "data.rdp";
            if (marker == 0x60000000) return "patch.rdp";
            if (marker == 0xC0000000 || marker == 0xD0000000) return "Current File";
            return "Unknown";
        }

        private static void ProcessNameStructure(BinaryReader reader, uint nameOffset, uint nameValue, ref TOCEntry entry)
        {
            reader.BaseStream.Seek(nameOffset, SeekOrigin.Begin);
            byte[] nameData = reader.ReadBytes((int)(nameValue * 4));

            using (MemoryStream ms = new MemoryStream(nameData))
            using (BinaryReader nameReader = new BinaryReader(ms))
            {
                uint namePtr = nameReader.ReadUInt32();
                uint typePtr = (nameValue > 1) ? nameReader.ReadUInt32() : 0;
                uint pathPtr = (nameValue > 2) ? nameReader.ReadUInt32() : 0;
                uint path2Ptr = (nameValue > 3) ? nameReader.ReadUInt32() : 0;
                uint noSetPathPtr = (nameValue > 4) ? nameReader.ReadUInt32() : 0;

                if (namePtr != 0) entry.Name = ReadString(reader, namePtr);
                if (typePtr != 0) entry.Type = ReadString(reader, typePtr);
                if (pathPtr != 0) entry.Path = ReadString(reader, pathPtr);
                if (path2Ptr != 0) entry.Path2 = ReadString(reader, path2Ptr);
                if (noSetPathPtr != 0) entry.NoSetPath = ReadString(reader, noSetPathPtr);
            }
        }

        private static string ReadString(BinaryReader reader, uint offset)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            List<byte> byteList = new List<byte>();

            byte currentByte;
            while ((currentByte = reader.ReadByte()) != 0)
            {
                byteList.Add(currentByte);
            }

            return Encoding.UTF8.GetString(byteList.ToArray());
        }
    }
}