using System.IO;

namespace GE2
{
    class Pres
    {
        int header;
        int groupOffset;
        byte groupCount;
        byte groupVersion;
        short unk;
        int version;
        int dataOffset;

        EntryGroup[] groups;

        public Pres(string file)
        {
            byte[] presData = File.ReadAllBytes(file);
            process(presData);
        }

        public Pres(byte[] presData)
        {
            process(presData);
        }

        void process(byte[] presData)
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(presData));
            header = reader.ReadInt32();
            groupOffset = reader.ReadInt32();
            groupCount = reader.ReadByte();
            groupVersion = reader.ReadByte();
            unk = reader.ReadInt16();
            version = reader.ReadInt32();

            reader.BaseStream.Position = groupOffset; //Only PSP version?
            groups = new EntryGroup[groupCount];
            for (int i = 0; i < groupCount; ++i)
                groups[i] = new EntryGroup(reader);

        }

        public void extract(string outDirectory)
        {
            for (int i = 0; i < groupCount; ++i)
                groups[i].extract(outDirectory);
        }
    }
}