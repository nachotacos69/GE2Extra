using System;
using System.IO;

namespace RES_PACKER
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Select an operation:");
            Console.WriteLine("1. Unpack system.res (Base Game)");
            Console.WriteLine("2. Unpack system_update.res (DLC Content)");
            Console.WriteLine("3. Pack system.res");
            Console.WriteLine("4. Pack system_update.res");
            Console.Write("Enter your choice (1-4): ");

            string choice = Console.ReadLine();
            string inputFile;
            string outputFolder;

            switch (choice)
            {
                case "1":
                case "2":
                    inputFile = choice == "1" ? "system.res" : "system_update.res";
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(inputFile));
                    Directory.CreateDirectory(outputFolder);
                    Unpack(inputFile, outputFolder);
                    break;

                case "3":
                case "4":
                    inputFile = choice == "3" ? "system.res" : "system_update.res";
                    outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "repacked");
                    Directory.CreateDirectory(outputFolder);
                    Pack(inputFile, outputFolder);
                    break;

                default:
                    Console.WriteLine("Invalid choice. Please enter 1, 2, 3, or 4.");
                    return;
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RES file: {ex.Message}");
            }
        }

        static void Pack(string inputFile, string outputFolder)
        {
            try
            {
                PackRES packer = new PackRES(inputFile, outputFolder);
                packer.Pack();
                Console.WriteLine("RES file packing completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error packing RES file: {ex.Message}");
            }
        }
    }
}