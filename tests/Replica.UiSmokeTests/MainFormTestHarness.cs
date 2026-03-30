using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
                using var lanApiStub = IsolatedLanApiStub.Start();

                var settingsPath = AppSettings.FileName;
                var settingsBackupPath = settingsPath + ".bak.autotest";
                var hadSettingsFile = File.Exists(settingsPath);

                try
                {
                    if (hadSettingsFile)
                        File.Copy(settingsPath, settingsBackupPath, overwrite: true);

                    ConfigureIsolatedSettings(tempRootPath, lanApiStub.BaseUrl, configureSettings);

                    using var form = new MainForm();
                    _ = form.Handle;
                    SetPrivateField(form, "_ordersStorageBackend", OrdersStorageMode.FileSystem);
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

    private static void ConfigureIsolatedSettings(string tempRootPath, string lanApiBaseUrl, Action<AppSettings>? configureSettings)
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
            SharedThumbnailCachePath = Path.Combine(tempRootPath, "Preview"),
            // Stage 6 forces LAN mode, so tests must use an isolated API endpoint.
            LanApiBaseUrl = lanApiBaseUrl
        };
        configureSettings?.Invoke(settings);
        settings.Save();
    }

    private sealed class IsolatedLanApiStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serveLoop;

        private IsolatedLanApiStub(HttpListener listener, string baseUrl)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _serveLoop = Task.Run(ServeLoopAsync);
        }

        public string BaseUrl { get; }

        public static IsolatedLanApiStub Start()
        {
            var port = ReserveLoopbackPort();
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            return new IsolatedLanApiStub(listener, prefix);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // Ignore listener shutdown races in tests.
            }

            try
            {
                _serveLoop.GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore background loop cancellation/shutdown errors in tests.
            }
        }

        private async Task ServeLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Ignore transient listener failures in test stub loop.
                    continue;
                }

                _ = Task.Run(() => HandleRequest(context));
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                var method = context.Request.HttpMethod ?? "GET";
                var payload = ResolveResponse(path, method, out var statusCode);

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                using var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false));
                writer.Write(payload);
                writer.Flush();
                context.Response.Close();
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch
                {
                    // Ignore terminal response races in tests.
                }
            }
        }

        private static string ResolveResponse(string path, string method, out int statusCode)
        {
            if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"ok\",\"service\":\"Replica.Api\",\"mode\":\"PostgreSql\",\"authMode\":\"Strict\"}";
            }

            if (string.Equals(path, "/live", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"live\",\"service\":\"Replica.Api\",\"mode\":\"PostgreSql\",\"authMode\":\"Strict\"}";
            }

            if (string.Equals(path, "/ready", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"ready\",\"service\":\"Replica.Api\",\"pendingMigrations\":0}";
            }

            if (string.Equals(path, "/slo", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"ok\"}";
            }

            if (string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"ok\"}";
            }

            if (string.Equals(path, "/api/orders", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "[]";
            }

            if (string.Equals(path, "/api/users", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "[{\"id\":\"u-andrew\",\"name\":\"Andrew\",\"role\":\"Admin\",\"isActive\":true}]";
            }

            if (string.Equals(path, "/api/diagnostics/push", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"status\":\"ok\",\"operations\":[]}";
            }

            if (string.Equals(path, "/api/auth/me", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"name\":\"Andrew\",\"role\":\"Admin\",\"isAuthenticated\":true,\"isValidated\":true,\"canManageUsers\":true,\"authScheme\":\"Header\",\"sessionId\":\"\"}";
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = (int)HttpStatusCode.OK;
                return "{\"accessToken\":\"stub-token\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"name\":\"Andrew\",\"role\":\"Admin\",\"sessionId\":\"stub-session\"}";
            }

            statusCode = (int)HttpStatusCode.NotFound;
            return "{\"error\":\"not_found\"}";
        }

        private static int ReserveLoopbackPort()
        {
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            return ((IPEndPoint)tcp.LocalEndpoint).Port;
        }
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
