using Gate.Server.Database;
using Gate.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args);

builder.UseSystemd();

builder.ConfigureServices((hostContext, services) =>
{
    var connectionString = hostContext.Configuration.GetConnectionString("GateDb")
        ?? "Host=localhost;Port=5432;Database=gatedb;Username=gateuser;Password=gatepass";

    services.AddDbContext<GateDbContext>(options =>
    {
        options.UseNpgsql(connectionString);
    });

    services.AddSingleton<SystemdManager>();
    services.AddSingleton<PermissionChecker>();
    services.AddHostedService<TcpGateServer>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<GateDbContext>();
        await DbInitializer.SeedAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not initialize database on startup (PostgreSQL may not be started yet or offline during local dev)");
    }
}

await app.RunAsync();
