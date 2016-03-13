using Spinner.Aspects;

namespace Spinner.Fody.Weaving
{
    internal static class FeaturesExtensions
    {
        internal static bool Has(this Features self, Features features)
        {
            return (self & features) != 0;
        }
    }
}