using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WeatherService.Api.Clients;
using WeatherService.Api.Configuration;
using WeatherService.Api.Data;
using WeatherService.Api.Data.Entities;
using WeatherService.Api.Services;

namespace WeatherService.Tests;

public class ForecastServiceTests : IDisposable
{
    private const double Latitude = 52.52;
    private const double Longitude = 13.41;
    private const string ValidJson = """{"hourly":{"temperature_2m":[20.5]}}""";

    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly WeatherDbContext _context;
    private readonly IWeatherClient _client;
    private readonly IOptions<WeatherConfiguration> _options;
    private readonly ILogger<ForecastService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ForecastServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"weather_test_{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={_dbPath};Default Timeout=1;";

        ServiceCollection services = new();
        services.AddDbContext<WeatherDbContext>(options =>
            options.UseSqlite(connectionString));

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _context = _serviceProvider.GetRequiredService<WeatherDbContext>();
        _context.Database.EnsureCreated();
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        _client = Substitute.For<IWeatherClient>();
        _logger = Substitute.For<ILogger<ForecastService>>();

        WeatherConfiguration config = new()
        {
            BaseUrl = "https://api.open-meteo.com/v1/forecast",
            Hourly = "temperature_2m",
            TimeoutSeconds = 2.5,
            MaxDataAgeSeconds = 600
        };

        _options = Options.Create(config);
    }

    private ForecastService CreateService()
    {
        return new ForecastService(_context, _client, _scopeFactory, _options, _logger);
    }

    private ForecastService CreateService(WeatherConfiguration config)
    {
        return new ForecastService(_context, _client, _scopeFactory, Options.Create(config), _logger);
    }

    private async Task SeedRecordAsync(
        double latitude,
        double longitude,
        string json,
        DateTime createdAtUtc)
    {
        WeatherRecord record = new()
        {
            Latitude = latitude,
            Longitude = longitude,
            RawJson = json,
            CreatedAtUtc = createdAtUtc
        };

        _context.WeatherRecords.Add(record);
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
            string wal = _dbPath + "-wal";
            if (File.Exists(wal))
            {
                File.Delete(wal);
            }
            string shm = _dbPath + "-shm";
            if (File.Exists(shm))
            {
                File.Delete(shm);
            }
        }
        catch
        {
            // Ignore cleanup failure in temp dir
        }
    }

    [Fact]
    public async Task ApiSuccess_DbSuccess_ReturnsFreshDataImmediately()
    {
        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(ValidJson);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Equal(ValidJson, result);

        await Task.Delay(200);

        WeatherRecord? saved = await _context.WeatherRecords.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal(ValidJson, saved.RawJson);
    }

    [Fact]
    public async Task RepeatedCalls_SameCoordinates_PerformsAtomicUpsert()
    {
        string firstJson = """{"hourly":{"temperature_2m":[10.0]}}""";
        string secondJson = """{"hourly":{"temperature_2m":[20.0]}}""";

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(firstJson);

        ForecastService service = CreateService();

        await service.GetAsync(Latitude, Longitude);
        await Task.Delay(200);

        int countAfterFirst = await _context.WeatherRecords.CountAsync();
        Assert.Equal(1, countAfterFirst);

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(secondJson);

        await service.GetAsync(Latitude, Longitude);
        await Task.Delay(200);

        int countAfterSecond = await _context.WeatherRecords.CountAsync();
        Assert.Equal(1, countAfterSecond);

        WeatherRecord? record = await _context.WeatherRecords.FirstOrDefaultAsync();
        Assert.NotNull(record);
        Assert.Equal(secondJson, record.RawJson);
    }

    [Fact]
    public async Task ApiFail_DbHasValidData_ReturnsPreviousData()
    {
        await SeedRecordAsync(Latitude, Longitude, ValidJson, DateTime.UtcNow.AddSeconds(-10));

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Equal(ValidJson, result);
    }

    [Fact]
    public async Task ApiFail_DbHasOldData_ReturnsNull()
    {
        await SeedRecordAsync(Latitude, Longitude, ValidJson, DateTime.UtcNow.AddSeconds(-700));

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Null(result);
    }

    [Fact]
    public async Task ApiFail_DbEmpty_ReturnsNull()
    {
        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Null(result);
    }

    [Fact]
    public async Task ApiSuccess_DbFail_ReturnsFreshDataWithoutBlocking()
    {
        string freshJson = """{"hourly":{"temperature_2m":[25.0]}}""";

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(freshJson);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Equal(freshJson, result);
    }

    [Fact]
    public async Task DbErrorDuringFallback_ReturnsNull()
    {
        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        DbContextOptions<WeatherDbContext> faultyOptions = new DbContextOptionsBuilder<WeatherDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        WeatherDbContext faultyContext = new(faultyOptions);
        faultyContext.Dispose();

        ForecastService service = new(faultyContext, _client, _scopeFactory, _options, _logger);

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Null(result);
    }

    [Fact]
    public async Task ApiTimeout_AttemptsFallbackAndReturnsValidDataIfPresent()
    {
        await SeedRecordAsync(Latitude, Longitude, ValidJson, DateTime.UtcNow.AddSeconds(-10));

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                CancellationToken token = callInfo.ArgAt<CancellationToken>(2);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return (string?)ValidJson;
            });

        ForecastService service = CreateService();

        DateTime start = DateTime.UtcNow;
        string? result = await service.GetAsync(Latitude, Longitude);
        TimeSpan elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed.TotalSeconds < 3.5, $"Should respect deadline but took {elapsed.TotalSeconds:F1}s");
        Assert.Equal(ValidJson, result);
    }

    [Fact]
    public async Task ConcurrentRequests_PreventDuplicateExternalCalls()
    {
        int callCount = 0;

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(100);
                return (string?)ValidJson;
            });

        ForecastService service = CreateService();

        Task<string?>[] tasks = Enumerable
            .Range(0, 100)
            .Select(_ => service.GetAsync(Latitude, Longitude))
            .ToArray();

        string?[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(ValidJson, r));
        Assert.True(callCount >= 1);
    }

    [Fact]
    public async Task MaxDataAgeZero_NeverUsesFallback()
    {
        await SeedRecordAsync(Latitude, Longitude, ValidJson, DateTime.UtcNow);

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        WeatherConfiguration config = new()
        {
            MaxDataAgeSeconds = 0
        };

        ForecastService service = CreateService(config);

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Null(result);
    }

    [Fact]
    public async Task FallbackOnlyReturnsMatchingCoordinates()
    {
        await SeedRecordAsync(48.13, 11.58, """{"city":"munich"}""", DateTime.UtcNow);

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        ForecastService service = CreateService();

        string? result = await service.GetAsync(Latitude, Longitude);

        Assert.Null(result);
    }

    [Fact]
    public async Task UserCancellation_ThrowsOperationCancelled()
    {
        using CancellationTokenSource userCts = new();

        _client.GetAsync(Latitude, Longitude, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                CancellationToken token = callInfo.ArgAt<CancellationToken>(2);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return (string?)ValidJson;
            });

        ForecastService service = CreateService();

        await userCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync(Latitude, Longitude, userCts.Token));
    }
}
