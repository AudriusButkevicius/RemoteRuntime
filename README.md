# RemoteRuntime

Yet another bastard child for remote process manipulation.

See [RemoteHands](https://github.com/AudriusButkevicius/RemoteHands) for alternative approach.

The project is composed out of 4 parts:

1. Runtime - C++ library that gets injected into a remote process and starts up a C# runtime
2. Host - The library the "Runtime" component starts. Listens on a named pipe for instructions for which C# library it should load.
3. Base - Base class for a plugin that you'd like the "Host" to run. Provides the basic expected interface.
4. Common - Common shared utilities. Also contains the code for injecting "Runtime" into remote processes (but that's accessible from Base).
5. DemoPlugin - An example tying it all together and showing how it works.

# How do I use it

See/Run DemoPlugin. It will start Notepad with a C# runtime embedded, executing your C# code.

# But why?

I suck as C++, so want to do stuff in C#.

# Dependencies

Assumes you have .Net Framework installed on your machine.
