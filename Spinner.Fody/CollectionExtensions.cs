using System;
using System.Collections.Generic;
using System.Threading;

namespace Spinner.Fody
{
    internal static class CollectionExtensions
    {
        internal static int AddRange<T>(this ICollection<T> collection, params T[] source)
        {
            int count = 0;
            foreach (T item in source)
            {
                collection.Add(item);
                count++;
            }
            return count;
        }

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

        internal static void SortStable<T>(this IList<T> collection, Comparison<T> comparison = null)
        {
            SortStable(collection, Comparer<T>.Create(comparison));
        }

        internal static void SortStable<T>(this IList<T> collection, IComparer<T> comparer = null)
        {
            if (comparer == null)
                comparer = Comparer<T>.Default;

            QuickSort(collection, comparer, 0, collection.Count - 1);
        }

        private static void QuickSort<T>(IList<T> collection, IComparer<T> comparer, int left, int right)
        {
            int count = collection.Count;
            do
            {
                int i = left;
                int j = right;
                T x = collection[i + ((j - i) >> 1)];

                do
                {
                    while (i < count && comparer.Compare(x, collection[i]) > 0)
                        i++;

                    while (j >= 0 && comparer.Compare(x, collection[j]) < 0)
                        j--;

                    if (i > j)
                        break;

                    if (i < j)
                    {
                        T temp = collection[i];
                        collection[i] = collection[j];
                        collection[j] = temp;
                    }

                    i++;
                    j--;
                } while (i <= j);

                if (j - left <= right - i)
                {
                    if (left < j)
                        QuickSort(collection, comparer, left, j);

                    left = i;
                }
                else {
                    if (i < right)
                        QuickSort(collection, comparer, i, right);

                    right = j;
                }
            } while (left < right);
        }
    }
}
