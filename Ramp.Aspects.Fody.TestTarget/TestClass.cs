using System;

namespace Ramp.Aspects.Fody.TestTarget
{
    public class TestClass
    {
        public static void Run()
        {
            Console.WriteLine("Result: " + TestMethod(10, 20, "thirty"));
        }

        internal static int TestMethod(int a, int b, string c)
        {
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            return b - a;
        }
    }
}
