# RemoteRuntime

![image](https://user-images.githubusercontent.com/1144861/142778799-48b75f10-c8cb-48f4-84d2-7d096dc33dc9.png)

# What

Yet another bastard child for remote process manipulation.

Uses C# AssemblyLoadContext (.NET 5+ I think) to dynamically load, execute, unload C# DLLs into a remote process, without having to restart the process.
The intended usecase is to quickly iterate on C# code that is supposed to run in and interact with native code.

See [RemoteHands](https://github.com/AudriusButkevicius/RemoteHands) for alternative approach.

# Why

I suck as C++, so want to do stuff in C#, and without having to restart the native process all the time.

# How

The project is composed out of 4 parts:

1. Runtime - C++ library that gets injected into a remote process and starts up a C# runtime
2. Host - The bootstrap library the "Runtime" component starts. Responsible for loading and unloading DLLs. Listens on a named pipe for instructions.
3. Base - Base class for a plugin that you'd like the "Host" to run. Provides the basic expected interface.
4. Common - Common shared utilities. Contains the code required to talk between the plugin and the host. Also contains the code for injecting "Runtime" into remote processes (but that's accessible from Base).

# How do I use it

See DemoPlugin - An example tying it all together and showing how it works.
It will start Notepad with a C# runtime embedded, executing your C# code.

# Dependencies

Assumes you have .NET 5+ installed on your machine.
