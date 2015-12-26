using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Xunit;

namespace Ramp.Aspects.Fody.Tests
{
    public class WeaverTests
    {
        private const string RelativeProjectpath = @"..\..\..\Ramp.Aspects.Fody.Tests\Ramp.Aspects.Fody.Tests.csproj";
        private const string RelativeAssemblyPath = @"bin\Debug\Ramp.Aspects.Fody.TestTarget.dll";
        private const string TestClassName = "Ramp.Aspects.Fody.TestTarget.TestClass";

        private Assembly Setup()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, RelativeProjectpath));

            string assemblyPath = Path.Combine(Path.GetDirectoryName(projectPath), RelativeAssemblyPath);

            string pdbPath = assemblyPath.Replace(".dll", ".pdb");

#if !DEBUG
            assemblyPath = assemblyPath.Replace("Debug", "Release");
            pdbPath = pdbPath.Replace("Debug", "Release");
#endif
            string newAssemblyPath = assemblyPath.Replace(".dll", ".w.dll");
            string newPdbPath = pdbPath.Replace(".pdb", ".w.pdb");
            File.Copy(assemblyPath, newAssemblyPath, true);
            File.Copy(pdbPath, newPdbPath, true);

            ModuleDefinition md = ModuleDefinition.ReadModule(newAssemblyPath);
            var weaver = new ModuleWeaver
            {
                ModuleDefinition = md
            };

            weaver.Execute();

            md.Write(newAssemblyPath, new WriterParameters { WriteSymbols = true });

            return Assembly.LoadFile(newAssemblyPath);
        }

        private void InvokeRunMethod(Assembly testAssembly)
        {
            Type testClass = testAssembly.GetType(TestClassName);

            MethodInfo runMethod = testClass.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            runMethod.Invoke(null, new object[0]);
        }

        [Fact]
        public void InitialTest()
        {
            Assembly a = Setup();

            InvokeRunMethod(a);
        }
    }
}
