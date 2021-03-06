using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;
using Mono.Collections.Generic;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody
{
    internal static class ReferenceExtensions
    {
        internal static GenericParameter Clone(this GenericParameter genericParameter, MethodDefinition newOwner)
        {
            return new GenericParameter(newOwner)
            {
                Name = genericParameter.Name,
                Attributes = genericParameter.Attributes
            };
        }

        internal static MethodReference WithGenericDeclaringType(this MethodReference self, GenericInstanceType type)
        {
            Debug.Assert(type.Module == self.Module);

            var reference = new MethodReference(self.Name, self.ReturnType, type)
            {
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention
            };

            if (self.HasParameters)
                foreach (ParameterDefinition parameter in self.Parameters)
                    reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            if (self.HasGenericParameters)
                foreach (GenericParameter genericParam in self.GenericParameters)
                    reference.GenericParameters.Add(new GenericParameter(genericParam.Name, reference));

            return reference;
        }

        internal static FieldReference WithGenericDeclaringType(this FieldReference self, GenericInstanceType type)
        {
            Debug.Assert(type.Module == self.Module);

            return new FieldReference(self.Name, self.FieldType, type);
        }

        internal static IEnumerable<MethodDefinition> GetInheritedMethods(this TypeDefinition type)
        {
            TypeDefinition current = type;
            while (current != null)
            {
                foreach (MethodDefinition m in current.Methods)
                    yield return m;

                current = current.BaseType?.Resolve();
            }
        }

        /// <summary>
        /// Does a fast check if two references are for the same definition.
        /// </summary>
        internal static bool IsSame(this TypeReference self, TypeReference other)
        {
            if (ReferenceEquals(self, other))
                return true;

            if (self.Name != other.Name || self.Namespace != other.Namespace)
                return false;

            if (self.HasGenericParameters)
            {
                if (!other.HasGenericParameters)
                    return false;

                if (self.GenericParameters.Count != other.GenericParameters.Count)
                    return false;
            }
            else if (other.HasGenericParameters)
            {
                return false;
            }

            Debug.Assert(self.Resolve() == other.Resolve());
            return true;
        }

        /// <summary>
        /// Does a fast check if two references are for the same definition.
        /// </summary>
        internal static bool IsSame(this FieldReference self, FieldReference other)
        {
            if (ReferenceEquals(self, other))
                return true;

            if (self.Name != other.Name)
                return false;

            if (!self.FieldType.IsSame(other.FieldType))
                return false;

            if (!self.DeclaringType.IsSame(other.DeclaringType))
                return false;

            Debug.Assert(self.Resolve() == other.Resolve());
            return true;
        }

        /// <summary>
        /// Shortcut for MetadataResolver.GetMethod() to get a matching method reference.
        /// </summary>
        internal static MethodDefinition GetMethod(this TypeDefinition self, MethodReference reference, bool inherited)
        {
            if (!inherited)
                return self.HasMethods ? MetadataResolver.GetMethod(self.Methods, reference) : null;

            TypeDefinition current = self;
            while (current != null)
            {
                MethodDefinition result;
                if (current.HasMethods && (result = MetadataResolver.GetMethod(current.Methods, reference)) != null)
                    return result;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        internal static MethodDefinition GetConstructor(this TypeDefinition self, int argc)
        {
            if (self.HasMethods)
            {
                for (int i = 0; i < self.Methods.Count; i++)
                {
                    MethodDefinition m = self.Methods[i];

                    if (argc == (m.HasParameters ? m.Parameters.Count : 0) && !m.IsStatic && m.IsConstructor)
                        return m;
                }
            }

            return null;
        }

        internal static MethodDefinition GetConstructor(this TypeDefinition self, IList<TypeReference> paramTypes)
        {
            if (self.HasMethods)
            {
                for (int i = 0; i < self.Methods.Count; i++)
                {
                    MethodDefinition m = self.Methods[i];

                    if (paramTypes.Count != (m.HasParameters ? m.Parameters.Count : 0) || m.IsStatic || !m.IsConstructor)
                        continue;

                    bool typeMatch = true;
                    for (int t = 0; t < paramTypes.Count; t++)
                    {
                        if (!m.Parameters[t].ParameterType.IsSame(paramTypes[t]))
                        {
                            typeMatch = false;
                            break;
                        }
                    }

                    if (!typeMatch)
                        continue;

                    return m;
                }
            }

            return null;
        }

        internal static FieldDefinition GetField(this TypeDefinition self, string name, bool inherited)
        {
            TypeDefinition current = self;
            while (current != null)
            {
                if (current.HasFields)
                {
                    for (int i = 0; i < current.Fields.Count; i++)
                    {
                        if (current.Fields[i].Name == name)
                            return current.Fields[i];
                    }
                }

                if (!inherited)
                    break;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        internal static PropertyDefinition GetProperty(this TypeDefinition self, string name, bool inherited)
        {
            TypeDefinition current = self;
            while (current != null)
            {
                if (current.HasProperties)
                {
                    for (int i = 0; i < current.Properties.Count; i++)
                    {
                        if (current.Properties[i].Name == name)
                            return current.Properties[i];
                    }
                }

                if (!inherited)
                    break;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        internal static EventDefinition GetEvent(this TypeDefinition self, string name, bool inherited)
        {
            TypeDefinition current = self;
            while (current != null)
            {
                if (current.HasEvents)
                {
                    for (int i = 0; i < current.Events.Count; i++)
                    {
                        if (current.Events[i].Name == name)
                            return current.Events[i];
                    }
                }

                if (!inherited)
                    break;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        internal static IMemberDefinition Resolve(this MemberReference self)
        {
            switch (self.GetProviderType())
            {
                case ProviderType.Type:
                    return ((TypeReference) self).Resolve();
                case ProviderType.Method:
                    return ((MethodReference) self).Resolve();
                case ProviderType.Property:
                    return ((PropertyReference) self).Resolve();
                case ProviderType.Event:
                    return ((EventReference) self).Resolve();
                case ProviderType.Field:
                    return ((FieldReference) self).Resolve();
                default:
                    throw new ArgumentOutOfRangeException(nameof(self));
            }
        }

        /// <summary>
        /// Check if this type or one of its bases implements an interface. Does not check the interface's own inheritance.
        /// </summary>
        internal static bool HasInterface(this TypeDefinition self, TypeReference interfaceType, bool inherited)
        {
            TypeDefinition current = self;
            while (current != null)
            {
                if (current.HasInterfaces)
                {
                    for (int i = 0; i < current.Interfaces.Count; i++)
                    {
                        if (current.Interfaces[i].IsSame(interfaceType))
                            return true;
                    }
                }

                if (!inherited)
                    break;

                current = current.BaseType?.Resolve();
            }

            return false;
        }

        //internal static bool IsSame(this MethodReference self, MethodReference other)
        //{
        //    if (ReferenceEquals(self, other))
        //        return true;

        //    if (self.Name != other.Name)
        //        return false;

        //    if (!IsSame(self.DeclaringType, other.DeclaringType))
        //        return false;

        //    if (self.HasThis != other.HasThis)
        //        return false;

        //    if (self.HasParameters != other.HasParameters)
        //        return false;

        //    if (self.HasGenericParameters != other.HasGenericParameters)
        //        return false;

        //    if (self.HasParameters && (self.Parameters.Count != other.Parameters.Count))
        //        return false;

        //    if (self.HasGenericParameters && (self.GenericParameters.Count != other.GenericParameters.Count))
        //        return false;

        //    if (!IsSame(self.ReturnType, other.ReturnType))
        //        return false;

        //    for (int i = 0; i < self.Parameters.Count; i++)
        //    {
        //        if (!self.Parameters[i].ParameterType.IsSame(other.Parameters[i].ParameterType))
        //            return false;
        //    }

        //    return true;
        //}

        internal static CustomAttributeArgument? GetNamedArgument(this CustomAttribute self, string name)
        {
            if (self.HasProperties)
            {
                foreach (CustomAttributeNamedArgument p in self.Properties)
                {
                    if (p.Name == name)
                        return p.Argument;
                }
            }

            if (self.HasFields)
            {
                foreach (CustomAttributeNamedArgument f in self.Fields)
                {
                    if (f.Name == name)
                        return f.Argument;
                }
            }

            return null;
        }

        internal static object GetArgumentValue(this CustomAttribute self, int index)
        {
            return self.HasConstructorArguments && index < self.ConstructorArguments.Count
                ? self.ConstructorArguments[index].Value
                : null;
        }

        internal static object GetNamedArgumentValue(this CustomAttribute self, string name)
        {
            return self.GetNamedArgument(name)?.Value;
        }

        internal static IEnumerable<IMemberDefinition> GetMembers(this TypeDefinition self)
        {
            if (self.HasFields)
            {
                foreach (FieldDefinition field in self.Fields)
                    yield return field;
            }

            if (self.HasEvents)
            {
                foreach (EventDefinition evt in self.Events)
                    yield return evt;
            }

            if (self.HasProperties)
            {
                foreach (PropertyDefinition prop in self.Properties)
                    yield return prop;
            }

            if (self.HasMethods)
            {
                foreach (MethodDefinition method in self.Methods)
                    yield return method;
            }

            if (self.HasNestedTypes)
            {
                foreach (TypeDefinition type in self.NestedTypes)
                    yield return type;
            }
        }

        internal static MethodDefinition GetMethodDefinition(this ParameterDefinition p)
        {
            return (MethodDefinition) p.Method;
        }

        internal static MethodDefinition GetMethodDefinition(this MethodReturnType r)
        {
            return (MethodDefinition) r.Method;
        }

        internal static bool IsReturnVoid(this MethodReference method)
        {
            return method.ReturnType.IsSame(method.Module.TypeSystem.Void);
        }
    }
}