using System;
using System.Collections.Generic;

namespace UsefulToolkit.Utility
{
    public sealed class RingBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _count;

        public int Capacity => _buffer.Length;
        public int Count => _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// 要素を追加する。
        /// 満杯時は最も古い要素を上書きする。
        /// </summary>
        public void Enqueue(T item)
        {
            int index = (_head + _count) % Capacity;
            _buffer[index] = item;

            if (_count == Capacity)
            {
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _count++;
            }
        }

        /// <summary>
        /// 最も古い要素を取り出して削除する。
        /// </summary>
        public T Dequeue()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("RingBuffer is empty.");
            }

            T value = _buffer[_head];
            _buffer[_head] = default!;

            _head = (_head + 1) % Capacity;
            _count--;

            return value;
        }

        /// <summary>
        /// 最も古い要素を取得する。
        /// </summary>
        public T Peek()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("RingBuffer is empty.");
            }

            return _buffer[_head];
        }

        /// <summary>
        /// 古い順に並んだ配列を取得する。
        /// </summary>
        public T[] AsArray()
        {
            T[] result = new T[_count];

            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[GetIndex(i)];
            }

            return result;
        }

        /// <summary>
        /// 古い順の連続領域として取得する。
        /// 必要な場合は内部配置を整列する。
        /// </summary>
        public ReadOnlySpan<T> AsReadOnlySpan()
        {
            if (_count == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            if (_head != 0)
            {
                T[] temp = new T[_count];

                for (int i = 0; i < _count; i++)
                {
                    temp[i] = _buffer[GetIndex(i)];
                }

                Array.Copy(temp, 0, _buffer, 0, _count);

                if (_count < Capacity)
                {
                    Array.Clear(_buffer, _count, Capacity - _count);
                }

                _head = 0;
            }

            return _buffer.AsSpan(0, _count);
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, Capacity);
            _head = 0;
            _count = 0;
        }

        private int GetIndex(int offset)
        {
            return (_head + offset) % Capacity;
        }
    }
}