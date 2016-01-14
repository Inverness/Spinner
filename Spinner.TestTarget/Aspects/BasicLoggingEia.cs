using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class BasicLoggingEia : EventInterceptionAspect
    {
        public override void OnAddHandler(EventInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnAddHandler called");
            args.ProceedAddHandler();
        }

        public override void OnRemoveHandler(EventInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnRemoveHandler called");
            args.ProceedRemoveHandler();
        }

        public override void OnInvokeHandler(EventInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnInvokeHandler called");
            args.ProceedInvokeHandler();
        }
    }
}