using System;
using System.Diagnostics;
using RemoteRuntime.Plugin;

namespace DemoPlugin
{
    public class MyPlugin : PluginBase
    {
        public override void Run()
        {
            while (!Terminate.WaitOne(1000))
            {
                Console.WriteLine($"Plugin running...");
            }
        }

        public static void Main(string[] args)
        {
            var processes = Process.GetProcessesByName("Notepad");
            var started = false;
            if (processes.Length == 0)
            {
                started = true;
                processes = new[] { Process.Start("notepad.exe") };
            }

            try
            {
                Inject(processes[0].Id);
            }
            finally
            {
                if (started)
                {
                    processes[0].Kill();
                }
            }
        }
    }
}
