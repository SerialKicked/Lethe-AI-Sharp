using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AIToolkit
{
    public class ThreadSafePriorityQueue<T> : IDisposable where T : class
    {
        private readonly SortedDictionary<int, Queue<T>> _queues = [];
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _queues.Values.Sum(q => q.Count);
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _queues.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Enqueue(T item, int priority)
        {
            ArgumentNullException.ThrowIfNull(item);

            _lock.EnterWriteLock();
            try
            {
                if (!_queues.ContainsKey(priority))
                    _queues[priority] = new Queue<T>();

                _queues[priority].Enqueue(item);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool TryDequeue(out T? item)
        {
            item = default;
            _lock.EnterWriteLock();
            try
            {
                // Use Reverse() on SortedDictionary for efficiency instead of OrderByDescending
                foreach (var kvp in _queues.Reverse())
                {
                    if (kvp.Value.Count > 0)
                    {
                        item = kvp.Value.Dequeue();
                        if (kvp.Value.Count == 0)
                            _queues.Remove(kvp.Key);
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}