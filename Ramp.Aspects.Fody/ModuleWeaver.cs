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
        private ModuleWeavingContext _moduleWeavingContext;

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
            _moduleWeavingContext = new ModuleWeavingContext(ModuleDefinition, aspectLibraryModule);

            var typeList = new List<TypeDefinition>(ModuleDefinition.GetAllTypes());

            // Execute type weavings in parallel. Module weaving context provides thread-safe imports.
            // Weaving does not require any other module-level changes.
            ParallelLoopResult parallelLoopResult = Parallel.ForEach(typeList, WeaveType);
            Debug.Assert(parallelLoopResult.IsCompleted, "parallelLoopResult.IsCompleted");
        }

        private void WeaveType(TypeDefinition type)
        {
            List<Tuple<MethodDefinition, TypeDefinition>> methodInterceptions = null;
            List<Tuple<PropertyDefinition, TypeDefinition>> propertyInterceptions = null;

            foreach (MethodDefinition method in type.Methods)
            {
                foreach (CustomAttribute a in method.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();

                    if (IsMethodInterceptionAspectAttribute(attributeType))
                    {
                        Debug.Assert(method.HasBody);

                        if (methodInterceptions == null)
                            methodInterceptions = new List<Tuple<MethodDefinition, TypeDefinition>>();
                        methodInterceptions.Add(Tuple.Create(method, attributeType));
                    }
                }
            }

            foreach (PropertyDefinition property in type.Properties)
            {
                foreach (CustomAttribute a in property.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();

                    if (IsPropertyInterceptionAspectAttribute(attributeType))
                    {
                        Debug.Assert(property.GetMethod != null || property.SetMethod != null);
                        
                        if (propertyInterceptions == null)
                            propertyInterceptions = new List<Tuple<PropertyDefinition, TypeDefinition>>();
                        propertyInterceptions.Add(Tuple.Create(property, attributeType));
                    }
                }
            }

            if (methodInterceptions != null)
            {
                foreach (Tuple<MethodDefinition, TypeDefinition> method in methodInterceptions)
                {
                    int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                    // TODO: Support aspect constructor arguments
                    MethodInterceptionAspectWeaver.Weave(_moduleWeavingContext,
                                                         method.Item1,
                                                         method.Item2,
                                                         aspectIndex);
                }
            }

            if (propertyInterceptions != null)
            {
                foreach (Tuple<PropertyDefinition, TypeDefinition> property in propertyInterceptions)
                {
                    int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                    PropertyInterceptionAspectWeaver.Weave(_moduleWeavingContext,
                                                           property.Item1,
                                                           property.Item2,
                                                           aspectIndex);
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
