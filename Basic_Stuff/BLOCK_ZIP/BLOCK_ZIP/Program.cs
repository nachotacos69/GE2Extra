/* Basic input output stuff to be honest

 */



using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Uses SharpZipZLIB for compression");
            Console.WriteLine("Usage: BLOCK_ZIP [file]");
            return;
        }

        string inputFile = args[0];

        if (!File.Exists(inputFile))
        {
            Console.WriteLine("File not found!");
            return;
        }

        try
        {
            
            byte[] inputData = File.ReadAllBytes(inputFile);

           
            byte[] compressedData = Compression.LeCompression(inputData);

            
            string outputFile = Path.GetFileNameWithoutExtension(inputFile) + "_compressed" + Path.GetExtension(inputFile);
            File.WriteAllBytes(outputFile, compressedData);

            Console.WriteLine($"File compressed successfully: {outputFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}