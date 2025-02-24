using System;
using System.IO;

namespace PRES_PROT
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Enter the path to the .res file:");
            string filePath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Console.WriteLine("No file provided. Exiting...");
                return;
            }

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found. Please check the path and try again.");
                return;
            }


            // Pass both filePath and outputFolder to PRES
            PRES.ProcessFile(filePath);

            Console.WriteLine("Processing complete.");
        }
    }
}
