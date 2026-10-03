using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ByteMsg233
{
    /// <summary>Caller-owned, single-threaded pool. The factory must create a fresh instance.</summary>
    public sealed class ByteMsgPool<T> where T : class
    {
        private readonly Stack<T> _items = new Stack<T>();
        private readonly HashSet<T> _available = new HashSet<T>(ReferenceComparer.Instance);
        private readonly Func<T> _factory;
        private readonly Action<T> _reset;
        private readonly int _maxRetained;
        public ByteMsgPool(Func<T> factory, Action<T> reset = null) : this(factory, reset, 1024) { }
        public ByteMsgPool(Func<T> factory, Action<T> reset, int maxRetained)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (maxRetained < 0) throw new ArgumentOutOfRangeException(nameof(maxRetained));
            _reset = reset; _maxRetained = maxRetained;
        }
        public T Rent()
        {
            if (_items.Count == 0) return _factory() ?? throw new InvalidOperationException("ByteMsg233 pool factory returned null.");
            var item = _items.Pop(); _available.Remove(item); return item;
        }
        public void Return(T value)
        {
            if (value == null) return;
            if (_available.Contains(value)) throw new InvalidOperationException("ByteMsg233 object was already returned.");
            if (_reset != null) _reset(value);
            else if (value is IByteMsgResettable resettable) resettable.Reset();
            if (_items.Count >= _maxRetained) return;
            _available.Add(value); _items.Push(value);
        }
        public void Prewarm(int count)
        {
            if (count < 0 || count > _maxRetained) throw new ArgumentOutOfRangeException(nameof(count));
            while (_items.Count < count) Return(_factory() ?? throw new InvalidOperationException("ByteMsg233 pool factory returned null."));
        }
        public void Clear() { _items.Clear(); _available.Clear(); }
        public int Count => _items.Count;
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
