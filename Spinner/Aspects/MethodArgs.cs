using System.Reflection;

namespace Spinner.Aspects
{
    public abstract class MethodArgs : AdviceArgs
    {
        protected MethodArgs(object instance, Arguments arguments)
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
        ///     Gets the method the aspect was applied to.
        /// </summary>
        public MethodInfo Method { get; set; }
    }
}