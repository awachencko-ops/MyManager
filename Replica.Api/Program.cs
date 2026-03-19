using System;
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

    builder.Services.AddSingleton<ILanOrderStore>(_ => new PostgreSqlLanOrderStore(replicaDbConnectionString));
}
else
{
    builder.Services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
