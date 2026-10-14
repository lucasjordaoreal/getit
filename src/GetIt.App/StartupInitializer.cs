using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace GetIt_App;

internal static class StartupInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var libsDir = Path.Combine(AppContext.BaseDirectory, "Libs");
        if (Directory.Exists(libsDir))
        {
            SetDllDirectory(libsDir);
        }

        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            if (Directory.Exists(libsDir))
            {
                var candidate = Path.Combine(libsDir, $"{assemblyName.Name}.dll");
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }
            return null;
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
