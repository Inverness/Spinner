using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class MethodArgsInitWeaver : AdviceWeaver
    {
        public VariableDefinition Variable { get; private set; }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);

            if (effectiveParameterCount == 0)
            {
                Variable = null;
                return;
            }
            
            GenericInstanceType argumentsType;
            FieldReference[] argumentFields;
            GetArgumentContainerInfo(method, out argumentsType, out argumentFields);

            MethodDefinition constructorDef = Aspect.Context.Spinner.ArgumentsT_ctor[effectiveParameterCount];
            MethodReference constructor = Aspect.Context.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);

            Variable = method.Body.AddVariableDefinition("arguments", argumentsType);

            var il = new ILProcessorEx();
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, Variable);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }
    }
}