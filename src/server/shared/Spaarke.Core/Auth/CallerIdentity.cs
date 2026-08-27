using System.Security.Claims;

namespace Spaarke.Core.Auth;

/// <summary>
/// What kind of principal a bearer token represents. Deliberately THREE-valued: the third
/// value exists so that "we could not tell" is a first-class answer rather than something a
/// two-valued predicate has to fold into one of the other two — and folding it is how a
/// malformed token ends up classified as a service principal.
/// </summary>
public enum CallerKind
{
    /// <summary>
    /// No positive determination could be made. Authorization sites MUST treat this as DENY
    /// (ADR-003 fail-closed). It is NOT a synonym for "application" or for "user".
    /// </summary>
    Indeterminate = 0,

    /// <summary>A user is present behind the token (delegated / on-behalf-of eligible).</summary>
    UserDelegated = 1,

    /// <summary>
    /// An app-only (client-credentials / managed-identity) token. No human is behind it.
    /// <see cref="CallerIdentity.ApplicationId"/> names the calling application.
    /// </summary>
    Application = 2,
}

/// <summary>
/// The single place in this codebase that decides what kind of caller a <see cref="ClaimsPrincipal"/>
/// is. A pure function over claims — no ambient state, no <c>HttpContext</c>, no I/O.
///
/// <para><b>Why this lives in <c>Spaarke.Core.Auth</c> and not in the BFF.</b>
/// <c>Spaarke.Core</c> cannot reference BFF <c>Infrastructure/**</c> (one-way layering, enforced by
/// <c>tests/Spaarke.ArchTests/LayerDependencyTests.cs</c>). The unified access-control evaluator lives
/// here, beside <c>AuthorizationService</c>, and it cannot decide whether ADR-034 membership derivation
/// applies without knowing whether the caller is a service principal or a person. A BFF-side primitive
/// would be unreachable from here and would simply be rebuilt later. Only BCL types are used
/// (<see cref="ClaimsPrincipal"/> is <c>System.Security.Claims</c>), so this adds no package reference
/// and no AspNetCore <c>FrameworkReference</c>.</para>
///
/// <para><b>⚠ THE TRAP this type exists to close.</b> <c>appid</c>/<c>azp</c> is present in
/// USER-DELEGATED tokens too — it names the client application that requested the token, never the
/// caller's kind. So an allow-list keyed on <c>appid</c> alone (<c>if (allowed.Contains(appId))</c>)
/// is satisfiable by a human who signed into that app registration. Any authorization gate built on
/// this type MUST be a CONJUNCTION: <see cref="IsApplication"/> AND <c>appid ∈ allow-list</c>. The
/// classification is the half that no interactive user can forge.</para>
///
/// <para><b>Why "positive" determination is load-bearing.</b> App-only is never inferred from the
/// ABSENCE of a user claim. Absence means a token we did not model — malformed, a future issuer, a
/// new flow — and inferring "service principal" from it would hand every such token the operator
/// capability. Absence yields <see cref="CallerKind.Indeterminate"/>, which authorization sites deny.</para>
///
/// <para><b>Claim-name duality.</b> Every claim is read in BOTH its short JWT form and its mapped
/// WS-Federation URI form. Microsoft.Identity.Web v3+ defaults <c>MapInboundClaims=false</c> (short
/// names survive), but the mapping is process-global state
/// (<c>Microsoft.IdentityModel.Tokens.DefaultInboundClaimTypeMap</c>) that other components can flip,
/// so reading only one form is a latent, environment-dependent bug. This mirrors the convention already
/// used across the BFF (e.g. <c>ChatDocumentEndpoints</c>, <c>AuditEnrichmentMiddleware</c>).</para>
/// </summary>
public sealed class CallerIdentity
{
    // Short (raw JWT) claim names.
    private const string ClaimAppId = "appid";
    private const string ClaimAzp = "azp";
    private const string ClaimObjectId = "oid";
    private const string ClaimSubject = "sub";
    private const string ClaimScope = "scp";
    private const string ClaimIdentityType = "idtyp";
    private const string ClaimTenantId = "tid";

    // Mapped (WS-Fed URI) forms produced when inbound claim mapping is enabled.
    private const string ClaimAppIdUri = "http://schemas.microsoft.com/identity/claims/appid";
    private const string ClaimObjectIdUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ClaimScopeUri = "http://schemas.microsoft.com/identity/claims/scope";
    private const string ClaimTenantIdUri = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ClaimTenantIdAlt = "tenant_id";

    /// <summary><c>idtyp</c> value Entra emits for an app-only token.</summary>
    private const string IdentityTypeApp = "app";

    /// <summary><c>idtyp</c> value Entra emits for a user token.</summary>
    private const string IdentityTypeUser = "user";

    private CallerIdentity(
        CallerKind kind,
        string? applicationId,
        string? objectId,
        string? tenantId,
        string determinationReason)
    {
        Kind = kind;
        ApplicationId = applicationId;
        ObjectId = objectId;
        TenantId = tenantId;
        DeterminationReason = determinationReason;
    }

    /// <summary>The classification. <see cref="CallerKind.Indeterminate"/> means DENY at authorization sites.</summary>
    public CallerKind Kind { get; }

    /// <summary>
    /// The calling application registration id (<c>appid</c> v1 / <c>azp</c> v2), when the token carries one.
    /// Populated for EVERY kind — a user-delegated token names its client app too. On its own this proves
    /// nothing about caller kind; see the TRAP note on the type.
    /// </summary>
    public string? ApplicationId { get; }

    /// <summary>
    /// The <c>oid</c> claim. For <see cref="CallerKind.UserDelegated"/> this is the user's directory object id;
    /// for <see cref="CallerKind.Application"/> it is the calling service principal's object id.
    /// </summary>
    public string? ObjectId { get; }

    /// <summary>The issuing Entra tenant (<c>tid</c>), when present.</summary>
    public string? TenantId { get; }

    /// <summary>
    /// Short, non-sensitive description of WHICH rule produced <see cref="Kind"/>. Intended for audit log
    /// lines on allow/deny decisions, so a reviewer can tell an <c>idtyp</c>-based determination from the
    /// structural <c>sub == oid</c> one without re-deriving it. Never contains claim VALUES.
    /// </summary>
    public string DeterminationReason { get; }

    /// <summary>True only for a positively-determined app-only caller.</summary>
    public bool IsApplication => Kind == CallerKind.Application;

    /// <summary>True only for a positively-determined user-delegated caller.</summary>
    public bool IsUserDelegated => Kind == CallerKind.UserDelegated;

    /// <summary>
    /// Classifies a principal. Never throws and never returns null; an unreadable or unmodelled token
    /// yields <see cref="CallerKind.Indeterminate"/>.
    ///
    /// <para><b>Precedence (order is deliberate and fail-closed):</b></para>
    /// <list type="number">
    ///   <item>Not authenticated → <c>Indeterminate</c>.</item>
    ///   <item>A delegated scope claim (<c>scp</c>) is present:
    ///     <list type="bullet">
    ///       <item>with an <c>oid</c> → <c>UserDelegated</c> (a user is positively present);</item>
    ///       <item>without an <c>oid</c> → <c>Indeterminate</c> (delegated scope but no user = a shape we
    ///             do not model; denying beats guessing).</item>
    ///     </list>
    ///     This branch runs BEFORE any application branch, so nothing carrying a delegated scope can be
    ///     classified <c>Application</c> — that is the structural guarantee behind the TRAP note.</item>
    ///   <item><c>idtyp == "user"</c> → <c>UserDelegated</c>.</item>
    ///   <item><c>idtyp == "app"</c> → <c>Application</c>. The most direct signal Entra offers, but it is an
    ///     OPTIONAL claim: nothing in this repo's Entra provisioning configures it, so it is expected to be
    ///     ABSENT in practice and must never be the only signal.</item>
    ///   <item><c>sub</c> and <c>oid</c> both present, non-blank and EQUAL → <c>Application</c>. In an Entra
    ///     app-only token both are the calling service principal's object id; in a user token <c>sub</c> is a
    ///     pairwise subject identifier scoped to (user, application) and never equals the user's <c>oid</c>.
    ///     Both claims are core (always emitted), so unlike <c>idtyp</c> this signal needs no tenant
    ///     configuration — it is the one that actually fires for the L2 managed-identity probe.</item>
    ///   <item>Anything else → <c>Indeterminate</c>.</item>
    /// </list>
    ///
    /// <para><b>Why <c>roles</c> is NOT used as a determinant.</b> A <c>roles</c> claim appears in
    /// user-delegated tokens as the signed-in user's app-role assignments, so "has roles" does not imply
    /// app-only. The usual "<c>roles</c> present AND <c>scp</c> absent" formulation smuggles in an absence
    /// test, which is exactly what this type refuses to do.</para>
    /// </summary>
    /// <param name="principal">The principal to classify. <c>null</c> is legal and yields <c>Indeterminate</c>.</param>
    public static CallerIdentity FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
        {
            return new CallerIdentity(
                CallerKind.Indeterminate, null, null, null, "principal is null or not authenticated");
        }

        var applicationId = FirstNonBlank(principal, ClaimAppId, ClaimAzp, ClaimAppIdUri);
        var objectId = FirstNonBlank(principal, ClaimObjectId, ClaimObjectIdUri);
        var tenantId = FirstNonBlank(principal, ClaimTenantId, ClaimTenantIdUri, ClaimTenantIdAlt);
        var identityType = FirstNonBlank(principal, ClaimIdentityType);

        // (2) POSITIVE user-side determination FIRST. Presence of a delegated scope claim is checked by
        //     presence (not value), matching the pre-existing BFF convention: an empty scp is still a
        //     delegated-shaped token, and treating it as such denies rather than escalates.
        var hasDelegatedScope =
            principal.HasClaim(c => c.Type == ClaimScope)
            || principal.HasClaim(c => c.Type == ClaimScopeUri);

        if (hasDelegatedScope)
        {
            return string.IsNullOrWhiteSpace(objectId)
                ? new CallerIdentity(
                    CallerKind.Indeterminate, applicationId, objectId, tenantId,
                    "delegated scope claim present without a user object id — unmodelled token shape")
                : new CallerIdentity(
                    CallerKind.UserDelegated, applicationId, objectId, tenantId,
                    "delegated scope claim present with a user object id");
        }

        // (3) Explicit user signal without a scope claim (e.g. an id_token-shaped principal).
        if (string.Equals(identityType, IdentityTypeUser, StringComparison.OrdinalIgnoreCase))
        {
            return new CallerIdentity(
                CallerKind.UserDelegated, applicationId, objectId, tenantId, "idtyp=user");
        }

        // (4) Strongest app-only signal — optional claim, expected absent in this deployment.
        if (string.Equals(identityType, IdentityTypeApp, StringComparison.OrdinalIgnoreCase))
        {
            return new CallerIdentity(
                CallerKind.Application, applicationId, objectId, tenantId, "idtyp=app");
        }

        // (5) Structural app-only signal — always available because sub and oid are core claims.
        var subject = FirstNonBlank(principal, ClaimSubject, ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(subject)
            && !string.IsNullOrWhiteSpace(objectId)
            && string.Equals(subject, objectId, StringComparison.OrdinalIgnoreCase))
        {
            return new CallerIdentity(
                CallerKind.Application, applicationId, objectId, tenantId,
                "sub equals oid — app-only token shape");
        }

        // (6) Fail closed.
        return new CallerIdentity(
            CallerKind.Indeterminate, applicationId, objectId, tenantId,
            "no positive app-only or user-delegated determination");
    }

    /// <summary>
    /// First claim among <paramref name="claimTypes"/> whose value is non-blank, in the order given.
    /// Returns <c>null</c> when none match — "absent" and "present but blank" are deliberately collapsed,
    /// because every consumer here treats a blank claim as no claim.
    /// </summary>
    private static string? FirstNonBlank(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
