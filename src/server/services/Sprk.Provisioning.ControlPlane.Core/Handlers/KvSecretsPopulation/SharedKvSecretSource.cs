// -----------------------------------------------------------------------------
// SharedKvSecretSource.cs
//
// Task 200 — H4-shared source-of-extraction primitive. Parses a manifest
// entry's `service_ref` string (format `<type>:<az-resource-name>`) into a
// typed record consumed by ISourceServiceKeyExtractor.
//
// FORMAT (locked by task 084 Phase A generator regex — see
// scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 § Test-ManifestShape):
//     ^[a-z][a-z0-9-]*:[A-Za-z0-9][A-Za-z0-9-]*$
// Examples:
//   search:sprksharedprod-search
//   cognitiveservices:sprksharedprod-openai
//   cognitiveservices:sprksharedprod-docintel
//   servicebus:sprksharedprod-servicebus
//   storage:sprksharedprodsa
//   redis:sprksharedprod-redis
//
// The prefix is a stable enum-mapped domain (SourceServiceType) — new
// source-types MUST land here + on the extractor together (POML escalation
// trigger: "If a new source-type is proposed later ... but no
// SdkSourceServiceKeyExtractor recipe exists for it, STOP and escalate").
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Parsed <c>service_ref</c> for a manifest entry with
/// <see cref="KvSecretValueSource.FromSharedService"/>. Assembled by
/// <see cref="H4SharedKvSecretsPopulationHandler"/> from
/// <see cref="KvSecretEntry.ServiceRef"/> + immutable via record semantics.
/// </summary>
/// <param name="SourceType">Which Azure source service the extractor must call.</param>
/// <param name="ResourceName">The Azure resource name (e.g. <c>sprksharedprod-search</c>) — never a resource ID; resource IDs are composed by the extractor from run-parameter subscription + resource-group + this name.</param>
/// <param name="RawServiceRef">The original <c>service_ref</c> string for audit-log diagnostics (never a secret value).</param>
public sealed record SharedKvSecretSource(
    SourceServiceType SourceType,
    string ResourceName,
    string RawServiceRef)
{
    /// <summary>
    /// Parses <paramref name="serviceRef"/> (format
    /// <c>&lt;type&gt;:&lt;az-resource-name&gt;</c>). Returns <c>false</c> +
    /// leaves <paramref name="parsed"/> null on any malformed input; the caller
    /// (handler) maps false → HandlerResult.Failure with a machine-stable
    /// rejection code. Never throws.
    /// </summary>
    public static bool TryParse(
        string? serviceRef,
        [NotNullWhen(true)] out SharedKvSecretSource? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(serviceRef)) return false;

        var colonIdx = serviceRef.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= serviceRef.Length - 1) return false;

        var typePart = serviceRef[..colonIdx];
        var namePart = serviceRef[(colonIdx + 1)..];
        if (string.IsNullOrWhiteSpace(typePart) || string.IsNullOrWhiteSpace(namePart)) return false;

        if (!TryMapType(typePart, out var sourceType)) return false;

        parsed = new SharedKvSecretSource(sourceType, namePart, serviceRef);
        return true;
    }

    private static bool TryMapType(string typePart, out SourceServiceType sourceType)
    {
        switch (typePart)
        {
            case "search":
                sourceType = SourceServiceType.AiSearchAdminKey;
                return true;
            case "cognitiveservices":
                sourceType = SourceServiceType.CognitiveServicesKey1;
                return true;
            case "servicebus":
                sourceType = SourceServiceType.ServiceBusRootSas;
                return true;
            case "storage":
                sourceType = SourceServiceType.StorageConnectionString;
                return true;
            case "redis":
                sourceType = SourceServiceType.RedisComposed;
                return true;
            default:
                sourceType = default;
                return false;
        }
    }
}

/// <summary>
/// The five source-service extraction recipes H4-shared supports today
/// (task 200 F19 automation scope). Adding a new value REQUIRES a matching
/// branch in <see cref="ISourceServiceKeyExtractor"/> implementations +
/// updated escalation-trigger review — never manifest-only.
/// </summary>
public enum SourceServiceType
{
    /// <summary><c>search:</c> prefix. AI Search admin PrimaryKey — <c>SearchServiceResource.GetAdminKeysAsync()</c>.</summary>
    AiSearchAdminKey = 1,

    /// <summary><c>cognitiveservices:</c> prefix. Cognitive Services Key1 — <c>CognitiveServicesAccountResource.GetKeysAsync().Key1</c> (Azure OpenAI + Document Intelligence).</summary>
    CognitiveServicesKey1 = 2,

    /// <summary><c>servicebus:</c> prefix. Service Bus RootManageSharedAccessKey PrimaryConnectionString — namespace-level auth rule.</summary>
    ServiceBusRootSas = 3,

    /// <summary><c>storage:</c> prefix. Storage account key1 composed into a full DefaultEndpointsProtocol connection string.</summary>
    StorageConnectionString = 4,

    /// <summary><c>redis:</c> prefix. Redis PrimaryKey composed with host + port into a StackExchange.Redis-compatible connection string.</summary>
    RedisComposed = 5,
}
