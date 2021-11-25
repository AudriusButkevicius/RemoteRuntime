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
    std::cout << "Runtime path " << dll_path.string() << std::endl;
    std::this_thread::sleep_for(std::chrono::seconds(2));
    try
    {
        if (!load_hostfxr())
        {
            std::cerr << "Failure: load_hostfxr()" << std::endl;
            return;
        }
    }
    catch (std::exception exc)
    {
        std::cout << "Exception " << exc.what() << std::endl;
        throw;
    } catch (...)
    {
        std::cout << "Exception " << GetLastError << std::endl;
        throw;
    }


    const std::filesystem::path host_path = dll_path.parent_path();
    const auto temp_dir = create_temporary_directory();
    std::cout << "Copying host from " << host_path.string() << " to " << temp_dir.string() << std::endl;
    copy(host_path, temp_dir);

    std::filesystem::path config_path = temp_dir / L"RemoteRuntime.Host.runtimeconfig.json";

    if (!exists(config_path))
    {
        std::cerr << "Config path " << config_path.string() << " does not exist" << std::endl;
        return;
    }

    const auto load_assembly_and_get_function_pointer = get_dotnet_load_assembly(config_path);
    if (load_assembly_and_get_function_pointer == nullptr)
    {
        std::cerr << "Failure: get_dotnet_load_assembly()" << std::endl;
        return;
    }

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
        std::cerr << "Failure: load_assembly_and_get_function_pointer()" << std::endl;
        return;
    }

    entrypoint(nullptr, 0);
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
