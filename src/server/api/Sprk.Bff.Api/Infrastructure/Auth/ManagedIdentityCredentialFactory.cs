using Azure.Core;
using Azure.Identity;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Factory for constructing <see cref="DefaultAzureCredential"/> instances pinned to the
/// configured User-Assigned Managed Identity (UAMI) clientId.
/// </summary>
/// <remarks>
/// Created 2026-05-24 in response to PlaybookService 500 errors after migrating dev to a Linux
/// App Service with multiple managed identities attached. Without an explicit UAMI clientId, the
/// underlying <c>ManagedIdentityCredential</c> fails with "Unable to load the proper Managed
/// Identity" when more than one identity is available on the resource. This consolidates the
/// canonical pattern from <see cref="Infrastructure.Graph.GraphClientFactory"/> so every
/// Dataverse/Cosmos/OpenAI consumer auths the same way.
///
/// Reads <c>Graph:ManagedIdentity:ClientId</c> first (the canonical Spaarke Auth v2 setting),
/// falling back to <c>ManagedIdentity:ClientId</c> for legacy ExternalAccess-style configurations.
/// If neither is set, returns a <c>DefaultAzureCredential</c> without a pinned clientId — fine
/// for local dev (chains through AzureCliCredential) and single-identity App Services.
///
/// Updated 2026-08-17 (customer-provisioning-orchestration-r1 Wave 4 Batch 4D drift-1 follow-up
/// to task 065): also pins the credential to a specific tenant via
/// <c>DefaultAzureCredentialOptions.TenantId</c> from <c>AZURE_TENANT_ID</c> /
/// <c>TENANT_ID</c> configuration — mirrors the <c>GraphClientFactory:132</c> fix from
/// task 065 (commit <c>f66a6add7</c>). Without an explicit <c>TenantId</c>, the credential
/// silently resolves to the MI host's default tenant which is CATASTROPHIC in a multi-tenant /
/// customer-provisioning scenario (§4D I5 / FR-32). Today this resolves to the same Spaarke
/// tenant (single-tenant BFF) that <see cref="Infrastructure.Graph.GraphClientFactory"/> sees;
/// the assignment is a forcing-function requirement so a future multi-tenant switch is safe from
/// implicit-tenant credential-context bugs.
/// </remarks>
public static class ManagedIdentityCredentialFactory
{
    /// <summary>
    /// Creates a <see cref="DefaultAzureCredential"/> pinned to the UAMI clientId and tenant from
    /// configuration. Unpinned clientId is fine for local dev / single-identity App Services;
    /// unpinned tenant is intentionally left as the DefaultAzureCredential default only when no
    /// <c>AZURE_TENANT_ID</c> / <c>TENANT_ID</c> is configured (local dev without config).
    /// </summary>
    public static TokenCredential Create(IConfiguration configuration)
    {
        var miClientId = ResolveUamiClientId(configuration);

        // customer-provisioning-orchestration-r1 §4D tenant-isolation invariant I5 / FR-32
        // (drift-1 follow-up to task 065): mirror the GraphClientFactory:132 fix — pin the
        // credential to a specific tenant so it does not silently resolve to the MI-host's
        // default tenant. Reads the same AZURE_TENANT_ID / TENANT_ID config keys that
        // GraphClientFactory reads in its ctor (line 53). Today this resolves to the same
        // Spaarke tenant (single-tenant BFF); the assignment is a forcing-function requirement
        // so a future multi-tenant switch is safe from implicit-tenant credential-context bugs.
        var tenantId = configuration["AZURE_TENANT_ID"] ?? configuration["TENANT_ID"];

        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            options.TenantId = tenantId;
        }

        return new DefaultAzureCredential(options);
    }

    /// <summary>
    /// Resolves the User-Assigned Managed Identity clientId from configuration, or <c>null</c> when
    /// none is configured (local dev, or a single-identity App Service).
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="Create"/> at auth-v4 task 020 so the MI-FIC assertion provider
    /// resolves the identity through the <b>same</b> code path as every app-only consumer, per
    /// ADR-028 A4: <i>"MUST obtain the confidential credential from the single shared credential
    /// provider … rather than constructing credentials per call site. Rationale: seven call sites each
    /// rolling their own credential handling is what made the previous state unfixable in one place."</i>
    /// Two copies of this two-line lookup would be two places to fix when the setting moves.
    ///
    /// <para><b>This returns the UAMI's clientId, which is NOT the application's clientId.</b> They are
    /// different identities and confusing them is the project's designated silent-failure hazard
    /// (FR-B4, guarded by task 023): the assertion is <i>minted by</i> the UAMI, while the confidential
    /// client it authenticates is the <i>app registration</i>. The dev subscription also contains five
    /// UAMIs, one of them (<c>spaarke-bff-identity</c>) named as though it were the BFF's without being
    /// attached to it — so resolve by configured id, never by name.</para>
    /// </remarks>
    public static string? ResolveUamiClientId(IConfiguration configuration)
    {
        // Blank is normalised to null, deliberately. IConfiguration returns "" (not null) for a key
        // present-but-empty -- an App Service setting cleared to blank, or "ClientId": "" in JSON --
        // so a bare ?? chain would let an EMPTY canonical key shadow a correctly-set legacy key, and
        // would hand "" to callers as though it were a real identity. Added at task 020 after code
        // review (W-2) found the assertion provider building a *user-assigned* assertion for a blank
        // identity while logging that it was falling back to system-assigned -- a wrong diagnostic in
        // exactly the FR-B4 identity-conflation scenario the log exists to make visible.
        var canonical = configuration["Graph:ManagedIdentity:ClientId"];
        if (!string.IsNullOrWhiteSpace(canonical)) return canonical;

        var legacy = configuration["ManagedIdentity:ClientId"];
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
    }
}
