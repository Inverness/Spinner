using System;
using System.Collections.Generic;
using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    internal static class MetadataExtensions
    {
        private static readonly Dictionary<TokenType, ProviderType> s_providerTypes =
            new Dictionary<TokenType, ProviderType>
            {
                {TokenType.Assembly, ProviderType.Assembly},
                {TokenType.TypeDef, ProviderType.Type},
                {TokenType.Method, ProviderType.Method},
                {TokenType.Property, ProviderType.Property},
                {TokenType.Event, ProviderType.Event},
                {TokenType.Field, ProviderType.Field},
                {TokenType.Param, ProviderType.Parameter}
            };

        public static ProviderType GetProviderType(this IMetadataTokenProvider target)
        {
            if (target.MetadataToken.TokenType == TokenType.Param && target is MethodReturnType)
                return ProviderType.MethodReturn;

            return s_providerTypes[target.MetadataToken.TokenType];
        }

        public static bool IsMatch(this MulticastTargets self, MulticastTargets other)
        {
            return (self & other) != 0;
        }

        public static MulticastTargets GetMulticastTargetType(this IMetadataTokenProvider target)
        {
            switch (GetProviderType(target))
            {
                case ProviderType.Assembly:
                    return MulticastTargets.Assembly;
                case ProviderType.Type:
                    var type = (TypeDefinition) target;

                    if (type.IsInterface)
                        return MulticastTargets.Interface;

                    if (type.BaseType?.Namespace == "System")
                    {
                        switch (type.BaseType.Name)
                        {
                            case "Enum":
                                return MulticastTargets.Enum;
                            case "ValueType":
                                return MulticastTargets.Struct;
                            case "Delegate":
                                return MulticastTargets.Delegate;
                        }
                    }

                    if (type.IsClass)
                        return MulticastTargets.Class;

                    throw new ArgumentOutOfRangeException(nameof(target));
                case ProviderType.Method:
                    var method = (MethodDefinition) target;
                    if (method != null)
                    {
                        if (method.Name[0] == '.')
                        {
                            if (method.Name == ".ctor")
                                return MulticastTargets.InstanceConstructor;
                            if (method.Name == ".cctor")
                                return MulticastTargets.StaticConstructor;
                        }
                        return MulticastTargets.Method;
                    }
                    break;
                case ProviderType.Property:
                    return MulticastTargets.Property;
                case ProviderType.Event:
                    return MulticastTargets.Event;
                case ProviderType.Field:
                    return MulticastTargets.Field;
                case ProviderType.Parameter:
                    return MulticastTargets.Parameter;
                case ProviderType.MethodReturn:
                    return MulticastTargets.ReturnValue;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            throw new ArgumentOutOfRangeException(nameof(target));
        }

        public static string GetName(this IMetadataTokenProvider self)
        {
            switch (self.GetProviderType())
            {
                case ProviderType.Assembly:
                    return ((AssemblyDefinition) self).FullName;
                case ProviderType.Type:
                    return ((TypeDefinition) self).FullName;
                case ProviderType.Method:
                case ProviderType.Property:
                case ProviderType.Event:
                case ProviderType.Field:
                    return ((IMemberDefinition) self).Name;
                case ProviderType.Parameter:
                    return ((ParameterDefinition) self).Name;
                default:
                    throw new ArgumentOutOfRangeException(nameof(self));
            }
        }
    }
}
