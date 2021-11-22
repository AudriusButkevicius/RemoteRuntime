using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace RemoteRuntime
{
    public class Host
    {
        public static int Run(string hello)
        {
            var pid = Process.GetCurrentProcess().Id;
            while (true)
            {
                try
                {
                    NamedPipeServerStream pipe = new(
                        $"remoteruntime-{pid}", PipeDirection.InOut, 1, PipeTransmissionMode.Message
                    );

                    Console.WriteLine("Awaiting client connection...");
                    pipe.WaitForConnection();
                    try
                    {
                        var server = MessageClient.CreateServer(pipe);

                        var message = server.Receive() as LoadAndRunRequest;
                        if (message == null)
                        {
                            throw new ApplicationException("Unknown message");
                        }

                        var cancellationTokenSource = new CancellationTokenSource();

                        var task = Task.Factory.StartNew(
                            () =>
                            {
                                try
                                {
                                    LoadModuleRequest(message, cancellationTokenSource.Token);
                                    Console.WriteLine("Sending success");
                                    server.Send(new StatusWithError(true, ""));
                                }
                                catch (OperationCanceledException)
                                {
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Exception in plugin {e}");
                                    if (pipe.IsConnected)
                                    {
                                        //server.Send(new StatusWithError(false, e.StackTrace));
                                    }
                                }
                                finally
                                {
                                    // To get out of the poll loop.
                                    pipe.Disconnect();
                                }
                            }
                        );

                        while (server.Poll() >= 0)
                        {
                            Thread.Yield();
                        }

                        Console.WriteLine("Client disconnected, cancelling task...");
                        cancellationTokenSource.Cancel();
                        task.Wait();
                        Console.WriteLine("Task finished");
                    }
                    finally
                    {
                        if (pipe.IsConnected) pipe.Disconnect();
                        pipe.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Try again
                }
            }

#pragma warning disable 162
            return 0;
#pragma warning restore 162
            // ReSharper disable once FunctionNeverReturns
        }

        private static ResolveEventHandler MakeAssemblyResolver(string stage, string directory)
        {
            return (sender, args) =>
            {
                string assemblyName = args.Name.Split('.')[0].Trim();
                Console.WriteLine($"==== {stage} loading {assemblyName}");
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

                return Assembly.Load(assemblyName);
            };
        }

        private static void ExecuteAndUnload(
            string assemblyPath, string typeName, CancellationToken cancellationToken
        )
        {
            var domain = AppDomain.CreateDomain($"{assemblyPath.GetHashCode()}");

            AppDomain.CurrentDomain.AssemblyResolve += MakeAssemblyResolver(
                "Normal", Path.GetDirectoryName(assemblyPath)
            );

            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += MakeAssemblyResolver(
                "Reflect", Path.GetDirectoryName(assemblyPath)
            );

            try
            {
                dynamic plugin = domain.CreateInstanceFromAndUnwrap(assemblyPath, typeName);
                var pluginTask = Task.Factory.StartNew(
                    () =>
                    {
                        Console.WriteLine($"Starting plugin {plugin.GetType().FullName}");
                        plugin.Entrypoint();
                    }, cancellationToken
                );

                pluginTask.Wait(cancellationToken);
                Console.WriteLine($"Plugin {plugin.GetType().FullName} finished");
            }
            finally
            {
                Console.WriteLine("Unloading...");
                AppDomain.Unload(domain);
                Console.WriteLine("Unloaded");
            }
        }

        private static void LoadModuleRequest(LoadAndRunRequest request, CancellationToken cancellationToken)
        {
            string sourceDirectory = Path.GetDirectoryName(request.Path);
            string temporaryDirectory = Utils.GetTemporaryDirectory();

            Console.WriteLine($"Copying plugin from {sourceDirectory} to {temporaryDirectory}");
            Utils.DirectoryCopy(sourceDirectory, temporaryDirectory, true);

            try
            {
                string copyAssemblyPath = Path.Combine(temporaryDirectory, Path.GetFileName(request.Path));
                ExecuteAndUnload(copyAssemblyPath, request.TypeName, cancellationToken);
            }
            finally
            {
                try
                {
                    Directory.Delete(temporaryDirectory, true);
                    Console.WriteLine($"Cleaned up temporary directory {temporaryDirectory}");
                }
                catch (Exception)
                {
                    // Best effort
                }
            }
        }
    }
}
