using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private TypeDefinition _propertyInterceptionAspectTypeDef;
        private int _aspectIndexCounter;
        private ImportContext _importContext;

        public ModuleWeaver()
        {
            LogDebug = s => { };
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };
        }

        public void Execute()
        {
            AssemblyNameReference aspectsModuleName = ModuleDefinition.AssemblyReferences.First(a => a.Name == "Ramp.Aspects");
            AssemblyDefinition aspectsAssembly = ModuleDefinition.AssemblyResolver.Resolve(aspectsModuleName);

            var aspectLibraryModule = aspectsAssembly.MainModule;
            _methodInterceptionAspectTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.IMethodInterceptionAspect");
            _propertyInterceptionAspectTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.IPropertyInterceptionAspect");
            _importContext = new ImportContext(ModuleDefinition, aspectLibraryModule);

            var typeList = new List<TypeDefinition>(ModuleDefinition.GetAllTypes());

            ParallelLoopResult result = Parallel.ForEach(typeList, WeaveType);
            Debug.Assert(result.IsCompleted);
        }

        private void WeaveType(TypeDefinition type)
        {
            foreach (MethodDefinition method in type.Methods.ToList())
            {
                foreach (CustomAttribute a in method.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();
                    if (IsMethodInterceptionAspectAttribute(attributeType))
                    {
                        Debug.Assert(method.HasBody);

                        int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                        MethodInterceptionAspectWeaver.Weave(_importContext, method, attributeType, aspectIndex);
                    }
                }
            }

            foreach (PropertyDefinition property in type.Properties.ToList())
            {
                foreach (CustomAttribute a in property.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();
                    if (IsPropertyInterceptionAspectAttribute(attributeType))
                    {
                        Debug.Assert(property.GetMethod != null || property.SetMethod != null);
                        
                        int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                        PropertyInterceptionAspectWeaver.Weave(_importContext, property, attributeType, aspectIndex);
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

        private bool IsPropertyInterceptionAspectAttribute(TypeDefinition attributeTypeDef)
        {
            TypeDefinition current = attributeTypeDef;
            do
            {
                foreach (TypeReference ir in current.Interfaces)
                {
                    if (ir.Resolve() == _propertyInterceptionAspectTypeDef)
                        return true;
                }

                current = current.BaseType?.Resolve();
            } while (current != null);

            return false;
        }
    }
}
