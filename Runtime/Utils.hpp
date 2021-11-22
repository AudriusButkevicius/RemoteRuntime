#pragma once

#include <filesystem>

#include <coreclr_delegates.h>
#include <hostfxr.h>

inline hostfxr_initialize_for_runtime_config_fn init_fptr;
inline hostfxr_get_runtime_delegate_fn get_delegate_fptr;
inline hostfxr_close_fn close_fptr;

bool load_hostfxr();
load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(std::filesystem::path config_path);
std::filesystem::path create_temporary_directory(unsigned long long max_tries = 1000);
