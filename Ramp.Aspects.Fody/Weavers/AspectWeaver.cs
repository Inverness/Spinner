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

        protected void WriteArgumentsInit(ILProcessor il, out VariableDefinition argumentsVariable, out FieldReference[] argumentFields)
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

            TypeDefinition argumentsTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.Arguments`" + Method.Parameters.Count);
            GenericInstanceType argumentsType = module.Import(argumentsTypeDef).MakeGenericInstanceType(baseParameterTypes);

            argumentsVariable = new VariableDefinition("arguments", argumentsType);
            il.Body.Variables.Add(argumentsVariable);

            MethodDefinition constructorDef = argumentsTypeDef.GetConstructors().Single(m => m.HasThis);
            MethodReference constructor = module.Import(constructorDef).WithGenericDeclaringType(argumentsType);

            il.Emit(OpCodes.Newobj, constructor);

            // Write variable initiations
            argumentFields = new FieldReference[Method.Parameters.Count];

            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                string fieldName = "Item" + i;
                FieldDefinition fieldDef = argumentsTypeDef.Fields.First(f => f.Name == fieldName);
                FieldReference field = module.Import(fieldDef).WithGenericDeclaringType(argumentsType);

                argumentFields[i] = field;

                if (Method.Parameters[i].IsOut)
                    continue;

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Stfld, module.Import(field));
            }

            il.Emit(OpCodes.Stloc, argumentsVariable);
        }
    }
}