using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WeatherService.Api.Clients;
using WeatherService.Api.Common;
using WeatherService.Api.Configuration;

namespace WeatherService.Tests;

public class WeatherClientTests
{
    private readonly WeatherConfiguration _config;
    private readonly IOptions<WeatherConfiguration> _options;
    private readonly ILogger<WeatherClient> _logger;
    private readonly SingleFlightGroup _singleFlight;

    public WeatherClientTests()
    {
        _config = new WeatherConfiguration
        {
            BaseUrl = "https://api.open-meteo.com/v1/forecast",
            Hourly = "temperature_2m",
            TimeoutSeconds = 2.5,
            MaxDataAgeSeconds = 600
        };

        _options = Options.Create(_config);
        _logger = Substitute.For<ILogger<WeatherClient>>();
        _singleFlight = new SingleFlightGroup();
    }

    private WeatherClient CreateClient(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler);
        return new WeatherClient(httpClient, _options, _singleFlight, _logger);
    }

    [Fact]
    public async Task GetAsync_SuccessStatusCode_ReturnsJson()
    {
        string expectedJson = """{"hourly":{"temperature_2m":[15.2]}}""";
        TestHttpMessageHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(expectedJson)
        });

        WeatherClient client = CreateClient(handler);

        string? result = await client.GetAsync(52.52, 13.41);

        Assert.Equal(expectedJson, result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_HttpError_ReturnsNull()
    {
        TestHttpMessageHandler handler = new(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        WeatherClient client = CreateClient(handler);

        string? result = await client.GetAsync(52.52, 13.41);

        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ReturnsNull()
    {
        TestHttpMessageHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("invalid json content")
        });

        WeatherClient client = CreateClient(handler);

        string? result = await client.GetAsync(52.52, 13.41);

        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_NetworkException_ReturnsNull()
    {
        ThrowingHttpMessageHandler handler = new(new HttpRequestException("Connection refused"));

        WeatherClient client = CreateClient(handler);

        string? result = await client.GetAsync(52.52, 13.41);

        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class TestHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
