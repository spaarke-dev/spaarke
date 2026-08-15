using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Spaarke.Dataverse;

/// <summary>
/// Canonical unwrap of the underlying WCF <see cref="ServiceClient"/> from the composite
/// <see cref="IDataverseService"/> facade, for the generic SDK operations (QueryExpression,
/// FetchExpression, RetrieveEntityRequest, UpsertRequest, ...) that the narrow interface does
/// not expose.
/// </summary>
/// <remarks>
/// The sole registered implementation, <see cref="DataverseServiceClientImpl"/>, WRAPS a
/// ServiceClient and exposes it via <see cref="DataverseServiceClientImpl.OrganizationService"/>
/// — it does NOT derive from ServiceClient. A direct <c>is/as ServiceClient</c> cast of
/// <see cref="IDataverseService"/> therefore ALWAYS fails at runtime (the historical "Bug-1"
/// class that shipped broken on the invoice-totals path). This extension is the SINGLE supported
/// way to reach the ServiceClient; consumers MUST NOT re-add per-file downcast helpers.
/// </remarks>
public static class DataverseServiceClientExtensions
{
    /// <summary>
    /// Resolves the underlying ready <see cref="ServiceClient"/>, failing loud with the actual
    /// runtime type when the backing service is not a ready <see cref="DataverseServiceClientImpl"/>.
    /// Never returns null; never returns a not-ready client.
    /// </summary>
    /// <param name="service">The composite Dataverse facade (the DI-registered singleton).</param>
    /// <param name="consumerName">Caller name, surfaced in the thrown message for diagnosis.</param>
    /// <returns>The ready, unwrapped <see cref="ServiceClient"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="service"/> is not a <see cref="DataverseServiceClientImpl"/>, or
    /// its <see cref="DataverseServiceClientImpl.OrganizationService"/> is null / not ready.
    /// </exception>
    public static ServiceClient UnwrapServiceClient(this IDataverseService service, string consumerName)
    {
        if (service is not DataverseServiceClientImpl impl)
        {
            throw new InvalidOperationException(
                $"{consumerName} requires IDataverseService to be backed by DataverseServiceClientImpl " +
                $"for direct ServiceClient/SDK operations (actual: {service?.GetType().FullName ?? "null"}).");
        }

        var serviceClient = impl.OrganizationService;
        if (serviceClient is null || !serviceClient.IsReady)
        {
            throw new InvalidOperationException(
                $"{consumerName} requires a ready Dataverse ServiceClient " +
                "(DataverseServiceClientImpl.OrganizationService) for direct SDK operations.");
        }

        return serviceClient;
    }

    /// <summary>
    /// Best-effort variant of <see cref="UnwrapServiceClient"/>: returns the underlying ready
    /// <see cref="ServiceClient"/>, or <c>null</c> (logging at Error) when the backing service is
    /// not a ready <see cref="DataverseServiceClientImpl"/>. For callers that degrade gracefully
    /// (return empty / null / fail-closed) rather than throwing.
    /// </summary>
    /// <param name="service">The composite Dataverse facade (the DI-registered singleton).</param>
    /// <param name="consumerName">Caller name, surfaced in the logged message for diagnosis.</param>
    /// <param name="logger">Logger used to record the resolution failure at Error.</param>
    /// <returns>The ready <see cref="ServiceClient"/>, or <c>null</c> when it cannot be resolved.</returns>
    public static ServiceClient? TryUnwrapServiceClient(
        this IDataverseService service,
        string consumerName,
        ILogger logger)
    {
        if (service is not DataverseServiceClientImpl impl)
        {
            logger.LogError(
                "{Consumer}: IDataverseService is not DataverseServiceClientImpl (actual: {Type})",
                consumerName, service?.GetType().FullName ?? "null");
            return null;
        }

        var serviceClient = impl.OrganizationService;
        if (serviceClient is null || !serviceClient.IsReady)
        {
            logger.LogError("{Consumer}: ServiceClient is not ready", consumerName);
            return null;
        }

        return serviceClient;
    }
}
