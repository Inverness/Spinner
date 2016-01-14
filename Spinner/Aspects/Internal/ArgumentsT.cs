using System;
using System.Diagnostics;

namespace Spinner.Aspects.Internal
{
    [DebuggerStepThrough]
    public sealed class Arguments<T0> : Arguments
    {
        public const int Size = 1;
        public T0 Item0;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Item0;
        }

        public override void SetValue(int index, object value)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Item0 = (T0) value;
        }

        public override T GetValue<T>(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            return (T) (object) Item0;
        }

        public override void SetValue<T>(int index, T value)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Item0 = (T0) (object) value;
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1> : Arguments
    {
        public const int Size = 2;
        public T0 Item0;
        public T1 Item1;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2> : Arguments
    {
        public const int Size = 3;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2, T3> : Arguments
    {
        public const int Size = 4;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                case 3:
                    return Item3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                case 3:
                    Item3 = (T3) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                case 3:
                    return (T) (object) Item3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                case 3:
                    Item3 = (T3) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2, T3, T4> : Arguments
    {
        public const int Size = 5;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                case 3:
                    return Item3;
                case 4:
                    return Item4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                case 3:
                    Item3 = (T3) value;
                    break;
                case 4:
                    Item4 = (T4) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                case 3:
                    return (T) (object) Item3;
                case 4:
                    return (T) (object) Item4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                case 3:
                    Item3 = (T3) (object) value;
                    break;
                case 4:
                    Item4 = (T4) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2, T3, T4, T5> : Arguments
    {
        public const int Size = 6;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                case 3:
                    return Item3;
                case 4:
                    return Item4;
                case 5:
                    return Item5;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                case 3:
                    Item3 = (T3) value;
                    break;
                case 4:
                    Item4 = (T4) value;
                    break;
                case 5:
                    Item5 = (T5) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                case 3:
                    return (T) (object) Item3;
                case 4:
                    return (T) (object) Item4;
                case 5:
                    return (T) (object) Item5;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                case 3:
                    Item3 = (T3) (object) value;
                    break;
                case 4:
                    Item4 = (T4) (object) value;
                    break;
                case 5:
                    Item5 = (T5) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2, T3, T4, T5, T6> : Arguments
    {
        public const int Size = 7;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                case 3:
                    return Item3;
                case 4:
                    return Item4;
                case 5:
                    return Item5;
                case 6:
                    return Item6;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                case 3:
                    Item3 = (T3) value;
                    break;
                case 4:
                    Item4 = (T4) value;
                    break;
                case 5:
                    Item5 = (T5) value;
                    break;
                case 6:
                    Item6 = (T6) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                case 3:
                    return (T) (object) Item3;
                case 4:
                    return (T) (object) Item4;
                case 5:
                    return (T) (object) Item5;
                case 6:
                    return (T) (object) Item6;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                case 3:
                    Item3 = (T3) (object) value;
                    break;
                case 4:
                    Item4 = (T4) (object) value;
                    break;
                case 5:
                    Item5 = (T5) (object) value;
                    break;
                case 6:
                    Item6 = (T6) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [DebuggerStepThrough]
    public sealed class Arguments<T0, T1, T2, T3, T4, T5, T6, T7> : Arguments
    {
        public const int Size = 8;
        public T0 Item0;
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public Arguments()
            : base(Size)
        {
        }

        public override object GetValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Item0;
                case 1:
                    return Item1;
                case 2:
                    return Item2;
                case 3:
                    return Item3;
                case 4:
                    return Item4;
                case 5:
                    return Item5;
                case 6:
                    return Item6;
                case 7:
                    return Item7;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue(int index, object value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) value;
                    break;
                case 1:
                    Item1 = (T1) value;
                    break;
                case 2:
                    Item2 = (T2) value;
                    break;
                case 3:
                    Item3 = (T3) value;
                    break;
                case 4:
                    Item4 = (T4) value;
                    break;
                case 5:
                    Item5 = (T5) value;
                    break;
                case 6:
                    Item6 = (T6) value;
                    break;
                case 7:
                    Item7 = (T7) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override T GetValue<T>(int index)
        {
            switch (index)
            {
                case 0:
                    return (T) (object) Item0;
                case 1:
                    return (T) (object) Item1;
                case 2:
                    return (T) (object) Item2;
                case 3:
                    return (T) (object) Item3;
                case 4:
                    return (T) (object) Item4;
                case 5:
                    return (T) (object) Item5;
                case 6:
                    return (T) (object) Item6;
                case 7:
                    return (T) (object) Item7;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override void SetValue<T>(int index, T value)
        {
            switch (index)
            {
                case 0:
                    Item0 = (T0) (object) value;
                    break;
                case 1:
                    Item1 = (T1) (object) value;
                    break;
                case 2:
                    Item2 = (T2) (object) value;
                    break;
                case 3:
                    Item3 = (T3) (object) value;
                    break;
                case 4:
                    Item4 = (T4) (object) value;
                    break;
                case 5:
                    Item5 = (T5) (object) value;
                    break;
                case 6:
                    Item6 = (T6) (object) value;
                    break;
                case 7:
                    Item7 = (T7) (object) value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
