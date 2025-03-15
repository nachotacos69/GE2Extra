using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RES_PACKER
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Select an operation:");
            Console.WriteLine("1. Unpack default input (system.res)");
            Console.WriteLine("2. Pack default input (system.res)");
            Console.WriteLine("3. Debugger sample.rtbl test file processing");
            Console.WriteLine("4. Single file unpacking (any file)");
            Console.WriteLine("5. Single debugger packing .res file (any file)");
            Console.WriteLine("6. Extract list range from RESINDEX.json");
            Console.WriteLine("7. Pack files from RESINDEX.json (verify sample.rtbl)");
            Console.Write("Enter your choice (1-7): ");

            string choice = Console.ReadLine();
            string inputFile;
            string outputFolder;

            switch (choice)
            {
                case "1":
                    inputFile = "system.res";
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(inputFile));
                    Directory.CreateDirectory(outputFolder);
                    Unpack(inputFile, outputFolder);
                    break;

                case "2":
                    PackAllFromIndex("system.res");
                    break;

                case "3":
                    inputFile = "sample.rtbl";
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "sample_rtbl_output");
                    Directory.CreateDirectory(outputFolder);
                    TestUnpackRTBL(inputFile, outputFolder);
                    break;

                case "4":
                    Console.Write("Enter the file path to unpack: ");
                    inputFile = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(inputFile))
                    {
                        Console.WriteLine("Error: No input file specified.");
                        return;
                    }
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(inputFile));
                    Directory.CreateDirectory(outputFolder);
                    Unpack(inputFile, outputFolder);
                    break;

                case "5":
                    Console.Write("Enter the .res file path to pack: ");
                    inputFile = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(inputFile) || !inputFile.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Error: Please specify a valid .res file.");
                        return;
                    }
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "repacked");
                    Directory.CreateDirectory(outputFolder);
                    PackSingle(inputFile, outputFolder);
                    break;

                case "6":
                    ExtractListRange();
                    break;

                case "7":
                    PackAllFromIndex("sample.rtbl");
                    break;

                default:
                    Console.WriteLine("Invalid choice. Please enter 1, 2, 3, 4, 5, 6, or 7.");
                    return;
            }
        }

        static void InitializeRdpFiles(string outputFolder, Dictionary<string, List<(long StartOffset, long EndOffset)>> usedOffsets)
        {
            string[] rdpFiles = { "package.rdp", "data.rdp", "patch.rdp" };
            Directory.CreateDirectory(outputFolder);

            foreach (var rdp in rdpFiles)
            {
                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), rdp);
                string destPath = Path.Combine(outputFolder, rdp);

                if (File.Exists(sourcePath))
                {
                    using (var sourceFs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] header = new byte[32];
                        int bytesRead = sourceFs.Read(header, 0, 32);
                        long originalSize = sourceFs.Length;

                        using (var destFs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                        {
                            destFs.Write(header, 0, bytesRead);
                            destFs.SetLength(originalSize);
                        }
                        Console.WriteLine($"[Debug] Initialized '{destPath}' with {bytesRead}-byte header from '{sourcePath}' and pre-allocated to {originalSize} bytes");
                    }
                    usedOffsets[rdp] = new List<(long, long)>();
                }
                else
                {
                    Console.WriteLine($"[Debug] Skipping '{destPath}' initialization (source '{sourcePath}' not found)");
                }
            }
        }

        static void PackAllFromIndex(string verifyFile)
        {
            string resIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "RESINDEX.json");
            if (!File.Exists(resIndexPath))
            {
                Console.WriteLine("Error: RESINDEX.json not found. Packing aborted.");
                return;
            }

            if (!File.Exists(verifyFile))
            {
                Console.WriteLine($"Error: Verification file '{verifyFile}' not found. Packing aborted.");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(resIndexPath);
                dynamic resIndex = JsonConvert.DeserializeObject(jsonContent);
                List<string> resList = new List<string>();

                if (resIndex.RES_LIST != null)
                {
                    foreach (var item in resIndex.RES_LIST)
                    {
                        resList.Add(item.ToString());
                    }
                }

                if (resList.Count == 0)
                {
                    Console.WriteLine("Error: RES_LIST is empty in RESINDEX.json. Packing aborted.");
                    return;
                }

                string repackedFolder = Path.Combine(Directory.GetCurrentDirectory(), "repacked");
                var usedOffsets = new Dictionary<string, List<(long StartOffset, long EndOffset)>>();
                InitializeRdpFiles(repackedFolder, usedOffsets);

                for (int i = resList.Count - 1; i >= 0; i--)
                {
                    string filePath = resList[i];
                    string outputDir = i == 0 ? repackedFolder : Path.GetDirectoryName(filePath);
                    Directory.CreateDirectory(outputDir);

                    Console.WriteLine($"Packing '{filePath}' to '{outputDir}'");
                    PackRES_EX packer = new PackRES_EX(filePath, outputDir, usedOffsets);
                    packer.Pack();
                }

                Console.WriteLine("Packing of all files from RESINDEX.json completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during packing from RESINDEX.json: {ex.Message}");
            }
        }

        static void Unpack(string inputFile, string outputFolder)
        {
            try
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                    return;
                }

                byte[] fileData = File.ReadAllBytes(inputFile);
                PRES resProcessor = new PRES();
                resProcessor.ProcessRES(fileData, inputFile, outputFolder);
                Console.WriteLine("RES file unpacking completed.");
                UnpackRES.SaveAllIndexes();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RES file '{inputFile}': {ex.Message}");
            }
        }

        static void PackSingle(string inputFile, string outputFolder)
        {
            try
            {
                var usedOffsets = new Dictionary<string, List<(long StartOffset, long EndOffset)>>();
                InitializeRdpFiles(outputFolder, usedOffsets);
                PackRES_EX packer = new PackRES_EX(inputFile, outputFolder, usedOffsets);
                packer.Pack();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error packing RES file '{inputFile}': {ex.Message}");
            }
        }

        static void TestUnpackRTBL(string inputFile, string outputFolder)
        {
            try
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Input file '{inputFile}' not found. Please create 'sample.rtbl' for testing.");
                    return;
                }

                Console.WriteLine("Debug: Starting RTBL test unpack for '{0}'", inputFile);
                byte[] fileData = File.ReadAllBytes(inputFile);
                DataHelper dataHelper = new DataHelper(inputFile);
                RTBL rtblProcessor = new RTBL(fileData, inputFile, outputFolder, dataHelper);
                List<RTBL.RTBLEntry> entries = rtblProcessor.ProcessRTBL();
                Console.WriteLine("Debug: Found {0} RTBL entries", entries.Count);
                Console.WriteLine("RTBL test unpacking completed. Check '{0}' for output.", outputFolder);
                UnpackRES.SaveAllIndexes();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RTBL file '{inputFile}': {ex.Message}");
            }
        }

        static void ExtractListRange()
        {
            string resIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "RESINDEX.json");
            if (!File.Exists(resIndexPath))
            {
                Console.WriteLine("Error: RESINDEX.json not found. Please unpack a .res file first.");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(resIndexPath);
                dynamic resIndex = JsonConvert.DeserializeObject(jsonContent);

                List<string> resList = new List<string>();
                List<string> rtblList = new List<string>();

                if (resIndex.RES_LIST != null)
                {
                    foreach (var item in resIndex.RES_LIST)
                    {
                        resList.Add(item.ToString());
                    }
                }
                if (resIndex.RTBL_LIST != null)
                {
                    foreach (var item in resIndex.RTBL_LIST)
                    {
                        rtblList.Add(item.ToString());
                    }
                }

                List<string> combinedList = new List<string>();
                combinedList.AddRange(resList);
                combinedList.AddRange(rtblList);

                const int chunkSize = 10000;
                SaveListToFiles(combinedList, "COMBINED_LIST", chunkSize);

                Console.WriteLine("\nCombined List (RES_LIST + RTBL_LIST):");
                for (int i = 0; i < combinedList.Count; i++)
                {
                    Console.WriteLine($"Line {i + 1}: {combinedList[i]}");
                }

                Console.WriteLine($"\nTotal RES_LIST entries: {resList.Count}");
                Console.WriteLine($"Total RTBL_LIST entries: {rtblList.Count}");
                Console.WriteLine($"Total Combined entries: {combinedList.Count}");
                Console.WriteLine("Lists have been saved to text files in chunks of 10,000 lines (e.g., COMBINED_LIST_1.txt).");

                Console.Write("Enter start line number (1-based): ");
                string startInput = Console.ReadLine();
                Console.Write("Enter end line number (1-based): ");
                string endInput = Console.ReadLine();

                if (!int.TryParse(startInput, out int startLine) || !int.TryParse(endInput, out int endLine) ||
                    startLine < 1 || endLine < 1 || startLine > combinedList.Count || endLine > combinedList.Count || startLine > endLine)
                {
                    Console.WriteLine("Error: Invalid line range. Ensure numbers are valid and within combined list bounds.");
                    return;
                }

                startLine--;
                endLine--;

                for (int i = startLine; i <= endLine; i++)
                {
                    string filePath = combinedList[i];
                    Console.WriteLine($"Processing Line {i + 1}: {filePath}");

                    if (!File.Exists(filePath))
                    {
                        Console.WriteLine($"Warning: File '{filePath}' not found. Skipping.");
                        continue;
                    }

                    string fileDir = Path.GetDirectoryName(filePath);
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFolder = Path.Combine(fileDir, fileName);
                    Directory.CreateDirectory(outputFolder);

                    if (filePath.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                    {
                        UnpackWithoutSavingIndexes(filePath, outputFolder);
                    }
                    else if (filePath.EndsWith(".rtbl", StringComparison.OrdinalIgnoreCase))
                    {
                        UnpackRTBLWithoutSavingIndexes(filePath, outputFolder);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Unsupported file type '{filePath}'. Skipping.");
                    }
                }

                Console.WriteLine("Extraction of selected range completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during Extract List Range: {ex.Message}");
            }
        }

        static void SaveListToFiles(List<string> list, string prefix, int chunkSize)
        {
            if (list.Count == 0) return;

            for (int i = 0; i < list.Count; i += chunkSize)
            {
                int chunkEnd = Math.Min(i + chunkSize, list.Count);
                string fileName = Path.Combine(Directory.GetCurrentDirectory(), $"{prefix}_{(i / chunkSize) + 1}.txt");
                using (StreamWriter writer = new StreamWriter(fileName))
                {
                    for (int j = i; j < chunkEnd; j++)
                    {
                        writer.WriteLine($"Line {j + 1}: {list[j]}");
                    }
                }
                Console.WriteLine($"Saved {prefix} lines {i + 1} to {chunkEnd} to '{fileName}'");
            }
        }

        static void UnpackWithoutSavingIndexes(string inputFile, string outputFolder)
        {
            try
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                    return;
                }

                byte[] fileData = File.ReadAllBytes(inputFile);
                PRES resProcessor = new PRES();
                resProcessor.ProcessRES(fileData, inputFile, outputFolder);
                Console.WriteLine($"RES file '{inputFile}' unpacking completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RES file '{inputFile}': {ex.Message}");
            }
        }

        static void UnpackRTBLWithoutSavingIndexes(string inputFile, string outputFolder)
        {
            try
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                    return;
                }

                Console.WriteLine($"Debug: Starting RTBL unpack for '{inputFile}'");
                byte[] fileData = File.ReadAllBytes(inputFile);
                DataHelper dataHelper = new DataHelper(inputFile);
                RTBL rtblProcessor = new RTBL(fileData, inputFile, outputFolder, dataHelper);
                List<RTBL.RTBLEntry> entries = rtblProcessor.ProcessRTBL();
                Console.WriteLine($"Debug: Found {entries.Count} RTBL entries");
                Console.WriteLine($"RTBL file '{inputFile}' unpacking completed. Check '{outputFolder}' for output.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RTBL file '{inputFile}': {ex.Message}");
            }
        }
    }
}