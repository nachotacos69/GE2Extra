using System;
using System.IO;

class NameData
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Path { get; set; } = "";
    public string Path2 { get; set; } = "";
    public string NoSetPath { get; set; } = "";
}

class NStruct
{
    public static NameData ParseName(BinaryReader reader, uint NameOffset, uint NameValue)
    {
        NameData nameData = new NameData();

        if (NameOffset == 0 || NameValue == 0) return null;

        long baseOffset = NameOffset;
        reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);

        if (NameValue >= 1) nameData.Name = ReadString(reader, reader.ReadUInt32());
        if (NameValue >= 2) nameData.Type = ReadString(reader, reader.ReadUInt32());
        if (NameValue >= 3) nameData.Path = ReadString(reader, reader.ReadUInt32());
        if (NameValue >= 4) nameData.Path2 = ReadString(reader, reader.ReadUInt32());
        if (NameValue >= 5) nameData.NoSetPath = ReadString(reader, reader.ReadUInt32());

        return nameData;
    }

    private static string ReadString(BinaryReader reader, uint offset)
    {
        if (offset == 0) return "";

        long currentPos = reader.BaseStream.Position;
        reader.BaseStream.Seek(offset, SeekOrigin.Begin);

        string result = "";
        while (true)
        {
            byte b = reader.ReadByte();
            if (b == 0) break;
            result += (char)b;
        }

        reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
        return result;
    }
}
