using System.IO;

namespace GE2
{
    class EntryGroup
    {
        int offset;
        int entryCount;

        FileEntry[] entries;

        public EntryGroup(BinaryReader reader)
        {
            offset = reader.ReadInt32();
            entryCount = reader.ReadInt32();

            entries = new FileEntry[entryCount];

            long currentOffset = reader.BaseStream.Position;
            reader.BaseStream.Position = offset;
            for (int i = 0; i < entryCount; ++i)
                entries[i] = new FileEntry(reader);
            reader.BaseStream.Position = currentOffset;
        }

        public void extract(string outDirectory)
        {
            for (int i = 0; i < entryCount; ++i)
                entries[i].extract(outDirectory);
        }
    }
}