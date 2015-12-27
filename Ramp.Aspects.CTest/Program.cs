using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ramp.Aspects.CTest
{
    [Features(Features.OnEntry | Features.FlowControl | Features.GetArguments)]
    internal sealed class ConsoleWriteAspect : MethodBoundaryAspect
    {
        public override void OnEntry(ref MethodExecutionArgs args)
        {
            int a = 0;
            if (args.Arguments.Count != 0)
                a = args.Arguments.GetValue<int>(0);
            Console.WriteLine("Entry: " + a);
        }

        public override void OnExit(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Exit");
        }

        public override void OnSuccess(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Success");
        }

        public override void OnException(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Exception");
        }

        public override void OnYield(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Yield: " + args.YieldValue);
        }

        public override void OnResume(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Resume: " + args.YieldValue);
        }
    }

    [Features(Features.All)]
    internal sealed class ConsoleWriteAspect2 : MethodBoundaryAspect
    {
        public override void OnEntry(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Entry 22");
        }

        public override void OnExit(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Exit 22");
        }

        public override void OnSuccess(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Success 22");
        }

        public override void OnException(ref MethodExecutionArgs args)
        {
            Console.WriteLine("Exception 22");
        }
    }

    [Features(Features.OnException | Features.FlowControl)]
    internal sealed class CatchAndLogExceptions : MethodBoundaryAspect
    {
        private readonly Type _exceptionType;
        private readonly Type _returnType;
        private readonly object _returnValue;
        private readonly bool _hasReturnValue;

        public CatchAndLogExceptions()
            : this(null, null)
        {
        }

        public CatchAndLogExceptions(Type exceptionType)
            : this(exceptionType, null)
        {
        }

        public CatchAndLogExceptions(Type exceptionType, Type returnType)
        {
            _exceptionType = exceptionType ?? typeof(Exception);
            _returnType = returnType ?? typeof(void);
        }

        public CatchAndLogExceptions(Type exceptionType, Type returnType, object returnValue)
        {
            _exceptionType = exceptionType ?? typeof(Exception);
            _returnType = returnType ?? typeof(void);
            _returnValue = returnValue;
            _hasReturnValue = true;
        }

        public override bool FilterException(ref MethodExecutionArgs args, Exception ex)
        {
            return _exceptionType.IsInstanceOfType(ex);
        }

        public override void OnException(ref MethodExecutionArgs args)
        {
            Console.WriteLine(args.Exception.GetType().Name + ": " + args.Exception.Message);
            Console.WriteLine(args.Exception.StackTrace);

            if (_returnType != typeof (void))
            {
                if (_hasReturnValue)
                    args.ReturnValue = _returnValue;
                else if (_returnType.IsValueType)
                    args.ReturnValue = Activator.CreateInstance(_returnType);
                else
                    args.ReturnValue = null;
            }

            args.FlowBehavior = FlowBehavior.Return;
        }
    }

    class TestClass
    {
        public FlowBehavior Gfb()
        {
            return FlowBehavior.RethrowException;
        }

        [ConsoleWriteAspect]
        public int TestProperty1
        {
            get;
            set;
        }

        [ConsoleWriteAspect]
        public void TestMethod1(int a, int b, string c)
        {
            Console.WriteLine("first! " + c);
            if (a < 3)
            {
                Console.WriteLine("Second!" + c);
                return;
            }
            Console.WriteLine("Third! " + b);
        }


        [ConsoleWriteAspect2]
        [ConsoleWriteAspect]
        public int TestMethod2(int ai1, int ai2, string as3, ref double adr4)
        {
            Console.WriteLine("first! " + as3);
            if (ai1 < 3)
            {
                Console.WriteLine("Second!" + as3);
                return 4234;
            }
            Console.WriteLine("Third! " + ai2);
            return 83;
        }

        [ConsoleWriteAspect2]
        [ConsoleWriteAspect]
        public void TestMethod3(int a, int b, string c, ref double d)
        {
            Console.WriteLine("first! " + c);
            if (a < 3)
            {
                Console.WriteLine("Second!" + c);
                return;
            }
            Console.WriteLine("Third! " + b);
        }

        [ConsoleWriteAspect2]
        public int TestMethod4(int a, int b, string c, ref double d)
        {
            Console.WriteLine("first! " + c);
            if (a < 3)
            {
                Console.WriteLine("Second!" + c);
                return 4234;
            }
            Console.WriteLine("Third! " + b);
            return 83;
        }


        [ConsoleWriteAspect2]
        public int TestMethod5(int a, int b, string c, ref double d)
        {
            Console.WriteLine("first! " + c);
            if (a < 3)
            {
                throw new NotImplementedException("Second! " + c);
            }
            Console.WriteLine("Third! " + b);
            return 83;
        }
        
        [ConsoleWriteAspect]
        public int TestMethod6(int arg1, int arg2, string arg3, out string arg4, ref double arg5)
        {
            Console.WriteLine("first! " + arg3);
            if (arg1 < 3)
            {
                Console.WriteLine("Second!" + arg3);
                arg4 = "after second";
                return 4234;
            }
            Console.WriteLine("Third! " + arg2);
            arg4 = "after third";
            return 83;
        }
        
        [ConsoleWriteAspect]
        [ConsoleWriteAspect2]
        public int TestMethod7(int arg1, int arg2, string arg3, out string arg4, ref double arg5)
        {
            Console.WriteLine("first! " + arg3);
            if (arg1 < 3)
            {
                Console.WriteLine("Second!" + arg3);
                arg4 = "after second";
                return 4234;
            }
            Console.WriteLine("Third! " + arg2);
            arg4 = "after third";
            return 83;
        }

        [ConsoleWriteAspect]
        public async Task<int> TestMethodA1(int a, int b, string s)
        {
            Console.WriteLine("In test method");
            if (a > 5)
                return 7;

            int awaitResult = await Number();

            int awaitResult2;
            if ((awaitResult2 = await Number()) >= 50)
            {
                return 99;
            }

            Console.WriteLine("Awaited");
            return awaitResult;
        }

        [ConsoleWriteAspect]
        public IEnumerable<int> TestMethodY1(int a)
        {
            yield return 1;
            yield return 2;
            if (a < 3)
                yield break;
            yield return 3;
            yield return 4;
        }

        private static async Task<int> Number()
        {
            await Task.Delay(1000);
            return 40;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            double four = 4;
            var r1 = new TestClass().TestMethod2(1, 2, "three", ref four);
            var result = TestExceptions(11);
            Console.WriteLine("Result: " + result);
        }

        [CatchAndLogExceptions(typeof(NotImplementedException), typeof(int), 33)]
        private static int TestExceptions(int a)
        {
            return ThrowAnException() + a;
        }

        private static int ThrowAnException()
        {
            throw new NotImplementedException("!!!");
        }
    }
}
