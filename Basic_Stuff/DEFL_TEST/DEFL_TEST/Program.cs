using System;

namespace Decompression
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("A Decompression Tool.\nUsage: DEFL_TEST [file]");
                return;
            }

            string filePath = args[0];

            try
            {
                Decompress decompressor = new Decompress();
                decompressor.ProcessFile(filePath);
                Console.WriteLine("Decompression completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}