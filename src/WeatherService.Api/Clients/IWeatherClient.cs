namespace WeatherService.Api.Clients;

public interface IWeatherClient
{
    Task<string?> GetAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
