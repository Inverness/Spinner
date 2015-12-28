using System;
using Mono.Cecil;

namespace Ramp.Aspects.Fody
{
    /// <summary>
    /// Provides context for safe importing
    /// </summary>
    internal class ImportContext
    {
        internal readonly ModuleDefinition CurrentModule;
        internal readonly IMetadataResolver SafeCurrentResolver;

        internal readonly WellKnownLibraryMembers Library;

        private readonly object _lock = new object();
        
        internal ImportContext(ModuleDefinition currentModule, ModuleDefinition libraryModule)
        {
            Library = new WellKnownLibraryMembers(libraryModule);
            CurrentModule = currentModule;
            SafeCurrentResolver = new SafeMetadataResolver(_lock, currentModule.MetadataResolver);
        }

        internal TypeReference SafeImport(Type type)
        {
            lock (_lock)
                return CurrentModule.Import(type);
        }

        internal TypeReference SafeImport(TypeReference type)
        {
            lock (_lock)
                return CurrentModule.Import(type);
        }

        internal MethodReference SafeImport(MethodReference method)
        {
            lock (_lock)
                return CurrentModule.Import(method);
        }

        internal FieldReference SafeImport(FieldReference field)
        {
            lock (_lock)
                return CurrentModule.Import(field);
        }

        internal TypeDefinition SafeGetType(string fullName)
        {
            lock (_lock)
                return CurrentModule.GetType(fullName);
        }

        private class SafeMetadataResolver : IMetadataResolver
        {
            private readonly object _lock;
            private readonly IMetadataResolver _inner;

            internal SafeMetadataResolver(object resolveLock, IMetadataResolver inner)
            {
                _lock = resolveLock;
                _inner = inner;
            }

            public TypeDefinition Resolve(TypeReference type)
            {
                lock (_lock)
                    return _inner.Resolve(type);
            }

            public FieldDefinition Resolve(FieldReference field)
            {
                lock (_lock)
                    return _inner.Resolve(field);
            }

            public MethodDefinition Resolve(MethodReference method)
            {
                lock (_lock)
                    return _inner.Resolve(method);
            }
        }
    }
}