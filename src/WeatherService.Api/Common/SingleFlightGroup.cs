using System.Collections.Concurrent;

namespace WeatherService.Api.Common;

public class SingleFlightGroup
{
    private readonly ConcurrentDictionary<string, Task<string?>> _calls = new();

    public async Task<string?> DoAsync(string key, Func<Task<string?>> factory, CancellationToken callerCancellationToken = default)
    {
        Task<string?> task = _calls.GetOrAdd(key, _ =>
        {
            Task<string?> sharedTask = Task.Run(async () =>
            {
                try
                {
                    return await factory();
                }
                finally
                {
                    _calls.TryRemove(key, out Task<string?>? _);
                }
            });

            return sharedTask;
        });

        return await task.WaitAsync(callerCancellationToken);
    }
}
