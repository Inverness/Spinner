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
    }
}
