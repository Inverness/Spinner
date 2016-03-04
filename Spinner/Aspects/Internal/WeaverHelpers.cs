using System;
using System.Diagnostics;
using System.Reflection;

namespace Spinner.Aspects.Internal
{
    /// <summary>
    ///     Internal helpers for compiler code generation.
    /// </summary>
    public static class WeaverHelpers
    {
        private static readonly Type[] s_emptyTypes = new Type[0];

        public static MethodInfo GetMethodInfo(Type type, string methodName, Type[] paramTypes, Type[] genericTypes)
        {
            Debug.Assert(type != null, "type != null");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "!string.IsNullOrEmpty(methodName)");

            if (paramTypes == null)
                paramTypes = s_emptyTypes;
            if (genericTypes == null)
                genericTypes = s_emptyTypes;

            foreach (MethodInfo m in type.GetRuntimeMethods())
            {
                if (m.Name != methodName)
                    continue;

                if (genericTypes.Length != 0 && !m.IsGenericMethodDefinition)
                    continue;
                
                ParameterInfo[] p = m.GetParameters();
                if (p.Length != paramTypes.Length)
                    continue;

                bool mismatch = false;
                for (int i = 0; i < p.Length; i++)
                {
                    Type t = p[i].ParameterType;

                    if (t.IsByRef)
                        t = t.GetElementType();

                    if (t != paramTypes[i])
                    {
                        mismatch = true;
                        break;
                    }
                }

                if (mismatch)
                    continue;

                if (genericTypes.Length != 0)
                    return m.MakeGenericMethod(genericTypes);
                else
                    return m;
            }

            throw new NotImplementedException($"GetMethodInfo failed for type {type}, method {methodName}");
        }
        
        public static void InvokeEvent<T>(Delegate handler, T aspect, EventInterceptionArgs args)
            where T : IEventInterceptionAspect
        {
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                args.Handler = targets[i];
                aspect.OnInvokeHandler(args);
            }
        }

        public static void InvokeEventAdvice(Delegate handler, Action<EventInterceptionArgs> advice, EventInterceptionArgs args)
        {
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                args.Handler = targets[i];
                advice(args);
            }
        }

        public static PropertyInfo GetPropertyInfo(Type type, string name)
        {
            return type.GetTypeInfo().GetDeclaredProperty(name);
        }

        public static EventInfo GetEventInfo(Type type, string name)
        {
            return type.GetTypeInfo().GetDeclaredEvent(name);
        }

        //private const int MaxMeaPoolSize = 16;

        //private static readonly Stack<MethodExecutionArgs> _pool = new Stack<MethodExecutionArgs>(MaxMeaPoolSize);

        //public static MethodExecutionArgs AllocateMethodExecutionArgs(object instance, Arguments arguments)
        //{
        //    MethodExecutionArgs mea;
        //    if (_pool.Count != 0)
        //    {
        //        mea = _pool.Pop();
        //    }
        //    else
        //    {
        //        mea = new MethodExecutionArgs();
        //    }

        //    mea.Instance = instance;
        //    mea.Arguments = arguments;

        //    return mea;
        //}

        //public static void Free(MethodExecutionArgs mea)
        //{
        //    if (mea != null && _pool.Count < MaxMeaPoolSize)
        //    {
        //        mea.Instance = null;
        //        mea.Tag = null;
        //        mea.Arguments = null;
        //        mea.FlowBehavior = FlowBehavior.Default;
        //        mea.ReturnValue = null;
        //        mea.YieldValue = null;
        //        _pool.Push(mea);
        //    }
        //}
    }
}
