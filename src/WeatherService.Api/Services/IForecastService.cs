namespace WeatherService.Api.Services;

public interface IForecastService
{
    Task<string?> GetAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
