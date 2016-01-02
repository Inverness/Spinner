using System.Diagnostics;

namespace Spinner
{
    public abstract class MethodInterceptionArgs : MethodArgs
    {
        [DebuggerStepThrough]
        protected MethodInterceptionArgs(object instance, Arguments arguments)
            : base(instance, arguments)
        {
        }

        /// <summary>
        ///     Gets or sets the current return value. This will only be valid if return interception has been
        ///     enabled for the aspect.
        /// </summary>
        public abstract object ReturnValue { get; set; }

        public abstract void Proceed();

        public abstract object Invoke(Arguments arguments);
    }
}
