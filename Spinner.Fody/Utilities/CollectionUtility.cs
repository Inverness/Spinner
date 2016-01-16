namespace Spinner.Fody.Utilities
{
    internal static class CollectionUtility<T>
    {
        private static T[] s_emptyArray;

        public static T[] EmptyArray
        {
            get
            {
                T[] a = s_emptyArray;
                if (a == null)
                    s_emptyArray = a = new T[0];
                return a;
            }
        }
    }
}
