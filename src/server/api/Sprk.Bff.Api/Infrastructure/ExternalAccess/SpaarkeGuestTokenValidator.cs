// spaarke-SPA-external-access-platform-r3 task 048 (2026-10-16) — ADR-028 A9 link proof, the guest half.
//
// POST /api/v1/external/workforce/link carries, in its BODY, the same person's Spaarke-tenant GUEST access token for the
// stamp BFF app (design notes/a9-bff-design.md §5). This class validates that token exactly as the DEFAULT (workforce)
// JwtBearer scheme would — the same TokenValidationParameters and the same signing metadata, READ from that scheme's
// options and never changed (task 048 escalation trigger) — and then applies the A9 link conditions to the validated
// token:
//   - aud is the stamp BFF app itself (bare id or api://{id}); the Copilot audience the default scheme also accepts is
//     refused here;
//   - a user token: idtyp, if present, is "user", and scp contains user_impersonation;
//   - tid = AzureAd:TenantId (the Spaarke tenant; unset → nothing is accepted);
//   - acct = 1 (a guest of the Spaarke tenant);
//   - the idp's home tenant (WorkforceMembershipTest.TryParseHomeTenant) = the home bearer's tid;
//   - a usable oid (the guest's Spaarke-tenant object id — the caller resolves it to an ENABLED systemuser);
//   - defense in depth: the guest's home UPN (from the #EXT# UPN, else email, else preferred_username) equals the home
//     bearer's preferred_username, case-insensitively. It can only refuse.
// Freshness is the token's own exp with the scheme's own clock skew (owner §11.3): no shorter or longer window.
//
// The token is never logged, stored or echoed. Failures log a fixed reason only. No OBO, no token acquisition.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Infrastructure.DI;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>Why a guest token was refused. Each value is also the route's public reason code.</summary>
public static class GuestTokenDeny
{
    /// <summary>The token is past its own expiry (with the default scheme's clock skew).</summary>
    public const string Expired = "sdap.access.link.guest_token_expired";

    /// <summary>
    /// The token failed validation (signature, issuer, audience, not-yet-valid, malformed), is not for the stamp BFF app,
    /// is not a user token, or carries no usable oid.
    /// </summary>
    public const string Invalid = "sdap.access.link.guest_token_invalid";

    /// <summary>The token's tid is not the Spaarke tenant (or the Spaarke tenant is not configured).</summary>
    public const string TenantNotSpaarke = "sdap.access.link.guest_tenant_not_spaarke";

    /// <summary>The token's acct is missing or is not 1: it is not a guest account.</summary>
    public const string NotGuest = "sdap.access.link.guest_account_required";

    /// <summary>The guest's home tenant (from idp) is not the home bearer's tid.</summary>
    public const string HomeTenantMismatch = "sdap.access.link.home_tenant_mismatch";

    /// <summary>
    /// The token could not be validated right now (the validation itself threw, e.g. the signing metadata could not be
    /// fetched). Retryable: the route answers 503, not a refusal.
    /// </summary>
    public const string Unavailable = "sdap.access.link.unavailable";

    /// <summary>The guest's home UPN does not equal the home bearer's preferred_username (or either is absent).</summary>
    public const string UpnMismatch = "sdap.access.link.upn_mismatch";
}

/// <summary>A validated guest token's answer: the guest's Spaarke-tenant object id, or why it was refused.</summary>
public sealed record GuestTokenValidation(Guid? GuestObjectId, string? DenyReason)
{
    internal static GuestTokenValidation Deny(string reason) => new(null, reason);
}

/// <summary>Validates the link route's body token with the default scheme's parameters. See the file header.</summary>
public sealed class SpaarkeGuestTokenValidator
{
    /// <summary>The marker Entra puts in a B2B guest's UPN (<c>name_domain#EXT#@tenant.onmicrosoft.com</c>).</summary>
    private const string ExternalUpnMarker = "#EXT#";

    private readonly IOptionsMonitor<JwtBearerOptions> _jwtBearer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SpaarkeGuestTokenValidator> _logger;

    public SpaarkeGuestTokenValidator(
        IOptionsMonitor<JwtBearerOptions> jwtBearer,
        IConfiguration configuration,
        ILogger<SpaarkeGuestTokenValidator> logger)
    {
        _jwtBearer = jwtBearer ?? throw new ArgumentNullException(nameof(jwtBearer));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates <paramref name="guestToken"/> and applies the A9 link conditions against the home bearer
    /// (<paramref name="homeTenantId"/>, <paramref name="homePreferredUsername"/>). Never throws for a bad token.
    /// </summary>
    public async Task<GuestTokenValidation> ValidateAsync(
        string guestToken, Guid homeTenantId, string? homePreferredUsername, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guestToken);

        var options = _jwtBearer.Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters.Clone();

        // Fail closed if the default scheme was ever configured without these: this route must not inherit a weakening.
        if (!parameters.RequireSignedTokens || !parameters.ValidateLifetime || !parameters.ValidateAudience
            || !parameters.ValidateIssuer)
        {
            _logger.LogError(
                "[A9-LINK] The default scheme's token validation parameters do not require signature, lifetime, audience and "
                + "issuer validation; every guest token is refused ({Reason})", GuestTokenDeny.Invalid);
            return GuestTokenValidation.Deny(GuestTokenDeny.Invalid);
        }

        TokenValidationResult result;
        try
        {
            // The signing metadata exactly as JwtBearerHandler supplies it: a BaseConfigurationManager is handed to the
            // validator (so key refresh and signing-key issuer validation behave as on the scheme); otherwise its current
            // configuration's issuer and keys are added.
            if (options.ConfigurationManager is BaseConfigurationManager baseManager)
            {
                // Fetched here first: the token handler swallows a failed metadata fetch and validates with no keys, which
                // would refuse a good token (403). A fetch that throws is an outage — Unavailable, 503, try again.
                _ = await baseManager.GetBaseConfigurationAsync(ct).ConfigureAwait(false);
                parameters.ConfigurationManager = baseManager;
            }
            else if (options.ConfigurationManager is not null)
            {
                var configuration = await options.ConfigurationManager.GetConfigurationAsync(ct).ConfigureAwait(false);
                parameters.ValidIssuers = (parameters.ValidIssuers ?? Array.Empty<string>()).Append(configuration.Issuer);
                parameters.IssuerSigningKeys = (parameters.IssuerSigningKeys ?? Array.Empty<SecurityKey>())
                    .Concat(configuration.SigningKeys);
            }

            result = await new JsonWebTokenHandler { MapInboundClaims = false }
                .ValidateTokenAsync(guestToken, parameters).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The exception text can quote token content: log its type only. A throw is not a verdict on the token.
            _logger.LogWarning("[A9-LINK] Guest token validation threw {ExceptionType} ({Reason})", ex.GetType().Name,
                GuestTokenDeny.Unavailable);
            return GuestTokenValidation.Deny(GuestTokenDeny.Unavailable);
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            var reason = result.Exception is SecurityTokenExpiredException ? GuestTokenDeny.Expired : GuestTokenDeny.Invalid;
            _logger.LogWarning("[A9-LINK] Guest token refused: {ExceptionType} ({Reason})",
                result.Exception?.GetType().Name ?? "none", reason);
            return GuestTokenValidation.Deny(reason);
        }

        var deny = DenyReason(
            jwt, homeTenantId, homePreferredUsername, _configuration["AzureAd:TenantId"], _configuration["AzureAd:ClientId"],
            out var guestObjectId);
        if (deny is not null)
        {
            _logger.LogWarning("[A9-LINK] Guest token refused by the A9 link conditions ({Reason})", deny);
            return GuestTokenValidation.Deny(deny);
        }

        return new GuestTokenValidation(guestObjectId, null);
    }

    /// <summary>
    /// The A9 link conditions on a token the default scheme's parameters have already validated (see the file header).
    /// Pure: every claim is read from the validated token itself. Null when accepted.
    /// </summary>
    internal static string? DenyReason(
        JsonWebToken jwt, Guid homeTenantId, string? homePreferredUsername, string? spaarkeTenantId, string? stampBffAppId,
        out Guid guestObjectId)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        guestObjectId = Guid.Empty;

        // The stamp BFF app only — never another audience the default scheme accepts (the Copilot audience).
        var stampAudiences = AuthorizationModule.BuildCiamValidAudiences(stampBffAppId);
        if (stampAudiences.Count == 0
            || !jwt.Audiences.Any(a => stampAudiences.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            return GuestTokenDeny.Invalid;
        }

        if (jwt.TryGetClaim("idtyp", out var idtyp) && !string.Equals(idtyp.Value, "user", StringComparison.Ordinal))
        {
            return GuestTokenDeny.Invalid;
        }

        if (!jwt.TryGetPayloadValue<string>("scp", out var scp) || scp is null
            || !scp.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("user_impersonation", StringComparer.Ordinal))
        {
            return GuestTokenDeny.Invalid;
        }

        if (!Guid.TryParse(spaarkeTenantId?.Trim(), out var spaarke) || spaarke == Guid.Empty
            || !jwt.TryGetPayloadValue<string>("tid", out var tid) || !Guid.TryParse(tid?.Trim(), out var tokenTenant)
            || tokenTenant != spaarke)
        {
            return GuestTokenDeny.TenantNotSpaarke;
        }

        if (!jwt.TryGetClaim(WorkforceCallerClaims.AcctClaim, out var acct) || acct.Value?.Trim() != "1")
        {
            return GuestTokenDeny.NotGuest;
        }

        if (!jwt.TryGetPayloadValue<string>("idp", out var idp)
            || !WorkforceMembershipTest.TryParseHomeTenant(idp, out var guestHomeTenant)
            || homeTenantId == Guid.Empty || guestHomeTenant != homeTenantId)
        {
            return GuestTokenDeny.HomeTenantMismatch;
        }

        if (!jwt.TryGetPayloadValue<string>("oid", out var oid) || !Guid.TryParse(oid?.Trim(), out var parsedOid)
            || parsedOid == Guid.Empty)
        {
            return GuestTokenDeny.Invalid;
        }

        var homeUpn = homePreferredUsername?.Trim();
        var guestHomeUpn = GuestHomeUpn(jwt);
        if (string.IsNullOrEmpty(homeUpn) || guestHomeUpn is null
            || !string.Equals(guestHomeUpn, homeUpn, StringComparison.OrdinalIgnoreCase))
        {
            return GuestTokenDeny.UpnMismatch;
        }

        guestObjectId = parsedOid;
        return null;
    }

    /// <summary>
    /// The guest's home UPN, read from the guest token (design §5): the first <c>#EXT#</c> UPN among <c>upn</c>,
    /// <c>unique_name</c> and <c>preferred_username</c>, decoded (<c>name_domain#EXT#@…</c> → <c>name@domain</c>: the LAST
    /// underscore before the marker separates the domain, which cannot contain one); else <c>email</c>; else a
    /// <c>preferred_username</c> without the marker. Null when none is usable. Pure.
    /// </summary>
    internal static string? GuestHomeUpn(JsonWebToken jwt)
    {
        foreach (var type in new[] { "upn", "unique_name", "preferred_username" })
        {
            var value = Value(jwt, type);
            var marker = value?.IndexOf(ExternalUpnMarker, StringComparison.OrdinalIgnoreCase) ?? -1;
            if (value is not null && marker > 0)
            {
                var local = value[..marker];
                var separator = local.LastIndexOf('_');
                return separator > 0 && separator < local.Length - 1
                    ? $"{local[..separator]}@{local[(separator + 1)..]}"
                    : null;
            }
        }

        return Value(jwt, "email") ?? Value(jwt, "preferred_username");
    }

    private static string? Value(JsonWebToken jwt, string type)
        => jwt.TryGetClaim(type, out var claim) && !string.IsNullOrWhiteSpace(claim.Value) && !claim.Value.Any(char.IsControl)
            ? claim.Value.Trim()
            : null;

    /// <summary>The home bearer's <c>preferred_username</c>, trimmed; null when absent. Never <c>upn</c> or <c>email</c>.</summary>
    internal static string? HomePreferredUsername(ClaimsPrincipal home)
        => home.FindFirst("preferred_username")?.Value?.Trim() is { Length: > 0 } value ? value : null;
}
