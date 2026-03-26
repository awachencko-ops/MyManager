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

public interface ILanOrderWriteApiGateway
{
    Task<LanOrderWriteApiResult> CreateOrderAsync(
        string apiBaseUrl,
        LanCreateOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> DeleteOrderAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanDeleteOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> UpdateOrderAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanUpdateOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> ReorderOrderItemsAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanReorderOrderItemsRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> AddOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanAddOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> UpdateOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        string itemId,
        LanUpdateOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<LanOrderWriteApiResult> DeleteOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        string itemId,
        LanDeleteOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed class LanOrderWriteApiGateway : ILanOrderWriteApiGateway
{
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public LanOrderWriteApiGateway(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<LanOrderWriteApiResult> CreateOrderAsync(
        string apiBaseUrl,
        LanCreateOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));

        if (!TryBuildCreateUri(apiBaseUrl, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Post,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> DeleteOrderAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanDeleteOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));

        if (!TryBuildUpdateUri(apiBaseUrl, orderInternalId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Delete,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> UpdateOrderAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanUpdateOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));

        if (!TryBuildUpdateUri(apiBaseUrl, orderInternalId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Patch,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> ReorderOrderItemsAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanReorderOrderItemsRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));

        if (!TryBuildReorderUri(apiBaseUrl, orderInternalId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Post,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> AddOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        LanAddOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));

        if (!TryBuildAddItemUri(apiBaseUrl, orderInternalId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Post,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> UpdateOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        string itemId,
        LanUpdateOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));
        if (string.IsNullOrWhiteSpace(itemId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("item id is required"));

        if (!TryBuildUpdateItemUri(apiBaseUrl, orderInternalId, itemId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Patch,
            request,
            actor,
            cancellationToken);
    }

    public Task<LanOrderWriteApiResult> DeleteOrderItemAsync(
        string apiBaseUrl,
        string orderInternalId,
        string itemId,
        LanDeleteOrderItemRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("request body is required"));
        if (string.IsNullOrWhiteSpace(orderInternalId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("order internal id is required"));
        if (string.IsNullOrWhiteSpace(itemId))
            return Task.FromResult(LanOrderWriteApiResult.BadRequest("item id is required"));

        if (!TryBuildUpdateItemUri(apiBaseUrl, orderInternalId, itemId, out var requestUri))
            return Task.FromResult(LanOrderWriteApiResult.Unavailable("invalid LAN API base URL"));

        return SendAsync(
            requestUri,
            HttpMethod.Delete,
            request,
            actor,
            cancellationToken);
    }

    private async Task<LanOrderWriteApiResult> SendAsync(
        Uri requestUri,
        HttpMethod method,
        object body,
        string actor,
        CancellationToken cancellationToken)
    {
        using var correlationScope = Logger.BeginCorrelationScope();
        using var logScope = Logger.BeginScope(
            ("component", "lan_order_write_api_gateway"),
            ("method", method.Method),
            ("target", requestUri.AbsolutePath));

        try
        {
            var correlationId = Logger.EnsureCorrelationId();
            var idempotencyKey = BuildIdempotencyKey(correlationId, method.Method, requestUri.AbsolutePath, body);
            using var request = new HttpRequestMessage(method, requestUri)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };

            request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);
            request.Headers.TryAddWithoutValidation(IdempotencyHeaderName, idempotencyKey);
            TryAddActorHeader(request, actor);

            Logger.Info($"LAN-API | write-send | method={method.Method} | target={requestUri} | idempotency_key={idempotencyKey}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Logger.Info($"LAN-API | write-response | status={(int)response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var order = DeserializeOrder(payload);
                if (order != null)
                    return LanOrderWriteApiResult.Success(order);

                return LanOrderWriteApiResult.Failed("LAN API returned malformed order payload");
            }

            var apiError = DeserializeError(payload);
            var errorMessage = !string.IsNullOrWhiteSpace(apiError.Error)
                ? apiError.Error!
                : $"LAN API returned {(int)response.StatusCode} ({response.StatusCode})";

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => LanOrderWriteApiResult.Conflict(errorMessage, apiError.CurrentVersion ?? 0),
                HttpStatusCode.NotFound => LanOrderWriteApiResult.NotFound(errorMessage),
                HttpStatusCode.BadRequest => LanOrderWriteApiResult.BadRequest(errorMessage),
                _ => LanOrderWriteApiResult.Failed(errorMessage)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("LAN-API | write-timeout");
            return LanOrderWriteApiResult.Unavailable("LAN API request timed out");
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn($"LAN-API | write-http-error | {ex.Message}");
            return LanOrderWriteApiResult.Unavailable(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error($"LAN-API | write-failed | {ex.Message}");
            return LanOrderWriteApiResult.Failed(ex.Message);
        }
    }

    private static bool TryBuildCreateUri(string apiBaseUrl, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        requestUri = new Uri(baseUri, "api/orders");
        return true;
    }

    private static bool TryBuildUpdateUri(string apiBaseUrl, string orderInternalId, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        var orderIdSegment = Uri.EscapeDataString(orderInternalId.Trim());
        requestUri = new Uri(baseUri, $"api/orders/{orderIdSegment}");
        return true;
    }

    private static bool TryBuildReorderUri(string apiBaseUrl, string orderInternalId, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        var orderIdSegment = Uri.EscapeDataString(orderInternalId.Trim());
        requestUri = new Uri(baseUri, $"api/orders/{orderIdSegment}/items/reorder");
        return true;
    }

    private static bool TryBuildAddItemUri(string apiBaseUrl, string orderInternalId, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        var orderIdSegment = Uri.EscapeDataString(orderInternalId.Trim());
        requestUri = new Uri(baseUri, $"api/orders/{orderIdSegment}/items");
        return true;
    }

    private static bool TryBuildUpdateItemUri(string apiBaseUrl, string orderInternalId, string itemId, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        var orderIdSegment = Uri.EscapeDataString(orderInternalId.Trim());
        var itemIdSegment = Uri.EscapeDataString(itemId.Trim());
        requestUri = new Uri(baseUri, $"api/orders/{orderIdSegment}/items/{itemIdSegment}");
        return true;
    }

    private static void TryAddActorHeader(HttpRequestMessage request, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return;

        var normalizedActor = actor.Trim();
        request.Headers.TryAddWithoutValidation(CurrentUserHeaderCodec.HeaderName, normalizedActor);

        if (CurrentUserHeaderCodec.RequiresEncoding(normalizedActor))
        {
            request.Headers.TryAddWithoutValidation(
                CurrentUserHeaderCodec.EncodedHeaderName,
                CurrentUserHeaderCodec.Encode(normalizedActor));
        }
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

    private static string BuildIdempotencyKey(string correlationId, string httpMethod, string requestPath, object body)
    {
        var bodyJson = body == null ? string.Empty : JsonSerializer.Serialize(body, JsonOptions);
        var source = $"{correlationId}|{httpMethod}|{requestPath}|{bodyJson}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        var shortHash = Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        return $"replica-write-{shortHash}";
    }

    private sealed class ApiErrorResponse
    {
        public string? Error { get; set; }
        public long? CurrentVersion { get; set; }
    }
}

public sealed class LanCreateOrderRequest
{
    public string OrderNumber { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CreatedById { get; set; } = string.Empty;
    public string CreatedByUser { get; set; } = string.Empty;
    public string Status { get; set; } = WorkflowStatusNames.Waiting;
    public string Keyword { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public SharedOrderStartMode StartMode { get; set; } = SharedOrderStartMode.Unknown;
    public SharedOrderTopologyMarker TopologyMarker { get; set; } = SharedOrderTopologyMarker.Unknown;
    public string PitStopAction { get; set; } = "-";
    public string ImposingAction { get; set; } = "-";
    public DateTime? ArrivalDate { get; set; }
    public DateTime? ManagerOrderDate { get; set; }
    public System.Collections.Generic.List<SharedOrderItem>? Items { get; set; }
}

public sealed class LanUpdateOrderRequest
{
    public long ExpectedVersion { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime? ManagerOrderDate { get; set; }
    public string? UserName { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }
    public string? FolderName { get; set; }
    public string? PitStopAction { get; set; }
    public string? ImposingAction { get; set; }
}

public sealed class LanDeleteOrderRequest
{
    public long ExpectedVersion { get; set; }
}

public sealed class LanReorderOrderItemsRequest
{
    public long ExpectedOrderVersion { get; set; }
    public System.Collections.Generic.List<string> OrderedItemIds { get; set; } = new();
}

public sealed class LanAddOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public SharedOrderItem Item { get; set; } = new();
}

public sealed class LanUpdateOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public long ExpectedItemVersion { get; set; }
    public string? ClientFileLabel { get; set; }
    public string? Variant { get; set; }
    public string? FileStatus { get; set; }
    public string? LastReason { get; set; }
    public string? SourcePath { get; set; }
    public string? PreparedPath { get; set; }
    public string? PrintPath { get; set; }
    public string? SourceFileHash { get; set; }
    public string? PreparedFileHash { get; set; }
    public string? PrintFileHash { get; set; }
    public string? PitStopAction { get; set; }
    public string? ImposingAction { get; set; }
}

public sealed class LanDeleteOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public long ExpectedItemVersion { get; set; }
}

public sealed class LanOrderWriteApiResult
{
    private LanOrderWriteApiResult(
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

    public static LanOrderWriteApiResult Success(SharedOrder order) => new(
        isSuccess: true,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: string.Empty,
        currentVersion: order?.Version ?? 0,
        order: order);

    public static LanOrderWriteApiResult Conflict(string error, long currentVersion) => new(
        isSuccess: false,
        isConflict: true,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: currentVersion,
        order: null);

    public static LanOrderWriteApiResult NotFound(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: true,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteApiResult BadRequest(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: true,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteApiResult Unavailable(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: true,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteApiResult Failed(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        currentVersion: 0,
        order: null);
}
