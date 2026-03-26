using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared;
using Replica.Shared.Models;

namespace Replica;

public interface ILanOrderRunApiGateway
{
    Task<LanOrderRunApiResult> StartRunAsync(
        string apiBaseUrl,
        string orderInternalId,
        long expectedOrderVersion,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderRunApiResult> StopRunAsync(
        string apiBaseUrl,
        string orderInternalId,
        long expectedOrderVersion,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed class LanOrderRunApiGateway : ILanOrderRunApiGateway
{
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public LanOrderRunApiGateway(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<LanOrderRunApiResult> StartRunAsync(
        string apiBaseUrl,
        string orderInternalId,
        long expectedOrderVersion,
        string actor,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            apiBaseUrl,
            orderInternalId,
            command: "run",
            expectedOrderVersion,
            actor,
            cancellationToken);
    }

    public Task<LanOrderRunApiResult> StopRunAsync(
        string apiBaseUrl,
        string orderInternalId,
        long expectedOrderVersion,
        string actor,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            apiBaseUrl,
            orderInternalId,
            command: "stop",
            expectedOrderVersion,
            actor,
            cancellationToken);
    }

    private async Task<LanOrderRunApiResult> SendCommandAsync(
        string apiBaseUrl,
        string orderInternalId,
        string command,
        long expectedOrderVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        using var correlationScope = Logger.BeginCorrelationScope();
        using var logScope = Logger.BeginScope(
            ("component", "lan_order_run_api_gateway"),
            ("api_command", command),
            ("order_internal_id", orderInternalId),
            ("expected_order_version", expectedOrderVersion.ToString()));

        if (string.IsNullOrWhiteSpace(orderInternalId))
            return LanOrderRunApiResult.BadRequest("order internal id is required");

        if (!TryBuildCommandUri(apiBaseUrl, orderInternalId, command, out var requestUri))
            return LanOrderRunApiResult.Unavailable("invalid LAN API base URL");

        try
        {
            var correlationId = Logger.EnsureCorrelationId();
            var idempotencyKey = BuildIdempotencyKey(correlationId, command, orderInternalId, expectedOrderVersion);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(
                    new RunCommandRequest
                    {
                        ExpectedOrderVersion = expectedOrderVersion
                    },
                    options: JsonOptions)
            };

            request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);
            request.Headers.TryAddWithoutValidation(IdempotencyHeaderName, idempotencyKey);
            TryAddActorHeader(request, actor);

            Logger.Info($"LAN-API | command-send | target={requestUri} | idempotency_key={idempotencyKey}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Logger.Info($"LAN-API | command-response | status={(int)response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var order = DeserializeOrder(payload);
                if (order != null)
                    return LanOrderRunApiResult.Success(order);

                return LanOrderRunApiResult.Failed("LAN API returned malformed order payload");
            }

            var apiError = DeserializeError(payload);
            var errorMessage = !string.IsNullOrWhiteSpace(apiError.Error)
                ? apiError.Error!
                : $"LAN API returned {(int)response.StatusCode} ({response.StatusCode})";

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => LanOrderRunApiResult.Conflict(errorMessage, apiError.CurrentVersion ?? 0),
                HttpStatusCode.NotFound => LanOrderRunApiResult.NotFound(errorMessage),
                HttpStatusCode.BadRequest => LanOrderRunApiResult.BadRequest(errorMessage),
                _ => LanOrderRunApiResult.Failed(errorMessage)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("LAN-API | command-timeout");
            return LanOrderRunApiResult.Unavailable("LAN API request timed out");
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn($"LAN-API | command-http-error | {ex.Message}");
            return LanOrderRunApiResult.Unavailable(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error($"LAN-API | command-failed | {ex.Message}");
            return LanOrderRunApiResult.Failed(ex.Message);
        }
    }

    private static string BuildIdempotencyKey(string correlationId, string command, string orderInternalId, long expectedOrderVersion)
    {
        var source = $"{correlationId}|{command}|{orderInternalId}|{expectedOrderVersion}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        var shortHash = Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        return $"replica-{command}-{shortHash}";
    }

    private static void TryAddActorHeader(HttpRequestMessage request, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return;

        var normalizedActor = actor.Trim();
        if (CurrentUserHeaderCodec.RequiresEncoding(normalizedActor))
        {
            request.Headers.TryAddWithoutValidation(
                CurrentUserHeaderCodec.HeaderName,
                CurrentUserHeaderCodec.BuildAsciiFallback(normalizedActor));
            request.Headers.TryAddWithoutValidation(
                CurrentUserHeaderCodec.EncodedHeaderName,
                CurrentUserHeaderCodec.Encode(normalizedActor));
            return;
        }

        request.Headers.TryAddWithoutValidation(CurrentUserHeaderCodec.HeaderName, normalizedActor);
    }

    private static bool TryBuildCommandUri(string apiBaseUrl, string orderInternalId, string command, out Uri? requestUri)
    {
        requestUri = null;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        var orderIdSegment = Uri.EscapeDataString(orderInternalId.Trim());
        requestUri = new Uri(baseUri, $"api/orders/{orderIdSegment}/{command}");
        return true;
    }

    private static SharedOrder? DeserializeOrder(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SharedOrder>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ApiErrorResponse DeserializeError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new ApiErrorResponse();

        try
        {
            return JsonSerializer.Deserialize<ApiErrorResponse>(payload, JsonOptions) ?? new ApiErrorResponse();
        }
        catch
        {
            return new ApiErrorResponse();
        }
    }

    private sealed class RunCommandRequest
    {
        public long ExpectedOrderVersion { get; set; }
    }

    private sealed class ApiErrorResponse
    {
        public string? Error { get; set; }
        public long? CurrentVersion { get; set; }
    }
}

public sealed class LanOrderRunApiResult
{
    private LanOrderRunApiResult(
        bool isSuccess,
        bool isConflict,
        bool isNotFound,
        bool isBadRequest,
        bool isUnavailable,
        string error,
        long currentVersion,
        SharedOrder? order)
    {
        IsSuccess = isSuccess;
        IsConflict = isConflict;
        IsNotFound = isNotFound;
        IsBadRequest = isBadRequest;
        IsUnavailable = isUnavailable;
        Error = error;
        CurrentVersion = currentVersion;
        Order = order;
    }

    public bool IsSuccess { get; }
    public bool IsConflict { get; }
    public bool IsNotFound { get; }
    public bool IsBadRequest { get; }
    public bool IsUnavailable { get; }
    public string Error { get; }
    public long CurrentVersion { get; }
    public SharedOrder? Order { get; }

    public static LanOrderRunApiResult Success(SharedOrder order) => new(
        isSuccess: true,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: string.Empty,
        currentVersion: order?.Version ?? 0,
        order: order);

    public static LanOrderRunApiResult Conflict(string error, long currentVersion) => new(
        isSuccess: false,
        isConflict: true,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: currentVersion,
        order: null);

    public static LanOrderRunApiResult NotFound(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: true,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderRunApiResult BadRequest(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: true,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderRunApiResult Unavailable(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: true,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderRunApiResult Failed(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);
}
