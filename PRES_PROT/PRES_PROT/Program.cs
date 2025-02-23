using System;
using System.IO;

namespace PRES_PROT
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===============Credits===============" +
                "\nCreated By: Yamato Nagasaki (https://github.com/nachotacos69)\nBLZ codes and other related data: HaoJun/Randerion (https://github.com/HaoJun0823)\n\n" +
                "\n==NOTES==\n~In case if some RES or RTBL files don't get extracted. i can't help with that, there is some alternative tools out there to use.\n\n===(Base Content File Requirements)===\n=>system.res\n=>package.rdp\n=>data.rdp" +
                "\n\n===(DLC Content File Requirements)===\n==>system_update.res\n==>package and data.rdp\n==>patch.rdp");
            Console.WriteLine("\n!! Loading DLC chunkdatas (ex: 0x14, and 0x18) is still not implemented yet.");
            Console.WriteLine("!! So we cant load both DLC and Base versions of that .res file sadly");
            Console.WriteLine("!! Extraction will take some time. So sleep tight :)");
            Console.WriteLine("\nSelect an option:");
            Console.WriteLine("1 - Use system.res (Base Content)");
            Console.WriteLine("2 - Use system_update.res (DLC Content)");
        // DEBUG    Console.WriteLine("3 - Manual RTBL Unpacking");

            Console.Write("\nEnter your choice: ");
            string choice = Console.ReadLine();

            string defaultFile = "system.res";
            string dlcFile = "system_update.res";

            bool defaultExists = File.Exists(defaultFile);
            bool dlcExists = File.Exists(dlcFile);

            switch (choice)
            {
                case "1":
                    if (defaultExists)
                    {
                        Console.WriteLine($"Processing: {defaultFile}");
                        PRES.ProcessFile(defaultFile);
                    }
                    else
                    {
                        Console.WriteLine("Error: system.res not found.");
                    }
                    break;

                case "2":
                    if (dlcExists)
                    {
                        Console.WriteLine($"Processing: {dlcFile}");
                        PRES.ProcessFile(dlcFile);
                    }
                    else
                    {
                        Console.WriteLine("Error: system_update.res not found.");
                    }
                    break;
                /* DEBUG RTBL 
                case "3":
                                    Console.Write("\nEnter the RTBL file name: ");
                                    string rtFile = Console.ReadLine();

                                    if (!string.IsNullOrEmpty(rtFile) && File.Exists(rtFile))
                                    {
                                        Console.WriteLine($"Processing RTBL file: {rtFile}");
                                        RTBL.ProcessRTBL(rtFile); // Corrected method call
                                    }
                                    else
                                    {
                                        Console.WriteLine("Error: Invalid RTBL file.");
                                    }
                                    break;
                DEBUG RTBL*/

                default:
                    Console.WriteLine("Invalid option. Exiting...");
                    break;
            }

            Console.WriteLine("Exiting...");
        }
    }
}
