using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// The runtime answer to "which customer is this BFF serving?" (D-14, unified-access-control-r2
/// task 123). Inject this — not <see cref="IOptions{TOptions}"/> of <see cref="CustomerOptions"/> —
/// wherever the customer is needed for a cache-key prefix, a log scope or a metric dimension.
///
/// <para>
/// 🔴 THE POINT OF THIS TYPE is that <see cref="Id"/> THROWS when the identity is unresolved. Reading
/// <c>CustomerOptions.Id</c> directly yields an empty string in that state, and an empty string used as
/// a cache-key prefix is a SHARED key across every customer — the precise defect task 122 found in the
/// Foundry agent-thread key, where two callers passed compile-time constants into the tenant slot and
/// produced one global thread. There is no default and no sentinel; an unresolved identity is an error
/// at the point of use.
/// </para>
///
/// <para>
/// Registered as a singleton (ADR-010: concrete, no interface — there is one implementation and no
/// testing seam is needed, since <see cref="CustomerIdResolver"/> holds the logic worth testing and is
/// a pure static). Resolution happens once, at construction.
/// </para>
///
/// <para>
/// ⚠️ This is defence in depth, NOT the customer boundary. The boundary is the dedicated per-customer
/// resource — per D-12 §3 and the ADR-009 amendment, Redis access control is per-INSTANCE, not
/// per-keyspace, so a customer-id key prefix is a convention our code enforces rather than one Redis
/// enforces. Nothing here softens the dedicated-per-customer-resource decision.
/// </para>
/// </summary>
public sealed class CustomerIdentity
{
    private readonly string? _id;
    private readonly string _unresolvedMessage;

    public CustomerIdentity(
        IOptions<CustomerOptions> options,
        IConfiguration configuration,
        ILogger<CustomerIdentity> logger)
    {
        var configuredValue = options.Value.Id;
        var resourceGroupName = configuration[CustomerIdResolver.ResourceGroupEnvironmentVariable];

        (_id, Source) = CustomerIdResolver.Resolve(configuredValue, resourceGroupName);
        _unresolvedMessage = CustomerIdResolver.BuildUnresolvedMessage(configuredValue, resourceGroupName);

        switch (Source)
        {
            case CustomerIdSource.Explicit:
                logger.LogInformation(
                    "Customer identity resolved to {CustomerId} from the explicit {SettingKey} setting.",
                    _id,
                    CustomerIdResolver.ExplicitSettingKey);
                break;

            // Logged at Warning, not Information, deliberately. Derivation is a legitimate path and is
            // why existing per-customer stamps need no deployment change — but it is a FALLBACK, and
            // D-14 §8 asks that drift into it be visible rather than assumed. A stamp running on the
            // derived path forever is a stamp whose app settings were never completed.
            case CustomerIdSource.DerivedFromResourceGroup:
                logger.LogWarning(
                    "Customer identity DERIVED as {CustomerId} from {ResourceGroupVariable}='{ResourceGroupName}' "
                    + "because {SettingKey} is not set. This is a supported fallback, not the intended "
                    + "configuration — set {SettingEnvironmentName} explicitly on this stamp.",
                    _id,
                    CustomerIdResolver.ResourceGroupEnvironmentVariable,
                    resourceGroupName,
                    CustomerIdResolver.ExplicitSettingKey,
                    CustomerIdResolver.ExplicitSettingEnvironmentName);
                break;

            // Reached only in Development / Testing — CustomerOptionsValidator fails startup in every
            // deployed environment before this can be constructed. Critical rather than Warning because
            // any consumer that now asks for the id will get an exception.
            default:
                logger.LogCritical("{UnresolvedMessage}", _unresolvedMessage);
                break;
        }
    }

    /// <summary>How the identity was established. <see cref="CustomerIdSource.Unresolved"/> when it was not.</summary>
    public CustomerIdSource Source { get; }

    /// <summary>
    /// True when a customer identity is available. Check this only where an unresolved identity is a
    /// legitimate state to handle; everywhere else just read <see cref="Id"/> and let it throw.
    /// </summary>
    public bool IsResolved => !string.IsNullOrEmpty(_id);

    /// <summary>
    /// The customerId this instance serves.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The identity is unresolved. Deliberate: returning a placeholder would make every customer share
    /// whatever this value keys.
    /// </exception>
    public string Id => _id ?? throw new InvalidOperationException(_unresolvedMessage);
}
