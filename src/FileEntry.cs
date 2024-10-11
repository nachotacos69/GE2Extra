using System.IO;

namespace GE2
{
    class FileEntry
    {
        enum AddressMode
        {
            Package = 0x4,
            Data = 0x5,
            Patch = 0x6  // Added AddressMode for patch.rdp
        };

        int dataSector;
        AddressMode addressMode;
        int compressedSize;
        int infoOffset;
        int infoCount;
        byte[] zeroes;
        public int decompressedSize;

        FileInfo info;
        FileData data;

        public FileEntry(BinaryReader reader)
        {
            try
            {
                int datum = reader.ReadInt32();
                dataSector = datum & 0x0fffffff;
                addressMode = (AddressMode)((datum & 0xf0000000) >> 28);
                compressedSize = reader.ReadInt32();
                infoOffset = reader.ReadInt32();
                infoCount = reader.ReadInt32();
                zeroes = reader.ReadBytes(0x0C);
                decompressedSize = reader.ReadInt32();

                if (datum == 0) return;

                long currentOffset = reader.BaseStream.Position;
                reader.BaseStream.Position = infoOffset;

                info = new FileInfo(reader, infoCount);

                int dataOffset;
                BinaryReader dataReader;
                string sourceFile = string.Empty;  // Added variable to store source file name

                // Handle the AddressMode cases
                switch (addressMode)
                {
                    case AddressMode.Package:
                        dataOffset = dataSector * 0x800;
                        dataReader = Program.package;
                        sourceFile = "package.rdp";  // Set source file
                        break;
                    case AddressMode.Data:
                        dataOffset = dataSector * 0x800;
                        dataReader = Program.data;
                        sourceFile = "data.rdp";  // Set source file
                        break;
                    case AddressMode.Patch:
                        dataOffset = dataSector * 0x800;  // Special address mode for patch.rdp
                        dataReader = Program.patch;
                        sourceFile = "patch.rdp";  // Set source file
                        break;
                    default:
                        dataOffset = dataSector;
                        dataReader = reader;
                        break;
                }

                // Pass the sourceFile to FileData constructor
                dataReader.BaseStream.Position = dataOffset;
                data = new FileData(dataReader, compressedSize, decompressedSize, info.getName(), info.getType(), sourceFile);
                reader.BaseStream.Position = currentOffset;
            }
            catch (EndOfStreamException e)
            {
                throw new Exception($"Failed to read file entry at stream position 0x{reader.BaseStream.Position:X} due to unexpected end of stream.", e);
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred while processing file {info?.getName() ?? "Unknown"} at stream position 0x{reader.BaseStream.Position:X}.", e);
            }
        }

        public void extract(string outDirectory)
        {
            try
            {
                if (data != null)
                {
                    data.extract(outDirectory);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error extracting file: {info.getName()}. Message: {ex.Message}", ex);
            }
        }
    }
}
