using System;
using System.IO;

namespace PRES_PROT
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===============NOTES===============" +
                "\nRTBL Interaction Not Implemented (YET)\n\n" +
                "\n===(Base Content File Requirements)===\n=>system.res\n=>package.rdp\n=>data.rdp" +
                "\n\n===(DLC Content File Requirements)===\n==>system_update.res\n==>package and data.rdp\n==>patch.rdp");
            Console.WriteLine("\nLoading DLC chunkdatas (ex: 0x14, and 0x18) is still not implemented yet.");
            Console.WriteLine("So we cant load both DLC and Base versions of that .res file sadly");
            Console.WriteLine("Extraction will take some time. So sleep tight :)");
            Console.WriteLine("\nSelect an option:");
            Console.WriteLine("1 - Extract system.res (Base Content)");
            Console.WriteLine("2 - Extract system_update.res (DLC Content)");


            Console.Write("Enter your choice: ");
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



            }
            Console.WriteLine("Exiting...");

        }
    }
}
