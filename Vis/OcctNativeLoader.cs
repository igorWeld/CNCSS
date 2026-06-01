using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CNCSS.Vis
{
    /// <summary>Ensures Open CASCADE native DLLs from <c>occt\x64</c> are on the load path before Occt.NET calls.</summary>
    internal static class OcctNativeLoader
    {
        private static readonly object Gate = new();
        private static bool _initialized;
        private static bool _resolveRegistered;
        private static string? _error;
        private static string? _nativeDir;

        public static string? InitializationError
        {
            get
            {
                EnsureInitialized();
                return _error;
            }
        }

        [ModuleInitializer]
        internal static void RegisterEarly()
        {
            EnsureInitialized();
        }

        public static bool EnsureInitialized()
        {
            RegisterAssemblyResolver();

            if (_initialized)
            {
                return _error == null;
            }

            lock (Gate)
            {
                if (_initialized)
                {
                    return _error == null;
                }

                _initialized = true;
                try
                {
                    string baseDir = AppContext.BaseDirectory;
                    string platform = Environment.Is64BitProcess ? "x64" : "x86";
                    _nativeDir = Path.Combine(baseDir, "occt", platform);
                    if (!Directory.Exists(_nativeDir))
                    {
                        _error = $"Не найдена папка Open CASCADE: {_nativeDir}";
                        return false;
                    }

                    string tkernel = Path.Combine(_nativeDir, "TKernel.dll");
                    if (!File.Exists(tkernel))
                    {
                        _error = $"Не найден TKernel.dll в {_nativeDir}";
                        return false;
                    }

                    string occtManaged = Path.Combine(_nativeDir, "Occt.NET.dll");
                    if (!File.Exists(occtManaged))
                    {
                        _error = $"Не найден Occt.NET.dll в {_nativeDir}";
                        return false;
                    }

                    SetDefaultDllDirectories(0x00001000);
                    if (!AddDllDirectory(_nativeDir))
                    {
                        int win32 = Marshal.GetLastWin32Error();
                        Trace.WriteLine($"AddDllDirectory failed: {win32}");
                    }

                    string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                    Environment.SetEnvironmentVariable(
                        "PATH",
                        _nativeDir + Path.PathSeparator + (pathEnv ?? string.Empty));

                    NativeLibrary.Load(tkernel);

                    string ijwhost = Path.Combine(_nativeDir, "Ijwhost.dll");
                    if (File.Exists(ijwhost))
                    {
                        NativeLibrary.Load(ijwhost);
                    }

                    // Load managed wrapper from occt\x64 so IJW host finds native deps beside it.
                    Assembly.LoadFrom(occtManaged);

                    _error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    _error = FormatLoadError(ex);
                    Trace.WriteLine("OcctNativeLoader: " + ex);
                    return false;
                }
            }
        }

        private static void RegisterAssemblyResolver()
        {
            if (_resolveRegistered)
            {
                return;
            }

            _resolveRegistered = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveOcctAssembly;
        }

        private static Assembly? ResolveOcctAssembly(object? sender, ResolveEventArgs args)
        {
            if (args.Name == null || !args.Name.StartsWith("Occt.NET,", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                EnsureInitialized();
                if (_nativeDir == null)
                {
                    return null;
                }

                string occtManaged = Path.Combine(_nativeDir, "Occt.NET.dll");
                return File.Exists(occtManaged) ? Assembly.LoadFrom(occtManaged) : null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("OcctNativeLoader resolve: " + ex);
                return null;
            }
        }

        private static string FormatLoadError(Exception ex)
        {
            if (ex is BadImageFormatException)
            {
                return "Occt.NET: несовместимая сборка. Запускайте x64-сборку CNCSS и установите Microsoft Visual C++ 2015–2022 Redistributable (x64).";
            }

            return ex.Message;
        }

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddDllDirectory(string lpPathName);
    }
}
