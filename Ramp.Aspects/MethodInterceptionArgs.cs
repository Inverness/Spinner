using System.Diagnostics;
using System.Reflection;

namespace Ramp.Aspects
{
    public struct MethodInterceptionArgs
    {
        [DebuggerStepThrough]
        public MethodInterceptionArgs(object instance, Arguments arguments)
        {
            Instance = instance;
            Arguments = arguments;
            ReturnValue = null;
            Method = null;
        }

        /// <summary>
        ///     Gets the instance the method is being executed. This will be null for static methods.
        /// </summary>
        public object Instance { get; set; }

        /// <summary>
        ///     Gets the arguments list for the method invocation. This will be null if the Arguments feature
        ///     has not been specified.
        /// </summary>
        public Arguments Arguments { get; set; }

        /// <summary>
        ///     Gets or sets the current return value. This will only be valid if return interception has been
        ///     enabled for the aspect.
        /// </summary>
        public object ReturnValue { get; set; }
        /// <summary>
        ///     Gets the method the aspect was applied to.
        /// </summary>
        public MethodInfo Method { get; set; }

        public void Proceed()
        {

        }

        public object Invoke(Arguments arguments)
        {
            return null;
        }
    }
}
