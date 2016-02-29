using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spinner.TestTarget.Aspects;

namespace Spinner.TestTarget
{
    public class MethodBoundaryTest
    {
        [BasicLoggingMba]
        public int OneAspect(int a, out int b, string c)
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

        [BasicLoggingMba]
        [BasicLoggingMba]
        public int TwoAspect(int a, out int b, string c)
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

        [ExceptionControlMba]
        public int ExceptionFlowBehaviorInt(int a, out int b, string c)
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

        [ExceptionControlMba]
        [ExceptionControlMba]
        public int ExceptionFlowBehaviorIntTwo(int a, out int b, string c)
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

        [BasicLoggingMba]
        public async Task<int> AsyncInt(int a, int b, string c)
        {
            a += await GetNum(3);
            
            a += await GetNum(4);

            if (a > 5)
                return a;
            
            a += await GetNum(5);
            
            a += await GetNum(6);

            return a;
        }

        [BasicLoggingMba]
        public async Task AsyncVoid(int a, int b, string c)
        {
            a += await GetNum(3);
            
            a += await GetNum(4);
            
            a += await GetNum(5);
            
            a += await GetNum(6);
        }

        [ExceptionControlMba]
        public async Task<int> ExceptionFlowBehaviorAsyncInt(int a, int b, string c)
        {
            a += await GetNum(3);

            a += await GetNum(4);

            if (a > 5)
                return a;

            a += await GetNum(5);

            a += await GetNum(6);

            return a;
        }

        [ExceptionControlMba]
        [ExceptionControlMba]
        public async Task<int> ExceptionFlowBehaviorAsyncIntTwo(int a, int b, string c)
        {
            a += await GetNum(3);

            a += await GetNum(4);

            if (a > 5)
                return a;

            a += await GetNum(5);

            a += await GetNum(6);

            return a;
        }

        [BasicLoggingMba]
        public IEnumerable<int> GeneratorInt(int a, int b, string c)
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

        private static Task<int> GetNum(int a)
        {
            return Task.FromResult(a * 2);
        }
    }
}