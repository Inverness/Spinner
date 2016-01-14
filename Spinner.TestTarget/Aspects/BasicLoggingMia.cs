using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class BasicLoggingMia : MethodInterceptionAspect
    {
        public override void OnInvoke(MethodInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnInvoke called");
            args.Proceed();
        }
    }
}