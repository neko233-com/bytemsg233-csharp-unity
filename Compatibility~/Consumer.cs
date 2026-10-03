using System;
using ByteMsg233;

// Deliberately valid C# 1 syntax. Language version and runtime target are different contracts.
public class Consumer
{
    public static int Main()
    {
        ByteMsgWriter writer = new ByteMsgWriter(128);
        writer.WriteUIntField(1, 233);
        writer.WriteStringField(2, "金币");
        ByteMsgReader reader = new ByteMsgReader(writer.ToArray());
        if (reader.ReadFieldHeader().Tag != 1 || reader.ReadVarint() != 233) return 1;
        if (reader.ReadFieldHeader().Tag != 2 || reader.ReadString() != "金币") return 2;
        reader.RequireEnd();
        Console.WriteLine("PASS compiled package consumer");
        return 0;
    }
}
