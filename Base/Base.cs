// ReSharper disable UnusedMember.Global UnusedType.Global InconsistentNaming MemberCanBeProtected.Global

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RemoteRuntime.Plugin
{
    public abstract class PluginBase : MarshalByRefObject
    {
        public abstract string GetVersion();
        public abstract void Run();

        public void Entrypoint()
        {
            AppDomain.CurrentDomain.DomainUnload += (sender, args) =>
            {
                Console.WriteLine($"{GetType().Name} {GetVersion()} asked to unload, setting termination event");
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
                    throw new ApplicationException($"Could not find Runtime.dll, please provide as {pathToRuntimeDll}");
                }

                pathToRuntimeDll = expectedPath;
            }

            if (Injector.Inject(pid, pathToRuntimeDll))
            {
                Console.WriteLine($"Injected runtime to {pid}");
            }

            var client = MessageClient.CreateClient(pid);
            Console.WriteLine($"Connected to {pid}...");
            client.Send(new LoadAndRunRequest(path, entrypoint));
            Console.WriteLine("Waiting for termination...");
            var outcome = client.Receive() as StatusWithError;
            Console.WriteLine($"Status: {outcome.Success} {outcome.Error}");
        }
    }
}
