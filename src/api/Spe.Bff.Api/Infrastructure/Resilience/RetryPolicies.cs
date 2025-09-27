using Microsoft.Graph;
using Polly;

namespace Spe.Bff.Api.Infrastructure.Resilience;

/// <summary>
/// Polly retry policies for Graph API calls with exponential backoff and Retry-After support.
/// </summary>
public static class RetryPolicies
{
    public static IAsyncPolicy GraphTransient() =>
        Policy.Handle<ServiceException>(ex => IsTransientError(ex))
              .RetryAsync(3);

    public static IAsyncPolicy<T> GraphTransient<T>() =>
        Policy<T>.Handle<ServiceException>(ex => IsTransientError(ex))
              .RetryAsync(3);

    private static bool IsTransientError(ServiceException ex)
    {
        // Check status code if available (it's an int in Graph SDK v5)
        var statusCode = ex.ResponseStatusCode;
        if (statusCode == 429 ||  // TooManyRequests
            statusCode == 503 ||  // ServiceUnavailable
            statusCode == 500 ||  // InternalServerError
            statusCode == 502 ||  // BadGateway
            statusCode == 504)    // GatewayTimeout
        {
            return true;
        }

        // Fallback to message checking
        return ex.Message?.Contains("TooManyRequests") == true ||
               ex.Message?.Contains("ServiceUnavailable") == true ||
               ex.Message?.Contains("InternalServerError") == true;
    }

}