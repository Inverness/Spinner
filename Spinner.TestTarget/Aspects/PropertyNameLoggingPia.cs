using System;
using Spinner.Aspects;

namespace Spinner.TestTarget.Aspects
{
    public class PropertyNameLoggingPia : PropertyInterceptionAspect
    {
        public override void OnGetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine(args.Property.Name + " OnGetValue called");
            args.ProceedGetValue();
        }

        public override void OnSetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine(args.Property.Name + " OnSetValue called");
            args.ProceedSetValue();
        }
    }
}