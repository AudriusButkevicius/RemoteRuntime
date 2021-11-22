﻿// ReSharper disable UnusedMember.Global UnusedType.Global InconsistentNaming MemberCanBeProtected.Global

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteRuntime.Plugin
{
    public abstract class PluginBase : MarshalByRefObject
    {
        protected readonly ManualResetEvent Terminate = new(false);

        public abstract void Run();

        public void Stop()
        {
            Console.WriteLine($"{GetType().Name} asked to unload, setting termination event");
            Terminate.Set();
        }


        protected static void Inject(int pid, string assemblyPath = null, string pathToRuntimeDll = null)
        {
            string path = assemblyPath ?? Assembly.GetEntryAssembly()?.Location;
            if (path == null || !File.Exists(path))
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
            Console.WriteLine($"Connected to {pid}... asking to run {path}");
            client.Send(new LoadAndRunRequest(path));

            Console.WriteLine("Waiting for termination...");
            var outcome = client.Receive() as StatusWithError;
            if (!outcome.Success)
            {
                throw new ApplicationException(outcome.Error);
            }
        }
    }
}
