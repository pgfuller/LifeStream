using System;
using System.Collections.Generic;

namespace LifeStream.Desktop.Services.SystemMonitor;

/// <summary>
/// A thread-safe circular buffer that overwrites oldest items when full.
/// Used for storing time-series metrics data with fixed memory usage.
/// </summary>
/// <typeparam name="T">The type of items stored in the buffer.</typeparam>
public class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _head;      // Next write position
    private int _count;     // Current number of items

    /// <summary>
    /// Creates a new ring buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items to store.</param>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Maximum number of items this buffer can hold.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Current number of items in the buffer.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Adds an item to the buffer. If full, overwrites the oldest item.
    /// </summary>
    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;

            if (_count < _buffer.Length)
                _count++;
        }
    }

    /// <summary>
    /// Gets all items in chronological order (oldest first).
    /// </summary>
    public List<T> ToList()
    {
        lock (_lock)
        {
            var result = new List<T>(_count);

            if (_count == 0)
                return result;

            // Calculate start position (oldest item)
            int start = _count < _buffer.Length ? 0 : _head;

            for (int i = 0; i < _count; i++)
            {
                int index = (start + i) % _buffer.Length;
                result.Add(_buffer[index]);
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the most recent N items in chronological order (oldest first).
    /// </summary>
    /// <param name="count">Number of recent items to retrieve.</param>
    public List<T> GetRecent(int count)
    {
        lock (_lock)
        {
            if (count <= 0 || _count == 0)
                return new List<T>();

            int actualCount = Math.Min(count, _count);
            var result = new List<T>(actualCount);

            // Calculate start position for the requested window
            int skipCount = _count - actualCount;
            int start = _count < _buffer.Length ? skipCount : (_head + skipCount) % _buffer.Length;

            for (int i = 0; i < actualCount; i++)
            {
                int index = (start + i) % _buffer.Length;
                result.Add(_buffer[index]);
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the most recent item, or default if buffer is empty.
    /// </summary>
    public T? GetLatest()
    {
        lock (_lock)
        {
            if (_count == 0)
                return default;

            // Head points to next write position, so latest is at head - 1
            int latestIndex = (_head - 1 + _buffer.Length) % _buffer.Length;
            return _buffer[latestIndex];
        }
    }

    /// <summary>
    /// Clears all items from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }
}
