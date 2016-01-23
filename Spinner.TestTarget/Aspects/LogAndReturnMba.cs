using System;
using System.Reflection;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class LogAndReturnMba : MethodBoundaryAspect
    {
        public override void OnEntry(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnEntry called");
            MethodInfo method = args.Method;
            if (method.ReturnType != typeof(void) && method.ReturnType.IsValueType)
                args.ReturnValue = Activator.CreateInstance(method.ReturnType);
            args.FlowBehavior = FlowBehavior.Return;
        }

        public override void OnException(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnException called: " + args.Exception);
        }
    }
}