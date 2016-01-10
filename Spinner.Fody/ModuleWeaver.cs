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

            _mwc = new ModuleWeavingContext(this,
                                            ModuleDefinition,
                                            ModuleDefinition.AssemblyResolver.Resolve(spinnerName).MainModule);

            List<TypeDefinition> types = ModuleDefinition.GetAllTypes().ToList();
            var stopwatch = new Stopwatch();
            
            // Analyze aspect types in parallel.

            LogInfo("Beginning aspect feature analysis");

            stopwatch.Start();

            Task[] analysisTasks = types.Where(AspectFeatureAnalyzer.IsMaybeAspect)
                                        .Select(CreateAnalysisAction)
                                        .Select(Task.Run)
                                        .ToArray();

            if (analysisTasks.Length != 0)
                Task.WhenAll(analysisTasks).Wait();

            stopwatch.Stop();

            LogInfo($"Finished feature analysis for {analysisTasks.Length} types in {stopwatch.ElapsedMilliseconds} ms");

            LogInfo("Beginning aspect weaving");

            stopwatch.Restart();

            // Execute type weavings in parallel. The ModuleWeavingContext provides thread-safe imports.
            // Weaving does not require any other module-level changes.

            // Tasks are only created when there is actual work to be done for a type.
            Task[] weaveTasks = types.Select(CreateWeaveAction)
                                     .Where(a => a != null)
                                     .Select(Task.Run)
                                     .ToArray();

            if (weaveTasks.Length != 0)
                Task.WhenAll(weaveTasks).Wait();

            stopwatch.Stop();

            LogInfo($"Finished aspect weaving for {weaveTasks.Length} types in {stopwatch.ElapsedMilliseconds} ms.");
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
                            TypeDefinition atype = a.AttributeType.Resolve();

                            if (IsAspectAttribute(atype, _mwc.Spinner.IMethodBoundaryAspect))
                            {
                                Debug.Assert(method.HasBody);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) method, atype, 0));

                                LogDebug($"Found aspect {atype.Name} for {method}");
                            }
                            else if (IsAspectAttribute(atype, _mwc.Spinner.IMethodInterceptionAspect))
                            {
                                Debug.Assert(method.HasBody);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) method, atype, 1));

                                LogDebug($"Found aspect {atype.Name} for {method}");
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
                            TypeDefinition atype = a.AttributeType.Resolve();

                            if (IsAspectAttribute(atype, _mwc.Spinner.IPropertyInterceptionAspect))
                            {
                                Debug.Assert(property.GetMethod != null || property.SetMethod != null);

                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) property, atype, 2));

                                LogDebug($"Found aspect {atype.Name} for {property}");
                            }
                        }
                    }
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition xevent in type.Events)
                {
                    if (xevent.HasCustomAttributes)
                    {
                        foreach (CustomAttribute a in xevent.CustomAttributes)
                        {
                            TypeDefinition atype = a.AttributeType.Resolve();

                            if (IsAspectAttribute(atype, _mwc.Spinner.IEventInterceptionAspect))
                            {
                                if (aspects == null)
                                    aspects = new List<Tuple<IMemberDefinition, TypeDefinition, int>>();
                                aspects.Add(Tuple.Create((IMemberDefinition) xevent, atype, 3));

                                LogDebug($"Found aspect {atype.Name} for {xevent}");
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

                    try
                    {
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
                            case 3:
                                EventInterceptionAspectWeaver.Weave(_mwc,
                                                                    (EventDefinition) a.Item1,
                                                                    a.Item2,
                                                                    aspectIndex);
                                break;

                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Exception while weaving aspect {a.Item2.Name} for member {a.Item1}: {ex.GetType().Name}: {ex.Message}");
                        LogError(ex.StackTrace);
                    }
                }
            };

            return taskAction;
        }

        private Action CreateAnalysisAction(TypeDefinition type)
        {
            return () =>
            {
                try
                {
                    AspectFeatureAnalyzer.Analyze(_mwc, type);
                }
                catch (Exception ex)
                {
                    LogError($"Exception while analyzing featores of type {type.Name}: {ex.GetType().Name}: {ex.Message}");
                    LogError(ex.StackTrace);
                }
            };
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
