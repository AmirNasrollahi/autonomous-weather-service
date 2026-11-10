namespace WeatherService.Api.Settings;

public class WeatherApiSettings
{
    public const string SectionName = "WeatherApi";

    public string BaseUrl { get; set; } = string.Empty;
    public string Hourly { get; set; } = "temperature_2m";
    public double TimeoutSeconds { get; set; } = 2.5;
    public int CacheTtlMinutes { get; set; } = 60;
}
