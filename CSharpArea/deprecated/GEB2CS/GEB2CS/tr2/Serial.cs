using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GEBCS.tr2
{
    public class ASCII
    {
        public string EncodingType { get; set; } = null;
        public List<string> Data { get; set; } = new List<string>();
    }
    public class INT8
    {
        public string EncodingType { get; set; } = null;
        public List<List<sbyte>> Data { get; set; } = new List<List<sbyte>>();
    }
    public class INT16
    {
        public string EncodingType { get; set; } = null;
        public List<List<short>> Data { get; set; } = new List<List<short>>();
    }
    public class INT32
    {
        public string EncodingType { get; set; } = null;
        public List<List<int>> Data { get; set; } = new List<List<int>>();
    }
    public class UINT8
    {
        public string EncodingType { get; set; } = null;
        public List<List<byte>> Data { get; set; } = new List<List<byte>>();
    }
    public class UINT16
    {
        public string EncodingType { get; set; } = null;
        public List<List<ushort>> Data { get; set; } = new List<List<ushort>>();
    }
    public class UINT32
    {
        public string EncodingType { get; set; } = null;
        public List<List<uint>> Data { get; set; } = new List<List<uint>>();
    }
    public class FLOAT32
    {
        public string EncodingType { get; set; } = null;
        public List<List<Single>> Data { get; set; } = new List<List<Single>>();
    }
}
