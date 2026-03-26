using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Replica;

public sealed class OrdersHistoryRepositoryCoordinator
{
    private const string HistoryBootstrapMarkerKey = "history_json_bootstrap_v1";

    private string _historyFilePath = AppSettings.DefaultHistoryFilePath;
    private OrdersStorageMode _ordersStorageBackend = OrdersStorageMode.FileSystem;
    private string _lanPostgreSqlConnectionString = AppSettings.DefaultLanPostgreSqlConnectionString;
    private IOrdersRepository? _ordersRepository;

    public string BackendName => _ordersRepository?.BackendName ?? "unknown";

    public void Configure(OrdersStorageMode storageBackend, string lanPostgreSqlConnectionString, string historyFilePath)
    {
        var nextHistoryFilePath = StoragePaths.ResolveFilePath(historyFilePath, "history.json");
        var nextConnectionString = lanPostgreSqlConnectionString ?? string.Empty;

        var configChanged = _ordersStorageBackend != storageBackend
            || !string.Equals(_historyFilePath, nextHistoryFilePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_lanPostgreSqlConnectionString, nextConnectionString, StringComparison.Ordinal);

        _ordersStorageBackend = storageBackend;
        _historyFilePath = nextHistoryFilePath;
        _lanPostgreSqlConnectionString = nextConnectionString;

        if (configChanged || _ordersRepository == null)
            _ordersRepository = CreateConfiguredRepository();
    }

    public bool TryLoadAll(out List<OrderData> orders)
    {
        EnsureRepository();

        var primaryError = string.Empty;
        if (_ordersRepository != null
            && _ordersRepository.TryLoadAll(out orders, out primaryError))
        {
            if (orders.Count == 0 && _ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
                TryBootstrapFromFileHistory(ref orders);

            if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
            {
                orders = NormalizeOrdersForSync(orders);
                TryMirrorLanHistoryToFileSnapshot(orders);
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(primaryError))
        {
            Logger.Warn(
                $"HISTORY | load-failed | backend={BackendName} | {primaryError}");
        }

        if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
        {
            Logger.Error(
                $"HISTORY | lan-primary-load-failed | backend={BackendName} | {primaryError}");
            orders = new List<OrderData>();
            return false;
        }

        var fallbackRepository = OrdersRepositoryFactory.CreateFileSystem(_historyFilePath);
        if (fallbackRepository.TryLoadAll(out orders, out var fallbackError))
        {
            Logger.Warn(
                $"HISTORY | fallback-load | backend={fallbackRepository.BackendName}");
            return true;
        }

        Logger.Error(
            $"HISTORY | fallback-load-failed | backend={fallbackRepository.BackendName} | {fallbackError}");
        orders = new List<OrderData>();
        return false;
    }

    public bool TrySaveAll(IReadOnlyCollection<OrderData> orders, out string error)
    {
        EnsureRepository();

        var primaryError = string.Empty;
        if (_ordersRepository != null
            && _ordersRepository.TrySaveAll(orders, out primaryError))
        {
            if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
                TryMirrorLanHistoryToFileSnapshot(orders);

            error = string.Empty;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(primaryError))
        {
            Logger.Warn(
                $"HISTORY | save-failed | backend={BackendName} | {primaryError}");
        }

        if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
        {
            error = string.IsNullOrWhiteSpace(primaryError)
                ? "postgresql save failed"
                : primaryError;
            Logger.Error(
                $"HISTORY | lan-primary-save-failed | backend={BackendName} | {error}");
            return false;
        }

        var fallbackRepository = OrdersRepositoryFactory.CreateFileSystem(_historyFilePath);
        if (fallbackRepository.TrySaveAll(orders, out var fallbackError))
        {
            Logger.Warn(
                $"HISTORY | fallback-save | backend={fallbackRepository.BackendName}");
            error = string.Empty;
            return true;
        }

        error = fallbackError;
        return false;
    }

    public bool TryAppendEvent(
        string orderInternalId,
        string itemId,
        string eventType,
        string eventSource,
        string payloadJson,
        out string error)
    {
        EnsureRepository();
        if (_ordersRepository == null)
        {
            error = "orders repository is not configured";
            return false;
        }

        return _ordersRepository.TryAppendEvent(
            orderInternalId ?? string.Empty,
            itemId ?? string.Empty,
            eventType ?? string.Empty,
            eventSource ?? string.Empty,
            string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            out error);
    }

    private void TryBootstrapFromFileHistory(ref List<OrderData> orders)
    {
        if (_ordersRepository is not PostgreSqlOrdersRepository postgreSqlRepository)
            return;

        var bootstrapMarkerExists = false;
        if (postgreSqlRepository.TryGetMetaValue(
                HistoryBootstrapMarkerKey,
                out var bootstrapMarkerValue,
                out var markerReadError))
        {
            bootstrapMarkerExists = !string.IsNullOrWhiteSpace(bootstrapMarkerValue);
            if (bootstrapMarkerExists)
            {
                Logger.Info(
                    $"HISTORY | bootstrap-skip-by-marker | backend={BackendName} | marker={HistoryBootstrapMarkerKey}");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(markerReadError))
        {
            Logger.Warn(
                $"HISTORY | bootstrap-marker-read-failed | backend={BackendName} | marker={HistoryBootstrapMarkerKey} | {markerReadError}");
        }

        var bootstrapRepository = OrdersRepositoryFactory.CreateFileSystem(_historyFilePath);
        if (!bootstrapRepository.TryLoadAll(out var bootstrapOrders, out var bootstrapLoadError))
        {
            if (!string.IsNullOrWhiteSpace(bootstrapLoadError))
            {
                Logger.Warn(
                    $"HISTORY | bootstrap-load-failed | backend={bootstrapRepository.BackendName} | {bootstrapLoadError}");
            }

            return;
        }

        if (bootstrapOrders.Count > 0)
        {
            orders = bootstrapOrders;
            Logger.Warn(
                $"HISTORY | bootstrap-load | source={bootstrapRepository.BackendName} | target={BackendName} | orders={bootstrapOrders.Count}");

            if (!_ordersRepository!.TrySaveAll(bootstrapOrders, out var bootstrapSaveError))
            {
                Logger.Warn(
                    $"HISTORY | bootstrap-save-failed | backend={BackendName} | {bootstrapSaveError}");
                return;
            }

            Logger.Info(
                $"HISTORY | bootstrap-save-success | backend={BackendName} | orders={bootstrapOrders.Count}");

            var markerPayload = JsonSerializer.Serialize(new
            {
                state = "imported",
                imported_orders = bootstrapOrders.Count,
                source = "history.json",
                completed_at = DateTime.Now
            });
            WriteBootstrapMarker(postgreSqlRepository, markerPayload, "imported");
            return;
        }

        if (bootstrapMarkerExists)
            return;

        var emptyMarkerPayload = JsonSerializer.Serialize(new
        {
            state = "empty-source",
            imported_orders = 0,
            source = "history.json",
            completed_at = DateTime.Now
        });
        WriteBootstrapMarker(postgreSqlRepository, emptyMarkerPayload, "empty-source");
    }

    private void TryMirrorLanHistoryToFileSnapshot(IReadOnlyCollection<OrderData> lanOrders)
    {
        if (_ordersRepository is not PostgreSqlOrdersRepository)
            return;

        var fileRepository = OrdersRepositoryFactory.CreateFileSystem(_historyFilePath);
        var normalizedLanOrders = NormalizeOrdersForSync(lanOrders);
        if (!fileRepository.TrySaveAll(normalizedLanOrders, out var mirrorError))
        {
            Logger.Warn(
                $"HISTORY | sync-lan-to-file-failed | backend={fileRepository.BackendName} | {mirrorError}");
            return;
        }

        Logger.Info(
            $"HISTORY | sync-lan-to-file | orders={normalizedLanOrders.Count} | backend={fileRepository.BackendName}");
    }

    private static List<OrderData> NormalizeOrdersForSync(IEnumerable<OrderData>? orders)
    {
        var normalized = new List<OrderData>();
        foreach (var source in (orders ?? Array.Empty<OrderData>()).Where(order => order != null))
        {
            var copy = CloneOrder(source);
            copy.InternalId = string.IsNullOrWhiteSpace(copy.InternalId) ? Guid.NewGuid().ToString("N") : copy.InternalId;
            copy.Items ??= new List<OrderFileItem>();
            for (var i = 0; i < copy.Items.Count; i++)
            {
                var item = copy.Items[i];
                if (item == null)
                {
                    copy.Items.RemoveAt(i);
                    i--;
                    continue;
                }

                item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId;
                item.SequenceNo = i;
            }

            normalized.Add(copy);
        }

        return normalized;
    }

    private static OrderData CloneOrder(OrderData source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<OrderData>(json) ?? new OrderData();
    }

    private static void WriteBootstrapMarker(PostgreSqlOrdersRepository repository, string markerPayload, string state)
    {
        if (!repository.TryUpsertMetaValue(HistoryBootstrapMarkerKey, markerPayload, out var markerWriteError))
        {
            Logger.Warn(
                $"HISTORY | bootstrap-marker-write-failed | backend={repository.BackendName} | marker={HistoryBootstrapMarkerKey} | {markerWriteError}");
            return;
        }

        Logger.Info(
            $"HISTORY | bootstrap-marker-written | backend={repository.BackendName} | marker={HistoryBootstrapMarkerKey} | state={state}");
    }

    private void EnsureRepository()
    {
        _ordersRepository ??= CreateConfiguredRepository();
    }

    private IOrdersRepository CreateConfiguredRepository()
    {
        var settings = new AppSettings
        {
            OrdersStorageBackend = _ordersStorageBackend,
            LanPostgreSqlConnectionString = _lanPostgreSqlConnectionString
        };

        return OrdersRepositoryFactory.Create(settings, _historyFilePath);
    }
}
