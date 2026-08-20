namespace Sprk.Bff.Api.Endpoints.Diagnostics;

/// <summary>
/// Resolves the SPE container id for a tenant from tenant-scoped, boot-time-bound
/// configuration — NEVER from a hardcoded literal or fallback default.
///
/// <para>
/// This is the BFF-side seam the L2 H13 I4 invariant probe
/// (<c>SpeContainerResolverInvariantProbe</c> in
/// <c>Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/</c>) exercises via
/// <c>GET /api/diagnostics/tenant-container-resolver</c>. Per spec.md FR-31 / design.md §4D I4:
/// every SPE container id handed to the customer's BFF Graph SDK MUST derive from this
/// resolver (or an IOptions bag bound from the customer's KV secret / Dataverse env-var
/// at boot). A fallback-default container id would silently route one customer's SPE
/// uploads into another customer's container (CATASTROPHIC cross-tenant leak).
/// </para>
///
/// <para>
/// Contract obligations (mirrored from the L2 probe's file header):
/// <list type="bullet">
/// <item>Resolution MUST be a live call against the same tenant-scoped configuration the
/// production upload path uses — never a canned mirror of inputs.</item>
/// <item>An unknown / mismatched tenant MUST fail resolution — it must NEVER return the
/// configured container for a tenant this deployment does not serve.</item>
/// <item>A missing configured container id MUST fail resolution — no fallback default.</item>
/// </list>
/// </para>
/// </summary>
public interface ITenantContainerResolver
{
    /// <summary>
    /// Resolves the SPE container id for <paramref name="tenantId"/>.
    /// Returns a failure result (never throws for expected failure modes) so the
    /// diagnostic endpoint can map failure codes to precise HTTP statuses.
    /// </summary>
    /// <param name="tenantId">Entra tenant id (GUID) to resolve for. Required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TenantContainerResolutionResult> ResolveAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Successful tenant-container resolution tuple. Field names align 1:1 with the
/// diagnostic endpoint's JSON contract consumed by the L2 I4 probe.
/// </summary>
/// <param name="TenantId">
/// The tenant the resolution was scoped to. Echoed VERBATIM as requested by the caller
/// (GUID casing is not semantic; the L2 probe compares ordinally against its request value,
/// so the resolver echoes the caller's exact string once the case-insensitive tenant-scope
/// match has succeeded).
/// </param>
/// <param name="ContainerId">The resolved SPE container id (canonical Graph <c>b!…</c> shape).</param>
/// <param name="ResolverSource">
/// Where the value was resolved from: <c>"options"</c> (IOptions bag bound at boot from
/// App Service settings / KV references), <c>"kv"</c>, or <c>"env"</c> — per the L2 probe's
/// documented enum.
/// </param>
/// <param name="ResolvedFromLiteral">
/// TRUE only if a hardcoded literal / fallback-default path fired — which this resolver
/// never does by construction (it fails instead). The L2 probe treats TRUE as CATASTROPHIC.
/// </param>
public sealed record TenantContainerResolution(
    string TenantId,
    string ContainerId,
    string ResolverSource,
    bool ResolvedFromLiteral);

/// <summary>Expected failure modes of tenant-container resolution.</summary>
public enum TenantContainerResolutionFailureCode
{
    /// <summary>
    /// The requested tenant does not match the tenant this BFF deployment is scoped to.
    /// The resolver refuses to return the configured container for a foreign tenant
    /// (§4D I4 — returning it would BE the cross-tenant leak the invariant catches).
    /// Client-addressable → HTTP 400.
    /// </summary>
    TenantNotServed,

    /// <summary>
    /// This deployment's own tenant scope is not pinned to a concrete tenant GUID
    /// (e.g., <c>Graph:TenantId</c> is blank, "common", or "organizations"), so the
    /// resolver cannot attest tenant-scoped resolution. Server misconfig → HTTP 500.
    /// </summary>
    TenantScopeNotPinned,

    /// <summary>
    /// No SPE container id is bound in configuration for this deployment. The resolver
    /// fails rather than substituting any default (the exact silent-fail I4 forbids).
    /// Server misconfig → HTTP 500.
    /// </summary>
    ContainerNotConfigured,
}

/// <summary>
/// Discriminated result of <see cref="ITenantContainerResolver.ResolveAsync"/> —
/// either a <see cref="Resolution"/> or a (<see cref="FailureCode"/>, <see cref="Diagnostic"/>) pair.
/// </summary>
public sealed record TenantContainerResolutionResult
{
    /// <summary>Successful resolution; null on failure.</summary>
    public TenantContainerResolution? Resolution { get; private init; }

    /// <summary>Failure code; null on success.</summary>
    public TenantContainerResolutionFailureCode? FailureCode { get; private init; }

    /// <summary>Operator-facing failure diagnostic; null on success.</summary>
    public string? Diagnostic { get; private init; }

    /// <summary>True when <see cref="Resolution"/> is populated.</summary>
    public bool Succeeded => Resolution is not null;

    /// <summary>Creates a success result.</summary>
    public static TenantContainerResolutionResult Success(TenantContainerResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new TenantContainerResolutionResult { Resolution = resolution };
    }

    /// <summary>Creates a failure result.</summary>
    public static TenantContainerResolutionResult Failure(
        TenantContainerResolutionFailureCode code, string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        return new TenantContainerResolutionResult { FailureCode = code, Diagnostic = diagnostic };
    }
}
