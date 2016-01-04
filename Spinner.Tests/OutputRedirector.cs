using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace Spinner.Tests
{
    internal class OutputRedirector : TextWriter
    {
        private readonly ITestOutputHelper _output;
        private readonly StringBuilder _line = new StringBuilder();

        internal OutputRedirector(ITestOutputHelper output)
        {
            _output = output;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value != '\n')
            {
                _line.Append(value);
            }
            else
            {
                if (_line.Length > 0 && _line[_line.Length - 1] == '\r')
                    _line.Length -= 1;

                _output.WriteLine(_line.ToString());
                _line.Clear();
            }
        }
    }
}