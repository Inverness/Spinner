using System;
using Spinner.Extensibility;
using Spinner.TestTarget.Aspects;

[assembly: BasicLoggingMia(AttributeTargetTypes = "Spinner.TestTarget.*",
                           AttributeTargetElements = MulticastTargets.Method)]

namespace Spinner.TestTarget
{
    public static class TestRun
    {
        public static void Run()
        {
            var mb = new MethodBoundaryTest();

            int two;
            mb.OneAspect(1, out two, "three");
            mb.EntryReturnVoid(1, 2, "three");
            int r1 = mb.EntryReturnInt(1, 2, "three");
            string r2 = mb.EntryReturnString(1, 2, "three");

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
