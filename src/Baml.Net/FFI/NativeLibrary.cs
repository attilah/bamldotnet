using System;
using System.Runtime.InteropServices;

namespace Baml.Net.FFI;

/// <summary>
/// Helper class to determine the correct native library name based on the platform.
/// </summary>
internal static class NativeLibrary
{
    /// <summary>
    /// Gets the native library name for the current platform.
    /// </summary>
    public static string LibraryName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "baml_cffi";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libbaml_cffi";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libbaml_cffi";

            throw new PlatformNotSupportedException(
                $"Unsupported platform: {RuntimeInformation.OSDescription}");
        }
    }
}
