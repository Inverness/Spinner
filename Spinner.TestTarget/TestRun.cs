using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spinner.TestTarget
{
    public static class TestRun
    {
        public static void Run()
        {
            var mb = new MethodBoundaryTest();

            int two;
            mb.OneAspect(1, out two, "three");

            var mi = new MethodInterceptionTest();

            mi.NoArgs();

            mi.NoArgsReturnInt();

            mi.WithOutArg(1, out two, "three");
        }
    }
}
