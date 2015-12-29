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
        private TypeDefinition _methodBoundaryAspectTypeDef;
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
            _methodBoundaryAspectTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.IMethodBoundaryAspect");
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
            List<Tuple<IMemberDefinition, TypeDefinition, int>> aspects = null;

            foreach (MethodDefinition method in type.Methods)
            {
                foreach (CustomAttribute a in method.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();

                    if (IsAspectAttribute(attributeType, _methodBoundaryAspectTypeDef))
                    {
                        Debug.Assert(method.HasBody);

                        if (aspects == null)
                            aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                        aspects.Add(Tuple.Create((IMemberDefinition) method, attributeType, 0));
                    }
                    else if (IsAspectAttribute(attributeType, _methodInterceptionAspectTypeDef))
                    {
                        Debug.Assert(method.HasBody);

                        if (aspects == null)
                            aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                        aspects.Add(Tuple.Create((IMemberDefinition) method, attributeType, 1));
                    }
                }
            }

            foreach (PropertyDefinition property in type.Properties)
            {
                foreach (CustomAttribute a in property.CustomAttributes)
                {
                    TypeDefinition attributeType = a.AttributeType.Resolve();

                    if (IsAspectAttribute(attributeType, _propertyInterceptionAspectTypeDef))
                    {
                        Debug.Assert(property.GetMethod != null || property.SetMethod != null);
                        
                        if (aspects == null)
                            aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                        aspects.Add(Tuple.Create((IMemberDefinition) property, attributeType, 2));
                    }
                }
            }

            if (aspects == null) return;

            foreach (Tuple<IMemberDefinition, TypeDefinition, int> a in aspects)
            {
                int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                switch (a.Item3)
                {
                    case 0:
                        MethodBoundaryAspectWeaver.Weave(_moduleWeavingContext,
                                                         (MethodDefinition) a.Item1,
                                                         a.Item2,
                                                         aspectIndex);
                        break;
                    case 1:
                        MethodInterceptionAspectWeaver.Weave(_moduleWeavingContext,
                                                             (MethodDefinition) a.Item1,
                                                             a.Item2,
                                                             aspectIndex);
                        break;
                    case 2:
                        PropertyInterceptionAspectWeaver.Weave(_moduleWeavingContext,
                                                               (PropertyDefinition) a.Item1,
                                                               a.Item2,
                                                               aspectIndex);
                        break;

                }
            }
        }

        private static bool IsAspectAttribute(TypeDefinition attributeTypeDef, TypeDefinition aspectTypeDef)
        {
            TypeDefinition current = attributeTypeDef;
            do
            {
                foreach (TypeReference ir in current.Interfaces)
                {
                    if (ir.Resolve() == aspectTypeDef)
                        return true;
                }

                current = current.BaseType?.Resolve();
            } while (current != null);

            return false;
        }
    }
}
