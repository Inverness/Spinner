using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Ramp.Aspects.Fody.Utilities;

namespace Ramp.Aspects.Fody.Weavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal sealed class MethodInterceptionAspectWeaver : AspectWeaver
    {
        internal MethodInterceptionAspectWeaver(
            ModuleDefinition aspectLibraryModule,
            CacheClassBuilder ccb,
            MethodDefinition method,
            TypeDefinition aspectType)
            : base(aspectLibraryModule, ccb, method, aspectType)
        {
        }

        internal void Weave()
        {
            const MethodAttributes preservedAttributes =
                MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PInvokeImpl |
                MethodAttributes.UnmanagedExport | MethodAttributes.HasSecurity | MethodAttributes.RequireSecObject;

            // Duplicate the target method under a new name: <Name>z__Original

            string originalName = $"<{Method.Name}>z__OriginalMethod";

            MethodAttributes originalAttributes = Method.Attributes & preservedAttributes |
                                                  MethodAttributes.Private;

            var original = new MethodDefinition(originalName, originalAttributes, Method.ReturnType);
            
            original.Parameters.AddRange(Method.Parameters);
            original.GenericParameters.AddRange(Method.GenericParameters.Select(p => p.Clone(original)));
            original.ImplAttributes = Method.ImplAttributes;
            original.SemanticsAttributes = Method.SemanticsAttributes;

            if (Method.IsPInvokeImpl)
            {
                original.PInvokeInfo = Method.PInvokeInfo;
                Method.PInvokeInfo = null;
                Method.IsPreserveSig = false;
                Method.IsPInvokeImpl = false;
            }
            else
            {
                original.Body.InitLocals = Method.Body.InitLocals;
                original.Body.Instructions.AddRange(Method.Body.Instructions);
                original.Body.Variables.AddRange(Method.Body.Variables);
                original.Body.ExceptionHandlers.AddRange(Method.Body.ExceptionHandlers);
            }

            Method.DeclaringType.Methods.Add(original);

            // Clear the target method body as it needs entirely new code

            Method.Body.InitLocals = true;
            Method.Body.Instructions.Clear();
            Method.Body.Variables.Clear();
            Method.Body.ExceptionHandlers.Clear();

            ILProcessor il = Method.Body.GetILProcessor();
            var lp = new LabelProcessor(il);

            // Begin writing the new body

            FieldReference aspectField = WriteAspectInit(il, lp);

            FieldReference[] argumentFields;
            VariableDefinition argumentsVariable = WriteArgumentsInit(il, out argumentFields);

            FieldReference bindingField = WriteBindingInit(il, lp, original, argumentFields);

            FieldReference returnValueField;
            VariableDefinition miaVariable = WriteMiaInit(il, argumentsVariable, bindingField, out returnValueField);

            WriteCallOnInvoke(il, aspectField, miaVariable);

            WriteReturn(il, miaVariable, returnValueField);

            // Fix labels and optimize

            lp.Finish();
            il.Body.OptimizeMacros();
        }

        /// <summary>
        /// Writes code to initialize the MethodInterceptionArgs
        /// </summary>
        /// <param name="il">IL processor</param>
        /// <param name="argumentsVariable">The arguments container</param>
        /// <param name="bindingField"></param>
        /// <returns></returns>
        private VariableDefinition WriteMiaInit(ILProcessor il, VariableDefinition argumentsVariable, FieldReference bindingField, out FieldReference returnValueField)
        {
            ModuleDefinition module = Method.Module;
            TypeSystem typeSystem = module.TypeSystem;
            
            TypeReference miaType;
            MethodReference constructor;
            
            if (ReturnsVoid)
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs");
                miaType = module.Import(miaTypeDef);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef);

                returnValueField = null;
            }
            else
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs`1");
                miaType = module.Import(miaTypeDef).MakeGenericInstanceType(Method.ReturnType);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef.MakeGenericDeclaringType(Method.ReturnType));

                FieldDefinition returnValueFieldDef = miaTypeDef.Fields.Single(f => f.Name == "TypedReturnValue");
                returnValueField = module.Import(returnValueFieldDef.MakeGenericDeclaringType(Method.ReturnType));
            }

            VariableDefinition miaVariable = new VariableDefinition("mia", miaType);
            il.Body.Variables.Add(miaVariable);

            if (Method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (Method.DeclaringType.IsValueType)
                    il.Emit(OpCodes.Box, typeSystem.Object);
            }

            if (argumentsVariable == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, argumentsVariable);
            }

            // the method binding
            if (bindingField == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, bindingField);
            }

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, miaVariable);

            return miaVariable;
        }

        private FieldReference WriteAspectInit(ILProcessor il, LabelProcessor lp)
        {
            var fattrs = FieldAttributes.Private |
                         FieldAttributes.Static;

            string fname = $"<{Method.Name}>z__CachedAspect" + Method.DeclaringType.Fields.Count;
            var aspectField = new FieldDefinition(fname, fattrs, Method.Module.Import(AspectType));
            Method.DeclaringType.Fields.Add(aspectField);

            MethodDefinition constructorDef = AspectType.GetConstructors().Single(m => m.HasThis && m.Parameters.Count == 0);
            MethodReference constructor = Method.Module.Import(constructorDef);

            Label brTarget = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Brtrue, brTarget.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, aspectField);
            lp.MarkLabel(brTarget);

            return aspectField;
        }

        private void WriteCallOnInvoke(ILProcessor il, FieldReference aspectField, VariableDefinition miaVariable)
        {
            MethodDefinition onInvokeMethodDef = AspectType.Methods.Single(m => m.Name == "OnInvoke");
            MethodReference onInvokeMethod = Method.Module.Import(onInvokeMethodDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, miaVariable);
            il.Emit(OpCodes.Callvirt, onInvokeMethod);
        }

        private void WriteReturn(ILProcessor il, VariableDefinition miaVariable, FieldReference returnValueField)
        {
            il.Emit(OpCodes.Ldloc, miaVariable);
            il.Emit(OpCodes.Ldfld, returnValueField);
            il.Emit(OpCodes.Ret);
        }

        private FieldReference WriteBindingInit(ILProcessor il, LabelProcessor lp, MethodReference original, FieldReference[] argumentFields)
        {
            ModuleDefinition module = Method.Module;

            TypeReference baseType;

            if (ReturnsVoid)
            {
                baseType = module.Import(AspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding"));
            }
            else
            {
                baseType = module.Import(AspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding`1"))
                                 .MakeGenericInstanceType(Method.ReturnType);
            }

            string className = $"<{Method.Name}>z__MethodBinding";

            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            var bindingTypeDef = new TypeDefinition(null, className, tattrs, baseType)
            {
                DeclaringType = Method.DeclaringType
            };

            Method.DeclaringType.NestedTypes.Add(bindingTypeDef);

            MethodDefinition constructor = MakeDefaultConstructor(bindingTypeDef);

            bindingTypeDef.Methods.Add(constructor);

            // Add the static instance field

            var instanceAttrs = FieldAttributes.Public | FieldAttributes.Static;
            var instanceField = new FieldDefinition("Instance", instanceAttrs, bindingTypeDef);

            bindingTypeDef.Fields.Add(instanceField);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition("Invoke", invokeAttrs, Method.ReturnType);
            
            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = module.Import(AspectLibraryModule.GetType("Ramp.Aspects.Arguments"));

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();
            var argumentVariables = new VariableDefinition[Method.Parameters.Count];
            VariableDefinition argsContainer = null;

            if (argumentVariables.Length != 0)
            {
                argsContainer = new VariableDefinition("castedArgs", argumentFields[0].DeclaringType);
                bil.Body.Variables.Add(argsContainer);
                bil.Body.InitLocals = true;

                bil.Emit(OpCodes.Ldarg_2);
                bil.Emit(OpCodes.Castclass, argumentFields[0].DeclaringType);
                bil.Emit(OpCodes.Stloc, argsContainer);
            }

            for (int i = 0; i < argumentVariables.Length; i++)
            {
                ParameterDefinition parameter = Method.Parameters[i];

                TypeReference realType = parameter.ParameterType.IsByReference
                    ? parameter.ParameterType.GetElementType()
                    : parameter.ParameterType;

                var argumentVariable = new VariableDefinition(parameter.Name, realType);
                bil.Body.Variables.Add(argumentVariable);

                argumentVariables[i] = argumentVariable;

                if (parameter.IsOut)
                    continue;

                bil.Emit(OpCodes.Ldloc, argsContainer);
                bil.Emit(OpCodes.Ldfld, argumentFields[i]);
                bil.Emit(OpCodes.Stloc, argumentVariable);
            }

            if (!Method.IsStatic)
            {
                var instanceVariable = new VariableDefinition("unboxedInstance", Method.DeclaringType);

                bil.Body.Variables.Add(instanceVariable);
                bil.Body.InitLocals = true;

                bil.Emit(OpCodes.Ldarg_1);
                bil.Emit(Method.DeclaringType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, Method.DeclaringType);
                bil.Emit(OpCodes.Stloc, instanceVariable);
                bil.Emit(Method.DeclaringType.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc);
            }

            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                OpCode opcode = Method.Parameters[i].ParameterType.IsByReference ? OpCodes.Ldloca : OpCodes.Ldloc;
                bil.Emit(opcode, argumentVariables[i]);
            }

            if (Method.IsStatic || Method.DeclaringType.IsValueType)
                bil.Emit(OpCodes.Call, original);
            else
                bil.Emit(OpCodes.Callvirt, original);
            bil.Emit(OpCodes.Ret);

            bindingTypeDef.Methods.Add(invokeMethod);

            // Initialize the binding instance

            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, instanceField);
            lp.MarkLabel(notNullLabel);

            return instanceField;
        }

        private static MethodDefinition MakeDefaultConstructor(TypeDefinition type)
        {
            TypeDefinition baseTypeDef = type.BaseType.Resolve();
            MethodDefinition baseCtorDef = baseTypeDef.Methods.Single(m => m.HasThis && m.Parameters.Count == 0);

            MethodReference baseCtor;
            if (type.BaseType.IsGenericInstance)
            {
                var baseType = (GenericInstanceType) type.BaseType;

                baseCtor = type.Module.Import(baseCtorDef.MakeGenericDeclaringType(baseType));
            }
            else
            {
                baseCtor = type.Module.Import(baseCtorDef);
            }

            var attrs = MethodAttributes.Public |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName;

            var method = new MethodDefinition(".ctor", attrs, type.Module.TypeSystem.Void);

            Collection<Instruction> i = method.Body.Instructions;
            i.Add(Instruction.Create(OpCodes.Ldarg_0));
            i.Add(Instruction.Create(OpCodes.Call, baseCtor));
            i.Add(Instruction.Create(OpCodes.Ret));

            return method;
        }
    }
}
