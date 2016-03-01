using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;
using Spinner.Fody.Utilities;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Base class for aspect weavers
    /// </summary>
    internal abstract class AspectWeaver
    {
        protected const string BindingInstanceFieldName = "Instance";
        protected const string StateMachineThisFieldName = "<>4__this";
        private const string MulticastAttributePropertyPrefix = "Attribute";

        // ReSharper disable InconsistentNaming
        protected readonly ModuleWeavingContext _mwc;
        protected readonly TypeDefinition _aspectType;
        protected readonly int _aspectIndex;
        protected readonly IMemberDefinition _aspectTarget;
        protected readonly Features _aspectFeatures;
        protected readonly MulticastInstance _mi;

        protected FieldDefinition _aspectField;
        protected TypeDefinition _bindingClass;
        protected FieldDefinition _bindingInstanceField;
        // ReSharper restore InconsistentNaming

        protected AspectWeaver(
            ModuleWeavingContext mwc,
            MulticastInstance mi,
            int aspectIndex,
            IMemberDefinition aspectTarget)
        {
            _mwc = mwc;
            _mi = mi;
            _aspectType = mi.AttributeType;
            _aspectIndex = aspectIndex;
            _aspectTarget = aspectTarget;
            _aspectFeatures = GetFeatures(_aspectType);
        }

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

        protected abstract void Weave();

        protected MethodDefinition DuplicateOriginalMethod(
            MethodDefinition method)
        {
            const MethodAttributes preservedAttributes =
                MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PInvokeImpl |
                MethodAttributes.UnmanagedExport | MethodAttributes.HasSecurity | MethodAttributes.RequireSecObject;

            // Duplicate the target method under a new name: <Name>z__OriginalMethod

            string originalName = NameGenerator.MakeOriginalMethodName(method.Name, _aspectIndex);

            MethodAttributes originalAttributes = method.Attributes & preservedAttributes |
                                                  MethodAttributes.Private;

            var original = new MethodDefinition(originalName, originalAttributes, method.ReturnType);

            AddCompilerGeneratedAttribute(original);

            original.Parameters.AddRange(method.Parameters);
            original.GenericParameters.AddRange(method.GenericParameters.Select(p => p.Clone(original)));
            original.ImplAttributes = method.ImplAttributes;
            original.SemanticsAttributes = method.SemanticsAttributes;

            if (method.IsPInvokeImpl)
            {
                original.PInvokeInfo = method.PInvokeInfo;
                method.PInvokeInfo = null;
                method.IsPreserveSig = false;
                method.IsPInvokeImpl = false;
            }
            else
            {
                original.Body.InitLocals = method.Body.InitLocals;
                original.Body.Instructions.AddRange(method.Body.Instructions);
                original.Body.Variables.AddRange(method.Body.Variables);
                original.Body.ExceptionHandlers.AddRange(method.Body.ExceptionHandlers);
            }

            method.DeclaringType.Methods.Add(original);

            return original;
        }

        /// <summary>
        /// Write code to initialize the argument container instance. Works for property getters and setters too.
        /// </summary>
        protected void WriteArgumentContainerInit(
            MethodDefinition method,
            int offset,
            out VariableDefinition argumentsVariable)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);

            if (effectiveParameterCount == 0)
            {
                argumentsVariable = null;
                return;
            }
            
            GenericInstanceType argumentsType;
            FieldReference[] argumentFields;
            GetArgumentContainerInfo(method, out argumentsType, out argumentFields);

            MethodDefinition constructorDef = _mwc.Spinner.ArgumentsT_ctor[effectiveParameterCount];
            MethodReference constructor = _mwc.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);
            
            argumentsVariable = method.Body.AddVariableDefinition("arguments", argumentsType);

            var il = new ILProcessorEx();
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, argumentsVariable);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        protected void WriteSmArgumentContainerInit(
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            out FieldDefinition arguments)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);

            if (effectiveParameterCount == 0)
            {
                arguments = null;
                return;
            }

            GenericInstanceType argumentsType;
            FieldReference[] argumentFields;
            GetArgumentContainerInfo(method, out argumentsType, out argumentFields);

            MethodDefinition constructorDef = _mwc.Spinner.ArgumentsT_ctor[effectiveParameterCount];
            MethodReference constructor = _mwc.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);

            string fieldName = NameGenerator.MakeAdviceArgsFieldName(_aspectIndex);
            arguments = new FieldDefinition(fieldName, FieldAttributes.Private, argumentsType);

            stateMachine.DeclaringType.Fields.Add(arguments);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldarg_0),
                Ins.Create(OpCodes.Newobj, constructor),
                Ins.Create(OpCodes.Stfld, arguments)
            };

            stateMachine.Body.InsertInstructions(offset, true, insc);
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

                baseParameterTypes[i] = _mwc.SafeImport(pt);
            }

            TypeDefinition typeDef = _mwc.Spinner.ArgumentsT[effectiveParameterCount];
            type = _mwc.SafeImport(typeDef).MakeGenericInstanceType(baseParameterTypes);

            fields = new FieldReference[effectiveParameterCount];

            for (int i = 0; i < effectiveParameterCount; i++)
            {
                FieldDefinition fieldDef = _mwc.Spinner.ArgumentsT_Item[effectiveParameterCount][i];
                FieldReference field = _mwc.SafeImport(fieldDef).WithGenericDeclaringType(type);

                fields[i] = field;
            }
        }

        /// <summary>
        /// Copies arguments from the method to the generic arguments container.
        /// </summary>
        protected void WriteCopyArgumentsToContainer(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            bool excludeOut)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(method, out argumentContainerType, out argumentContainerFields);

            var il = new ILProcessorEx();

            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                if (method.Parameters[i].IsOut && excludeOut)
                    continue;

                TypeReference parameterType = method.Parameters[i].ParameterType;

                il.Emit(OpCodes.Ldloc, argumentsVariable);
                il.Emit(OpCodes.Ldarg, method.Parameters[i]);
                if (parameterType.IsByReference)
                {
                    if (parameterType.GetElementType().IsValueType)
                        il.Emit(OpCodes.Ldobj, parameterType.GetElementType());
                    else
                        il.Emit(OpCodes.Ldind_Ref);
                }

                il.Emit(OpCodes.Stfld, argumentContainerFields[i]);
            }

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        protected void WriteSmCopyArgumentsToContainer(
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            FieldDefinition argumentContainerField,
            bool excludeOut)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);
            if (effectiveParameterCount == 0)
                return;

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(method, out argumentContainerType, out argumentContainerFields);

            var il = new ILProcessorEx();
            
            for (int i = 0; i < effectiveParameterCount; i++)
            {
                if (method.Parameters[i].IsOut && excludeOut)
                    continue;

                ParameterDefinition p = method.Parameters[i];

                Func<FieldDefinition, bool> isField = f => f.Name == p.Name && f.FieldType.IsSame(p.ParameterType);

                FieldReference smArgumentField = stateMachine.DeclaringType.Fields.FirstOrDefault(isField);

                // Release builds will optimize out unused fields
                if (smArgumentField == null)
                    continue;

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, argumentContainerField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, smArgumentField);
                il.Emit(OpCodes.Stfld, argumentContainerFields[i]);
            }

            if (il.Instructions.Count != 0)
                stateMachine.Body.InsertInstructions(offset, true, il.Instructions);
        }

        /// <summary>
        /// Copies arguments from the generic arguments container to the method.
        /// </summary>
        protected void WriteCopyArgumentsFromContainer(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            bool includeNormal,
            bool includeRef)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(method, out argumentContainerType, out argumentContainerFields);

            var il = new ILProcessorEx();
            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                TypeReference parameterType = method.Parameters[i].ParameterType;

                if (parameterType.IsByReference)
                {
                    if (!includeRef)
                        continue;
                    
                    il.Emit(OpCodes.Ldarg, method.Parameters[i]);
                    il.Emit(OpCodes.Ldloc, argumentsVariable);
                    il.Emit(OpCodes.Ldfld, argumentContainerFields[i]);
                    if (parameterType.GetElementType().IsValueType)
                        il.Emit(OpCodes.Stobj, parameterType.GetElementType());
                    else
                        il.Emit(OpCodes.Stind_Ref);
                }
                else
                {
                    if (!includeNormal)
                        continue;

                    il.Emit(OpCodes.Ldloc, argumentsVariable);
                    il.Emit(OpCodes.Ldfld, argumentContainerFields[i]);
                    il.Emit(OpCodes.Starg, method.Parameters[i]);
                }
            }

            if (il.Instructions.Count != 0)
                method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        /// <summary>
        /// Copies arguments from the generic arguments container to the method.
        /// </summary>
        protected void WriteSmCopyArgumentsFromContainer(
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            FieldReference argumentContainerField,
            bool includeNormal,
            bool includeRef)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(method, out argumentContainerType, out argumentContainerFields);

            var il = new ILProcessorEx();
            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                ParameterDefinition p = method.Parameters[i];

                if (p.ParameterType.IsByReference)
                {
                    if (!includeRef)
                        continue;
                }
                else
                {
                    if (!includeNormal)
                        continue;
                }

                Func<FieldDefinition, bool> isField = f => f.Name == p.Name && f.FieldType.IsSame(p.ParameterType);

                FieldReference smArgumentField = stateMachine.DeclaringType.Fields.FirstOrDefault(isField);

                // Release builds will optimize out unused fields
                if (smArgumentField == null)
                    continue;

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldfld, argumentContainerField);
                il.Emit(OpCodes.Ldfld, argumentContainerFields[i]);
                il.Emit(OpCodes.Stfld, smArgumentField);
            }

            if (il.Instructions.Count != 0)
                method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        ///// <summary>
        ///// Initializes out arugments to their default values.
        ///// </summary>
        //protected static void WriteOutArgumentInit(MethodDefinition method, ILProcessor il)
        //{
        //    for (int i = 0; i < GetEffectiveParameterCount(method); i++)
        //    {
        //        if (!method.Parameters[i].IsOut)
        //            continue;

        //        TypeReference parameterType = method.Parameters[i].ParameterType;
        //        int argumentIndex = method.IsStatic ? i : i + 1;

        //        if (parameterType.IsByReference)
        //        {
        //            il.Emit(OpCodes.Ldarg, argumentIndex);
        //            if (parameterType.GetElementType().IsValueType)
        //            {
        //                il.Emit(OpCodes.Initobj, parameterType.GetElementType());
        //            }
        //            else
        //            {
        //                il.Emit(OpCodes.Ldnull);
        //                il.Emit(OpCodes.Stind_Ref);
        //            }
        //        }
        //        else
        //        {
        //            if (parameterType.IsValueType)
        //            {
        //                il.Emit(OpCodes.Ldarga, argumentIndex);
        //                il.Emit(OpCodes.Initobj, parameterType);
        //            }
        //            else
        //            {
        //                il.Emit(OpCodes.Ldnull);
        //                il.Emit(OpCodes.Starg, argumentIndex);
        //            }

        //        }
        //    }
        //}

        /// <summary>
        /// Write aspect initialization, works for both normal methods and state machines.
        /// </summary>
        protected void WriteAspectInit(
            MethodDefinition method,
            int offset)
        {
            // Figure out if the attribute has any arguments that need to be initialized
            CustomAttribute attr = _mi.Attribute;

            int argCount = attr.HasConstructorArguments ? attr.ConstructorArguments.Count : 0;
            int propCount = attr.HasProperties ? attr.Properties.Count : 0;
            int fieldCount = attr.HasFields ? attr.Fields.Count : 0;

            Ins jtNotNull = Ins.Create(OpCodes.Nop);

            var il = new ILProcessorEx();
            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.Emit(OpCodes.Brtrue, jtNotNull);

            MethodReference ctor;
            if (argCount == 0)
            {
                // NOTE: Aspect type can't be generic since its declared by an attribute
                ctor = _mwc.SafeImport(_aspectType.GetConstructor(0));
            }
            else
            {
                List<TypeReference> argTypes = attr.ConstructorArguments.Select(c => c.Type).ToList();
                
                ctor = _mwc.SafeImport(_aspectType.GetConstructor(argTypes));

                for (int i = 0; i < attr.ConstructorArguments.Count; i++)
                    EmitAttributeArgument(il, attr.ConstructorArguments[i]);
            }

            il.Emit(OpCodes.Newobj, ctor);

            if (fieldCount != 0)
            {
                for (int i = 0; i < attr.Fields.Count; i++)
                {
                    CustomAttributeNamedArgument na = attr.Fields[i];

                    FieldReference field = _mwc.SafeImport(_aspectType.GetField(na.Name, true));

                    il.Emit(OpCodes.Dup);
                    EmitAttributeArgument(il, na.Argument);
                    il.Emit(OpCodes.Stfld, field);
                }
            }

            if (propCount != 0)
            {
                for (int i = 0; i < attr.Properties.Count; i++)
                {
                    CustomAttributeNamedArgument na = attr.Properties[i];

                    // Skip attribute properties that are not needed at runtime
                    if (na.Name.StartsWith(MulticastAttributePropertyPrefix))
                        continue;

                    MethodReference setter = _mwc.SafeImport(_aspectType.GetProperty(na.Name, true).SetMethod);

                    il.Emit(OpCodes.Dup);
                    EmitAttributeArgument(il, na.Argument);
                    il.Emit(OpCodes.Callvirt, setter);
                }
            }

            il.Emit(OpCodes.Stsfld, _aspectField);
            il.Append(jtNotNull);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        protected void EmitAttributeArgument(ILProcessorEx il, CustomAttributeArgument arg)
        {
            EmitLiteral(il, arg.Type, arg.Value);
        }

        protected void EmitLiteral(ILProcessorEx il, TypeReference type, object value)
        {
            if (value == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (value is bool)
            {
                il.Emit((bool) value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            }
            else if (value is char)
            {
                il.Emit(OpCodes.Ldc_I4, (char) value);
            }
            else if (value is byte)
            {
                il.Emit(OpCodes.Ldc_I4, (byte) value);
            }
            else if (value is sbyte)
            {
                il.Emit(OpCodes.Ldc_I4, (sbyte) value);
            }
            else if (value is short)
            {
                il.Emit(OpCodes.Ldc_I4, (short) value);
            }
            else if (value is ushort)
            {
                il.Emit(OpCodes.Ldc_I4, (ushort) value);
            }
            else if (value is int)
            {
                il.Emit(OpCodes.Ldc_I4, (int) value);
            }
            else if (value is uint)
            {
                il.Emit(OpCodes.Ldc_I4, (int) (uint) value);
            }
            else if (value is long)
            {
                il.Emit(OpCodes.Ldc_I8, (long) value);
            }
            else if (value is ulong)
            {
                il.Emit(OpCodes.Ldc_I8, (long) (ulong) value);
            }
            else if (value is float)
            {
                il.Emit(OpCodes.Ldc_R4, (float) value);
            }
            else if (value is double)
            {
                il.Emit(OpCodes.Ldc_R8, (double) value);
            }
            else if (value is string)
            {
                il.Emit(OpCodes.Ldstr, (string) value);
            }
            else if (value is TypeReference)
            {
                MethodReference getTypeFromHandle = _mwc.SafeImport(_mwc.Framework.Type_GetTypeFromHandle);
                il.Emit(OpCodes.Ldtoken, (TypeReference) value);
                il.Emit(OpCodes.Call, getTypeFromHandle);
            }
            else if (value is CustomAttributeArgument[])
            {
                var caaArray = (CustomAttributeArgument[]) value;
                TypeReference elementType = _mwc.SafeImport(type.GetElementType());
                TypeReference objectType = elementType.Module.TypeSystem.Object;
                OpCode stelemOpCode = GetStelemOpCode(elementType);

                il.Emit(OpCodes.Ldc_I4, caaArray.Length);
                il.Emit(OpCodes.Newarr, elementType);

                for (int i = 0; i < caaArray.Length; i++)
                {
                    CustomAttributeArgument caa = caaArray[i];

                    if (caa.Value is CustomAttributeArgument)
                        caa = (CustomAttributeArgument) caa.Value;

                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);

                    EmitLiteral(il, caa.Type, caa.Value);

                    if (elementType.IsSame(objectType) && caa.Type.IsValueType)
                        il.Emit(OpCodes.Box, caa.Type);

                    il.Emit(stelemOpCode);
                    //il.Emit(OpCodes.Stelem_Any, elementType);
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(value), value.GetType().Name);
            }
        }

        protected OpCode GetStelemOpCode(TypeReference type)
        {
            TypeSystem ts = type.Module.TypeSystem;

            if (type == ts.Boolean || type == ts.Byte || type == ts.SByte)
                return OpCodes.Stelem_I1;
            if (type == ts.Char || type == ts.Int16 || type == ts.UInt16)
                return OpCodes.Stelem_I2;
            if (type == ts.Int32 || type == ts.UInt32)
                return OpCodes.Stelem_I4;
            if (type == ts.Int64 || type == ts.UInt64)
                return OpCodes.Stelem_I8;
            if (type == ts.Single)
                return OpCodes.Stelem_R4;
            if (type == ts.Double)
                return OpCodes.Stelem_R8;
            return OpCodes.Stelem_Ref;
        }

        /// <summary>
        /// Writes a call to an aspect's advice with the advice args object if available.
        /// </summary>
        protected void WriteCallAdvice(
            MethodDefinition method,
            int offset,
            MethodReference baseReference,
            VariableDefinition iaVariableOpt)
        {
            MethodDefinition adviceDef = _aspectType.GetMethod(baseReference, true);
            MethodReference advice = _mwc.SafeImport(adviceDef);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, _aspectField),
                iaVariableOpt != null ? Ins.Create(OpCodes.Ldloc, iaVariableOpt) : Ins.Create(OpCodes.Ldnull),
                Ins.Create(OpCodes.Callvirt, advice)
            };

            method.Body.InsertInstructions(offset, true, insc);
        }

        /// <summary>
        /// 
        /// </summary>
        protected void WriteBindingInit(MethodDefinition method, int offset)
        {
            // Initialize the binding instance
            FieldReference instanceField = _bindingClass.Fields.Single(f => f.Name == BindingInstanceFieldName);
            MethodReference constructor = _bindingClass.Methods.Single(f => f.IsConstructor && !f.IsStatic);

            Ins notNullLabel = Ins.Create(OpCodes.Nop);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, instanceField),
                Ins.Create(OpCodes.Brtrue, notNullLabel),
                Ins.Create(OpCodes.Newobj, constructor),
                Ins.Create(OpCodes.Stsfld, instanceField),
                notNullLabel
            };

            method.Body.InsertInstructions(offset, true, insc);
        }

        protected void WriteSetMethodInfo(
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            VariableDefinition maVarOpt,
            FieldReference maFieldOpt)
        {
            MethodDefinition target = stateMachineOpt ?? method;
            MethodReference getMethodFromHandle = _mwc.SafeImport(_mwc.Framework.MethodBase_GetMethodFromHandle);
            MethodReference setMethod = _mwc.SafeImport(_mwc.Spinner.MethodArgs_Method.SetMethod);
            TypeReference methodInfo = _mwc.SafeImport(_mwc.Framework.MethodInfo);

            var il = new ILProcessorEx();

            il.EmitLoadOrNull(maVarOpt, maFieldOpt);

            il.Emit(OpCodes.Ldtoken, method);
            il.Emit(OpCodes.Call, getMethodFromHandle);
            il.Emit(OpCodes.Castclass, methodInfo);

            il.Emit(OpCodes.Callvirt, setMethod);

            target.Body.InsertInstructions(offset, true, il.Instructions);
        }

        protected void CreateAspectCacheField()
        {
            string name = NameGenerator.MakeAspectFieldName(_aspectTarget.Name, _aspectIndex);

            var fattrs = FieldAttributes.Private | FieldAttributes.Static;
            
            var aspectFieldDef = new FieldDefinition(name, fattrs, _mwc.SafeImport(_aspectType));
            AddCompilerGeneratedAttribute(aspectFieldDef);
            _aspectTarget.DeclaringType.Fields.Add(aspectFieldDef);

            _aspectField = aspectFieldDef;
        }

        protected MethodDefinition MakeDefaultConstructor(TypeDefinition type)
        {
            TypeDefinition baseTypeDef = type.BaseType.Resolve();
            MethodDefinition baseCtorDef = baseTypeDef.Methods.Single(m => !m.IsStatic && m.Parameters.Count == 0);

            MethodReference baseCtor = type.BaseType.IsGenericInstance
                ? _mwc.SafeImport(baseCtorDef).WithGenericDeclaringType((GenericInstanceType) type.BaseType)
                : _mwc.SafeImport(baseCtorDef);

            var attrs = MethodAttributes.Public |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName;

            var method = new MethodDefinition(".ctor", attrs, _mwc.Module.TypeSystem.Void);

            var il = new ILProcessorEx(method.Body.Instructions);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, baseCtor);
            il.Emit(OpCodes.Ret);

            return method;
        }

        protected void AddCompilerGeneratedAttribute(ICustomAttributeProvider definition)
        {
            MethodReference ctor = _mwc.SafeImport(_mwc.Framework.CompilerGeneratedAttribute_ctor);
            
            definition.CustomAttributes.Add(new CustomAttribute(ctor));
        }

        /// <summary>
        /// Get the features declared for a type. AnalzyedFeaturesAttribute takes precedence over FeaturesAttribute.
        /// </summary>
        protected Features GetFeatures(TypeDefinition aspectType)
        {
            TypeDefinition attrType = _mwc.Spinner.FeaturesAttribute;
            TypeDefinition analyzedAttrType = _mwc.Spinner.AnalyzedFeaturesAttribute;

            Features? features = null;

            TypeDefinition current = aspectType;
            while (current != null)
            {
                if (current.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in current.CustomAttributes)
                    {
                        TypeReference atype = a.AttributeType;

                        if (atype.IsSame(analyzedAttrType))
                        {
                            return (Features) (uint) a.ConstructorArguments.First().Value;
                        }

                        if (atype.IsSame(attrType))
                        {
                            features = (Features) (uint) a.ConstructorArguments.First().Value;
                            // Continue in case AnalyzedFeaturesAttribute is found.
                        }
                    }
                }

                // No need to examine base type if found here
                if (features.HasValue)
                    return features.Value;

                current = current.BaseType?.Resolve();
            }

            return Features.None;
        }

        /// <summary>
        /// Get the features declared for an advice. AnalzyedFeaturesAttribute takes precedence over FeaturesAttribute.
        /// </summary>
        protected Features GetFeatures(MethodDefinition advice)
        {
            TypeDefinition attrType = _mwc.Spinner.FeaturesAttribute;
            TypeDefinition analyzedAttrType = _mwc.Spinner.AnalyzedFeaturesAttribute;
            
            Features? features = null;

            MethodDefinition current = advice;
            while (current != null)
            {
                if (current.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in current.CustomAttributes)
                    {
                        TypeReference atype = a.AttributeType;

                        if (atype.IsSame(analyzedAttrType))
                        {
                            return (Features) (uint) a.ConstructorArguments.First().Value;
                        }

                        if (atype.IsSame(attrType))
                        {
                            features = (Features) (uint) a.ConstructorArguments.First().Value;
                            // Continue in case AnalyzedFeaturesAttribute is found on same type.
                        }
                    }
                }

                if (features.HasValue)
                    return features.Value;

                current = current.HasOverrides ? current.Overrides.Single().Resolve() : null;
            }

            return Features.None;
        }

        /// <summary>
        /// Create a nested binding class with an empty default constructor and a static instance field.
        /// </summary>
        protected void CreateBindingClass(
            TypeReference baseType,
            string name)
        {
            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            _bindingClass = new TypeDefinition(null, name, tattrs, baseType)
            {
                DeclaringType = _aspectTarget.DeclaringType
            };

            AddCompilerGeneratedAttribute(_bindingClass);

            _aspectTarget.DeclaringType.NestedTypes.Add(_bindingClass);

            MethodDefinition constructorDef = MakeDefaultConstructor(_bindingClass);

            _bindingClass.Methods.Add(constructorDef);

            var instanceAttrs = FieldAttributes.Public | FieldAttributes.Static;
            _bindingInstanceField = new FieldDefinition(BindingInstanceFieldName, instanceAttrs, _bindingClass);

            _bindingClass.Fields.Add(_bindingInstanceField);
        }
    }

    internal static class FeaturesExtensions
    {
        internal static bool Has(this Features self, Features features)
        {
            return (self & features) != 0;
        }
    }
}