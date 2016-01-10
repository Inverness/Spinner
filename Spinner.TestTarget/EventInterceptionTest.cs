using System;
using Spinner.TestTarget.Aspects;

namespace Spinner.TestTarget
{
    public class EventInterceptionTest
    {
        private EventHandler _customHandlers;

        [BasicLoggingEia]
        public event EventHandler Normal;

        [BasicLoggingEia]
        public static event EventHandler NormalStatic;

        [BasicLoggingEia]
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
}