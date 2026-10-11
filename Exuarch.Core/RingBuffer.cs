using System.Collections;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class RingBuffer<T> : IReadOnlyList<T>
    {
        private readonly T[] items;
        private int start;
        public RingBuffer(int capacity)
        {
            items = new T[capacity];
        }
        public int Capacity { get { return items.Length; } }
        public int Count { get; private set; }
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new System.ArgumentOutOfRangeException(nameof(index));
                return items[(start + index) % items.Length];
            }
        }
        public void Add(T item)
        {
            if (Count < items.Length)
            {
                items[(start + Count) % items.Length] = item;
                Count++;
            }
            else
            {
                items[start] = item;
                start = (start + 1) % items.Length;
            }
        }
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}
