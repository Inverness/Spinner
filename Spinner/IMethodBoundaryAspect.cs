using System;

namespace Spinner
{
    public interface IMethodBoundaryAspect : IAspect
    {
        void OnEntry(MethodExecutionArgs args);

        void OnExit(MethodExecutionArgs args);

        void OnException(MethodExecutionArgs args);

        void OnSuccess(MethodExecutionArgs args);

        void OnYield(MethodExecutionArgs args);

        void OnResume(MethodExecutionArgs args);

        bool FilterException(MethodExecutionArgs args, Exception ex);
    }
}
