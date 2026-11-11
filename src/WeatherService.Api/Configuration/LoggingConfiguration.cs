using Serilog;
using Serilog.Events;

namespace WeatherService.Api.Configuration;

public static class LoggingConfiguration
{
    public static WebApplicationBuilder ConfigureSerilog(this WebApplicationBuilder builder)
    {
        string logPath = Path.Combine(AppContext.BaseDirectory, "logs", "weather-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console()
            .WriteTo.File(
                path: logPath,
                restrictedToMinimumLevel: LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 5,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }
}
