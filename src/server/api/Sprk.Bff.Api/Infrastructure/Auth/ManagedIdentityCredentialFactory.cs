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
        var miClientId = configuration["Graph:ManagedIdentity:ClientId"]
            ?? configuration["ManagedIdentity:ClientId"];

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
}
