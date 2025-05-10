using System;
using System.IO;
using System.Collections.Generic;
using GIL.FUNCTION;
using System.Text.Json;
using System.Linq;
using BLZ2_DEFL;

#pragma warning disable CS8604 // Possible null reference argument.

namespace GEBCS
{

        public class PresUnpack
        {
            private Pres pres = new Pres();
            private BR reader;
            private Dictionary<long, int> grup = new Dictionary<long, int>();
            private string outFolder;
            private Dictionary<string, int> duplicateCek = new Dictionary<string, int>();
            private string duplicate = "";
            private FileStream fileStream;

            public PresUnpack(string fileName)
            {
                fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                outFolder = Path.GetDirectoryName(fileName) + "\\" + Path.GetFileNameWithoutExtension(fileName) + "\\";
                reader = new BR(fileStream);
                pres.Filename = fileName;

                if (Path.GetExtension(fileName).Equals(".rtbl", StringComparison.OrdinalIgnoreCase))
                {
                    GenerateRTBLInfo();
                }
                else
                {
                    pres.Magic = reader.ReadUInt32();
                    pres.GrupOffset = reader.ReadInt32();
                    pres.GrupCount = reader.ReadByte();
                    pres.GrupVersion = reader.ReadByte();
                    pres.CeckSum = reader.ReadUInt16();
                    pres.Version = reader.ReadInt32();
                    pres.ChunkData = reader.ReadInt32();
                    reader.BaseStream.Seek(12, SeekOrigin.Current);
                    pres.TotalFile = 0;
                    pres.Grups = new List<int>();
                    for (int i = 0; i < 8; i++)
                    {
                        int off = reader.ReadInt32();
                        int count = reader.ReadInt32();
                        pres.Grups.Add(off);
                        pres.Grups.Add(count);
                        if (off > 0)
                        {
                            grup.Add((long)off, count);
                            pres.TotalFile += count;
                        }
                    }
                    GenerateFileInfo();
                }
            }

        private void GenerateRTBLInfo()
        {
            pres.Files = new List<Record>();
            long fileLength = fileStream.Length;
            long position = 0;

            Console.WriteLine($"Starting RTBL scan: FileLength = {fileLength}");

            while (position < fileLength)
            {
                reader.BaseStream.Seek(position, SeekOrigin.Begin);
             //   Console.WriteLine($"Position = 0x{position:X}: Checking 16-byte block");

                if (fileLength - position < 16)
                {
                    Console.WriteLine($"  WARNING: Incomplete block at 0x{position:X}, remaining bytes = {fileLength - position}");
                    break;
                }
                byte[] block = reader.ReadBytes(16);
                bool isPadding = block.All(b => b == 0);

                if (isPadding)
                {
                //    Console.WriteLine($"  Skipping padding block at 0x{position:X}");
                    position += 16;
                    continue;
                }

                Console.WriteLine($"  TOC tagged at 0x{position:X}");
                reader.BaseStream.Seek(position, SeekOrigin.Begin);
                int rawOffset = reader.ReadInt32();
                Console.WriteLine($"  rawOffset = 0x{rawOffset:X8}");

                Record file = new Record
                {
                    RTBLTOCOffset = (int)position
                };
                file.rawOffset = rawOffset;
                file.Size = reader.ReadInt32();
                file.OffsetName = reader.ReadInt32();
                file.ChunkName = reader.ReadInt32();
                reader.ReadBytes(12);
                file.DSize = reader.ReadInt32();
                Console.WriteLine($"  Size = 0x{file.Size:X}, OffsetName = 0x{file.OffsetName:X}, ChunkName = {file.ChunkName}");

                file.UpdatePointer = new List<bool>
        {
            Convert.ToBoolean(file.rawOffset),
            Convert.ToBoolean(file.Size),
            false,
            false,
            Convert.ToBoolean(file.DSize)
        };

                int indicator = (file.rawOffset >> 28) & 0xF;
                file.Offset = file.rawOffset & 0x0FFFFFFF;
                switch (indicator)
                {
                    case 0x4:
                        file.Location = "PackageFiles";
                        file.ShiftOffset = 11;
                        break;
                    case 0x5:
                        file.Location = "DataFiles";
                        file.ShiftOffset = 11;
                        break;
                    case 0xC:
                    case 0xD:
                        file.Location = "Local";
                        file.ShiftOffset = 0;
                        break;
                    case 0x0:
                        file.Location = "dummy";
                        file.ShiftOffset = 0;
                        if (file.Size == 0 && file.OffsetName != 0 && file.ChunkName != 0)
                            file.FileName = "empty";
                        break;
                    default:
                        file.Location = "unknown";
                        break;
                }
                Console.WriteLine($"  Location = {file.Location}, Offset = 0x{file.Offset:X}");

                if (file.ChunkName > 0)
                {
                    long nameBase = position + file.OffsetName;
                    Console.WriteLine($"  Seeking to chunk table at 0x{nameBase:X}");
                    if (nameBase + 12 >= fileLength)
                    {
                        Console.WriteLine($"  ERROR: nameBase + 12 (0x{nameBase + 12:X}) exceeds fileLength 0x{fileLength:X}");
                        break;
                    }
                    reader.BaseStream.Seek(nameBase, SeekOrigin.Begin);
                    reader.BaseStream.Seek(12, SeekOrigin.Current);
                    long namePos = reader.BaseStream.Position;
                 //   Console.WriteLine($"  Skipped 12 bytes, now at 0x{namePos:X}");

                    file.ElementName = new List<string>();
                    string name = reader.GetUtf8(namePos);
                    Console.WriteLine($"  Name = '{name}'");
                    file.ElementName.Add(name);

                    long formatPos = namePos + name.Length + 1;
                    if (formatPos >= fileLength)
                    {
                        Console.WriteLine($"  ERROR: formatPos 0x{formatPos:X} exceeds fileLength 0x{fileLength:X}");
                        file.ElementName.Add("");
                    }
                    else
                    {
                        string format = reader.GetUtf8(formatPos);
                        Console.WriteLine($"  Format = '{format}'");
                        file.ElementName.Add(format);
           
                    }

                    // Apply duplicate suffix
                    string dupName = string.Join("", file.ElementName).ToUpper();
                    try
                    {
                        duplicate = string.Format("_{0,0:d4}", duplicateCek[dupName]);
                        duplicateCek[dupName]++;
                    }
                    catch (Exception)
                    {
                        duplicateCek.Add(dupName, 1);
                        duplicate = ""; // First instance gets no suffix
                    }
                    file.FileName = file.ElementName[0] + duplicate + "." + file.ElementName[1];
                    Console.WriteLine($"  FileName = '{file.FileName}' (duplicate = '{duplicate}')");
                    duplicate = "";

                    long stringEnd = formatPos + file.ElementName[1].Length + 1;
                    long nextPos = ((stringEnd + 15) / 16) * 16;
                    Console.WriteLine($"  String ends at 0x{stringEnd:X}, moving to next 16-byte boundary at 0x{nextPos:X}");
                    position = nextPos;
                }
                else
                {
                    file.FileName = "dummy";
                    position += 16;
                }

                pres.Files.Add(file);
                Console.WriteLine($"  Added file to pres.Files, count now = {pres.Files.Count}");
            }
            pres.TotalFile = pres.Files.Count;
            Console.WriteLine($"RTBL scan complete: TotalFiles = {pres.TotalFile}");
        }

        private void GenerateFileInfo()
            {
                // Unchanged from previous version
                pres.Files = new List<Record>();
                MemoryStream memory = new MemoryStream(reader.ReadBytes(pres.TotalFile * 32));
                pres.TocSize = (int)reader.BaseStream.Position;
                BinaryReader toc = new BinaryReader(memory);
                for (int i = 0; i < grup.Count; i++)
                {
                    int count = grup[toc.BaseStream.Position + 0x60];
                    for (int j = 0; j < count; j++)
                    {
                        Record file = new Record();
                        file.rawOffset = toc.ReadInt32();
                        file.Size = toc.ReadInt32();
                        file.OffsetName = toc.ReadInt32();
                        file.ChunkName = toc.ReadInt32();
                        toc.ReadBytes(12);
                        file.DSize = toc.ReadInt32();
                        file.UpdatePointer = new List<bool>
                {
                    Convert.ToBoolean(file.rawOffset),
                    Convert.ToBoolean(file.Size),
                    Convert.ToBoolean(file.OffsetName),
                    Convert.ToBoolean(file.ChunkName),
                    Convert.ToBoolean(file.DSize)
                };

                        int indicator = (file.rawOffset >> 28) & 0xF;
                        file.Offset = file.rawOffset & 0x0FFFFFFF;
                        switch (indicator)
                        {
                            case 0x4:
                                file.Location = "PackageFiles";
                                file.ShiftOffset = 11;
                                break;
                            case 0x5:
                                file.Location = "DataFiles";
                                file.ShiftOffset = 11;
                                break;
                            case 0xC:
                            case 0xD:
                                file.Location = "Local";
                                file.ShiftOffset = 0;
                                break;
                            case 0x0:
                                file.Location = "dummy";
                                file.ShiftOffset = 0;
                                if (file.Size == 0 && file.OffsetName != 0 && file.ChunkName != 0)
                                    file.FileName = "empty";
                                break;
                            default:
                                file.Location = "unknown";
                                break;
                        }

                        if (file.ChunkName > 0)
                        {
                            reader.BaseStream.Seek(file.OffsetName, SeekOrigin.Begin);
                            file.ElementName = new List<string>();
                            for (int k = 0; k < file.ChunkName; k++)
                            {
                                int offChunk = reader.ReadInt32();
                                file.ElementName.Add(reader.GetUtf8(offChunk));
                            }
                            string dupName = string.Join("", file.ElementName).ToUpper();
                            try
                            {
                                duplicate = string.Format("_{0,0:d4}", duplicateCek[dupName]);
                                duplicateCek[dupName]++;
                            }
                            catch (Exception)
                            {
                                duplicateCek.Add(dupName, 1);
                            }
                        }
                        file.RealSize = file.Size;
                        if (file.ChunkName == 0)
                        {
                            file.FileName = "dummy";
                        }
                        else if (file.ChunkName == 1)
                        {
                            file.FileName = file.ElementName[0];
                        }
                        else if (file.ChunkName > 1)
                        {
                            string rawBaseName = file.ElementName[0] + "." + file.ElementName[1];
                            string baseName = file.ElementName[0] + duplicate + "." + file.ElementName[1];
                            string lastElement = file.ElementName.Last();
                            if (lastElement.Contains("/") && lastElement.EndsWith(rawBaseName, StringComparison.OrdinalIgnoreCase))
                            {
                                file.FileName = lastElement;
                            }
                            else
                            {
                                file.FileName = string.Join("\\", file.ElementName.Skip(2)) + "\\" + baseName;
                            }
                        }
                        duplicate = "";
                        pres.Files.Add(file);
                    }
                }
            }

        public void Unpack(ref BR package, ref BR data, ref ResNames resNames, ref Tr2Names tr2Names, ref PackageFiles packageFiles, ref DataFiles dataFiles)
        {
            // Step 1: Extract all files
            for (int i = 0; i < pres.TotalFile; i++)
            {
                Record file = pres.Files[i];
                if (file.Location != "dummy" && file.FileName != "empty")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outFolder + file.FileName));
                    Console.WriteLine("Extract: " + outFolder + file.FileName);
                    int offset = file.Offset << file.ShiftOffset;
                    int size = file.RealSize;
                    byte[] comBuffer;
                    switch (file.Location)
                    {
                        case "Local":
                            comBuffer = reader.GetBytes(offset, size);
                            break;
                        case "PackageFiles":
                            packageFiles.Files.Add(new PackageContent { Name = outFolder + file.FileName, Offset = file.Offset });
                            comBuffer = package.GetBytes(offset, file.Size);
                            int padding = 0x800 - (file.Size % 0x800);
                            if (padding == 0x800) padding = 0;
                            pres.Files[i].MaxSize = file.Size + padding;
                            break;
                        case "DataFiles":
                            dataFiles.Files.Add(new PackageContent { Name = outFolder + file.FileName, Offset = file.Offset });
                            comBuffer = data.GetBytes(offset, file.Size);
                            padding = 0x800 - (file.Size % 0x800);
                            if (padding == 0x800) padding = 0;
                            pres.Files[i].MaxSize = file.Size + padding;
                            break;
                        default:
                            comBuffer = new byte[0];
                            break;
                    }
                    if (comBuffer.Length > 0)
                    {
                        if (comBuffer.Length >= 4 && BitConverter.ToUInt32(comBuffer, 0) == Deflate.BLZ2_MAGIC)
                        {
                            file.Compression = true;
                            byte[] decBuffer = Deflate.DecompressBLZ2(comBuffer);
                            if (decBuffer != null)
                            {
                                File.WriteAllBytes(outFolder + file.FileName, decBuffer);
                            }
                            else
                            {
                                Console.WriteLine($"Failed to decompress BLZ2: {outFolder + file.FileName}");
                                File.WriteAllBytes(outFolder + file.FileName, comBuffer);
                            }
                        }
                        else
                        {
                            file.Compression = false;
                            File.WriteAllBytes(outFolder + file.FileName, comBuffer);
                        }
                    }
                    else
                    {
                        File.WriteAllBytes(outFolder + file.FileName, comBuffer);
                    }
                }
            }

            // Step 2: Process .res and .rtbl files after extraction
            List<string> resFilesToProcess = new List<string>();
            for (int i = 0; i < pres.TotalFile; i++)
            {
                Record file = pres.Files[i];
                if (file.Location != "dummy" && file.FileName != "empty")
                {
                    if (Path.GetExtension(file.FileName) == ".res" || Path.GetExtension(file.FileName) == ".rtbl")
                    {
                        resNames.Names.Add(outFolder + file.FileName);
                        resFilesToProcess.Add(outFolder + file.FileName);
                    }
                    if (Path.GetExtension(file.FileName) == ".tr2")
                    {
                        tr2Names.Names.Add(outFolder + file.FileName);
                    }
                }
            }

            // Close the current FileStream to release any locks
            fileStream.Close();
            fileStream.Dispose();

            // Step 3: Recursively unpack .res/.rtbl files
            foreach (string resFile in resFilesToProcess)
            {
                Console.WriteLine($"Processing nested file: {resFile}");
                PresUnpack pres = new PresUnpack(resFile);
                pres.Unpack(ref package, ref data, ref resNames, ref tr2Names, ref packageFiles, ref dataFiles);
            }

            CreateXml();
        }

        private void CreateXml()
            {
                string jsonString = JsonSerializer.Serialize(pres, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                File.WriteAllText(Path.ChangeExtension(pres.Filename, "json"), jsonString);
            }
        }
    }

#pragma warning restore CS8604 // Possible null reference argument.