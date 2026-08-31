// -----------------------------------------------------------------------------
// CosmosModule.cs
//
// L2 CONTROL-PLANE COSMOS DB composition (task 037).
//
// SPEC / ADR references:
//   - spec.md FR-27:  Cosmos DB `spaarke-provisioning` database, `runs`
//                     container (partition `/customerId`, TTL 365d). Secrets
//                     in `parameters` stored as KV URI refs only (never
//                     cleartext; enforced structurally by the KeyVaultSecretRef
//                     POCO — no `Value` string — and by the task 025 ArchTest).
//   - spec.md FR-30 / §4D I3: EVERY Cosmos read/write MUST include the
//                     partition-key predicate; new ArchTest in Wave C6 scans
//                     for SDK calls without explicit `PartitionKey` and fails
//                     the build on hit. Write compliant code from day 1 —
//                     never reach for cross-partition queries in this module.
//   - spec.md FR-23 I5: same-customer runs serialize via optimistic
//                     concurrency. The Cosmos-side ETag guard on
//                     ReplaceRunAsync is a load-bearing signal for the
//                     endpoint layer (Wave C5) to return HTTP 409 with the
//                     current stored run.
//   - ADR-028:        DefaultAzureCredential (UAMI-pinned via
//                     ManagedIdentity:ClientId app-setting) — NEVER account-
//                     key credential. Matches `disableLocalAuth: true` set on
//                     the account by cosmos-provisioning.bicep + the Cosmos
//                     DB Built-in Data Contributor RBAC granted to the L2
//                     App Service MI by task 030.
//   - ADR-010:        Feature-module extension method keeps Program.cs at
//                     ~15 non-framework DI lines (NFR-07 god-class ratchet).
//   - ADR-032:        No conditional service branches in this module —
//                     Cosmos is UNCONDITIONALLY registered because every L2
//                     endpoint (Wave C5) and the reconciler (Wave C5) depend
//                     on it. If a future feature-gates a specific handler,
//                     apply the Null-Object kill-switch pattern (P1/P2/P3)
//                     to THAT handler, never to the CosmosClient itself.
//
// PATTERN parity with BFF: mirrors `AiPersistenceModule` — TokenCredential
// singleton fed to CosmosClientBuilder, ItemRequestOptions.IfMatchEtag for
// ETag concurrency, System.Text.Json camelCase policy. Deliberately does NOT
// reference the BFF assembly (project MUST rule — L2 is a PEER service).
//
// SCOPE (task 037):
//   - Register central `TokenCredential` (matches BFF's ManagedIdentity-
//     Credential factory pattern; separate L2-owned copy per ADR-010).
//   - Register `CosmosClient` singleton (thread-safe; owns connection pool).
//   - Register `IProvisioningRunRepository` -> Cosmos implementation (Scoped).
//   - No Service Bus wiring (task 038), no App Insights wiring (task 039), no
//     handler implementations (Wave C5).
//
// FAIL-FAST config validation: reading `Cosmos:AccountEndpoint` at startup
// throws if unset. The scaffold `appsettings.template.json` carries an
// unset placeholder so a fresh checkout fails loud; deployed environments
// bind via App Service configuration + `platform-controlplane.bicep`.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for the L2 control-plane Cosmos client + the
/// <see cref="IProvisioningRunRepository"/> abstraction over the
/// <c>spaarke-provisioning/runs</c> container.
/// </summary>
public static class CosmosModule
{
    /// <summary>Configuration section that carries account endpoint, database, and container names.</summary>
    public const string ConfigSection = "Cosmos";

    /// <summary>Default database name locked by design (see cosmos-provisioning.bicep).</summary>
    public const string DefaultDatabaseName = "spaarke-provisioning";

    /// <summary>Default container name locked by design (see cosmos-provisioning.bicep).</summary>
    public const string DefaultContainerName = "runs";

    /// <summary>
    /// Registers <see cref="TokenCredential"/> (UAMI-pinned), <see cref="CosmosClient"/>,
    /// and <see cref="IProvisioningRunRepository"/> with the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound configuration (reads the <c>Cosmos</c> section + optional <c>ManagedIdentity:ClientId</c>).</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup if <c>Cosmos:AccountEndpoint</c> is missing or empty — NFR-05 fail-fast contract.
    /// </exception>
    public static IServiceCollection AddCosmosModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail-fast — no partial start-up on missing endpoint (NFR-05).
        var endpoint = configuration[$"{ConfigSection}:AccountEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                $"Configuration '{ConfigSection}:AccountEndpoint' is not set. " +
                "The L2 control-plane requires the fleet-scoped Cosmos account endpoint " +
                "(e.g. https://cosmos-spaarke-platform-dev.documents.azure.com:443/). " +
                "In deployed environments this is bound via App Service settings + Bicep; " +
                "for local dev, copy appsettings.template.json to appsettings.json and set the value.");
        }

        var databaseName = configuration[$"{ConfigSection}:DatabaseName"] ?? DefaultDatabaseName;
        var containerName = configuration[$"{ConfigSection}:ContainerName"] ?? DefaultContainerName;

        // Central TokenCredential — UAMI-pinned when `ManagedIdentity:ClientId`
        // is set (deployed L2 App Service — required because App Services can
        // have multiple MIs attached and the SDK cannot disambiguate without
        // an explicit clientId; mirrors BFF's ManagedIdentityCredentialFactory
        // per ADR-028). Local dev with no clientId chains through
        // AzureCliCredential via DefaultAzureCredential — no code change
        // required to switch environments.
        //
        // Registered as a singleton so downstream consumers (Cosmos, and in
        // task 038/039 Service Bus + Azure Monitor exporter) share the same
        // credential instance -> token cache reuse.
        services.AddSingleton<TokenCredential>(sp =>
        {
            var miClientId = configuration["ManagedIdentity:ClientId"];
            var options = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(miClientId))
            {
                options.ManagedIdentityClientId = miClientId;
            }
            return new DefaultAzureCredential(options);
        });

        // CosmosClient — singleton per Cosmos SDK guidance (thread-safe;
        // owns the connection pool; long-lived across the App Service
        // lifetime). Uses TokenCredential (RBAC data plane) so master keys
        // are never touched — matches cosmos-provisioning.bicep's
        // `disableLocalAuth: true`.
        //
        // Serializer: camelCase — the Cosmos SDK's DEFAULT serializer here is
        // Newtonsoft-based (no custom System.Text.Json CosmosSerializer is
        // registered on this CosmosClientBuilder). Every POCO in Models/
        // carries explicit STJ [JsonPropertyName] attributes, but those are
        // NOT honored on the actual write path — Newtonsoft silently ignores
        // them. Property-name AND enum-value fidelity are achieved instead
        // via matching Newtonsoft attributes on the POCOs themselves
        // ([Newtonsoft.Json.JsonProperty("id")] on RunId; dual STJ+Newtonsoft
        // [JsonConverter(StringEnumConverter)] on RunStatus/GateState/
        // QuarantineState — task 106 / DS-5 §C4.5, mirroring the #19/#20
        // precedent). This CamelCase policy is a defensive default for any
        // ad-hoc type that lacks explicit attributes (e.g. future query
        // projections), NOT the mechanism the documented POCOs rely on.
        //
        // Throttling: 30s max retry wait, 9 attempts — same tolerance as
        // AiPersistenceModule (BFF pattern). Serverless Cosmos rate-limits
        // are unlikely at L2 volumes but the retry policy costs nothing.
        //
        // ConnectionMode: Direct — 429-recovery latency is materially better
        // than Gateway mode on serverless accounts; matches BFF pattern.
        services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            return new CosmosClientBuilder(endpoint, credential)
                .WithSerializerOptions(new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                })
                .WithConnectionModeDirect()
                .WithThrottlingRetryOptions(
                    maxRetryWaitTimeOnThrottledRequests: TimeSpan.FromSeconds(30),
                    maxRetryAttemptsOnThrottledRequests: 9)
                .Build();
        });

        // Repository — Scoped: CosmosClient itself is Singleton, but Scoped
        // per HTTP request matches the ADR-010 default (predictable
        // lifetime; safe to inject IHttpContextAccessor / correlation id in a
        // future task).
        services.AddScoped<IProvisioningRunRepository>(sp =>
            new CosmosProvisioningRunRepository(
                cosmosClient: sp.GetRequiredService<CosmosClient>(),
                databaseName: databaseName,
                containerName: containerName,
                logger: sp.GetRequiredService<ILogger<CosmosProvisioningRunRepository>>()));

        return services;
    }
}
