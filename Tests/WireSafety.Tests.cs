using System;
using ByteMsg233;
using Xunit;

public sealed class WireSafetyTests
{
    [Fact]
    public void VarintRejectsOverflowInTenthByte()
    {
        var malformed = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
        Assert.Throws<FormatException>(() => new ByteMsgReader(malformed).ReadVarint());
    }

    [Fact]
    public void CountIsCheckedBeforeAllocatingTheCollection()
    {
        var writer = new ByteMsgWriter();
        writer.WriteVarint(100000);
        var reader = new ByteMsgReader(writer.ToArray());
#if !NETFRAMEWORK
        var allocated = GC.GetAllocatedBytesForCurrentThread();
#endif
        Assert.Throws<FormatException>(() => reader.ReadPackedVarints());
#if !NETFRAMEWORK
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 64000);
#endif
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(15)]
    public void RejectsIllegalFieldHeaders(byte header)
    {
        Assert.Throws<FormatException>(() => new ByteMsgReader(new[] { header }).ReadFieldHeader());
    }

    [Fact]
    public void LargestValidFieldTagRoundTrips()
    {
        var writer = new ByteMsgWriter();
        writer.WriteFieldHeader(0x1fffffff, ByteMsgWireType.Varint);
        Assert.Equal(0x1fffffff, new ByteMsgReader(writer.ToArray()).ReadFieldHeader().Tag);
    }

    [Fact]
    public void DuplicateReturnCannotLeaseOneInstanceTwice()
    {
        var pool = new ByteMsgPool<object>(() => new object());
        var item = pool.Rent();
        pool.Return(item);
        Assert.Throws<InvalidOperationException>(() => pool.Return(item));
    }

    [Fact]
    public void ProtocolHelloRequiresBothFieldsAndCorrectWireTypes()
    {
        Assert.Throws<FormatException>(() => ByteMsgProtocol.ReadHello(Array.Empty<byte>()));
        Assert.Throws<FormatException>(() => ByteMsgProtocol.ReadHello(new byte[] { 10, 0, 16, 0 }));
    }
}
