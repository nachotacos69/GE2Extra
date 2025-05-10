using System.Collections.Generic;

namespace GEBCS
{

    public class Record
    {
        public List<bool> UpdatePointer { get; set; } = new List<bool>();
        public int RTBLTOCOffset { get; set; } // Offset of this TOC entry in .rtbl files (0 for non-.rtbl)
        public string Location { get; set; } = null;
        public int rawOffset { get; set; } // Raw offset with indicator
        public int Offset { get; set; } // Processed offset
        public int ShiftOffset { get; set; }
        public int Size { get; set; }
        public int RealSize { get; set; }
        public int MaxSize { get; set; }
        public int DSize { get; set; }
        public int OffsetName { get; set; }
        public int ChunkName { get; set; }
        public List<string> ElementName { get; set; } = new List<string>();
        public string FileName { get; set; } = null;
        public bool Compression { get; set; }
    }

    public class Pres
    {
        public string Filename { get; set; } = null;
        public uint Magic { get; set; }
        public int GrupOffset { get; set; }
        public byte GrupCount { get; set; }
        public byte GrupVersion { get; set; }
        public ushort CeckSum { get; set; }
        public int Version { get; set; }
        public int ChunkData { get; set; }
        public int TocSize { get; set; }
        public int TotalFile { get; set; }
        public List<int> Grups { get; set; } = null;
        public List<Record> Files { get; set; } = null;
    }
}