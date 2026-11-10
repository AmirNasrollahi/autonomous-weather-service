namespace WeatherService.Api.Data.Entities;

public sealed class WeatherRecord
{
    public long Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
