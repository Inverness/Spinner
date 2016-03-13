using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Spinner.Aspects;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weaving.AdviceWeavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal sealed class MethodInterceptionAdviceWeaver : AdviceWeaver
    {
        private const string InvokeMethodName = "Invoke";

        private readonly AdviceInfo _invokeAdvice;
        private readonly MethodDefinition _method;
        private MethodDefinition _original;
        private FieldReference _returnValueField;
        private VariableDefinition _miaVar;

        internal MethodInterceptionAdviceWeaver(AspectWeaver parent, AdviceInfo invoke, MethodDefinition method)
            : base(parent, method)
        {
            _invokeAdvice = invoke;
            _method = method;
        }

        public override void Weave()
        {
            _original = DuplicateOriginalMethod(_method);
            
            CreateMethodBindingClass();
            
            Parent.CreateAspectCacheField();

            // Clear the target method body as it needs entirely new code
            MethodDefinition method = _method;

            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            var il = new ILProcessorEx(method.Body);

            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(method, il.Count, out argumentsVariable);

            WriteCopyArgumentsToContainer(method, il.Count, argumentsVariable, true);

            WriteAspectInit(method, il.Count);

            WriteBindingInit(method, il.Count);
            
            WriteMiaInit(method, il.Count, argumentsVariable);

            if (Aspect.Features.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, null, il.Count, _miaVar, null);
            
            WriteCallAdvice(method, il.Count, (MethodReference) _invokeAdvice.Source, _miaVar);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(method, il.Count, argumentsVariable, false, true);

            if (_returnValueField != null)
            {
                il.Emit(OpCodes.Ldloc, _miaVar);
                il.Emit(OpCodes.Ldfld, _returnValueField);
            }
            il.Emit(OpCodes.Ret);

            // Fix labels and optimize

            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        /// <summary>
        /// Writes the MethodInterceptionArgs initialization.
        /// </summary>
        private void WriteMiaInit(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable)
        {
            TypeReference miaType;
            MethodReference constructor;
            
            if (method.IsReturnVoid())
            {
                TypeDefinition miaTypeDef = Context.Spinner.BoundMethodInterceptionArgs;
                miaType = Context.SafeImport(miaTypeDef);

                MethodDefinition constructorDef = Context.Spinner.BoundMethodInterceptionArgs_ctor;
                constructor = Context.SafeImport(constructorDef);
            }
            else
            {
                TypeDefinition miaTypeDef = Context.Spinner.BoundMethodInterceptionArgsT1;
                GenericInstanceType genericMiaType = Context.SafeImport(miaTypeDef).MakeGenericInstanceType(method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = Context.Spinner.BoundMethodInterceptionArgsT1_ctor;
                constructor = Context.SafeImport(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = Context.Spinner.BoundMethodInterceptionArgsT1_TypedReturnValue;
                _returnValueField = Context.SafeImport(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            _miaVar = method.Body.AddVariableDefinition(miaType);
            var il = new ILProcessorEx();

            if (method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType)
                {
                    il.Emit(OpCodes.Ldobj, method.DeclaringType);
                    il.Emit(OpCodes.Box, method.DeclaringType);
                }
            }

            il.EmitLoadOrNull(argumentsVariable, null);
            
            il.Emit(OpCodes.Ldsfld, BindingInstanceField);

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, _miaVar);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        private void CreateMethodBindingClass()
        {
            ModuleDefinition module = _method.Module;

            TypeReference baseType;

            if (_method.IsReturnVoid())
            {
                baseType = Context.SafeImport(Context.Spinner.MethodBinding);
            }
            else
            {
                baseType = Context.SafeImport(Context.Spinner.MethodBindingT1).MakeGenericInstanceType(_method.ReturnType);
            }

            string name = NameGenerator.MakeMethodBindingName(_method.Name, Aspect.Index);

            CreateBindingClass(baseType, name);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition(InvokeMethodName, invokeAttrs, _method.ReturnType);

            BindingClass.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = Context.SafeImport(Context.Spinner.Arguments);

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            var il = new ILProcessorEx(invokeMethod.Body);

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(_method, out argumentContainerType, out argumentContainerFields);

            // Case the arguments container from its base type to the generic instance type
            VariableDefinition argsContainer = null;
            if (_method.Parameters.Count != 0)
            {
                argsContainer = invokeMethod.Body.AddVariableDefinition(argumentContainerType);

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Castclass, argumentContainerType);
                il.Emit(OpCodes.Stloc, argsContainer);
            }

            // Load the instance for the method call
            if (!_method.IsStatic)
            {
                // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldind_Ref);
                il.Emit(_method.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, _method.DeclaringType);
            }

            // Load arguments or addresses directly from the arguments container
            for (int i = 0; i < _method.Parameters.Count; i++)
            {
                bool byRef = _method.Parameters[i].ParameterType.IsByReference;

                il.Emit(OpCodes.Ldloc, argsContainer);
                il.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
            }

            if (_method.IsStatic || _method.DeclaringType.IsValueType)
                il.Emit(OpCodes.Call, _original);
            else
                il.Emit(OpCodes.Callvirt, _original);

            il.Emit(OpCodes.Ret);
        }
    }
}
