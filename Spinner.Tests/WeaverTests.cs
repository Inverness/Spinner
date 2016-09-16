using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Spinner.Fody;
using Xunit;
using Xunit.Abstractions;

namespace Spinner.Tests
{
    public class WeaverTests
    {
        private const string RelativeProjectpath = @"..\..\..\Spinner.Tests\Spinner.Tests.csproj";
        private const string RelativeAssemblyPath = @"bin\Debug\Spinner.TestTarget.dll";
        private const string TestClassName = "Spinner.TestTarget.TestRun";
        private const string PeVerifyName = "PEVerify.exe";

        private static readonly string[] PeVerifyDirectories =
        {
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6.1 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v8.1A\bin\NETFX 4.5.1 Tools",
            @"%PROGRAMFILES(X86)%\Microsoft SDKs\Windows\v8.0A\bin\NETFX 4.0 Tools"
        };

        private readonly ITestOutputHelper _output;
        private readonly Assembly _assembly;

        public WeaverTests(ITestOutputHelper output)
        {
            _output = output;
            Console.SetOut(new OutputRedirector(output));

            _assembly = WeaveAndLoadAssembly();
        }

        private WeaverTests(string[] args, ITestOutputHelper output)
        {
            _output = output;
            _assembly = WeaveAndLoadAssembly();
        }

        public static void RunFromConsole(string[] args)
        {
            new WeaverTests(args, new ConsoleOutputHelper()).WeaveAndRun();
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
            string newPdbPath = pdbPath.Replace(".pdb", ".w.pdb");

            var readerParameters = new ReaderParameters {ReadSymbols = true};

            ModuleDefinition moduleDefinition = ModuleDefinition.ReadModule(assemblyPath, readerParameters);

            var weaver = new ModuleWeaver
            {
                ModuleDefinition = moduleDefinition,
#if DEBUG
                LogDebug = s => _output.WriteLine(s),
#endif
                LogInfo = s => _output.WriteLine(s),
                LogWarning = s => _output.WriteLine(s),
                LogError = s => _output.WriteLine(s)
            };

            weaver.Execute();

            moduleDefinition.Assembly.Name.Name = moduleDefinition.Assembly.Name.Name + ".w";
            moduleDefinition.Name = moduleDefinition.Name.Replace(".dll", ".w.dll");

            moduleDefinition.Write(newAssemblyPath, new WriterParameters { WriteSymbols = true });

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

            _output.WriteLine("---- Running PEVerify ----");

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

            _output.WriteLine("--------------------------");
        }

        [Fact(DisplayName = "Weave and Run")]
        public void WeaveAndRun()
        {
            RunPeVerify(_assembly.Location, 0);

            InvokeRunMethod(_assembly);
        }

        private class ConsoleOutputHelper : ITestOutputHelper
        {
            public void WriteLine(string message)
            {
                Console.WriteLine(message);
            }

            public void WriteLine(string format, params object[] args)
            {
                Console.WriteLine(format, args);
            }
        }
    }
}
