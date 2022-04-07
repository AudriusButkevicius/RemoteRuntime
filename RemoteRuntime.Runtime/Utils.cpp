#define NETHOST_EXPORT
#include <filesystem>
#include <random>
#include <sstream>

#include <nethost.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <Windows.h>
#include "Utils.hpp"

#include <iostream>

bool load_hostfxr()
{
    char_t buffer[MAX_PATH];
    size_t buffer_size = sizeof(buffer) / sizeof(char_t);
    std::cout << "Calling get_hostfxr_path" << std::endl << std::flush;
    const int rc = get_hostfxr_path(buffer, &buffer_size, nullptr);
    if (rc != 0)
        return false;
    std::wcout << L"Loading runtime from " << std::wstring(buffer) << std::endl << std::flush;
    const HMODULE lib = ::LoadLibraryW(buffer);
    init_fptr = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(::GetProcAddress(
        lib, "hostfxr_initialize_for_runtime_config"));
    get_delegate_fptr = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
        ::GetProcAddress(lib, "hostfxr_get_runtime_delegate"));
    close_fptr = reinterpret_cast<hostfxr_close_fn>(::GetProcAddress(lib, "hostfxr_close"));

    return init_fptr && get_delegate_fptr && close_fptr;
}

load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(std::filesystem::path config_path)
{
    // Load .NET Core
    void* load_assembly_and_get_function_pointer = nullptr;
    hostfxr_handle cxt = nullptr;
    std::cout << "Calling init_fptr..." << std::endl << std::flush;
    int rc = init_fptr(config_path.c_str(), nullptr, &cxt);
    if (rc != 0 || cxt == nullptr)
    {
        std::cerr << "Init failed: " << std::hex << std::showbase << rc << std::endl << std::flush;
        close_fptr(cxt);
        return nullptr;
    }

    // Get the load assembly function pointer
    std::cout << "Calling get_delegate_fptr..." << std::endl << std::flush;
    rc = get_delegate_fptr(
        cxt,
        hdt_load_assembly_and_get_function_pointer,
        &load_assembly_and_get_function_pointer);
    if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
    {
        std::cerr << "Get delegate failed: " << std::hex << std::showbase << rc << std::endl << std::flush;
        return nullptr;
    }
       
    std::cout << "Calling close_fptr..." << std::endl << std::flush;
    close_fptr(cxt);
    return static_cast<load_assembly_and_get_function_pointer_fn>(load_assembly_and_get_function_pointer);
}


std::filesystem::path create_temporary_directory(unsigned long long max_tries)
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
