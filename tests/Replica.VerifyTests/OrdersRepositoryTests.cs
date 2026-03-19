using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrdersRepositoryTests
{
    [Fact]
    public void Factory_ReturnsFileSystemRepository_ForFileSystemMode()
    {
        var settings = new AppSettings
        {
            OrdersStorageBackend = OrdersStorageMode.FileSystem,
            HistoryFilePath = Path.Combine(Path.GetTempPath(), "Replica", "history.json")
        };

        var repository = OrdersRepositoryFactory.Create(settings, settings.HistoryFilePath);

        Assert.IsType<FileSystemOrdersRepository>(repository);
        Assert.Equal("filesystem", repository.BackendName);
    }

    [Fact]
    public void Factory_ReturnsPostgreSqlRepository_ForLanMode()
    {
        var settings = new AppSettings
        {
            OrdersStorageBackend = OrdersStorageMode.LanPostgreSql,
            LanPostgreSqlConnectionString = AppSettings.DefaultLanPostgreSqlConnectionString,
            HistoryFilePath = Path.Combine(Path.GetTempPath(), "Replica", "history.json")
        };

        var repository = OrdersRepositoryFactory.Create(settings, settings.HistoryFilePath);

        Assert.IsType<PostgreSqlOrdersRepository>(repository);
        Assert.Equal("postgresql", repository.BackendName);
    }

    [Fact]
    public void FileSystemRepository_Roundtrip_WritesAndReadsOrders()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_FileRepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var historyPath = Path.Combine(tempRootPath, "history.json");
            var repository = new FileSystemOrdersRepository(historyPath);
            var expectedOrders = new List<OrderData>
            {
                new()
                {
                    InternalId = "order-1",
                    Id = "1001",
                    Status = WorkflowStatusNames.Waiting,
                    UserName = "QA User"
                }
            };

            var saveResult = repository.TrySaveAll(expectedOrders, out var saveError);
            Assert.True(saveResult, saveError);

            var loadResult = repository.TryLoadAll(out var actualOrders, out var loadError);
            Assert.True(loadResult, loadError);
            Assert.Single(actualOrders);
            Assert.Equal("1001", actualOrders[0].Id);
            Assert.Equal(WorkflowStatusNames.Waiting, actualOrders[0].Status);
        }
        finally
        {
            if (Directory.Exists(tempRootPath))
                Directory.Delete(tempRootPath, recursive: true);
        }
    }

    [Fact]
    public void PostgreSqlRepository_Load_Fails_WhenConnectionStringEmpty()
    {
        var repository = new PostgreSqlOrdersRepository(string.Empty);

        var result = repository.TryLoadAll(out var orders, out var error);

        Assert.False(result);
        Assert.Empty(orders);
        Assert.Contains("connection string is empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlRepository_Save_Fails_WhenConnectionStringEmpty()
    {
        var repository = new PostgreSqlOrdersRepository("   ");
        var orders = new List<OrderData> { new() { InternalId = "x1", Id = "1002" } };

        var result = repository.TrySaveAll(orders, out var error);

        Assert.False(result);
        Assert.Contains("connection string is empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileSystemRepository_AppendEvent_IsNoOpSuccess()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), "Replica", "history-noop.json");
        var repository = new FileSystemOrdersRepository(historyPath);

        var result = repository.TryAppendEvent(
            orderInternalId: "order-1",
            itemId: string.Empty,
            eventType: "run",
            eventSource: "ui",
            payloadJson: "{}",
            out var error);

        Assert.True(result, error);
        Assert.True(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void PostgreSqlRepository_AppendEvent_Fails_WhenConnectionStringEmpty()
    {
        var repository = new PostgreSqlOrdersRepository(string.Empty);

        var result = repository.TryAppendEvent(
            orderInternalId: "order-1",
            itemId: string.Empty,
            eventType: "run",
            eventSource: "ui",
            payloadJson: "{}",
            out var error);

        Assert.False(result);
        Assert.Contains("connection string is empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlRepository_GetMetaValue_Fails_WhenConnectionStringEmpty()
    {
        var repository = new PostgreSqlOrdersRepository(string.Empty);

        var result = repository.TryGetMetaValue("history_json_bootstrap_v1", out var value, out var error);

        Assert.False(result);
        Assert.Equal(string.Empty, value);
        Assert.Contains("connection string is empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlRepository_UpsertMetaValue_Fails_WhenConnectionStringEmpty()
    {
        var repository = new PostgreSqlOrdersRepository(string.Empty);

        var result = repository.TryUpsertMetaValue("history_json_bootstrap_v1", "{\"state\":\"imported\"}", out var error);

        Assert.False(result);
        Assert.Contains("connection string is empty", error, StringComparison.OrdinalIgnoreCase);
    }
}
