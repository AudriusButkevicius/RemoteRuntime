using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static RemoteRuntime.Native;

namespace RemoteRuntime
{
    public static class Injector
    {
        public static bool Inject(Process targetProcess, string dllName)
        {
            if (!File.Exists(dllName))
            {
                throw new FileLoadException("File does not exist", dllName);
            }

            if (targetProcess.HasExited)
            {
                throw new ApplicationException("Process has exited");
            }

            foreach (ProcessModule targetProcessModule in targetProcess.Modules)
            {
                if (targetProcessModule.FileName == dllName)
                {
                    return false;
                }
            }

            // geting the handle of the process - with required privileges
            IntPtr procHandle =
                OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION |
                    PROCESS_VM_WRITE | PROCESS_VM_READ, false, targetProcess.Id
                );

            try
            {
                // searching for the address of LoadLibraryA and storing it in a pointer
                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                var sz = (uint)((dllName.Length + 1) * Marshal.SizeOf(typeof(char)));

                // alocating some memory on the target process - enough to store the name of the dll
                // and storing its address in a pointer
                IntPtr allocMemAddress =
                    VirtualAllocEx(procHandle, IntPtr.Zero, sz, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                // writing the name of the dll there
                WriteProcessMemory(procHandle, allocMemAddress, Encoding.Default.GetBytes(dllName), sz, out _);

                // creating a thread that will call LoadLibraryA with allocMemAddress as argument
                IntPtr threadHandle = CreateRemoteThread(
                    procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress,
                    0, IntPtr.Zero
                );
                
                while (WaitForSingleObject(threadHandle, 1000) != 0)
                {
                    if (targetProcess.HasExited)
                    {
                        throw new ApplicationException("Process has exited");
                    }
                }

                CloseHandle(threadHandle);

                VirtualFreeEx(procHandle, allocMemAddress, 0, MEM_RELEASE);

                return true;
            }
            finally
            {
                CloseHandle(procHandle);
            }
        }
    }
}
