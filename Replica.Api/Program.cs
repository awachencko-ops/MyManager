using System;
using Microsoft.EntityFrameworkCore;
using Replica.Api.Data;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var replicaDbConnectionString = builder.Configuration.GetConnectionString("ReplicaDb") ?? string.Empty;
var configuredStoreMode = builder.Configuration["ReplicaApi:StoreMode"]?.Trim();
var effectiveStoreMode = string.IsNullOrWhiteSpace(configuredStoreMode)
    ? (string.IsNullOrWhiteSpace(replicaDbConnectionString) ? "InMemory" : "PostgreSql")
    : configuredStoreMode;

builder.WebHost.ConfigureKestrel(options =>
{
    // Stage 3 requirement: LAN API endpoint on 0.0.0.0:5000
    options.ListenAnyIP(5000);
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

var app = builder.Build();

if (string.Equals(effectiveStoreMode, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ReplicaDbContext>>();
    using var db = dbContextFactory.CreateDbContext();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCorrelationContext();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", (ILanOrderStore store) => Results.Ok(new
{
    status = "ok",
    service = "Replica.Api",
    store = store.GetType().Name,
    mode = effectiveStoreMode
}));

app.Run();
