namespace DotNetRepoInspector.Persistence.Http;

public static class HttpInspectionSinkErrorCodes
{
    public const string InvalidConfiguration = "http-invalid-configuration";
    public const string TransportFailure = "http-transport-failure";
    public const string RequestTimeout = "http-request-timeout";
    public const string TransientResponse = "http-transient-response";
    public const string AuthenticationFailed = "http-authentication-failed";
    public const string EndpointNotFound = "http-endpoint-not-found";
    public const string RequestRejected = "http-request-rejected";
    public const string UnexpectedResponse = "http-unexpected-response";
}
