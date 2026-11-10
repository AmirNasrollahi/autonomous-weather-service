using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherService.Api.Common;
using WeatherService.Api.Settings;

namespace WeatherService.Api.Services;

public class WeatherApiClient(
    HttpClient httpClient,
    IOptions<WeatherApiSettings> settings,
    SingleFlightGroup singleFlight,
    ILogger<WeatherApiClient> logger) : IWeatherApiClient
{
    private readonly WeatherApiSettings _settings = settings.Value;

    public Task<string?> FetchWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        string key = string.Create(
            CultureInfo.InvariantCulture, 
            $"{latitude}_{longitude}_{_settings.Hourly}");

        return singleFlight.DoAsync(
            key, 
            () => ExecuteFetchAsync(latitude, longitude), 
            cancellationToken);
    }

    private async Task<string?> ExecuteFetchAsync(double latitude, double longitude)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        Dictionary<string, string?> queryParams = new()
        {
            ["latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
            ["hourly"] = _settings.Hourly
        };

        string url = QueryHelpers.AddQueryString(_settings.BaseUrl, queryParams);

        using CancellationTokenSource timeoutCts = new(timeout);

        try
        {
            logger.LogInformation("Sending outbound weather request to {Url}", url);

            using HttpResponseMessage response = await httpClient.GetAsync(url, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("External weather API responded with status code {StatusCode}", response.StatusCode);
                return null;
            }

            string rawJson = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            using JsonDocument jsonDocument = JsonDocument.Parse(rawJson);

            logger.LogInformation("Weather data received and validated for coords ({Latitude}, {Longitude})", latitude, longitude);
            return rawJson;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("External weather request timed out after {Timeout}s for coords ({Latitude}, {Longitude})", timeout.TotalSeconds, latitude, longitude);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error while calling external weather API for coords ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "External weather API returned invalid JSON for coords ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during weather API call for coords ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
    }
}
