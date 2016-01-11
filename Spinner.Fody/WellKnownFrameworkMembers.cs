using System.Linq;
using Mono.Cecil;

namespace Spinner.Fody
{
    /// <summary>
    /// Cache well known definitions from the .NET Framework. These are not imported by default.
    /// </summary>
    internal class WellKnownFrameworkMembers
    {
        private const string NsSystem = "System";
        private const string NsReflection = "System.Reflection";
        private const string NsCompilerServices = "System.Runtime.CompilerServices";

        // ReSharper disable InconsistentNaming
        internal readonly TypeDefinition Exception;
        internal readonly TypeDefinition Attribute;
        internal readonly TypeDefinition AsyncStateMachineAttribute;
        internal readonly TypeDefinition IteratorStateMachineAttribute;
        internal readonly TypeDefinition CompilerGeneratedAttribute;
        internal readonly MethodDefinition CompilerGeneratedAttribute_ctor;
        internal readonly TypeDefinition MethodBase;
        internal readonly MethodDefinition MethodBase_GetMethodFromHandle;
        internal readonly TypeDefinition MethodInfo;
        internal readonly TypeDefinition Delegate;
        internal readonly MethodDefinition Type_GetTypeFromHandle;
        // ReSharper restore InconsistentNaming

        internal WellKnownFrameworkMembers(ModuleDefinition currentModule)
        {
            AssemblyNameReference runtimeAssemblyName = currentModule.AssemblyReferences.FirstOrDefault(n => n.Name == "System.Runtime");
            if (runtimeAssemblyName == null)
                runtimeAssemblyName = currentModule.AssemblyReferences.First(n => n.Name == "mscorlib");

            AssemblyDefinition runtimeAssembly = currentModule.AssemblyResolver.Resolve(runtimeAssemblyName);
            ModuleDefinition module = runtimeAssembly.MainModule;

            Exception = module.GetType(NsSystem, nameof(Exception));
            Attribute = module.GetType(NsSystem, nameof(Attribute));
            AsyncStateMachineAttribute = module.GetType(NsCompilerServices, nameof(AsyncStateMachineAttribute));
            IteratorStateMachineAttribute = module.GetType(NsCompilerServices, nameof(IteratorStateMachineAttribute));
            CompilerGeneratedAttribute = module.GetType(NsCompilerServices, nameof(CompilerGeneratedAttribute));
            CompilerGeneratedAttribute_ctor = CompilerGeneratedAttribute.Methods.First(m => m.IsConstructor && !m.IsStatic && !m.HasParameters);
            MethodBase = module.GetType(NsReflection, "MethodBase");
            MethodBase_GetMethodFromHandle = MethodBase.Methods.First(m => m.Name == "GetMethodFromHandle" && m.Parameters.Count == 1);
            MethodInfo = module.GetType(NsReflection, "MethodInfo");
            Delegate = module.GetType(NsSystem, "Delegate");
            var type = module.GetType(NsSystem, "Type");
            Type_GetTypeFromHandle = type.Methods.First(m => m.Name == "GetTypeFromHandle");
        }
    }
}