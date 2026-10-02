using System;
using System.Collections.Generic;

namespace ClassIsland.Core;

/// <summary>
/// List-backed min-priority queue. The smallest priority, per <see cref="Comparer{T}"/>, is dequeued first.
/// </summary>
public sealed class PriorityQueue<TElement, TPriority>
{
    private readonly List<Entry> _items = new List<Entry>();
    private readonly IComparer<TPriority> _comparer;

    /// <summary>
    /// Creates a queue that orders priorities with <see cref="Comparer{T}.Default"/>.
    /// </summary>
    public PriorityQueue()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a queue that orders priorities with <paramref name="comparer"/>.
    /// </summary>
    public PriorityQueue(IComparer<TPriority>? comparer)
    {
        _comparer = comparer ?? Comparer<TPriority>.Default;
    }

    /// <summary>
    /// Number of queued elements.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Adds an element with the given priority.
    /// </summary>
    public void Enqueue(TElement element, TPriority priority)
    {
        _items.Add(new Entry(element, priority));
    }

    /// <summary>
    /// Removes and returns the element with the lowest priority.
    /// </summary>
    public TElement Dequeue()
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("Queue empty.");
        }

        var best = 0;
        for (var i = 1; i < _items.Count; i++)
        {
            if (_comparer.Compare(_items[i].Priority, _items[best].Priority) < 0)
            {
                best = i;
            }
        }

        var element = _items[best].Element;
        _items.RemoveAt(best);
        return element;
    }

    /// <summary>
    /// Removes every queued element.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
    }

    private struct Entry
    {
        public Entry(TElement element, TPriority priority)
        {
            Element = element;
            Priority = priority;
        }

        public TElement Element;
        public TPriority Priority;
    }
}
