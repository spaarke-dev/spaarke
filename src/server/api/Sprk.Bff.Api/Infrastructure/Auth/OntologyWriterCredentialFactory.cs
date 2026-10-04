using Azure.Core;
using Azure.Identity;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Builds the app-only credential for the Signal writer's DEDICATED, least-privileged Dataverse identity
/// (<c>mi-ontology-writer-dev</c>, task 006 — owner decision 2026-10-03, task 002 escalation option A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately NOT <see cref="ManagedIdentityCredentialFactory"/>.</b> That factory (and every other BFF
/// credential site) resolves its UAMI client id from <c>Graph:ManagedIdentity:ClientId</c> /
/// <c>ManagedIdentity:ClientId</c> — the keys for the BFF's SYSTEM ADMINISTRATOR identity
/// (<c>mi-bff-api-dev</c> / <c>SDAP-BFF-SPE-API</c>). Reusing either key here would make the Signal writer
/// authenticate as System Administrator, which is exactly what task 006 exists to prevent: Signals and
/// Decision Records are written by a Create-only, least-privileged principal
/// (<c>systemuserid 3121bf1b-9fbf-f111-aaaf-0022482913fc</c>), never by the shared sysadmin client.
/// </para>
/// <para>
/// <b>Config key</b>: <see cref="ConfigKey"/> (<c>Ontology:Writer:ManagedIdentityClientId</c>) — a NEW, separate
/// key, named per <c>notes/006-writer-identity-plan.md</c>. Its value is <c>mi-ontology-writer-dev</c>'s client
/// id, <c>69040982-612e-469e-a85f-26d5172367c5</c> in <c>spaarke-bff-dev</c>.
/// </para>
/// <para>
/// <b>Fail closed.</b> An empty/missing key throws rather than falling back to an unpinned credential or to
/// the shared sysadmin client. On an App Service with more than one managed identity attached (dev has two:
/// <c>mi-bff-api-dev</c> and <c>mi-ontology-writer-dev</c>), an UNPINNED <c>ManagedIdentityCredential</c> fails
/// to resolve at all ("Unable to load the proper Managed Identity") — so there is no silent-wrong-identity
/// failure mode here, only a loud one. This is the behavior task 006's writer-identity plan calls for.
/// </para>
/// <para>
/// <b>Why <see cref="ManagedIdentityCredential"/> directly, not <see cref="DefaultAzureCredential"/>.</b> Every
/// other BFF credential site uses <c>DefaultAzureCredential</c> pinned via
/// <c>DefaultAzureCredentialOptions.ManagedIdentityClientId</c>, which also chains through several other
/// credential types (environment, CLI, …) before reaching managed identity. For the writer there must be
/// exactly one path to a token and no fallback chain to silently land on a different identity in local dev or a
/// misconfigured environment — <see cref="ManagedIdentityId.FromUserAssignedClientId"/> pins the UAMI
/// unambiguously, matching the recipe recorded in <c>notes/006-writer-identity-plan.md</c> (verbatim, task
/// 006's own plan for how task 030 would authenticate).
/// </para>
/// <para>
/// <b>Not yet exercised.</b> No code calls this factory outside the BFF process, and the Kudu SCM container used
/// to verify <c>mi-bff-api-dev</c>'s token path has no <c>IDENTITY_ENDPOINT</c>, so the writer's token
/// acquisition cannot be proven from a workstation or from this sandboxed task-execution environment. It is
/// first exercised when <c>spaarke-bff-dev</c> is next deployed and <see cref="SignalsModule"/>'s DI
/// registration resolves <c>IOntologyWriterDataverseClient</c> on first use (task 006 escalation: if the token
/// acquisition fails, STOP — do not fall back to the shared client).
/// </para>
/// </remarks>
public static class OntologyWriterCredentialFactory
{
    /// <summary>
    /// The Signal writer's dedicated config key. NOT <c>Graph:ManagedIdentity:ClientId</c> or
    /// <c>ManagedIdentity:ClientId</c> — those name the sysadmin identity.
    /// </summary>
    public const string ConfigKey = "Ontology:Writer:ManagedIdentityClientId";

    /// <summary>
    /// Builds the writer's pinned <see cref="TokenCredential"/>. Throws
    /// <see cref="InvalidOperationException"/> when <see cref="ConfigKey"/> is empty — fail closed, never a
    /// silent fallback to the shared Dataverse client (task 006 / task 002 escalation option A).
    /// </summary>
    public static TokenCredential Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clientId = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                $"{ConfigKey} is empty. The Signal writer (task 006's mi-ontology-writer-dev) refuses to " +
                "authenticate without its own pinned managed-identity client id — it MUST NOT fall back to " +
                "the shared Dataverse client, which is System Administrator (task 002 escalation option A). " +
                "Set this App Service setting to the UAMI's client id before the Signal writer can run.");
        }

        return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
}
