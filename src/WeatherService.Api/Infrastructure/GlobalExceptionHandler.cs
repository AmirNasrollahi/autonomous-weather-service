using Microsoft.AspNetCore.Diagnostics;

namespace WeatherService.Api.Infrastructure;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception intercepted: {Message}", exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsync("null", cancellationToken);

        return true;
    }
}
