using System;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: RES_ARCHIVE -x (extraction command)");
            return;
        }

        string command = args[0].ToLower();

        if (command == "-x")
        {
            string filePath = args.Length > 1 ? args[1] : "system.res";
            Console.WriteLine($"Extracting: {filePath}");
            PresUnpack.Extract(filePath);
        }
        else
        {
            Console.WriteLine("Unknown command.");
        }
    }
}
