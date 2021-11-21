#include <filesystem>
#include <iostream>
#include <metahost.h>
#include <process.h>
#include <random>
#include <sstream>
#include <Windows.h>

#pragma comment(lib, "mscoree.lib")


std::filesystem::path create_temporary_directory(unsigned long long max_tries = 100)
{
    const auto tmp_dir = std::filesystem::temp_directory_path();
    unsigned long long i = 0;
    std::random_device dev;
    std::mt19937 prng(dev());
    const std::uniform_int_distribution<uint64_t> rand(0);
    std::filesystem::path path;
    while (true)
    {
        std::stringstream ss;
        ss << std::hex << rand(prng);
        path = tmp_dir / ss.str();
        // true if the directory was created.
        if (create_directory(path))
        {
            break;
        }
        if (i == max_tries)
        {
            throw std::runtime_error("could not find non-existing directory");
        }
        i++;
    }
    return path;
}


void command_thread(void* module)
{
    WCHAR buffer[MAX_PATH];
    GetModuleFileName(static_cast<HMODULE>(module), buffer, MAX_PATH);

    const std::filesystem::path module_path(buffer);
    std::wcout << "Module path: " << module_path << std::endl;

    if (!exists(module_path.parent_path() / "Host.dll"))
    {
        std::wcerr << "Could not find " << (module_path.parent_path() / "Host.dll") << std::endl;
        return;
    }

    const auto tmp_dir = create_temporary_directory();
    copy(module_path.parent_path(), tmp_dir, std::filesystem::copy_options::recursive);
    std::wcout << "Copying assemblies from " << module_path.parent_path() << " to " << tmp_dir << std::endl;

    ICLRMetaHost* meta_host = nullptr;
    IEnumUnknown* runtime = nullptr;
    ICLRRuntimeInfo* runtime_info = nullptr;
    ICLRRuntimeHost* runtime_host = nullptr;
    IUnknown* enum_runtime = nullptr;
    DWORD bytes = 2048, result = 0;

    if (CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&meta_host) != S_OK)
    {
        std::cout << "[x] Error: CLRCreateInstance(..)" << std::endl;
        return;
    }

    if (meta_host->EnumerateInstalledRuntimes(&runtime) != S_OK)
    {
        std::cout << "[x] Error: EnumerateInstalledRuntimes(..)" << std::endl;
        return;
    }

    const LPWSTR framework_name = static_cast<LPWSTR>(LocalAlloc(LPTR, 2048));
    if (framework_name == nullptr)
    {
        std::cout << "[x] Error: malloc could not allocate" << std::endl;
        return;
    }

    // Enumerate through runtimes and show supported frameworks
    while (runtime->Next(1, &enum_runtime, nullptr) == S_OK)
    {
        if (enum_runtime->QueryInterface<ICLRRuntimeInfo>(&runtime_info) == S_OK)
        {
            if (runtime_info != nullptr)
            {
                runtime_info->GetVersionString(framework_name, &bytes);
                std::wcout << L"[*] Supported Framework: " << std::wstring(framework_name) << std::endl;
            }
        }
    }

    if (runtime_info->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtime_host) != S_OK)
    {
        std::cout << "[x] ..GetInterface(CLSID_CLRRuntimeHost...) failed" << std::endl;
        return;
    }

    std::wcout << L"[*] Using runtime: " << std::wstring(framework_name) << std::endl;

    // Start runtime, and load our assembly
    if (runtime_host->Start() != S_OK)
    {
        std::cout << "[x] ..Start() failed" << std::endl;
        return;
    }

    const auto assembly = tmp_dir / "Host.dll";
    if (!exists(assembly))
    {
        std::cout << "[x] ..Does not exist" << std::endl;
        return;
    }

    std::cout << "[*] ======= Calling .NET Code =======" << std::endl;
    if (runtime_host->ExecuteInDefaultAppDomain(
        assembly.c_str(),
        L"RemoteRuntime.Host", L"Run", nullptr,
        &result
    ) != S_OK)
    {
        std::cout << "[x] Error: ExecuteInDefaultAppDomain(..) failed" << std::endl;
        return;
    }
    std::cout << "[*] ======= Done =======" << std::endl;
}

//---------------------------------------------------------------------------
BOOL WINAPI DllMain(HMODULE handle, DWORD reason, PVOID reversed)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        if (AllocConsole())
        {
            FILE* f_dummy;
            freopen_s(&f_dummy, "CONOUT$", "w", stdout);
            freopen_s(&f_dummy, "CONOUT$", "w", stderr);
            freopen_s(&f_dummy, "CONIN$", "r", stdin);
            std::cout.clear();
            std::clog.clear();
            std::cerr.clear();
            std::cin.clear();
        }

        std::cout << "Injection success" << std::endl;
        _beginthread(command_thread, 0, handle);
        return TRUE;
    }

    return FALSE;
}
