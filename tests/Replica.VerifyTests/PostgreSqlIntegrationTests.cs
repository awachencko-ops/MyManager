using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Replica.Api.Contracts;
using Replica.Api.Data;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class PostgreSqlIntegrationTests
{
    [Fact]
    public void PostgreSqlIntegration_Roundtrip_SingleAndGroupOrders()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var repository = new PostgreSqlOrdersRepository(db.ConnectionString);

        Assert.True(repository.TryLoadAll(out var initialOrders, out var initialLoadError), initialLoadError);
        Assert.Empty(initialOrders);

        var singleOrder = new OrderData
        {
            InternalId = "single-order-1",
            Id = "1001",
            UserName = "Integration User",
            Status = WorkflowStatusNames.Waiting,
            StartMode = OrderStartMode.Simple,
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            OrderDate = new DateTime(2026, 3, 1),
            ArrivalDate = new DateTime(2026, 3, 2),
            SourceFileHash = "source-hash-single",
            PreparedFileHash = "prepared-hash-single",
            PrintFileHash = "print-hash-single",
            Items = new List<OrderFileItem>
            {
                new()
                {
                    ItemId = "single-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "single.pdf",
                    SourceFileHash = "item-source-hash-single",
                    PreparedFileHash = "item-prepared-hash-single",
                    PrintFileHash = "item-print-hash-single",
                    FileStatus = WorkflowStatusNames.Waiting
                }
            }
        };

        var groupOrder = new OrderData
        {
            InternalId = "group-order-1",
            Id = "1002",
            UserName = "Integration User",
            Status = WorkflowStatusNames.Waiting,
            StartMode = OrderStartMode.Extended,
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            OrderDate = new DateTime(2026, 3, 3),
            ArrivalDate = new DateTime(2026, 3, 4),
            Items = new List<OrderFileItem>
            {
                new()
                {
                    ItemId = "group-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "group-1.pdf",
                    SourceFileHash = "item-source-hash-group-1",
                    FileStatus = WorkflowStatusNames.Waiting
                },
                new()
                {
                    ItemId = "group-item-2",
                    SequenceNo = 1,
                    ClientFileLabel = "group-2.pdf",
                    SourceFileHash = "item-source-hash-group-2",
                    FileStatus = WorkflowStatusNames.Waiting
                }
            }
        };

        var saveOk = repository.TrySaveAll(new List<OrderData> { singleOrder, groupOrder }, out var saveError);
        Assert.True(saveOk, saveError);

        var loadOk = repository.TryLoadAll(out var loadedOrders, out var loadError);
        Assert.True(loadOk, loadError);
        Assert.Equal(2, loadedOrders.Count);

        var loadedSingle = loadedOrders.Single(x => x.InternalId == "single-order-1");
        Assert.Single(loadedSingle.Items);
        Assert.Equal("item-source-hash-single", loadedSingle.Items[0].SourceFileHash);

        var loadedGroup = loadedOrders.Single(x => x.InternalId == "group-order-1");
        Assert.Equal(2, loadedGroup.Items.Count);
        Assert.Equal(new[] { 0L, 1L }, loadedGroup.Items.OrderBy(x => x.SequenceNo).Select(x => x.SequenceNo).ToArray());

        Assert.Equal(2, QueryEventCount(db.ConnectionString, "add-order", "ui"));
        Assert.Equal(3, QueryEventCount(db.ConnectionString, "add-item", "ui"));
    }

    [Fact]
    public void PostgreSqlIntegration_DetectsConcurrencyConflict_BetweenWriters()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var repositoryA = new PostgreSqlOrdersRepository(db.ConnectionString);
        var repositoryB = new PostgreSqlOrdersRepository(db.ConnectionString);

        Assert.True(repositoryA.TryLoadAll(out _, out var loadAError), loadAError);
        Assert.True(repositoryB.TryLoadAll(out _, out var loadBError), loadBError);

        var orderFromA = new OrderData
        {
            InternalId = "order-a",
            Id = "A-100",
            UserName = "A",
            Status = WorkflowStatusNames.Waiting,
            Items = new List<OrderFileItem>
            {
                new()
                {
                    ItemId = "order-a-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "a.pdf",
                    FileStatus = WorkflowStatusNames.Waiting
                }
            }
        };

        Assert.True(repositoryA.TrySaveAll(new List<OrderData> { orderFromA }, out var saveAError), saveAError);

        var orderFromB = new OrderData
        {
            InternalId = "order-b",
            Id = "B-100",
            UserName = "B",
            Status = WorkflowStatusNames.Waiting,
            Items = new List<OrderFileItem>
            {
                new()
                {
                    ItemId = "order-b-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "b.pdf",
                    FileStatus = WorkflowStatusNames.Waiting
                }
            }
        };

        var saveBOk = repositoryB.TrySaveAll(new List<OrderData> { orderFromB }, out var saveBError);
        Assert.False(saveBOk);
        Assert.Contains("concurrency conflict", saveBError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlIntegration_AppendsOperationalEvents()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var repository = new PostgreSqlOrdersRepository(db.ConnectionString);

        Assert.True(repository.TryAppendEvent("order-1", string.Empty, "run", "ui", "{\"scope\":\"integration\"}", out var runError), runError);
        Assert.True(repository.TryAppendEvent("order-1", string.Empty, "stop", "ui", "{\"scope\":\"integration\"}", out var stopError), stopError);
        Assert.True(repository.TryAppendEvent("order-1", string.Empty, "status-change", "processor", "{\"scope\":\"integration\"}", out var statusError), statusError);

        Assert.Equal(1, QueryEventCount(db.ConnectionString, "run", "ui"));
        Assert.Equal(1, QueryEventCount(db.ConnectionString, "stop", "ui"));
        Assert.Equal(1, QueryEventCount(db.ConnectionString, "status-change", "processor"));
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_RunStopLifecycle_PersistsLockAndEvents()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "RUN-100",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        }, "integration-user");

        var start = store.TryStartRun(created.InternalId, new RunOrderRequest
        {
            ExpectedOrderVersion = created.Version
        }, "integration-runner");

        Assert.True(start.IsSuccess, start.Error);
        Assert.NotNull(start.Order);
        Assert.Equal("Processing", start.Order!.Status);
        Assert.Equal(created.Version + 1, start.Order.Version);

        var duplicateStart = store.TryStartRun(created.InternalId, new RunOrderRequest
        {
            ExpectedOrderVersion = start.Order.Version
        }, "integration-runner");

        Assert.True(duplicateStart.IsConflict);
        Assert.Contains("run already active", duplicateStart.Error, StringComparison.OrdinalIgnoreCase);

        var stop = store.TryStopRun(created.InternalId, new StopOrderRequest
        {
            ExpectedOrderVersion = start.Order.Version
        }, "integration-runner");

        Assert.True(stop.IsSuccess, stop.Error);
        Assert.NotNull(stop.Order);
        Assert.Equal("Cancelled", stop.Order!.Status);
        Assert.Equal(start.Order.Version + 1, stop.Order.Version);

        using var verifyDb = CreateReplicaDbContext(db.ConnectionString);
        var lockRow = verifyDb.OrderRunLocks.Single(x => x.OrderInternalId == created.InternalId);
        Assert.False(lockRow.IsActive);
        Assert.Equal("integration-runner", lockRow.LeaseOwner);

        var runEvents = verifyDb.OrderEvents.Count(x => x.OrderInternalId == created.InternalId && x.EventType == "run");
        var stopEvents = verifyDb.OrderEvents.Count(x => x.OrderInternalId == created.InternalId && x.EventType == "stop");
        Assert.Equal(1, runEvents);
        Assert.Equal(1, stopEvents);
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_RunStart_IsIdempotentByKey()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "RUN-IDEM-100",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        }, "integration-user");

        var firstStart = store.TryStartRun(
            created.InternalId,
            new RunOrderRequest { ExpectedOrderVersion = created.Version },
            "integration-runner",
            idempotencyKey: "idem-run-start-1");
        var secondStart = store.TryStartRun(
            created.InternalId,
            new RunOrderRequest { ExpectedOrderVersion = created.Version },
            "integration-runner",
            idempotencyKey: "idem-run-start-1");

        Assert.True(firstStart.IsSuccess, firstStart.Error);
        Assert.True(secondStart.IsSuccess, secondStart.Error);
        Assert.NotNull(firstStart.Order);
        Assert.NotNull(secondStart.Order);
        Assert.Equal(firstStart.Order!.Version, secondStart.Order!.Version);
        Assert.Equal("Processing", secondStart.Order.Status);

        using var verifyDb = CreateReplicaDbContext(db.ConnectionString);
        var runEvents = verifyDb.OrderEvents.Count(x => x.OrderInternalId == created.InternalId && x.EventType == "run");
        Assert.Equal(1, runEvents);
        var idempotencyRows = verifyDb.OrderWriteIdempotency.Count(x =>
            x.OrderInternalId == created.InternalId
            && x.CommandName == "run"
            && x.IdempotencyKey == "idem-run-start-1");
        Assert.Equal(1, idempotencyRows);
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_RunStart_IdempotencyReuseWithDifferentPayload_ReturnsBadRequest()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "RUN-IDEM-200",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        }, "integration-user");

        var firstStart = store.TryStartRun(
            created.InternalId,
            new RunOrderRequest { ExpectedOrderVersion = created.Version },
            "integration-runner",
            idempotencyKey: "idem-run-start-2");
        Assert.True(firstStart.IsSuccess, firstStart.Error);

        var secondStart = store.TryStartRun(
            created.InternalId,
            new RunOrderRequest { ExpectedOrderVersion = created.Version + 1 },
            "integration-runner",
            idempotencyKey: "idem-run-start-2");
        Assert.True(secondStart.IsBadRequest);
        Assert.Contains("idempotency", secondStart.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_WriteCommands_AreIdempotentByKey()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var createRequest = new CreateOrderRequest
        {
            OrderNumber = "WR-IDEM-100",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        };

        var createFirst = store.TryCreateOrder(createRequest, "integration-user", idempotencyKey: "idem-write-create-1");
        var createSecond = store.TryCreateOrder(createRequest, "integration-user", idempotencyKey: "idem-write-create-1");
        Assert.True(createFirst.IsSuccess, createFirst.Error);
        Assert.True(createSecond.IsSuccess, createSecond.Error);
        Assert.NotNull(createFirst.Order);
        Assert.NotNull(createSecond.Order);
        var current = createSecond.Order!;
        Assert.Equal(createFirst.Order!.InternalId, createSecond.Order!.InternalId);

        var updateRequest = new UpdateOrderRequest
        {
            ExpectedVersion = current.Version,
            Status = "Processing",
            OrderNumber = "WR-IDEM-100"
        };
        var updateFirst = store.TryUpdateOrder(current.InternalId, updateRequest, "integration-user", idempotencyKey: "idem-write-update-1");
        var updateSecond = store.TryUpdateOrder(current.InternalId, updateRequest, "integration-user", idempotencyKey: "idem-write-update-1");
        Assert.True(updateFirst.IsSuccess, updateFirst.Error);
        Assert.True(updateSecond.IsSuccess, updateSecond.Error);
        current = updateSecond.Order!;

        var addRequest = new AddOrderItemRequest
        {
            ExpectedOrderVersion = current.Version,
            Item = new Replica.Shared.Models.SharedOrderItem
            {
                ItemId = "w-item-1",
                SequenceNo = 0,
                ClientFileLabel = "w-item.pdf",
                FileStatus = WorkflowStatusNames.Waiting
            }
        };
        var addFirst = store.TryAddItem(current.InternalId, addRequest, "integration-user", idempotencyKey: "idem-write-add-1");
        var addSecond = store.TryAddItem(current.InternalId, addRequest, "integration-user", idempotencyKey: "idem-write-add-1");
        Assert.True(addFirst.IsSuccess, addFirst.Error);
        Assert.True(addSecond.IsSuccess, addSecond.Error);
        current = addSecond.Order!;
        var currentItem = current.Items.Single(x => x.ItemId == "w-item-1");

        var itemUpdateRequest = new UpdateOrderItemRequest
        {
            ExpectedOrderVersion = current.Version,
            ExpectedItemVersion = currentItem.Version,
            FileStatus = "Ready",
            LastReason = "idem-check"
        };
        var itemUpdateFirst = store.TryUpdateItem(current.InternalId, currentItem.ItemId, itemUpdateRequest, "integration-user", idempotencyKey: "idem-write-item-update-1");
        var itemUpdateSecond = store.TryUpdateItem(current.InternalId, currentItem.ItemId, itemUpdateRequest, "integration-user", idempotencyKey: "idem-write-item-update-1");
        Assert.True(itemUpdateFirst.IsSuccess, itemUpdateFirst.Error);
        Assert.True(itemUpdateSecond.IsSuccess, itemUpdateSecond.Error);
        current = itemUpdateSecond.Order!;
        currentItem = current.Items.Single(x => x.ItemId == "w-item-1");

        var reorderRequest = new ReorderOrderItemsRequest
        {
            ExpectedOrderVersion = current.Version,
            OrderedItemIds = new List<string> { "w-item-1" }
        };
        var reorderFirst = store.TryReorderItems(current.InternalId, reorderRequest, "integration-user", idempotencyKey: "idem-write-reorder-1");
        var reorderSecond = store.TryReorderItems(current.InternalId, reorderRequest, "integration-user", idempotencyKey: "idem-write-reorder-1");
        Assert.True(reorderFirst.IsSuccess, reorderFirst.Error);
        Assert.True(reorderSecond.IsSuccess, reorderSecond.Error);
        current = reorderSecond.Order!;
        currentItem = current.Items.Single(x => x.ItemId == "w-item-1");

        var deleteRequest = new DeleteOrderItemRequest
        {
            ExpectedOrderVersion = current.Version,
            ExpectedItemVersion = currentItem.Version
        };
        var deleteFirst = store.TryDeleteItem(current.InternalId, currentItem.ItemId, deleteRequest, "integration-user", idempotencyKey: "idem-write-item-delete-1");
        var deleteSecond = store.TryDeleteItem(current.InternalId, currentItem.ItemId, deleteRequest, "integration-user", idempotencyKey: "idem-write-item-delete-1");
        Assert.True(deleteFirst.IsSuccess, deleteFirst.Error);
        Assert.True(deleteSecond.IsSuccess, deleteSecond.Error);
        Assert.Empty(deleteSecond.Order!.Items);

        using var verifyDb = CreateReplicaDbContext(db.ConnectionString);
        Assert.Equal(1, verifyDb.Orders.Count());
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "create-order" && x.IdempotencyKey == "idem-write-create-1"));
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "update-order" && x.IdempotencyKey == "idem-write-update-1"));
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "add-item" && x.IdempotencyKey == "idem-write-add-1"));
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "update-item" && x.IdempotencyKey == "idem-write-item-update-1"));
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "reorder-items" && x.IdempotencyKey == "idem-write-reorder-1"));
        Assert.Equal(1, verifyDb.OrderWriteIdempotency.Count(x => x.CommandName == "delete-item" && x.IdempotencyKey == "idem-write-item-delete-1"));
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_UpdateOrder_IdempotencyReuseWithDifferentPayload_ReturnsBadRequest()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "WR-IDEM-200",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        }, "integration-user");

        var key = "idem-write-update-mismatch-1";
        var first = store.TryUpdateOrder(
            created.InternalId,
            new UpdateOrderRequest
            {
                ExpectedVersion = created.Version,
                Status = "Processing"
            },
            "integration-user",
            idempotencyKey: key);
        Assert.True(first.IsSuccess, first.Error);

        var second = store.TryUpdateOrder(
            created.InternalId,
            new UpdateOrderRequest
            {
                ExpectedVersion = created.Version,
                Status = "Cancelled"
            },
            "integration-user",
            idempotencyKey: key);
        Assert.True(second.IsBadRequest);
        Assert.Contains("idempotency", second.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_RunStop_RejectsVersionMismatch()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "RUN-200",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting
        }, "integration-user");

        var startMismatch = store.TryStartRun(created.InternalId, new RunOrderRequest
        {
            ExpectedOrderVersion = created.Version + 5
        }, "integration-runner");

        Assert.True(startMismatch.IsConflict);
        Assert.Equal(created.Version, startMismatch.CurrentVersion);
        Assert.Contains("version mismatch", startMismatch.Error, StringComparison.OrdinalIgnoreCase);

        var start = store.TryStartRun(created.InternalId, new RunOrderRequest
        {
            ExpectedOrderVersion = created.Version
        }, "integration-runner");

        Assert.True(start.IsSuccess, start.Error);
        Assert.NotNull(start.Order);

        var stopMismatch = store.TryStopRun(created.InternalId, new StopOrderRequest
        {
            ExpectedOrderVersion = start.Order!.Version + 5
        }, "integration-runner");

        Assert.True(stopMismatch.IsConflict);
        Assert.Equal(start.Order.Version, stopMismatch.CurrentVersion);
        Assert.Contains("version mismatch", stopMismatch.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlIntegration_EfCoreStore_DeleteItem_ReindexesAndAppendsEvent()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var store = CreateEfCoreStore(db.ConnectionString);

        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "DEL-ITEM-100",
            UserName = "Integration User",
            CreatedById = "integration-user",
            CreatedByUser = "integration-user",
            Status = WorkflowStatusNames.Waiting,
            Items = new List<Replica.Shared.Models.SharedOrderItem>
            {
                new() { ItemId = "i-a", SequenceNo = 0, FileStatus = WorkflowStatusNames.Waiting },
                new() { ItemId = "i-b", SequenceNo = 1, FileStatus = WorkflowStatusNames.Waiting }
            }
        }, "integration-user");

        Assert.Equal(2, created.Items.Count);

        var deleteResult = store.TryDeleteItem(
            created.InternalId,
            "i-a",
            new DeleteOrderItemRequest
            {
                ExpectedOrderVersion = created.Version,
                ExpectedItemVersion = created.Items.First(x => x.ItemId == "i-a").Version
            },
            "integration-user");

        Assert.True(deleteResult.IsSuccess, deleteResult.Error);
        Assert.NotNull(deleteResult.Order);
        var updated = deleteResult.Order!;
        Assert.Equal(created.Version + 1, updated.Version);
        Assert.Single(updated.Items);
        Assert.Equal("i-b", updated.Items[0].ItemId);
        Assert.Equal(0, updated.Items[0].SequenceNo);

        using var verifyDb = CreateReplicaDbContext(db.ConnectionString);
        var deleteEvents = verifyDb.OrderEvents.Count(x => x.OrderInternalId == created.InternalId && x.EventType == "delete-item");
        Assert.Equal(1, deleteEvents);
    }

    [Fact]
    public void PostgreSqlIntegration_Coordinator_SynchronizesFileAndLanHistories()
    {
        if (!IsIntegrationEnabled())
            return;

        using var db = TemporaryPostgreSqlDatabase.Create();
        var lanRepository = new PostgreSqlOrdersRepository(db.ConnectionString);
        Assert.True(lanRepository.TryLoadAll(out _, out var initialLanLoadError), initialLanLoadError);

        var lanOrder = new OrderData
        {
            InternalId = "lan-order-1",
            Id = "LAN-100",
            UserName = "LAN User",
            Status = WorkflowStatusNames.Waiting,
            OrderDate = new DateTime(2026, 3, 20),
            ArrivalDate = new DateTime(2026, 3, 20, 9, 0, 0),
            Items = new List<OrderFileItem>()
        };
        Assert.True(lanRepository.TrySaveAll(new List<OrderData> { lanOrder }, out var lanSaveError), lanSaveError);

        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_CoordinatorSync_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);
        var historyPath = Path.Combine(tempRootPath, "history.json");

        try
        {
            var fileRepository = new FileSystemOrdersRepository(historyPath);
            var fileOrder = new OrderData
            {
                InternalId = "file-order-1",
                Id = "FILE-100",
                UserName = "File User",
                Status = WorkflowStatusNames.Waiting,
                OrderDate = new DateTime(2026, 3, 20),
                ArrivalDate = new DateTime(2026, 3, 20, 9, 5, 0),
                Items = new List<OrderFileItem>()
            };
            Assert.True(fileRepository.TrySaveAll(new List<OrderData> { fileOrder }, out var fileSaveError), fileSaveError);

            var coordinator = new OrdersHistoryRepositoryCoordinator();
            coordinator.Configure(OrdersStorageMode.LanPostgreSql, db.ConnectionString, historyPath);

            Assert.True(coordinator.TryLoadAll(out var synchronizedOrders));
            Assert.Equal(2, synchronizedOrders.Count);
            Assert.Contains(synchronizedOrders, order => order.InternalId == lanOrder.InternalId);
            Assert.Contains(synchronizedOrders, order => order.InternalId == fileOrder.InternalId);

            var lanVerifier = new PostgreSqlOrdersRepository(db.ConnectionString);
            Assert.True(lanVerifier.TryLoadAll(out var lanAfterSync, out var lanAfterSyncError), lanAfterSyncError);
            Assert.Equal(2, lanAfterSync.Count);
            Assert.Contains(lanAfterSync, order => order.InternalId == fileOrder.InternalId);

            Assert.True(fileRepository.TryLoadAll(out var fileAfterSync, out var fileAfterSyncError), fileAfterSyncError);
            Assert.Equal(2, fileAfterSync.Count);
            Assert.Contains(fileAfterSync, order => order.InternalId == lanOrder.InternalId);
        }
        finally
        {
            if (Directory.Exists(tempRootPath))
                Directory.Delete(tempRootPath, recursive: true);
        }
    }

    private static bool IsIntegrationEnabled()
    {
        var rawValue = Environment.GetEnvironmentVariable("REPLICA_RUN_PG_INTEGRATION");
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        var normalized = rawValue.Trim();
        return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static long QueryEventCount(string connectionString, string eventType, string eventSource)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var cmd = new NpgsqlCommand(
            "select count(*) from order_events where event_type = @event_type and event_source = @event_source;",
            connection);
        cmd.Parameters.AddWithValue("event_type", eventType);
        cmd.Parameters.AddWithValue("event_source", eventSource);

        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static EfCoreLanOrderStore CreateEfCoreStore(string connectionString)
    {
        using var db = CreateReplicaDbContext(connectionString);
        db.Database.Migrate();

        return new EfCoreLanOrderStore(new TestReplicaDbContextFactory(connectionString));
    }

    private static ReplicaDbContext CreateReplicaDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ReplicaDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ReplicaDbContext(options);
    }

    private sealed class TestReplicaDbContextFactory : IDbContextFactory<ReplicaDbContext>
    {
        private readonly DbContextOptions<ReplicaDbContext> _options;

        public TestReplicaDbContextFactory(string connectionString)
        {
            _options = new DbContextOptionsBuilder<ReplicaDbContext>()
                .UseNpgsql(connectionString)
                .Options;
        }

        public ReplicaDbContext CreateDbContext()
        {
            return new ReplicaDbContext(_options);
        }
    }

    private sealed class TemporaryPostgreSqlDatabase : IDisposable
    {
        public string DatabaseName { get; private set; } = string.Empty;
        public string ConnectionString { get; private set; } = string.Empty;

        public static TemporaryPostgreSqlDatabase Create()
        {
            var db = new TemporaryPostgreSqlDatabase();
            db.Initialize();
            return db;
        }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(DatabaseName))
                return;

            try
            {
                using var adminConnection = new NpgsqlConnection(BuildAdminConnectionString());
                adminConnection.Open();

                using (var terminateCmd = new NpgsqlCommand(
                    "select pg_terminate_backend(pid) from pg_stat_activity where datname = @db_name and pid <> pg_backend_pid();",
                    adminConnection))
                {
                    terminateCmd.Parameters.AddWithValue("db_name", DatabaseName);
                    terminateCmd.ExecuteNonQuery();
                }

                using var dropCmd = new NpgsqlCommand($"drop database if exists \"{DatabaseName}\";", adminConnection);
                dropCmd.ExecuteNonQuery();
            }
            catch
            {
                // Best-effort cleanup for temporary integration DB.
            }
        }

        private void Initialize()
        {
            DatabaseName = $"replica_it_{Guid.NewGuid():N}";
            ConnectionString = BuildDatabaseConnectionString(DatabaseName);

            using var adminConnection = new NpgsqlConnection(BuildAdminConnectionString());
            adminConnection.Open();
            using var createCmd = new NpgsqlCommand($"create database \"{DatabaseName}\";", adminConnection);
            createCmd.ExecuteNonQuery();
        }

        private static string BuildAdminConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(AppSettings.DefaultLanPostgreSqlConnectionString)
            {
                Database = "postgres"
            };
            return builder.ConnectionString;
        }

        private static string BuildDatabaseConnectionString(string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder(AppSettings.DefaultLanPostgreSqlConnectionString)
            {
                Database = databaseName
            };
            return builder.ConnectionString;
        }
    }
}
