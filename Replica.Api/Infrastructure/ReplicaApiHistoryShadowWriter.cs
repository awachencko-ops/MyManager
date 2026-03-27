using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Infrastructure;

public readonly record struct ReplicaApiHistoryShadowWriteContext(
    string CommandName,
    string Actor,
    string CorrelationId);

public readonly record struct ReplicaApiHistoryShadowWriteResult(
    bool IsSuccess,
    string Error,
    string FilePath,
    int OrdersCount);

public interface IReplicaApiHistoryShadowWriter
{
    Task<ReplicaApiHistoryShadowWriteResult> TryWriteAsync(
        ReplicaApiHistoryShadowWriteContext context,
        CancellationToken cancellationToken);
}

public sealed class NoOpReplicaApiHistoryShadowWriter : IReplicaApiHistoryShadowWriter
{
    public Task<ReplicaApiHistoryShadowWriteResult> TryWriteAsync(
        ReplicaApiHistoryShadowWriteContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ReplicaApiHistoryShadowWriteResult(
            IsSuccess: true,
            Error: string.Empty,
            FilePath: string.Empty,
            OrdersCount: 0));
    }
}

public sealed class FileReplicaApiHistoryShadowWriter : IReplicaApiHistoryShadowWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILanOrderStore _store;
    private readonly IOptions<ReplicaApiMigrationOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<FileReplicaApiHistoryShadowWriter> _logger;

    public FileReplicaApiHistoryShadowWriter(
        ILanOrderStore store,
        IOptions<ReplicaApiMigrationOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<FileReplicaApiHistoryShadowWriter> logger)
    {
        _store = store;
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ReplicaApiHistoryShadowWriteResult> TryWriteAsync(
        ReplicaApiHistoryShadowWriteContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedOptions = ReplicaApiMigrationConfiguration.Normalize(_options.Value);
            var filePath = ReplicaApiMigrationConfiguration.ResolveShadowHistoryFilePath(normalizedOptions);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            var orders = _store.GetOrders(createdBy: string.Empty)
                .Select(CloneOrder)
                .ToList();
            var effectiveCorrelationId = !string.IsNullOrWhiteSpace(context.CorrelationId)
                ? context.CorrelationId
                : _httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty;
            var payload = new ReplicaApiHistoryShadowDocument
            {
                GeneratedAtUtc = DateTime.UtcNow,
                CorrelationId = effectiveCorrelationId,
                Command = context.CommandName ?? string.Empty,
                Actor = context.Actor ?? string.Empty,
                Orders = orders
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);

            _logger.LogInformation(
                "MIGRATION | shadow-write-success | command={Command} | actor={Actor} | correlation_id={CorrelationId} | orders={OrdersCount} | path={FilePath}",
                payload.Command,
                payload.Actor,
                payload.CorrelationId,
                payload.Orders.Count,
                filePath);

            return new ReplicaApiHistoryShadowWriteResult(
                IsSuccess: true,
                Error: string.Empty,
                FilePath: filePath,
                OrdersCount: payload.Orders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "MIGRATION | shadow-write-failed | command={Command} | actor={Actor} | error={Error}",
                context.CommandName ?? string.Empty,
                context.Actor ?? string.Empty,
                ex.Message);

            return new ReplicaApiHistoryShadowWriteResult(
                IsSuccess: false,
                Error: ex.Message,
                FilePath: string.Empty,
                OrdersCount: 0);
        }
    }

    private static SharedOrder CloneOrder(SharedOrder source)
    {
        var sourceItems = source.Items ?? new List<SharedOrderItem>();
        return new SharedOrder
        {
            InternalId = source.InternalId,
            OrderNumber = source.OrderNumber,
            Keyword = source.Keyword,
            UserName = source.UserName,
            CreatedById = source.CreatedById,
            CreatedByUser = source.CreatedByUser,
            ArrivalDate = source.ArrivalDate,
            ManagerOrderDate = source.ManagerOrderDate,
            FolderName = source.FolderName,
            Status = source.Status,
            StartMode = source.StartMode,
            TopologyMarker = source.TopologyMarker,
            Version = source.Version,
            LastStatusReason = source.LastStatusReason,
            LastStatusSource = source.LastStatusSource,
            LastStatusAt = source.LastStatusAt,
            PitStopAction = source.PitStopAction,
            ImposingAction = source.ImposingAction,
            Items = sourceItems.Select(CloneItem).ToList()
        };
    }

    private static SharedOrderItem CloneItem(SharedOrderItem source)
    {
        return new SharedOrderItem
        {
            ItemId = source.ItemId,
            Version = source.Version,
            SequenceNo = source.SequenceNo,
            ClientFileLabel = source.ClientFileLabel,
            Variant = source.Variant,
            SourcePath = source.SourcePath,
            SourceFileSizeBytes = source.SourceFileSizeBytes,
            SourceFileHash = source.SourceFileHash,
            PreparedPath = source.PreparedPath,
            PreparedFileSizeBytes = source.PreparedFileSizeBytes,
            PreparedFileHash = source.PreparedFileHash,
            PrintPath = source.PrintPath,
            PrintFileSizeBytes = source.PrintFileSizeBytes,
            PrintFileHash = source.PrintFileHash,
            FileStatus = source.FileStatus,
            LastReason = source.LastReason,
            UpdatedAt = source.UpdatedAt,
            PitStopAction = source.PitStopAction,
            ImposingAction = source.ImposingAction
        };
    }

    private sealed class ReplicaApiHistoryShadowDocument
    {
        public DateTime GeneratedAtUtc { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public string Command { get; init; } = string.Empty;
        public string Actor { get; init; } = string.Empty;
        public IReadOnlyList<SharedOrder> Orders { get; init; } = [];
    }
}
