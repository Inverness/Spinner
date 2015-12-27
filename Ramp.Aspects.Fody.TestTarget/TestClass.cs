using System;
using Ramp.Aspects.Internal;

namespace Ramp.Aspects.Fody.TestTarget
{
    public class TestIntercept : MethodInterceptionAspect
    {
        public override void OnInvoke(MethodInterceptionArgs args)
        {
            Console.WriteLine("Entry");
            args.Proceed();
            Console.WriteLine("Success");
        }
    }

    public class TestClass
    {
        public static void Run()
        {
            Console.WriteLine("Result: " + TestMethod(10, 20, "thirty"));
        }

        [TestIntercept]
        internal static int TestMethod(int a, int b, string c)
        {
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            return b - a;
        }

        internal static int TestMethodCompare(int a, int b, string c)
        {
            var arguments = new Arguments<int, int, string>
            {
                Item0 = a,
                Item1 = b,
                Item2 = c
            };

            return b - arguments.Item1;
        }
    }
}
