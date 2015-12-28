using System;
using System.Collections.Generic;
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
        private TypeDefinition _propertyInterceptionAspectTypeDef;

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
            ModuleDefinition alm = aspectsAssembly.MainModule;

            _methodInterceptionAspectTypeDef = alm.GetType("Ramp.Aspects.IMethodInterceptionAspect");
            _propertyInterceptionAspectTypeDef = alm.GetType("Ramp.Aspects.IPropertyInterceptionAspect");

            int aspectIndexCounter = 0;

            var typeList = new List<TypeDefinition>(ModuleDefinition.GetAllTypes());
            var methodList = new List<MethodDefinition>();
            var propertyList = new List<PropertyDefinition>();

            foreach (TypeDefinition type in typeList)
            {
                methodList.AddRange(type.Methods);

                foreach (MethodDefinition method in methodList)
                {
                    foreach (CustomAttribute a in method.CustomAttributes)
                    {
                        TypeDefinition attributeType = a.AttributeType.Resolve();
                        if (IsMethodInterceptionAspectAttribute(attributeType))
                        {
                            Debug.Assert(method.HasBody);

                            int aspectIndex = aspectIndexCounter++;

                            MethodInterceptionAspectWeaver.Weave(alm, method, attributeType, aspectIndex);
                        }
                    }
                }

                methodList.Clear();

                propertyList.AddRange(type.Properties);

                foreach (PropertyDefinition property in propertyList)
                {
                    foreach (CustomAttribute a in property.CustomAttributes)
                    {
                        TypeDefinition attributeType = a.AttributeType.Resolve();
                        if (IsPropertyInterceptionAspectAttribute(attributeType))
                        {
                            Debug.Assert(property.GetMethod != null || property.SetMethod != null);

                            int aspectIndex = aspectIndexCounter++;

                            PropertyInterceptionAspectWeaver.Weave(alm, property, attributeType, aspectIndex);
                        }
                    }
                }

                propertyList.Clear();
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
