using System;
using System.Collections.Generic;
using System.Diagnostics;
using RemoteRuntime.Plugin;

namespace DemoPlugin
{
    public class MyPlugin : PluginBase
    {
        public override void Run(Dictionary<string, string> arguments)
        {
            foreach (var keyValuePair in arguments)
            {
                Console.WriteLine(keyValuePair);
            }
            int i = 0;
            while (!Terminate.WaitOne(1000))
            {
                Console.WriteLine($"C# plugin running...");
                i++;
                if (i > 5)
                {
                    throw new ApplicationException("bad stufff");
                }
            }
        }

        public static void Main(string[] args)
        {
            var processes = Process.GetProcessesByName("Notepad");
            if (processes.Length == 0)
            {
                processes = new[] { Process.Start("notepad.exe") };
            }

            Inject(processes[0].Id, arguments: new Dictionary<string, string>
            {
                {"foo", "bar"}
            });
        }
    }
}
