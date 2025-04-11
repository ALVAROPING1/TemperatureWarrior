using System.Collections.Generic;
using Meadow;

namespace RingBuffer
{
    public class RingBuffer<T>
    {
        // tail == head -> buffer vacío
        // tail + 1 == head -> buffer lleno
        private int head = 0;
        private int tail = 0;

        // Lista en lugar de cola para permitir resizing entre distintos usos
        // (rondas)
        private List<T> buffer;

        public RingBuffer()
            : this(0) { }

        public RingBuffer(int capacity)
        {
            buffer = new(capacity);
            ResizeAndReset(capacity);
        }

        public bool is_empty()
        {
            return tail == head;
        }

        public bool Dequeue(out T result)
        {
            result = default!;
            if (tail == head)
                return false;
            result = buffer[head];
            head = (head + 1) % buffer.Capacity;
            return true;
        }

        public bool Enqueue(T item)
        {
            if (tail + 1 == head)
                return false;
            buffer[tail] = item;
            tail = (tail + 1) % buffer.Capacity;
            return true;
        }

        public void Clear()
        {
            buffer.Clear();
        }

        public void ResizeAndReset(int newCapacity)
        {
            head = 0;
            tail = 0;
            if (newCapacity <= buffer.Capacity)
                return;
            buffer.Capacity = newCapacity;
            for (int i = buffer.Count; i < buffer.Capacity; ++i)
                buffer.Add(default!);
        }
    }
}
