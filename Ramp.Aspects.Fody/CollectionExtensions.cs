using System.Collections.Generic;

namespace Ramp.Aspects.Fody
{
    internal static class CollectionExtensions
    {
        internal static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> source)
        {
            foreach (T item in source)
                collection.Add(item);
        }

        internal static void InsertRange<T>(this IList<T> collection, int index, IEnumerable<T> source)
        {
            if (index == collection.Count)
            {
                foreach (T item in source)
                    collection.Add(item);
            }
            else
            {
                foreach (T item in source)
                    collection.Insert(index++, item);
            }
        }

        internal static void InsertRange<T>(this IList<T> collection, int index, params T[] source)
        {
            if (index == collection.Count)
            {
                foreach (T item in source)
                    collection.Add(item);
            }
            else
            {
                foreach (T item in source)
                    collection.Insert(index++, item);
            }
        }
    }
}
