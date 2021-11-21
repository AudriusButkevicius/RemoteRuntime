using System;
using System.Diagnostics;
using RemoteRuntime.Plugin;

namespace DemoPlugin
{
    public class MyPlugin : PluginBase
    {
        public override string GetVersion() => "1.0.0";

        public override void Run()
        {
            while (!Terminate.WaitOne(1000))
            {
                Console.WriteLine("Plugin running...");
            }
        }

        public static void Main(string[] args)
        {
            var processes = Process.GetProcessesByName("Notepad");
            if (processes.Length == 0)
            {
                processes = new[] { Process.Start("notepad.exe") };
            }

            Inject(processes[0].Id);
        }
    }
}
