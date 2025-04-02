using System;
using System.IO;

namespace RES_UNPACKER
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==============GOD EATER RES UNPACKER [FOR JAPANESE VERSIONS]==============\nSupports the Following:\n\n- GOD EATER 2 [PSP/PSVITA]\n- GOD EATER RESURRECTION [PSVITA]\n- GOD EATER 2 RAGE BURST [PSVITA]\n- GOD EATER OFFSHOT Series [PSVITA]\n\nWARNING: Extraction will take some time, so do something else while it extracts.\n\n");
            Console.WriteLine("For PSVITA Side: The Path system can be wacky since i mostly focused this on the PSP Side, but everything should work.\n");
            Console.WriteLine("Select a .res file to process:");
            Console.WriteLine("1. system.res (Base Game) || GE2,GER,GE2RB,OFFSHOT");
            Console.WriteLine("2. system_update.res (DLC Content) || GE2,GER,GE2RB");
            Console.Write("Enter your choice (1 or 2): ");

            string choice = Console.ReadLine();
            string inputFile;
            switch (choice)
            {
                case "1":
                    inputFile = "system.res";
                    break;
                case "2":
                    inputFile = "system_update.res";
                    break;
                default:
                    Console.WriteLine("Invalid choice. Please enter 1 or 2.");
                    return;
            }

            string outputFolder = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(inputFile));
            Directory.CreateDirectory(outputFolder);

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

                Console.WriteLine("RES file processing completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing RES file: {ex.Message}");
            }
        }
    }
}