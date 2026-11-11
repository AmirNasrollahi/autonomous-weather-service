using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherService.Api.Clients;
using WeatherService.Api.Configuration;
using WeatherService.Api.Data;
using WeatherService.Api.Data.Entities;

namespace WeatherService.Api.Services;

public class ForecastService(
    WeatherDbContext context,
    IWeatherClient client,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<WeatherConfiguration> configuration,
    ILogger<ForecastService> logger) : IForecastService
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2.9);
    private static readonly TimeSpan FallbackTimeout = TimeSpan.FromMilliseconds(400);
    private readonly WeatherConfiguration _config = configuration.Value;

    public async Task<string?> GetAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Deadline);

        try
        {
            string? result = await client.GetAsync(latitude, longitude, cts.Token);

            if (result is not null)
            {
                _ = SaveAsync(latitude, longitude, result);
                return result;
            }

            logger.LogWarning("API returned no data, retrieving fallback for ({Latitude}, {Longitude})", latitude, longitude);
            return await GetFallbackAsync(latitude, longitude, cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning("API call timed out, retrieving fallback for ({Latitude}, {Longitude})", latitude, longitude);
            using CancellationTokenSource fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fallbackCts.CancelAfter(FallbackTimeout);
            return await GetFallbackAsync(latitude, longitude, fallbackCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during fetch, retrieving fallback for ({Latitude}, {Longitude})", latitude, longitude);
            return await GetFallbackAsync(latitude, longitude, cts.Token);
        }
    }

    private async Task<string?> GetFallbackAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        try
        {
            WeatherRecord? record = await context.WeatherRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Latitude == latitude && x.Longitude == longitude, cancellationToken);

            if (record is null)
            {
                return null;
            }

            if (_config.MaxDataAgeSeconds <= 0)
            {
                logger.LogWarning("MaxDataAgeSeconds is 0, fallback data is disabled");
                return null;
            }

            DateTime validSince = DateTime.UtcNow.AddSeconds(-_config.MaxDataAgeSeconds);

            if (record.CreatedAtUtc < validSince)
            {
                logger.LogWarning("Record for ({Latitude}, {Longitude}) is expired (Created: {CreatedAt})", latitude, longitude, record.CreatedAtUtc);
                return null;
            }

            return record.RawJson;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read fallback for ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
    }

    private async Task SaveAsync(double latitude, double longitude, string data)
    {
        try
        {
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            WeatherDbContext dbContext = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();

            string sql = """
                INSERT INTO WeatherRecords (Latitude, Longitude, RawJson, CreatedAtUtc)
                VALUES ({0}, {1}, {2}, {3})
                ON CONFLICT(Latitude, Longitude) DO UPDATE SET
                    RawJson = excluded.RawJson,
                    CreatedAtUtc = excluded.CreatedAtUtc;
                """;

            await dbContext.Database.ExecuteSqlRawAsync(sql, latitude, longitude, data, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save weather record for ({Latitude}, {Longitude})", latitude, longitude);
        }
    }
}
