using System.Collections.Concurrent;

namespace WeatherService.Api.Common;

public class SingleFlightGroup
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _calls = new();

    public async Task<string?> DoAsync(
        string key,
        Func<Task<string?>> factory,
        CancellationToken callerCancellationToken = default)
    {
        Lazy<Task<string?>>? lazy = null;

        lazy = _calls.GetOrAdd(
            key,
            callKey => new Lazy<Task<string?>>(
                () => Task.Run(async () =>
                {
                    try
                    {
                        return await factory();
                    }
                    finally
                    {
                        _calls.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(callKey, lazy!));
                    }
                }),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(callerCancellationToken);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (lazy.IsValueCreated && (lazy.Value.IsFaulted || lazy.Value.IsCanceled))
            {
                _calls.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(key, lazy));
            }
            throw;
        }
    }
}
