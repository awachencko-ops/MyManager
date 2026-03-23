using Microsoft.EntityFrameworkCore;
using Replica.Api.Data.Entities;

namespace Replica.Api.Data;

public sealed class ReplicaDbContext : DbContext
{
    public ReplicaDbContext(DbContextOptions<ReplicaDbContext> options) : base(options)
    {
    }

    public DbSet<OrderRecord> Orders => Set<OrderRecord>();
    public DbSet<OrderItemRecord> OrderItems => Set<OrderItemRecord>();
    public DbSet<OrderEventRecord> OrderEvents => Set<OrderEventRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<StorageMetaRecord> StorageMeta => Set<StorageMetaRecord>();
    public DbSet<OrderRunLockRecord> OrderRunLocks => Set<OrderRunLockRecord>();
    public DbSet<OrderRunIdempotencyRecord> OrderRunIdempotency => Set<OrderRunIdempotencyRecord>();
    public DbSet<OrderWriteIdempotencyRecord> OrderWriteIdempotency => Set<OrderWriteIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderRecord>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.InternalId);
            entity.Property(x => x.InternalId).HasColumnName("internal_id");
            entity.Property(x => x.OrderNumber).HasColumnName("order_number").HasMaxLength(256).HasDefaultValue(string.Empty);
            entity.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(256).HasDefaultValue(string.Empty);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(128).HasDefaultValue(string.Empty);
            entity.Property(x => x.ArrivalDate).HasColumnName("arrival_date").HasColumnType("timestamp without time zone");
            entity.Property(x => x.OrderDate).HasColumnName("order_date").HasColumnType("timestamp without time zone");
            entity.Property(x => x.StartMode).HasColumnName("start_mode");
            entity.Property(x => x.TopologyMarker).HasColumnName("topology_marker");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");

            entity.HasIndex(x => x.OrderNumber).HasDatabaseName("ix_orders_order_number");
            entity.HasIndex(x => x.ArrivalDate).HasDatabaseName("ix_orders_arrival_date");
        });

        modelBuilder.Entity<OrderItemRecord>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(x => x.ItemId);
            entity.Property(x => x.ItemId).HasColumnName("item_id");
            entity.Property(x => x.OrderInternalId).HasColumnName("order_internal_id");
            entity.Property(x => x.SequenceNo).HasColumnName("sequence_no");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");

            entity.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderInternalId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.OrderInternalId).HasDatabaseName("ix_order_items_order_internal_id");
            entity.HasIndex(x => new { x.OrderInternalId, x.SequenceNo }).IsUnique().HasDatabaseName("uq_order_items_sequence");
            entity.ToTable(tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_order_items_sequence_non_negative", "sequence_no >= 0");
            });
        });

        modelBuilder.Entity<OrderEventRecord>(entity =>
        {
            entity.ToTable("order_events");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedOnAdd();
            entity.Property(x => x.OrderInternalId).HasColumnName("order_internal_id").HasDefaultValue(string.Empty);
            entity.Property(x => x.ItemId).HasColumnName("item_id").HasDefaultValue(string.Empty);
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128);
            entity.Property(x => x.EventSource).HasColumnName("event_source").HasMaxLength(128);
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");

            entity.HasIndex(x => x.OrderInternalId).HasDatabaseName("ix_order_events_order_internal_id");
        });

        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.UserName);
            entity.Property(x => x.UserName).HasColumnName("user_name");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<StorageMetaRecord>(entity =>
        {
            entity.ToTable("storage_meta");
            entity.HasKey(x => x.MetaKey);
            entity.Property(x => x.MetaKey).HasColumnName("meta_key");
            entity.Property(x => x.MetaValue).HasColumnName("meta_value");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<OrderRunLockRecord>(entity =>
        {
            entity.ToTable("order_run_locks");
            entity.HasKey(x => x.OrderInternalId);
            entity.Property(x => x.OrderInternalId).HasColumnName("order_internal_id");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.LeaseToken).HasColumnName("lease_token").HasDefaultValue(string.Empty);
            entity.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasDefaultValue(string.Empty);
            entity.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamp without time zone");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");

            entity.HasIndex(x => x.IsActive).HasDatabaseName("ix_order_run_locks_is_active");
        });

        modelBuilder.Entity<OrderRunIdempotencyRecord>(entity =>
        {
            entity.ToTable("order_run_idempotency");
            entity.HasKey(x => new { x.OrderInternalId, x.CommandName, x.IdempotencyKey });
            entity.Property(x => x.OrderInternalId).HasColumnName("order_internal_id");
            entity.Property(x => x.CommandName).HasColumnName("command_name").HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(128);
            entity.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(256).HasDefaultValue(string.Empty);
            entity.Property(x => x.ResultKind).HasColumnName("result_kind").HasMaxLength(32);
            entity.Property(x => x.Error).HasColumnName("error").HasDefaultValue(string.Empty);
            entity.Property(x => x.CurrentVersion).HasColumnName("current_version");
            entity.Property(x => x.ResponseOrderJson).HasColumnName("response_order_json").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");

            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_order_run_idempotency_created_at");
        });

        modelBuilder.Entity<OrderWriteIdempotencyRecord>(entity =>
        {
            entity.ToTable("order_write_idempotency");
            entity.HasKey(x => new { x.CommandName, x.IdempotencyKey });
            entity.Property(x => x.CommandName).HasColumnName("command_name").HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(128);
            entity.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(256).HasDefaultValue(string.Empty);
            entity.Property(x => x.OrderInternalId).HasColumnName("order_internal_id").HasDefaultValue(string.Empty);
            entity.Property(x => x.ResultKind).HasColumnName("result_kind").HasMaxLength(32);
            entity.Property(x => x.Error).HasColumnName("error").HasDefaultValue(string.Empty);
            entity.Property(x => x.CurrentVersion).HasColumnName("current_version");
            entity.Property(x => x.ResponseOrderJson).HasColumnName("response_order_json").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");

            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_order_write_idempotency_created_at");
            entity.HasIndex(x => x.OrderInternalId).HasDatabaseName("ix_order_write_idempotency_order_internal_id");
        });
    }
}
