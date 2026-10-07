using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// How the running BFF learned which customer it serves. Recorded so that drift into the derived
/// path is VISIBLE rather than assumed — see <see cref="CustomerIdResolver"/> remarks.
/// </summary>
public enum CustomerIdSource
{
    /// <summary>No customer identity could be established. There is deliberately no default.</summary>
    Unresolved = 0,

    /// <summary>Read from the explicit <c>Customer:Id</c> setting (<c>Customer__Id</c> env var).</summary>
    Explicit = 1,

    /// <summary>Derived from the App Service <c>WEBSITE_RESOURCE_GROUP</c> name.</summary>
    DerivedFromResourceGroup = 2
}

/// <summary>
/// Resolves the customer this BFF instance serves (D-14, unified-access-control-r2 task 123).
///
/// <para>
/// WHY THIS EXISTS. <c>customerId</c> names every Azure resource a customer owns
/// (<c>rg-spaarke-{customerId}-{env}</c> and everything inside it) and, before this type, was readable
/// by no line of BFF code. D-12 established that <c>tenantId</c> is IDENTICAL for every Model 1
/// customer — they all live in the Spaarke Entra tenant — so every tenant-keyed control separates
/// Entra tenants rather than customers. This is the missing runtime handle on the boundary that the
/// infrastructure already draws.
/// </para>
///
/// <para>
/// 🔴 WHAT THIS IS NOT. The customer boundary is the DEDICATED PER-CUSTOMER RESOURCE, not this value.
/// Per D-12 §3 and the ADR-009 amendment, Redis access control is per-INSTANCE, not per-keyspace: a
/// customer-id key prefix is a convention our code enforces, not one Redis enforces. This is defence in
/// depth and an observability handle (cache-key prefixes, log scopes, metric dimensions) — never a
/// substitute for the dedicated instance.
/// </para>
///
/// <para>
/// NO DEFAULT, EVER. Both paths below read the SAME customerId from different places; neither invents
/// one. When neither resolves the result is <see cref="CustomerIdSource.Unresolved"/>, which fails
/// startup in deployed environments (<see cref="CustomerOptionsValidator"/>) and throws at the point of
/// use everywhere else (<see cref="CustomerIdentity.Id"/>). A shared placeholder is precisely the
/// failure this exists to remove — it is the same shape as the compile-time constants task 122 found
/// sitting in the Foundry agent-thread cache key.
/// </para>
/// </summary>
public static class CustomerIdResolver
{
    /// <summary>
    /// Configuration key holding the explicit value. As an App Service application setting (and in the
    /// Bicep that emits it) this is spelled <c>Customer__Id</c>.
    /// </summary>
    public const string ExplicitSettingKey = "Customer:Id";

    /// <summary>
    /// The same setting in App Service / environment-variable spelling — what the Bicep emits and what
    /// an operator types into <c>az webapp config appsettings set</c>. Held as its own constant rather
    /// than derived from <see cref="ExplicitSettingKey"/> so the operator-facing string is literal.
    /// </summary>
    public const string ExplicitSettingEnvironmentName = "Customer__Id";

    /// <summary>
    /// Environment variable App Service sets automatically on every instance. It is literally
    /// <c>rg-spaarke-{customerId}-{env}</c> for a per-customer stamp, so the customerId is ALREADY
    /// present on every such stamp with no deployment change.
    /// </summary>
    public const string ResourceGroupEnvironmentVariable = "WEBSITE_RESOURCE_GROUP";

    /// <summary>
    /// The canonical customerId standard: 3–8 characters, lowercase letters and digits, first character
    /// a letter. Derived in
    /// <c>docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "The customerId standard"</c> and
    /// enforced as a LENGTH by <c>@minLength(3) @maxLength(8)</c> in both customer stacks.
    ///
    /// <para>
    /// ⚠️ The CHARACTER half of the rule cannot be enforced in Bicep — ARM has no <c>@pattern</c>
    /// decorator and this repo has no <c>bicepconfig.json</c> enabling experimental assertions. It is
    /// enforced where the value is ASSIGNED (provisioning intake, cpo-r1) and asserted HERE on the
    /// derived path, so a resource group that does not carry a legal id fails loudly rather than
    /// yielding an illegal one.
    /// </para>
    /// </summary>
    public const string CustomerIdPattern = "^[a-z][a-z0-9]{2,7}$";

    private static readonly Regex CustomerId = new(CustomerIdPattern, RegexOptions.Compiled);

    /// <summary>
    /// A per-customer resource group is <c>rg-spaarke-{customerId}-{env}</c> — exactly two
    /// dash-separated segments after the prefix. Anchored on purpose:
    /// <c>rg-spaarke-dev</c> (one segment) and the retired
    /// <c>rg-spaarke-{customerId}-{env}-model1</c> (three) must NOT match.
    /// </summary>
    private static readonly Regex PerCustomerResourceGroup = new(
        @"^rg-spaarke-(?<customer>[^-]+)-(?<env>[^-]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 🔴 Segments that occupy the customerId POSITION in a resource-group name but are NOT customers.
    /// Without this list the platform stamp derives <c>customerId = "platform"</c> — a silently invented
    /// customer, which is the exact failure class this whole mechanism exists to remove. Verified live
    /// names: <c>rg-spaarke-platform-{env}</c> (hosts the BFF and the L2 control plane —
    /// <c>platform.bicep:123</c>, <c>platform-controlplane.bicep:209</c>, and the worked example in
    /// <c>scripts/Deploy-BffApi.ps1</c>), <c>rg-spaarke-shared-{env}</c> (the retired Model 1 shared
    /// tier — <c>model1-shared.bicep:84</c>), and <c>rg-spaarke-byok-prod</c>
    /// (<c>infrastructure/byok/main.bicep:19</c>).
    ///
    /// <para>
    /// A stamp in one of these groups is not a per-customer stamp, so it must set
    /// <see cref="ExplicitSettingKey"/> explicitly. Deriving would be worse than failing.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> NonCustomerResourceGroupSegments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "platform", "shared", "byok" };

    /// <summary>
    /// Resolves the customer identity from the two permitted sources, in precedence order:
    /// the explicit setting, then derivation from the resource-group name.
    /// </summary>
    /// <param name="explicitValue">Value of <see cref="ExplicitSettingKey"/>, or null/blank if unset.</param>
    /// <param name="resourceGroupName">
    /// Value of <see cref="ResourceGroupEnvironmentVariable"/>, or null/blank when not running on App Service.
    /// </param>
    /// <returns>
    /// The resolved id and the source it came from. <see cref="CustomerIdSource.Unresolved"/> with a
    /// null id when neither path yields a legal value — callers decide how loudly to fail.
    /// </returns>
    public static (string? Id, CustomerIdSource Source) Resolve(
        string? explicitValue,
        string? resourceGroupName)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            var trimmed = explicitValue.Trim();

            // An explicit value that violates the standard is a misconfiguration, not a customer. It
            // would compose resource names that do not exist (or, worse, that collide — the
            // storage-account name strips hyphens, so 'acme-x' and 'acmex' resolve to the same account).
            // Report it as unresolved so the validator's message names the setting.
            return CustomerId.IsMatch(trimmed)
                ? (trimmed, CustomerIdSource.Explicit)
                : (null, CustomerIdSource.Unresolved);
        }

        if (string.IsNullOrWhiteSpace(resourceGroupName))
        {
            return (null, CustomerIdSource.Unresolved);
        }

        var match = PerCustomerResourceGroup.Match(resourceGroupName.Trim());
        if (!match.Success)
        {
            return (null, CustomerIdSource.Unresolved);
        }

        // Lower-cased before validation, and ONLY on this path. Azure resource-group names are
        // case-insensitive and preserve whatever case they were created with, so an operator-created
        // 'RG-Spaarke-Acme-Prod' names the same group the Bicep would have created as
        // 'rg-spaarke-acme-prod'. Rejecting it would be a spurious startup failure over letter case.
        //
        // The EXPLICIT setting above is deliberately NOT normalised: it is the authoritative value,
        // copied from sprk_dataverseenvironment.sprk_customerid, and it should be exactly canonical.
        // Accepting a non-canonical spelling there would hide a registry that has drifted.
        var candidate = match.Groups["customer"].Value.ToLowerInvariant();

        if (NonCustomerResourceGroupSegments.Contains(candidate))
        {
            return (null, CustomerIdSource.Unresolved);
        }

        // Assert the convention rather than trusting it (D-14 §8): a resource group whose customer
        // segment is not a legal customerId means the naming convention this derivation depends on no
        // longer holds. Parse-and-hope would hand back an id that names no real resource.
        return CustomerId.IsMatch(candidate)
            ? (candidate, CustomerIdSource.DerivedFromResourceGroup)
            : (null, CustomerIdSource.Unresolved);
    }

    /// <summary>
    /// The operator-facing explanation used both by the startup failure and by the throw at point of
    /// use, so the two cannot drift into saying different things.
    /// </summary>
    public static string BuildUnresolvedMessage(string? explicitValue, string? resourceGroupName)
    {
        var explicitState = string.IsNullOrWhiteSpace(explicitValue)
            ? "is not set"
            : $"is set to '{explicitValue.Trim()}', which does not match the customerId standard {CustomerIdPattern}";

        var resourceGroupState = string.IsNullOrWhiteSpace(resourceGroupName)
            ? $"{ResourceGroupEnvironmentVariable} is not set (not running on App Service?)"
            : $"{ResourceGroupEnvironmentVariable} is '{resourceGroupName.Trim()}', which is not a "
              + "per-customer resource group of the form rg-spaarke-{customerId}-{env}";

        return $"This BFF cannot determine which customer it is serving. {ExplicitSettingKey} "
            + $"(app setting '{ExplicitSettingEnvironmentName}') {explicitState}, and {resourceGroupState}. "
            + "Set the app setting explicitly on this stamp. There is deliberately no default: an "
            + "absent customer identity must never resolve to a shared value (D-14). See "
            + "docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md § 'Customer identity (Customer__Id)'.";
    }
}
