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
/// <b>Shape (amended 2026-10-07, owner — spec.md §6 ADR-028 row).</b> Built the way the central factory builds
/// its credential — a <see cref="DefaultAzureCredential"/> with <c>TenantId</c> pinned from
/// <c>AZURE_TENANT_ID</c> / <c>TENANT_ID</c> (tenant-isolation invariant I5, FR-32) and
/// <c>ManagedIdentityClientId</c> pinned to the writer's own UAMI — but LOCKED to that one identity:
/// <list type="bullet">
///   <item>Every non-managed-identity source is excluded (<see cref="BuildOptions"/>). A unit test enumerates
///   every public <c>Exclude*</c> property of the installed Azure.Identity by reflection, so a source added by
///   a future package version fails the build instead of slipping into the chain.</item>
///   <item><c>AZURE_TOKEN_CREDENTIALS</c> is refused unless it selects managed identity
///   (<see cref="EnsureCredentialSelectionIsManagedIdentityOnly"/>). Measured against Azure.Identity 1.21.0
///   (2026-10-07): that variable OVERRIDES the <c>Exclude*</c> options — with every other source excluded,
///   <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> still yields a chain of exactly
///   <c>[AzureCliCredential]</c>, and <c>EnvironmentCredential</c> likewise. Exclusions alone therefore do NOT
///   close the fallback; this guard does.</item>
/// </list>
/// The result is a chain whose only member is <c>ManagedIdentityCredential</c> for the writer's UAMI: no code
/// path or setting can land the writer on the CLI, the environment's service principal, or the sysadmin UAMI.
/// </para>
/// <para>
/// <b>Fail closed, three ways.</b> An empty <see cref="ConfigKey"/>, an empty tenant, or a non-managed-identity
/// <c>AZURE_TOKEN_CREDENTIALS</c> each throw <see cref="InvalidOperationException"/> rather than produce a
/// credential. The tenant is a deliberate departure from the central factory, which leaves the tenant unpinned
/// when neither key is set (local dev convenience): the writer has no local-dev path at all, and an unpinned
/// credential is what I5 forbids. No deployed BFF lacks <c>TENANT_ID</c> — <c>GraphClientFactory</c> throws at
/// construction without it.
/// </para>
/// <para>
/// <b>Live seam tests are unaffected.</b> <c>SignalWriterSeamTests</c> never calls this factory: it builds its
/// own <see cref="AzureCliCredential"/> in the test fixture and hands <c>OntologyWriterDataverseClient</c> a
/// pre-built connection through that class's <c>internal</c> test constructor. There is no test-only switch in
/// this file, so there is none to enable in production.
/// </para>
/// <para>
/// <b>Not yet exercised.</b> The managed-identity token path only resolves inside an Azure-hosted process with
/// an <c>IDENTITY_ENDPOINT</c>. It is first exercised when <c>spaarke-bff-dev</c> is next deployed and
/// <c>OntologyWriterDataverseClient</c> connects on first use (task 006 escalation: if the token acquisition
/// fails, STOP — do not fall back to the shared client).
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
    /// <c>AZURE_TOKEN_CREDENTIALS</c> values under which the locked options still produce a managed-identity-only
    /// chain (verified against Azure.Identity 1.21.0): <c>prod</c> = environment + workload identity + managed
    /// identity, of which the first two are excluded; <c>ManagedIdentityCredential</c> = managed identity alone.
    /// Unset/blank leaves the options in charge. Everything else — a named developer or environment source,
    /// <c>dev</c>, <c>ManagedIdentityAsFederatedIdentityCredential</c>, or an unknown value — is refused.
    /// </summary>
    private static readonly HashSet<string> ManagedIdentityOnlySelections =
        new(StringComparer.OrdinalIgnoreCase) { "prod", "ManagedIdentityCredential" };

    /// <summary>
    /// Builds the writer's pinned <see cref="TokenCredential"/>. Throws <see cref="InvalidOperationException"/>
    /// when <see cref="ConfigKey"/> or the tenant is empty, or when <c>AZURE_TOKEN_CREDENTIALS</c> would select a
    /// non-managed-identity source — fail closed, never a silent fallback (task 006 / task 002 escalation
    /// option A).
    /// </summary>
    public static TokenCredential Create(IConfiguration configuration)
    {
        var options = BuildOptions(configuration);

        EnsureCredentialSelectionIsManagedIdentityOnly(
            Environment.GetEnvironmentVariable(DefaultAzureCredential.DefaultEnvironmentVariableName));

        return new DefaultAzureCredential(options);
    }

    /// <summary>
    /// The writer's credential options: tenant + writer UAMI pinned, every non-managed-identity source excluded.
    /// <c>internal</c> so the unit tests can inspect the options directly (tests/CLAUDE.md B8 — no reflection
    /// into non-public members).
    /// </summary>
    internal static DefaultAzureCredentialOptions BuildOptions(IConfiguration configuration)
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

        // Same keys, same precedence as ManagedIdentityCredentialFactory.Create (tenant-isolation invariant I5 /
        // FR-32). Unlike that factory, a blank result fails closed — see the class remarks.
        var tenantId = configuration["AZURE_TENANT_ID"] ?? configuration["TENANT_ID"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                "Neither AZURE_TENANT_ID nor TENANT_ID is configured. The Signal writer refuses to build a " +
                "credential that is not pinned to a tenant (tenant-isolation invariant I5, FR-32).");
        }

        var options = new DefaultAzureCredentialOptions
        {
            TenantId = tenantId,
            ManagedIdentityClientId = clientId,

            // The ONE source left in the chain.
            ExcludeManagedIdentityCredential = false,

            // Every other source, excluded. The unit test enumerates the installed package's Exclude* properties
            // by reflection, so a source a future Azure.Identity adds fails that test until it is listed here.
            ExcludeEnvironmentCredential = true,
            ExcludeWorkloadIdentityCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzureCliCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeAzureDeveloperCliCredential = true,
            ExcludeInteractiveBrowserCredential = true,
            ExcludeBrokerCredential = true,
        };

        // Obsolete since Azure.Identity 1.15 (the source no longer joins the chain, and the default is already
        // true), set explicitly anyway so the exclusion is stated rather than inherited from a default.
#pragma warning disable CS0618
        options.ExcludeSharedTokenCacheCredential = true;
#pragma warning restore CS0618

        return options;
    }

    /// <summary>
    /// Refuses an <c>AZURE_TOKEN_CREDENTIALS</c> value that would put a non-managed-identity source in the
    /// writer's chain. That variable overrides <c>DefaultAzureCredentialOptions.Exclude*</c> (measured,
    /// Azure.Identity 1.21.0), so without this check a host-level setting could hand the writer the Azure CLI
    /// login or the environment's service principal.
    /// </summary>
    internal static void EnsureCredentialSelectionIsManagedIdentityOnly(string? credentialSelection)
    {
        if (string.IsNullOrWhiteSpace(credentialSelection)
            || ManagedIdentityOnlySelections.Contains(credentialSelection.Trim()))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{DefaultAzureCredential.DefaultEnvironmentVariableName}='{credentialSelection}' would replace the " +
            "Signal writer's managed identity with a different credential source. The writer authenticates ONLY as " +
            $"its own user-assigned managed identity ({ConfigKey}); unset the variable, or set it to 'prod' or " +
            "'ManagedIdentityCredential'.");
    }
}
