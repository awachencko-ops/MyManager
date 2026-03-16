using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaApplication = FlaUI.Core.Application;

namespace Replica.UiSmokeTests;

public sealed class MainFormSmokeTests
{
    [Fact]
    public void Launch_ShowsMainWindowAndJobsGrid()
    {
        RunInSta(() =>
        {
            using var session = UiSmokeSession.Start();

            Assert.False(session.MainWindow.IsOffscreen);

            var jobsGrid = session.MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("dgvJobs").Or(cf.ByControlType(ControlType.Table)));
            Assert.NotNull(jobsGrid);
        });
    }

    [Fact]
    public void Launch_ShowsMainToolbarButtons()
    {
        RunInSta(() =>
        {
            using var session = UiSmokeSession.Start();

            var buttons = session.MainWindow
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .Select(button => button.Name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("Создать", buttons);
            Assert.Contains("Запустить", buttons);
            Assert.Contains("Остановить", buttons);
            Assert.Contains("Удалить", buttons);
            Assert.Contains("Папка", buttons);
            Assert.Contains("Лог", buttons);
            Assert.Contains("Параметры", buttons);
        });
    }

    [Fact]
    public void Launch_ShowsTrayIndicatorTexts()
    {
        RunInSta(() =>
        {
            using var session = UiSmokeSession.Start();

            var statusTexts = session.MainWindow
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Select(text => text.Name?.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToList();

            Assert.NotEmpty(statusTexts);
            Assert.Contains(
                statusTexts,
                text => string.Equals(text, "\u0413\u043E\u0442\u043E\u0432\u043E", StringComparison.OrdinalIgnoreCase)
                        || text.StartsWith("\u0421\u0442\u0440\u043E\u043A:", StringComparison.OrdinalIgnoreCase)
                        || text.StartsWith("\u0421\u0432\u043E\u0431\u043E\u0434\u043D\u043E", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void RunInSta(Action action, int timeoutSeconds = 90)
    {
        Exception? capturedException = null;
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException($"STA smoke test timed out after {timeoutSeconds} seconds.");

        if (capturedException != null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();
    }
}

internal sealed class UiSmokeSession : IDisposable
{
    private readonly string _tempRootPath;
    private readonly FlaApplication _application;
    private readonly UIA3Automation _automation;

    private UiSmokeSession(string tempRootPath, FlaApplication application, UIA3Automation automation, Window mainWindow)
    {
        _tempRootPath = tempRootPath;
        _application = application;
        _automation = automation;
        MainWindow = mainWindow;
    }

    public Window MainWindow { get; }

    public static UiSmokeSession Start()
    {
        var sourceExePath = ResolveReplicaExePath();
        var sourceAppDirectory = Path.GetDirectoryName(sourceExePath)
            ?? throw new InvalidOperationException("Replica app directory is not resolved.");

        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_FlaUI_Smoke", Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(tempRootPath, "app");
        DirectoryCopy(sourceAppDirectory, appDirectory, recursive: true);

        var appDataDirectory = Path.Combine(tempRootPath, "AppData");
        var configDirectory = Path.Combine(tempRootPath, "Config");
        var ordersDirectory = Path.Combine(tempRootPath, "Orders");
        var tempOrdersDirectory = Path.Combine(ordersDirectory, "TempReplica");
        var grandpaDirectory = Path.Combine(tempRootPath, "Grandpa");
        var pitstopDirectory = Path.Combine(tempRootPath, "WARNING NOT DELETE", "PitStop");
        var imposingDirectory = Path.Combine(tempRootPath, "WARNING NOT DELETE", "HotImposing");

        Directory.CreateDirectory(appDataDirectory);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(ordersDirectory);
        Directory.CreateDirectory(tempOrdersDirectory);
        Directory.CreateDirectory(grandpaDirectory);
        Directory.CreateDirectory(pitstopDirectory);
        Directory.CreateDirectory(imposingDirectory);

        File.WriteAllText(Path.Combine(appDataDirectory, "history.json"), "[]");
        File.WriteAllText(Path.Combine(configDirectory, "users.json"), "[\"QA User\",\"Operator\"]");
        File.WriteAllText(Path.Combine(configDirectory, "pitstop_actions.json"), "[]");
        File.WriteAllText(Path.Combine(configDirectory, "imposing_configs.json"), "[]");

        var settingsPayload = new
        {
            OrdersRootPath = ordersDirectory,
            GrandpaPath = grandpaDirectory,
            ArchiveDoneSubfolder = "Готово",
            RunTimeoutMinutes = 10,
            UseExtendedMode = false,
            TempFolderName = "TempReplica",
            TempFolderPath = tempOrdersDirectory,
            SortArrivalDescending = true,
            HistoryFilePath = Path.Combine(appDataDirectory, "history.json"),
            ManagerLogFilePath = Path.Combine(appDataDirectory, "manager.log"),
            OrderLogsFolderPath = Path.Combine(appDataDirectory, "order-logs"),
            UsersFilePath = Path.Combine(configDirectory, "users.json"),
            UsersCacheFilePath = Path.Combine(appDataDirectory, "users.cache.json"),
            FontsFolderPath = string.Empty,
            SharedThumbnailCachePath = Path.Combine(tempRootPath, "Preview"),
            PitStopConfigFilePath = Path.Combine(configDirectory, "pitstop_actions.json"),
            ImposingConfigFilePath = Path.Combine(configDirectory, "imposing_configs.json"),
            PitStopHotfoldersRootPath = pitstopDirectory,
            ImposingHotfoldersRootPath = imposingDirectory,
            AllowManualSequenceReordering = true,
            MaxParallelism = 4,
            DefaultOrderSortBy = "SequenceNo",
            VariantDictionary = new[] { "A4", "A3", "draft", "final" },
            AutoRenameOnDuplicate = true
        };

        var settingsPath = Path.Combine(appDirectory, "settings.json");
        var settingsJson = JsonSerializer.Serialize(settingsPayload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, settingsJson);

        var appPath = Path.Combine(appDirectory, "Replica.exe");
        if (!File.Exists(appPath))
            throw new FileNotFoundException("Replica.exe is missing in smoke test runtime directory.", appPath);

        var startInfo = new ProcessStartInfo(appPath)
        {
            WorkingDirectory = appDirectory,
            UseShellExecute = false
        };

        var application = FlaApplication.Launch(startInfo);
        var automation = new UIA3Automation();
        var mainWindow = WaitForMainWindow(application, automation, timeoutSeconds: 25);
        if (mainWindow == null)
            throw new InvalidOperationException("Main window was not detected during smoke startup.");

        return new UiSmokeSession(tempRootPath, application, automation, mainWindow);
    }

    public void Dispose()
    {
        try
        {
            _application.Close();
        }
        catch
        {
            // Ignore close races in smoke cleanup.
        }

        try
        {
            var process = Process.GetProcessById(_application.ProcessId);
            if (!process.HasExited)
            {
                if (!process.WaitForExit(4000))
                    process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup races.
        }

        try
        {
            _automation.Dispose();
        }
        catch
        {
            // Ignore cleanup races.
        }

        try
        {
            if (Directory.Exists(_tempRootPath))
                Directory.Delete(_tempRootPath, recursive: true);
        }
        catch
        {
            // Ignore temp cleanup issues.
        }
    }

    private static Window? WaitForMainWindow(FlaApplication application, UIA3Automation automation, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow <= deadline)
        {
            var mainWindow = application.GetMainWindow(automation);
            if (mainWindow != null)
                return mainWindow;

            Thread.Sleep(250);
        }

        return null;
    }

    private static string ResolveReplicaExePath()
    {
        var repoRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var exePath = Path.Combine(repoRootPath, "bin", "Debug", "net8.0-windows", "Replica.exe");

        if (File.Exists(exePath))
            return exePath;

        throw new FileNotFoundException(
            "Replica.exe not found. Build app first: dotnet build Replica.csproj",
            exePath);
    }

    private static void DirectoryCopy(string sourceDirectoryPath, string destinationDirectoryPath, bool recursive)
    {
        var sourceDirectory = new DirectoryInfo(sourceDirectoryPath);
        if (!sourceDirectory.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectoryPath}");

        Directory.CreateDirectory(destinationDirectoryPath);

        foreach (var file in sourceDirectory.GetFiles())
        {
            var destinationPath = Path.Combine(destinationDirectoryPath, file.Name);
            file.CopyTo(destinationPath, overwrite: true);
        }

        if (!recursive)
            return;

        foreach (var childDirectory in sourceDirectory.GetDirectories())
        {
            var childDestinationPath = Path.Combine(destinationDirectoryPath, childDirectory.Name);
            DirectoryCopy(childDirectory.FullName, childDestinationPath, recursive);
        }
    }
}
