using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteRuntime
{
    internal class HostAssemblyLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;

        public HostAssemblyLoadContext(string pluginPath) : base(true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName name)
        {
            string assemblyPath = _resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                Console.WriteLine($"Loading dependant assembly {Path.GetFileName(assemblyPath)}");
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }
    }

    public static class Host
    {
        // It is important to mark this method as NoInlining, otherwise the JIT could decide
        // to inline it into the Main method. That could then prevent successful unloading
        // of the plugin because some of the MethodInfo / Type / Plugin.Base / HostAssemblyLoadContext
        // instances may get lifetime extended beyond the point when the plugin is expected to be
        // unloaded.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExecuteAndUnload(
            string assemblyPath, CancellationToken cancellationToken, out WeakReference alcWeakRef
        )
        {
            var alc = new HostAssemblyLoadContext(assemblyPath);
            alcWeakRef = new WeakReference(alc);
            Assembly a = alc.LoadFromAssemblyPath(assemblyPath);
            try
            {
                var types = new List<Type>();
                foreach (Type type in a.GetTypes())
                {
                    if (!type.IsClass || type.IsAbstract)
                    {
                        continue;
                    }

                    MethodInfo run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo stop = type.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
                    if (run != null && stop != null)
                    {
                        types.Add(type);
                    }
                }

                if (types.Count != 1)
                {
                    throw new ApplicationException(
                        $"Found none/multiple types implementing the interface: {types.Count}"
                    );
                }

                dynamic plugin = Activator.CreateInstance(types[0]);

                alc.Unloading += context => plugin.Stop();

                var pluginTask = Task.Factory.StartNew(
                    () =>
                    {
                        Console.WriteLine($"Starting plugin {plugin.GetType().Name}");
                        plugin.Run();
                    }, cancellationToken
                );

                pluginTask.Wait(cancellationToken);
                Console.WriteLine($"Plugin {plugin.GetType().Name} finished");
            }
            finally
            {
                Console.WriteLine("Unloading...");
                alc.Unload();
            }
        }

        public static int Run(IntPtr arg, int argLength)
        {
            var pid = Process.GetCurrentProcess().Id;
            while (true)
            {
                try
                {
                    NamedPipeServerStream pipe = new(
                        $"remoteruntime-{pid}", PipeDirection.InOut, 1, PipeTransmissionMode.Message
                    );

                    Console.WriteLine("Awaiting client connection... :)");
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
                                    LoadModuleRequest(message.Path, cancellationTokenSource.Token);
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
                                        server.Send(new StatusWithError(false, e.StackTrace));
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

        private static void LoadModuleRequest(string assemblyPath, CancellationToken cancellationToken)
        {
            string sourceDirectory = Path.GetDirectoryName(assemblyPath);
            string temporaryDirectory = GetTemporaryDirectory();


            Console.WriteLine($"Copying plugin from {sourceDirectory} to {temporaryDirectory}");
            DirectoryCopy(sourceDirectory, temporaryDirectory, true);

            WeakReference hostAlcWeakRef = null;
            try
            {
                string copyAssemblyPath = Path.Join(temporaryDirectory, Path.GetFileName(assemblyPath));
                ExecuteAndUnload(copyAssemblyPath, cancellationToken, out hostAlcWeakRef);
            }
            finally
            {
                if (hostAlcWeakRef != null && hostAlcWeakRef.IsAlive)
                {
                    // Poll and run GC until the AssemblyLoadContext is unloaded.
                    // You don't need to do that unless you want to know when the context
                    // got unloaded. You can just leave it to the regular GC.
                    for (int i = 0; hostAlcWeakRef.IsAlive && (i < 10); i++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(1000);
                    }

                    if (hostAlcWeakRef.IsAlive)
                    {
                        Console.WriteLine("Failed to collect plugin assemblies");
                    }
                    else
                    {
                        Console.WriteLine("Cleaned up nicely");
                    }
                }

                if (hostAlcWeakRef == null || !hostAlcWeakRef.IsAlive)
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

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName
                );
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, true);
                }
            }
        }

        private static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
    }
}
