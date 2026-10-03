using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ByteMsg233;
using Xunit;

public sealed class BoundaryTests
{
    private enum SmallState : byte { Idle = 0, Active = 233 }

    [Fact]
    public void EnumUnderlyingTypesDoNotWrapOrThrowForValidValues()
    {
        Assert.Equal(SmallState.Active, ByteMsgEnum.FromValue<SmallState>(233));
        Assert.False(ByteMsgEnum.IsDefinedValue<SmallState>(256));
        Assert.False(ByteMsgEnum.IsDefinedValue<SmallState>(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteMsgEnum.FromValue<SmallState>(256));
    }

    [Fact]
    public async Task IndependentServerContextsCanEncodeConcurrently()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            var writer = new ByteMsgWriter();
            for (var i = 0; i < 1000; i++)
            {
                writer.Reset(); writer.WriteVarint((ulong)(worker * 1000 + i)); writer.WriteString("金币");
                var view = writer.ToArraySegment(); var reader = new ByteMsgReader(view.Array!, view.Offset, view.Count);
                Assert.Equal((ulong)(worker * 1000 + i), reader.ReadVarint());
                Assert.Equal("金币", reader.ReadString()); reader.RequireEnd();
            }
        })));
    }

    [Fact]
    public void UnsignedAndSignedBoundariesAndSeededRandomRoundTrip()
    {
        var writer = new ByteMsgWriter();
        var random = new Random(233);
        var bytes = new byte[8];
        var values = new List<ulong> { 0, 1, 127, 128, 16383, 16384, uint.MaxValue, long.MaxValue, 1UL << 63, ulong.MaxValue };
        for (var i = 0; i < 10000; i++) { random.NextBytes(bytes); values.Add(BitConverter.ToUInt64(bytes, 0)); }
        foreach (var value in values) { writer.WriteVarint(value); writer.WriteZigZag(unchecked((long)value)); writer.WriteFixed64(value); }
        var reader = new ByteMsgReader(writer.ToArray());
        foreach (var value in values)
        {
            Assert.Equal(value, reader.ReadVarint());
            Assert.Equal(unchecked((long)value), reader.ReadZigZag());
            Assert.Equal(value, reader.ReadFixed64());
        }
        reader.RequireEnd();
    }

    [Fact]
    public void ByteWindowsAndNestedReadersDoNotCopyAndRespectLimits()
    {
        var data = new byte[] { 99, 3, 2, 1, 7, 99 };
        var root = new ByteMsgReader(data, 1, 4);
        var child = root.ReadSubReader();
        Assert.Equal(4, root.Offset);
        Assert.Equal(3, child.Length);
        data[4] = 8;
        Assert.Equal(new byte[] { 1, 8 }, child.ReadBytes());
        child.RequireEnd(); root.RequireEnd();
        Assert.Throws<FormatException>(() => root.ReadByte());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteMsgReader(data, int.MaxValue, 1));
        var limited = new ByteMsgReader(new byte[] { 2, 1, 0 }, new ByteMsgReaderOptions(maxDepth: 1));
        Assert.Throws<FormatException>(() => limited.ReadSubReader().ReadSubReader());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    public void NestedMessageLengthPrefixesSurviveBufferGrowth(int payloadLength)
    {
        var writer = new ByteMsgWriter(1);
        var payload = Enumerable.Repeat((byte)0x55, payloadLength).ToArray();
        writer.WriteMessage(parent => parent.WriteMessage(child => child.WriteRaw(payload, 0, payload.Length)));
        var nested = new ByteMsgReader(writer.ToArray()).ReadSubReader().ReadSubReader();
        Assert.Equal(payloadLength, nested.Length);
        for (var i = 0; i < payloadLength; i++) Assert.Equal((byte)0x55, nested.ReadByte());
        nested.RequireEnd();
    }

    [Fact]
    public void NestedCallbackFailureRollsBackTheWholeField()
    {
        var writer = new ByteMsgWriter(); writer.WriteVarint(233);
        var original = writer.ToArray();
        Assert.Throws<Exception>(() => writer.WriteMessageField(1, w => { w.WriteString("partial"); throw new Exception("failure"); }));
        Assert.Equal(original, writer.ToArray());
    }

    [Fact]
    public void CollectionLimitsApplyBeforeAllocation()
    {
        foreach (Func<ByteMsgReader, object> read in new Func<ByteMsgReader, object>[]
        {
            r => r.ReadPackedVarints(), r => r.ReadPackedZigZags(), r => r.ReadDeltaVarints(),
            r => r.ReadBoolBitset(), r => r.ReadStringList()
        })
        {
            var writer = new ByteMsgWriter(); writer.WriteVarint(4); writer.WriteFixed64(0);
            var reader = new ByteMsgReader(writer.ToArray(), new ByteMsgReaderOptions(maxCollectionCount: 3));
            Assert.Throws<FormatException>(() => read(reader));
        }
        Assert.Throws<FormatException>(() => new ByteMsgReader(new byte[] { 2, 0, 0 }, new ByteMsgReaderOptions(maxFieldBytes: 1)).ReadBytes());
    }

    [Fact]
    public void InvalidUtf8AndTruncatedPayloadsFailAsFormatErrors()
    {
        Assert.Throws<FormatException>(() => new ByteMsgReader(new byte[] { 2, 0xc0, 0x80 }).ReadString());
        for (var i = 0; i < 8; i++) Assert.Throws<FormatException>(() => new ByteMsgReader(new byte[i]).ReadFixed64());
        Assert.Throws<FormatException>(() => new ByteMsgReader(new byte[] { 9, 1 }).ReadBytes());
        Assert.Throws<FormatException>(() => new ByteMsgReader(new byte[] { 0x80 }).ReadVarint());
        Assert.Throws<FormatException>(() => new ByteMsgReader(Enumerable.Repeat((byte)0x80, 11).ToArray()).ReadVarint());
    }

    [Fact]
    public void UnknownFieldSkipHasNoPayloadAllocationAfterWarmup()
    {
        var writer = new ByteMsgWriter(); writer.WriteBytes(new byte[1024 * 1024]);
        var data = writer.ToArray(); var reader = new ByteMsgReader(data);
        reader.SkipField(ByteMsgWireType.LengthDelimited); reader.Reset(data);
#if !NETFRAMEWORK
        var before = GC.GetAllocatedBytesForCurrentThread();
#endif
        for (var i = 0; i < 100; i++) { reader.Reset(data); reader.SkipField(ByteMsgWireType.LengthDelimited); }
#if !NETFRAMEWORK
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
#endif
        Assert.True(reader.IsEof);
    }

    [Fact]
    public void ReusedWriterAndPoolHotPathsDoNotAllocateAfterWarmup()
    {
        var writer = new ByteMsgWriter(1024);
        var pool = new ByteMsgPool<object>(() => new object()); pool.Prewarm(8);
        Action<ByteMsgWriter> nested = w => w.WriteString("金币");
        for (var i = 0; i < 100; i++) { writer.Reset(); writer.WriteMessage(nested); pool.Return(pool.Rent()); }
#if !NETFRAMEWORK
        var before = GC.GetAllocatedBytesForCurrentThread();
#endif
        for (var i = 0; i < 1000; i++) { writer.Reset(); writer.WriteMessage(nested); pool.Return(pool.Rent()); }
#if !NETFRAMEWORK
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
#endif
        Assert.Equal(8, pool.Count);
    }

    [Fact]
    public void FixedCallerBufferFailsWithoutGrowing()
    {
        var buffer = new byte[2]; var writer = new ByteMsgWriter(buffer);
        writer.WriteVarint(233); Assert.Same(buffer, writer.ToArraySegment().Array);
        Assert.Throws<InvalidOperationException>(() => writer.WriteVarint(1));
        Assert.Equal(2, writer.Length);
        writer.Reset(); writer.WriteVarint(1); Assert.Equal(1, writer.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteMsgByteBuffer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteMsgByteBuffer(0, 4).EnsureCapacity(5));
    }

    [Fact]
    public void SeededMalformedInputFuzzIsBoundedAndFailsWithFormatErrors()
    {
        var random = new Random(233);
        for (var iteration = 0; iteration < 10000; iteration++)
        {
            var bytes = new byte[random.Next(0, 128)]; random.NextBytes(bytes);
            var reader = new ByteMsgReader(bytes, new ByteMsgReaderOptions(128, 128, 8));
            try
            {
                while (!reader.IsEof)
                {
                    var before = reader.Offset;
                    var header = reader.ReadFieldHeader(); reader.SkipField(header.WireType);
                    Assert.True(reader.Offset > before && reader.Offset <= bytes.Length);
                }
            }
            catch (FormatException) { }
        }
    }
}
