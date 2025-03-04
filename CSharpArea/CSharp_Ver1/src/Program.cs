using System;
using System.IO;
using System.Text;
namespace GE2
{
    class Program
    {
        // Declare these fields to hold the readers for base game and DLC
        public static BinaryReader package;
        public static BinaryReader data;
        public static BinaryReader patch; // DLC reader for patch.rdp

        // Counter to track the total number of extracted files
        public static int totalExtractedFiles = 0;

        static void Main(string[] args)
        {
            // Create a StreamWriter to write output to a text file
            using (StreamWriter writer = new StreamWriter("output_text.txt"))
            {
                // Redirect console output to both the console and the file
                Console.SetOut(new MultiTextWriter(Console.Out, writer));
                Console.SetError(new MultiTextWriter(Console.Error, writer)); // Capture error output as well

                try
                {
                    // Prompt user to select base game or DLC mode
                    Console.WriteLine("==Select extraction mode==\n[1] Base Game\n[2] DLC\n[3] OffShot (PSVita only -bugged but can extract certain files-)");
                    string selection = Console.ReadLine();

                    if (selection == "1")
                    {
                        ExtractBaseGame();
                    }
                    else if (selection == "2")
                    {
                        ExtractDLC();
                    }
                    else if (selection == "3")
                    {
                        OffShot();
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection.");
                    }

                    // Output total number of extracted files
                    Console.WriteLine($"\nExtraction completed successfully! Total files extracted: {totalExtractedFiles}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
                finally
                {
                    // Close the BinaryReaders
                    if (package != null) package.Close();
                    if (data != null) data.Close();
                    if (patch != null) patch.Close(); // Ensure patch reader is closed
                }
            }
        }

        static void ExtractBaseGame()
        {
            string packageFile = "package.rdp";
            string dataFile = "data.rdp";
            string systemResFile = "system.res";

            // Check if required base game files exist
            if (!File.Exists(packageFile) || !File.Exists(dataFile) || !File.Exists(systemResFile))
            {
                Console.WriteLine("Base game files missing. Ensure package.rdp, data.rdp, and system.res are present.");
                return;
            }

            // Open the package and data files as BinaryReaders
            package = new BinaryReader(File.OpenRead(packageFile));
            data = new BinaryReader(File.OpenRead(dataFile));

            // Process the .res file (system.res) to extract base game files
            Console.WriteLine("Processing system.res...");
            Pres systemRes = new Pres(systemResFile);
            string outDirectory = "BaseGame"; // Output folder for base game extraction
            systemRes.extract(outDirectory);
        }

        static void ExtractDLC()
        {
            string packageFile = "package.rdp";
            string dataFile = "data.rdp";
            string patchFile = "patch.rdp";
            string systemUpdateFile = "system_update.res";

            // Check if required DLC files exist
            if (!File.Exists(packageFile) || !File.Exists(dataFile) || !File.Exists(patchFile) || !File.Exists(systemUpdateFile))
            {
                Console.WriteLine("DLC files missing. Ensure package.rdp, data.rdp, patch.rdp, and system_update.res are present.");
                return;
            }

            // Open the package, data, and patch files as BinaryReaders
            package = new BinaryReader(File.OpenRead(packageFile));
            data = new BinaryReader(File.OpenRead(dataFile));
            patch = new BinaryReader(File.OpenRead(patchFile)); // Open the patch.rdp file

            // Process the system_update.res file to extract DLC files
            Console.WriteLine("Processing system_update.res...");
            Pres systemUpdateRes = new Pres(systemUpdateFile);
            string outDirectory = "DLC"; // Output folder for DLC extraction
            systemUpdateRes.extract(outDirectory);
        }

        static void OffShot()
        {
            string packageFile = "package.rdp";
            string systemOffshotFile = "system.res";

            // Check if required OffShot files exist
            if (!File.Exists(packageFile) || !File.Exists(systemOffshotFile))
            {
                Console.WriteLine("Offshot files missing. Ensure package.rdp, and system.res are present.");
                return;
            }

            // Open the package
            package = new BinaryReader(File.OpenRead(packageFile));

            // Process the system.res file to extract
            Console.WriteLine("Processing system.res...");
            Pres systemOffshotRes = new Pres(systemOffshotFile);
            string outDirectory = "Offshot"; // Output folder for OffShot extraction
            systemOffshotRes.extract(outDirectory);
        }
    }

    // Helper class to redirect console output to both console and file
    public class MultiTextWriter : TextWriter
    {
        private readonly TextWriter consoleWriter;
        private readonly TextWriter fileWriter;

        public MultiTextWriter(TextWriter consoleWriter, TextWriter fileWriter)
        {
            this.consoleWriter = consoleWriter;
            this.fileWriter = fileWriter;
        }

        public override Encoding Encoding => consoleWriter.Encoding;

        public override void Write(char value)
        {
            consoleWriter.Write(value);
            fileWriter.Write(value);
        }

        public override void WriteLine(string value)
        {
            consoleWriter.WriteLine(value);
            fileWriter.WriteLine(value);
        }

        public override void Flush()
        {
            consoleWriter.Flush();
            fileWriter.Flush();
        }
    }
}
