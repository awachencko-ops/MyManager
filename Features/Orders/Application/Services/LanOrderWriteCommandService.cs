using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared.Models;

namespace Replica;

public sealed class LanOrderWriteCommandService
{
    private readonly ILanOrderWriteApiGateway _lanOrderWriteApiGateway;

    public LanOrderWriteCommandService(ILanOrderWriteApiGateway lanOrderWriteApiGateway)
    {
        _lanOrderWriteApiGateway = lanOrderWriteApiGateway ?? throw new ArgumentNullException(nameof(lanOrderWriteApiGateway));
    }

    public async Task<LanOrderWriteCommandResult> TryCreateOrderAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            return LanOrderWriteCommandResult.BadRequest("order is required");

        var request = BuildCreateRequest(order, actor, normalizeUserName);
        var apiResult = await _lanOrderWriteApiGateway.CreateOrderAsync(
            lanApiBaseUrl,
            request,
            actor,
            cancellationToken);

        return ToCommandResult(apiResult);
    }

    public async Task<LanOrderWriteCommandResult> TryUpdateOrderAsync(
        OrderData currentOrder,
        OrderData updatedOrder,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default)
    {
        if (currentOrder == null || updatedOrder == null)
            return LanOrderWriteCommandResult.BadRequest("order is required");

        if (string.IsNullOrWhiteSpace(currentOrder.InternalId))
            return LanOrderWriteCommandResult.BadRequest("order internal id is required");

        var request = BuildUpdateRequest(currentOrder, updatedOrder, normalizeUserName);
        var apiResult = await _lanOrderWriteApiGateway.UpdateOrderAsync(
            lanApiBaseUrl,
            currentOrder.InternalId,
            request,
            actor,
            cancellationToken);

        return ToCommandResult(apiResult);
    }

    public async Task<LanOrderWriteCommandResult> TryDeleteOrderAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            return LanOrderWriteCommandResult.BadRequest("order is required");

        if (string.IsNullOrWhiteSpace(order.InternalId))
            return LanOrderWriteCommandResult.BadRequest("order internal id is required");

        var request = new LanDeleteOrderRequest
        {
            ExpectedVersion = order.StorageVersion
        };
        var apiResult = await _lanOrderWriteApiGateway.DeleteOrderAsync(
            lanApiBaseUrl,
            order.InternalId,
            request,
            actor,
            cancellationToken);

        return ToCommandResult(apiResult);
    }

    public async Task<LanOrderWriteCommandResult> TryReorderItemsAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            return LanOrderWriteCommandResult.BadRequest("order is required");
        if (string.IsNullOrWhiteSpace(order.InternalId))
            return LanOrderWriteCommandResult.BadRequest("order internal id is required");

        var orderedIds = (order.Items ?? new List<OrderFileItem>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
            .OrderBy(item => item.SequenceNo)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .Select(item => item.ItemId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var request = new LanReorderOrderItemsRequest
        {
            ExpectedOrderVersion = order.StorageVersion,
            OrderedItemIds = orderedIds
        };

        var apiResult = await _lanOrderWriteApiGateway.ReorderOrderItemsAsync(
            lanApiBaseUrl,
            order.InternalId,
            request,
            actor,
            cancellationToken);

        return ToCommandResult(apiResult);
    }

    public async Task<LanOrderWriteCommandResult> TryUpsertItemAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (order == null || item == null)
            return LanOrderWriteCommandResult.BadRequest("order/item is required");
        if (string.IsNullOrWhiteSpace(order.InternalId))
            return LanOrderWriteCommandResult.BadRequest("order internal id is required");
        if (string.IsNullOrWhiteSpace(item.ItemId))
            return LanOrderWriteCommandResult.BadRequest("item id is required");

        LanOrderWriteApiResult apiResult;
        if (item.StorageVersion > 0)
        {
            var updateRequest = BuildUpdateItemRequest(order, item);
            apiResult = await _lanOrderWriteApiGateway.UpdateOrderItemAsync(
                lanApiBaseUrl,
                order.InternalId,
                item.ItemId,
                updateRequest,
                actor,
                cancellationToken);
        }
        else
        {
            var addRequest = BuildAddItemRequest(order, item);
            apiResult = await _lanOrderWriteApiGateway.AddOrderItemAsync(
                lanApiBaseUrl,
                order.InternalId,
                addRequest,
                actor,
                cancellationToken);
        }

        return ToCommandResult(apiResult);
    }

    public async Task<LanOrderWriteCommandResult> TryDeleteItemAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (order == null || item == null)
            return LanOrderWriteCommandResult.BadRequest("order/item is required");
        if (string.IsNullOrWhiteSpace(order.InternalId))
            return LanOrderWriteCommandResult.BadRequest("order internal id is required");
        if (string.IsNullOrWhiteSpace(item.ItemId))
            return LanOrderWriteCommandResult.BadRequest("item id is required");

        var request = BuildDeleteItemRequest(order, item);
        var apiResult = await _lanOrderWriteApiGateway.DeleteOrderItemAsync(
            lanApiBaseUrl,
            order.InternalId,
            item.ItemId,
            request,
            actor,
            cancellationToken);

        return ToCommandResult(apiResult);
    }

    private static LanCreateOrderRequest BuildCreateRequest(
        OrderData source,
        string actor,
        Func<string, string> normalizeUserName)
    {
        var normalizedUserName = normalizeUserName?.Invoke(source.UserName ?? string.Empty) ?? string.Empty;
        var request = new LanCreateOrderRequest
        {
            OrderNumber = source.Id?.Trim() ?? string.Empty,
            UserName = normalizedUserName,
            CreatedById = actor?.Trim() ?? string.Empty,
            CreatedByUser = actor?.Trim() ?? string.Empty,
            Status = NormalizeStatus(source.Status),
            Keyword = source.Keyword?.Trim() ?? string.Empty,
            FolderName = source.FolderName?.Trim() ?? string.Empty,
            StartMode = (SharedOrderStartMode)source.StartMode,
            TopologyMarker = (SharedOrderTopologyMarker)source.FileTopologyMarker,
            PitStopAction = NormalizeAction(source.PitStopAction),
            ImposingAction = NormalizeAction(source.ImposingAction),
            ArrivalDate = source.ArrivalDate == default ? null : source.ArrivalDate,
            ManagerOrderDate = source.OrderDate == default ? null : source.OrderDate,
            Items = BuildCreateItems(source)
        };

        return request;
    }

    private static List<SharedOrderItem> BuildCreateItems(OrderData source)
    {
        var mapped = new List<SharedOrderItem>();
        foreach (var item in (source.Items ?? new List<OrderFileItem>()).Where(item => item != null))
            mapped.Add(MapItem(item));

        if (mapped.Count > 0)
            return mapped;

        if (string.IsNullOrWhiteSpace(source.SourcePath)
            && string.IsNullOrWhiteSpace(source.PreparedPath)
            && string.IsNullOrWhiteSpace(source.PrintPath))
        {
            return mapped;
        }

        mapped.Add(new SharedOrderItem
        {
            ItemId = Guid.NewGuid().ToString("N"),
            SequenceNo = 0,
            ClientFileLabel = source.Id?.Trim() ?? string.Empty,
            Variant = string.Empty,
            SourcePath = source.SourcePath ?? string.Empty,
            SourceFileSizeBytes = source.SourceFileSizeBytes,
            SourceFileHash = source.SourceFileHash ?? string.Empty,
            PreparedPath = source.PreparedPath ?? string.Empty,
            PreparedFileSizeBytes = source.PreparedFileSizeBytes,
            PreparedFileHash = source.PreparedFileHash ?? string.Empty,
            PrintPath = source.PrintPath ?? string.Empty,
            PrintFileSizeBytes = source.PrintFileSizeBytes,
            PrintFileHash = source.PrintFileHash ?? string.Empty,
            FileStatus = NormalizeStatus(source.Status),
            LastReason = source.LastStatusReason ?? string.Empty,
            UpdatedAt = DateTime.Now,
            PitStopAction = NormalizeAction(source.PitStopAction),
            ImposingAction = NormalizeAction(source.ImposingAction)
        });

        return mapped;
    }

    private static LanUpdateOrderRequest BuildUpdateRequest(
        OrderData currentOrder,
        OrderData updatedOrder,
        Func<string, string> normalizeUserName)
    {
        var normalizedUserName = normalizeUserName?.Invoke(updatedOrder.UserName ?? currentOrder.UserName ?? string.Empty) ?? string.Empty;
        return new LanUpdateOrderRequest
        {
            ExpectedVersion = currentOrder.StorageVersion,
            OrderNumber = updatedOrder.Id?.Trim() ?? string.Empty,
            ManagerOrderDate = updatedOrder.OrderDate == default ? null : updatedOrder.OrderDate,
            UserName = normalizedUserName,
            Status = NormalizeStatus(updatedOrder.Status),
            Keyword = updatedOrder.Keyword?.Trim() ?? string.Empty,
            FolderName = updatedOrder.FolderName?.Trim() ?? string.Empty,
            PitStopAction = NormalizeAction(updatedOrder.PitStopAction),
            ImposingAction = NormalizeAction(updatedOrder.ImposingAction)
        };
    }

    private static LanAddOrderItemRequest BuildAddItemRequest(OrderData order, OrderFileItem item)
    {
        return new LanAddOrderItemRequest
        {
            ExpectedOrderVersion = order.StorageVersion,
            Item = MapItem(item)
        };
    }

    private static LanUpdateOrderItemRequest BuildUpdateItemRequest(OrderData order, OrderFileItem item)
    {
        return new LanUpdateOrderItemRequest
        {
            ExpectedOrderVersion = order.StorageVersion,
            ExpectedItemVersion = item.StorageVersion,
            ClientFileLabel = item.ClientFileLabel ?? string.Empty,
            Variant = item.Variant ?? string.Empty,
            FileStatus = NormalizeStatus(item.FileStatus),
            LastReason = item.LastReason ?? string.Empty,
            SourcePath = item.SourcePath ?? string.Empty,
            PreparedPath = item.PreparedPath ?? string.Empty,
            PrintPath = item.PrintPath ?? string.Empty,
            SourceFileHash = item.SourceFileHash ?? string.Empty,
            PreparedFileHash = item.PreparedFileHash ?? string.Empty,
            PrintFileHash = item.PrintFileHash ?? string.Empty,
            PitStopAction = NormalizeAction(item.PitStopAction),
            ImposingAction = NormalizeAction(item.ImposingAction)
        };
    }

    private static LanDeleteOrderItemRequest BuildDeleteItemRequest(OrderData order, OrderFileItem item)
    {
        return new LanDeleteOrderItemRequest
        {
            ExpectedOrderVersion = order.StorageVersion,
            ExpectedItemVersion = item.StorageVersion
        };
    }

    private static SharedOrderItem MapItem(OrderFileItem source)
    {
        return new SharedOrderItem
        {
            ItemId = string.IsNullOrWhiteSpace(source.ItemId) ? Guid.NewGuid().ToString("N") : source.ItemId.Trim(),
            SequenceNo = source.SequenceNo,
            ClientFileLabel = source.ClientFileLabel ?? string.Empty,
            Variant = source.Variant ?? string.Empty,
            SourcePath = source.SourcePath ?? string.Empty,
            SourceFileSizeBytes = source.SourceFileSizeBytes,
            SourceFileHash = source.SourceFileHash ?? string.Empty,
            PreparedPath = source.PreparedPath ?? string.Empty,
            PreparedFileSizeBytes = source.PreparedFileSizeBytes,
            PreparedFileHash = source.PreparedFileHash ?? string.Empty,
            PrintPath = source.PrintPath ?? string.Empty,
            PrintFileSizeBytes = source.PrintFileSizeBytes,
            PrintFileHash = source.PrintFileHash ?? string.Empty,
            FileStatus = NormalizeStatus(source.FileStatus),
            LastReason = source.LastReason ?? string.Empty,
            UpdatedAt = source.UpdatedAt == default ? DateTime.Now : source.UpdatedAt,
            PitStopAction = NormalizeAction(source.PitStopAction),
            ImposingAction = NormalizeAction(source.ImposingAction)
        };
    }

    private static LanOrderWriteCommandResult ToCommandResult(LanOrderWriteApiResult apiResult)
    {
        if (apiResult == null)
            return LanOrderWriteCommandResult.Failed("LAN API returned empty response");

        if (apiResult.IsSuccess)
        {
            if (apiResult.Order == null)
                return LanOrderWriteCommandResult.Failed("LAN API returned empty order payload");

            var mappedOrder = MapOrder(apiResult.Order);
            return LanOrderWriteCommandResult.Success(mappedOrder);
        }

        if (apiResult.IsConflict)
            return LanOrderWriteCommandResult.Conflict(apiResult.Error, apiResult.CurrentVersion);
        if (apiResult.IsNotFound)
            return LanOrderWriteCommandResult.NotFound(apiResult.Error);
        if (apiResult.IsBadRequest)
            return LanOrderWriteCommandResult.BadRequest(apiResult.Error);
        if (apiResult.IsUnavailable)
            return LanOrderWriteCommandResult.Unavailable(apiResult.Error);

        return LanOrderWriteCommandResult.Failed(apiResult.Error);
    }

    private static OrderData MapOrder(SharedOrder source)
    {
        var mapped = new OrderData
        {
            InternalId = string.IsNullOrWhiteSpace(source.InternalId) ? Guid.NewGuid().ToString("N") : source.InternalId.Trim(),
            StorageVersion = source.Version,
            Id = source.OrderNumber?.Trim() ?? string.Empty,
            StartMode = (OrderStartMode)source.StartMode,
            FileTopologyMarker = (OrderFileTopologyMarker)source.TopologyMarker,
            Keyword = source.Keyword?.Trim() ?? string.Empty,
            UserName = source.UserName?.Trim() ?? string.Empty,
            ArrivalDate = source.ArrivalDate == default ? DateTime.Now : source.ArrivalDate,
            OrderDate = source.ManagerOrderDate == default ? OrderData.PlaceholderOrderDate : source.ManagerOrderDate,
            FolderName = source.FolderName?.Trim() ?? string.Empty,
            Status = NormalizeStatus(source.Status),
            PitStopAction = NormalizeAction(source.PitStopAction),
            ImposingAction = NormalizeAction(source.ImposingAction),
            LastStatusReason = source.LastStatusReason ?? string.Empty,
            LastStatusSource = source.LastStatusSource ?? string.Empty,
            LastStatusAt = source.LastStatusAt == default ? DateTime.Now : source.LastStatusAt,
            Items = (source.Items ?? new List<SharedOrderItem>())
                .Where(item => item != null)
                .OrderBy(item => item.SequenceNo)
                .ThenBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(MapItem)
                .ToList()
        };

        if (mapped.Items.Count == 1)
        {
            var single = mapped.Items[0];
            mapped.SourcePath = single.SourcePath;
            mapped.SourceFileSizeBytes = single.SourceFileSizeBytes;
            mapped.SourceFileHash = single.SourceFileHash;
            mapped.PreparedPath = single.PreparedPath;
            mapped.PreparedFileSizeBytes = single.PreparedFileSizeBytes;
            mapped.PreparedFileHash = single.PreparedFileHash;
            mapped.PrintPath = single.PrintPath;
            mapped.PrintFileSizeBytes = single.PrintFileSizeBytes;
            mapped.PrintFileHash = single.PrintFileHash;
        }

        OrderTopologyService.Normalize(mapped);
        return mapped;
    }

    private static OrderFileItem MapItem(SharedOrderItem source)
    {
        return new OrderFileItem
        {
            ItemId = string.IsNullOrWhiteSpace(source.ItemId) ? Guid.NewGuid().ToString("N") : source.ItemId.Trim(),
            StorageVersion = source.Version,
            SequenceNo = source.SequenceNo,
            ClientFileLabel = source.ClientFileLabel ?? string.Empty,
            Variant = source.Variant ?? string.Empty,
            SourcePath = source.SourcePath ?? string.Empty,
            SourceFileSizeBytes = source.SourceFileSizeBytes,
            SourceFileHash = source.SourceFileHash ?? string.Empty,
            PreparedPath = source.PreparedPath ?? string.Empty,
            PreparedFileSizeBytes = source.PreparedFileSizeBytes,
            PreparedFileHash = source.PreparedFileHash ?? string.Empty,
            PrintPath = source.PrintPath ?? string.Empty,
            PrintFileSizeBytes = source.PrintFileSizeBytes,
            PrintFileHash = source.PrintFileHash ?? string.Empty,
            FileStatus = NormalizeStatus(source.FileStatus),
            LastReason = source.LastReason ?? string.Empty,
            UpdatedAt = source.UpdatedAt == default ? DateTime.Now : source.UpdatedAt,
            PitStopAction = NormalizeAction(source.PitStopAction),
            ImposingAction = NormalizeAction(source.ImposingAction)
        };
    }

    private static string NormalizeStatus(string? status)
    {
        return WorkflowStatusNames.Normalize(status) ?? WorkflowStatusNames.Waiting;
    }

    private static string NormalizeAction(string? action)
    {
        return string.IsNullOrWhiteSpace(action)
            ? "-"
            : action.Trim();
    }
}

public sealed class LanOrderWriteCommandResult
{
    private LanOrderWriteCommandResult(
        bool isSuccess,
        bool isConflict,
        bool isNotFound,
        bool isBadRequest,
        bool isUnavailable,
        string error,
        long currentVersion,
        OrderData? order)
    {
        IsSuccess = isSuccess;
        IsConflict = isConflict;
        IsNotFound = isNotFound;
        IsBadRequest = isBadRequest;
        IsUnavailable = isUnavailable;
        Error = error ?? string.Empty;
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
    public OrderData? Order { get; }

    public static LanOrderWriteCommandResult Success(OrderData order) => new(
        isSuccess: true,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: string.Empty,
        currentVersion: order?.StorageVersion ?? 0,
        order: order);

    public static LanOrderWriteCommandResult Conflict(string error, long currentVersion) => new(
        isSuccess: false,
        isConflict: true,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error,
        currentVersion: currentVersion,
        order: null);

    public static LanOrderWriteCommandResult NotFound(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: true,
        isBadRequest: false,
        isUnavailable: false,
        error: error,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteCommandResult BadRequest(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: true,
        isUnavailable: false,
        error: error,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteCommandResult Unavailable(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: true,
        error: error,
        currentVersion: 0,
        order: null);

    public static LanOrderWriteCommandResult Failed(string error) => new(
        isSuccess: false,
        isConflict: false,
        isNotFound: false,
        isBadRequest: false,
        isUnavailable: false,
        error: error,
        currentVersion: 0,
        order: null);
}
