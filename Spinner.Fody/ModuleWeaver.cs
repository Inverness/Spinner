using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Spinner.Fody.Analysis;
using Spinner.Fody.Weavers;

namespace Spinner.Fody
{
    public class ModuleWeaver
    {
        public ModuleDefinition ModuleDefinition { get; set; }

        public Action<string> LogDebug { get; set; }
        
        public Action<string> LogInfo { get; set; }
        
        public Action<string> LogWarning { get; set; }
        
        public Action<string> LogError { get; set; }

        // Definition for IMethodInterceptionAspect
        private int _aspectIndexCounter;
        private ModuleWeavingContext _mwc;

        public ModuleWeaver()
        {
            LogDebug = s => { };
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };
        }

        public void Execute()
        {
            LogInfo($"Beginning aspect weaving for {ModuleDefinition.Assembly.FullName}");

            AssemblyNameReference spinnerName = ModuleDefinition.AssemblyReferences.FirstOrDefault(a => a.Name == "Spinner");

            if (spinnerName == null)
            {
                LogWarning("No reference to Spinner assembly detected. Doing nothing.");
                return;
            }

            _mwc = new ModuleWeavingContext(ModuleDefinition,
                                            ModuleDefinition.AssemblyResolver.Resolve(spinnerName).MainModule);

            List<TypeDefinition> types = ModuleDefinition.GetAllTypes().ToList();
            
            Task[] analysisTasks = types.Select(CreateAnalysisAction)
                                        .Where(a => a != null)
                                        .Select(Task.Run)
                                        .ToArray();

            if (analysisTasks.Length != 0)
            {
                Task.WhenAll(analysisTasks).Wait();
                LogInfo($"Finished feature analysisf or {analysisTasks.Length} types.");
            }

            // Execute type weavings in parallel. The ModuleWeavingContext provides thread-safe imports.
            // Weaving does not require any other module-level changes.

            // Tasks are only created when there is actual work to be done for a type.
            Task[] tasks = types.Select(CreateWeaveAction)
                                .Where(a => a != null)
                                .Select(Task.Run)
                                .ToArray();

            if (tasks.Length != 0)
            {
                Task.WhenAll(tasks).Wait();
                LogInfo($"Finished aspect weaving for {tasks.Length} types.");
            }
            else
            {
                LogWarning("No types found with aspects.");
            }
        }

        private Action CreateWeaveAction(TypeDefinition type)
        {
            List<Tuple<IMemberDefinition, TypeDefinition, int>> aspects = null;

            // Use HasX properties to avoid on-demand allocation of the collections.

            if (type.HasMethods)
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (method.HasCustomAttributes)
                    {
                        foreach (CustomAttribute a in method.CustomAttributes)
                        {
                            TypeDefinition attributeType = a.AttributeType.Resolve();

                            if (IsAspectAttribute(attributeType, _mwc.Spinner.IMethodBoundaryAspect))
                            {
                                Debug.Assert(method.HasBody);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) method, attributeType, 0));

                                LogDebug($"Found aspect {attributeType.Name} for {method}");
                            }
                            else if (IsAspectAttribute(attributeType, _mwc.Spinner.IMethodInterceptionAspect))
                            {
                                Debug.Assert(method.HasBody);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) method, attributeType, 1));

                                LogDebug($"Found aspect {attributeType.Name} for {method}");
                            }
                        }
                    }
                }
            }

            if (type.HasProperties)
            {
                foreach (PropertyDefinition property in type.Properties)
                {
                    if (property.HasCustomAttributes)
                    {
                        foreach (CustomAttribute a in property.CustomAttributes)
                        {
                            TypeDefinition attributeType = a.AttributeType.Resolve();

                            if (IsAspectAttribute(attributeType, _mwc.Spinner.IPropertyInterceptionAspect))
                            {
                                Debug.Assert(property.GetMethod != null || property.SetMethod != null);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) property, attributeType, 2));

                                LogDebug($"Found aspect {attributeType.Name} for {property}");
                            }
                        }
                    }
                }
            }

            if (aspects == null)
                return null;

            Action taskAction = () =>
            {
                LogInfo($"Weaving {aspects.Count} aspects for {type}");

                foreach (Tuple<IMemberDefinition, TypeDefinition, int> a in aspects)
                {
                    int aspectIndex = Interlocked.Increment(ref _aspectIndexCounter);

                    switch (a.Item3)
                    {
                        case 0:
                            MethodBoundaryAspectWeaver.Weave(_mwc,
                                                             (MethodDefinition) a.Item1,
                                                             a.Item2,
                                                             aspectIndex);
                            break;
                        case 1:
                            MethodInterceptionAspectWeaver.Weave(_mwc,
                                                                 (MethodDefinition) a.Item1,
                                                                 a.Item2,
                                                                 aspectIndex);
                            break;
                        case 2:
                            PropertyInterceptionAspectWeaver.Weave(_mwc,
                                                                   (PropertyDefinition) a.Item1,
                                                                   a.Item2,
                                                                   aspectIndex);
                            break;

                    }
                }
            };

            return taskAction;
        }

        private Action CreateAnalysisAction(TypeDefinition type)
        {
            return () => AspectFeatureAnalyzer.Analyze(_mwc, type);
        }

        private static bool IsAspectAttribute(TypeDefinition attributeType, TypeDefinition aspectType)
        {
            TypeDefinition current = attributeType;
            do
            {
                for (int i = 0; i < current.Interfaces.Count; i++)
                {
                    if (current.Interfaces[i].Resolve() == aspectType)
                        return true;
                }

                current = current.BaseType?.Resolve();
            } while (current != null);

            return false;
        }
    }
}
