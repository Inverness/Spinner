using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Spinner.Fody.Execution
{
    internal class BuildTimeExecutionGuest : MarshalByRefObject
    {
        public MemberInfo[] ExecuteMethodPointcut(
            string assemblyName,
            string typeName,
            string methodName,
            string appliedAssemblyName,
            string appliedTypeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            Assembly assembly = assemblies.FirstOrDefault(a => a.FullName == assemblyName);

            TypeInfo type = assembly?.GetType(typeName)?.GetTypeInfo();

            MethodInfo tm = type?.GetMethod(methodName);

            if (tm == null)
                return null;

            Assembly appliedAssembly = assemblies.FirstOrDefault(a => a.FullName == appliedAssemblyName);

            TypeInfo appliedType = appliedAssembly?.GetType(appliedTypeName)?.GetTypeInfo();

            if (appliedType == null)
                return null;

            var result = (IEnumerable) tm.Invoke(null, new object[] { appliedType });

            return result.Cast<MemberInfo>().ToArray();
        }
    }
}