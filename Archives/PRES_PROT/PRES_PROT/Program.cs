using System;
using System.IO;

class Program
{
    static void Main()
    {
        string filePath = "system.res";  // Adjust if needed
        string outputDirectory = Path.GetFileNameWithoutExtension(filePath); // Use input file name as output folder
        Pres.ParseResFile(filePath, outputDirectory);
    }
}