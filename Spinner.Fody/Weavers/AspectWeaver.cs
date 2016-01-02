using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Base class for aspect weavers
    /// </summary>
    internal class AspectWeaver
    {
        protected const string BindingInstanceFieldName = "Instance";
        protected const string StateMachineThisFieldName = "<>4__this";

        /// <summary>
        /// Gets an effective parameter count by excluding the value parameter of a property setter.
        /// </summary>
        protected static int GetEffectiveParameterCount(MethodDefinition method)
        {
            int e = method.Parameters.Count;
            if (method.IsSetter)
                e--;
            return e;
        }

        protected static MethodDefinition DuplicateOriginalMethod(MethodDefinition method, int aspectIndex)
        {
            const MethodAttributes preservedAttributes =
                MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PInvokeImpl |
                MethodAttributes.UnmanagedExport | MethodAttributes.HasSecurity | MethodAttributes.RequireSecObject;

            // Duplicate the target method under a new name: <Name>z__OriginalMethod

            string originalName = $"<{ExtractOriginalName(method.Name)}>z__OriginalMethod" + aspectIndex;

            MethodAttributes originalAttributes = method.Attributes & preservedAttributes |
                                                  MethodAttributes.Private;

            var original = new MethodDefinition(originalName, originalAttributes, method.ReturnType);

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
        protected static void WriteArgumentContainerInit(
            ModuleWeavingContext mwc,
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
            GetArgumentContainerInfo(mwc, method, out argumentsType, out argumentFields);

            MethodDefinition constructorDef = mwc.Spinner.Arguments_ctor[effectiveParameterCount];
            MethodReference constructor = mwc.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);
            
            argumentsVariable = method.Body.AddVariableDefinition("arguments", argumentsType);

            var insc = method.Body.Instructions;
            insc.Insert(offset, Ins.Create(OpCodes.Newobj, constructor));
            insc.Insert(offset + 1, Ins.Create(OpCodes.Stloc, argumentsVariable));
        }

        protected static void WriteSmArgumentContainerInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            out VariableDefinition arguments)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);

            if (effectiveParameterCount == 0)
            {
                arguments = null;
                return;
            }

            GenericInstanceType argumentsType;
            FieldReference[] argumentFields;
            GetArgumentContainerInfo(mwc, method, out argumentsType, out argumentFields);

            MethodDefinition constructorDef = mwc.Spinner.Arguments_ctor[effectiveParameterCount];
            MethodReference constructor = mwc.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);

            arguments = stateMachine.Body.AddVariableDefinition(argumentsType);

            var insc = new[]
            {
                Ins.Create(OpCodes.Newobj, constructor),
                Ins.Create(OpCodes.Stloc, arguments)
            };

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        protected static void GetArgumentContainerInfo(
            ModuleWeavingContext mwc,
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

                baseParameterTypes[i] = pt;
            }

            TypeDefinition typeDef = mwc.Spinner.Arguments[effectiveParameterCount];
            type = mwc.SafeImport(typeDef).MakeGenericInstanceType(baseParameterTypes);

            fields = new FieldReference[effectiveParameterCount];

            for (int i = 0; i < effectiveParameterCount; i++)
            {
                FieldDefinition fieldDef = mwc.Spinner.Arguments_Item[effectiveParameterCount][i];
                FieldReference field = mwc.SafeImport(fieldDef).WithGenericDeclaringType(type);

                fields[i] = field;
            }
        }

        /// <summary>
        /// Copies arguments from the method to the generic arguments container.
        /// </summary>
        protected static void WriteCopyArgumentsToContainer(
            ModuleWeavingContext rc,
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            bool excludeOut)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(rc, method, out argumentContainerType, out argumentContainerFields);

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

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteSmCopyArgumentsToContainer(
            ModuleWeavingContext rc,
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            VariableDefinition arguments,
            bool excludeOut)
        {
            int effectiveParameterCount = GetEffectiveParameterCount(method);
            if (effectiveParameterCount == 0)
                return;

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(rc, method, out argumentContainerType, out argumentContainerFields);

            var insc = new Collection<Ins>();

            for (int i = 0; i < effectiveParameterCount; i++)
            {
                if (method.Parameters[i].IsOut && excludeOut)
                    continue;

                ParameterDefinition p = method.Parameters[i];

                FieldReference af =
                    stateMachine.DeclaringType.Fields.FirstOrDefault(f => f.Name == p.Name &&
                                                                          f.FieldType.IsSame(p.ParameterType));

                // Release builds will optimize out unused fields
                if (af == null)
                    continue;

                insc.Add(Ins.Create(OpCodes.Ldloc, arguments));
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, af));
                insc.Add(Ins.Create(OpCodes.Stfld, argumentContainerFields[i]));
            }

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// Copies arguments from the generic arguments container to the method.
        /// </summary>
        protected static void WriteCopyArgumentsFromContainer(
            ModuleWeavingContext rc,
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            bool includeNormal,
            bool includeRef)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(rc, method, out argumentContainerType, out argumentContainerFields);

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

            method.Body.InsertInstructions(offset, insc);
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
        protected static void WriteAspectInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            TypeDefinition aspectType,
            FieldReference aspectCacheField)
        {
            // NOTE: Aspect type can't be generic since its declared by an attribute
            MethodDefinition ctorDef = aspectType.GetConstructors().Single(m => !m.IsStatic && m.Parameters.Count == 0);
            MethodReference ctor = mwc.SafeImport(ctorDef);

            Ins jtNotNull = CreateNop();

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, aspectCacheField),
                Ins.Create(OpCodes.Brtrue, jtNotNull),
                Ins.Create(OpCodes.Newobj, ctor),
                Ins.Create(OpCodes.Stsfld, aspectCacheField),
                jtNotNull
            };

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteCallAdvice(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            string name,
            TypeDefinition aspectType,
            FieldReference aspectField,
            VariableDefinition iaVariable)
        {
            MethodDefinition adviceDef = aspectType.Methods.Single(m => m.Name == name);
            MethodReference advice = mwc.SafeImport(adviceDef);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, aspectField),
                Ins.Create(OpCodes.Ldloc, iaVariable),
                Ins.Create(OpCodes.Callvirt, advice)
            };

            method.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// 
        /// </summary>
        protected static void WriteBindingInit(MethodDefinition method, int offset, TypeDefinition bindingType)
        {
            // Initialize the binding instance
            FieldReference instanceField = bindingType.Fields.Single(f => f.Name == BindingInstanceFieldName);
            MethodReference constructor = bindingType.Methods.Single(f => f.IsConstructor && !f.IsStatic);

            Ins notNullLabel = CreateNop();

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, instanceField),
                Ins.Create(OpCodes.Brtrue, notNullLabel),
                Ins.Create(OpCodes.Newobj, constructor),
                Ins.Create(OpCodes.Stsfld, instanceField),
                notNullLabel
            };

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteSetMethodInfo(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            VariableDefinition maVarOpt,
            FieldReference maFieldOpt)
        {
            MethodDefinition target = stateMachineOpt ?? method;
            MethodReference getMethodFromHandle = mwc.SafeImport(mwc.Framework.MethodBase_GetMethodFromHandle);
            MethodReference setMethod = mwc.SafeImport(mwc.Spinner.MethodArgs_Method.SetMethod);
            TypeReference methodInfo = mwc.SafeImport(mwc.Framework.MethodInfo);

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

            target.Body.InsertInstructions(offset, insc);
        }

        protected static void CreateAspectCacheField(
            ModuleWeavingContext mwc,
            TypeDefinition declaringType,
            TypeReference aspectType,
            string cacheFieldName,
            out FieldReference aspectCacheField)
        {
            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            // Find existing so property aspects do not generate two cache fields
            var aspectFieldDef = new FieldDefinition(cacheFieldName, fattrs, mwc.SafeImport(aspectType));
            AddCompilerGeneratedAttribute(mwc, aspectFieldDef);
            declaringType.Fields.Add(aspectFieldDef);

            aspectCacheField = aspectFieldDef;
        }

        protected static string ExtractOriginalName(string name)
        {
            const StringComparison comparison = StringComparison.InvariantCulture;

            int endBracketIndex;
            if (name.StartsWith("<", comparison) && (endBracketIndex = name.IndexOf(">", comparison)) != -1)
            {
                return name.Substring(1, endBracketIndex - 1);
            }
            else
            {
                return name;
            }
        }

        protected static MethodDefinition MakeDefaultConstructor(ModuleWeavingContext mwc, TypeDefinition type)
        {
            TypeDefinition baseTypeDef = type.BaseType.Resolve();
            MethodDefinition baseCtorDef = baseTypeDef.Methods.Single(m => !m.IsStatic && m.Parameters.Count == 0);

            MethodReference baseCtor = type.BaseType.IsGenericInstance
                ? mwc.SafeImport(baseCtorDef).WithGenericDeclaringType((GenericInstanceType) type.BaseType)
                : mwc.SafeImport(baseCtorDef);

            var attrs = MethodAttributes.Public |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName;

            var method = new MethodDefinition(".ctor", attrs, mwc.Module.TypeSystem.Void);

            Collection<Ins> i = method.Body.Instructions;
            i.Add(Ins.Create(OpCodes.Ldarg_0));
            i.Add(Ins.Create(OpCodes.Call, baseCtor));
            i.Add(Ins.Create(OpCodes.Ret));

            return method;
        }

        protected static Ins CreateNop()
        {
            return Ins.Create(OpCodes.Nop);
        }

        protected static void AddCompilerGeneratedAttribute(ModuleWeavingContext mwc, ICustomAttributeProvider definition)
        {
            if (definition.CustomAttributes.Any(a => a.AttributeType.IsSame(mwc.Framework.CompilerGeneratedAttribute)))
                return;

            MethodReference ctor = mwc.SafeImport(mwc.Framework.CompilerGeneratedAttribute_ctor);
            
            definition.CustomAttributes.Add(new CustomAttribute(ctor));
        }
    }
}