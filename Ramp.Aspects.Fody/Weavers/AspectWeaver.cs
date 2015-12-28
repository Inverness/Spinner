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
    /// Base class for aspect weavers
    /// </summary>
    internal class AspectWeaver
    {
        protected const string BindingInstanceFieldName = "Instance";

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
            ImportContext ic,
            MethodDefinition method,
            ILProcessor il,
            out VariableDefinition argumentsVariable)
        {
            // For property setters, the last argument is the property value which is handled separately.
            int effectiveParameterCount = GetEffectiveParameterCount(method);

            if (effectiveParameterCount == 0)
            {
                argumentsVariable = null;
                return;
            }

            // Write the constructor
            
            GenericInstanceType argumentsType;
            FieldReference[] argumentFields;
            GetArgumentContainerInfo(ic, method, out argumentsType, out argumentFields);
            TypeDefinition argumentsTypeDef = argumentsType.Resolve();

            argumentsVariable = il.Body.AddVariableDefinition("arguments", argumentsType);

            MethodDefinition constructorDef = argumentsTypeDef.GetConstructors().Single(m => m.HasThis);
            MethodReference constructor = ic.SafeImport(constructorDef).WithGenericDeclaringType(argumentsType);

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, argumentsVariable);
        }

        protected static void GetArgumentContainerInfo(
            ImportContext ic,
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

            TypeDefinition typeDef = ic.Library.ArgumentContainer[effectiveParameterCount];
            type = ic.SafeImport(typeDef).MakeGenericInstanceType(baseParameterTypes);

            fields = new FieldReference[effectiveParameterCount];

            for (int i = 0; i < effectiveParameterCount; i++)
            {
                string fieldName = "Item" + i;
                FieldDefinition fieldDef = typeDef.Fields.First(f => f.Name == fieldName);
                FieldReference field = ic.SafeImport(fieldDef).WithGenericDeclaringType(type);

                fields[i] = field;
            }
        }

        /// <summary>
        /// Copies arguments from the method to the generic arguments container.
        /// </summary>
        protected static void WriteCopyArgumentsToContainer(
            ImportContext rc,
            MethodDefinition method,
            ILProcessor il,
            VariableDefinition argumentsVariable,
            bool excludeOut)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(rc, method, out argumentContainerType, out argumentContainerFields);

            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                if (method.Parameters[i].IsOut && excludeOut)
                    continue;

                TypeReference parameterType = method.Parameters[i].ParameterType;
                int argumentIndex = method.IsStatic ? i : i + 1;

                il.Emit(OpCodes.Ldloc, argumentsVariable);
                il.Emit(OpCodes.Ldarg, argumentIndex);
                if (parameterType.IsByReference)
                {
                    if (parameterType.GetElementType().IsValueType)
                        il.Emit(OpCodes.Ldobj, parameterType.GetElementType());
                    else
                        il.Emit(OpCodes.Ldind_Ref);
                }

                il.Emit(OpCodes.Stfld, argumentContainerFields[i]);
            }
        }

        /// <summary>
        /// Copies arguments from the generic arguments container to the method.
        /// </summary>
        protected static void WriteCopyArgumentsFromContainer(
            ImportContext rc,
            MethodDefinition method,
            ILProcessor il,
            VariableDefinition argumentsVariable,
            bool includeNormal,
            bool includeRef)
        {
            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(rc, method, out argumentContainerType, out argumentContainerFields);

            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                TypeReference parameterType = method.Parameters[i].ParameterType;

                int argumentIndex = method.IsStatic ? i : i + 1;

                if (parameterType.IsByReference)
                {
                    if (!includeRef)
                        continue;
                    
                    il.Emit(OpCodes.Ldarg, argumentIndex);
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
                    il.Emit(OpCodes.Starg, argumentIndex);
                }
            }
        }

        /// <summary>
        /// Initializes out arugments to their default values.
        /// </summary>
        protected static void WriteOutArgumentInit(MethodDefinition method, ILProcessor il)
        {
            for (int i = 0; i < GetEffectiveParameterCount(method); i++)
            {
                if (!method.Parameters[i].IsOut)
                    continue;

                TypeReference parameterType = method.Parameters[i].ParameterType;
                int argumentIndex = method.IsStatic ? i : i + 1;

                if (parameterType.IsByReference)
                {
                    il.Emit(OpCodes.Ldarg, argumentIndex);
                    if (parameterType.GetElementType().IsValueType)
                    {
                        il.Emit(OpCodes.Initobj, parameterType.GetElementType());
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Stind_Ref);
                    }
                }
                else
                {
                    if (parameterType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldarga, argumentIndex);
                        il.Emit(OpCodes.Initobj, parameterType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Starg, argumentIndex);
                    }

                }
            }
        }

        /// <summary>
        /// Write aspect initialization.
        /// </summary>
        protected static void WriteAspectInit(
            ImportContext ic,
            MethodDefinition method,
            TypeDefinition aspectType,
            FieldReference aspectCacheField,
            ILProcessor il,
            LabelProcessor lp)
        {
            // NOTE: Aspect type can't be generic since its declared by an attribute
            MethodDefinition ctorDef = aspectType.GetConstructors().Single(m => m.HasThis && m.Parameters.Count == 0);
            MethodReference ctor = ic.SafeImport(ctorDef);

            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, aspectCacheField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stsfld, aspectCacheField);
            lp.MarkLabel(notNullLabel);
        }

        protected static void WriteCallAdvice(
            ImportContext ic,
            string name,
            TypeDefinition aspectType,
            ILProcessor il,
            FieldReference aspectField,
            VariableDefinition iaVariable)
        {
            MethodDefinition adviceDef = aspectType.Methods.Single(m => m.Name == name);
            MethodReference advice = ic.SafeImport(adviceDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, iaVariable);
            il.Emit(OpCodes.Callvirt, advice);
        }

        /// <summary>
        /// 
        /// </summary>
        protected static void WriteBindingInit(ILProcessor il, LabelProcessor lp, TypeDefinition bindingType)
        {
            // Initialize the binding instance
            FieldReference instanceField = bindingType.Fields.Single(f => f.Name == BindingInstanceFieldName);
            MethodReference constructor = bindingType.Methods.Single(f => f.IsConstructor && !f.IsStatic);

            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, instanceField);
            lp.MarkLabel(notNullLabel);
        }

        protected static void CreateAspectCacheField(
            ImportContext ic,
            TypeDefinition declaringType,
            TypeReference aspectType,
            string cacheFieldName,
            out FieldReference aspectCacheField)
        {
            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            // Find existing so property aspects do not generate two cache fields
            var aspectFieldDef = new FieldDefinition(cacheFieldName, fattrs, ic.SafeImport(aspectType));
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

        protected static MethodDefinition MakeDefaultConstructor(ImportContext ic, TypeDefinition type)
        {
            TypeDefinition baseTypeDef = type.BaseType.Resolve();
            MethodDefinition baseCtorDef = baseTypeDef.Methods.Single(m => m.HasThis && m.Parameters.Count == 0);

            MethodReference baseCtor;
            if (type.BaseType.IsGenericInstance)
            {
                var baseType = (GenericInstanceType) type.BaseType;

                baseCtor = ic.SafeImport(baseCtorDef).WithGenericDeclaringType(baseType);
            }
            else
            {
                baseCtor = ic.SafeImport(baseCtorDef);
            }

            var attrs = MethodAttributes.Public |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName;

            var method = new MethodDefinition(".ctor", attrs, ic.CurrentModule.TypeSystem.Void);

            Collection<Instruction> i = method.Body.Instructions;
            i.Add(Instruction.Create(OpCodes.Ldarg_0));
            i.Add(Instruction.Create(OpCodes.Call, baseCtor));
            i.Add(Instruction.Create(OpCodes.Ret));

            return method;
        }
    }
}