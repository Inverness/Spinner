using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace Spinner.Fody.Execution
{
    internal class BuildTimeExecutionEngine
    {
        private readonly ModuleWeavingContext _context;
        private readonly object _loadLock = new object();
        private AppDomain _guestAppDomain;
        private BuildTimeExecutionGuest _guest;

        public BuildTimeExecutionEngine(ModuleWeavingContext context)
        {
            _context = context;
        }

        public MemberReference[] ExecuteMethodPointcut(MethodReference method, TypeReference appliedType)
        {
            MethodDefinition methodDef = method.Resolve();
            TypeDefinition appliedTypeDef = appliedType.Resolve();

            string assemblyName = methodDef.Module.Assembly.FullName;
            string typeName = methodDef.DeclaringType.FullName;
            string methodName = methodDef.Name;

            string appliedAssemblyName = appliedTypeDef.Module.Assembly.FullName;
            string appliedTypeName = appliedTypeDef.FullName;

            PrepareGuest();

            MemberInfo[] members = _guest.ExecuteMethodPointcut(assemblyName,
                                                                typeName,
                                                                methodName,
                                                                appliedAssemblyName,
                                                                appliedTypeName);

            return members.Select(ToReference).ToArray();
        }

        public void Shutdown()
        {
            if (_guestAppDomain != null)
            {
                _guest = null;
                AppDomain.Unload(_guestAppDomain);
                _guestAppDomain = null;
            }
        }

        private void PrepareGuest()
        {
            if (_guestAppDomain != null)
                return;

            lock (_loadLock)
            {
                if (_guestAppDomain != null)
                    return;

                string guestAssemblyName = _context.Module.Assembly.FullName;

                _guestAppDomain = AppDomain.CreateDomain("BuildTimeExecution");
                _guestAppDomain.Load(new AssemblyName(guestAssemblyName));

                _guest = (BuildTimeExecutionGuest) _guestAppDomain.CreateInstanceAndUnwrap(
                    typeof(BuildTimeExecutionGuest).Assembly.GetName().Name,
                    typeof(BuildTimeExecutionGuest).FullName);
            }
        }

        private MemberReference ToReference(MemberInfo memberInfo)
        {
            TypeInfo typeInfo;
            if ((typeInfo = memberInfo as TypeInfo) != null)
                return _context.Module.Import(typeInfo);

            MethodInfo methodInfo;
            if ((methodInfo = memberInfo as MethodInfo) != null)
                return _context.Module.Import(methodInfo);

            FieldInfo fieldInfo;
            if ((fieldInfo = memberInfo as FieldInfo) != null)
                return _context.Module.Import(fieldInfo);

            PropertyInfo propertyInfo;
            if ((propertyInfo = memberInfo as PropertyInfo) != null)
            {
                TypeDefinition declaringType = _context.Module.Import(propertyInfo.DeclaringType).Resolve();
                return declaringType.GetProperty(propertyInfo.Name, false);
            }

            EventInfo eventInfo;
            if ((eventInfo = memberInfo as EventInfo) != null)
            {
                TypeDefinition declaringType = _context.Module.Import(eventInfo.DeclaringType).Resolve();
                return declaringType.GetEvent(eventInfo.Name, false);
            }

            throw new ArgumentOutOfRangeException(nameof(memberInfo));
        }
    }
}
