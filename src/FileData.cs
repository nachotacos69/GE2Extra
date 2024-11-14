using System;
using System.Text;
using System.IO;
using PRES_AREA.BLZ2;
using PRES_AREA.BLZ4;


namespace GE2
{
    class FileData
    {
        string fileName;
        string fileType;
        string fullName;

        BinaryReader binaryReader;
        long offset;
        int size;
        int decSize;
        string sourceFile; // New field to track the file source (e.g., package.rdp, data.rdp, etc.)

        public FileData(BinaryReader reader, int compressedSize, int decompressedSize, string name, string type, string sourceFile)
        {
            fileName = name;
            fileType = type;
            fullName = fileName;
            this.sourceFile = sourceFile; // Set the source file

            if (fileType != "") fullName += '.' + fileType;

            binaryReader = reader;
            offset = binaryReader.BaseStream.Position;
            size = compressedSize;
            decSize = decompressedSize;
        }

        public void extract(string outDirectory)
		{
			try
			{
				Console.WriteLine($"Reading: {fullName} within {sourceFile} || In offset: 0x{offset:X}");

				// Reposition the reader to the file's offset and read the data
				binaryReader.BaseStream.Position = offset;
				byte[] data = binaryReader.ReadBytes(size);

				// Check for BLZ2 or BLZ4 magic number and unpack if necessary
				if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x327a6c62)
				{
					Console.WriteLine($"Unpacking BLZ2: {fullName} within {sourceFile} || In offset: 0x{offset:X}");
					data = BLZ2Utils.UnpackBLZ2Data(data);
				}
				else if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x347a6c62) // BLZ4 magic number
				{
					Console.WriteLine($"Unpacking BLZ4: {fullName} within {sourceFile} || In offset: 0x{offset:X}");
					data = BLZ4Utils.UnpackBLZ4Data(data); // Use BLZ4Utils for decompression
				}

				string outputPath;
				string folderName = fileType == "res" ? Path.GetFileNameWithoutExtension(fullName) : fullName;

				if (fileType == "res")
				{
					// Create the directory and ensure it's unique
					outputPath = Path.Combine(outDirectory, folderName);
					outputPath = GetUniqueDirectoryName(outputPath);
					Directory.CreateDirectory(outputPath);

					// Backup .res file
					string backupResPath = Path.Combine(outDirectory, $"{folderName}.res");
					backupResPath = GetUniqueFileName(backupResPath);
					File.WriteAllBytes(backupResPath, data);

					// Log success and increment counter
					Program.totalExtractedFiles++;
					Console.WriteLine($"Extracting: {fullName} within {sourceFile} || In offset: 0x{offset:X} -> 0x{offset + size:X}");

					// Extract contents of .res file
					Pres extracted = new Pres(data);
					extracted.extract(outputPath);
				}
				else
				{
					// For non-res files, just write the data to the output directory
					outputPath = Path.Combine(outDirectory, fullName);
					outputPath = GetUniqueFileName(outputPath);
					File.WriteAllBytes(outputPath, data);

					// Log success
					Program.totalExtractedFiles++;
					Console.WriteLine($"Extracting: {fullName} within {sourceFile} || In offset: 0x{offset:X} -> 0x{offset + size:X}");
				}
			}
			catch (Exception ex)
			{
				// Log error with the file, offset, and reason
				Console.WriteLine($"Error Extracting: {fullName} within {sourceFile} || In offset: 0x{offset:X} || Reason: {ex.Message}");
			}
		}



        private string GetUniqueFileName(string filePath)
        {
            if (!File.Exists(filePath)) return filePath;

            string directory = Path.GetDirectoryName(filePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter:D4}{extension}");
                counter++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        private string GetUniqueDirectoryName(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return dirPath;

            int counter = 1;
            string newDirPath = dirPath;

            while (Directory.Exists(newDirPath))
            {
                newDirPath = $"{dirPath}_{counter:D4}";
                counter++;
            }

            return newDirPath;
        }
    }
}
