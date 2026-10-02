// unified-access-control-r2 task 141 — the customer-workforce-tenant setting and the member test.
//
// Owner decision I1 = (b), 2026-09-30: a NEW explicit per-deployment setting listing the customer workforce
// tenant ids. EMPTY = DENY. It never defaults to AzureAd:TenantId and it is not TenantRouting:Tenants[].
//
// Why AzureAd:TenantId is the wrong answer (D-13): in Model 1 the per-customer BFF app registration lives in
// SPAARKE's tenant, so a Model-1 customer's employees sign in with the CUSTOMER's `tid`. A member test keyed
// on AzureAd:TenantId would refuse every Model-1 Type-2 employee AND auto-bind Spaarke's own staff into that
// customer's environment. In Model 2 the two happen to coincide — and the setting then lists it explicitly.
//
// Provisioning writes the value (customer-provisioning-orchestration-r1); the contract for that is in
// projects/unified-access-control-r2/notes/handoffs/INCOMING-141-workforce-tenant-list.md.

using System.Security.Claims;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Options bound from <c>WorkforceIdentity</c>: which workforce tenants' MEMBERS may be email-bound to, or
/// have created for them, a contact on first sign-in.
/// </summary>
public sealed class WorkforceIdentityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "WorkforceIdentity";

    /// <summary>
    /// The customer workforce tenant ids (GUID strings) this deployment serves. Empty or absent means NO
    /// caller is ever email-bound or gets a contact created — every Type-2 first sign-in is denied. App
    /// Service form: <c>WorkforceIdentity__CustomerTenantIds__0</c>, <c>__1</c>, …
    /// </summary>
    public List<string> CustomerTenantIds { get; set; } = new();

    /// <summary>The configured tenant ids, parsed. Malformed entries never parse (and fail startup validation).</summary>
    public IReadOnlySet<Guid> ParsedCustomerTenantIds()
        => CustomerTenantIds
            .Select(v => Guid.TryParse(v?.Trim(), out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToHashSet();
}

/// <summary>
/// Startup validation (ADR-010 <c>ValidateOnStart</c>). Empty is VALID — it means deny. What is refused is a
/// value that cannot mean what an operator intended: a non-GUID, the all-zero GUID, or the CIAM tenant (CIAM
/// callers are routed to the CIAM plane by <c>tid</c> and must never be treated as workforce members).
/// </summary>
public sealed class WorkforceIdentityOptionsValidator : IValidateOptions<WorkforceIdentityOptions>
{
    private readonly string? _ciamTenantId;

    public WorkforceIdentityOptionsValidator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _ciamTenantId = configuration["Ciam:TenantId"];
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WorkforceIdentityOptions options)
        => Validate(options, _ciamTenantId);

    /// <summary>The validation rule, pure.</summary>
    public static ValidateOptionsResult Validate(WorkforceIdentityOptions options, string? ciamTenantId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Guid.TryParse(ciamTenantId, out var ciam);

        for (var i = 0; i < options.CustomerTenantIds.Count; i++)
        {
            var raw = options.CustomerTenantIds[i];
            if (!Guid.TryParse(raw?.Trim(), out var tenant) || tenant == Guid.Empty)
            {
                failures.Add(
                    $"{WorkforceIdentityOptions.SectionName}:CustomerTenantIds:{i} = '{raw}' is not a tenant id (a non-zero GUID).");
            }
            else if (ciam != Guid.Empty && tenant == ciam)
            {
                failures.Add(
                    $"{WorkforceIdentityOptions.SectionName}:CustomerTenantIds:{i} is the CIAM tenant; external users are never workforce members.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The member test's answer for a workforce token.</summary>
public enum WorkforceMembership
{
    /// <summary>A user token from a configured customer tenant with <c>acct = 0</c>.</summary>
    Member,

    /// <summary>Not positively a user-delegated token (app-only, or a shape we do not model).</summary>
    NotUserToken,

    /// <summary>The customer-tenant setting is empty: nobody is a member.</summary>
    TenantListEmpty,

    /// <summary>The token's <c>tid</c> is not a configured customer tenant (incl. the registration's own, in Model 1).</summary>
    ForeignTenant,

    /// <summary>The token carries no <c>acct</c> claim — fail closed, never inferred.</summary>
    AcctMissing,

    /// <summary><c>acct = 1</c>: a B2B guest of the tenant.</summary>
    Guest,

    /// <summary>An <c>acct</c> value other than 0 or 1.</summary>
    AcctUnrecognized,
}

/// <summary>The claims the workforce plane reads, gathered once.</summary>
public sealed record WorkforceCallerClaims(
    CallerKind Kind,
    string? TenantId,
    string? Acct,
    string? Email,
    string? GivenName,
    string? FamilyName,
    string? DisplayName)
{
    /// <summary>The <c>acct</c> optional claim (member = 0, guest = 1).</summary>
    public const string AcctClaim = "acct";

    /// <summary>
    /// Reads the claims from a validated workforce principal. The caller kind comes from
    /// <see cref="CallerIdentity.FromPrincipal"/> — the repo's one classifier — never from <c>idtyp</c> alone,
    /// which is optional and expected absent.
    /// </summary>
    public static WorkforceCallerClaims From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = CallerIdentity.FromPrincipal(user);

        return new WorkforceCallerClaims(
            identity.Kind,
            identity.TenantId,
            First(user, AcctClaim),
            TokenEmail(user),
            First(user, "given_name", ClaimTypes.GivenName),
            First(user, "family_name", ClaimTypes.Surname),
            First(user, "name"));
    }

    /// <summary>
    /// The token's email: <c>email</c>, then <c>preferred_username</c>, then <c>upn</c>. NOT verified by
    /// anything here — Microsoft documents these as mutable and unsuitable for authorization. It is trusted
    /// only behind the member test: for a MEMBER of a configured customer tenant, that tenant's admin controls
    /// the attribute. A non-member's email is never used to bind or create.
    /// </summary>
    public static string? TokenEmail(ClaimsPrincipal user)
        => First(user, "email", "preferred_username", "upn", ClaimTypes.Email);

    private static string? First(ClaimsPrincipal user, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

/// <summary>The workforce member test. Pure.</summary>
public static class WorkforceMembershipTest
{
    /// <summary>Deny code: not positively a user token (app-only included — app-only tokens are never bound).</summary>
    public const string DenyNotUserToken = "sdap.access.deny.workforce_app_only_token";

    /// <summary>Deny code: the customer-tenant setting is empty.</summary>
    public const string DenyTenantListEmpty = "sdap.access.deny.workforce_tenant_list_empty";

    /// <summary>Deny code: the caller's tenant is not a configured customer tenant.</summary>
    public const string DenyForeignTenant = "sdap.access.deny.workforce_tenant_not_customer";

    /// <summary>Deny code: no <c>acct</c> claim.</summary>
    public const string DenyAcctMissing = "sdap.access.deny.workforce_acct_claim_missing";

    /// <summary>Deny code: a guest (<c>acct = 1</c>).</summary>
    public const string DenyGuest = "sdap.access.deny.workforce_guest";

    /// <summary>Deny code: an <c>acct</c> value that is neither 0 nor 1.</summary>
    public const string DenyAcctUnrecognized = "sdap.access.deny.workforce_acct_unrecognized";

    /// <summary>
    /// Is this caller a MEMBER of a configured customer workforce tenant? Order is deliberate: the caller kind
    /// first (an app-only token is never bound, whatever else it carries), then the setting (empty denies
    /// everyone), then the tenant, then <c>acct</c>. Membership is never inferred from an email domain or a
    /// <c>#EXT#</c> UPN.
    /// </summary>
    public static WorkforceMembership Evaluate(
        CallerKind kind, string? tenantId, string? acct, IReadOnlySet<Guid> customerTenantIds)
    {
        ArgumentNullException.ThrowIfNull(customerTenantIds);

        if (kind != CallerKind.UserDelegated)
        {
            return WorkforceMembership.NotUserToken;
        }

        if (customerTenantIds.Count == 0)
        {
            return WorkforceMembership.TenantListEmpty;
        }

        if (!Guid.TryParse(tenantId?.Trim(), out var tid) || !customerTenantIds.Contains(tid))
        {
            return WorkforceMembership.ForeignTenant;
        }

        if (string.IsNullOrWhiteSpace(acct))
        {
            return WorkforceMembership.AcctMissing;
        }

        return acct.Trim() switch
        {
            "0" => WorkforceMembership.Member,
            "1" => WorkforceMembership.Guest,
            _ => WorkforceMembership.AcctUnrecognized,
        };
    }

    /// <summary>The deny code for a non-member, or null for a member.</summary>
    public static string? DenyCodeFor(WorkforceMembership membership) => membership switch
    {
        WorkforceMembership.Member => null,
        WorkforceMembership.NotUserToken => DenyNotUserToken,
        WorkforceMembership.TenantListEmpty => DenyTenantListEmpty,
        WorkforceMembership.ForeignTenant => DenyForeignTenant,
        WorkforceMembership.AcctMissing => DenyAcctMissing,
        WorkforceMembership.Guest => DenyGuest,
        WorkforceMembership.AcctUnrecognized => DenyAcctUnrecognized,
        _ => throw new ArgumentOutOfRangeException(nameof(membership), membership, "Every outcome needs its own code."),
    };
}
