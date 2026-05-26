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
/// </remarks>
public static class ManagedIdentityCredentialFactory
{
    /// <summary>
    /// Creates a <see cref="DefaultAzureCredential"/> pinned to the UAMI clientId from
    /// configuration, or an unpinned credential if no clientId is configured.
    /// </summary>
    public static TokenCredential Create(IConfiguration configuration)
    {
        var miClientId = configuration["Graph:ManagedIdentity:ClientId"]
            ?? configuration["ManagedIdentity:ClientId"];

        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }

        return new DefaultAzureCredential(options);
    }
}
