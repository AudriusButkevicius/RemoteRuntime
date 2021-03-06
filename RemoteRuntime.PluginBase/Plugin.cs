// ReSharper disable UnusedMember.Global UnusedType.Global InconsistentNaming MemberCanBeProtected.Global

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RemoteRuntime.Plugin
{
    public abstract class PluginBase
    {
        protected readonly ManualResetEvent Terminate = new(false);

        public abstract void Run(Dictionary<string, string> arguments);

        public void Stop()
        {
            Console.WriteLine($"{GetType().Name} asked to unload, setting termination event");
            Terminate.Set();
        }

        protected static void Inject(Process process, string pathToRuntimeDll = null, string assemblyPath = null, Dictionary<string, string> arguments = null, bool copyAssembly = false)
        {
            string path = assemblyPath ?? Assembly.GetEntryAssembly()?.Location;
            if (path == null || !File.Exists(path))
            {
                throw new ApplicationException($"Could not find own assembly? {path}");
            }

            if (pathToRuntimeDll == null)
            {
                string expectedPath = Path.GetFullPath(
                    Path.Combine(
                        Path.GetDirectoryName(path), @"..\..\..\..\..\RemoteRuntime.Runtime\bin\x64\Release\Runtime.dll"
                    )
                );
                if (!File.Exists(expectedPath))
                {
                    throw new ApplicationException(
                        $"Could not find Runtime.dll, please provide as {nameof(pathToRuntimeDll)} argument"
                    );
                }

                pathToRuntimeDll = expectedPath;
            }

            if (Injector.Inject(process, pathToRuntimeDll))
            {
                Console.WriteLine($"Injected runtime to {process.Id}");
            }

            var client = MessageClient.CreateClient(process);
            Console.WriteLine($"Connected to {process.Id}... asking to run {path}");
            client.Send(new LoadAndRunRequest(path, arguments, copyAssembly));

            while (true)
            {
                var msg = client.Receive();
                if (msg is LogLine log)
                {
                    var writer = log.IsError ? Console.Error : Console.Out;
                    writer.Write(log.Line);
                    writer.Flush();
                }
                else if (msg is StatusWithError status)
                {
                    if (!status.Success)
                    {
                        throw new ApplicationException(status.Error);
                    }

                    break;
                }
                else
                {
                    throw new ApplicationException("Unhandled message type");
                }
            }
        }
    }
}
