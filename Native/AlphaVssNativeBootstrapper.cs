using System.Reflection;
using System.Runtime.InteropServices;

namespace SandboxTimeline;

internal static class AlphaVssNativeBootstrapper
{
    private static readonly string[] NativeLoadOrder =
    [
        "Ijwhost.dll",
        "AlphaVSS.x64.dll"
    ];

    private const string ManagedCommonFileName = "AlphaVSS.Common.dll";
    private static readonly object InitLock = new();
    private static bool _initialized;
    private static readonly List<string> LoadedModules = new();

    public static string ApplicationBaseDirectory => AppContext.BaseDirectory;

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            var baseDirectory = ApplicationBaseDirectory;
            RegisterManagedAssemblyResolver(baseDirectory);
            SetNativeSearchDirectory(baseDirectory);
            EnsureNativeLayout(baseDirectory);

            var missingFiles = new List<string>();
            foreach (var fileName in NativeLoadOrder)
            {
                if (!TryLoadNativeModule(baseDirectory, fileName, out var loadedPath))
                {
                    missingFiles.Add(fileName);
                    continue;
                }

                LoadedModules.Add(loadedPath);
            }

            if (missingFiles.Count > 0)
            {
                var discovered = Directory.Exists(baseDirectory)
                    ? string.Join(", ", Directory.GetFiles(baseDirectory, "*.dll").Select(Path.GetFileName))
                    : "(directory missing)";

                throw new DllNotFoundException(
                    "AlphaVSS native dependencies could not be loaded from the application base directory.\n" +
                    $"BaseDirectory: {baseDirectory}\n" +
                    $"Missing: {string.Join(", ", missingFiles)}\n" +
                    $"Discovered DLLs: {discovered}\n" +
                    "Publish must copy Ijwhost.dll, AlphaVSS.x64.dll, and AlphaVSS.Common.dll beside SandboxTimeline.exe.");
            }

            _initialized = true;
        }
    }

    public static bool IsNativeModuleLoaded => LoadedModules.Count >= NativeLoadOrder.Length;

    private static void SetNativeSearchDirectory(string baseDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            SetDllDirectory(baseDirectory);

            var nestedNativeDirectory = Path.Combine(baseDirectory, "runtimes", "win-x64", "native");
            if (Directory.Exists(nestedNativeDirectory))
            {
                SetDllDirectory(nestedNativeDirectory);
            }

            SetDllDirectory(baseDirectory);
        }
        catch
        {
        }
    }

    private static void EnsureNativeLayout(string baseDirectory)
    {
        var nestedDirectory = Path.Combine(baseDirectory, "runtimes", "win-x64", "native");
        Directory.CreateDirectory(nestedDirectory);

        foreach (var fileName in NativeLoadOrder.Concat([ManagedCommonFileName]))
        {
            var rootPath = Path.Combine(baseDirectory, fileName);
            var nestedPath = Path.Combine(nestedDirectory, fileName);
            if (File.Exists(rootPath) && !File.Exists(nestedPath))
            {
                File.Copy(rootPath, nestedPath, overwrite: true);
            }
            else if (File.Exists(nestedPath) && !File.Exists(rootPath))
            {
                File.Copy(nestedPath, rootPath, overwrite: true);
            }
        }
    }

    private static bool TryLoadNativeModule(string baseDirectory, string fileName, out string loadedPath)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, fileName),
            Path.Combine(baseDirectory, "runtimes", "win-x64", "native", fileName)
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(candidate, out _))
            {
                loadedPath = candidate;
                return true;
            }
        }

        loadedPath = string.Empty;
        return false;
    }

    private static void RegisterManagedAssemblyResolver(string baseDirectory)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name);
            if (!string.Equals(requested.Name, "AlphaVSS.Common", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var managedPath = Path.Combine(baseDirectory, ManagedCommonFileName);
            return File.Exists(managedPath) ? Assembly.LoadFrom(managedPath) : null;
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
