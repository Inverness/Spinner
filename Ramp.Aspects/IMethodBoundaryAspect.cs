using System;

namespace Ramp.Aspects
{
    public interface IMethodBoundaryAspect : IAspect
    {
        void OnEntry(ref MethodExecutionArgs args);

        void OnExit(ref MethodExecutionArgs args);

        void OnException(ref MethodExecutionArgs args);

        void OnSuccess(ref MethodExecutionArgs args);

        void OnYield(ref MethodExecutionArgs args);

        void OnResume(ref MethodExecutionArgs args);

        bool FilterException(ref MethodExecutionArgs args, Exception ex);
    }
}
