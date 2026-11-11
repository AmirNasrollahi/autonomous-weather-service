using WeatherService.Api;
using WeatherService.Api.Configuration;
using WeatherService.Api.Endpoints;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSerilog();

builder.Services.AddWeatherServices(builder.Configuration);

WebApplication app = builder.Build();

app.UseExceptionHandler();

await app.InitializeDatabaseAsync();

app.MapWeatherEndpoint();

app.Run();
