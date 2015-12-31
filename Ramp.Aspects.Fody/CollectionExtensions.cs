using System.Collections.Generic;

namespace Ramp.Aspects.Fody
{
    internal static class CollectionExtensions
    {
        internal static int AddRange<T>(this ICollection<T> collection, IEnumerable<T> source)
        {
            int count = 0;
            foreach (T item in source)
            {
                collection.Add(item);
                count++;
            }
            return count;
        }

        internal static int InsertRange<T>(this IList<T> collection, int index, IEnumerable<T> source)
        {
            int count = 0;
            if (index == collection.Count)
            {
                foreach (T item in source)
                {
                    collection.Add(item);
                    count++;
                }
            }
            else
            {
                foreach (T item in source)
                {
                    collection.Insert(index++, item);
                    count++;
                }
            }
            return count;
        }

        internal static int InsertRange<T>(this IList<T> collection, int index, params T[] source)
        {
            int count = 0;
            if (index == collection.Count)
            {
                foreach (T item in source)
                {
                    collection.Add(item);
                    count++;
                }
            }
            else
            {
                foreach (T item in source)
                {
                    collection.Insert(index++, item);
                    count++;
                }
            }
            return count;
        }
    }
}
