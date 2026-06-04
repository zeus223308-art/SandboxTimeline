using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace SandboxTimeline;

/// <summary>
/// Silent background auto-patcher: compares assembly version to a remote manifest,
/// downloads the latest release package, atomically swaps files, and relaunches the app.
/// </summary>
public sealed class UpdateManager
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly MainWindow _mainWindow;
    private readonly string[] _startupArguments;

    public UpdateManager(MainWindow mainWindow, string[] startupArguments)
    {
        _mainWindow = mainWindow;
        _startupArguments = startupArguments ?? Array.Empty<string>();
    }

    /// <summary>
    /// Returns true when an update was staged and the current process must exit for relaunch.
    /// Returns false when no update is needed or when any network/patch error occurs (bypass).
    /// </summary>
    public async Task<bool> TrySilentUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!UpdateConfiguration.IsAutoUpdateEnabled())
        {
            StartupDiagnostics.Log("Silent auto-update disabled (default). Set SANDBOXTIMELINE_UPDATE_ENABLED=1 to enable.");
            return false;
        }

        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (UpdateConfiguration.IsUnsafeAutoUpdateTargetDirectory(targetDirectory))
        {
            StartupDiagnostics.Log(
                $"Silent auto-update bypassed: unsafe install folder '{targetDirectory}'. Move the app out of OneDrive/sync folders.");
            return false;
        }

        if (_startupArguments.Any(argument =>
                string.Equals(argument, UpdateConfiguration.SkipUpdateArgument, StringComparison.OrdinalIgnoreCase)))
        {
            StartupDiagnostics.Log("Silent auto-update skipped (--skip-update).");
            return false;
        }

        try
        {
            var currentVersion = GetCurrentAssemblyVersion();
            StartupDiagnostics.Log($"Silent auto-update check started. Current version={currentVersion}.");

            var manifest = await FetchVersionManifestAsync(cancellationToken).ConfigureAwait(false);
            if (manifest == null)
            {
                StartupDiagnostics.Log("Silent auto-update bypassed: manifest unavailable.");
                return false;
            }

            if (!Version.TryParse(manifest.LatestVersion, out var remoteVersion))
            {
                StartupDiagnostics.Log($"Silent auto-update bypassed: invalid remote version '{manifest.LatestVersion}'.");
                return false;
            }

            if (remoteVersion <= currentVersion)
            {
                StartupDiagnostics.Log($"Silent auto-update bypassed: already up to date ({currentVersion}).");
                return false;
            }

            ReportStatus("Str_AutoUpdateApplying");

            var updateWorkspace = Path.Combine(
                Path.GetTempPath(),
                "SandboxTimeline",
                "Updates",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updateWorkspace);

            var downloadedPackagePath = await DownloadPackageAsync(
                    manifest.PackageDownloadUrl,
                    updateWorkspace,
                    cancellationToken)
                .ConfigureAwait(false);

            var stagingDirectory = await ExtractPackageAsync(
                    downloadedPackagePath,
                    updateWorkspace,
                    cancellationToken)
                .ConfigureAwait(false);

            var executablePath = ResolveApplicationExecutablePath(targetDirectory);
            if (!File.Exists(executablePath))
            {
                StartupDiagnostics.Log($"Silent auto-update bypassed: executable not found at '{executablePath}'.");
                return false;
            }

            LaunchAtomicSwapAndRelaunch(
                Process.GetCurrentProcess().Id,
                stagingDirectory,
                targetDirectory,
                executablePath);

            StartupDiagnostics.Log(
                $"Silent auto-update staged. Remote={remoteVersion}, staging='{stagingDirectory}', target='{targetDirectory}'.");
            return true;
        }
        catch (HttpRequestException ex)
        {
            StartupDiagnostics.Log("Silent auto-update bypassed due to network error.", ex);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            StartupDiagnostics.Log("Silent auto-update bypassed due to timeout.", ex);
            return false;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Silent auto-update bypassed due to unexpected error.", ex);
            return false;
        }
    }

    private void ReportStatus(string resourceKey)
    {
        try
        {
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.ReportAutoUpdateStatus(Loc.Get(resourceKey)));
        }
        catch
        {
        }
    }

    private static Version GetCurrentAssemblyVersion()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName().Version;
        return assembly ?? new Version(1, 0, 0, 0);
    }

    private async Task<RemoteVersionManifest?> FetchVersionManifestAsync(CancellationToken cancellationToken)
    {
        var manifestUrl = UpdateConfiguration.ResolveVersionManifestUrl();
        using var response = await SharedHttpClient.GetAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var manifestText = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(manifestText))
        {
            return null;
        }

        using var reader = new StringReader(manifestText);
        var versionLine = reader.ReadLine()?.Trim();
        var packageUrlLine = reader.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(versionLine) || string.IsNullOrWhiteSpace(packageUrlLine))
        {
            return null;
        }

        return new RemoteVersionManifest(versionLine, packageUrlLine);
    }

    private async Task<string> DownloadPackageAsync(
        string packageUrl,
        string updateWorkspace,
        CancellationToken cancellationToken)
    {
        ReportStatus("Str_AutoUpdateDownloading");

        var extension = Path.GetExtension(new Uri(packageUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".zip";
        }

        var packagePath = Path.Combine(updateWorkspace, $"SandboxTimeline-update{extension}");

        using var response = await SharedHttpClient.GetAsync(
            packageUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var localStream = new FileStream(
            packagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await remoteStream.CopyToAsync(localStream, cancellationToken).ConfigureAwait(false);

        return packagePath;
    }

    private async Task<string> ExtractPackageAsync(
        string downloadedPackagePath,
        string updateWorkspace,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(downloadedPackagePath);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractDirectory = Path.Combine(updateWorkspace, "extracted");
            Directory.CreateDirectory(extractDirectory);
            await Task.Run(() => ZipFile.ExtractToDirectory(downloadedPackagePath, extractDirectory, overwriteFiles: true), cancellationToken)
                .ConfigureAwait(false);
            return ResolvePayloadRootDirectory(extractDirectory);
        }

        if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            var singleFileStage = Path.Combine(updateWorkspace, "extracted");
            Directory.CreateDirectory(singleFileStage);
            var targetExe = Path.Combine(singleFileStage, Path.GetFileName(downloadedPackagePath));
            File.Copy(downloadedPackagePath, targetExe, overwrite: true);
            return singleFileStage;
        }

        throw new InvalidOperationException($"Unsupported update package type '{extension}'.");
    }

    private static string ResolvePayloadRootDirectory(string extractDirectory)
    {
        var directExecutable = Path.Combine(extractDirectory, "SandboxTimeline.exe");
        if (File.Exists(directExecutable))
        {
            return extractDirectory;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(extractDirectory))
        {
            var nestedExecutable = Path.Combine(subdirectory, "SandboxTimeline.exe");
            if (File.Exists(nestedExecutable))
            {
                return subdirectory;
            }
        }

        throw new FileNotFoundException(
            "Update package does not contain SandboxTimeline.exe.",
            directExecutable);
    }

    private static string ResolveApplicationExecutablePath(string targetDirectory)
    {
        var besideDll = Path.Combine(targetDirectory, "SandboxTimeline.exe");
        if (File.Exists(besideDll))
        {
            return besideDll;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        return besideDll;
    }

    private static void LaunchAtomicSwapAndRelaunch(
        int currentProcessId,
        string stagingDirectory,
        string targetDirectory,
        string executablePath)
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), "SandboxTimeline", "UpdateScripts");
        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(scriptDirectory, $"apply-update-{Guid.NewGuid():N}.cmd");
        var scriptBuilder = new StringBuilder();
        scriptBuilder.AppendLine("@echo off");
        scriptBuilder.AppendLine("setlocal EnableExtensions");
        scriptBuilder.AppendLine($"set TARGET_PID={currentProcessId}");
        scriptBuilder.AppendLine($"set STAGING={QuoteForCmd(stagingDirectory)}");
        scriptBuilder.AppendLine($"set TARGET={QuoteForCmd(targetDirectory)}");
        scriptBuilder.AppendLine($"set EXE={QuoteForCmd(executablePath)}");
        scriptBuilder.AppendLine(":WAIT");
        scriptBuilder.AppendLine("tasklist /FI \"PID eq %TARGET_PID%\" 2>nul | find \"%TARGET_PID%\" >nul");
        scriptBuilder.AppendLine("if %ERRORLEVEL%==0 (");
        scriptBuilder.AppendLine("  timeout /t 1 /nobreak >nul");
        scriptBuilder.AppendLine("  goto WAIT");
        scriptBuilder.AppendLine(")");
        scriptBuilder.AppendLine("robocopy \"%STAGING%\" \"%TARGET%\" /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS /NC /NS");
        scriptBuilder.AppendLine("set ROBO=%ERRORLEVEL%");
        scriptBuilder.AppendLine("if %ROBO% GEQ 8 exit /b %ROBO%");
        if (IsRunningAsAdministrator())
        {
            scriptBuilder.AppendLine("start \"\" /D \"%TARGET%\" \"%EXE%\" --skip-update");
        }
        else
        {
            scriptBuilder.AppendLine(
                "powershell -NoProfile -WindowStyle Hidden -Command \"Start-Process -FilePath '%EXE%' -ArgumentList '--skip-update' -Verb RunAs -WorkingDirectory '%TARGET%'\"");
        }

        scriptBuilder.AppendLine("del \"%~f0\"");
        File.WriteAllText(scriptPath, scriptBuilder.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = targetDirectory
        });
    }

    private static string QuoteForCmd(string value) => value.Replace("\"", "\"\"");

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private sealed record RemoteVersionManifest(string LatestVersion, string PackageDownloadUrl);
}
