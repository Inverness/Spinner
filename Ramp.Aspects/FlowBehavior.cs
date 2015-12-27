
namespace Ramp.Aspects
{
    /// <summary>
    ///     Allows flow control from an aspect.
    /// </summary>
    public enum FlowBehavior
    {
        /// <summary>
        ///     Use the default behavior for the current method. Continue is the default for OnEntry(), OnExit(), and
        ///     OnSuccess(). RethrowException is the default for OnException().
        /// </summary>
        Default,

        /// <summary>
        ///     Continue normal execution. For OnException() this does not rethrow the exception.
        /// </summary>
        Continue,

        /// <summary>
        ///     Rethrows the current exception in OnException().
        /// </summary>
        RethrowException,

        /// <summary>
        ///     Return immediately from the current method. Only available for OnEntry() and OnException().
        /// </summary>
        Return,

        /// <summary>
        ///     Retry execution of a method after it has been interrupted. This is only available for OnException().
        /// </summary>
        Retry,
    }
}
