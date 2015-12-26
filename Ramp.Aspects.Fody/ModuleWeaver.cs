using System;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Ramp.Aspects.Fody.Weavers;

namespace Ramp.Aspects.Fody
{
    public class ModuleWeaver
    {
        public ModuleDefinition ModuleDefinition { get; set; }

        public Action<string> LogDebug { get; set; }
        
        public Action<string> LogInfo { get; set; }
        
        public Action<string> LogWarning { get; set; }
        
        public Action<string> LogError { get; set; }

        // Definition for IMethodInterceptionAspect
        private TypeDefinition _methodInterceptionAspectTypeDef;
        private CacheClassBuilder _ccb;

        public void Execute()
        {
            LogDebug = s => { };
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };

            _ccb = new CacheClassBuilder(ModuleDefinition);

            AssemblyNameReference aspectsModuleName = ModuleDefinition.AssemblyReferences.First(a => a.Name == "Ramp.Aspects");
            AssemblyDefinition aspectsAssembly = ModuleDefinition.AssemblyResolver.Resolve(aspectsModuleName);

            _methodInterceptionAspectTypeDef = aspectsAssembly.MainModule.GetType("Ramp.Aspects.IMethodInterceptionAspect");

            foreach (TypeDefinition type in ModuleDefinition.GetAllTypes().ToList())
            {
                foreach (MethodDefinition method in type.Methods.ToList())
                {
                    foreach (CustomAttribute a in method.CustomAttributes)
                    {
                        TypeDefinition attributeTypeDef = a.AttributeType.Resolve();
                        if (IsMethodInterceptionAspectAttribute(attributeTypeDef))
                        {
                            Debug.Assert(method.HasBody);
                            var w = new MethodInterceptionAspectWeaver(aspectsAssembly.MainModule, _ccb, method, attributeTypeDef);
                            w.Weave();
                        }
                    }
                }
            }
        }

        private bool IsMethodInterceptionAspectAttribute(TypeDefinition attributeTypeDef)
        {
            TypeDefinition current = attributeTypeDef;
            do
            {
                foreach (TypeReference ir in current.Interfaces)
                {
                    if (ir.Resolve() == _methodInterceptionAspectTypeDef)
                        return true;
                }

                current = current.BaseType?.Resolve();
            } while (current != null);

            return false;
        }
    }
}
