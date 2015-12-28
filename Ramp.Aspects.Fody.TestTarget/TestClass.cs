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

    public class TestIntercept2 : MethodInterceptionAspect
    {
        public override void OnInvoke(MethodInterceptionArgs args)
        {
            Console.WriteLine("Entry 2");
            args.Proceed();
            Console.WriteLine("Success 2");
        }
    }

    public class TestPropIntercept : PropertyInterceptionAspect
    {
        public override void OnGetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine("Get value !");
            args.ProceedGetValue();
        }

        public override void OnSetValue(PropertyInterceptionArgs args)
        {
            Console.WriteLine("Set value !");
            args.ProceedSetValue();
        }
    }

    public class TestClass
    {
        private int _x;

        public static void Run()
        {
            var tc = new TestClass();
            int b;
            tc.TestMethod(10, out b, "thirty");
            Console.WriteLine("Result: " + b);
        }

        [TestPropIntercept]
        public int TestProperty
        {
            get { return _x; }

            set { _x = value + 1; }
        }

        [TestPropIntercept]
        public int this[int index, string a]
        {
            //get { return _x + index; }

            set { _x = index + 1; }
        }

        [TestIntercept]
        [TestIntercept2]
        internal void TestMethod(int a, out int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
        }

        internal int TestMethodCompare(int a, int b, ref string c)
        {
            var arguments = new Arguments<int, int, string>
            {
                Item0 = a,
                Item1 = b,
                Item2 = c
            };

            return b - arguments.Item1;
        }

        internal static void TestMethodCompare2(ref object instance, Arguments args)
        {
            var castedArgs = (Arguments<int, int, string>) args;
            ((TestClass) instance).TestMethod(castedArgs.Item0, out castedArgs.Item1, castedArgs.Item2);
        }
    }


    public struct TestClassStruct
    {
        public static void Run()
        {
            var tc = new TestClassStruct();
            Console.WriteLine("Result: " + tc.TestMethod(10, 20, "thirty"));
        }

        [TestIntercept]
        [TestIntercept2]
        internal int TestMethod(int a, int b, string c)
        {
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            return b - a;
        }

        internal int TestMethodCompare(int a, int b, string c)
        {
            var arguments = new Arguments<int, int, string>
            {
                Item0 = a,
                Item1 = b,
                Item2 = c
            };

            return b - arguments.Item1;
        }

        internal static int TestMethodCompare2(ref object instance, Arguments args)
        {
            var castedArgs = (Arguments<int, int, string>) args;
            int a = castedArgs.Item0;
            int b = castedArgs.Item1;
            string c = castedArgs.Item2;
            return ((TestClassStruct) instance).TestMethod(a, b, c);
        }
    }
}
