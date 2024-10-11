using System.IO;
using System.Collections.Generic;

namespace GE2
{
    class FileInfo
    {
        int[] offsets;
        string[] infos;

        public FileInfo(BinaryReader reader, int infoCount)
        {
            List<int> offsetList = new List<int>();
            for (int i = 0; i < infoCount; ++i)
                offsetList.Add(reader.ReadInt32());

            offsets = offsetList.ToArray();

            List<string> infoList = new List<string>();
            for (int i = 0; i < infoCount; ++i)
            {
                reader.BaseStream.Position = offsets[i];
                string info = "";
                char c = reader.ReadChar();
                while (c != '\0')
                {
                    info += c;
                    c = reader.ReadChar();
                }
                infoList.Add(info);
            }

            infos = infoList.ToArray();
        }

        public string getName()
        {
            if (infos.Length == 0) return "";
            return infos[0];
        }

        public string getType()
        {
            if (infos.Length < 2) return "";
            return infos[1];
        }
    }
}