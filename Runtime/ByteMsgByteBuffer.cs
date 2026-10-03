using System;

namespace ByteMsg233
{
    public sealed class ByteMsgByteBuffer : IByteMsgResettable
    {
        private byte[] _buffer;
        private readonly int _maxCapacity;

        public ByteMsgByteBuffer(int capacity = 0) : this(capacity, 16 * 1024 * 1024) { }

        public ByteMsgByteBuffer(int capacity, int maxCapacity)
        {
            if (capacity < 0 || maxCapacity < capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            _maxCapacity = maxCapacity;
            _buffer = capacity > 0 ? new byte[capacity] : Array.Empty<byte>();
        }

        public int Length { get; private set; }
        public int Capacity => _buffer.Length;
        public byte[] Buffer => _buffer;

        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, Length);

        public void EnsureCapacity(int capacity)
        {
            if (capacity < 0 || capacity > _maxCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_buffer.Length >= capacity)
            {
                return;
            }

            var next = (int)Math.Min(_maxCapacity, Math.Max((long)capacity, Math.Max(16L, _buffer.Length * 2L)));
            Array.Resize(ref _buffer, next);
        }

        public void SetLength(int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            EnsureCapacity(length);
            Length = length;
        }

        public void Set(ReadOnlySpan<byte> value)
        {
            SetLength(value.Length);
            value.CopyTo(_buffer);
        }

        public void Reset()
        {
            Length = 0;
        }
    }
}
