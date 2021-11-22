// ReSharper disable UnusedMember.Global UnusedType.Global InconsistentNaming MemberCanBeProtected.Global

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace RemoteRuntime.Plugin
{
    public abstract class PluginBase : MarshalByRefObject
    {
        public abstract void Run();

        private ResolveEventHandler MakeAssemblyResolver(string stage, string directory)
        {
            return (sender, args) =>
            {
                string assemblyName = args.Name.Split('.')[0].Trim();
                Console.WriteLine($"==== Internal {stage} loading {assemblyName}");
                string[] dirs = { directory, RuntimeEnvironment.GetRuntimeDirectory() };
                string[] exts = { ".exe", ".dll" };
                foreach (string dir in dirs)
                {
                    foreach (string ext in exts)
                    {
                        string filename = assemblyName + ext;
                        string path = Path.Combine(dir, filename);
                        if (File.Exists(path))
                        {
                            return Assembly.LoadFile(path);
                        }
                    }
                }

                Console.WriteLine($"==== Internal {stage} loading {assemblyName} ----- NOT FOUND");
                return Assembly.Load(assemblyName);
            };
        }

        public void Entrypoint()
        {
            Assembly myAssembly = Assembly.GetAssembly(typeof(PluginBase)) ?? Assembly.GetExecutingAssembly();
            if (myAssembly == null)
            {
                throw new ApplicationException("Could not find own assembly?");
            }

            AppDomain.CurrentDomain.DomainUnload += (sender, args) =>
            {
                Console.WriteLine($"{GetType().Name} asked to unload, setting termination event");
                Terminate.Set();
            };
            Run();
        }

        protected readonly ManualResetEvent Terminate = new(false);

        protected static void Inject(
            int pid, string pathToRuntimeDll = null, string assemblyPath = null, Type entrypointType = null
        )
        {
            string path = assemblyPath ?? Assembly.GetEntryAssembly()?.Location;
            Type entrypoint = entrypointType ?? Assembly.GetEntryAssembly()?.EntryPoint.DeclaringType;
            if (path == null || !File.Exists(path) || entrypoint == null)
            {
                throw new ApplicationException($"Could not find own assembly? {path}");
            }

            if (pathToRuntimeDll == null)
            {
                string expectedPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(path), @"..\..\..\..\..\Runtime\bin\x64\Release\Runtime.dll")
                );
                if (!File.Exists(expectedPath))
                {
                    throw new ApplicationException(
                        $"Could not find Runtime.dll, please provide as {nameof(pathToRuntimeDll)} argument"
                    );
                }

                pathToRuntimeDll = expectedPath;
            }

            if (Injector.Inject(pid, pathToRuntimeDll))
            {
                Console.WriteLine($"Injected runtime to {pid}");
            }

            var client = MessageClient.CreateClient(pid);
            Console.WriteLine($"Connected to {pid}... asking to run {path} {entrypoint}");
            client.Send(new LoadAndRunRequest(path, entrypoint));
            Console.WriteLine("Waiting for termination...");
            var outcome = client.Receive() as StatusWithError;
            Console.WriteLine($"Status: {outcome.Success} {outcome.Error}");
        }
    }
}
