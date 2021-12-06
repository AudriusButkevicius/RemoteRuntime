// ReSharper disable UnusedMember.Global UnusedType.Global InconsistentNaming MemberCanBeProtected.Global

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RemoteRuntime.Plugin
{
    public abstract class PluginBase
    {
        protected readonly ManualResetEvent Terminate = new(false);

        public abstract void Run();

        public void Stop()
        {
            Console.WriteLine($"{GetType().Name} asked to unload, setting termination event");
            Terminate.Set();
        }


        protected static void Inject(int pid, string pathToRuntimeDll = null, string assemblyPath = null)
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

            if (Injector.Inject(pid, pathToRuntimeDll))
            {
                Console.WriteLine($"Injected runtime to {pid}");
            }

            var client = MessageClient.CreateClient(pid);
            Console.WriteLine($"Connected to {pid}... asking to run {path}");
            client.Send(new LoadAndRunRequest(path));

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
