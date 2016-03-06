using System;
using Spinner.TestTarget.Aspects;

namespace Spinner.TestTarget
{
    public class MethodInterceptionTest
    {
        //[BasicLoggingMia]
        internal void NoArgs()
        {
            Console.WriteLine("test no args");
        }

        //[BasicLoggingMia]
        internal int NoArgsReturnInt()
        {
            Console.WriteLine("test no args");
            return 43;
        }

        //[BasicLoggingMia]
        internal void WithOutArg(int a, out int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
        }

        //[BasicLoggingMia]
        internal int WithOutArgReturnInt(int a, out int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            return b * 2;
        }

        //[BasicLoggingMia]
        internal void WithRefArg(int a, ref int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
        }
    }
}