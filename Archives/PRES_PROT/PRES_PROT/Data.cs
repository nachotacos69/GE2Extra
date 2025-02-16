using System;
using System.IO;

class Data
{
    public static void ExtractFile(BinaryReader reader, uint interpretedOffset, uint endOffset, uint compressedSize, string name, string type, string path, string path2, string noSetPath, string outputDirectory)
    {
        // Ensure the output directory exists
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string filePath = Path.Combine(outputDirectory, $"{name}.{type}");

        // Handle PATH/PATH2
        if (!string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(path2))
        {
            // Use PATH2 if it exists, otherwise use PATH
            string directoryPath = !string.IsNullOrEmpty(path2) ? path2 : path;

            // Combine the output directory with the PATH/PATH2 directory
            directoryPath = Path.Combine(outputDirectory, directoryPath);

            // Create the directory and any subdirectories
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Handle NoSetPath (PATH= prefix)
            if (!string.IsNullOrEmpty(noSetPath) && noSetPath.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase))
            {
                string originalFilePath = noSetPath.Substring(5); // Remove "PATH=" prefix

                // Construct the destination path within the PATH/PATH2 directory
                string destinationFilePath = Path.Combine(directoryPath, originalFilePath);

                // Ensure the destination directory exists
                string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // Check if the original file exists
                if (File.Exists(originalFilePath))
                {
                    // Handle duplicate file names
                    destinationFilePath = GetUniqueFileName(destinationFilePath);

                    // Copy the file from the original location to the destination path
                    File.Copy(originalFilePath, destinationFilePath, overwrite: true);
                    Console.WriteLine($"Copied file from {originalFilePath} to {destinationFilePath}");
                    return; // Skip the rest of the extraction logic for NoSetPath
                }
                else
                {
                    Console.WriteLine($"Original file not found: {originalFilePath}");
                    return;
                }
            }

            // If NoSetPath is not provided, place the file in the PATH/PATH2 directory
            filePath = Path.Combine(directoryPath, $"{name}.{type}");
        }

        // Handle duplicate file names
        filePath = GetUniqueFileName(filePath);

        // Extract the file data
        reader.BaseStream.Seek(interpretedOffset, SeekOrigin.Begin);
        byte[] data = reader.ReadBytes((int)compressedSize);

        // Check if the data is BLZ2 compressed
        if (BLZ.IsBLZ2(data))
        {
            Console.WriteLine($"Decompressing BLZ2 file: {filePath}");
            data = BLZ.DecompressBLZ2(data);
        }

        // Write the file
        File.WriteAllBytes(filePath, data);
        Console.WriteLine($"Extracted file: {filePath}");
    }

    public static void HandleMarker(uint marker, uint interpretedOffset, uint compressedSize, string name, string type, string outputDirectory)
    {
        string rdpFile = "";
        switch (marker)
        {
            case 0x4:
                rdpFile = "package.rdp";
                break;
            case 0x5:
                rdpFile = "data.rdp";
                break;
            case 0x6:
                rdpFile = "patch.rdp";
                break;
        }

        if (!string.IsNullOrEmpty(rdpFile))
        {
            using (BinaryReader rdpReader = new BinaryReader(File.Open(rdpFile, FileMode.Open)))
            {
                rdpReader.BaseStream.Seek(interpretedOffset, SeekOrigin.Begin);
                byte[] data = rdpReader.ReadBytes((int)compressedSize);

                // Check if the data is BLZ2 compressed
                if (BLZ.IsBLZ2(data))
                {
                    Console.WriteLine($"Decompressing BLZ2 file from {rdpFile}");
                    data = BLZ.DecompressBLZ2(data);
                }

                // Ensure the output directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // Handle duplicate file names
                string filePath = Path.Combine(outputDirectory, $"{name}.{type}");
                filePath = GetUniqueFileName(filePath);

                // Write the file
                File.WriteAllBytes(filePath, data);
                Console.WriteLine($"Extracted file: {filePath}");
            }
        }
    }

    private static string GetUniqueFileName(string filePath)
    {
        // If the file doesn't exist, return the original path
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        // Extract directory, file name, and extension
        string directory = Path.GetDirectoryName(filePath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);

        // Append a suffix like _0000, _0001, etc., until a unique file name is found
        int counter = 0;
        string newFilePath;
        do
        {
            newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter.ToString("D4")}{extension}");
            counter++;
        } while (File.Exists(newFilePath));

        return newFilePath;
    }
}