using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Replica;

public sealed class OrdersHistoryRepositoryCoordinator
{
    private string _historyFilePath = AppSettings.DefaultHistoryFilePath;
    private OrdersStorageMode _ordersStorageBackend = OrdersStorageMode.LanPostgreSql;
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
            if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
                orders = NormalizeOrdersForSync(orders);

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
