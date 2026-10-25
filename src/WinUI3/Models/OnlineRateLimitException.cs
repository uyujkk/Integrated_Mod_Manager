using System.Net;

namespace ModFolderCopier.WinUI;

public sealed class OnlineRateLimitException : HttpRequestException
{
    public OnlineRateLimitException(TimeSpan? retryAfter)
        : base("GameBanana rate limit reached.", null, HttpStatusCode.TooManyRequests)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
