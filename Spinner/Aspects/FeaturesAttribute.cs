using System;

namespace Spinner.Aspects
{
    /// <summary>
    ///     Describes features that will affect code generation for aspects and what can be done
    ///     with MethodExecutionArgs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FeaturesAttribute : Attribute
    {
        public FeaturesAttribute(Features features)
        {
            Features = features;
        }

        public Features Features { get; }
    }
}
