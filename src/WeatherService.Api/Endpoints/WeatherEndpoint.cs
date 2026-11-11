using WeatherService.Api.Services;

namespace WeatherService.Api.Endpoints;

public static class WeatherEndpoint
{
    public static void MapWeatherEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/weather", GetWeatherAsync);
    }

    private static async Task<IResult> GetWeatherAsync(
        IForecastService forecastService,
        double latitude = 52.52,
        double longitude = 13.41,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(latitude) || latitude is < -90.0 or > 90.0 ||
            !double.IsFinite(longitude) || longitude is < -180.0 or > 180.0)
        {
            return Results.Content("null", "application/json");
        }

        string? result = await forecastService.GetAsync(latitude, longitude, cancellationToken);

        return Results.Content(result ?? "null", "application/json");
    }
}
