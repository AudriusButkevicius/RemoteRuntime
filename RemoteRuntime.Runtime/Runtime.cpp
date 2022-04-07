#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <iostream>
#include <filesystem>
#include <fstream>
#include "Utils.hpp"

#include <Windows.h>

#include <coreclr_delegates.h>
#include <thread>


void command_thread(HMODULE module)
{
    WCHAR dll_path_cstr[MAX_PATH];
    GetModuleFileName(module, dll_path_cstr, MAX_PATH);
    const std::filesystem::path dll_path(dll_path_cstr);
    std::cout << "Runtime path " << dll_path.string() << std::endl << std::flush;
    std::this_thread::sleep_for(std::chrono::seconds(1));
    try
    {
        if (!load_hostfxr())
        {
            std::cerr << "Failure: load_hostfxr()" << std::endl << std::flush;
            return;
        }
        std::cout << "load_hostfxr succeeded" << std::endl << std::flush;

        const std::filesystem::path host_path = dll_path.parent_path();
        const auto temp_dir = host_path;
        // const auto temp_dir = create_temporary_directory();
        // std::cout << "Copying host from " << host_path.string() << " to " << temp_dir.string() << std::endl;
        // copy(host_path, temp_dir);

        std::filesystem::path config_path = temp_dir / L"RemoteRuntime.Host.runtimeconfig.json";

        if (!exists(config_path))
        {
            std::cerr << "Config path " << config_path.string() << " does not exist" << std::endl << std::flush;
            return;
        }

        //std::this_thread::sleep_for(std::chrono::milliseconds(500));

        std::cout << "Calling get_dotnet_load_assembly" << std::endl << std::flush;
        const auto load_assembly_and_get_function_pointer = get_dotnet_load_assembly(config_path);
        if (load_assembly_and_get_function_pointer == nullptr)
        {
            std::cerr << "Failure: get_dotnet_load_assembly()" << std::endl << std::flush;
            return;
        }

        //std::this_thread::sleep_for(std::chrono::milliseconds(500));

        std::cout << "Calling load_assembly_and_get_function_pointer" << std::endl << std::flush;
        component_entry_point_fn entrypoint = nullptr;
        int rc = load_assembly_and_get_function_pointer(
            (temp_dir / L"RemoteRuntime.Host.dll").c_str(),
            L"RemoteRuntime.Host, RemoteRuntime.Host",
            L"Run",
            nullptr,
            nullptr,
            reinterpret_cast<void**>(&entrypoint));
        if (rc != 0 || entrypoint == nullptr)
        {
            std::cerr << "Failure: load_assembly_and_get_function_pointer()" << std::endl << std::flush;
            return;
        }

        entrypoint(nullptr, 0);
    }
    catch (std::exception& exc)
    {
        std::cout << "Exception " << exc.what() << std::endl << std::flush;
        throw;
    } catch (...)
    {
        std::cout << "Exception " << GetLastError() << std::endl << std::flush;
        throw;
    }
}

//---------------------------------------------------------------------------
BOOL WINAPI DllMain(HMODULE handle, DWORD reason, PVOID reversed)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        if (AllocConsole())
        {
            FILE* fDummy;
            freopen_s(&fDummy, "CONOUT$", "w", stdout);
            freopen_s(&fDummy, "CONOUT$", "w", stderr);
            freopen_s(&fDummy, "CONIN$", "r", stdin);
            std::cout.clear();
            std::clog.clear();
            std::cerr.clear();
            std::cin.clear();
        }

        std::thread(command_thread, handle).detach();
        return TRUE;
    }

    return FALSE;
}
