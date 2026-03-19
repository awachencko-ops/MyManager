using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
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
