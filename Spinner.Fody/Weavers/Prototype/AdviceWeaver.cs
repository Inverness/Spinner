using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Spinner.Fody.Weavers.Prototype
{
    internal abstract class AdviceWeaver
    {
        protected readonly AspectWeaver2 P;

        internal AdviceWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
        {
            P = p;
            AdviceMethod = adviceMethod;
        }

        public MethodDefinition AdviceMethod { get; }

        public void Weave(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous)
        {
            Debug.Assert(method != null && offset >= -1);
            WeaveCore(method, stateMachine, offset, previous);
        }

        protected abstract void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous);

        /// <summary>
        /// Gets an effective parameter count by excluding the value parameter of a property setter.
        /// </summary>
        protected static int GetEffectiveParameterCount(MethodDefinition method)
        {
            int e = method.Parameters.Count;
            if (method.IsSetter || method.IsAddOn || method.IsRemoveOn)
                e--;
            return e;
        }

        protected void GetArgumentContainerInfo(
            MethodDefinition method,
            out GenericInstanceType type,
            out FieldReference[] fields)
        {
            int effectiveParameterCount = method.Parameters.Count;
            if (method.IsSetter)
                effectiveParameterCount--;

            if (effectiveParameterCount == 0)
            {
                type = null;
                fields = null;
                return;
            }

            var baseParameterTypes = new TypeReference[effectiveParameterCount];
            for (int i = 0; i < effectiveParameterCount; i++)
            {
                TypeReference pt = method.Parameters[i].ParameterType;

                if (pt.IsByReference)
                    pt = pt.GetElementType();

                baseParameterTypes[i] = P.Context.SafeImport(pt);
            }

            TypeDefinition typeDef = P.Context.Spinner.ArgumentsT[effectiveParameterCount];
            type = P.Context.SafeImport(typeDef).MakeGenericInstanceType(baseParameterTypes);

            fields = new FieldReference[effectiveParameterCount];

            for (int i = 0; i < effectiveParameterCount; i++)
            {
                FieldDefinition fieldDef = P.Context.Spinner.ArgumentsT_Item[effectiveParameterCount][i];
                FieldReference field = P.Context.SafeImport(fieldDef).WithGenericDeclaringType(type);

                fields[i] = field;
            }
        }

        protected void AddCompilerGeneratedAttribute(ICustomAttributeProvider definition)
        {
            MethodReference ctor = P.Context.SafeImport(P.Context.Framework.CompilerGeneratedAttribute_ctor);

            definition.CustomAttributes.Add(new CustomAttribute(ctor));
        }
    }
}