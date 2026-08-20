namespace DotNetRepoInspector.Persistence.Http;

public sealed class HttpInspectionSnapshotSinkOptions
{
    public HttpInspectionSnapshotSinkOptions(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
    }

    public Uri Endpoint
    {
        get;
    }

    public string? BearerToken
    {
        get;
        init;
    }

    public int MaxAttempts
    {
        get;
        init;
    } = 3;

    public TimeSpan RetryBaseDelay
    {
        get;
        init;
    } = TimeSpan.FromMilliseconds(250);

    internal void Validate()
    {
        if (!Endpoint.IsAbsoluteUri ||
            (Endpoint.Scheme != Uri.UriSchemeHttp && Endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The HTTP sink endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(Endpoint));
        }

        if (!string.IsNullOrEmpty(Endpoint.UserInfo))
        {
            throw new ArgumentException(
                "The HTTP sink endpoint must not contain user information.",
                nameof(Endpoint));
        }

        if (MaxAttempts is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttempts),
                MaxAttempts,
                "HTTP sink attempts must be between 1 and 5.");
        }

        if (RetryBaseDelay < TimeSpan.Zero || RetryBaseDelay > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryBaseDelay),
                RetryBaseDelay,
                "HTTP sink retry base delay must be between zero and ten seconds.");
        }

        if (BearerToken is not null &&
            (BearerToken.Contains('\r') || BearerToken.Contains('\n')))
        {
            throw new ArgumentException(
                "The HTTP sink bearer token contains invalid characters.",
                nameof(BearerToken));
        }
    }
}
