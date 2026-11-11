using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherService.Api.Common;
using WeatherService.Api.Configuration;

namespace WeatherService.Api.Clients;

public class WeatherClient(
    HttpClient httpClient,
    IOptions<WeatherConfiguration> configuration,
    SingleFlightGroup singleFlight,
    ILogger<WeatherClient> logger) : IWeatherClient
{
    private readonly WeatherConfiguration _config = configuration.Value;

    public Task<string?> GetAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{latitude}_{longitude}_{_config.Hourly}");

        return singleFlight.DoAsync(key, () => FetchAsync(latitude, longitude), cancellationToken);
    }

    private async Task<string?> FetchAsync(double latitude, double longitude)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        Dictionary<string, string?> parameters = new()
        {
            ["latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
            ["hourly"] = _config.Hourly
        };

        string url = QueryHelpers.AddQueryString(_config.BaseUrl, parameters);

        using CancellationTokenSource cts = new(timeout);

        try
        {
            logger.LogInformation("Sending GET request to {Url}", url);

            using HttpResponseMessage response = await httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Request failed with status code {StatusCode}", response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            using JsonDocument _ = JsonDocument.Parse(json);

            logger.LogInformation("Received valid weather data for ({Latitude}, {Longitude})", latitude, longitude);
            return json;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning("Request timed out after {Timeout}s for ({Latitude}, {Longitude})", timeout.TotalSeconds, latitude, longitude);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error requesting weather for ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON received for ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error requesting weather for ({Latitude}, {Longitude})", latitude, longitude);
            return null;
        }
    }
}
