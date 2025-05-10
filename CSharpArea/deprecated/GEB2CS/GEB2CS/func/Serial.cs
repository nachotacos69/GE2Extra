using System.Collections.Generic;

namespace GEBCS
{
    public class ResNames
    {
        public List<string> Names { get; set; } = new List<string>();
    }
    public class Tr2Names
    {
        public List<string> Names { get; set; } = new List<string>();
    }
    public class PackageContent
    {
        public string Name { get; set; } = null;
        public int Offset { get; set; }
    }
    public class PackageFiles
    {
        public List<PackageContent> Files { get; set; } = new List<PackageContent>(); // For package.rdp
    }
    public class DataFiles
    {
        public List<PackageContent> Files { get; set; } = new List<PackageContent>(); // For data.rdp
    }
}