using System.Collections.Generic;
using Spinner.Aspects;
using Spinner.Fody.Weaving;

namespace Spinner.Fody
{
    /// <summary>
    /// Contains helper methods for logging.
    /// </summary>
    internal static class LogHelper
    {
        internal static string GetJoinedFeatureString(Features features, string separator = "|")
        {
            return features == Features.None ? nameof(Features.None) : string.Join(separator, GetFeatureStrings(features));
        }

        /// <summary>
        /// Gets names of all feature flags.
        /// </summary>
        internal static IEnumerable<string> GetFeatureStrings(Features features)
        {
            if (features == Features.None)
            {
                yield return nameof(Features.None);
                yield break;
            }

            if (features.Has(Features.OnEntry))
                yield return nameof(Features.OnEntry);
            if (features.Has(Features.OnExit))
                yield return nameof(Features.OnExit);
            if (features.Has(Features.OnSuccess))
                yield return nameof(Features.OnSuccess);
            if (features.Has(Features.OnException))
                yield return nameof(Features.OnException);
            if (features.Has(Features.OnYield))
                yield return nameof(Features.OnYield);
            if (features.Has(Features.OnResume))
                yield return nameof(Features.OnResume);
            if (features.Has(Features.Instance))
                yield return nameof(Features.Instance);
            if (features.Has(Features.GetArguments))
                yield return nameof(Features.GetArguments);
            if (features.Has(Features.SetArguments))
                yield return nameof(Features.SetArguments);
            if (features.Has(Features.FlowControl))
                yield return nameof(Features.FlowControl);
            if (features.Has(Features.ReturnValue))
                yield return nameof(Features.ReturnValue);
            if (features.Has(Features.YieldValue))
                yield return nameof(Features.YieldValue);
            if (features.Has(Features.MemberInfo))
                yield return nameof(Features.MemberInfo);
            if (features.Has(Features.Tag))
                yield return nameof(Features.Tag);
        }
    }
}