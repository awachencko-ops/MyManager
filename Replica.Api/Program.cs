using System;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Replica.Api.Application.Abstractions;
using Replica.Api.Data;
using Replica.Api.Application.Behaviors;
using Replica.Api.Hubs;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var replicaDbConnectionString = builder.Configuration.GetConnectionString("ReplicaDb") ?? string.Empty;
var configuredStoreMode = builder.Configuration["ReplicaApi:StoreMode"]?.Trim();
var configuredBindAddress = builder.Configuration["ReplicaApi:BindAddress"]?.Trim();
var configuredPort = builder.Configuration.GetValue<int?>("ReplicaApi:Port") ?? 5000;
var effectiveAuthMode = ReplicaApiAuthConfiguration.ResolveMode(builder.Configuration);
var effectiveMigrationOptions = ReplicaApiMigrationConfiguration.Resolve(builder.Configuration);
if (configuredPort <= 0 || configuredPort > 65535)
    configuredPort = 5000;

var effectiveStoreMode = string.IsNullOrWhiteSpace(configuredStoreMode)
    ? (string.IsNullOrWhiteSpace(replicaDbConnectionString) ? "InMemory" : "PostgreSql")
    : configuredStoreMode;

builder.WebHost.ConfigureKestrel(options =>
{
    if (string.IsNullOrWhiteSpace(configuredBindAddress)
        || string.Equals(configuredBindAddress, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuredBindAddress, "::", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuredBindAddress, "+", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuredBindAddress, "*", StringComparison.OrdinalIgnoreCase))
    {
        options.ListenAnyIP(configuredPort);
        return;
    }

    if (IPAddress.TryParse(configuredBindAddress, out var parsedAddress))
    {
        options.Listen(parsedAddress, configuredPort);
        return;
    }

    options.ListenAnyIP(configuredPort);
});

if (string.Equals(effectiveStoreMode, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(replicaDbConnectionString))
        throw new InvalidOperationException("ReplicaApi:StoreMode=PostgreSql requires ConnectionStrings:ReplicaDb");

    builder.Services.AddDbContextFactory<ReplicaDbContext>(options =>
        options.UseNpgsql(replicaDbConnectionString));
    builder.Services.AddSingleton<ILanOrderStore, EfCoreLanOrderStore>();
}
else
{
    builder.Services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IReplicaApiTokenService, ReplicaApiTokenService>();
builder.Services.AddScoped<IReplicaApiCurrentActorAccessor, ReplicaApiCurrentActorAccessor>();
builder.Services.AddScoped<IReplicaApiCurrentUserAccessor, ReplicaApiCurrentActorAccessor>();
builder.Services.AddScoped<IReplicaApiAuthService, ReplicaApiAuthServiceAdapter>();
builder.Services.AddSingleton<IReplicaOrderPushPublisher, SignalRReplicaOrderPushPublisher>();
builder.Services.AddSingleton<IReplicaApiHistoryShadowWriter, FileReplicaApiHistoryShadowWriter>();
builder.Services.AddMediatR(typeof(Program));
builder.Services.AddReplicaApiCommandPipeline();
builder.Services.Configure<ReplicaApiCommandPipelineOptions>(
    builder.Configuration.GetSection("ReplicaApi:CommandPipeline"));
builder.Services.Configure<ReplicaApiMigrationOptions>(
    builder.Configuration.GetSection("ReplicaApi:Migration"));

var app = builder.Build();

if (string.Equals(effectiveStoreMode, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ReplicaDbContext>>();
    using var db = dbContextFactory.CreateDbContext();
    db.Database.Migrate();
}

using (var bootstrapScope = app.Services.CreateScope())
{
    var bootstrapStore = bootstrapScope.ServiceProvider.GetRequiredService<ILanOrderStore>();
    var bootstrapLogger = bootstrapScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Replica.Api.Bootstrap");
    ReplicaApiBootstrapUsers.EnsurePresent(bootstrapStore, builder.Configuration, bootstrapLogger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCorrelationContext();
app.UseReplicaApiCurrentUserContext();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ReplicaOrderHub>("/hubs/orders");

app.MapGet("/live", () => Results.Ok(new
{
    status = "live",
    service = "Replica.Api",
    mode = effectiveStoreMode,
    authMode = effectiveAuthMode,
    migration = new
    {
        effectiveMigrationOptions.DualWriteEnabled,
        effectiveMigrationOptions.ShadowWriteFailurePolicy
    }
}));

app.MapGet("/ready", async (IServiceProvider services, ILanOrderStore store) =>
{
    if (!string.Equals(effectiveStoreMode, "PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(new
        {
            status = "ready",
            service = "Replica.Api",
            store = store.GetType().Name,
            mode = effectiveStoreMode,
            authMode = effectiveAuthMode
        });
    }

    try
    {
        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ReplicaDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            return Results.Json(new
            {
                status = "not_ready",
                reason = "database connection failed",
                mode = effectiveStoreMode,
                authMode = effectiveAuthMode
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
        return Results.Ok(new
        {
            status = pendingMigrations.Count == 0 ? "ready" : "degraded",
            service = "Replica.Api",
            store = store.GetType().Name,
            mode = effectiveStoreMode,
            authMode = effectiveAuthMode,
            pendingMigrations = pendingMigrations.Count
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "not_ready",
            reason = ex.Message,
            mode = effectiveStoreMode,
            authMode = effectiveAuthMode
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/metrics", () => Results.Ok(ReplicaApiObservability.GetSnapshot()));

app.MapGet("/slo", () =>
{
    var snapshot = ReplicaApiObservability.GetSnapshot();
    const double availabilityTarget = 0.995;
    const double p95LatencyTargetMs = 500;
    const double writeSuccessTarget = 0.99;

    var availabilityOk = snapshot.HttpAvailabilityRatio >= availabilityTarget;
    var latencyOk = snapshot.HttpLatencyP95Ms <= p95LatencyTargetMs;
    var writeSuccessOk = snapshot.WriteSuccessRatio >= writeSuccessTarget;

    return Results.Ok(new
    {
        status = availabilityOk && latencyOk && writeSuccessOk ? "ok" : "degraded",
        evaluatedAtUtc = DateTime.UtcNow,
        targets = new
        {
            availabilityRatio = availabilityTarget,
            latencyP95Ms = p95LatencyTargetMs,
            writeSuccessRatio = writeSuccessTarget
        },
        current = new
        {
            snapshot.HttpAvailabilityRatio,
            snapshot.HttpLatencyP95Ms,
            snapshot.WriteSuccessRatio
        }
    });
});

app.MapGet("/health", (ILanOrderStore store) => Results.Ok(new
{
    status = "ok",
    service = "Replica.Api",
    store = store.GetType().Name,
    mode = effectiveStoreMode,
    authMode = effectiveAuthMode,
    migration = new
    {
        effectiveMigrationOptions.DualWriteEnabled,
        effectiveMigrationOptions.ShadowWriteFailurePolicy
    }
}));

app.Run();


