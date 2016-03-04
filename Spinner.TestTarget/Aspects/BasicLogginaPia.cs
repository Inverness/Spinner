using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class BasicLogginaPia : LocationInterceptionAspect
    {
        public override void OnGetValue(LocationInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnGetValue called");
            args.ProceedGetValue();
        }

        public override void OnSetValue(LocationInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnSetValue called");
            args.ProceedSetValue();
        }
    }
}