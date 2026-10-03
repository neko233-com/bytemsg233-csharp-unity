using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ByteMsg233;
using Xunit;

public sealed class InteropTests
{
    private static readonly ulong[] Scalars = { 0, 1, 127, 128, 16384, uint.MaxValue, ulong.MaxValue };
    private static byte[] ParseHex(string text)
    {
        var bytes = new byte[text.Length / 2];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        return bytes;
    }
    private static Dictionary<string, byte[]> LoadVectors() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "vectors.txt"))
        .ToDictionary(line => line.Split('=')[0], line => ParseHex(line.Split('=')[1]), StringComparer.Ordinal);

    [Fact]
    public void CSharpEncodingMatchesPinnedGoRuntimeAndWritesReverseVerificationArtifact()
    {
        var vectors = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var writer = new ByteMsgWriter();
        foreach (var value in Scalars) writer.WriteVarint(value);
        writer.WriteZigZag(long.MinValue); writer.WriteFixed32(0x12345678); writer.WriteFixed64(0x0102030405060708);
        writer.WriteString("金币🙂"); writer.WriteBytes(new byte[] { 0, 1, 127, 255 });
        vectors["scalars"] = writer.ToArray(); writer.Reset();
        writer.WritePackedVarints(new ulong[] { 0, 127, 128, ulong.MaxValue });
        writer.WriteDeltaVarints(new ulong[] { 100, 90, 200, 0, ulong.MaxValue });
        writer.WriteBoolBitset(new[] { true, false, true, true, false, true, false, false, true });
        writer.WriteStringList(new[] { "", "金币", "🙂" });
        vectors["blocks"] = writer.ToArray(); writer.Reset();
        ByteMsgProtocol.WriteHello(writer, new ByteMsgProtocolHello(233, 200));
        vectors["hello"] = writer.ToArray(); writer.Reset();
        writer.WriteFieldHeader(0x1fffffff, ByteMsgWireType.Varint); writer.WriteVarint(233);
        vectors["max_tag"] = writer.ToArray();
        var expected = LoadVectors();
        Assert.Equal(expected.Count, vectors.Count);
        foreach (var vector in vectors) Assert.Equal(expected[vector.Key], vector.Value);
        var lines = vectors.Select(pair => pair.Key + "=" + BitConverter.ToString(pair.Value).Replace("-", "").ToLowerInvariant());
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "csharp-vectors.txt"), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }

    [Fact]
    public void CSharpDecodesGoProducedScalarsBlocksAndHandshake()
    {
        var vectors = LoadVectors(); var reader = new ByteMsgReader(vectors["scalars"]);
        foreach (var value in Scalars) Assert.Equal(value, reader.ReadVarint());
        Assert.Equal(long.MinValue, reader.ReadZigZag());
        Assert.Equal(0x12345678U, reader.ReadFixed32());
        Assert.Equal(0x0102030405060708UL, reader.ReadFixed64());
        Assert.Equal("金币🙂", reader.ReadString());
        Assert.Equal(new byte[] { 0, 1, 127, 255 }, reader.ReadBytes()); reader.RequireEnd();
        reader.Reset(vectors["blocks"]);
        Assert.Equal(new ulong[] { 0, 127, 128, ulong.MaxValue }, reader.ReadPackedVarints());
        Assert.Equal(new ulong[] { 100, 90, 200, 0, ulong.MaxValue }, reader.ReadDeltaVarints());
        Assert.Equal(new[] { true, false, true, true, false, true, false, false, true }, reader.ReadBoolBitset());
        Assert.Equal(new[] { "", "金币", "🙂" }, reader.ReadStringList()); reader.RequireEnd();
        var hello = ByteMsgProtocol.ReadHello(vectors["hello"]);
        Assert.Equal(233UL, hello.Version); Assert.Equal(200UL, hello.MinCompatible);
    }
}
