namespace Sprk.Bff.Api.Services.Communication.Tracking;

/// <summary>
/// FR-A1 / NFR-07: signs and verifies the opaque, tamper-evident token carried in the outbound tracking
/// footer. The token binds <c>(recordType, recordId, tenantId, issued)</c> under an HMAC-SHA256 key held in
/// Azure Key Vault (never in config, never logged — ADR-028 Path A). <b>Verify-before-trust</b> is the
/// load-bearing security property: a forged, edited, or malformed token MUST fail verification and be ignored
/// by the <c>TrackingTokenRung</c> (task 013), so it can never auto-file to an attacker-chosen record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async by necessity.</b> The signing key is resolved from Key Vault (an async I/O), so the surface is
/// <see cref="SignAsync"/> / <see cref="VerifyAsync"/> rather than the POML's synchronous <c>TryVerify(out …)</c>
/// — a sync out-param cannot await the key load without a fragile pre-warm. The no-throw contract is preserved
/// via <see cref="TrackingTokenVerification"/> (a bool + optional payload), NOT exceptions.
/// </para>
/// <para>
/// <b>Best-effort / non-fatal (NFR-04).</b> When the feature is unconfigured (no Key Vault secret name) or the
/// key cannot be resolved, <see cref="SignAsync"/> returns <c>null</c> (no footer token — the send path simply
/// omits it) and <see cref="VerifyAsync"/> returns <see cref="TrackingTokenVerification.Invalid"/>. The callers
/// (send path 012, rung 013) MUST treat a null/invalid result as "no trusted token", never as a hard failure.
/// </para>
/// </remarks>
public interface ITrackingTokenSigner
{
    /// <summary>
    /// Signs <c>(recordType, recordId, tenantId, issued)</c> and returns a URL-safe opaque token, or
    /// <c>null</c> when the feature is unconfigured / the key is unavailable (non-fatal — the footer is omitted).
    /// </summary>
    Task<string?> SignAsync(
        string recordType,
        Guid recordId,
        string? tenantId,
        DateTimeOffset issued,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a token's HMAC signature (constant-time) and, on success, returns the bound payload. Returns
    /// <see cref="TrackingTokenVerification.Invalid"/> for any tampered / forged / malformed / unconfigured
    /// input. NEVER throws (NFR-07 / NFR-04).
    /// </summary>
    Task<TrackingTokenVerification> VerifyAsync(string? token, CancellationToken ct = default);
}

/// <summary>
/// The tuple bound by a tracking token. <see cref="Issued"/> is carried (not enforced) here — the freshness /
/// staleness decision belongs to the <c>TrackingTokenRung</c> (task 013), keeping the signer purely about
/// signature validity.
/// </summary>
public sealed record TrackingTokenPayload(
    string RecordType,
    Guid RecordId,
    string? TenantId,
    DateTimeOffset Issued);

/// <summary>
/// Result of <see cref="ITrackingTokenSigner.VerifyAsync"/>. <see cref="IsValid"/> is true only when the HMAC
/// signature verified; <see cref="Payload"/> is non-null exactly when <see cref="IsValid"/> is true. Replaces
/// the POML's <c>bool TryVerify(out …)</c> so the contract survives async key resolution (see the interface
/// remarks).
/// </summary>
public sealed record TrackingTokenVerification(bool IsValid, TrackingTokenPayload? Payload)
{
    /// <summary>The canonical "not a trusted token" result — tampered, forged, malformed, or unconfigured.</summary>
    public static readonly TrackingTokenVerification Invalid = new(false, null);
}
