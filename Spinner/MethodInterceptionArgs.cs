using System.Diagnostics;
using System.Reflection;

namespace Spinner
{
    public abstract class MethodInterceptionArgs : AdviceArgs
    {
        [DebuggerStepThrough]
        protected MethodInterceptionArgs(object instance, Arguments arguments)
            : base(instance)
        {
            Arguments = arguments;
        }

        /// <summary>
        ///     Gets the arguments list for the method invocation. This will be null if the Arguments feature
        ///     has not been specified.
        /// </summary>
        public Arguments Arguments { get; set; }

        /// <summary>
        ///     Gets or sets the current return value. This will only be valid if return interception has been
        ///     enabled for the aspect.
        /// </summary>
        public abstract object ReturnValue { get; set; }
        /// <summary>
        ///     Gets the method the aspect was applied to.
        /// </summary>
        public MethodInfo Method { get; set; }

        public abstract void Proceed();

        public abstract object Invoke(Arguments arguments);
    }
}
