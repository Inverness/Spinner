using System;
using System.Diagnostics;
using System.Reflection;

namespace Ramp.Aspects
{
    /// <summary>
    ///     Arguments provided to a method boundary aspect about the method being executed.
    /// </summary>
    public struct MethodExecutionArgs
    {
        [DebuggerStepThrough]
        public MethodExecutionArgs(object instance, Arguments arguments)
        {
            Instance = instance;
            Tag = null;
            Arguments = arguments;
            FlowBehavior = FlowBehavior.Default;
            ReturnValue = null;
            YieldValue = null;
            Exception = null;
            Method = null;
        }

        /// <summary>
        ///     Gets the instance the method is being executed. This will be null for static methods.
        /// </summary>
        public object Instance { get; set; }

        /// <summary>
        ///     Gets or sets user-specified data that is stored while an aspect is being executed.
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        ///     Gets the arguments list for the method invocation. This will be null if the Arguments feature
        ///     has not been specified.
        /// </summary>
        public Arguments Arguments { get; set; }

        /// <summary>
        ///     Gets or sets the current flow behavior. This will only be valid if flow control has been enabled
        ///     for the aspect.
        /// </summary>
        public FlowBehavior FlowBehavior { get; set; }

        /// <summary>
        ///     Gets or sets the current return value. This will only be valid if return interception has been
        ///     enabled for the aspect.
        /// </summary>
        public object ReturnValue { get; set; }

        /// <summary>
        ///     Gets or sets the current async or iterator yield value. This will only be valid if yield
        ///     interception has been enabled for the aspect.
        /// 
        ///     For async methods, this is set to the object being awaited for OnYield(), and the result
        ///     of the await for OnResume(). For iterators this is set to the object being yield returned
        ///     or was previously yield returned.
        /// </summary>
        public object YieldValue { get; set; }

        /// <summary>
        ///     Gets the current exception.
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        ///     Gets the method the aspect was applied to.
        /// </summary>
        public MethodInfo Method { get; set; }
    }
}