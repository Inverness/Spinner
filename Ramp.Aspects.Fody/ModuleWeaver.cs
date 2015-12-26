using System;
using Mono.Cecil;

namespace Ramp.Aspects.Fody
{
    public class ModuleWeaver
    {
        public ModuleDefinition ModuleDefinition { get; set; }

        public Action<string> LogDebug { get; set; }
        
        public Action<string> LogInfo { get; set; }
        
        public Action<string> LogWarning { get; set; }
        
        public Action<string> LogError { get; set; }

        public void Execute()
        {
            LogDebug = s => { };
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };
        }
    }
}
