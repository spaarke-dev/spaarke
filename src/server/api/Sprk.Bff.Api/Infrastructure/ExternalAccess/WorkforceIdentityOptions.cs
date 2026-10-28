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
// The ONE use of AzureAd:TenantId on this plane (R3 FR-15, #1563): a customer-tenant guest — a B2B guest (acct = 1)
// of the stamp's own tenant whose home tenant (idp) is on this list — answers Member, which additionally requires
// tid = AzureAd:TenantId; the member test itself still never defaults the list from it. Since #2128 (owner
// 2026-10-20) that Member is NOT admitted: ContactIdentityBinder refuses it workforce_guest_use_home_signin and
// creates, binds and resolves nothing (ADR-028 A9: customer staff sign in at their home directory).
//
// Provisioning writes the value (customer-provisioning-orchestration-r1); the contract for that is in
// projects/unified-access-control-r2/notes/handoffs/INCOMING-141-workforce-tenant-list.md.

using System.Security.Claims;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Options bound from <c>WorkforceIdentity</c>: which workforce tenants' MEMBERS pass the member test that gates the
/// contact-only branch of the Workforce token plane (a caller with no enabled systemuser) on EVERY sign-in — the first
/// one email-binds or creates their contact, later ones resolve through that binding — and which home directories the
/// WorkforceHome scheme (ADR-028 A9) accepts. An enabled systemuser is resolved before the member test runs.
/// </summary>
public sealed class WorkforceIdentityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "WorkforceIdentity";

    /// <summary>
    /// The customer workforce tenant ids (GUID strings) this deployment serves. Empty or absent denies every
    /// contact-only sign-in on the Workforce token plane, the first and every later one (#2128 round 1: an existing
    /// binding does not outlive the member test), and every WorkforceHome token. It does not affect a caller resolved
    /// to an enabled systemuser, which runs before the member test. App Service form:
    /// <c>WorkforceIdentity__CustomerTenantIds__0</c>, <c>__1</c>, …
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

    public WorkforceIdentityOptionsValidator(
        IConfiguration configuration, ILogger<WorkforceIdentityOptionsValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        _ciamTenantId = configuration["Ciam:TenantId"];

        // R3 FR-15: the guest branch applies only when tid = AzureAd:TenantId, so a deployment whose stamp tenant is
        // missing or not a non-zero GUID gives every guest its plain member-test deny instead of the #2128 "use home
        // sign-in" refusal. Both refuse; the warning tells an operator which one a guest will see. Warn once (this
        // validator is a singleton, built at startup); never fail startup on it — most deployments have no guests.
        if (!IsUsableStampTenant(configuration[WorkforcePrincipalResolver.StampTenantConfigKey]))
        {
            logger.LogWarning(
                "[WF-AUTH] {Key} is not a non-zero GUID — a customer-tenant B2B guest gets its plain member-test deny, " +
                "not workforce_guest_use_home_signin (FR-15 guest admission is retired: an acct=1 workforce caller with " +
                "no enabled systemuser is denied either way)",
                WorkforcePrincipalResolver.StampTenantConfigKey);
        }
    }

    /// <summary>True when the stamp tenant is a non-zero GUID — the precondition of the FR-15 conjunction (refused since #2128).</summary>
    internal static bool IsUsableStampTenant(string? stampTenantId)
        => Guid.TryParse(stampTenantId?.Trim(), out var stamp) && stamp != Guid.Empty;

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
    /// <summary>
    /// A user token from a configured customer tenant with <c>acct = 0</c> — or (R3 FR-15) a customer-tenant guest:
    /// <c>acct = 1</c> in the stamp's own tenant whose home tenant (<c>idp</c>) is a configured customer tenant. Since
    /// #2128 the guest form is refused by the binder (<see cref="WorkforceMembershipTest.DenyGuestUseHomeSignin"/>).
    /// </summary>
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
    string? DisplayName,
    string? Idp)
{
    /// <summary>The <c>acct</c> optional claim (member = 0, guest = 1).</summary>
    public const string AcctClaim = "acct";

    /// <summary>
    /// The <c>idp</c> claim: the identity provider that authenticated the subject. For a B2B guest it is the
    /// guest's HOME tenant issuer (<c>https://sts.windows.net/{home tid}/</c>), not the tenant signed in to.
    /// </summary>
    public const string IdpClaim = "idp";

    /// <summary>The WS-Federation form <c>idp</c> takes when inbound claim mapping is on.</summary>
    public const string IdpClaimUri = "http://schemas.microsoft.com/identity/claims/identityprovider";

    /// <summary>
    /// The <c>xms_edov</c> optional claim: the email's domain is verified by the tenant that owns it. Only a boolean
    /// <c>true</c> counts (<see cref="EmailDomainOwnerVerifiedOf"/>).
    /// </summary>
    public const string EmailDomainOwnerVerifiedClaim = "xms_edov";

    /// <summary>
    /// The token's <c>xms_edov</c> (<see cref="EmailDomainOwnerVerifiedOf"/>): true only for a single boolean <c>true</c>
    /// claim. Null or false is "not verified" — an email bind on the Workforce planes then needs the invited tenant.
    /// </summary>
    public bool? EmailDomainOwnerVerified { get; init; }

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
            First(user, "name"),
            Single(user, IdpClaim, IdpClaimUri))
        {
            EmailDomainOwnerVerified = EmailDomainOwnerVerifiedOf(user),
        };
    }

    /// <summary>
    /// The <c>xms_edov</c> claim as the email bind reads it: <c>true</c> ONLY when the principal carries exactly one
    /// <c>xms_edov</c> claim whose value type is <see cref="ClaimValueTypes.Boolean"/> and whose value is true (a JSON
    /// boolean in the token). A string <c>"true"</c>, any other value, a repeated claim or no claim is <c>null</c> (not
    /// verified, fail closed). Pure.
    /// </summary>
    public static bool? EmailDomainOwnerVerifiedOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = user.FindAll(EmailDomainOwnerVerifiedClaim).ToList();
        return claims.Count == 1
               && claims[0].ValueType == ClaimValueTypes.Boolean
               && bool.TryParse(claims[0].Value?.Trim(), out var verified)
               && verified
            ? true
            : null;
    }

    /// <summary>
    /// The one distinct non-blank value carried under <paramref name="types"/>, or null when there is none or
    /// more than one — an ambiguous identity-provider claim is never resolved by picking one (fail closed).
    /// </summary>
    private static string? Single(ClaimsPrincipal user, params string[] types)
    {
        var values = types
            .SelectMany(user.FindAll)
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>The marker Entra puts in a B2B guest's UPN (<c>name_domain#EXT#@tenant.onmicrosoft.com</c>).</summary>
    private const string ExternalUpnMarker = "#EXT#";

    /// <summary>
    /// The token's email: <c>email</c>, then <c>preferred_username</c>, then <c>upn</c> (raw or, with inbound
    /// claim mapping on, as <see cref="ClaimTypes.Email"/> / <see cref="ClaimTypes.Upn"/>). NOT verified by
    /// anything here — Microsoft documents these as mutable and unsuitable for authorization. It is trusted
    /// only behind the member test: for a MEMBER of a configured customer tenant, that tenant's admin controls
    /// the attribute. A non-member's email is never used to bind or create.
    /// </summary>
    /// <remarks>
    /// For a guest (<c>acct = 1</c>) a value containing <c>#EXT#</c> is NOT an email: it is the guest object's UPN in
    /// the tenant signed in to, an address that is neither deliverable nor the person's. It is skipped, so a guest
    /// with no real <c>email</c> claim carries no email (never bound to or named after the UPN; since #2128 no contact
    /// is created for a customer-tenant guest at all). <c>acct = 0</c> tokens are unchanged.
    /// </remarks>
    public static string? TokenEmail(ClaimsPrincipal user)
    {
        var isGuest = First(user, AcctClaim) == "1";
        return First(
            user,
            v => !isGuest || !v.Contains(ExternalUpnMarker, StringComparison.OrdinalIgnoreCase),
            "email", "preferred_username", "upn", ClaimTypes.Email, ClaimTypes.Upn);
    }

    private static string? First(ClaimsPrincipal user, params string[] types)
        => First(user, _ => true, types);

    private static string? First(ClaimsPrincipal user, Func<string, bool> accept, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value) && accept(value.Trim()))
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
    /// Deny code (#2128): a customer-tenant guest — the FR-15 conjunction, which <see cref="Evaluate"/> still answers
    /// <see cref="WorkforceMembership.Member"/> — is refused by <c>ContactIdentityBinder</c> and pointed at home
    /// sign-in (ADR-028 A9). Not a member-test outcome, so <see cref="DenyCodeFor"/> never returns it.
    /// </summary>
    public const string DenyGuestUseHomeSignin = "sdap.access.deny.workforce_guest_use_home_signin";

    /// <summary>
    /// Is this caller a MEMBER of a configured customer workforce tenant? Order is deliberate: the caller kind
    /// first (an app-only token is never bound, whatever else it carries), then the setting (empty denies
    /// everyone), then the tenant, then <c>acct</c>. Membership is never inferred from an email domain or a
    /// <c>#EXT#</c> UPN.
    /// </summary>
    /// <remarks>
    /// R3 FR-15 (#1563) adds ONE branch, the customer-tenant guest: an exact conjunction of a user
    /// token, <c>acct = 1</c>, <c>tid</c> = <paramref name="stampTenantId"/> (the stamp's own tenant,
    /// <c>AzureAd:TenantId</c>) and the home tenant parsed from <paramref name="idp"/> ∈
    /// <paramref name="customerTenantIds"/>. It yields <see cref="WorkforceMembership.Member"/>; when any part
    /// fails the result is exactly what it was before the branch existed (and so is its deny code). With no
    /// stamp tenant or no <c>idp</c> supplied the branch never applies. Since #2128 the binder refuses that Member
    /// (<see cref="DenyGuestUseHomeSignin"/>): a customer-tenant guest with no enabled systemuser (the contact-only
    /// branch) is never admitted on this plane. An enabled systemuser — a licensed B2B guest included — is resolved by
    /// <c>WorkforcePrincipalResolver</c>'s systemuser branch before the binder, and this test, run.
    /// Both parameters are REQUIRED (no defaults) so that a caller cannot silently skip FR-15 by omission; pass
    /// <c>null</c> to say "no stamp tenant / no idp" explicitly.
    /// </remarks>
    public static WorkforceMembership Evaluate(
        CallerKind kind,
        string? tenantId,
        string? acct,
        IReadOnlySet<Guid> customerTenantIds,
        string? stampTenantId,
        string? idp)
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

        if (!Guid.TryParse(tenantId?.Trim(), out var tid))
        {
            return WorkforceMembership.ForeignTenant;
        }

        // Model 1 stamp: the guest's tid is the stamp tenant, which the customer list never holds — so without the
        // branch it is ForeignTenant here. (Dev, which lists its own tenant — NFR-07 — reaches the acct switch.)
        var registeredGuest = IsRegisteredGuest(tid, acct, stampTenantId, idp, customerTenantIds);

        if (!customerTenantIds.Contains(tid))
        {
            return registeredGuest ? WorkforceMembership.Member : WorkforceMembership.ForeignTenant;
        }

        if (string.IsNullOrWhiteSpace(acct))
        {
            return WorkforceMembership.AcctMissing;
        }

        return acct.Trim() switch
        {
            "0" => WorkforceMembership.Member,
            "1" => registeredGuest ? WorkforceMembership.Member : WorkforceMembership.Guest,
            _ => WorkforceMembership.AcctUnrecognized,
        };
    }

    /// <summary>
    /// The FR-15 conjunction. Every part must hold: <c>acct</c> is exactly <c>1</c>; the stamp tenant is a
    /// non-zero GUID and equals the token's <c>tid</c>; and <c>idp</c> names exactly one home tenant that is a
    /// configured customer tenant.
    /// </summary>
    private static bool IsRegisteredGuest(
        Guid tid, string? acct, string? stampTenantId, string? idp, IReadOnlySet<Guid> customerTenantIds)
        => acct?.Trim() == "1"
            && Guid.TryParse(stampTenantId?.Trim(), out var stamp) && stamp != Guid.Empty
            && tid == stamp
            && TryParseHomeTenant(idp, out var home)
            && customerTenantIds.Contains(home);

    private const string V1IssuerHost = "sts.windows.net";
    private const string V2IssuerHost = "login.microsoftonline.com";

    /// <summary>
    /// The home tenant named by an <c>idp</c> claim. Accepted shapes, and only these: a bare tenant GUID; the v1
    /// Entra issuer <c>https://sts.windows.net/{tid}/</c>; the v2 Entra issuer
    /// <c>https://login.microsoftonline.com/{tid}/v2.0</c>. Anything else — another host (a social or federated
    /// IdP, <c>live.com</c>, <c>mail</c>), another scheme, a port, a query, user info, extra path segments, a
    /// non-<c>D</c>-format or all-zero GUID — is refused (fail closed).
    /// </summary>
    internal static bool TryParseHomeTenant(string? idp, out Guid homeTenant)
    {
        homeTenant = Guid.Empty;
        var value = idp?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (Guid.TryParseExact(value, "D", out var bare))
        {
            homeTenant = bare;
            return bare != Guid.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        // "/{tid}/" or "/{tid}/v2.0": one leading slash, at most one trailing slash, no empty segment.
        var path = uri.AbsolutePath;
        if (path.EndsWith('/'))
        {
            path = path[..^1];
        }

        var segments = path.Length > 1 ? path[1..].Split('/') : Array.Empty<string>();
        if (segments.Length == 0 || segments.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var host = uri.Host;

        var shapeMatches =
            (string.Equals(host, V1IssuerHost, StringComparison.OrdinalIgnoreCase) && segments.Length == 1)
            || (string.Equals(host, V2IssuerHost, StringComparison.OrdinalIgnoreCase) && segments.Length == 2
                && segments[1] == "v2.0");

        if (!shapeMatches || !Guid.TryParseExact(segments[0], "D", out var tenant) || tenant == Guid.Empty)
        {
            return false;
        }

        homeTenant = tenant;
        return true;
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
