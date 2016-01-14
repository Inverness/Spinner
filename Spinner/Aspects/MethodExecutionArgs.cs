using System;
using System.Diagnostics;

namespace Spinner.Aspects
{
    /// <summary>
    ///     Arguments provided to a method boundary aspect about the method being executed.
    /// </summary>
    public sealed class MethodExecutionArgs : MethodArgs
    {
        [DebuggerStepThrough]
        public MethodExecutionArgs(object instance, Arguments arguments)
            : base(instance, arguments)
        {
        }

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
    }
}