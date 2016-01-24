using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
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

        // ReSharper disable InconsistentNaming
        protected readonly ModuleWeavingContext _mwc;
        protected readonly TypeDefinition _aspectType;
        protected readonly int _aspectIndex;
        protected readonly IMemberDefinition _aspectTarget;
        protected readonly Features _aspectFeatures;

        protected FieldDefinition _aspectField;
        protected TypeDefinition _bindingClass;
        protected FieldDefinition _bindingInstanceField;
        // ReSharper restore InconsistentNaming

        protected AspectWeaver(
            ModuleWeavingContext mwc,
            TypeDefinition aspectType,
            int aspectIndex,
            IMemberDefinition aspectTarget)
        {
            _mwc = mwc;
            _aspectType = aspectType;
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

            var insc = method.Body.Instructions;
            insc.Insert(offset, Ins.Create(OpCodes.Newobj, constructor));
            insc.Insert(offset + 1, Ins.Create(OpCodes.Stloc, argumentsVariable));
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

            var insc = new Collection<Ins>();

            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                if (method.Parameters[i].IsOut && excludeOut)
                    continue;

                TypeReference parameterType = method.Parameters[i].ParameterType;

                insc.Add(Ins.Create(OpCodes.Ldloc, argumentsVariable));
                insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters[i]));
                if (parameterType.IsByReference)
                {
                    insc.Add(parameterType.GetElementType().IsValueType
                        ? Ins.Create(OpCodes.Ldobj, parameterType.GetElementType())
                        : Ins.Create(OpCodes.Ldind_Ref));
                }

                insc.Add(Ins.Create(OpCodes.Stfld, argumentContainerFields[i]));
            }

            method.Body.InsertInstructions(offset, true, insc);
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

            var insc = new Collection<Ins>();
            
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

                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, argumentContainerField));
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, smArgumentField));
                insc.Add(Ins.Create(OpCodes.Stfld, argumentContainerFields[i]));
            }

            stateMachine.Body.InsertInstructions(offset, true, insc);
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

            var insc = new Collection<Ins>();
            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                TypeReference parameterType = method.Parameters[i].ParameterType;

                if (parameterType.IsByReference)
                {
                    if (!includeRef)
                        continue;
                    
                    insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters[i]));
                    insc.Add(Ins.Create(OpCodes.Ldloc, argumentsVariable));
                    insc.Add(Ins.Create(OpCodes.Ldfld, argumentContainerFields[i]));
                    insc.Add(parameterType.GetElementType().IsValueType
                        ? Ins.Create(OpCodes.Stobj, parameterType.GetElementType())
                        : Ins.Create(OpCodes.Stind_Ref));
                }
                else
                {
                    if (!includeNormal)
                        continue;

                    insc.Add(Ins.Create(OpCodes.Ldloc, argumentsVariable));
                    insc.Add(Ins.Create(OpCodes.Ldfld, argumentContainerFields[i]));
                    insc.Add(Ins.Create(OpCodes.Starg, method.Parameters[i]));
                }
            }

            if (insc.Count != 0)
                method.Body.InsertInstructions(offset, true, insc);
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

            var insc = new Collection<Ins>();
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

                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Dup));
                insc.Add(Ins.Create(OpCodes.Ldfld, argumentContainerField));
                insc.Add(Ins.Create(OpCodes.Ldfld, argumentContainerFields[i]));
                insc.Add(Ins.Create(OpCodes.Stfld, smArgumentField));
            }

            method.Body.InsertInstructions(offset, true, insc);
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
            // NOTE: Aspect type can't be generic since its declared by an attribute
            MethodDefinition ctorDef = _aspectType.GetConstructors().Single(m => !m.IsStatic && m.Parameters.Count == 0);
            MethodReference ctor = _mwc.SafeImport(ctorDef);

            Ins jtNotNull = Ins.Create(OpCodes.Nop);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, _aspectField),
                Ins.Create(OpCodes.Brtrue, jtNotNull),
                Ins.Create(OpCodes.Newobj, ctor),
                Ins.Create(OpCodes.Stsfld, _aspectField),
                jtNotNull
            };

            method.Body.InsertInstructions(offset, true, insc);
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

            var insc = new Collection<Ins>();

            if (maFieldOpt != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, maFieldOpt));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, maVarOpt));
            }

            insc.Add(Ins.Create(OpCodes.Ldtoken, method));
            insc.Add(Ins.Create(OpCodes.Call, getMethodFromHandle));
            insc.Add(Ins.Create(OpCodes.Castclass, methodInfo));

            insc.Add(Ins.Create(OpCodes.Callvirt, setMethod));

            target.Body.InsertInstructions(offset, true, insc);
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

            Collection<Ins> i = method.Body.Instructions;
            i.Add(Ins.Create(OpCodes.Ldarg_0));
            i.Add(Ins.Create(OpCodes.Call, baseCtor));
            i.Add(Ins.Create(OpCodes.Ret));

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