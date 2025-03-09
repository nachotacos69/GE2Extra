using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RES_PACKER
{
    public class PackRES
    {
        private readonly string resFileName;
        private readonly string jsonFileName;
        private readonly string outputFolder;
        private DataHelper.ResFileData resData;

        public PackRES(string resFileName, string outputFolder)
        {
            this.resFileName = resFileName;
            this.jsonFileName = Path.ChangeExtension(resFileName, ".json");
            this.outputFolder = outputFolder;
        }

        public void Pack()
        {
            try
            {
                if (!File.Exists(jsonFileName))
                    throw new Exception($"JSON file '{jsonFileName}' not found.");
                string jsonContent = File.ReadAllText(jsonFileName);
                resData = JsonConvert.DeserializeObject<DataHelper.ResFileData>(jsonContent);

                string outputResFile = Path.Combine(outputFolder, Path.GetFileName(resFileName));
                if (!File.Exists(resFileName))
                    throw new Exception($"RES file '{resFileName}' not found.");
                File.Copy(resFileName, outputResFile, true);

                using (var resStream = new FileStream(outputResFile, FileMode.Open, FileAccess.ReadWrite))
                {
                    foreach (var fileEntry in resData.Files)
                    {
                        ProcessFileEntry(fileEntry, resStream);
                    }
                }

                Console.WriteLine($"Repacked RES file saved to {outputResFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error repacking RES file '{resFileName}': {ex.Message}");
                throw;
            }
        }

        private void ProcessFileEntry(DataHelper.FileEntry fileEntry, FileStream resStream)
        {
            if (fileEntry.OffsetType == "dummy")
            {
                Console.WriteLine($"Skipping processing for dummy entry: DSize = {fileEntry.DSize}");
                UpdateTOC(fileEntry, resStream, fileEntry.CSize, fileEntry.DSize);
                return;
            }

            string filePath = fileEntry.FileName;
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Warning: File '{filePath}' not found, skipping.");
                UpdateTOC(fileEntry, resStream, fileEntry.CSize, fileEntry.DSize);
                return;
            }

            byte[] newData = File.ReadAllBytes(filePath);
            uint newDSize = (uint)newData.Length;
            byte[] finalData;
            uint newCSize;

            uint actualOffset = fileEntry.Offset & 0x0FFFFFFF;
            if (fileEntry.RDP)
                actualOffset *= 0x800;

            Console.WriteLine($"Processing {filePath}: OFFSET: 0x{actualOffset:X8}, Compression = {fileEntry.Compression}, Original Size = {newDSize}");

            if (fileEntry.Compression)
            {
                finalData = Compression.LeCompression(newData);
                newCSize = (uint)finalData.Length;
                Console.WriteLine($"Compressed: New CSize = {newCSize}");
            }
            else
            {
                finalData = newData;
                newCSize = newDSize;
                Console.WriteLine($"Uncompressed (Compression = false): CSize = {newCSize}, Data Length = {finalData.Length}");
            }

            if (fileEntry.RDP)
            {
                UpdateRDPFile(fileEntry, actualOffset, finalData, newCSize, newDSize);
            }
            else
            {
                UpdateRESFile(fileEntry, actualOffset, finalData, newCSize, newDSize, resStream);
            }

            // Adjust newCSize for TOC to the aligned size
            uint alignedCSize = AlignTo0xF(newCSize);
            UpdateTOC(fileEntry, resStream, alignedCSize, newDSize);
        }

        private uint AlignTo0xF(uint size)
        {
            // Align size to the next 0xF boundary
            uint remainder = size % 16; // 0x10 = 16 bytes
            if (remainder == 0)
                return size;
            return size + (16 - remainder);
        }

        private void UpdateRESFile(DataHelper.FileEntry fileEntry, uint actualOffset, byte[] newData, uint newCSize, uint newDSize, FileStream resStream)
        {
            if (actualOffset + fileEntry.CSize > resStream.Length)
            {
                Console.WriteLine($"Error: Offset 0x{actualOffset:X8} exceeds RES file size for {fileEntry.FileName}");
                return;
            }

            resStream.Seek(actualOffset, SeekOrigin.Begin);
            uint alignedCSize = AlignTo0xF(newCSize);
            if (newCSize <= fileEntry.CSize)
            {
                resStream.Write(newData, 0, (int)newCSize);
                if (alignedCSize > newCSize)
                {
                    uint paddingSize = alignedCSize - newCSize;
                    byte[] padding = new byte[paddingSize];
                    Array.Clear(padding, 0, (int)paddingSize);
                    resStream.Write(padding, 0, (int)paddingSize);
                    Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                }
            }
            else
            {
                if (CheckRESOffsetFit(fileEntry, actualOffset, alignedCSize, resStream))
                {
                    resStream.Write(newData, 0, (int)newCSize);
                    if (alignedCSize > newCSize)
                    {
                        uint paddingSize = alignedCSize - newCSize;
                        byte[] padding = new byte[paddingSize];
                        Array.Clear(padding, 0, (int)paddingSize);
                        resStream.Write(padding, 0, (int)paddingSize);
                        Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                    }
                    Console.WriteLine($"Fit new data at 0x{actualOffset:X8}-0x{actualOffset + alignedCSize:X8} without shifting");
                }
                else
                {
                    ShiftDataInRES(resStream, actualOffset, fileEntry.CSize, alignedCSize);
                    resStream.Seek(actualOffset, SeekOrigin.Begin);
                    resStream.Write(newData, 0, (int)newCSize);
                    if (alignedCSize > newCSize)
                    {
                        uint paddingSize = alignedCSize - newCSize;
                        byte[] padding = new byte[paddingSize];
                        Array.Clear(padding, 0, (int)paddingSize);
                        resStream.Write(padding, 0, (int)paddingSize);
                        Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                    }
                }
            }
        }

        private void UpdateRDPFile(DataHelper.FileEntry fileEntry, uint actualOffset, byte[] newData, uint newCSize, uint newDSize)
        {
            string rdpFileName = GetRDPFileName(fileEntry.OffsetType);
            string originalRdpPath = Path.Combine(Directory.GetCurrentDirectory(), rdpFileName);
            string outputRdpPath = Path.Combine(outputFolder, rdpFileName);

            if (!File.Exists(originalRdpPath))
            {
                Console.WriteLine($"Error: Original RDP file '{originalRdpPath}' not found for {fileEntry.FileName}");
                return;
            }

            if (!File.Exists(outputRdpPath))
            {
                File.Copy(originalRdpPath, outputRdpPath, true);
                Console.WriteLine($"Copied RDP file to '{outputRdpPath}'");
            }

            using (var rdpStream = new FileStream(outputRdpPath, FileMode.Open, FileAccess.ReadWrite))
            {
                if (actualOffset + fileEntry.CSize > rdpStream.Length)
                {
                    Console.WriteLine($"Error: Offset 0x{actualOffset:X8} exceeds RDP file size for {fileEntry.FileName}");
                    return;
                }

                rdpStream.Seek(actualOffset, SeekOrigin.Begin);
                uint alignedCSize = AlignTo0xF(newCSize);
                if (newCSize <= fileEntry.CSize)
                {
                    rdpStream.Write(newData, 0, (int)newCSize);
                    if (alignedCSize > newCSize)
                    {
                        uint paddingSize = alignedCSize - newCSize;
                        byte[] padding = new byte[paddingSize];
                        Array.Clear(padding, 0, (int)paddingSize);
                        rdpStream.Write(padding, 0, (int)paddingSize);
                        Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                    }
                }
                else
                {
                    if (CheckRDPOffsetFit(fileEntry, actualOffset, alignedCSize))
                    {
                        rdpStream.Write(newData, 0, (int)newCSize);
                        if (alignedCSize > newCSize)
                        {
                            uint paddingSize = alignedCSize - newCSize;
                            byte[] padding = new byte[paddingSize];
                            Array.Clear(padding, 0, (int)paddingSize);
                            rdpStream.Write(padding, 0, (int)paddingSize);
                            Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                        }
                        Console.WriteLine($"Fit new data at 0x{actualOffset:X8}-0x{actualOffset + alignedCSize:X8} without shifting");
                    }
                    else if (CheckRDPOffsetOverlap(fileEntry, actualOffset, alignedCSize))
                    {
                        uint newActualOffset = FindNewRDPOffset(actualOffset, alignedCSize);
                        Console.WriteLine($"Offset overlap detected for {fileEntry.FileName}, reassigning to OFFSET: 0x{newActualOffset:X8}");
                        fileEntry.Offset = (fileEntry.Offset & 0xF0000000) | (newActualOffset / 0x800);
                        actualOffset = newActualOffset;

                        if (actualOffset + alignedCSize > rdpStream.Length)
                        {
                            rdpStream.SetLength(actualOffset + alignedCSize);
                        }
                        rdpStream.Seek(actualOffset, SeekOrigin.Begin);
                        rdpStream.Write(newData, 0, (int)newCSize);
                        if (alignedCSize > newCSize)
                        {
                            uint paddingSize = alignedCSize - newCSize;
                            byte[] padding = new byte[paddingSize];
                            Array.Clear(padding, 0, (int)paddingSize);
                            rdpStream.Write(padding, 0, (int)paddingSize);
                            Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                        }
                    }
                    else
                    {
                        ShiftDataInRDP(rdpStream, actualOffset, fileEntry.CSize, alignedCSize);
                        rdpStream.Seek(actualOffset, SeekOrigin.Begin);
                        rdpStream.Write(newData, 0, (int)newCSize);
                        if (alignedCSize > newCSize)
                        {
                            uint paddingSize = alignedCSize - newCSize;
                            byte[] padding = new byte[paddingSize];
                            Array.Clear(padding, 0, (int)paddingSize);
                            rdpStream.Write(padding, 0, (int)paddingSize);
                            Console.WriteLine($"Padded 0x{actualOffset + newCSize:X8}-0x{actualOffset + alignedCSize:X8} with {paddingSize} zeros to align to 0xF");
                        }
                    }
                }
            }
        }

        private bool CheckRESOffsetFit(DataHelper.FileEntry currentEntry, uint actualOffset, uint newCSize, FileStream resStream)
        {
            uint endOffset = actualOffset + newCSize;
            foreach (var entry in resData.Files)
            {
                if (entry == currentEntry || entry.RDP)
                    continue;

                uint otherOffset = entry.Offset & 0x0FFFFFFF;
                uint otherEndOffset = otherOffset + entry.CSize;

                if (actualOffset < otherEndOffset && endOffset > otherOffset)
                {
                    Console.WriteLine($"Overlap detected with {entry.FileName}: 0x{otherOffset:X8}-0x{otherEndOffset:X8}");
                    return false;
                }
            }
            return endOffset <= resStream.Length; // Fits if no overlap and within file length
        }

        private bool CheckRDPOffsetFit(DataHelper.FileEntry currentEntry, uint actualOffset, uint newCSize)
        {
            uint endOffset = actualOffset + newCSize;
            foreach (var entry in resData.Files)
            {
                if (entry == currentEntry || !entry.RDP || !entry.OffsetType.Equals(currentEntry.OffsetType))
                    continue;

                uint otherOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
                uint otherEndOffset = otherOffset + entry.CSize;

                if (actualOffset < otherEndOffset && endOffset > otherOffset)
                {
                    Console.WriteLine($"Overlap detected with {entry.FileName}: 0x{otherOffset:X8}-0x{otherEndOffset:X8}");
                    return false;
                }
            }
            return true; // Fits if no overlap (file length check handled separately)
        }

        private bool CheckRDPOffsetOverlap(DataHelper.FileEntry currentEntry, uint actualOffset, uint newCSize)
        {
            uint endOffset = actualOffset + newCSize;
            foreach (var entry in resData.Files)
            {
                if (entry == currentEntry || !entry.RDP || !entry.OffsetType.Equals(currentEntry.OffsetType))
                    continue;

                uint otherOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
                uint otherEndOffset = otherOffset + entry.CSize;

                if ((actualOffset < otherEndOffset && endOffset > otherOffset) ||
                    (otherOffset < endOffset && otherEndOffset > actualOffset))
                {
                    Console.WriteLine($"Overlap detected with {entry.FileName}: 0x{otherOffset:X8}-0x{otherEndOffset:X8}");
                    return true;
                }
            }
            return false;
        }

        private uint FindNewRDPOffset(uint originalOffset, uint newCSize)
        {
            uint newOffset = originalOffset;
            bool overlap;

            do
            {
                newOffset += 0x800;
                uint scaledOffset = newOffset / 0x800;
                if (newOffset % 0x800 != 0)
                {
                    newOffset = (scaledOffset + 1) * 0x800;
                }

                overlap = false;
                foreach (var entry in resData.Files)
                {
                    if (!entry.RDP || !entry.OffsetType.Equals(resData.Files[resData.Files.IndexOf(entry)].OffsetType))
                        continue;

                    uint otherOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
                    uint otherEndOffset = otherOffset + entry.CSize;

                    if (newOffset < otherEndOffset && (newOffset + newCSize) > otherOffset)
                    {
                        overlap = true;
                        break;
                    }
                }
            } while (overlap);

            return newOffset;
        }

        private void ShiftDataInRES(FileStream resStream, uint offset, uint oldSize, uint newSize)
        {
            long originalLength = resStream.Length;
            long shiftAmount = newSize - oldSize;
            resStream.SetLength(originalLength + shiftAmount);

            const int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];
            long currentPos = originalLength - 1;
            long newPos = currentPos + shiftAmount;

            while (currentPos >= offset + oldSize)
            {
                int bytesToRead = (int)Math.Min(bufferSize, currentPos - (offset + oldSize) + 1);
                resStream.Seek(currentPos - bytesToRead + 1, SeekOrigin.Begin);
                int bytesRead = resStream.Read(buffer, 0, bytesToRead);
                resStream.Seek(newPos - bytesToRead + 1, SeekOrigin.Begin);
                resStream.Write(buffer, 0, bytesRead);
                currentPos -= bytesToRead;
                newPos -= bytesToRead;
            }

            foreach (var entry in resData.Files)
            {
                uint entryActualOffset = entry.Offset & 0x0FFFFFFF;
                if (!entry.RDP && entryActualOffset > offset)
                {
                    entry.Offset = (entry.Offset & 0xF0000000) | (entryActualOffset + (newSize - oldSize));
                }
            }
        }

        private void ShiftDataInRDP(FileStream rdpStream, uint offset, uint oldSize, uint newSize)
        {
            long originalLength = rdpStream.Length;
            long shiftAmount = newSize - oldSize;
            rdpStream.SetLength(originalLength + shiftAmount);

            const int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];
            long currentPos = originalLength - 1;
            long newPos = currentPos + shiftAmount;

            while (currentPos >= offset + oldSize)
            {
                int bytesToRead = (int)Math.Min(bufferSize, currentPos - (offset + oldSize) + 1);
                rdpStream.Seek(currentPos - bytesToRead + 1, SeekOrigin.Begin);
                int bytesRead = rdpStream.Read(buffer, 0, bytesToRead);
                rdpStream.Seek(newPos - bytesToRead + 1, SeekOrigin.Begin);
                rdpStream.Write(buffer, 0, bytesRead);
                currentPos -= bytesToRead;
                newPos -= bytesToRead;
            }

            foreach (var entry in resData.Files)
            {
                uint entryActualOffset = (entry.Offset & 0x0FFFFFFF) * 0x800;
                if (entry.RDP && entryActualOffset > offset)
                {
                    uint newActualOffset = entryActualOffset + (newSize - oldSize);
                    entry.Offset = (entry.Offset & 0xF0000000) | (newActualOffset / 0x800);
                }
            }
        }

        private void UpdateTOC(DataHelper.FileEntry fileEntry, FileStream resStream, uint newCSize, uint newDSize)
        {
            int fileIndex = resData.Files.IndexOf(fileEntry);
            int totalEntriesBefore = 0;
            long tocOffset = -1;

            for (int i = 0; i < resData.Groups.Count; i += 2)
            {
                int groupOffset = resData.Groups[i];
                int groupCount = resData.Groups[i + 1];

                if (fileIndex >= totalEntriesBefore && fileIndex < totalEntriesBefore + groupCount)
                {
                    int entryInGroup = fileIndex - totalEntriesBefore;
                    tocOffset = groupOffset + (entryInGroup * 32);
                    break;
                }
                totalEntriesBefore += groupCount;
            }

            if (tocOffset == -1 || tocOffset + 32 > resStream.Length)
            {
                Console.WriteLine($"Error: Could not locate TOC offset for {fileEntry.FileName ?? "dummy"} (index {fileIndex})");
                return;
            }

            Console.WriteLine($"Updating TOC for {fileEntry.FileName ?? "dummy"} at OFFSET: 0x{tocOffset:X8}");
            resStream.Seek(tocOffset, SeekOrigin.Begin);
            using (var writer = new BinaryWriter(resStream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(fileEntry.Offset);
                writer.Write(newCSize);
                writer.Write(fileEntry.OffsetName);
                writer.Write(fileEntry.ChunkName);
                writer.Write(new byte[12], 0, 12);
                writer.Write(newDSize);
            }
        }

        private string GetRDPFileName(string offsetType)
        {
            switch (offsetType)
            {
                case "RDP Package File": return "package.rdp";
                case "RDP Data File": return "data.rdp";
                case "RDP Patch File": return "patch.rdp";
                default: throw new Exception($"Unknown RDP OffsetType: {offsetType}");
            }
        }
    }
}