using System;
using Spinner.Aspects;
using Spinner.Aspects.Advices;
using Spinner.Extensibility;

namespace Spinner.TestTarget.Aspects
{
    public sealed class ComposedBasicLoggingMba : TypeLevelAspect
    {
        [MethodEntryAdvice]
        [MulticastPointcut(Targets = MulticastTargets.Method, Attributes = MulticastAttributes.Public | ~MulticastAttributes.AnyVisibility)]
        public void OnEntry(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnEntry called ");
        }

        [MethodSuccessAdvice(Master = "OnEntry")]
        public void OnSuccess(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnSuccess called");
        }

        [MethodExitAdvice(Master = "OnEntry")]
        public void OnExit(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnExit called");
        }

        [MethodExceptionAdvice(Master = "OnEntry")]
        public void OnException(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnException called " + args.Exception);
            throw new Exception("Wrapping Exception", args.Exception);
        }

        public bool FilterException(MethodExecutionArgs args, Exception ex)
        {
            Console.WriteLine(GetType().Name + " FilterException called " + ex);
            return true;
        }

        [MethodYieldAdvice(Master = "OnEntry")]
        public void OnYield(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnYield called");
        }

        [MethodResumeAdvice(Master = "OnEntry")]
        public void OnResume(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnResume called");
        }
    }
}