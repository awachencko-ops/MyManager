using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Replica.Shared;

namespace Replica;

public static class LanOrderPushEventNames
{
    public const string OrderUpdated = "OrderUpdated";
    public const string OrderDeleted = "OrderDeleted";
    public const string ForceRefresh = "ForceRefresh";
}

public static class LanOrderPushConnectionStates
{
    public const string Connected = "connected";
    public const string Reconnecting = "reconnecting";
    public const string Reconnected = "reconnected";
    public const string Closed = "closed";
    public const string StartFailed = "start-failed";
    public const string Stopped = "stopped";
}

public sealed record LanOrderPushEvent(
    string EventType,
    string OrderId,
    string Reason,
    DateTime OccurredAtUtc);

public sealed class LanOrderPushEventArgs : EventArgs
{
    public LanOrderPushEventArgs(LanOrderPushEvent pushEvent)
    {
        PushEvent = pushEvent ?? throw new ArgumentNullException(nameof(pushEvent));
    }

    public LanOrderPushEvent PushEvent { get; }
}

public sealed class LanOrderPushConnectionStateChangedEventArgs : EventArgs
{
    public LanOrderPushConnectionStateChangedEventArgs(
        string state,
        string message,
        Exception? error)
    {
        State = state ?? string.Empty;
        Message = message ?? string.Empty;
        Error = error;
    }

    public string State { get; }
    public string Message { get; }
    public Exception? Error { get; }
}

public static class LanOrderPushEventParser
{
    public static LanOrderPushEvent Parse(
        string eventType,
        object? payload,
        DateTime? fallbackUtc = null)
    {
        var normalizedEventType = string.IsNullOrWhiteSpace(eventType)
            ? LanOrderPushEventNames.ForceRefresh
            : eventType.Trim();

        var occurredAtUtc = fallbackUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var orderId = string.Empty;
        var reason = string.Empty;

        if (TryExtractPayload(payload, out var root))
        {
            orderId = ReadString(root, "orderId");
            reason = ReadString(root, "reason");

            if (TryReadUtcDateTime(root, "occurredAtUtc", out var parsedOccurredAtUtc))
                occurredAtUtc = parsedOccurredAtUtc;
        }

        if (string.Equals(normalizedEventType, LanOrderPushEventNames.OrderUpdated, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedEventType, LanOrderPushEventNames.OrderDeleted, StringComparison.OrdinalIgnoreCase))
        {
            reason = string.Empty;
        }
        else if (string.Equals(normalizedEventType, LanOrderPushEventNames.ForceRefresh, StringComparison.OrdinalIgnoreCase))
        {
            orderId = string.Empty;
        }

        return new LanOrderPushEvent(
            normalizedEventType,
            orderId,
            reason,
            occurredAtUtc);
    }

    private static bool TryExtractPayload(object? payload, out JsonElement root)
    {
        if (payload is JsonElement jsonElement)
        {
            root = jsonElement;
            return root.ValueKind == JsonValueKind.Object;
        }

        if (payload == null)
        {
            root = default;
            return false;
        }

        try
        {
            var serialized = JsonSerializer.SerializeToElement(payload);
            root = serialized;
            return root.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var valueElement))
            return string.Empty;
        if (valueElement.ValueKind != JsonValueKind.String)
            return string.Empty;

        return valueElement.GetString()?.Trim() ?? string.Empty;
    }

    private static bool TryReadUtcDateTime(JsonElement root, string propertyName, out DateTime value)
    {
        value = DateTime.MinValue;
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var valueElement))
            return false;
        if (valueElement.ValueKind != JsonValueKind.String)
            return false;

        var raw = valueElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }
}

public interface ILanOrderPushClient : IAsyncDisposable
{
    event EventHandler<LanOrderPushEventArgs>? EventReceived;
    event EventHandler<LanOrderPushConnectionStateChangedEventArgs>? ConnectionStateChanged;

    Task StartAsync(
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpLanOrderPushClient : ILanOrderPushClient
{
    public event EventHandler<LanOrderPushEventArgs>? EventReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<LanOrderPushConnectionStateChangedEventArgs>? ConnectionStateChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync(
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class LanOrderPushReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    public TimeSpan ResolveDelay(int previousRetryCount)
    {
        var normalizedRetryCount = Math.Max(0, previousRetryCount);
        if (normalizedRetryCount >= RetryDelays.Length)
            return RetryDelays[^1];

        return RetryDelays[normalizedRetryCount];
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var previousRetryCount = retryContext.PreviousRetryCount >= int.MaxValue
            ? int.MaxValue
            : (int)retryContext.PreviousRetryCount;
        return ResolveDelay(previousRetryCount);
    }
}

public sealed class SignalRLanOrderPushClient : ILanOrderPushClient
{
    private readonly object _sync = new();
    private readonly ILanApiAuthSessionStore _authSessionStore;
    private readonly IRetryPolicy _reconnectPolicy;
    private HubConnection? _connection;
    private string _activeHubUrl = string.Empty;
    private string _activeActor = string.Empty;
    private bool _isDisposed;

    public SignalRLanOrderPushClient(
        ILanApiAuthSessionStore? authSessionStore = null,
        IRetryPolicy? reconnectPolicy = null)
    {
        _authSessionStore = authSessionStore ?? new InMemoryLanApiAuthSessionStore();
        _reconnectPolicy = reconnectPolicy ?? new LanOrderPushReconnectPolicy();
    }

    public event EventHandler<LanOrderPushEventArgs>? EventReceived;
    public event EventHandler<LanOrderPushConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async Task StartAsync(
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!TryBuildHubUrl(apiBaseUrl, out var hubUrl))
            throw new InvalidOperationException("invalid LAN API base URL");

        var normalizedActor = string.IsNullOrWhiteSpace(actor)
            ? UserIdentityResolver.DefaultServerName
            : actor.Trim();

        HubConnection? connectionToDispose;
        HubConnection connectionToStart;
        lock (_sync)
        {
            if (_connection != null
                && string.Equals(_activeHubUrl, hubUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_activeActor, normalizedActor, StringComparison.Ordinal))
            {
                return;
            }

            connectionToDispose = _connection;
            connectionToStart = BuildConnection(hubUrl, normalizedActor);
            _connection = connectionToStart;
            _activeHubUrl = hubUrl;
            _activeActor = normalizedActor;
        }

        if (connectionToDispose != null)
            await DisposeConnectionAsync(connectionToDispose, cancellationToken).ConfigureAwait(false);

        try
        {
            await connectionToStart.StartAsync(cancellationToken).ConfigureAwait(false);
            RaiseConnectionStateChanged(
                LanOrderPushConnectionStates.Connected,
                $"Connected to {hubUrl}",
                error: null);
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_connection, connectionToStart))
                {
                    _connection = null;
                    _activeHubUrl = string.Empty;
                    _activeActor = string.Empty;
                }
            }

            await DisposeConnectionAsync(connectionToStart, cancellationToken).ConfigureAwait(false);
            RaiseConnectionStateChanged(
                LanOrderPushConnectionStates.StartFailed,
                ex.Message,
                ex);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        HubConnection? connectionToDispose;
        lock (_sync)
        {
            connectionToDispose = _connection;
            _connection = null;
            _activeHubUrl = string.Empty;
            _activeActor = string.Empty;
        }

        if (connectionToDispose == null)
            return;

        await DisposeConnectionAsync(connectionToDispose, cancellationToken).ConfigureAwait(false);
        RaiseConnectionStateChanged(
            LanOrderPushConnectionStates.Stopped,
            "Connection stopped",
            error: null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private HubConnection BuildConnection(string hubUrl, string actor)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(
                hubUrl,
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    options.AccessTokenProvider = ResolveAccessTokenAsync;
                    AddActorHeaders(options, actor);
                })
            .WithAutomaticReconnect(_reconnectPolicy);

        var connection = builder.Build();

        connection.On<object?>(LanOrderPushEventNames.OrderUpdated, payload =>
        {
            HandleIncomingEvent(LanOrderPushEventNames.OrderUpdated, payload);
        });
        connection.On<object?>(LanOrderPushEventNames.OrderDeleted, payload =>
        {
            HandleIncomingEvent(LanOrderPushEventNames.OrderDeleted, payload);
        });
        connection.On<object?>(LanOrderPushEventNames.ForceRefresh, payload =>
        {
            HandleIncomingEvent(LanOrderPushEventNames.ForceRefresh, payload);
        });

        connection.Reconnecting += error =>
        {
            RaiseConnectionStateChanged(
                LanOrderPushConnectionStates.Reconnecting,
                error?.Message ?? "Reconnecting",
                error);
            return Task.CompletedTask;
        };
        connection.Reconnected += connectionId =>
        {
            RaiseConnectionStateChanged(
                LanOrderPushConnectionStates.Reconnected,
                $"Reconnected ({connectionId ?? "n/a"})",
                error: null);
            return Task.CompletedTask;
        };
        connection.Closed += error =>
        {
            RaiseConnectionStateChanged(
                LanOrderPushConnectionStates.Closed,
                error?.Message ?? "Connection closed",
                error);
            return Task.CompletedTask;
        };

        return connection;
    }

    private Task<string?> ResolveAccessTokenAsync()
    {
        if (!_authSessionStore.TryGetActiveSession(out var session))
            return Task.FromResult<string?>(null);

        var accessToken = session.AccessToken?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(accessToken)
            ? Task.FromResult<string?>(null)
            : Task.FromResult<string?>(accessToken);
    }

    private static void AddActorHeaders(HttpConnectionOptions options, string actor)
    {
        if (options == null || string.IsNullOrWhiteSpace(actor))
            return;

        var normalizedActor = actor.Trim();
        if (CurrentUserHeaderCodec.RequiresEncoding(normalizedActor))
        {
            options.Headers[CurrentUserHeaderCodec.HeaderName] = CurrentUserHeaderCodec.BuildAsciiFallback(normalizedActor);
            options.Headers[CurrentUserHeaderCodec.EncodedHeaderName] = CurrentUserHeaderCodec.Encode(normalizedActor);
            return;
        }

        options.Headers[CurrentUserHeaderCodec.HeaderName] = normalizedActor;
    }

    private void HandleIncomingEvent(string eventType, object? payload)
    {
        var parsedEvent = LanOrderPushEventParser.Parse(eventType, payload, DateTime.UtcNow);
        try
        {
            EventReceived?.Invoke(this, new LanOrderPushEventArgs(parsedEvent));
        }
        catch
        {
            // Handlers are external to this adapter; push client should stay alive.
        }
    }

    private void RaiseConnectionStateChanged(string state, string message, Exception? error)
    {
        try
        {
            ConnectionStateChanged?.Invoke(
                this,
                new LanOrderPushConnectionStateChangedEventArgs(state, message, error));
        }
        catch
        {
            // Handlers are external to this adapter; push client should stay alive.
        }
    }

    private static async Task DisposeConnectionAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // no-op
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // no-op
        }
    }

    private static bool TryBuildHubUrl(string apiBaseUrl, out string hubUrl)
    {
        hubUrl = string.Empty;
        if (!TryResolveBaseUri(apiBaseUrl, out var baseUri))
            return false;

        var hubUri = new Uri(baseUri, "hubs/orders");
        hubUrl = hubUri.ToString();
        return true;
    }

    private static bool TryResolveBaseUri(string rawBaseUrl, out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(rawBaseUrl))
            return false;

        var candidate = rawBaseUrl.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsedBaseUri)
            && parsedBaseUri != null)
        {
            baseUri = parsedBaseUri;
            return true;
        }

        if (Uri.TryCreate($"http://{candidate}", UriKind.Absolute, out parsedBaseUri)
            && parsedBaseUri != null)
        {
            baseUri = parsedBaseUri;
            return true;
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SignalRLanOrderPushClient));
    }
}
