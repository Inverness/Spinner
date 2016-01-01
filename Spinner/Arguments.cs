using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Spinner
{
    public abstract class Arguments : IList<object>, IReadOnlyList<object>
    {
        public const int MaxItems = 8;

        private readonly int _count;

        [DebuggerStepThrough]
        protected Arguments(int count)
        {
            _count = count;
        }

        public int IndexOf(object item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (Equals(GetValue(i), item))
                    return i;
            }
            return -1;
        }

        public void Insert(int index, object item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        public object this[int index]
        {
            get { return GetValue(index); }

            set { SetValue(index, value); }
        }

        public void Add(object item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(object item)
        {
            return IndexOf(item) != -1;
        }

        public void CopyTo(object[] array, int arrayIndex)
        {
            for (int i = 0; i < _count; i++)
            {
                array[arrayIndex++] = GetValue(i);
            }
        }

        public int Count => _count;

        public bool IsReadOnly => true;

        public bool Remove(object item)
        {
            throw new NotSupportedException();
        }

        public abstract object GetValue(int index);

        public abstract void SetValue(int index, object value);

        public abstract T GetValue<T>(int index);

        public abstract void SetValue<T>(int index, T value);

        public IEnumerator<object> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return GetValue(i);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
