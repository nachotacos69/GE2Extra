using System;
using System.IO;
using System.Collections.Generic;
using GIL.FUNCTION;
using System.Text.Json;
using System.Text;
using BLZ2_COMPRESSION;

namespace GEBCS
{
    class PresRepack
    {
        private Pres pres = new Pres();
        private BW writer;
        private string outFolder;
        private FileStream resStream; // Temp file stream
        private string originalResName;
        private bool isRtbl; // Flag to differentiate RTBL from .res
        private Dictionary<long, long> occupiedOffsets = new Dictionary<long, long>(); // Start -> End offsets

        public PresRepack(string resName)
        {
            pres = JsonSerializer.Deserialize<Pres>(File.ReadAllText(Path.ChangeExtension(resName, "json")));
            outFolder = Path.GetDirectoryName(resName) + "\\" + Path.GetFileNameWithoutExtension(resName) + "\\";
            originalResName = resName;
            string tempResName = Path.Combine(Path.GetDirectoryName(resName), $"temp_{Path.GetFileNameWithoutExtension(resName)}.res");
            isRtbl = Path.GetExtension(resName).ToLower() == ".rtbl";

            File.Copy(resName, tempResName, true);
            resStream = new FileStream(tempResName, FileMode.Open, FileAccess.ReadWrite);
            writer = new BW(resStream);

            if (!isRtbl)
            {
                writer.BaseStream.Seek(0, SeekOrigin.Begin);
                writer.Write(pres.Magic);
                writer.Write(pres.GrupOffset);
                writer.Write(pres.GrupCount);
                writer.Write(pres.GrupVersion);
                writer.Write(pres.CeckSum); // Use original checksum from JSON
                writer.Write(pres.Version);
                writer.Write(pres.ChunkData);
                writer.Write(new byte[12]); // 12-byte padding
                foreach (int i in pres.Grups)
                {
                    writer.Write(i);
                }
                pres.TocSize = 32 + (pres.TotalFile * 32);
            }
            else
            {
                pres.TocSize = 0; // Not used for RTBL
            }
        }

        public void RepackF(ref BW package, ref BW data, ref Dictionary<int, int> ptSeekSamePackage, ref Dictionary<int, int> ptSeekSameData, ref Dictionary<string, int> packageDict, ref Dictionary<string, int> dataDict)
        {
            long tocStart = 0;
            if (!isRtbl)
            {
                tocStart = writer.BaseStream.Position;
                for (int i = 0; i < pres.TotalFile; i++)
                {
                    writer.Write(new byte[32]); // Reserve space for TOC
                }
                occupiedOffsets[0] = pres.TocSize;
            }

            long lastPosition = isRtbl ? 0 : pres.TocSize;

            foreach (Record file in pres.Files)
            {
                Console.WriteLine("Repack: " + outFolder + file.FileName);
                if (file.Location == "Local")
                {
                    byte[] buffer = File.ReadAllBytes(outFolder + file.FileName);
                    byte[] dataToWrite = file.Compression ? Compression.LeCompression(buffer) : buffer;
                    file.Size = dataToWrite.Length;
                    long originalOffset = file.Offset;
                    long targetOffset = originalOffset;

                    bool offsetFree = true;
                    foreach (var range in occupiedOffsets)
                    {
                        if (targetOffset >= range.Key && targetOffset < range.Value)
                        {
                            offsetFree = false;
                            break;
                        }
                    }
                    if (!offsetFree || (targetOffset < pres.TocSize && !isRtbl))
                    {
                        targetOffset = lastPosition;
                    }
                    file.Offset = (int)targetOffset;

                    writer.BaseStream.Seek(targetOffset, SeekOrigin.Begin);
                    writer.Write(dataToWrite);
                    occupiedOffsets[targetOffset] = writer.BaseStream.Position;
                    lastPosition = Math.Max(lastPosition, writer.BaseStream.Position);

                    Console.WriteLine($"  Data - Original POS: 0x{originalOffset:X} // New POS: 0x{targetOffset:X}");

                    if (!isRtbl || (file.UpdatePointer[2] || file.UpdatePointer[3]))
                    {
                        long originalNameOffset = file.OffsetName;
                        long nameTargetOffset = originalNameOffset;
                        offsetFree = true;
                        foreach (var range in occupiedOffsets)
                        {
                            if (nameTargetOffset >= range.Key && nameTargetOffset < range.Value)
                            {
                                offsetFree = false;
                                break;
                            }
                        }
                        if (!offsetFree || (nameTargetOffset < pres.TocSize && !isRtbl))
                        {
                            nameTargetOffset = lastPosition;
                        }
                        file.OffsetName = (int)nameTargetOffset;

                        writer.BaseStream.Seek(nameTargetOffset, SeekOrigin.Begin);
                        MemoryStream arrName = new MemoryStream();
                        int baseName = (int)nameTargetOffset + (file.ChunkName * 4);
                        foreach (string cname in file.ElementName)
                        {
                            writer.Write((int)arrName.Position + baseName);
                            arrName.Write(Encoding.UTF8.GetBytes(cname), 0, cname.Length);
                            arrName.Write(new byte[1], 0, 1);
                        }
                        writer.Write(arrName.ToArray());
                        occupiedOffsets[nameTargetOffset] = writer.BaseStream.Position;
                        lastPosition = Math.Max(lastPosition, writer.BaseStream.Position);

                        Console.WriteLine($"  Name - Original POS: 0x{originalNameOffset:X} // New POS: 0x{nameTargetOffset:X}");
                    }
                    else
                    {
                        Console.WriteLine($"  Name - Original POS: 0x{file.OffsetName:X} // Unchanged (UpdatePointer[2,3] = false)");
                    }
                }
                else if (file.Location == "PackageFiles")
                {
                    byte[] buffer = File.ReadAllBytes(outFolder + file.FileName);
                    byte[] dataToWrite = file.Compression ? Compression.LeCompression(buffer) : buffer;
                    file.Size = dataToWrite.Length;
                    int originalOffset = packageDict[outFolder + file.FileName];
                    int newOffset;
                    if (ptSeekSamePackage.TryGetValue(originalOffset, out newOffset))
                    {
                        file.Offset = newOffset;
                    }
                    else
                    {
                        newOffset = originalOffset;
                        long actualPosition = newOffset << 11;
                        package.BaseStream.Seek(actualPosition, SeekOrigin.Begin);
                        package.Write(dataToWrite);
                        package.WritePadding(0x800, actualPosition);
                        ptSeekSamePackage[originalOffset] = newOffset;
                        file.Offset = newOffset;
                    }

                    Console.WriteLine($"  Data - Original POS: 0x{originalOffset << 11:X} // New POS: 0x{newOffset << 11:X}");

                    if (!isRtbl || (file.UpdatePointer[2] || file.UpdatePointer[3]))
                    {
                        long originalNameOffset = file.OffsetName;
                        long nameTargetOffset = originalNameOffset;
                        bool offsetFree = true;
                        foreach (var range in occupiedOffsets)
                        {
                            if (nameTargetOffset >= range.Key && nameTargetOffset < range.Value)
                            {
                                offsetFree = false;
                                break;
                            }
                        }
                        if (!offsetFree || (nameTargetOffset < pres.TocSize && !isRtbl))
                        {
                            nameTargetOffset = lastPosition;
                        }
                        file.OffsetName = (int)nameTargetOffset;

                        writer.BaseStream.Seek(nameTargetOffset, SeekOrigin.Begin);
                        MemoryStream arrName = new MemoryStream();
                        int baseName = (int)nameTargetOffset + (file.ChunkName * 4);
                        foreach (string cname in file.ElementName)
                        {
                            writer.Write((int)arrName.Position + baseName);
                            arrName.Write(Encoding.UTF8.GetBytes(cname), 0, cname.Length);
                            arrName.Write(new byte[1], 0, 1);
                        }
                        writer.Write(arrName.ToArray());
                        occupiedOffsets[nameTargetOffset] = writer.BaseStream.Position;
                        lastPosition = Math.Max(lastPosition, writer.BaseStream.Position);

                        Console.WriteLine($"  Name - Original POS: 0x{originalNameOffset:X} // New POS: 0x{nameTargetOffset:X}");
                    }
                    else
                    {
                        Console.WriteLine($"  Name - Original POS: 0x{file.OffsetName:X} // Unchanged (UpdatePointer[2,3] = false)");
                    }
                }
                else if (file.Location == "DataFiles")
                {
                    byte[] buffer = File.ReadAllBytes(outFolder + file.FileName);
                    byte[] dataToWrite = file.Compression ? Compression.LeCompression(buffer) : buffer;
                    file.Size = dataToWrite.Length;
                    int originalOffset = dataDict[outFolder + file.FileName];
                    int newOffset;
                    if (ptSeekSameData.TryGetValue(originalOffset, out newOffset))
                    {
                        file.Offset = newOffset;
                    }
                    else
                    {
                        newOffset = originalOffset;
                        long actualPosition = newOffset << 11;
                        data.BaseStream.Seek(actualPosition, SeekOrigin.Begin);
                        data.Write(dataToWrite);
                        data.WritePadding(0x800, actualPosition);
                        ptSeekSameData[originalOffset] = newOffset;
                        file.Offset = newOffset;
                    }

                    Console.WriteLine($"  Data - Original POS: 0x{originalOffset << 11:X} // New POS: 0x{newOffset << 11:X}");

                    if (!isRtbl || (file.UpdatePointer[2] || file.UpdatePointer[3]))
                    {
                        long originalNameOffset = file.OffsetName;
                        long nameTargetOffset = originalNameOffset;
                        bool offsetFree = true;
                        foreach (var range in occupiedOffsets)
                        {
                            if (nameTargetOffset >= range.Key && nameTargetOffset < range.Value)
                            {
                                offsetFree = false;
                                break;
                            }
                        }
                        if (!offsetFree || (nameTargetOffset < pres.TocSize && !isRtbl))
                        {
                            nameTargetOffset = lastPosition;
                        }
                        file.OffsetName = (int)nameTargetOffset;

                        writer.BaseStream.Seek(nameTargetOffset, SeekOrigin.Begin);
                        MemoryStream arrName = new MemoryStream();
                        int baseName = (int)nameTargetOffset + (file.ChunkName * 4);
                        foreach (string cname in file.ElementName)
                        {
                            writer.Write((int)arrName.Position + baseName);
                            arrName.Write(Encoding.UTF8.GetBytes(cname), 0, cname.Length);
                            arrName.Write(new byte[1], 0, 1);
                        }
                        writer.Write(arrName.ToArray());
                        occupiedOffsets[nameTargetOffset] = writer.BaseStream.Position;
                        lastPosition = Math.Max(lastPosition, writer.BaseStream.Position);

                        Console.WriteLine($"  Name - Original POS: 0x{originalNameOffset:X} // New POS: 0x{nameTargetOffset:X}");
                    }
                    else
                    {
                        Console.WriteLine($"  Name - Original POS: 0x{file.OffsetName:X} // Unchanged (UpdatePointer[2,3] = false)");
                    }
                }
                else if (file.Location == "dummy")
                {
                    file.Offset = 0;
                    Console.WriteLine($"  Name - Original POS: 0x{file.OffsetName:X} // Unchanged (dummy)");
                }
            }

            // Write TOC
            if (isRtbl)
            {
                using (BinaryReader reader = new BinaryReader(new FileStream(originalResName, FileMode.Open, FileAccess.Read)))
                {
                    foreach (Record file in pres.Files)
                    {
                        writer.BaseStream.Seek(file.RTBLTOCOffset, SeekOrigin.Begin);
                        reader.BaseStream.Seek(file.RTBLTOCOffset, SeekOrigin.Begin);

                        int originalRawOffset = reader.ReadInt32();
                        int originalSize = reader.ReadInt32();
                        int originalOffsetName = reader.ReadInt32();
                        int originalChunkName = reader.ReadInt32();
                        byte[] padding = reader.ReadBytes(12);
                        int originalDSize = reader.ReadInt32();

                        int indicator = (file.rawOffset >> 28) & 0xF;
                        if (file.Location == "PackageFiles") indicator = 0x4;
                        else if (file.Location == "DataFiles") indicator = 0x5;
                        else if (file.Location == "dummy") indicator = 0x0;

                        int rawOffset = file.UpdatePointer[0] ? ((indicator << 28) | (file.Offset & 0x0FFFFFFF)) : originalRawOffset;
                        int size = file.UpdatePointer[1] ? file.Size : originalSize;
                        int offsetName = file.UpdatePointer[2] ? file.OffsetName : originalOffsetName;
                        int chunkName = file.UpdatePointer[3] ? file.ChunkName : originalChunkName;
                        int dSize = file.UpdatePointer[4] ? file.DSize : originalDSize;

                        writer.Write(rawOffset);
                        writer.Write(size);
                        writer.Write(offsetName);
                        writer.Write(chunkName);
                        writer.Write(padding);
                        writer.Write(dSize);

                        occupiedOffsets[file.RTBLTOCOffset] = writer.BaseStream.Position;
                    }
                }
            }
            else
            {
                writer.BaseStream.Seek(tocStart, SeekOrigin.Begin);
                foreach (Record file in pres.Files)
                {
                    int indicator = (file.rawOffset >> 28) & 0xF;
                    if (file.Location == "PackageFiles") indicator = 0x4;
                    else if (file.Location == "DataFiles") indicator = 0x5;
                    else if (file.Location == "dummy") indicator = 0x0;

                    int rawOffset = (indicator << 28) | (file.Offset & 0x0FFFFFFF);
                    writer.Write(rawOffset);
                    writer.Write(file.Size);
                    writer.Write(file.OffsetName);
                    writer.Write(file.ChunkName);
                    writer.Write(new byte[12]);
                    writer.Write(file.DSize);
                }
            }

            writer.Flush();
            PreserveUnlistedDataAndFinalize();
            resStream.Close();
        }

        private void PreserveUnlistedDataAndFinalize()
        {
            byte[] tempData = new byte[resStream.Length];
            resStream.Seek(0, SeekOrigin.Begin);
            resStream.Read(tempData, 0, tempData.Length);

            byte[] originalData;
            using (FileStream originalStream = new FileStream(originalResName, FileMode.Open, FileAccess.Read))
            {
                originalData = new byte[originalStream.Length];
                originalStream.Read(originalData, 0, originalData.Length);
            }

            byte[] finalData = new byte[Math.Max(tempData.Length, originalData.Length)];
            Array.Copy(originalData, finalData, originalData.Length);

            foreach (var range in occupiedOffsets)
            {
                long start = range.Key;
                long end = range.Value;
                if (start < tempData.Length && end <= tempData.Length)
                {
                    Array.Copy(tempData, start, finalData, start, end - start);
                }
            }

            // Reuse original checksum from JSON for .res files
            if (!isRtbl)
            {
                finalData[0xA] = (byte)(pres.CeckSum >> 8);
                finalData[0xB] = (byte)(pres.CeckSum & 0xFF);
            }

            File.WriteAllBytes(originalResName, finalData);
            resStream.Close();
            File.Delete(resStream.Name);
        }
    }
}