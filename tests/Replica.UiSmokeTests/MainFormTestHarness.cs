using System.Reflection;
using System.Runtime.ExceptionServices;
using Replica;

namespace Replica.UiSmokeTests;

internal static class MainFormTestHarness
{
    private static readonly object SettingsLock = new();

    public static void RunWithIsolatedForm(Action<MainForm, string> action, int timeoutSeconds = 90)
    {
        RunWithIsolatedFormCore(action, configureSettings: null, timeoutSeconds);
    }

    public static void RunWithIsolatedForm(
        Action<MainForm, string> action,
        Action<AppSettings> configureSettings,
        int timeoutSeconds = 90)
    {
        RunWithIsolatedFormCore(action, configureSettings, timeoutSeconds);
    }

    private static void RunWithIsolatedFormCore(
        Action<MainForm, string> action,
        Action<AppSettings>? configureSettings,
        int timeoutSeconds)
    {
        RunInSta(() =>
        {
            lock (SettingsLock)
            {
                var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_MainForm_CoreTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRootPath);

                var settingsPath = AppSettings.FileName;
                var settingsBackupPath = settingsPath + ".bak.autotest";
                var hadSettingsFile = File.Exists(settingsPath);

                try
                {
                    if (hadSettingsFile)
                        File.Copy(settingsPath, settingsBackupPath, overwrite: true);

                    ConfigureIsolatedSettings(tempRootPath, configureSettings);

                    using var form = new MainForm();
                    _ = form.Handle;
                    action(form, tempRootPath);
                }
                finally
                {
                    try
                    {
                        if (hadSettingsFile && File.Exists(settingsBackupPath))
                            File.Copy(settingsBackupPath, settingsPath, overwrite: true);
                        else if (!hadSettingsFile && File.Exists(settingsPath))
                            File.Delete(settingsPath);
                    }
                    catch
                    {
                        // Ignore restore races in test cleanup.
                    }

                    try
                    {
                        if (File.Exists(settingsBackupPath))
                            File.Delete(settingsBackupPath);
                    }
                    catch
                    {
                        // Ignore cleanup races.
                    }

                    try
                    {
                        if (Directory.Exists(tempRootPath))
                            Directory.Delete(tempRootPath, recursive: true);
                    }
                    catch
                    {
                        // Ignore temp cleanup races.
                    }
                }
            }
        }, timeoutSeconds);
    }

    public static object? InvokePrivate(object target, string methodName, params object?[]? args)
    {
        var method = FindMethod(target.GetType(), methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method.Invoke(target, args);
    }

    public static MethodInfo GetPrivateMethod(object target, string methodName, BindingFlags scopeFlags)
    {
        var method = FindMethod(target.GetType(), methodName, scopeFlags | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method;
    }

    public static FieldInfo GetPrivateFieldInfo(object target, string fieldName, BindingFlags scopeFlags)
    {
        var field = FindField(target.GetType(), fieldName, scopeFlags | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        return field;
    }

    public static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = FindField(target.GetType(), fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        var value = field.GetValue(target);
        if (value is T typed)
            return typed;

        throw new InvalidOperationException($"Field '{fieldName}' is not of expected type {typeof(T).FullName}.");
    }

    public static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = FindField(target.GetType(), fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
    }

    public static void SetPrivateEnumFieldByValue(object target, string fieldName, int enumValue)
    {
        var field = FindField(target.GetType(), fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        var typedValue = Enum.ToObject(field.FieldType, enumValue);
        field.SetValue(target, typedValue);
    }

    private static MethodInfo? FindMethod(Type? type, string methodName, BindingFlags flags)
    {
        while (type != null)
        {
            var method = type.GetMethod(methodName, flags | BindingFlags.DeclaredOnly);
            if (method != null)
                return method;
            type = type.BaseType;
        }

        return null;
    }

    private static FieldInfo? FindField(Type? type, string fieldName, BindingFlags flags)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName, flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    private static void ConfigureIsolatedSettings(string tempRootPath, Action<AppSettings>? configureSettings)
    {
        var ordersRootPath = Path.Combine(tempRootPath, "Orders");
        var grandpaPath = Path.Combine(tempRootPath, "Grandpa");
        var tempFolderPath = Path.Combine(ordersRootPath, "TempReplica");
        var appDataPath = Path.Combine(tempRootPath, "AppData");
        var configPath = Path.Combine(tempRootPath, "Config");
        var usersSourcePath = Path.Combine(configPath, "users.json");
        var usersCachePath = Path.Combine(appDataPath, "users.cache.json");
        var historyPath = Path.Combine(appDataPath, "history.json");
        var managerLogPath = Path.Combine(appDataPath, "manager.log");
        var orderLogsPath = Path.Combine(appDataPath, "order-logs");
        var pitstopConfigPath = Path.Combine(configPath, "pitstop_actions.json");
        var imposingConfigPath = Path.Combine(configPath, "imposing_configs.json");
        var pitstopRootPath = Path.Combine(tempRootPath, "WARNING NOT DELETE", "PitStop");
        var imposingRootPath = Path.Combine(tempRootPath, "WARNING NOT DELETE", "HotImposing");

        Directory.CreateDirectory(ordersRootPath);
        Directory.CreateDirectory(grandpaPath);
        Directory.CreateDirectory(tempFolderPath);
        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(configPath);
        Directory.CreateDirectory(orderLogsPath);
        Directory.CreateDirectory(pitstopRootPath);
        Directory.CreateDirectory(imposingRootPath);

        File.WriteAllText(usersSourcePath, "[\"QA User\",\"Operator\",\"Сервер \\\"Таудеми\\\"\"]");
        File.WriteAllText(historyPath, "[]");
        File.WriteAllText(pitstopConfigPath, "[]");
        File.WriteAllText(imposingConfigPath, "[]");

        var settings = new AppSettings
        {
            OrdersRootPath = ordersRootPath,
            GrandpaPath = grandpaPath,
            ArchiveDoneSubfolder = "Готово",
            UseExtendedMode = false,
            TempFolderName = "TempReplica",
            TempFolderPath = tempFolderPath,
            HistoryFilePath = historyPath,
            ManagerLogFilePath = managerLogPath,
            OrderLogsFolderPath = orderLogsPath,
            UsersFilePath = usersSourcePath,
            UsersCacheFilePath = usersCachePath,
            PitStopConfigFilePath = pitstopConfigPath,
            ImposingConfigFilePath = imposingConfigPath,
            PitStopHotfoldersRootPath = pitstopRootPath,
            ImposingHotfoldersRootPath = imposingRootPath,
            SharedThumbnailCachePath = Path.Combine(tempRootPath, "Preview")
        };
        configureSettings?.Invoke(settings);
        settings.Save();
    }

    private static void RunInSta(Action action, int timeoutSeconds)
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
            throw new TimeoutException($"MainForm isolated test timed out after {timeoutSeconds} seconds.");

        if (capturedException != null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();
    }
}
