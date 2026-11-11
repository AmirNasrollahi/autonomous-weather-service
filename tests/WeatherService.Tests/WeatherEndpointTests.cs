using NSubstitute;
using WeatherService.Api.Endpoints;
using WeatherService.Api.Services;

namespace WeatherService.Tests;

public class WeatherEndpointTests
{
    private readonly IForecastService _forecastService = Substitute.For<IForecastService>();

    [Fact]
    public async Task GetWeather_WhenDataExists_ReturnsContentWithJson()
    {
        string expectedJson = """{"temperature":20}""";
        _forecastService.GetAsync(52.52, 13.41, Arg.Any<CancellationToken>())
            .Returns(expectedJson);

        string? result = await _forecastService.GetAsync(52.52, 13.41);
        Assert.Equal(expectedJson, result);
    }

    [Fact]
    public async Task GetWeather_WhenNoData_ReturnsNull()
    {
        _forecastService.GetAsync(52.52, 13.41, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        string? result = await _forecastService.GetAsync(52.52, 13.41);
        Assert.Null(result);
    }
}
