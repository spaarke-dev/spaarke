using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Creates external-user accounts in the Entra External ID (CIAM) tenant as LOCAL email accounts
/// (Graph <c>POST /users</c> with an <c>identities</c> block), for the admin-initiated broker
/// onboarding flow (task 025, ADR-028 Amendment A1). Replaces the workforce Entra B2B guest
/// invitation — no B2B guest is created and no user token is ever exchanged (broker-only).
///
/// <para><b>Why this exists (ADR-010 / §11 justification).</b> The existing
/// <see cref="GraphUserService.CreateUserAsync"/> creates <i>workforce</i> users via
/// <c>GraphClientFactory.ForApp()</c> (single-tenant, UPN-based) and cannot create a CIAM local
/// account (which needs the cross-tenant CIAM Graph client from task 022 and an email
/// <c>identities</c> block instead of a generated UPN). It is not extensible to that shape, so this
/// focused service reuses <see cref="PasswordGenerator"/> and <see cref="CiamGraphClientFactory"/>
/// rather than forking a divergent path. It is also the testable seam for the CIAM user payload
/// (see <see cref="BuildCiamUser"/>).</para>
///
/// ADR-010: concrete singleton (no interface).
/// </summary>
public sealed class CiamUserProvisioningService
{
    private readonly CiamGraphClientFactory _ciamGraphClientFactory;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly ILogger<CiamUserProvisioningService> _logger;

    /// <summary>
    /// The CIAM tenant's initial domain (e.g. <c>spaarkeextid.onmicrosoft.com</c>), used as the
    /// local-account identity <c>issuer</c> (NOT a social IdP). Sourced from <c>Ciam:Domain</c>.
    /// </summary>
    private readonly string _issuerDomain;

    public CiamUserProvisioningService(
        CiamGraphClientFactory ciamGraphClientFactory,
        PasswordGenerator passwordGenerator,
        IConfiguration configuration,
        ILogger<CiamUserProvisioningService> logger)
    {
        _ciamGraphClientFactory = ciamGraphClientFactory ?? throw new ArgumentNullException(nameof(ciamGraphClientFactory));
        _passwordGenerator = passwordGenerator ?? throw new ArgumentNullException(nameof(passwordGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _issuerDomain = configuration.GetSection("Ciam")["Domain"]
            ?? throw new InvalidOperationException("Ciam:Domain is not configured (CIAM tenant initial domain).");
    }

    /// <summary>
    /// Creates a CIAM local email account and returns the new user's stable object id (<c>oid</c>).
    /// The account is created with a cryptographically-generated temporary password,
    /// <c>forceChangePasswordNextSignIn=true</c>, and <c>passwordPolicies=DisablePasswordExpiration</c>
    /// (required for local accounts per the CIAM provisioning spec). The temp password is delivered
    /// out-of-band via SSPR "Forgot password" — it is never returned to the caller or logged.
    /// </summary>
    public async Task<string> CreateCiamUserAsync(
        string email,
        string? firstName,
        string? lastName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var temporaryPassword = _passwordGenerator.Generate();
        var user = BuildCiamUser(email, firstName, lastName, temporaryPassword, _issuerDomain);

        var graphClient = await _ciamGraphClientFactory.CreateClientForAppAsync(ct);
        var created = await graphClient.Users.PostAsync(user, cancellationToken: ct);

        var oid = created?.Id
            ?? throw new InvalidOperationException("CIAM Graph POST /users returned a null user id.");

        _logger.LogInformation("[CIAM-PROVISION] Created CIAM user {Oid} for {Email}", oid, email);
        return oid;
    }

    /// <summary>
    /// Builds the Graph <c>POST /users</c> payload for a CIAM local email account. Pure and static so
    /// task 030 can assert the payload shape (email <c>identities</c> block,
    /// <c>forceChangePasswordNextSignIn</c>, <c>passwordPolicies=DisablePasswordExpiration</c>) without
    /// a live Graph call.
    /// </summary>
    internal static User BuildCiamUser(
        string email,
        string? firstName,
        string? lastName,
        string temporaryPassword,
        string issuerDomain)
    {
        var displayName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email;
        }

        return new User
        {
            AccountEnabled = true,
            DisplayName = displayName,
            GivenName = string.IsNullOrWhiteSpace(firstName) ? null : firstName,
            Surname = string.IsNullOrWhiteSpace(lastName) ? null : lastName,
            Mail = email,
            // Local email account: issuer is the CIAM tenant's initial domain, NOT a social IdP.
            Identities = new List<ObjectIdentity>
            {
                new ObjectIdentity
                {
                    SignInType = "emailAddress",
                    Issuer = issuerDomain,
                    IssuerAssignedId = email
                }
            },
            PasswordProfile = new PasswordProfile
            {
                Password = temporaryPassword,
                ForceChangePasswordNextSignIn = true
            },
            // Required for local accounts (per CIAM provisioning spec) — password must not expire.
            PasswordPolicies = "DisablePasswordExpiration"
        };
    }
}
