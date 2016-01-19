using System;
using Spinner.Extensibility;
using Spinner.TestTarget.Aspects;

namespace Spinner.TestTarget
{
    [BasicLoggingEia(
        AttributeTargetElements = MulticastTargets.Event,
        AttributeTargetMemberAttributes = MulticastAttributes.Public,
        AttributeInheritance = MulticastInheritance.Multicast)
        ]
    public class EventInterceptionTest
    {
        private EventHandler _customHandlers;

        //[BasicLoggingEia]
        public event EventHandler Normal;

        //[BasicLoggingEia]
        public static event EventHandler NormalStatic;

        //[BasicLoggingEia]
        public event EventHandler Custom
        {
            add { _customHandlers += value; }

            remove { _customHandlers -= value; }
        }

        public void Invoke()
        {
            Normal?.Invoke(this, EventArgs.Empty);
        }

        public static void InvokeStatic()
        {
            NormalStatic?.Invoke(null, EventArgs.Empty);
        }
    }

    public class EventInterceptionTestSubclass : EventInterceptionTest
    {
        public event EventHandler NormalDerived;

        public void InvokeDerived()
        {
            NormalDerived?.Invoke(this, EventArgs.Empty);
        }
    }
}