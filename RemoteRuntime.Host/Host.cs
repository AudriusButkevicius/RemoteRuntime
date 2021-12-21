using System;
using System.Collections.Concurrent;
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
        private readonly TextWriter _log;

        public HostAssemblyLoadContext(string pluginPath, TextWriter log) : base(true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _log = log;
        }

        protected override Assembly Load(AssemblyName name)
        {
            string assemblyPath = _resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                _log.WriteLine($"Loading dependant assembly {Path.GetFileName(assemblyPath)}");
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }
    }

    public static class Host
    {
        private static TextWriter Log = Console.Out;

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
            var alc = new HostAssemblyLoadContext(assemblyPath, Log);
            alcWeakRef = new WeakReference(alc);
            Assembly a = alc.LoadFromAssemblyPath(assemblyPath);
            MethodInfo run = null;
            MethodInfo stop = null;
            object plugin = null;
            try
            {
                var types = new List<Type>();
                foreach (Type type in a.GetTypes())
                {
                    if (!type.IsClass || type.IsAbstract || type.IsNotPublic || !type.IsPublic)
                    {
                        continue;
                    }

                    run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance);
                    stop = type.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
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

                plugin = Activator.CreateInstance(types[0], true);
                if (plugin == null)
                {
                    throw new ApplicationException(
                        "Failed create class instance. Is it public? Is the assembly dynamically loadable?"
                    );
                }

                alc.Unloading += _ =>
                {
                    Log.WriteLine("Calling stop from unloading...");
                    stop.Invoke(plugin, null);
                };

                var pluginTask = Task.Factory.StartNew(
                    () =>
                    {
                        Log.WriteLine($"Starting plugin {plugin.GetType().Name}");
                        run.Invoke(plugin, null);
                    }, cancellationToken
                );

                pluginTask.Wait(cancellationToken);
                Log.WriteLine($"Plugin {plugin.GetType().Name} finished");
            }
            finally
            {
                Log.WriteLine("Unloading...");
                try
                {
                    alc.Unload();
                    if (plugin != null)
                    {
                        Log.WriteLine("Calling stop from finally...");
                        stop?.Invoke(plugin, null);   
                    }
                }
                catch (Exception)
                {
                    // Can fail if we tried to load something dodgy.
                    Log.WriteLine("Unload request threw");
                }
            }
        }

        public static int Run(IntPtr arg, int argLength)
        {
            Log = Console.Out;
            var pid = Process.GetCurrentProcess().Id;
            var stdoutMessages = new ConcurrentQueue<string>();
            var stderrMessages = new ConcurrentQueue<string>();
            var stdout = new RedirectingTextWriter(stdoutMessages, Console.Out);
            var stderr = new RedirectingTextWriter(stderrMessages, Console.Error);
            Console.SetOut(stdout);
            Console.SetError(stderr);

            void ForwardLogs(MessageClient server)
            {
                while (stdoutMessages.TryDequeue(out var line))
                {
                    if (line != null)
                    {
                        server.Send(new LogLine(line, false));
                    }
                }

                while (stderrMessages.TryDequeue(out var line))
                {
                    if (line != null)
                    {
                        server.Send(new LogLine(line, true));
                    }
                }
            }

            while (true)
            {
                try
                {
                    NamedPipeServerStream pipe = new(
                        $"remoteruntime-{pid}", PipeDirection.InOut, 1, PipeTransmissionMode.Message
                    );

                    Log.WriteLine("Awaiting client connection...");
                    pipe.WaitForConnection();
                    stdoutMessages.Clear();
                    stderrMessages.Clear();
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
                                }
                                catch (OperationCanceledException)
                                {
                                    Log.WriteLine("Plugin cancelled...");
                                }
                                catch (Exception e)
                                {
                                    Log.WriteLine($"Exception in plugin");
                                    Log.WriteLine(e.ToString());
                                    throw;
                                }
                            }
                        );

                        while (server.Poll() >= 0 && !task.IsCompleted)
                        {
                            ForwardLogs(server);
                            Thread.Yield();
                        }

                        ForwardLogs(server);

                        if (task.IsCompleted)
                        {
                            if (task.IsCompletedSuccessfully)
                            {
                                Log.WriteLine("Task completed successfully");
                                server.Send(new StatusWithError(true, ""));
                            }
                            else
                            {
                                Log.WriteLine("Task failed");
                                server.Send(
                                    new StatusWithError(false, task.Exception?.ToString() ?? "No exception???")
                                );
                            }
                        }
                        else
                        {
                            Log.WriteLine("Client disconnected, cancelling task...");
                            cancellationTokenSource.Cancel();
                            task.Wait();
                            Log.WriteLine("Task finished");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.WriteLine($"Main loop failed:\n{e}");
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

            Log.WriteLine($"Copying plugin from {sourceDirectory} to {temporaryDirectory}");
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
                    for (int i = 0; hostAlcWeakRef.IsAlive && i < 10; i++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    if (hostAlcWeakRef.IsAlive)
                    {
                        Log.WriteLine("Failed to collect plugin assemblies");
                    }
                    else
                    {
                        Log.WriteLine("Cleaned up nicely");
                    }
                }

                try
                {
                    Directory.Delete(temporaryDirectory, true);
                    Log.WriteLine($"Cleaned up temporary directory {temporaryDirectory}");
                }
                catch (Exception)
                {
                    // Best effort
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
