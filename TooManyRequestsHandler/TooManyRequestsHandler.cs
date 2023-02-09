using System.Net;
using Nito.AsyncEx;

namespace TooManyRequestsHandler;

/// <summary>
/// Handles HTTP 429 Requests and automatically retries after the returned time. Will also handle parallel calls.
/// Note that the Time per each request gets very high. Consider setting <see cref="HttpClient.Timeout" /> to a <see cref="TimeSpan"/> of -1ms (infinite) 
/// </summary>
public class TooManyRequestsHandler : HttpClientHandler
{
    private readonly PauseTokenSource _pauseSource = new();

    public PauseToken PauseToken { get; }

    private readonly AsyncLock _asyncLock = new();

#if NET5_0_OR_GREATER
    private readonly object _syncLock = new();
#endif

    public TooManyRequestsHandler()
    {
        PauseToken = _pauseSource.Token;
    }

#if NET5_0_OR_GREATER
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PauseToken.WaitWhilePaused(cancellationToken);

        HttpResponseMessage result = base.Send(request, cancellationToken);

        if (result.StatusCode != HttpStatusCode.TooManyRequests)
            return result;

        lock (_syncLock)
        {
            DateTimeOffset? time = result.Headers.RetryAfter?.Date;

            TimeSpan delay = time - DateTimeOffset.UtcNow ?? TimeSpan.Zero;

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    _pauseSource.IsPaused = true;

                    Thread.Sleep(delay);
                }
                finally
                {
                    _pauseSource.IsPaused = false;
                }
            }
        }

        return Send(request, cancellationToken);
    }
#endif

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // check if requests are paused and wait
        await PauseToken.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage result = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // if result is anything but 429, return (even if it may is an error)
        if (result.StatusCode != HttpStatusCode.TooManyRequests)
            return result;

        // create a locker which will unlock at the end of the stack
        using IDisposable locker = await _asyncLock.LockAsync(cancellationToken).ConfigureAwait(false);

        // calculate delay
        DateTimeOffset? time = result.Headers.RetryAfter?.Date;
        TimeSpan delay = time - DateTimeOffset.UtcNow ?? TimeSpan.Zero;

        // if delay is 0 or below, return new requests
        if (delay <= TimeSpan.Zero)
        {
            // very important to unlock
            locker.Dispose();

            // recursively recall itself
            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // otherwise pause requests
            _pauseSource.IsPaused = true;

            // then wait the calculated delay
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pauseSource.IsPaused = false;
        }

        // make sure to unlock again (otherwise the method would lock itself because of recurse)
        locker.Dispose();

        // recursively recall itself
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
