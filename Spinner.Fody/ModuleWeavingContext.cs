using System;
using Mono.Cecil;

namespace Spinner.Fody
{
    /// <summary>
    /// Provides global context and functionality for weavers.
    /// </summary>
    internal class ModuleWeavingContext
    {
        internal readonly ModuleDefinition Module;
        internal readonly WellKnownSpinnerMembers Spinner;
        internal readonly WellKnownFrameworkMembers Framework;
        
        internal ModuleWeavingContext(ModuleDefinition module, ModuleDefinition libraryModule)
        {
            Module = module;
            Spinner = new WellKnownSpinnerMembers(libraryModule);
            Framework = new WellKnownFrameworkMembers(module);
        }

        internal TypeReference SafeImport(Type type)
        {
            lock (Module)
                return Module.Import(type);
        }

        internal TypeReference SafeImport(TypeReference type)
        {
            if (type.Module == Module)
                return type;
            lock (Module)
                return Module.Import(type);
        }

        internal MethodReference SafeImport(MethodReference method)
        {
            if (method.Module == Module)
                return method;
            lock (Module)
                return Module.Import(method);
        }

        internal FieldReference SafeImport(FieldReference field)
        {
            if (field.Module == Module)
                return field;
            lock (Module)
                return Module.Import(field);
        }

        internal TypeDefinition SafeGetType(string fullName)
        {
            lock (Module)
                return Module.GetType(fullName);
        }
    }
}