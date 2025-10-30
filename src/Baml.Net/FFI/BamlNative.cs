using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Baml.Net.FFI;

/// <summary>
/// P/Invoke declarations for the BAML native library (baml_cffi).
/// </summary>
internal static partial class BamlNative
{
    private const string LibName = "baml_cffi";

    static BamlNative()
    {
        // Register a custom resolver to load from runtimes/{rid}/native folder
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(BamlNative).Assembly,
            DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
            return IntPtr.Zero;

        // Get platform-specific library name
        var libFileName = GetLibraryFileName();
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var rid = GetRuntimeIdentifier();

        // Try standard .NET runtime path first
        var runtimePath = Path.Combine(assemblyDir, "runtimes", rid, "native", libFileName);
        if (File.Exists(runtimePath) && System.Runtime.InteropServices.NativeLibrary.TryLoad(runtimePath, out var handle))
            return handle;

        // Fallback to direct path
        var directPath = Path.Combine(assemblyDir, libFileName);
        if (File.Exists(directPath) && System.Runtime.InteropServices.NativeLibrary.TryLoad(directPath, out handle))
            return handle;

        return IntPtr.Zero;
    }

    private static string GetRuntimeIdentifier()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : throw new PlatformNotSupportedException();

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException()
        };

        return $"{os}-{arch}";
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{LibName}.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"lib{LibName}.dylib";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"lib{LibName}.so";
        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Gets the version of the BAML runtime.
    /// </summary>
    /// <returns>Pointer to a null-terminated UTF-8 string containing the version.</returns>
    /// <remarks>
    /// The returned string is owned by the native library and should not be freed.
    /// </remarks>
    [LibraryImport(LibName, EntryPoint = "version")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial IntPtr GetVersionNative();

    /// <summary>
    /// Gets the version of the BAML runtime as a managed string.
    /// </summary>
    /// <returns>The version string.</returns>
    public static string GetVersion()
    {
        IntPtr versionPtr = GetVersionNative();
        if (versionPtr == IntPtr.Zero)
            throw new InvalidOperationException("Failed to get BAML version from native library");

        return Marshal.PtrToStringUTF8(versionPtr)
            ?? throw new InvalidOperationException("Failed to marshal BAML version string");
    }

    /// <summary>
    /// Creates a BAML runtime instance.
    /// </summary>
    /// <param name="rootPath">Root path for the BAML project.</param>
    /// <param name="srcFilesJson">JSON string containing source files map (path -> content).</param>
    /// <param name="envVarsJson">JSON string containing environment variables.</param>
    /// <returns>Handle to the runtime instance, or IntPtr.Zero on failure.</returns>
    [LibraryImport(LibName, EntryPoint = "create_baml_runtime", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial IntPtr CreateRuntime(
        string rootPath,
        string srcFilesJson,
        string envVarsJson);

    /// <summary>
    /// Destroys a BAML runtime instance and frees associated resources.
    /// </summary>
    /// <param name="runtime">Handle to the runtime instance to destroy.</param>
    [LibraryImport(LibName, EntryPoint = "destroy_baml_runtime")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial void DestroyRuntime(IntPtr runtime);

    /// <summary>
    /// Invokes the BAML CLI with the provided arguments.
    /// </summary>
    /// <param name="args">Array of argument strings.</param>
    /// <param name="argc">Number of arguments.</param>
    /// <returns>Exit code from the CLI invocation.</returns>
    [LibraryImport(LibName, EntryPoint = "invoke_runtime_cli")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial int InvokeRuntimeCli(IntPtr args, int argc);

    /// <summary>
    /// Executes a BAML function.
    /// </summary>
    /// <param name="runtime">Handle to the runtime instance.</param>
    /// <param name="functionName">Pointer to null-terminated function name string.</param>
    /// <param name="args">Pointer to protobuf-encoded arguments.</param>
    /// <param name="argsLen">Length of the arguments buffer.</param>
    /// <param name="callbackId">Callback identifier for async operations.</param>
    /// <returns>Pointer to error string on failure, IntPtr.Zero on success.</returns>
    [LibraryImport(LibName, EntryPoint = "call_function_from_c")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial IntPtr CallFunction(
        IntPtr runtime,
        IntPtr functionName,
        IntPtr args,
        nuint argsLen,
        uint callbackId);

    /// <summary>
    /// Executes a BAML function with streaming support.
    /// The native function has the same signature as CallFunction - it uses
    /// the global registered callbacks for streaming chunks.
    /// </summary>
    /// <param name="runtime">Handle to the runtime instance.</param>
    /// <param name="functionName">Pointer to null-terminated UTF-8 encoded function name.</param>
    /// <param name="args">Pointer to protobuf-encoded arguments.</param>
    /// <param name="argsLen">Length of the arguments buffer.</param>
    /// <param name="callbackId">Callback identifier for async operations.</param>
    /// <returns>Pointer to result buffer, or IntPtr.Zero on failure.</returns>
    [LibraryImport(LibName, EntryPoint = "call_function_stream_from_c")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial IntPtr CallFunctionStream(
        IntPtr runtime,
        IntPtr functionName,
        IntPtr args,
        nuint argsLen,
        uint callbackId);

    /// <summary>
    /// Callback delegate for function results (both success and streaming).
    /// </summary>
    /// <param name="callbackId">The callback identifier.</param>
    /// <param name="isDone">1 if this is the final result, 0 for streaming updates.</param>
    /// <param name="content">Pointer to the result data buffer.</param>
    /// <param name="length">Length of the result data buffer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ResultCallback(uint callbackId, int isDone, IntPtr content, nuint length);

    /// <summary>
    /// Callback delegate for function errors.
    /// </summary>
    /// <param name="callbackId">The callback identifier.</param>
    /// <param name="isDone">1 if this is the final error, 0 for streaming errors.</param>
    /// <param name="content">Pointer to the error message buffer.</param>
    /// <param name="length">Length of the error message buffer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ErrorCallback(uint callbackId, int isDone, IntPtr content, nuint length);

    /// <summary>
    /// Callback delegate for streaming function updates (on tick).
    /// </summary>
    /// <param name="callbackId">The callback identifier.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void OnTickCallback(uint callbackId);

    /// <summary>
    /// Registers callback functions with the native library.
    /// Must be called once before any function calls.
    /// </summary>
    /// <param name="resultCallback">Callback for successful results.</param>
    /// <param name="errorCallback">Callback for errors.</param>
    /// <param name="onTickCallback">Callback for streaming updates.</param>
    [LibraryImport(LibName, EntryPoint = "register_callbacks")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial void RegisterCallbacks(
        ResultCallback resultCallback,
        ErrorCallback errorCallback,
        OnTickCallback onTickCallback);

    /// <summary>
    /// Frees a buffer returned by native functions.
    /// </summary>
    /// <param name="buffer">The buffer to free.</param>
    [LibraryImport(LibName, EntryPoint = "free_buffer")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial void FreeBuffer(Buffer buffer);

    /// <summary>
    /// Represents a buffer returned from native code.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Buffer
    {
        public IntPtr Data;
        public nuint Length;
    }
}
