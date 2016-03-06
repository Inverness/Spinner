using Spinner.Aspects;

namespace Spinner.Fody.Weavers
{
    internal static class FeaturesExtensions
    {
        internal static bool Has(this Features self, Features features)
        {
            return (self & features) != 0;
        }
    }
}