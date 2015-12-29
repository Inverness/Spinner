using System;
using Mono.Cecil;

namespace Ramp.Aspects.Fody
{
    /// <summary>
    /// Provides thread-safe importing and well known aspect library members.
    /// </summary>
    internal class ModuleWeavingContext
    {
        internal readonly ModuleDefinition Module;
        internal readonly WellKnownLibraryMembers Library;
        internal readonly WellKnownFrameworkMembers Framework;
        
        internal ModuleWeavingContext(ModuleDefinition module, ModuleDefinition libraryModule)
        {
            Module = module;
            Library = new WellKnownLibraryMembers(libraryModule);
            Framework = new WellKnownFrameworkMembers(module);
        }

        //internal TypeDefinition SafeResolve(TypeReference type)
        //{
        //    if (type.IsDefinition)
        //        return (TypeDefinition) type;
        //    lock (type.Module)
        //        return type.Resolve();
        //}

        //internal FieldDefinition SafeResolve(FieldReference field)
        //{
        //    if (field.IsDefinition)
        //        return (FieldDefinition) field;
        //    lock (field.Module)
        //        return field.Resolve();
        //}

        //internal PropertyDefinition SafeResolve(PropertyReference property)
        //{
        //    if (property.IsDefinition)
        //        return (PropertyDefinition) property;
        //    lock (property.Module)
        //        return property.Resolve();
        //}

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