using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class PresUnpack
{
    public static void Extract(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found: " + filePath);
            return;
        }

        string outputDir = Path.GetFileNameWithoutExtension(filePath);
        Directory.CreateDirectory(outputDir); // Create a directory based on the file name

        Dictionary<string, int> fileNameCounts = new Dictionary<string, int>(); // Track duplicate file names

        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (BinaryReader br = new BinaryReader(fs))
        {
            // Read the basic header information
            Pres pres = new Pres
            {
                Magic = br.ReadUInt32(),
                GroupOffset = br.ReadInt32(),
                GroupCount = br.ReadByte(),
                Unknown = br.ReadByte(),
                CheckSum = br.ReadUInt16(),
                Unknown2 = br.ReadInt32(),
                TocSize = br.ReadInt32(),
                TotalFiles = br.ReadInt32()
            };

            // Jump to the group offset
            fs.Seek(pres.GroupOffset, SeekOrigin.Begin);

            // Read groups
            List<(int Offset, int Size)> groups = new List<(int Offset, int Size)>();
            for (int i = 0; i < pres.GroupCount; i++)
            {
                int offset = br.ReadInt32();
                int size = br.ReadInt32();
                groups.Add((offset, size));
            }

            // Process each group's TOC
            foreach (var group in groups)
            {
                fs.Seek(group.Offset, SeekOrigin.Begin);

                for (int j = 0; j < group.Size; j++)
                {
                    Pres.Record record = new Pres.Record
                    {
                        Offset = br.ReadInt32(),
                        Size = br.ReadInt32(),
                        OffsetName = br.ReadInt32(),
                        ChunkName = br.ReadInt32(),
                        Padding = br.ReadBytes(12),
                        Dsize = br.ReadInt32()
                    };

                    // Skip "dummy" files (offset = 0)
                    if (record.Offset == 0)
                    {
                        Console.WriteLine($"Skipping dummy file at index {j + 1}.");
                        continue;
                    }

                    // Extract file name and handle duplicates
                    string fileName = ExtractFileName(fs, br, record.OffsetName, record.ChunkName);

                    if (string.IsNullOrWhiteSpace(fileName) || fileName == "Unknown")
                    {
                        Console.WriteLine($"Skipping file with invalid or missing name at index {j + 1}.");
                        continue;
                    }

                    fileName = HandleDuplicateFileName(fileName, fileNameCounts);

                    Console.WriteLine($"Extracting: {fileName}");

                    // Determine file offset and extract data (i will implement 0xD later)
                    if ((record.Offset & 0xFF000000) == 0xC0000000) // Internal offset
                    {
                        int actualOffset = record.Offset & 0x00FFFFFF;
                        ExtractInternalFile(fs, actualOffset, record.Size, Path.Combine(outputDir, fileName));
                    }
                    else // External offset
                    {
                        try
                        {
                            HandleExternalFile(record.Offset, record.Size, fileName, outputDir);
                        }
                        catch (InvalidOperationException ex)
                        {
                            Console.WriteLine($"Error: {ex.Message} Skipping file: {fileName}");
                        }
                    }
                }
            }
        }
    }

    private static string HandleDuplicateFileName(string fileName, Dictionary<string, int> fileNameCounts)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string newFileName = fileName;

        if (fileNameCounts.ContainsKey(fileName))
        {
            int count = fileNameCounts[fileName]++;
            newFileName = $"{baseName}_{count:D4}{extension}";
        }
        else
        {
            fileNameCounts[fileName] = 1;
        }

        return newFileName;
    }

    private static void ExtractInternalFile(FileStream fs, int offset, int size, string outputFilePath)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] data = new byte[size];
        fs.Read(data, 0, size);

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? string.Empty);
        File.WriteAllBytes(outputFilePath, data);

        Console.WriteLine($"Extracted internal file to: {outputFilePath}");
    }

    private static void HandleExternalFile(int offset, int size, string fileName, string outputDir)
    {
        string externalFile = DetermineExternalFile(offset);

        if (!File.Exists(externalFile))
        {
            Console.WriteLine($"External file {externalFile} not found, skipping {fileName}.");
            return;
        }

        int actualOffset = (offset & 0x00FFFFFF) * 0x800; // math. offset * 800 = absolute data. this is only for external source.

        using (FileStream extFs = new FileStream(externalFile, FileMode.Open, FileAccess.Read))
        {
            extFs.Seek(actualOffset, SeekOrigin.Begin);
            byte[] data = new byte[size];
            extFs.Read(data, 0, size);

            string outputFilePath = Path.Combine(outputDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? string.Empty);
            File.WriteAllBytes(outputFilePath, data);

            Console.WriteLine($"Extracted external file to: {outputFilePath}");
        }
    }
    /// each file tends to have a specialized address mode. 
    /// so if you convert a little endian to big endian, for example: LE: 60DE0140 -> BE: 4001DE60
    /// this means that the offset is one of the following here, it will detect the first byte if it matches with the files below

    private static string DetermineExternalFile(int offset) // finding source file.
    {
        switch ((offset & 0xFF000000))
        {
            case 0x40000000:
                return "package.rdp";
            case 0x50000000:
                return "data.rdp";
            case 0x60000000:
                return "patch.rdp";
            default:
                throw new InvalidOperationException($"Unknown external file type for offset: 0x{offset:X}");
        }
    }

    private static string ExtractFileName(FileStream fs, BinaryReader br, int offsetName, int chunkName)
    {
        if (offsetName == 0 || chunkName <= 0)
        {
            return "Unknown";
        }

        List<string> tables = new List<string>();
        long originalPosition = fs.Position;

        try
        {
            fs.Seek(offsetName, SeekOrigin.Begin);

            for (uint i = 0; i < chunkName; i++)
            {
                uint stringOffset = br.ReadUInt32();

                if (stringOffset == 0)
                    break;

                // Ensure string offset is valid
                if (stringOffset < 0 || stringOffset >= fs.Length)
                {
                    Console.WriteLine($"Invalid string offset: 0x{stringOffset:X}");
                    return "Unknown";
                }

                long currentPos = fs.Position;
                fs.Seek(stringOffset, SeekOrigin.Begin);

                string tableEntry = ReadNullTerminatedString(br);
                tables.Add(tableEntry);

                fs.Seek(currentPos, SeekOrigin.Begin); // Return to OffsetName table
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting file name: {ex.Message}");
            return "Unknown";
        }
        finally
        {
            fs.Seek(originalPosition, SeekOrigin.Begin); // Return to original position
        }

        // Build file name
        string name = tables.Count > 0 ? tables[0] : "Unknown";
        string extension = tables.Count > 1 ? tables[1] : "";
        string path = tables.Count > 2 ? tables[2] : "";

        string fullPath = Path.Combine(path, $"{name}.{extension}".Trim('.'));
        return fullPath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ReadNullTerminatedString(BinaryReader br)
    {
        List<byte> bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 00)
        {
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

public class Pres
{
    public uint Magic { get; set; }
    public int GroupOffset { get; set; }
    public int GroupCount { get; set; }
    public int Unknown { get; set; }
    public uint CheckSum { get; set; }
    public int Unknown2 { get; set; }
    public int TocSize { get; set; }
    public int TotalFiles { get; set; }

    public class Record
    {
        public int Offset { get; set; }
        public int Size { get; set; }
        public int OffsetName { get; set; }
        public int ChunkName { get; set; }
        public byte[] Padding { get; set; }
        public int Dsize { get; set; }
    }
}
