using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WeatherService.Api.Clients;
using WeatherService.Api.Common;
using WeatherService.Api.Configuration;
using WeatherService.Api.Data;
using WeatherService.Api.Infrastructure;
using WeatherService.Api.Services;

namespace WeatherService.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddWeatherServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WeatherConfiguration>(configuration.GetSection("Weather"));

        string rawConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=weather.db;Default Timeout=1;";

        SqliteConnectionStringBuilder connectionBuilder = new(rawConnectionString);
        if (!Path.IsPathRooted(connectionBuilder.DataSource))
        {
            connectionBuilder.DataSource = Path.Combine(AppContext.BaseDirectory, connectionBuilder.DataSource);
        }

        string connectionString = connectionBuilder.ToString();

        services.AddDbContext<WeatherDbContext>(options =>
            options.UseSqlite(connectionString, sqliteOptions => sqliteOptions.CommandTimeout(1)));

        services.AddSingleton<SingleFlightGroup>();
        services.AddHttpClient<IWeatherClient, WeatherClient>();
        services.AddScoped<IForecastService, ForecastService>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        WeatherDbContext context = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        try
        {
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database. Checking for database corruption");

            string connectionString = context.Database.GetConnectionString() ?? string.Empty;
            SqliteConnectionStringBuilder builder = new(connectionString);
            string dbFilePath = builder.DataSource;

            try
            {
                if (File.Exists(dbFilePath))
                {
                    string corruptFilePath = dbFilePath + ".corrupt";
                    File.Move(dbFilePath, corruptFilePath, overwrite: true);
                    logger.LogWarning("Corrupted database file moved to {CorruptPath}", corruptFilePath);
                }

                await context.Database.EnsureCreatedAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                logger.LogInformation("Database recreated successfully after corruption");
            }
            catch (Exception recoveryEx)
            {
                logger.LogCritical(recoveryEx, "Database recovery failed. Service will run without local persistence");
            }
        }
    }
}
