﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace Ramp.Aspects.Fody.Tests
{
    public class WeaverTests
    {
        private const string RelativeProjectpath = @"..\..\..\Ramp.Aspects.Fody.TestTarget\Ramp.Aspects.Fody.TestTarget.csproj";
        private const string RelativeAssemblyPath = @"bin\Debug\Ramp.Aspects.Fody.TestTarget.dll";
        private const string TestClassName = "Ramp.Aspects.Fody.TestTarget.TestClass";
        private const string PeVerifyName = "PEVerify.exe";

        private static readonly string[] PeVerifyDirectories =
        {
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6.1 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v8.1A\bin\NETFX 4.5.1 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v8.0A\bin\NETFX 4.0 Tools"
        };

        private readonly ITestOutputHelper _output;

        public WeaverTests(ITestOutputHelper output)
        {
            _output = output;
            Console.SetOut(new OutputRedirector(output));
        }

        private Assembly WeaveAndLoadAssembly()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, RelativeProjectpath));

            string assemblyPath = Path.Combine(Path.GetDirectoryName(projectPath), RelativeAssemblyPath);

            string pdbPath = assemblyPath.Replace(".dll", ".pdb");

#if !DEBUG
            assemblyPath = assemblyPath.Replace("Debug", "Release");
            pdbPath = pdbPath.Replace("Debug", "Release");
#endif
            string newAssemblyPath = assemblyPath.Replace(".dll", ".w.dll");
            //string newPdbPath = pdbPath.Replace(".pdb", ".w.pdb");

            File.Copy(assemblyPath, newAssemblyPath, true);
            File.SetLastWriteTimeUtc(newAssemblyPath, DateTime.UtcNow);
            //File.Copy(pdbPath, newPdbPath, true);

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(newAssemblyPath));
            ModuleDefinition md = ModuleDefinition.ReadModule(newAssemblyPath, new ReaderParameters { AssemblyResolver = resolver});
            var weaver = new ModuleWeaver
            {
                ModuleDefinition = md
            };

            weaver.Execute();

            //md.Name = md.Name.Replace(".dll", ".w.dll");

            md.Write(newAssemblyPath, new WriterParameters { WriteSymbols = false });

            _output.WriteLine("Wrote: " + newAssemblyPath);

            return Assembly.LoadFile(newAssemblyPath);
        }

        private void InvokeRunMethod(Assembly testAssembly)
        {
            Type testClass = testAssembly.GetType(TestClassName);

            MethodInfo runMethod = testClass.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            runMethod.Invoke(null, new object[0]);
        }

        private void RunPeVerify(string path, int exitCode)
        {
            string filename = null;

            foreach (string dir in PeVerifyDirectories)
            {
                filename = Environment.ExpandEnvironmentVariables(Path.Combine(dir, PeVerifyName));
                if (File.Exists(filename))
                    break;
                filename = null;
            }

            if (filename == null)
                throw new InvalidOperationException("PEVerify not found");

            var psi = new ProcessStartInfo(filename, path)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            var proc = new Process
            {
                StartInfo = psi
            };

            proc.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    _output.WriteLine(e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();

            using (proc)
            {
                if (proc.WaitForExit(10000))
                {
                    Assert.Equal(exitCode, proc.ExitCode);
                }
                else
                {
                    proc.Kill();
                    throw new TimeoutException("PEVerify timed out");
                }
            }
        }

        [Fact(DisplayName = "Weave Only")]
        public void WeaveOnly()
        {
            Assembly a = WeaveAndLoadAssembly();
            RunPeVerify(a.Location, 0);
        }

        [Fact(DisplayName = "Weave and Run", Skip = "NA")]
        public void WeaveAndRun()
        {

            Assembly a = WeaveAndLoadAssembly();

            InvokeRunMethod(a);
        }
    }
}
