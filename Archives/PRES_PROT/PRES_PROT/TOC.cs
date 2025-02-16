using System;
using System.IO;

class TOC
{
    public static void ParseTOC(BinaryReader reader, uint TOC_Offset, uint TOC_Count, string outputDirectory)
    {
        if (TOC_Count == 0)
        {
            TOC_Count = 1;
        }

        for (int i = 0; i < TOC_Count; i++)
        {
            long entryOffset = TOC_Offset + (i * 32);
            reader.BaseStream.Seek(entryOffset, SeekOrigin.Begin);

            uint raw_TOC_OFF = reader.ReadUInt32(); // Read raw offset with marker
            uint TOC_CSIZE = reader.ReadUInt32();
            uint TOC_NAME = reader.ReadUInt32();
            uint TOC_NAMEVAL = reader.ReadUInt32();

            reader.BaseStream.Seek(12, SeekOrigin.Current); // Skip padding
            uint TOC_DSIZE = reader.ReadUInt32(); // Decompressed size

            // Extract TOC Marker (remove unnecessary zeroes when printing out)
            uint TOC_MARKER = (raw_TOC_OFF & 0xF0000000) >> 28;
            uint TOC_OFF = raw_TOC_OFF & 0x0FFFFFFF; // Remove marker to get actual offset

            // Check if the marker matches RDP files (4, 5, 6) and adjust offset
            if (TOC_MARKER == 4 || TOC_MARKER == 5 || TOC_MARKER == 6)
            {
                TOC_OFF *= 0x800; // Convert to absolute offset
            }

            // Skip TOCs that have the first 16 bytes as zero (BUT do not reduce TOC_Count)
            if (raw_TOC_OFF == 0 && TOC_CSIZE == 0 && TOC_NAME == 0 && TOC_NAMEVAL == 0)
            {
                continue; // Don't print or count empty TOCs
            }

            //debug   int tocIndex = i + 1; // Start numbering from 1 default :)
            //debug   Console.WriteLine($"\nTOC {tocIndex}:");
            //debug    Console.WriteLine($"  Raw Offset: 0x{raw_TOC_OFF:X} (Marker: 0x{TOC_MARKER:X})");
            //debug     Console.WriteLine($"  Interpreted Offset: 0x{TOC_OFF:X}");
            //debug     Console.WriteLine($"  End Offset: 0x{(TOC_OFF + TOC_CSIZE):X}");
            //debug     Console.WriteLine($"  Compressed Size: {TOC_CSIZE}");
            //debug     Console.WriteLine($"  Name Offset: 0x{TOC_NAME:X}");
            //debug     Console.WriteLine($"  Name Value: {TOC_NAMEVAL}");
            //debug    Console.WriteLine($"  Decompressed Size: {TOC_DSIZE}");

            // Call Name Table processing
            if (TOC_NAME != 0 && TOC_NAMEVAL > 0)
            {
                var nameData = NStruct.ParseName(reader, TOC_NAME, TOC_NAMEVAL);
                if (nameData != null)
                {
                    //debug          Console.WriteLine($"===> Name Structure for TOC {tocIndex}");
                    //debug          if (!string.IsNullOrEmpty(nameData.Name)) Console.WriteLine($"  Name: {nameData.Name}");
                    //debug          if (!string.IsNullOrEmpty(nameData.Type)) Console.WriteLine($"  Type: {nameData.Type}");
                    //debug          if (!string.IsNullOrEmpty(nameData.Path)) Console.WriteLine($"  Path: {nameData.Path}");
                    //debug          if (!string.IsNullOrEmpty(nameData.Path2)) Console.WriteLine($"  Path2: {nameData.Path2}");
                    //debug          if (!string.IsNullOrEmpty(nameData.NoSetPath)) Console.WriteLine($"  NoSetPath: {nameData.NoSetPath}");

                    if (TOC_MARKER == 4 || TOC_MARKER == 5 || TOC_MARKER == 6)
                    {
                        Data.HandleMarker(TOC_MARKER, TOC_OFF, TOC_CSIZE, nameData.Name, nameData.Type, outputDirectory);
                    }
                    else
                    {
                        Data.ExtractFile(reader, TOC_OFF, TOC_OFF + TOC_CSIZE, TOC_CSIZE, nameData.Name, nameData.Type, nameData.Path, nameData.Path2, nameData.NoSetPath, outputDirectory);
                    }
                }
            }
        }
    }
}