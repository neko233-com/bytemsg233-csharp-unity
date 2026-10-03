using System;
using System.Collections.Generic;
using System.Text;

namespace ByteMsg233
{
    /// <summary>Reusable single-threaded writer. Views remain valid only until the next mutation.</summary>
    public sealed class ByteMsgWriter
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private byte[] _buffer;
        private int _length;
        private readonly int _maxCapacity;
        private readonly bool _allowGrowth;
        public ByteMsgWriter(int capacity = 128) : this(capacity, 16 * 1024 * 1024) { }
        public ByteMsgWriter(int capacity, int maxCapacity)
        {
            if (capacity < 0 || maxCapacity < capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new byte[capacity]; _maxCapacity = maxCapacity; _allowGrowth = true;
        }
        public ByteMsgWriter(byte[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _maxCapacity = buffer.Length; _allowGrowth = false;
        }
        public int Length => _length;
        public int Capacity => _buffer.Length;
        public void Reset() { _length = 0; }
        public byte[] ToArray()
        {
            if (_length == 0) return Array.Empty<byte>();
            var result = new byte[_length]; Buffer.BlockCopy(_buffer, 0, result, 0, _length); return result;
        }
        public ArraySegment<byte> ToArraySegment() => new ArraySegment<byte>(_buffer, 0, _length);
        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _length);
        private void Ensure(int additional)
        {
            if (additional < 0 || additional > _maxCapacity - _length) throw new InvalidOperationException("ByteMsg233 writer capacity limit exceeded.");
            var required = _length + additional;
            if (required <= _buffer.Length) return;
            if (!_allowGrowth) throw new InvalidOperationException("ByteMsg233 caller buffer is full.");
            var grown = (int)Math.Min(_maxCapacity, Math.Max((long)required, Math.Max(16L, _buffer.Length * 2L)));
            Array.Resize(ref _buffer, grown);
        }
        public void WriteRaw(ReadOnlySpan<byte> bytes)
        {
            Ensure(bytes.Length); bytes.CopyTo(new Span<byte>(_buffer, _length, bytes.Length)); _length += bytes.Length;
        }
        public void WriteRaw(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
            Ensure(count); Buffer.BlockCopy(bytes, offset, _buffer, _length, count); _length += count;
        }
        private static int VarintSize(ulong value)
        {
            var size = 1; while (value >= 128) { value >>= 7; size++; } return size;
        }
        public void WriteVarint(ulong value)
        {
            Ensure(VarintSize(value));
            while (value >= 128) { _buffer[_length++] = (byte)(value | 128); value >>= 7; }
            _buffer[_length++] = (byte)value;
        }
        public void WriteUInt(uint value) => WriteVarint(value);
        public void WriteULong(ulong value) => WriteVarint(value);
        public void WriteZigZag(long value) => WriteVarint(ZigZagEncode(value));
        public void WriteBool(bool value) => WriteVarint(value ? 1UL : 0UL);
        public void WriteEnum(int value) => WriteVarint(unchecked((ulong)value));
        public void WriteString(string value)
        {
            value = value ?? string.Empty;
            var count = Utf8.GetByteCount(value);
            Ensure(checked(VarintSize((ulong)count) + count));
            WriteVarint((ulong)count);
            _length += Utf8.GetBytes(value, 0, value.Length, _buffer, _length);
        }
        public void WriteBytes(byte[] value) => WriteBytes(value == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(value));
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            Ensure(checked(VarintSize((ulong)value.Length) + value.Length));
            WriteVarint((ulong)value.Length); WriteRaw(value);
        }
        public void WriteBytes(ByteMsgByteBuffer value) => WriteBytes(value == null ? ReadOnlySpan<byte>.Empty : value.Span);
        public void WriteFixed32(uint value)
        {
            Ensure(4);
            for (var i = 0; i < 4; i++) _buffer[_length++] = (byte)(value >> (8 * i));
        }
        public void WriteFixed64(ulong value)
        {
            Ensure(8);
            for (var i = 0; i < 8; i++) _buffer[_length++] = (byte)(value >> (8 * i));
        }
        public void WritePackedVarints(IReadOnlyList<ulong> values)
        {
            WriteVarint((ulong)(values == null ? 0 : values.Count));
            if (values != null) for (var i = 0; i < values.Count; i++) WriteVarint(values[i]);
        }
        public void WritePackedZigZags(IReadOnlyList<long> values)
        {
            WriteVarint((ulong)(values == null ? 0 : values.Count));
            if (values != null) for (var i = 0; i < values.Count; i++) WriteZigZag(values[i]);
        }
        public void WriteDeltaVarints(IReadOnlyList<ulong> values)
        {
            WriteVarint((ulong)(values == null ? 0 : values.Count));
            if (values == null || values.Count == 0) return;
            var previous = values[0]; WriteVarint(previous);
            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i]; WriteZigZag(unchecked((long)current - (long)previous)); previous = current;
            }
        }
        public void WriteBoolBitset(IReadOnlyList<bool> values)
        {
            WriteVarint((ulong)(values == null ? 0 : values.Count));
            if (values == null) return;
            for (var index = 0; index < values.Count; index += 8)
            {
                byte bits = 0;
                for (var bit = 0; bit < Math.Min(8, values.Count - index); bit++)
                    if (values[index + bit]) bits |= (byte)(1 << bit);
                Ensure(1); _buffer[_length++] = bits;
            }
        }
        public void WriteStringList(IReadOnlyList<string> values)
        {
            WriteVarint((ulong)(values == null ? 0 : values.Count));
            if (values != null) for (var i = 0; i < values.Count; i++) WriteString(values[i]);
        }
        internal static bool IsValidWireType(ByteMsgWireType value)
            => value == ByteMsgWireType.Varint || value == ByteMsgWireType.Fixed32 || value == ByteMsgWireType.Fixed64 || value == ByteMsgWireType.LengthDelimited;
        public void WriteFieldHeader(int tag, ByteMsgWireType wireType)
        {
            if (tag <= 0 || tag > 0x1fffffff) throw new ArgumentOutOfRangeException(nameof(tag));
            if (!IsValidWireType(wireType)) throw new ArgumentOutOfRangeException(nameof(wireType));
            WriteVarint(((ulong)tag << 3) | (uint)wireType);
        }
        public void WriteUIntField(int tag, uint value) { WriteFieldHeader(tag, ByteMsgWireType.Varint); WriteUInt(value); }
        public void WriteULongField(int tag, ulong value) { WriteFieldHeader(tag, ByteMsgWireType.Varint); WriteULong(value); }
        public void WriteZigZagField(int tag, long value) { WriteFieldHeader(tag, ByteMsgWireType.Varint); WriteZigZag(value); }
        public void WriteBoolField(int tag, bool value) { WriteFieldHeader(tag, ByteMsgWireType.Varint); WriteBool(value); }
        public void WriteEnumField(int tag, int value) { WriteFieldHeader(tag, ByteMsgWireType.Varint); WriteEnum(value); }
        public void WriteStringField(int tag, string value) { WriteFieldHeader(tag, ByteMsgWireType.LengthDelimited); WriteString(value); }
        public void WriteBytesField(int tag, byte[] value) { WriteFieldHeader(tag, ByteMsgWireType.LengthDelimited); WriteBytes(value); }

        /// <summary>The callback appends to this writer. It must not Reset the writer or alter earlier output.</summary>
        public void WriteMessage(Action<ByteMsgWriter> encode)
        {
            if (encode == null) throw new ArgumentNullException(nameof(encode));
            var start = _length;
            try
            {
                WriteVarint(0); encode(this);
                if (_length <= start) throw new InvalidOperationException("A nested encoder reset its writer.");
                var count = _length - start - 1;
                var extra = VarintSize((ulong)count) - 1;
                Ensure(extra);
                if (extra > 0) Buffer.BlockCopy(_buffer, start + 1, _buffer, start + 1 + extra, count);
                var end = _length + extra; _length = start;
                WriteVarint((ulong)count); _length = end;
            }
            catch { _length = start; throw; }
        }
        public void WriteMessageField(int tag, Action<ByteMsgWriter> encode)
        {
            if (encode == null) throw new ArgumentNullException(nameof(encode));
            var start = _length;
            try { WriteFieldHeader(tag, ByteMsgWireType.LengthDelimited); WriteMessage(encode); }
            catch { _length = start; throw; }
        }
        public void WriteListField<T>(int tag, IReadOnlyList<T> values, Action<ByteMsgWriter, T> writeItem)
        {
            if (writeItem == null) throw new ArgumentNullException(nameof(writeItem));
            if (values == null || values.Count == 0) return;
            WriteMessageField(tag, writer =>
            {
                writer.WriteVarint((ulong)values.Count);
                for (var i = 0; i < values.Count; i++) writeItem(writer, values[i]);
            });
        }
        public void WriteMapField<TKey, TValue>(int tag, IEnumerable<KeyValuePair<TKey, TValue>> values,
            Action<ByteMsgWriter, TKey> writeKey, Action<ByteMsgWriter, TValue> writeValue)
        {
            if (writeKey == null) throw new ArgumentNullException(nameof(writeKey));
            if (writeValue == null) throw new ArgumentNullException(nameof(writeValue));
            if (values == null) return;
            var entries = values as ICollection<KeyValuePair<TKey, TValue>> ?? new List<KeyValuePair<TKey, TValue>>(values);
            if (entries.Count == 0) return;
            WriteMessageField(tag, writer =>
            {
                writer.WriteVarint((ulong)entries.Count);
                foreach (var entry in entries) { writeKey(writer, entry.Key); writeValue(writer, entry.Value); }
            });
        }
        public static ulong ZigZagEncode(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));
        public static long ZigZagDecode(ulong value) => unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1)));
    }
}
