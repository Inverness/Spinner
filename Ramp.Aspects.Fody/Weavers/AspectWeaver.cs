using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Ramp.Aspects.Fody.Weavers
{
    /// <summary>
    /// Base class for aspect weavers
    /// </summary>
    internal abstract class AspectWeaver
    {
        protected readonly ModuleDefinition AspectLibraryModule;
        protected readonly CacheClassBuilder CacheClassBuilder;
        protected readonly MethodDefinition Method;
        protected readonly TypeDefinition AspectType;
        protected readonly bool ReturnsVoid;

        protected AspectWeaver(
            ModuleDefinition aspectLibraryModule,
            CacheClassBuilder ccb,
            MethodDefinition method,
            TypeDefinition aspectType)
        {
            AspectLibraryModule = aspectLibraryModule;
            CacheClassBuilder = ccb;
            Method = method;
            AspectType = aspectType;
            ReturnsVoid = Method.ReturnType == Method.Module.TypeSystem.Void;
        }

        /// <summary>
        /// Write code to initialize the argument container instance.
        /// </summary>
        /// <param name="il"></param>
        /// <param name="argumentsVariable"></param>
        /// <param name="argumentFields"></param>
        protected void WriteArgumentContainerInit(
            ILProcessor il,
            out VariableDefinition argumentsVariable,
            out FieldReference[] argumentFields)
        {
            if (Method.Parameters.Count == 0)
            {
                argumentsVariable = null;
                argumentFields = null;
                return;
            }

            ModuleDefinition module = Method.Module;

            // Write the constructor

            TypeReference[] baseParameterTypes =
                Method.Parameters
                    .Select(p => p.ParameterType.IsByReference ? p.ParameterType.GetElementType() : p.ParameterType)
                    .ToArray();

            TypeDefinition argumentsTypeDef =
                AspectLibraryModule.GetType("Ramp.Aspects.Internal.Arguments`" + Method.Parameters.Count);
            GenericInstanceType argumentsType =
                module.Import(argumentsTypeDef).MakeGenericInstanceType(baseParameterTypes);

            argumentsVariable = new VariableDefinition("arguments", argumentsType);
            il.Body.Variables.Add(argumentsVariable);

            MethodDefinition constructorDef = argumentsTypeDef.GetConstructors().Single(m => m.HasThis);
            MethodReference constructor = module.Import(constructorDef).WithGenericDeclaringType(argumentsType);

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, argumentsVariable);
            
            // Cache argument fields for later use
            argumentFields = new FieldReference[Method.Parameters.Count];

            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                string fieldName = "Item" + i;
                FieldDefinition fieldDef = argumentsTypeDef.Fields.First(f => f.Name == fieldName);
                FieldReference field = module.Import(fieldDef).WithGenericDeclaringType(argumentsType);

                argumentFields[i] = field;
            }
        }

        /// <summary>
        /// Copies arguments from the method to the generic arguments container.
        /// </summary>
        /// <param name="il"></param>
        /// <param name="argumentsVariable"></param>
        /// <param name="argumentFields"></param>
        /// <param name="excludeOut"></param>
        protected void WriteCopyArgumentsToContainer(ILProcessor il, VariableDefinition argumentsVariable, FieldReference[] argumentFields, bool excludeOut)
        {
            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                if (Method.Parameters[i].IsOut && excludeOut)
                    continue;

                TypeReference parameterType = Method.Parameters[i].ParameterType;
                int argumentIndex = Method.IsStatic ? i : i + 1;

                il.Emit(OpCodes.Ldloc, argumentsVariable);
                il.Emit(OpCodes.Ldarg, argumentIndex);
                if (parameterType.IsByReference)
                {
                    if (parameterType.GetElementType().IsValueType)
                        il.Emit(OpCodes.Ldobj, parameterType.GetElementType());
                    else
                        il.Emit(OpCodes.Ldind_Ref);
                }

                il.Emit(OpCodes.Stfld, argumentFields[i]);
            }
        }

        /// <summary>
        /// Copies arguments from the generic arguments container to the method.
        /// </summary>
        /// <param name="il"></param>
        /// <param name="argumentsVariable"></param>
        /// <param name="argumentFields"></param>
        /// <param name="includeNormal"></param>
        /// <param name="includeRef"></param>
        protected void WriteCopyArgumentsFromContainer(ILProcessor il, VariableDefinition argumentsVariable, FieldReference[] argumentFields, bool includeNormal, bool includeRef)
        {
            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                TypeReference parameterType = Method.Parameters[i].ParameterType;

                int argumentIndex = Method.IsStatic ? i : i + 1;

                if (parameterType.IsByReference)
                {
                    if (!includeRef)
                        continue;
                    
                    il.Emit(OpCodes.Ldarg, argumentIndex);
                    il.Emit(OpCodes.Ldloc, argumentsVariable);
                    il.Emit(OpCodes.Ldfld, argumentFields[i]);
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
                    il.Emit(OpCodes.Ldfld, argumentFields[i]);
                    il.Emit(OpCodes.Starg, argumentIndex);
                }
            }
        }

        /// <summary>
        /// Initializes out arugments to their default values.
        /// </summary>
        /// <param name="il"></param>
        protected void WriteOutArgumentInit(ILProcessor il)
        {
            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                if (!Method.Parameters[i].IsOut)
                    continue;

                TypeReference parameterType = Method.Parameters[i].ParameterType;
                int argumentIndex = Method.IsStatic ? i : i + 1;

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
    }
}