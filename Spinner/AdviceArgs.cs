using System.Diagnostics;

namespace Spinner
{
    public abstract class AdviceArgs
    {
        internal object InternalInstance;

        [DebuggerStepThrough]
        protected AdviceArgs(object instance)
        {
            InternalInstance = instance;
        }

        /// <summary>
        ///     Gets the instance the method is being executed. This will be null for static methods.
        /// </summary>
        public object Instance
        {
            get { return InternalInstance; }

            set { InternalInstance = value; }
        }

        /// <summary>
        ///     Gets or sets user-specified data that is stored while an aspect is being executed.
        /// </summary>
        public object Tag { get; set; }
    }
}