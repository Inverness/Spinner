using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class BasicLoggingMba : MethodBoundaryAspect
    {
        public override void OnEntry(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnEntry called");
        }

        public override void OnSuccess(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnSuccess called");
        }

        public override void OnExit(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnExit called");
        }

        public override void OnException(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnException called " + args.Exception);
            throw new Exception("Wrapping Exception", args.Exception);
        }

        public override bool FilterException(MethodExecutionArgs args, Exception ex)
        {
            Console.WriteLine(GetType().Name + " FilterException called " + ex);
            return true;
        }

        public override void OnYield(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnYield called");
        }

        public override void OnResume(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnResume called");
        }
    }
}