using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class PropertyNameLoggingPia : LocationInterceptionAspect
    {
        public override void OnGetValue(LocationInterceptionArgs args)
        {
            Console.WriteLine(args.Location.Name + " OnGetValue called");
            args.ProceedGetValue();
        }

        public override void OnSetValue(LocationInterceptionArgs args)
        {
            Console.WriteLine(args.Location.Name + " OnSetValue called");
            args.ProceedSetValue();
        }
    }
}