using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spinner.Internal;

namespace Spinner.TestTarget
{
    [Features(Features.All)]
    public class TestIntercept : MethodInterceptionAspect
    {
        public override void OnInvoke(MethodInterceptionArgs args)
        {
            Console.WriteLine("Entry");
            args.Proceed();
            Console.WriteLine("Success");
        }
    }

    [Features(Features.All)]
    public class TestIntercept2 : MethodInterceptionAspect
    {
        public override void OnInvoke(MethodInterceptionArgs args)
        {
            Console.WriteLine("Entry 2");
            args.Proceed();
            Console.WriteLine("Success 2");
        }
    }

    [Features(Features.All)]
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

    [Features(Features.All)]
    public class TestBoundary : MethodBoundaryAspect
    {
        public override void OnEntry(MethodExecutionArgs args)
        {
            Console.WriteLine("Boundary Entry! " + args.Instance);
        }

        public override void OnSuccess(MethodExecutionArgs args)
        {
            Console.WriteLine("Boundary success! " + args.Instance);
        }
    }

    public class TestClass
    {
        private int _x;
        private EventHandler _testEvent2;

        public static void Run()
        {
            var tc = new TestClass();
            int b;
            tc.TestMethod(10, out b, "thirty");
            Console.WriteLine("Result: " + b);

            int r = tc.TestMethod2(44, out b, "forty");
        }

        public event EventHandler TestEvent;

        public event EventHandler TestEvent2
        {
            add { _testEvent2 += value; }

            remove { _testEvent2 -= value; }
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
            get { return _x + index; }

            set { _x = index + 1; }
        }

        [TestIntercept]
        [TestIntercept2]
        internal void TestMethod(int a, out int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            TestEvent?.Invoke(this, EventArgs.Empty);

        }

        [TestBoundary]
        [TestBoundary]
        internal int TestMethod2(int a, out int b, string c)
        {
            b = 20;
            Console.WriteLine("test: " + (a + b) + " --- " + c);
            if (a > 3)
            {
                Console.WriteLine("test 99");
                return 99;
            }
            Console.WriteLine("Test 22");
            return a;
        }

        internal async Task<int> TestAsyncMethod(int a, int b, string c)
        {
            await Task.Delay(a);
            a -= b;
            await Task.Delay(a);
            a -= b;
            await Task.Delay(a);
            return b - a;
        }

        [TestBoundary]
        internal async Task<int> TestAsyncMethodCompareSimple(int a, int b, string c)
        {
            Console.WriteLine("Call OnYield");
            a += await GetNum(3);
            Console.WriteLine("Call OnResume");

            Console.WriteLine("Call OnYield");
            a += await GetNum(4);
            Console.WriteLine("Call OnResume");

            if (a > 5)
                return a;

            Console.WriteLine("Call OnYield");
            a += await GetNum(5);
            Console.WriteLine("Call OnResume");

            Console.WriteLine("Call OnYield");
            a += await GetNum(6);
            Console.WriteLine("Call OnResume");

            return a;
        }

        [TestBoundary]
        internal async Task TestAsyncMethodCompareSimpleVoid(int a, int b, string c)
        {
            Console.WriteLine("Call OnYield");
            a += await GetNum(3);
            Console.WriteLine("Call OnResume");

            Console.WriteLine("Call OnYield");
            a += await GetNum(4);
            Console.WriteLine("Call OnResume");

            Console.WriteLine("Call OnYield");
            a += await GetNum(5);
            Console.WriteLine("Call OnResume");

            Console.WriteLine("Call OnYield");
            a += await GetNum(6);
            Console.WriteLine("Call OnResume");
        }

        internal Task<int> GetNum(int a)
        {
            return Task.FromResult(a + 1);
        }

        internal async Task<int> TestAsyncMethodCompare(int a, int b, string c)
        {
            Console.WriteLine("Call OnEntry");
            try
            {
                Console.WriteLine("Call OnYield");
                await Task.Delay(3);
                Console.WriteLine("Call OnResume");

                Console.WriteLine("Call OnYield");
                await Task.Delay(4);
                Console.WriteLine("Call OnResume");

                Console.WriteLine("Call OnYield");
                await Task.Delay(5);
                Console.WriteLine("Call OnResume");

                Console.WriteLine("Call OnYield");
                await Task.Delay(6);
                Console.WriteLine("Call OnResume");

                int returnValue = 3;
                Console.WriteLine("Call OnSuccess");
                return returnValue;
            }
            catch (Exception ex) when (FilterFunc(a, ex))
            {
                Console.WriteLine("Call OnException");
                throw;
            }
            finally
            {
                Console.WriteLine("Call OnExit");
            }
        }

        internal IEnumerable<int> TestGeneratorMethod(int a, int b, string c)
        {
            yield return a;
            a -= b;
            yield return a;
            a -= b;
            yield return a;
            if (a < 5)
                yield break;
            a -= b;
            yield return a;
        } 

        internal int TestExceptionMethod(int a, out int b, string c)
        {
            try
            {
                b = 20;
                Console.WriteLine("test: " + (a + b) + " --- " + c);
            }
            catch (FormatException ex)
            {
                Console.WriteLine(ex.Message);
                b = 4;
                return 3;
            }
            catch (Exception ex) when (FilterFunc(a, ex))
            {
                b = 3;
                return 2;
            }
            catch (Exception)
            {
                b = 6;
                return 1;
            }
            finally
            {
                a += 3;
            }
            return b - a;
        }

        internal int TestSimpleExceptionMethod(int a, out int b, string c)
        {
            try
            {
                b = 20;
                Console.WriteLine("test: " + (a + b) + " --- " + c);
            }
            catch (Exception ex) when (FilterFunc(a, ex))
            {
                b = 3;
                return 2;
            }
            finally
            {
                a += 3;
            }
            return b - a;
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

        internal static bool FilterFunc(int a, Exception ex)
        {
            return a > 5;
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
