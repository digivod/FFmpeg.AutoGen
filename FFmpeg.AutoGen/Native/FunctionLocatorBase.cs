﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFmpeg.AutoGen.Native;

public abstract class FunctionLocatorBase : IFunctionLocator
{
    public static readonly Dictionary<string, string[]> LibraryDependenciesMap =
        new()
        {
            { "avcodec", new[] { "avutil", "swresample" } },
            { "avdevice", new[] { "avcodec", "avfilter", "avformat", "avutil" } },
            { "avfilter", new[] { "avcodec", "avformat", "avutil", "postproc", "swresample", "swscale" } },
            { "avformat", new[] { "avcodec", "avutil" } },
            { "avutil", new string[0] },
            { "postproc", new[] { "avutil" } },
            { "swresample", new[] { "avutil" } },
            { "swscale", new[] { "avutil" } }
        };

    private readonly Dictionary<string, IntPtr> _loadedLibraries = new();

    private readonly object _syncRoot = new();

    public T GetFunctionDelegate<T>(string libraryName, string functionName, bool throwOnError = true)
    {
        var nativeLibraryHandle = LoadLibrary(libraryName, throwOnError);
        var ptr = GetFunctionPointer(nativeLibraryHandle, functionName);

        if (ptr == IntPtr.Zero)
        {
            if (throwOnError) throw new EntryPointNotFoundException($"Could not find the entrypoint for {functionName}.");
            return default;
        }

#if NETSTANDARD2_0_OR_GREATER
        try
        {
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }
        catch (MarshalDirectiveException)
        {
            if (throwOnError)
                throw;
            return default;
        }
#else
        return (T)(object)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
#endif
    }

    protected abstract string GetNativeLibraryName(string libraryName, int version);
    protected abstract IntPtr LoadNativeLibrary(string libraryName);

    protected abstract IntPtr GetFunctionPointer(IntPtr nativeLibraryHandle, string functionName);

    private IntPtr LoadLibrary(string libraryName, bool throwOnError)
    {
        if (_loadedLibraries.TryGetValue(libraryName, out var ptr)) return ptr;

        lock (_syncRoot)
        {
            if (_loadedLibraries.TryGetValue(libraryName, out ptr)) return ptr;

            var dependencies = LibraryDependenciesMap[libraryName];
            dependencies.Where(n => !_loadedLibraries.ContainsKey(n) && !n.Equals(libraryName))
                .ToList()
                .ForEach(n => LoadLibrary(n, false));

            var version = ffmpeg.LibraryVersionMap[libraryName];
            var nativeLibraryName = GetNativeLibraryName(Path.Combine(ffmpeg.RootPath, libraryName), version);

            ptr = LoadNativeLibrary(nativeLibraryName);

            if (ptr != IntPtr.Zero) _loadedLibraries.Add(libraryName, ptr);
            else if (throwOnError)
            {
                throw new DllNotFoundException(
                    $"Unable to load DLL '{libraryName}.{version} under {ffmpeg.RootPath}': The specified module could not be found.");
            }

            return ptr;
        }
    }
}