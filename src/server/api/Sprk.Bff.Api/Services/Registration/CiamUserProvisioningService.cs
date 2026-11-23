using System.Diagnostics;
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
/// ADR-010: concrete singleton (no interface). Not sealed only so <see cref="CreateCiamUserAsync"/> is a test seam
/// (task 036 review: the invite-and-grant tests run the "newly provisioned" path without a live CIAM tenant); no
/// production type derives from it.
/// </summary>
public class CiamUserProvisioningService
{
    private readonly CiamGraphClientFactory _ciamGraphClientFactory;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly ILogger<CiamUserProvisioningService> _logger;

    /// <summary>
    /// The CIAM tenant's initial domain (e.g. <c>spaarkeextid.onmicrosoft.com</c>), used as the
    /// local-account identity <c>issuer</c> (NOT a social IdP). Sourced from <c>Ciam:Domain</c>; <c>null</c> on a host
    /// without CIAM, validated at first use (<see cref="EnsureConfigured"/>).
    /// </summary>
    private readonly string? _issuerDomain;

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

        // Task 185 slice F (ADR-010 path C — lazy validation, not a service locator): read here, validated at first use, so
        // a host without Ciam:* can construct this service (and the invite service that injects it). Only a CIAM call there
        // throws InvalidOperationException; configured hosts behave as before.
        _issuerDomain = configuration.GetSection("Ciam")["Domain"];
    }

    /// <summary>
    /// Task 185 slice F: whether this host carries every <c>Ciam:*</c> value an account create needs (this service's
    /// <c>Ciam:Domain</c> and the CIAM Graph client's). Pure; never throws.
    /// </summary>
    internal bool IsConfigured => _issuerDomain is not null && _ciamGraphClientFactory.IsConfigured;

    /// <summary>
    /// Task 185 slice F: throws <see cref="InvalidOperationException"/> naming the first <c>Ciam:*</c> value that is not
    /// configured — the checks the constructors used to run, with the same messages. A no-op on a configured host.
    /// </summary>
    internal void EnsureConfigured()
    {
        _ = _issuerDomain ?? throw new InvalidOperationException("Ciam:Domain is not configured (CIAM tenant initial domain).");
        _ciamGraphClientFactory.EnsureConfigured();
    }

    /// <summary>
    /// Creates a CIAM local email account and returns the new user's stable object id (<c>oid</c>).
    /// The account is created with a cryptographically-generated temporary password,
    /// <c>forceChangePasswordNextSignIn=true</c>, and <c>passwordPolicies=DisablePasswordExpiration</c>
    /// (required for local accounts per the CIAM provisioning spec). The temp password is delivered
    /// out-of-band via SSPR "Forgot password" — it is never returned to the caller or logged.
    /// </summary>
    public virtual async Task<string> CreateCiamUserAsync(
        string email,
        string? firstName,
        string? lastName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        EnsureConfigured();

        var temporaryPassword = _passwordGenerator.Generate();
        var user = BuildCiamUser(email, firstName, lastName, temporaryPassword, _issuerDomain!);

        var graphClient = await _ciamGraphClientFactory.CreateClientForAppAsync(ct);
        User? created;
        try
        {
            created = await graphClient.Users.PostAsync(user, cancellationToken: ct);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError error) when (IsExistingAccountConflict(error))
        {
            // Task 184: the email already has a CIAM account. Translated here so no caller outside this Graph-owning
            // service references a Graph SDK type (ADR-007); every other Graph error propagates unchanged.
            throw new CiamAccountAlreadyExistsException(email, error);
        }

        var oid = created?.Id
            ?? throw new InvalidOperationException("CIAM Graph POST /users returned a null user id.");

        // Never the invitee's email: the oid and the trace id identify the create.
        _logger.LogInformation("[CIAM-PROVISION] Created CIAM user {Oid}. Trace {TraceId}.", oid,
            Activity.Current?.TraceId.ToString() ?? "(none)");
        return oid;
    }

    /// <summary>
    /// Task 184 — true when <paramref name="error"/> is Graph's answer to a second <c>POST /users</c> for an email that already
    /// has a CIAM account: HTTP 400 <c>Request_BadRequest</c> with an <c>ObjectConflict</c> on <c>proxyAddresses</c> (the
    /// request sets <c>mail</c>), as provisioning's spike S1 (ii) measured on 2026-10-12. ONLY that: any other 400, a conflict
    /// on another property, or any other status keeps the invite's failure path. Pure.
    /// </summary>
    /// <remarks>
    /// The conflict is read from <c>error.details[]</c> (<c>code = ObjectConflict</c>, <c>target = proxyAddresses</c>). When
    /// Graph sends no details, the message must name <c>proxyAddresses</c> as already existing ("Another object with the
    /// same value for property proxyAddresses already exists.").
    /// </remarks>
    internal static bool IsExistingAccountConflict(Microsoft.Graph.Models.ODataErrors.ODataError? error)
    {
        if (error is null || error.ResponseStatusCode != StatusCodes.Status400BadRequest)
        {
            return false;
        }

        var main = error.Error;
        if (main is null || !string.Equals(main.Code, "Request_BadRequest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (main.Details is { Count: > 0 } details)
        {
            return details.Any(d =>
                string.Equals(d.Code, "ObjectConflict", StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.Target, "proxyAddresses", StringComparison.OrdinalIgnoreCase));
        }

        var message = main.Message ?? string.Empty;
        return message.Contains("proxyAddresses", StringComparison.OrdinalIgnoreCase)
               && message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// Task 184 — thrown by <see cref="CiamUserProvisioningService.CreateCiamUserAsync"/> when the email already has a CIAM
/// account (Graph 400 <c>ObjectConflict</c> on <c>proxyAddresses</c>; see
/// <see cref="CiamUserProvisioningService.IsExistingAccountConflict"/>). The invite treats it as the "ExistingAccount"
/// outcome. It carries no account id: the oid is deliberately NOT looked up at invite time (the account may belong to
/// another customer's invite), and the contact binds at its first sign-in through the E11 intent gate. Graph-free surface
/// (ADR-007); the Graph error is kept only as <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class CiamAccountAlreadyExistsException : InvalidOperationException
{
    public CiamAccountAlreadyExistsException(string email, Exception innerException)
        : base("A CIAM account already exists for this email address.", innerException)
    {
        Email = email;
    }

    /// <summary>The invitee email the CIAM create was refused for.</summary>
    public string Email { get; }
}
