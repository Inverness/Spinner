using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class ExceptionControlMba : BasicLoggingMba
    {
        public override void OnException(MethodExecutionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnException called " + args.Exception);
            args.Exception = null;
        }
    }
}