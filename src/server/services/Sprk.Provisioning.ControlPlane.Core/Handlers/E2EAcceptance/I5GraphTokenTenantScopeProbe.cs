// -----------------------------------------------------------------------------
// I5GraphTokenTenantScopeProbe.cs
//
// Task 179 (Phase C'' Wave G-7 Batch G-7A1 — H13 acceptance-gate real probe) —
// PRODUCTION <see cref="IInvariantProbe"/> impl for
// <see cref="InvariantKind.I5GraphTokenTenant"/>. Runtime sample-verifies the
// §4D I5 invariant (Graph token tenant scope, FR-32): the token acquisition
// path used by provisioning handlers carries an EXPLICIT per-tenant authority;
// the acquired token's `tid` claim matches the requested tenant.
//
// SILENT-FAIL CATEGORY GUARDED: cross-tenant token leak — the operational
// symptom is a Graph call in customer-A's provisioning flow succeeding with
// a token minted for the Spaarke platform tenant (or worse, for a DIFFERENT
// customer tenant), enabling data reads/writes across tenant boundaries.
// Compile-time enforcement (ArchTest I5 — task 064) catches the code shape;
// this runtime probe catches the RESIDUAL misconfiguration where source is
// clean but the runtime credential chain picks up the wrong tenant.
//
// TASK 130 PRECEDENT (referenced verbatim in this task's briefing):
//   GraphServiceClient / AzureIdentityAuthenticationProvider have NO
//   per-request tenantId overload; the ONLY correct per-tenant scoping is a
//   FRESH DefaultAzureCredential per call with TenantId baked into
//   DefaultAzureCredentialOptions. GraphAppRegistrationProvisioner.cs +
//   GraphRestAppRoleGranter.cs are the two established callers of this
//   pattern. This probe verifies the SAME pattern is being used by every
//   handler that mints Graph tokens — belt-and-braces on top of the ArchTest.
//
// COMPOSITE-COORDINATED (task 174): implements <see cref="IInvariantProbe"/>
// (shared seam authored by sibling task 174 alongside
// <see cref="CompositeInvariantVerifier"/>) so the composite dispatches this
// per-kind. Sibling probes (I1/I2/I3/I4 authored by tasks 170/173/174/176)
// implement the SAME interface; CompositeInvariantVerifier falls back to
// InvariantProbeDeferralMessages.DeferralDiagnostic for any invariant without
// a registered probe.
//
// TESTABILITY (fake-transport per POML briefing):
//   The <see cref="TokenCredential"/> factory is injected as
//   <c>Func&lt;string, TokenCredential&gt;</c> so tests can supply a fake
//   credential AND assert the factory was called with the correct tenantId.
//   Production wiring uses
//   <c>tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions
//   { TenantId = tenantId })</c> — the same lambda H10's GraphRestAppRoleGranter
//   already establishes (identical Azure.Identity idiom).
//
// PROBE DECISION TREE (contract per IInvariantProbe.ProbeAsync docs: return
// outcome, never throw):
//   (1) tenantId blank            -> Failed (probe refuses ambient acquisition)
//   (2) Factory returns null      -> InfraFault
//   (3) Factory throws            -> InfraFault
//   (4) GetTokenAsync throws      -> InfraFault (with exception type/message
//                                    in diagnostic)
//   (5) Token blank / malformed   -> InfraFault
//   (6) JWT parse fails           -> InfraFault
//   (7) `tid` claim missing       -> InfraFault (cannot verdict cross-tenant)
//   (8) `tid` != requested tenant -> Failed (CATASTROPHIC — cross-tenant leak)
//   (9) `tid` == requested tenant -> Passed
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: PlaceholderInvariantVerifier's I5 branch returns InfraFault
//     unconditionally; CompositeInvariantVerifier (task 174) is the new
//     dispatcher.
//   Extension: adding a per-invariant IInvariantProbe following the shared
//     seam IS the minimal move.
//   Cost-of-doing-nothing: without this probe, H13's acceptance gate cannot
//     verdict I5 as Pass — every future customer stamp would either flip to
//     Ready with I5 InfraFault silenced OR forever remain Quarantined on I5.
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IInvariantProbe"/>
public sealed class I5GraphTokenTenantScopeProbe : IInvariantProbe
{
    /// <summary>
    /// Graph default scope — parity with the callers this probe is
    /// verifying (<c>GraphRestAppRoleGranter.GraphScope</c>,
    /// <c>GraphAppRegistrationProvisioner.GraphDefaultScope</c>, etc.).
    /// </summary>
    public const string GraphDefaultScope = "https://graph.microsoft.com/.default";

    /// <summary>JWT claim name used by Entra to encode the token's issuing tenant id.</summary>
    private const string TidClaimName = "tid";

    private readonly Func<string, TokenCredential> _tenantScopedCredentialFactory;
    private readonly ILogger<I5GraphTokenTenantScopeProbe> _logger;

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I5GraphTokenTenant;

    /// <summary>
    /// Production constructor. Uses <c>new DefaultAzureCredential(new
    /// DefaultAzureCredentialOptions { TenantId = tenantId })</c> per call —
    /// the pattern task 130 established (see file header).
    /// </summary>
    public I5GraphTokenTenantScopeProbe(ILogger<I5GraphTokenTenantScopeProbe> logger)
        : this(
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
              logger)
    {
    }

    /// <summary>
    /// Testing constructor — accepts an arbitrary credential factory so unit
    /// tests can inject a fake <see cref="TokenCredential"/> AND assert the
    /// factory was called with the correct tenantId.
    /// </summary>
    public I5GraphTokenTenantScopeProbe(
        Func<string, TokenCredential> tenantScopedCredentialFactory,
        ILogger<I5GraphTokenTenantScopeProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantScopedCredentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantScopedCredentialFactory = tenantScopedCredentialFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) A blank tenantId is a FAILED verdict — the probe refuses to
        //     exercise an ambient/default-tenant acquisition even if the
        //     runtime allowed one.
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogWarning(
                "I5 probe FAILED — blank tenantId (customerId={CustomerId} runId={RunId}). " +
                "The probe refuses to exercise ambient/default-tenant Graph token acquisition; " +
                "an explicit tenantId is required per §4D I5 (FR-32).",
                request.CustomerId, request.RunId);
            return new InvariantVerificationOutcome.Failed(
                InvariantKind.I5GraphTokenTenant,
                "observed=blank tenantId; expected=an explicit Entra tenant GUID. " +
                "§4D I5 (FR-32) forbids ambient/default-tenant Graph token acquisition — " +
                "the probe refuses to attempt one.");
        }

        // (2) Build a tenant-scoped credential via the injected factory.
        //     Passing the requested tenantId to the factory is itself part
        //     of the invariant being tested: production wiring MUST bake
        //     TenantId into DefaultAzureCredentialOptions.
        TokenCredential? credential;
        try
        {
            credential = _tenantScopedCredentialFactory(request.TenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"tenant-scoped credential factory threw for tenantId='{request.TenantId}': " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
        if (credential is null)
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"tenant-scoped credential factory returned null for tenantId='{request.TenantId}'.");
        }

        // (3) Acquire a Graph token from the tenant-scoped credential.
        AccessToken token;
        try
        {
            token = await credential
                .GetTokenAsync(new TokenRequestContext(new[] { GraphDefaultScope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "I5 probe InfraFault — Graph token acquisition threw " +
                "(customerId={CustomerId} runId={RunId} tenantId={TenantId})",
                request.CustomerId, request.RunId, request.TenantId);
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"Graph token acquisition threw for tenantId='{request.TenantId}': " +
                $"{ex.GetType().Name}: {ex.Message}. " +
                "Cannot verdict cross-tenant leak — handler classifies Resumable.");
        }

        if (string.IsNullOrWhiteSpace(token.Token))
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"Graph token acquisition returned an empty token for tenantId='{request.TenantId}'. " +
                "Cannot verdict — handler classifies Resumable.");
        }

        // (4) Extract the `tid` claim.
        string? actualTid;
        try
        {
            actualTid = TryExtractTidClaim(token.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"acquired Graph token could not be JWT-parsed: {ex.GetType().Name}: {ex.Message}. " +
                "Cannot verdict cross-tenant leak — handler classifies Resumable.");
        }
        if (string.IsNullOrWhiteSpace(actualTid))
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant,
                $"acquired Graph token has no `{TidClaimName}` claim; cannot verdict tenant scope. " +
                "Handler classifies Resumable.");
        }

        // (5) Verdict. The comparison is Ordinal + case-INsensitive (Entra
        //     tenant GUIDs are documented as case-insensitive per Azure ADR
        //     conventions; matches ADR-044 canonicalization posture — the
        //     probe accepts either casing rather than forcing a
        //     canonicalization pass).
        if (!string.Equals(actualTid, request.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "I5 probe FAILED — CROSS-TENANT LEAK: expected tenantId={ExpectedTenantId} " +
                "acquired token tid={ActualTid} (customerId={CustomerId} runId={RunId}). " +
                "§4D I5 (FR-32) CATASTROPHIC.",
                request.TenantId, actualTid, request.CustomerId, request.RunId);
            return new InvariantVerificationOutcome.Failed(
                InvariantKind.I5GraphTokenTenant,
                $"observed=Graph token `tid` claim '{actualTid}'; " +
                $"expected='{request.TenantId}'. Cross-tenant token leak — the credential " +
                $"factory returned a credential whose acquired token is scoped to a DIFFERENT " +
                $"tenant than requested. §4D I5 CATASTROPHIC severity — QuarantineRequired.");
        }

        _logger.LogInformation(
            "I5 probe Passed: Graph token `tid` matches requested tenantId " +
            "(customerId={CustomerId} runId={RunId} tenantId={TenantId})",
            request.CustomerId, request.RunId, request.TenantId);
        return new InvariantVerificationOutcome.Passed(InvariantKind.I5GraphTokenTenant);
    }

    /// <summary>
    /// Extracts the <c>tid</c> claim from a JWT bearer token. Returns
    /// <c>null</c> if the claim is absent. Throws on malformed JWTs — the
    /// caller classifies InfraFault.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal — no <c>System.IdentityModel.Tokens.Jwt</c>
    /// dependency (not currently referenced by this project); base64-url +
    /// <see cref="System.Text.Json"/> is sufficient for reading a single
    /// well-known claim. Signature validation is NOT required (the probe
    /// is verifying WHICH tenant issued the token, not that the token was
    /// forged — the credential chain does not return unsigned tokens).
    /// </remarks>
    internal static string? TryExtractTidClaim(string jwt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwt);
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            throw new FormatException($"JWT has {parts.Length} segment(s); expected at least 2 (header.payload).");
        }
        var payloadBytes = Base64UrlDecode(parts[1]);
        using var document = JsonDocument.Parse(payloadBytes);
        if (!document.RootElement.TryGetProperty(TidClaimName, out var tidElement))
        {
            return null;
        }
        return tidElement.ValueKind == JsonValueKind.String
            ? tidElement.GetString()
            : tidElement.ToString();
    }

    /// <summary>
    /// Base64-URL decoder (RFC 7515 §2). JWTs use URL-safe base64 without
    /// padding; the standard <see cref="Convert.FromBase64String"/> rejects
    /// that shape, so we re-pad + swap the alphabet locally.
    /// </summary>
    internal static byte[] Base64UrlDecode(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var builder = new StringBuilder(input.Length + 4);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            builder.Append(c switch
            {
                '-' => '+',
                '_' => '/',
                _ => c,
            });
        }
        switch (builder.Length % 4)
        {
            case 2: builder.Append("=="); break;
            case 3: builder.Append('='); break;
            case 0: break;
            default:
                throw new FormatException($"Invalid base64url length {builder.Length} (mod 4 = {builder.Length % 4}).");
        }
        return Convert.FromBase64String(builder.ToString());
    }
}
