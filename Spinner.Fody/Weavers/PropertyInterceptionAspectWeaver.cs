using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    internal sealed class PropertyInterceptionAspectWeaver : AspectWeaver
    {
        private const string GetValueMethodName = "GetValue";
        private const string SetValueMethodName = "SetValue";

        private readonly PropertyDefinition _property;
        private MethodDefinition _originalGetter;
        private MethodDefinition _originalSetter;

        private PropertyInterceptionAspectWeaver(
            ModuleWeavingContext mwc,
            TypeDefinition aspectType,
            int aspectIndex,
            PropertyDefinition aspectTarget)
            : base(mwc, aspectType, aspectIndex, aspectTarget)
        {
            _property = aspectTarget;
            Debug.Assert(_property.GetMethod != null || _property.SetMethod != null);
        }

        protected override void Weave()
        {
            MethodDefinition getter = _property.GetMethod;
            MethodDefinition setter = _property.SetMethod;

            _originalGetter = getter != null ? DuplicateOriginalMethod(getter) : null;
            _originalSetter = setter != null ? DuplicateOriginalMethod(setter) : null;
            
            CreatePropertyBindingClass();

            CreateAspectCacheField();

            if (getter != null)
                WeaveMethod(getter);
            if (setter != null)
                WeaveMethod(setter);
        }

        internal static void Weave(ModuleWeavingContext mwc, PropertyDefinition property, TypeDefinition aspect, int index)
        {
            new PropertyInterceptionAspectWeaver(mwc, aspect, index, property).Weave();
        }

        private void WeaveMethod(MethodDefinition method)
        {
            Debug.Assert(method.IsGetter || method.IsSetter);

            // Clear the target method body as it needs entirely new code
            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            Collection<Ins> insc = method.Body.Instructions;

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(method, insc.Count, out argumentsVariable);

            WriteCopyArgumentsToContainer(method, insc.Count, argumentsVariable, true);
            
            WriteAspectInit(method, insc.Count);

            WriteBindingInit(method, insc.Count);
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WritePiaInit(method, insc.Count, argumentsVariable, out iaVariable, out valueField);
            
            if (_aspectFeatures.Has(Features.MemberInfo))
                WriteSetPropertyInfo(method, insc.Count, iaVariable);
            
            if (method.IsSetter)
            {
                Debug.Assert(method.Parameters.Count >= 1);

                insc.Add(Ins.Create(OpCodes.Ldloc, iaVariable));
                insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters.Last()));
                insc.Add(Ins.Create(OpCodes.Stfld, valueField));
            }
            
            MethodReference adviceBase = method.IsGetter
                ? _mwc.Spinner.IPropertyInterceptionAspect_OnGetValue
                : _mwc.Spinner.IPropertyInterceptionAspect_OnSetValue;

            WriteCallAdvice(method, insc.Count, adviceBase, iaVariable);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(method, insc.Count, argumentsVariable, false, true);
            
            if (method.IsGetter)
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, iaVariable));
                insc.Add(Ins.Create(OpCodes.Ldfld, valueField));
            }

            insc.Add(Ins.Create(OpCodes.Ret));

            // Fix labels and optimize
            
            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        private void WriteSetPropertyInfo(
            MethodDefinition method,
            int offset,
            VariableDefinition piaVariable)
        {
            MethodReference getTypeFromHandle = _mwc.SafeImport(_mwc.Framework.Type_GetTypeFromHandle);
            MethodReference setProperty = _mwc.SafeImport(_mwc.Spinner.PropertyInterceptionArgs_Property.SetMethod);
            MethodReference getPropertyInfo = _mwc.SafeImport(_mwc.Spinner.WeaverHelpers_GetPropertyInfo);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldloc, piaVariable),
                Ins.Create(OpCodes.Ldtoken, _property.DeclaringType),
                Ins.Create(OpCodes.Call, getTypeFromHandle),
                Ins.Create(OpCodes.Ldstr, _property.Name),
                Ins.Create(OpCodes.Call, getPropertyInfo),
                Ins.Create(OpCodes.Callvirt, setProperty)
            };

            method.Body.InsertInstructions(offset, insc);
        }

        private void CreatePropertyBindingClass()
        {
            ModuleDefinition module = _property.Module;

            string name = NameGenerator.MakePropertyBindingName(_property.Name, _aspectIndex);
            TypeReference baseType = _mwc.SafeImport(_mwc.Spinner.PropertyBindingT1).MakeGenericInstanceType(_property.PropertyType);

            CreateBindingClass(baseType, name);

            // Override the GetValue method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(GetValueMethodName, mattrs, _property.PropertyType);

                _bindingClass.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType = _mwc.SafeImport(_mwc.Spinner.Arguments);

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, argumentsBaseType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (_property.GetMethod != null)
                {
                    GenericInstanceType argumentContainerType;
                    FieldReference[] argumentContainerFields;
                    GetArgumentContainerInfo(_property.GetMethod,
                                             out argumentContainerType,
                                             out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (_property.GetMethod.Parameters.Count != 0)
                    {
                        argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!_property.GetMethod.IsStatic)
                    {
                        // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                        bil.Emit(OpCodes.Ldarg_1);
                        bil.Emit(OpCodes.Ldind_Ref);
                        bil.Emit(_property.GetMethod.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 _property.GetMethod.DeclaringType);
                    }

                    // Load arguments or addresses directly from the arguments container
                    for (int i = 0; i < _property.GetMethod.Parameters.Count; i++)
                    {
                        bool byRef = _property.GetMethod.Parameters[i].ParameterType.IsByReference;

                        bil.Emit(OpCodes.Ldloc, argsContainer);
                        bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
                    }

                    if (_property.GetMethod.IsStatic || _property.GetMethod.DeclaringType.IsValueType)
                        bil.Emit(OpCodes.Call, _originalGetter);
                    else
                        bil.Emit(OpCodes.Callvirt, _originalGetter);
                }
                else
                {
                    if (_property.PropertyType.IsValueType)
                    {
                        VariableDefinition returnVar = bil.Body.AddVariableDefinition(_property.PropertyType);

                        bil.Emit(OpCodes.Ldloca, returnVar);
                        bil.Emit(OpCodes.Initobj, _property.PropertyType);
                        bil.Emit(OpCodes.Ldloc, returnVar);
                    }
                    else
                    {
                        bil.Emit(OpCodes.Ldnull);
                    }
                }

                bil.Emit(OpCodes.Ret);
            }

            // Override the SetValue Method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(SetValueMethodName, mattrs, module.TypeSystem.Void);

                _bindingClass.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType = _mwc.SafeImport(_mwc.Spinner.Arguments);

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, argumentsBaseType));
                bmethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, _property.PropertyType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (_property.SetMethod != null)
                {
                    Debug.Assert(_property.SetMethod.Parameters.Count >= 1);

                    GenericInstanceType argumentContainerType;
                    FieldReference[] argumentContainerFields;
                    GetArgumentContainerInfo(_property.SetMethod,
                                             out argumentContainerType,
                                             out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (_property.SetMethod.Parameters.Count != 1)
                    {
                        argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!_property.SetMethod.IsStatic)
                    {
                        // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                        bil.Emit(OpCodes.Ldarg_1);
                        bil.Emit(OpCodes.Ldind_Ref);
                        bil.Emit(_property.SetMethod.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 _property.SetMethod.DeclaringType);
                    }

                    // Load arguments or addresses directly from the arguments container
                    for (int i = 0; i < _property.SetMethod.Parameters.Count - 1; i++)
                    {
                        bool byRef = _property.SetMethod.Parameters[i].ParameterType.IsByReference;

                        bil.Emit(OpCodes.Ldloc, argsContainer);
                        bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
                    }

                    // Load new property value
                    bil.Emit(OpCodes.Ldarg_3);

                    if (_property.SetMethod.IsStatic || _property.SetMethod.DeclaringType.IsValueType)
                        bil.Emit(OpCodes.Call, _originalSetter);
                    else
                        bil.Emit(OpCodes.Callvirt, _originalSetter);
                }

                bil.Emit(OpCodes.Ret);
            }
        }

        /// <summary>
        /// Writes the PropertyInterceptionArgs initialization.
        /// </summary>
        private void WritePiaInit(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            out VariableDefinition iaVariable,
            out FieldReference valueField)
        {
            TypeDefinition piaTypeDef = _mwc.Spinner.BoundPropertyInterceptionArgsT1;
            GenericInstanceType genericPiaType = _mwc.SafeImport(piaTypeDef).MakeGenericInstanceType(_property.PropertyType);
            TypeReference piaType = genericPiaType;

            MethodDefinition constructorDef = _mwc.Spinner.BoundPropertyInterceptionArgsT1_ctor;
            MethodReference constructor = _mwc.SafeImport(constructorDef).WithGenericDeclaringType(genericPiaType);

            FieldDefinition valueFieldDef = _mwc.Spinner.BoundPropertyInterceptionArgsT1_TypedValue;
            valueField = _mwc.SafeImport(valueFieldDef).WithGenericDeclaringType(genericPiaType);

            iaVariable = method.Body.AddVariableDefinition(piaType);

            var insc = new Collection<Ins>();

            if (method.IsStatic)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                if (method.DeclaringType.IsValueType)
                {
                    insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
                    insc.Add(Ins.Create(OpCodes.Box, method.DeclaringType));
                }
            }

            insc.Add(argumentsVariable == null
                ? Ins.Create(OpCodes.Ldnull)
                : Ins.Create(OpCodes.Ldloc, argumentsVariable));

            insc.Add(Ins.Create(OpCodes.Ldsfld, _bindingInstanceField));

            insc.Add(Ins.Create(OpCodes.Newobj, constructor));
            insc.Add(Ins.Create(OpCodes.Stloc, iaVariable));

            method.Body.InsertInstructions(offset, insc);
        }
    }
}