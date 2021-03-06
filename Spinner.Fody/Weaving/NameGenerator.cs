﻿using System;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Generates names for weaved members.
    /// </summary>
    internal static class NameGenerator
    {
        // http://stackoverflow.com/questions/2508828/where-to-learn-about-vs-debugger-magic-names

        internal static string MakeAspectFieldName(string methodName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(methodName)}>w__Aspect{aspectIndex}";
        }

        internal static string MakeEventBindingName(string memberName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(memberName)}>w__EventBinding{aspectIndex}";
        }

        internal static string MakePropertyBindingName(string memberName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(memberName)}>w__PropertyBinding{aspectIndex}";
        }

        internal static string MakeMethodBindingName(string memberName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(memberName)}>w__MethodBinding{aspectIndex}";
        }

        internal static string MakeOriginalMethodName(string methodName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(methodName)}>w__Original{aspectIndex}";
        }

        internal static string MakeEventInvokerName(string memberName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(memberName)}>w__EventInvoker{aspectIndex}";
        }

        internal static string MakeEventInvokerDelegateCacheName(string memberName, int aspectIndex)
        {
            return $"<{ExtractOriginalName(memberName)}>w__EventInvokerDelegate{aspectIndex}";
        }

        internal static string MakeArgumentContainerFieldName(int aspectIndex)
        {
            return $"<>w__args{aspectIndex}";
        }

        internal static string MakeAdviceArgsFieldName(int aspectIndex)
        {
            return $"<>w__adviceArgs{aspectIndex}";
        }

        internal static string ExtractOriginalName(string name)
        {
            const StringComparison comparison = StringComparison.InvariantCulture;

            int endBracketIndex;
            if (name.StartsWith("<", comparison) && (endBracketIndex = name.IndexOf(">", comparison)) != -1)
            {
                return name.Substring(1, endBracketIndex - 1);
            }
            else
            {
                return name;
            }
        }
    }
}
