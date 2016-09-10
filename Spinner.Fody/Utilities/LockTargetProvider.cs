using System;
using System.Collections.Concurrent;

namespace Spinner.Fody.Utilities
{
    /// <summary>
    /// Provides objects for locking based on a target, rather than locking on the target itself.
    /// </summary>
    internal sealed class LockTargetProvider<T>
    {
        private readonly ConcurrentDictionary<T, object> _locks = new ConcurrentDictionary<T, object>();

        private readonly Func<T, object> _makeLock;

        public LockTargetProvider(Func<T, object> makeLock = null)
        {
            _makeLock = makeLock ?? (k => new object());
        }

        public object Get(T obj)
        {
            return _locks.GetOrAdd(obj, _makeLock);
        }
    }
}
