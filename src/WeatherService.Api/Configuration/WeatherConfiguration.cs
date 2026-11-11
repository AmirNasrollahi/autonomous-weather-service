namespace WeatherService.Api.Configuration;

public class WeatherConfiguration
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Hourly { get; set; } = "temperature_2m";
    public double TimeoutSeconds { get; set; } = 2.5;
    public int MaxDataAgeSeconds { get; set; } = 600;
}
