using FarmaciaSalacor.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Services;

public sealed class DbInitializerHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DbInitializerHostedService> _logger;

    public DbInitializerHostedService(IServiceProvider services, ILogger<DbInitializerHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Corre una sola vez al iniciar la app, sin bloquear el arranque.
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

            if (db.Database.IsMySql())
            {
                await db.Database.EnsureCreatedAsync(timeoutCts.Token);
            }
            else
            {
                await db.Database.MigrateAsync(timeoutCts.Token);
            }

            await DbSeeder.SeedAsync(db);
            _logger.LogInformation("Database initialized successfully");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            // No tumbamos el proceso: Railway necesita que el servicio responda healthcheck.
            _logger.LogError(ex, "Database initialization failed");
        }
    }
}
