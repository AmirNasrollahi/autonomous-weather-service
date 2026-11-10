namespace WeatherService.Api.Services;

public interface IWeatherApiClient
{
    Task<string?> FetchWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
