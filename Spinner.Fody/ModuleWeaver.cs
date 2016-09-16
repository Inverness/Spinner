#define WITH_THREADING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using NLog;
using NLog.Config;
using Spinner.Fody.Analysis;
using Spinner.Fody.Multicasting;
using Spinner.Fody.Utilities;
using Spinner.Fody.Weaving;

namespace Spinner.Fody
{
    public class ModuleWeaver
    {
        private static readonly Logger s_log = LogManager.GetCurrentClassLogger();

        public ModuleDefinition ModuleDefinition { get; set; }

        public Action<string> LogDebug { get; set; }
        
        public Action<string> LogInfo { get; set; }
        
        public Action<string> LogWarning { get; set; }
        
        public Action<string> LogError { get; set; }

        // Definition for IMethodInterceptionAspect
        private ModuleWeavingContext _mwc;
        private MulticastAttributeRegistry _multicastAttributeRegistry;

        public ModuleWeaver()
        {
            LogDebug = s => { };
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };
        }

        public void Execute()
        {
            //
            // Initialize logging from NLog to Fody.
            //

            LogLevel minLevel =
#if TRACE
                LogLevel.Trace;
#elif DEBUG
                LogLevel.Debug;
#else
                LogLevel.Info;
#endif
            var fodyTarget = new FodyLogTarget(LogError, LogWarning, LogInfo, LogDebug)
            {
                Layout = "${level:uppercase=true} [${threadid}] ${logger} - ${message}",
                Name = "fody"
            };

            if (LogManager.Configuration == null)
                LogManager.Configuration = new LoggingConfiguration();
            LogManager.Configuration.AddTarget(fodyTarget);
            LogManager.Configuration.AddRule(minLevel, LogLevel.Fatal, fodyTarget);
            LogManager.ReconfigExistingLoggers();
            
            s_log.Info("---- Beginning aspect weaving for: {0} ----", ModuleDefinition.Assembly.FullName);

            //
            // Initialize the module weaving context, which contains global state and services for the
            // analyzers, multicast engine, and weavers
            //

            AssemblyNameReference spinnerName = ModuleDefinition.AssemblyReferences.FirstOrDefault(a => a.Name == "Spinner");

            if (spinnerName == null)
            {
                s_log.Warn("No reference to Spinner assembly detected. Doing nothing.");
                return;
            }

            _mwc = new ModuleWeavingContext(ModuleDefinition,
                                            ModuleDefinition.AssemblyResolver.Resolve(spinnerName).MainModule);

            List<TypeDefinition> types = ModuleDefinition.GetAllTypes().ToList();

            //
            // Create the multicast attribute registry. This will identify all multicast attributes in the current
            // module and any referenced modules and cast them onto their target objects. This is observational
            // only and does not edit any modules.
            //

            s_log.Info("Beginning attribute multicasting...");

            var stopwatch = Stopwatch.StartNew();

            _multicastAttributeRegistry = MulticastAttributeRegistry.Create(_mwc);

            stopwatch.Stop();

            s_log.Info("Finished attribute multicasting in {0} ms", stopwatch.ElapsedMilliseconds);

            //
            // Analyze aspect types in the current module to identify what features of an aspect they use.
            // Feature information allows the apect weavers to optimize out code that wont be used. Attributes will
            // be added to relevant methods and types. This executes in parallel for each type.
            //

            s_log.Info("Beginning aspect feature analysis...");

            stopwatch.Restart();

            var analysisLocks = new LockTargetProvider<TypeDefinition>();

            Task[] analysisTasks = types.Select(t => CreateAnalysisAction(t, analysisLocks))
                                        .Where(a => a != null)
                                        .Select(RunTask)
                                        .ToArray();

            if (analysisTasks.Length != 0)
                Task.WhenAll(analysisTasks).Wait();

            stopwatch.Stop();

            s_log.Info("Finished feature analysis for {0} types in {1} ms", analysisTasks.Length, stopwatch.ElapsedMilliseconds);

            s_log.Info("Beginning aspect weaving...");

            stopwatch.Restart();

            //
            // Weave aspects for types in the current module. This executes in parallel for each type.
            //
            
            Task[] weaveTasks = types.Select(CreateWeaveAction)
                                     .Where(a => a != null)
                                     .Select(RunTask)
                                     .ToArray();

            if (weaveTasks.Length != 0)
                Task.WhenAll(weaveTasks).Wait();

            stopwatch.Stop();

            s_log.Info("---- Finished aspect weaving for {0} types in {1} ms ----", weaveTasks.Length, stopwatch.ElapsedMilliseconds);

            _mwc.BuildTimeExecutionEngine.Shutdown();
        }

        private static Task RunTask(Action action)
        {
#if WITH_THREADING
            return Task.Run(action);
#else
            action();
            return Task.FromResult(true);
#endif
        }

        private Action CreateWeaveAction(TypeDefinition type)
        {
            // State machine weaving is handled by its owning method. Trying to treat state machines as their own type
            // causes threading issues with the declaring type's weaver.
            if (NameUtility.IsStateMachineName(type.Name))
                return null;

            return () =>
            {
                AspectWeaver[] weavers = AspectWeaverFactory.TryCreate(_mwc, _multicastAttributeRegistry, type);

                if (weavers != null)
                {
                    foreach (AspectWeaver w in weavers)
                        w.Weave();
                }
            };
        }

        private Action CreateAnalysisAction(TypeDefinition type, LockTargetProvider<TypeDefinition> ltp)
        {
            if (!AspectFeatureAnalyzer.IsMaybeAspect(type))
                return null;

            return () =>
            {
                try
                {
                    AspectFeatureAnalyzer.Analyze(_mwc, type, ltp);
                }
                catch (Exception ex)
                {
                    LogError($"Exception while analyzing featores of type {type.Name}: {ex.GetType().Name}: {ex.Message}");
                    LogError(ex.StackTrace);
                    throw;
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
