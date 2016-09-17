using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Spinner.Fody
{
    /// <summary>
    /// Cache well known definitions from the .NET Framework. These are not imported by default.
    /// </summary>
    internal class WellKnownFrameworkMembers
    {
        private const string NSys = "System";
        private const string NRef = "System.Reflection";
        private const string NComp = "System.Runtime.CompilerServices";

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
        internal readonly TypeDefinition ActionT1;
        internal readonly MethodDefinition ActionT1_ctor;
        // ReSharper restore InconsistentNaming

        internal WellKnownFrameworkMembers(ModuleDefinition currentModule)
        {
            AssemblyNameReference runtimeAssemblyName = currentModule.AssemblyReferences.FirstOrDefault(n => n.Name == "System.Runtime");
            if (runtimeAssemblyName == null)
                runtimeAssemblyName = currentModule.AssemblyReferences.First(n => n.Name == "mscorlib");

            AssemblyDefinition runtimeAssembly = currentModule.AssemblyResolver.Resolve(runtimeAssemblyName);
            ModuleDefinition module = runtimeAssembly.MainModule;

            Exception = module.GetType(NSys, nameof(Exception));
            Attribute = module.GetType(NSys, nameof(Attribute));
            AsyncStateMachineAttribute = module.GetType(NComp, nameof(AsyncStateMachineAttribute));
            IteratorStateMachineAttribute = module.GetType(NComp, nameof(IteratorStateMachineAttribute));
            CompilerGeneratedAttribute = module.GetType(NComp, nameof(CompilerGeneratedAttribute));
            CompilerGeneratedAttribute_ctor = CompilerGeneratedAttribute.Methods.First(m => m.IsConstructor && !m.IsStatic && !m.HasParameters);
            MethodBase = module.GetType(NRef, "MethodBase");
            MethodBase_GetMethodFromHandle = MethodBase.Methods.First(m => m.Name == "GetMethodFromHandle" && m.Parameters.Count == 1);
            MethodInfo = module.GetType(NRef, "MethodInfo");
            Delegate = module.GetType(NSys, "Delegate");
            var type = module.GetType(NSys, "Type");
            Type_GetTypeFromHandle = type.Methods.First(m => m.Name == "GetTypeFromHandle");
            ActionT1 = type = module.GetType(NSys, "Action`1");
            ActionT1_ctor = type.GetConstructors().Single();
        }
    }
}