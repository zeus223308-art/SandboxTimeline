using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace SandboxTimeline;

public static class Program
{
    private const string ExeFileName = "SandboxTimeline.exe";

    [STAThread]
    public static int Main(string[] args)
    {
        StartupDiagnostics.Log($"Program.Main started. Admin={IsRunningAsAdministrator()}, x64={Environment.Is64BitProcess}, BaseDir={AppContext.BaseDirectory}");

        if (!IsRunningAsAdministrator())
        {
            StartupDiagnostics.Log("Relaunching elevated process.");
            return RelaunchElevated(args);
        }

        if (!Environment.Is64BitProcess)
        {
            StartupDiagnostics.Log("Process is not 64-bit.");
            MessageBox.Show(
                "Sandbox Timeline must run as a 64-bit process for VSS snapshots.\nRebuild the solution with PlatformTarget=x64.",
                "64-bit Required",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }

        try
        {
            AlphaVssNativeBootstrapper.EnsureInitialized();
            StartupDiagnostics.Log("AlphaVSS native bootstrap OK.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("AlphaVSS native bootstrap failed.", ex);
            MessageBox.Show(
                ExceptionDisplayFormatter.FormatWithPrefix(
                    "AlphaVSS native module could not be loaded from the application directory:",
                    ex),
                "AlphaVSS Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }

        if (!SingleInstanceManager.TryAcquire())
        {
            StartupDiagnostics.Log("Single-instance mutex already held. Requesting activation of existing window.");
            if (!SingleInstanceManager.RequestActivateExistingWindow())
            {
                MessageBox.Show(
                    "Sandbox Timeline is already running.\n\n" +
                    "• Check the system tray (notification area) for the Sandbox Timeline icon.\n" +
                    "• Press Ctrl + Win + Z to open the main window.\n" +
                    "• If nothing appears, end all SandboxTimeline.exe tasks in Task Manager and run again.\n\n" +
                    "Sandbox Timeline이 이미 실행 중입니다.\n" +
                    "• 작업 표시줄 오른쪽(시스템 트레이) 아이콘을 확인하세요.\n" +
                    "• Ctrl + Win + Z 로 창을 열 수 있습니다.\n" +
                    "• 창이 없으면 작업 관리자에서 SandboxTimeline.exe를 모두 종료한 뒤 다시 실행하세요.",
                    "Sandbox Timeline",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return 0;
        }

        try
        {
            StartupDiagnostics.Log("Starting WPF application.");
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var exitCode = app.Run();
            StartupDiagnostics.Log($"Application exited with code {exitCode}.");
            return exitCode;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Fatal startup failure in Program.Main.", ex);
            MessageBox.Show(
                ExceptionDisplayFormatter.FormatWithPrefix("Sandbox Timeline failed to start:", ex),
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            SingleInstanceManager.Release();
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ResolveApplicationExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var exeBesideDll = Path.Combine(baseDir, ExeFileName);
        if (File.Exists(exeBesideDll))
        {
            return exeBesideDll;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(processPath))
        {
            return processPath;
        }

        var assemblyDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var exeNearAssembly = Path.Combine(assemblyDir, ExeFileName);
            if (File.Exists(exeNearAssembly))
            {
                return exeNearAssembly;
            }
        }

        return exeBesideDll;
    }

    private static int RelaunchElevated(string[] args)
    {
        var exePath = ResolveApplicationExecutable();
        var argumentList = string.Join(" ", args.Select(a => $"\"{a}\""));

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = argumentList,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        };

        try
        {
            Process.Start(startInfo);
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return 1;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            MessageBox.Show(
                "Administrator approval is required.\n\n" +
                "Please right-click SandboxTimeline.exe and choose 'Run as administrator', " +
                "or approve the UAC prompt when it appears.",
                "Elevation Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return 1;
        }
        catch (Win32Exception)
        {
            MessageBox.Show(
                "Sandbox Timeline requires administrator privileges to manage system snapshots and sandbox isolation.",
                "Elevation Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return 1;
        }
    }
}
