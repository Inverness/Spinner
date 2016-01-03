using System;

namespace Spinner.Internal
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public sealed class AnalyzedFeaturesAttribute : Attribute
    {
        public AnalyzedFeaturesAttribute(Features features)
        {
            Features = features;
        }

        public Features Features { get; }
    }
}