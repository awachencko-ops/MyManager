using System.Reflection;
using Replica;
using VerifyXunit;
using Xunit;

namespace Replica.VerifyTests;

public sealed class VerifySnapshotsTests : VerifyBase
{
    public VerifySnapshotsTests()
        : base()
    {
    }

    [Fact]
    public Task Snapshot_OrderContracts_SingleAndMultiShape()
    {
        var singleOrder = new OrderData
        {
            InternalId = "order-single-001",
            Id = "00526",
            StartMode = OrderStartMode.Simple,
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            Status = WorkflowStatusNames.Waiting,
            UserName = "QA User",
            Items =
            [
                new OrderFileItem
                {
                    ItemId = "item-single-001",
                    ClientFileLabel = "poster-a3",
                    Variant = "A3",
                    SourcePath = @"\\NAS\Orders\00526\in\poster-a3.pdf",
                    PreparedPath = @"\\NAS\Orders\00526\prepress\poster-a3.pdf",
                    PrintPath = @"\\NAS\Orders\00526\print\00526.pdf",
                    FileStatus = WorkflowStatusNames.Completed,
                    SequenceNo = 0
                }
            ]
        };

        singleOrder.RefreshAggregatedStatus();

        var multiOrder = new OrderData
        {
            InternalId = "order-multi-001",
            Id = "00527",
            StartMode = OrderStartMode.Extended,
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Status = WorkflowStatusNames.Processing,
            UserName = "Operator",
            Items =
            [
                new OrderFileItem
                {
                    ItemId = "item-multi-001",
                    ClientFileLabel = "flyer-front",
                    Variant = "A4",
                    FileStatus = WorkflowStatusNames.Processing,
                    SequenceNo = 0
                },
                new OrderFileItem
                {
                    ItemId = "item-multi-002",
                    ClientFileLabel = "flyer-back",
                    Variant = "A4",
                    FileStatus = WorkflowStatusNames.Waiting,
                    SequenceNo = 1
                }
            ]
        };

        var snapshot = new
        {
            Single = new
            {
                singleOrder.Id,
                singleOrder.StartMode,
                singleOrder.FileTopologyMarker,
                singleOrder.Status,
                singleOrder.ItemsCount,
                singleOrder.IsSingleOrderMarked,
                singleOrder.IsMultiOrderMarked,
                Items = singleOrder.Items.Select(item => new
                {
                    item.ClientFileLabel,
                    item.Variant,
                    item.SourcePath,
                    item.PreparedPath,
                    item.PrintPath,
                    item.FileStatus,
                    item.SequenceNo
                })
            },
            Multi = new
            {
                multiOrder.Id,
                multiOrder.StartMode,
                multiOrder.FileTopologyMarker,
                multiOrder.Status,
                multiOrder.ItemsCount,
                multiOrder.IsSingleOrderMarked,
                multiOrder.IsMultiOrderMarked,
                Items = multiOrder.Items.Select(item => new
                {
                    item.ClientFileLabel,
                    item.Variant,
                    item.FileStatus,
                    item.SequenceNo
                })
            }
        };

        return Verify(snapshot);
    }

    [Fact]
    public Task Snapshot_OrderTopology_NormalizeLegacySingleOrder()
    {
        var order = new OrderData
        {
            InternalId = "legacy-single-001",
            Id = "00528",
            SourcePath = @"C:\Replica\Orders\00528\in\layout.pdf",
            PreparedPath = @"C:\Replica\Orders\00528\prepress\layout.pdf",
            PrintPath = @"C:\Replica\Orders\00528\print\00528.pdf",
            PitStopAction = "CheckBleeds",
            ImposingAction = "A3_2up",
            Items = []
        };

        var result = OrderTopologyService.Normalize(order);

        var snapshot = new
        {
            result.Changed,
            Issues = result.Issues.ToArray(),
            order.FileTopologyMarker,
            order.Status,
            order.ItemsCount,
            Items = order.Items.Select((item, index) => new
            {
                Index = index,
                item.ClientFileLabel,
                item.SourcePath,
                item.PreparedPath,
                item.PrintPath,
                item.FileStatus,
                item.PitStopAction,
                item.ImposingAction,
                item.SequenceNo
            }).ToArray()
        };

        return Verify(snapshot);
    }

    [Fact]
    public Task Snapshot_OrderTopology_NormalizeMultiOrderWithOrderLevelPaths()
    {
        var order = new OrderData
        {
            InternalId = "legacy-multi-001",
            Id = "00529",
            SourcePath = @"C:\Replica\Orders\00529\in\legacy-order-level.pdf",
            Items =
            [
                new OrderFileItem
                {
                    ItemId = "multi-item-1",
                    ClientFileLabel = "front",
                    SourcePath = @"C:\Replica\Orders\00529\in\front.pdf",
                    SequenceNo = 0
                },
                new OrderFileItem
                {
                    ItemId = "multi-item-2",
                    ClientFileLabel = "back",
                    SourcePath = @"C:\Replica\Orders\00529\in\back.pdf",
                    SequenceNo = 1
                }
            ]
        };

        var result = OrderTopologyService.Normalize(order);

        var snapshot = new
        {
            result.Changed,
            Issues = result.Issues.ToArray(),
            order.FileTopologyMarker,
            order.ItemsCount,
            OrderLevelPaths = new
            {
                order.SourcePath,
                order.PreparedPath,
                order.PrintPath
            }
        };

        return Verify(snapshot);
    }

    [Fact]
    public Task Snapshot_WorkflowContracts_NormalizationAndMappings()
    {
        var rawStatuses = new[]
        {
            "Обработка заказа 00526...",
            "Ошибка",
            "Сборка",
            "в работе",
            "✅ Готово",
            "Напечатано",
            "Неизвестный статус"
        };

        var normalized = rawStatuses
            .Select(raw => new
            {
                Raw = raw,
                Normalized = WorkflowStatusNames.Normalize(raw)
            })
            .ToArray();

        var queueMappings = WorkflowStatusNames.QueueMappings
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new
            {
                Queue = x.Key,
                Statuses = x.Value
            })
            .ToArray();

        var columnStageMapping = new[]
        {
            OrderGridColumnNames.Source,
            OrderGridColumnNames.Prepared,
            OrderGridColumnNames.PreparedLegacy,
            OrderGridColumnNames.Print,
            OrderGridColumnNames.Status
        }.Select(columnName => new
        {
            Column = columnName,
            Stage = OrderGridColumnNames.ResolveStage(columnName)
        }).ToArray();

        var snapshot = new
        {
            QueueStatuses = QueueStatusNames.All,
            FilterableStatuses = WorkflowStatusNames.Filterable,
            Normalized = normalized,
            QueueMappings = queueMappings,
            ColumnStageMapping = columnStageMapping
        };

        return Verify(snapshot);
    }

    [Fact]
    public async Task Snapshot_UsersDirectoryService_LoadCases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Replica_Verify_UsersDirectory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "users.json");
            var cachePath = Path.Combine(tempRoot, "users.cache.json");

            File.WriteAllText(sourcePath, "[\"QA User\",\"Operator\",\"QA User\"]");
            var fromSource = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });

            File.Delete(sourcePath);
            var fromCache = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });

            File.Delete(cachePath);
            var fromFallback = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });

            var snapshot = new
            {
                Source = BuildUsersLoadSnapshot(fromSource),
                Cache = BuildUsersLoadSnapshot(fromCache),
                Fallback = BuildUsersLoadSnapshot(fromFallback)
            };

            await Verify(snapshot);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup races for tests.
            }
        }
    }

    private static object InvokeUsersDirectoryLoad(string sourcePath, string cachePath, IEnumerable<string> fallbackUsers)
    {
        var usersDirectoryType = typeof(AppSettings).Assembly.GetType("Replica.UsersDirectoryService")
            ?? throw new InvalidOperationException("Type Replica.UsersDirectoryService not found.");

        var loadMethod = usersDirectoryType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(usersDirectoryType.FullName, "Load");

        var result = loadMethod.Invoke(null, new object[] { sourcePath, cachePath, fallbackUsers });
        return result ?? throw new InvalidOperationException("UsersDirectoryService.Load returned null.");
    }

    private static object BuildUsersLoadSnapshot(object loadResult)
    {
        var users = (IEnumerable<string>?)GetProperty(loadResult, "Users") ?? Array.Empty<string>();
        var loadedFromSource = (bool)(GetProperty(loadResult, "LoadedFromSource") ?? false);
        var loadedFromCache = (bool)(GetProperty(loadResult, "LoadedFromCache") ?? false);
        var statusText = (string)(GetProperty(loadResult, "StatusText") ?? string.Empty);

        return new
        {
            LoadedFromSource = loadedFromSource,
            LoadedFromCache = loadedFromCache,
            StatusText = statusText,
            Users = users.ToArray()
        };
    }

    private static object? GetProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);

        return property.GetValue(target);
    }
}
