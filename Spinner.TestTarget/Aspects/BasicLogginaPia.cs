using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class BasicLogginaPia : PropertyInterceptionAspect
    {
        public override void OnGetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnGetValue called");
            args.ProceedGetValue();
        }

        public override void OnSetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine(GetType().Name + " OnSetValue called");
            args.ProceedSetValue();
        }
    }
}