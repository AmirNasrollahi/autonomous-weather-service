using WeatherService.Api.Common;

namespace WeatherService.Tests;

public class SingleFlightGroupTests
{
    [Fact]
    public async Task ConcurrentCalls_SameKey_ExecuteFactoryOnce()
    {
        SingleFlightGroup group = new();
        int executionCount = 0;
        TaskCompletionSource<string?> gate = new();

        Task<string?>[] tasks = Enumerable
            .Range(0, 100)
            .Select(_ => group.DoAsync("key", async () =>
            {
                Interlocked.Increment(ref executionCount);
                return await gate.Task;
            }))
            .ToArray();

        await Task.Delay(100);

        Assert.Equal(1, executionCount);

        gate.SetResult("result");

        string?[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("result", r));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task ConcurrentCalls_DifferentKeys_ExecuteFactoryForEach()
    {
        SingleFlightGroup group = new();
        int executionCount = 0;

        Task<string?>[] tasks = Enumerable
            .Range(0, 10)
            .Select(i => group.DoAsync($"key_{i}", async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(50);
                return $"result_{i}";
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(10, executionCount);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelSharedTask()
    {
        SingleFlightGroup group = new();
        TaskCompletionSource<string?> gate = new();
        using CancellationTokenSource callerCts = new();

        Task<string?> task1 = group.DoAsync("key", () => gate.Task, callerCts.Token);
        Task<string?> task2 = group.DoAsync("key", () => gate.Task);

        await callerCts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task1);

        gate.SetResult("done");

        string? result2 = await task2;
        Assert.Equal("done", result2);
    }

    [Fact]
    public async Task AfterCompletion_NewCallExecutesFactory()
    {
        SingleFlightGroup group = new();

        string? result1 = await group.DoAsync("key", () => Task.FromResult<string?>("first"));

        string? result2 = await group.DoAsync("key", () => Task.FromResult<string?>("second"));

        Assert.Equal("first", result1);
        Assert.Equal("second", result2);
    }
}
