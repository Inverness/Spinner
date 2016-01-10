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

            var ei = new EventInterceptionTest();
            ei.Normal += OnNormalEvent;
            ei.Normal += OnNormalEvent2;
            ei.Invoke();
        }

        private static void OnNormalEvent2(object sender, EventArgs eventArgs)
        {
        }

        private static void OnNormalEvent(object sender, EventArgs eventArgs)
        {
        }
    }
}
