using System;
using System.IO;
using GIL.FUNCTION;
using System.Text.Json;
using System.Collections.Generic;
using BLZ2_COMPRESSION;

namespace GEBCS
{
    class Program
    {
        static void Unpack()
        {
            ResNames resName = new ResNames { Names = new List<string> { ".\\system.res" } };
            Tr2Names tr2Name = new Tr2Names { Names = new List<string>() };
            PackageFiles packageFiles = new PackageFiles();
            DataFiles dataFiles = new DataFiles();

            FileStream pkgStream = new FileStream("package.rdp", FileMode.Open, FileAccess.Read);
            FileStream dataStream = new FileStream("data.rdp", FileMode.Open, FileAccess.Read);
            BR package = new BR(pkgStream);
            BR data = new BR(dataStream);
            PresUnpack pres = new PresUnpack(".\\system.res");
            pres.Unpack(ref package, ref data, ref resName, ref tr2Name, ref packageFiles, ref dataFiles);
            package.Close();
            data.Close();

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText("Resnames.json", JsonSerializer.Serialize(resName, jsonOptions));
            File.WriteAllText("Tr2names.json", JsonSerializer.Serialize(tr2Name, jsonOptions));
            File.WriteAllText("PackageFiles.json", JsonSerializer.Serialize(packageFiles, jsonOptions));
            File.WriteAllText("DataFiles.json", JsonSerializer.Serialize(dataFiles, jsonOptions));

            Console.WriteLine("Unpack finished, press any key");
            Console.ReadKey();
        }

        static void Repack()
        {
            long packageSize = 0x43ABD000; // 1,135,333,376 bytes
            long dataSize = 0xEA12000;     // 245,440,512 bytes

            ResNames resNames = JsonSerializer.Deserialize<ResNames>(File.ReadAllText("Resnames.json"));
            PackageFiles packageFiles = JsonSerializer.Deserialize<PackageFiles>(File.ReadAllText("PackageFiles.json"));
            DataFiles dataFiles = JsonSerializer.Deserialize<DataFiles>(File.ReadAllText("DataFiles.json"));

            Dictionary<string, int> packageDict = new Dictionary<string, int>();
            foreach (var item in packageFiles.Files)
            {
                packageDict[item.Name] = item.Offset;
            }
            Dictionary<string, int> dataDict = new Dictionary<string, int>();
            foreach (var item in dataFiles.Files)
            {
                dataDict[item.Name] = item.Offset;
            }

            // Read original RDP headers
            byte[] packageHeader;
            byte[] dataHeader;
            using (FileStream pkgOriginalStream = new FileStream("package.rdp", FileMode.Open, FileAccess.Read))
            {
                packageHeader = new byte[32];
                pkgOriginalStream.Read(packageHeader, 0, 32);
            }
            using (FileStream dataOriginalStream = new FileStream("data.rdp", FileMode.Open, FileAccess.Read))
            {
                dataHeader = new byte[32];
                dataOriginalStream.Read(dataHeader, 0, 32);
            }

            // Create new RDP files with pre-allocated sizes
            FileStream pkgStream = new FileStream("package_new.rdp", FileMode.Create, FileAccess.Write);
            FileStream dataStream = new FileStream("data_new.rdp", FileMode.Create, FileAccess.Write);
            BW package = new BW(pkgStream);
            BW dataBW = new BW(dataStream);

            // Set file sizes and write headers
            pkgStream.SetLength(packageSize);
            package.Write(packageHeader);
            package.WritePadding(0x800, 0);
            dataStream.SetLength(dataSize);
            dataBW.Write(dataHeader);
            dataBW.WritePadding(0x800, 0);

            Dictionary<int, int> ptSeekSamePackage = new Dictionary<int, int>();
            Dictionary<int, int> ptSeekSameData = new Dictionary<int, int>();

            for (int i = resNames.Names.Count - 1; i >= 0; i--)
            {
                PresRepack pres = new PresRepack(resNames.Names[i]);
                pres.RepackF(ref package, ref dataBW, ref ptSeekSamePackage, ref ptSeekSameData, ref packageDict, ref dataDict);
            }

            package.Close();
            dataBW.Close();

            Console.WriteLine("Repack finished, press any key");
            Console.ReadKey();
        }

        static void Usage()
        {
            Console.WriteLine("Unpack: GEBCS.exe -x");
            Console.WriteLine("Repack: GEBCS.exe -c");
            Console.WriteLine("Note: Place GEBCS.exe, system.res, package.rdp, and data.rdp in the same folder");
            Environment.Exit(0);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("God Eater 2 package unpacker/repacker");
            Console.WriteLine("================================");
            if (args.Length < 1) Usage();
            string mode = args[0].ToUpper();
            if (mode == "-X")
            {
                Unpack();
            }
            else if (mode == "-C")
            {
                Repack();
            }
            else
            {
                Usage();
            }
        }
    }
}