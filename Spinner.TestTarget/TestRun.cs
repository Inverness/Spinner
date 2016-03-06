using System;
using Spinner.Aspects;
using Spinner.Extensibility;
using Spinner.TestTarget.Aspects;

//[assembly: BasicLoggingMia(AttributeTargetTypes = "Spinner.TestTarget.*",
//                           AttributeTargetElements = MulticastTargets.Method)]

//[assembly: Spinner.TestTarget.EntryLogAspect(
//    AttributeInheritance = MulticastInheritance.Multicast,
//    AttributeTargetTypes = "Spinner.*",
//    AttributeTargetTypeAttributes = MulticastAttributes.All & ~MulticastAttributes.CompilerGenerated,
//    AttributeTargetElements = MulticastTargets.Method,
//    AttributeTargetMemberAttributes = MulticastAttributes.All & ~MulticastAttributes.CompilerGenerated
//    )]

//[assembly: Spinner.TestTarget.EntryLogAspect(
//    AttributeInheritance = MulticastInheritance.Multicast,
//    AttributeTargetTypes = "Spinner.TestTarget.EntryLogAspect",
//    AttributeTargetTypeAttributes = MulticastAttributes.All & ~MulticastAttributes.CompilerGenerated,
//    AttributeTargetElements = MulticastTargets.Method,
//    AttributeTargetMemberAttributes = MulticastAttributes.All,
//    AttributeExclude = true
//    )]

namespace Spinner.TestTarget
{
    internal class EntryLogAspect : MethodBoundaryAspect
    {
        public override void OnEntry(MethodExecutionArgs args)
        {
            Console.WriteLine($"---- Entered {args.Method.DeclaringType.Name}.{args.Method.Name}");
        }

        public override void OnExit(MethodExecutionArgs args)
        {
            Console.WriteLine($"---- Exited {args.Method.DeclaringType.Name}.{args.Method.Name}");
        }
    }

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
