using System;
using System.Collections.Generic;
using ByteMsg233;
using Xunit;

public sealed class LegacySmokeTests
{
    [Fact]
    public void ExistingRuntimeScenarios()
    {
static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

var writer = new ByteMsgWriter();
writer.WriteFieldHeader(99, ByteMsgWireType.Varint);
writer.WriteVarint(9001);
writer.WriteUIntField(1, 123);
writer.WriteFieldHeader(100, ByteMsgWireType.LengthDelimited);
writer.WriteString("future");
writer.WriteStringField(2, "Hero");
writer.WriteEnumField(3, (int)HeroState.Moving);
writer.WriteFieldHeader(101, ByteMsgWireType.Fixed32);
writer.WriteFixed32(0x12345678);
writer.WriteListField(4, new List<string> { "a", "b" }, (w, value) => w.WriteString(value));
writer.WriteFieldHeader(102, ByteMsgWireType.Fixed64);
writer.WriteFixed64(0x0102030405060708);
writer.WriteMapField(5, new Dictionary<string, string> { ["hp"] = "99" }, (w, key) => w.WriteString(key), (w, value) => w.WriteString(value));

var reader = new ByteMsgReader(writer.ToArray());
var hero = Hero.Rent();

while (!reader.IsEof)
{
    var header = reader.ReadFieldHeader();
    switch (header.Tag)
    {
        case 1:
            hero.Id = (uint)reader.ReadVarint();
            break;
        case 2:
            hero.Name = reader.ReadString();
            break;
        case 3:
            hero.State = ByteMsgEnum.FromValue<HeroState>((int)reader.ReadVarint());
            break;
        case 4:
            hero.Tags = reader.ReadList(r => r.ReadString());
            break;
        case 5:
            hero.Attrs = reader.ReadMap(r => r.ReadString(), r => r.ReadString());
            break;
        default:
            reader.SkipField(header.WireType);
            break;
    }
}

Assert(hero.Id == 123, "id should roundtrip");
Assert(hero.Name == "Hero", "name should roundtrip");
Assert(hero.State == HeroState.Moving, "enum should restore");
Assert(hero.Tags.Count == 2 && hero.Tags[1] == "b", "list should roundtrip");
Assert(hero.Attrs["hp"] == "99", "map should roundtrip");

var blockWriter = new ByteMsgWriter();
blockWriter.WritePackedVarints(new List<ulong> { 1, 2, 127, 128 });
blockWriter.WriteDeltaVarints(new List<ulong> { 100, 101, 109 });
blockWriter.WriteBoolBitset(new List<bool> { true, false, true, true, false, true, false, false, true });
blockWriter.WriteStringList(new List<string> { "rank", "battle" });
var blockReader = new ByteMsgReader(blockWriter.ToArray());
var packed = blockReader.ReadPackedVarints();
Assert(packed.Count == 4 && packed[3] == 128, "packed varint roundtrip failed");
var delta = blockReader.ReadDeltaVarints();
Assert(delta.Count == 3 && delta[2] == 109, "delta varint roundtrip failed");
var flags = blockReader.ReadBoolBitset();
Assert(flags.Count == 9 && flags[0] && !flags[1] && flags[8], "bool bitset roundtrip failed");
var strings = blockReader.ReadStringList();
Assert(strings.Count == 2 && strings[1] == "battle", "string list roundtrip failed");

var bytesWriter = new ByteMsgWriter();
bytesWriter.WriteBytes(new byte[] { 1, 2, 3, 4 });
bytesWriter.WriteBytes(new byte[] { 5, 6 });
var bytesReader = new ByteMsgReader(bytesWriter.ToArray());
var reusableBytes = new ByteMsgByteBuffer(4);
bytesReader.ReadBytes(reusableBytes);
var byteCapacity = reusableBytes.Capacity;
Assert(reusableBytes.Length == 4 && reusableBytes.Span[3] == 4, "byte buffer first read failed");
bytesReader.ReadBytes(reusableBytes);
Assert(reusableBytes.Length == 2 && reusableBytes.Capacity == byteCapacity && reusableBytes.Span[1] == 6, "byte buffer should reuse capacity");

var helloWriter = new ByteMsgWriter();
var localHello = new ByteMsgProtocolHello(7, 6);
ByteMsgProtocol.WriteHello(helloWriter, localHello);
var remoteHello = ByteMsgProtocol.ReadHello(helloWriter.ToArray());
Assert(remoteHello.Version == 7 && remoteHello.MinCompatible == 6, "protocol hello roundtrip failed");
ByteMsgProtocol.CheckCompatible(localHello, remoteHello);

hero.Release();
var reused = Hero.Rent();
Assert(reused.Id == 0, "pool should reset id");
Assert(reused.Tags.Count == 0, "pool should reset list");

Console.WriteLine("ByteMsg233 C# runtime tests passed.");


    }
}

public enum HeroState
{
    Idle = 0,
    Moving = 1,
    Dead = 2,
}

public sealed class Hero : IByteMsgResettable
{
    private static readonly ByteMsgPool<Hero> Pool = new(() => new Hero());

    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HeroState State { get; set; } = HeroState.Idle;
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Attrs { get; set; } = new();

    public static Hero Rent() => Pool.Rent();
    public void Release() => Pool.Return(this);

    public void Reset()
    {
        Id = 0;
        Name = string.Empty;
        State = HeroState.Idle;
        Tags.Clear();
        Attrs.Clear();
    }
}
