﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal sealed class MethodInterceptionAspectWeaver : AspectWeaver
    {
        private const string InvokeMethodName = "Invoke";

        private readonly MethodDefinition _method;
        private MethodDefinition _original;
        private FieldReference _returnValueField;
        private VariableDefinition _miaVar;

        public MethodInterceptionAspectWeaver(
            ModuleWeavingContext mwc,
            TypeDefinition aspectType,
            int aspectIndex,
            MethodDefinition aspectTarget)
            : base(mwc, aspectType, aspectIndex, aspectTarget)
        {
            _method = aspectTarget;
        }

        protected override void Weave()
        {
            _original = DuplicateOriginalMethod(_method);
            
            CreateMethodBindingClass();
            
            CreateAspectCacheField();

            // Clear the target method body as it needs entirely new code
            MethodDefinition method = _method;

            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            Collection<Ins> insc = method.Body.Instructions;

            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(method, insc.Count, out argumentsVariable);

            WriteCopyArgumentsToContainer(method, insc.Count, argumentsVariable, true);

            WriteAspectInit(method, insc.Count);

            WriteBindingInit(method, insc.Count);
            
            WriteMiaInit(method, insc.Count, argumentsVariable);

            if (_aspectFeatures.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, null, insc.Count, _miaVar, null);

            MethodReference adviceBase = _mwc.Spinner.IMethodInterceptionAspect_OnInvoke;
            WriteCallAdvice(method, insc.Count, adviceBase, _miaVar);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(method, insc.Count, argumentsVariable, false, true);

            if (_returnValueField != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, _miaVar));
                insc.Add(Ins.Create(OpCodes.Ldfld, _returnValueField));
            }
            insc.Add(Ins.Create(OpCodes.Ret));

            // Fix labels and optimize

            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        internal static void Weave(ModuleWeavingContext mwc, MethodDefinition method, TypeDefinition aspect, int index)
        {
            new MethodInterceptionAspectWeaver(mwc, aspect, index, method).Weave();
        }

        /// <summary>
        /// Writes the MethodInterceptionArgs initialization.
        /// </summary>
        private void WriteMiaInit(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable)
        {
            ModuleDefinition module = method.Module;
            
            TypeReference miaType;
            MethodReference constructor;
            
            if (method.ReturnType == module.TypeSystem.Void)
            {
                TypeDefinition miaTypeDef = _mwc.Spinner.BoundMethodInterceptionArgs;
                miaType = _mwc.SafeImport(miaTypeDef);

                MethodDefinition constructorDef = _mwc.Spinner.BoundMethodInterceptionArgs_ctor;
                constructor = _mwc.SafeImport(constructorDef);
            }
            else
            {
                TypeDefinition miaTypeDef = _mwc.Spinner.BoundMethodInterceptionArgsT1;
                GenericInstanceType genericMiaType = _mwc.SafeImport(miaTypeDef).MakeGenericInstanceType(method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = _mwc.Spinner.BoundMethodInterceptionArgsT1_ctor;
                constructor = _mwc.SafeImport(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = _mwc.Spinner.BoundMethodInterceptionArgsT1_TypedReturnValue;
                _returnValueField = _mwc.SafeImport(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            _miaVar = method.Body.AddVariableDefinition(miaType);
            var insc = new Collection<Ins>();

            if (method.IsStatic)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                if (method.DeclaringType.IsValueType)
                {
                    insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
                    insc.Add(Ins.Create(OpCodes.Box, method.DeclaringType));
                }
            }

            insc.Add(argumentsVariable == null
                ? Ins.Create(OpCodes.Ldnull)
                : Ins.Create(OpCodes.Ldloc, argumentsVariable));
            
            insc.Add(Ins.Create(OpCodes.Ldsfld, _bindingInstanceField));

            insc.Add(Ins.Create(OpCodes.Newobj, constructor));
            insc.Add(Ins.Create(OpCodes.Stloc, _miaVar));

            method.Body.InsertInstructions(offset, insc);
        }

        private void CreateMethodBindingClass()
        {
            ModuleDefinition module = _method.Module;

            TypeReference baseType;

            if (_method.ReturnType == module.TypeSystem.Void)
            {
                baseType = _mwc.SafeImport(_mwc.Spinner.MethodBinding);
            }
            else
            {
                baseType = _mwc.SafeImport(_mwc.Spinner.MethodBindingT1).MakeGenericInstanceType(_method.ReturnType);
            }

            string name = NameGenerator.MakeMethodBindingName(_method.Name, _aspectIndex);

            CreateBindingClass(baseType, name);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition(InvokeMethodName, invokeAttrs, _method.ReturnType);

            _bindingClass.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = _mwc.SafeImport(_mwc.Spinner.Arguments);

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(_method, out argumentContainerType, out argumentContainerFields);

            // Case the arguments container from its base type to the generic instance type
            VariableDefinition argsContainer = null;
            if (_method.Parameters.Count != 0)
            {
                argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                bil.Emit(OpCodes.Ldarg_2);
                bil.Emit(OpCodes.Castclass, argumentContainerType);
                bil.Emit(OpCodes.Stloc, argsContainer);
            }

            // Load the instance for the method call
            if (!_method.IsStatic)
            {
                // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                bil.Emit(OpCodes.Ldarg_1);
                bil.Emit(OpCodes.Ldind_Ref);
                bil.Emit(_method.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, _method.DeclaringType);
            }

            // Load arguments or addresses directly from the arguments container
            for (int i = 0; i < _method.Parameters.Count; i++)
            {
                bool byRef = _method.Parameters[i].ParameterType.IsByReference;

                bil.Emit(OpCodes.Ldloc, argsContainer);
                bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
            }

            if (_method.IsStatic || _method.DeclaringType.IsValueType)
                bil.Emit(OpCodes.Call, _original);
            else
                bil.Emit(OpCodes.Callvirt, _original);

            bil.Emit(OpCodes.Ret);
        }
    }
}
