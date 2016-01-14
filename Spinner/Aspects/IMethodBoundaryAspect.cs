using System;

namespace Spinner.Aspects
{
    /// <summary>
    /// Describes the advices that must be implemented for a method boundary aspect.
    /// </summary>
    public interface IMethodBoundaryAspect : IAspect
    {
        /// <summary>
        /// Invoked when entering a method before the body is executed.
        /// </summary>
        /// <param name="args"></param>
        void OnEntry(MethodExecutionArgs args);

        /// <summary>
        /// Invoked when exiting a method after the body has been executed. This call both when returning normally and
        /// when an exception is thrown.
        /// </summary>
        /// <param name="args"></param>
        void OnExit(MethodExecutionArgs args);

        /// <summary>
        /// Invoked when an exception occurrs in a method.
        /// </summary>
        /// <param name="args"></param>
        void OnException(MethodExecutionArgs args);

        /// <summary>
        /// Invoked when a method returns normally without an exception. This is invoked before OnExit().
        /// </summary>
        /// <param name="args"></param>
        void OnSuccess(MethodExecutionArgs args);

        /// <summary>
        /// Invoked before am async or iterator method yields.
        /// </summary>
        /// <param name="args"></param>
        void OnYield(MethodExecutionArgs args);

        /// <summary>
        /// Invoked after an async or iterator method resumes.
        /// </summary>
        /// <param name="args"></param>
        void OnResume(MethodExecutionArgs args);

        /// <summary>
        /// Invoked to determine if an exception should be caught.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="ex"></param>
        /// <returns></returns>
        bool FilterException(MethodExecutionArgs args, Exception ex);
    }
}
