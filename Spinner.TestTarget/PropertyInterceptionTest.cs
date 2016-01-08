using Spinner.TestTarget.Aspects;

namespace Spinner.TestTarget
{
    public class PropertyInterceptionTest
    {
        private int _x;
        
        [BasicLogginaPia]
        public int Basic
        {
            get { return _x; }

            set { _x = value + 1; }
        }

        [BasicLogginaPia]
        public int this[int index, string a]
        {
            get { return _x + index; }

            set { _x = index + 1; }
        }
    }
}