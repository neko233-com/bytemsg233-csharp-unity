using System;
using System.Collections.Generic;
using System.Text;

namespace ByteMsg233
{
    public sealed class ByteMsgReaderOptions
    {
        public static readonly ByteMsgReaderOptions Default = new ByteMsgReaderOptions();
        public int MaxCollectionCount { get; }
        public int MaxFieldBytes { get; }
        public int MaxDepth { get; }
        public ByteMsgReaderOptions(int maxCollectionCount = 1000000, int maxFieldBytes = 16 * 1024 * 1024, int maxDepth = 64)
        {
            if (maxCollectionCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCollectionCount));
            if (maxFieldBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxFieldBytes));
            if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            MaxCollectionCount = maxCollectionCount;
            MaxFieldBytes = maxFieldBytes;
            MaxDepth = maxDepth;
        }
    }

    /// <summary>A bounded view over caller-owned bytes. Keep the input unchanged while reading. Readers are single-threaded.</summary>
    public sealed class ByteMsgReader
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private byte[] _data;
        private int _start, _offset, _end, _depth;
        private ByteMsgReaderOptions _options;
        public ByteMsgReader(byte[] data) : this(data, ByteMsgReaderOptions.Default) { }
        public ByteMsgReader(byte[] data, ByteMsgReaderOptions options)
            : this(data, 0, data == null ? 0 : data.Length, options, 0) { }
        public ByteMsgReader(byte[] data, int offset, int length)
            : this(data, offset, length, ByteMsgReaderOptions.Default, 0) { }
        private ByteMsgReader(byte[] data, int offset, int length, ByteMsgReaderOptions options, int depth)
        {
            _data = data;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ResetView(data, offset, length, depth);
        }
        public int Offset => _offset - _start;
        public int Length => _end - _start;
        public bool IsEof => _offset == _end;
        public int Remaining => _end - _offset;
        public void Reset(byte[] data) => Reset(data, 0, data == null ? 0 : data.Length);
        public void Reset(byte[] data, int offset, int length) => ResetView(data, offset, length, 0);
        private void ResetView(byte[] data, int offset, int length, int depth)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || length > data.Length - offset) throw new ArgumentOutOfRangeException(nameof(length));
            _data = data; _start = offset; _offset = offset; _end = offset + length; _depth = depth;
        }
        public byte ReadByte()
        {
            if (IsEof) throw new FormatException("Unexpected end of ByteMsg233 buffer.");
            return _data[_offset++];
        }
        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var index = 0; index < 10; index++)
            {
                var current = ReadByte();
                if (index == 9 && current > 1) throw new FormatException("ByteMsg233 varint exceeds 64 bits.");
                result |= (ulong)(current & 0x7f) << (index * 7);
                if ((current & 0x80) == 0) return result;
            }
            throw new FormatException("Invalid ByteMsg233 varint.");
        }
        public long ReadZigZag() => ByteMsgWriter.ZigZagDecode(ReadVarint());
        public bool ReadBool() => ReadVarint() != 0;
        private int ReadLength()
        {
            var length = ReadVarint();
            if (length > (ulong)Remaining || length > (ulong)_options.MaxFieldBytes)
                throw new FormatException("ByteMsg233 field exceeds the input or configured byte limit.");
            return (int)length;
        }
        public ArraySegment<byte> ReadBytesSegment()
        {
            var length = ReadLength();
            var result = new ArraySegment<byte>(_data, _offset, length);
            _offset += length;
            return result;
        }
        public byte[] ReadBytes()
        {
            var view = ReadBytesSegment();
            if (view.Count == 0) return Array.Empty<byte>();
            var bytes = new byte[view.Count];
            Buffer.BlockCopy(_data, view.Offset, bytes, 0, view.Count);
            return bytes;
        }
        public ByteMsgByteBuffer ReadBytes(ByteMsgByteBuffer target)
        {
            var view = ReadBytesSegment();
            if (target == null) target = new ByteMsgByteBuffer(view.Count);
            target.SetLength(view.Count);
            Buffer.BlockCopy(_data, view.Offset, target.Buffer, 0, view.Count);
            return target;
        }
        public string ReadString()
        {
            var view = ReadBytesSegment();
            try { return Utf8.GetString(_data, view.Offset, view.Count); }
            catch (DecoderFallbackException e) { throw new FormatException("Invalid ByteMsg233 UTF-8 string.", e); }
        }
        public uint ReadFixed32()
        {
            if (Remaining < 4) throw new FormatException("Truncated ByteMsg233 fixed32.");
            var value = (uint)(_data[_offset] | (_data[_offset + 1] << 8) | (_data[_offset + 2] << 16) | (_data[_offset + 3] << 24));
            _offset += 4; return value;
        }
        public ulong ReadFixed64()
        {
            if (Remaining < 8) throw new FormatException("Truncated ByteMsg233 fixed64.");
            ulong value = 0;
            for (var i = 0; i < 8; i++) value |= (ulong)_data[_offset + i] << (8 * i);
            _offset += 8; return value;
        }
        private int ReadCount(int minBytes, bool bitset = false)
        {
            var count = ReadVarint();
            if (count > (ulong)_options.MaxCollectionCount)
                throw new FormatException("ByteMsg233 collection exceeds the configured count limit.");
            var minimum = bitset ? (count + 7) / 8 : count * (ulong)minBytes;
            if (minimum > (ulong)Remaining) throw new FormatException("ByteMsg233 collection count exceeds the remaining input.");
            return (int)count;
        }
        private static List<T> Prepare<T>(List<T> values, int count)
        {
            if (values == null) return new List<T>(count);
            values.Clear();
            if (values.Capacity < count) values.Capacity = count;
            return values;
        }
        public List<ulong> ReadPackedVarints(List<ulong> values = null)
        {
            var count = ReadCount(1); values = Prepare(values, count);
            for (var i = 0; i < count; i++) values.Add(ReadVarint());
            return values;
        }
        public List<long> ReadPackedZigZags(List<long> values = null)
        {
            var count = ReadCount(1); values = Prepare(values, count);
            for (var i = 0; i < count; i++) values.Add(ReadZigZag());
            return values;
        }
        public List<ulong> ReadDeltaVarints(List<ulong> values = null)
        {
            var count = ReadCount(1); values = Prepare(values, count);
            if (count == 0) return values;
            var current = ReadVarint(); values.Add(current);
            for (var i = 1; i < count; i++)
            {
                current = unchecked((ulong)((long)current + ReadZigZag())); values.Add(current);
            }
            return values;
        }
        public List<bool> ReadBoolBitset(List<bool> values = null)
        {
            var count = ReadCount(0, true); values = Prepare(values, count);
            for (var i = 0; i < count; i += 8)
            {
                var current = ReadByte();
                for (var bit = 0; bit < Math.Min(8, count - i); bit++) values.Add((current & (1 << bit)) != 0);
            }
            return values;
        }
        public List<string> ReadStringList(List<string> values = null)
        {
            var count = ReadCount(1); values = Prepare(values, count);
            for (var i = 0; i < count; i++) values.Add(ReadString());
            return values;
        }
        public ByteMsgFieldHeader ReadFieldHeader()
        {
            var raw = ReadVarint();
            var wire = (ByteMsgWireType)(raw & 7);
            if (raw > uint.MaxValue || (raw >> 3) == 0 || !ByteMsgWriter.IsValidWireType(wire))
                throw new FormatException("Invalid ByteMsg233 field header.");
            return new ByteMsgFieldHeader((int)(raw >> 3), wire);
        }
        public ByteMsgReader ReadSubReader() => ReadSubReader(null);
        public ByteMsgReader ReadSubReader(ByteMsgReader target)
        {
            if (ReferenceEquals(target, this)) throw new ArgumentException("A reader cannot be its own child.", nameof(target));
            if (_depth >= _options.MaxDepth) throw new FormatException("ByteMsg233 nesting exceeds the configured depth limit.");
            var view = ReadBytesSegment();
            if (target == null) return new ByteMsgReader(_data, view.Offset, view.Count, _options, _depth + 1);
            target._options = _options;
            target.ResetView(_data, view.Offset, view.Count, _depth + 1);
            return target;
        }
        public T ReadMessage<T>(Func<ByteMsgReader, T> readValue)
        {
            if (readValue == null) throw new ArgumentNullException(nameof(readValue));
            var nested = ReadSubReader(); var value = readValue(nested); nested.RequireEnd(); return value;
        }
        public List<T> ReadList<T>(Func<ByteMsgReader, T> readItem) => ReadList(readItem, null);
        public List<T> ReadList<T>(Func<ByteMsgReader, T> readItem, List<T> target)
        {
            if (readItem == null) throw new ArgumentNullException(nameof(readItem));
            var nested = ReadSubReader(); var count = nested.ReadCount(1); var values = Prepare(target, count);
            for (var i = 0; i < count; i++) values.Add(readItem(nested));
            nested.RequireEnd(); return values;
        }
        public Dictionary<TKey, TValue> ReadMap<TKey, TValue>(Func<ByteMsgReader, TKey> readKey, Func<ByteMsgReader, TValue> readValue)
            => ReadMap(readKey, readValue, null);
        public Dictionary<TKey, TValue> ReadMap<TKey, TValue>(Func<ByteMsgReader, TKey> readKey, Func<ByteMsgReader, TValue> readValue, Dictionary<TKey, TValue> target)
        {
            if (readKey == null) throw new ArgumentNullException(nameof(readKey));
            if (readValue == null) throw new ArgumentNullException(nameof(readValue));
            var nested = ReadSubReader(); var count = nested.ReadCount(2);
            var values = target ?? new Dictionary<TKey, TValue>(count); values.Clear();
            for (var i = 0; i < count; i++) values[readKey(nested)] = readValue(nested);
            nested.RequireEnd(); return values;
        }
        public void RequireEnd()
        {
            if (!IsEof) throw new FormatException("ByteMsg233 decoder left trailing bytes.");
        }
        public void SkipField(ByteMsgWireType wireType)
        {
            switch (wireType)
            {
                case ByteMsgWireType.Varint: ReadVarint(); break;
                case ByteMsgWireType.Fixed64: ReadFixed64(); break;
                case ByteMsgWireType.LengthDelimited:
                    var length = ReadLength(); _offset += length; break;
                case ByteMsgWireType.Fixed32: ReadFixed32(); break;
                default: throw new FormatException("Unsupported ByteMsg233 wire type.");
            }
        }
    }
}
