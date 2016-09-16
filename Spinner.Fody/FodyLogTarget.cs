using System;
using NLog;
using NLog.Targets;

namespace Spinner.Fody
{
    [Target("Fody")]
    public class FodyLogTarget : TargetWithLayout
    {
        private readonly Action<string> _error;
        private readonly Action<string> _warning;
        private readonly Action<string> _info;
        private readonly Action<string> _debug;

        public FodyLogTarget(Action<string> error, Action<string> warning, Action<string> info, Action<string> debug)
        {
            _error = error;
            _warning = warning;
            _info = info;
            _debug = debug;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            string message = Layout.Render(logEvent);

            switch (logEvent.Level.Ordinal)
            {
                case 0:
                    _debug(message);
                    break;
                case 1:
                    _debug(message);
                    break;
                case 2:
                    _info(message);
                    break;
                case 3:
                    _warning(message);
                    break;
                case 4:
                    _error(message);
                    break;
                case 5:
                    _error(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logEvent), "unknown log level: " + logEvent.Level.Name);
            }
        }
    }
}