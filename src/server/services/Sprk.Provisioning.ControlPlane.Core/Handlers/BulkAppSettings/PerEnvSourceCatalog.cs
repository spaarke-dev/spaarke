// -----------------------------------------------------------------------------
// PerEnvSourceCatalog.cs
//
// Task 245a (G25 — run-context contract). The CLOSED set of non-literal
// `per_env_source` values a manifest `per_env_settings` entry may use, and how
// H4b resolves each one from the run.
//
// Before this catalog, H4b looked every source key up in run.Parameters.NonSecret
// (`kv_vault_uri`, `cosmos_endpoint`, `tenant_id`, …) — keys nothing ever wrote,
// so every real run failed H4b with per-env-input-missing. Each source now names
// exactly where its value lives: a typed InterStepState property written by a
// named handler, an intake parameter (IntakeParameterCatalog), or — for
// `from-intake-parameter:customer_id` (task 238) — the run's own customerId,
// which is intake too (the POST /api/runs body field) but lives on
// run.CustomerId rather than in the nonSecretParameters map.
//
// An unknown source, or a known source key with the wrong origin prefix, fails
// the manifest read (FilePerEnvSettingsManifest) — the same closed-vocabulary
// discipline T226 applied to the secret manifest's value_source.
//
// The source KEY (the part after the colon) also names the generated
// Configure-AppServiceSettings script parameter (PascalCase — the operator
// artifact H4b's settings are parity-tested against, task 253), so it must
// stay stable once used in the manifest.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>One accepted non-literal <c>per_env_source</c>.</summary>
/// <param name="Expression">The full manifest string, e.g. <c>from-h2a-output:kv_vault_uri</c>.</param>
/// <param name="SourceKey">The part after the colon — also the generated script parameter's base name.</param>
/// <param name="ProducerHandlerId">The handler whose output this is, or <c>null</c> for an intake value.</param>
/// <param name="Location">Where the value lives, for diagnostics (e.g. <c>InterStepState.KeyVaultUri</c>).</param>
/// <param name="Resolve">Reads the value from the run; null/blank when not (yet) present.</param>
public sealed record PerEnvSource(
    string Expression,
    string SourceKey,
    string? ProducerHandlerId,
    string Location,
    Func<ProvisioningRun, string?> Resolve);

/// <summary>
/// The closed set of non-literal <c>per_env_source</c> values H4b can resolve.
/// </summary>
public static class PerEnvSourceCatalog
{
    private static readonly PerEnvSource[] Entries =
    [
        Output("from-h2a-output:kv_vault_uri", HandlerIds.H2a, nameof(InterStepState.KeyVaultUri), r => r.InterStepState.KeyVaultUri),
        Output("from-h2a-output:cosmos_endpoint", HandlerIds.H2a, nameof(InterStepState.CosmosEndpoint), r => r.InterStepState.CosmosEndpoint),
        Output("from-h2a-output:uami_client_id", HandlerIds.H2a, nameof(InterStepState.MiClientId), r => r.InterStepState.MiClientId),
        Output("from-h2a-output:service_bus_fqns", HandlerIds.H2a, nameof(InterStepState.ServiceBusFullyQualifiedNamespace), r => r.InterStepState.ServiceBusFullyQualifiedNamespace),
        // T242: the stamp's Azure Managed Redis endpoint (host:10000) — a plain setting; the cache has no keys.
        Output("from-h2a-output:redis_endpoint", HandlerIds.H2a, nameof(InterStepState.RedisEndpoint), r => r.InterStepState.RedisEndpoint),
        // T246: the stamp's Azure AI Content Safety endpoint — a plain setting; the account has local auth disabled.
        Output("from-h2a-output:content_safety_endpoint", HandlerIds.H2a, nameof(InterStepState.ContentSafetyEndpoint), r => r.InterStepState.ContentSafetyEndpoint),
        Output("from-h3-output:bff_app_client_id", HandlerIds.H3, nameof(InterStepState.BffAppRegId), r => r.InterStepState.BffAppRegId),
        // T245b: the Dataverse environment URL — a plain app setting (it was a KV secret H4 could never
        // write: H5 runs after H4). H4b ← H5 in the DAG.
        Output("from-h5-output:dataverse_env_url", HandlerIds.H5, nameof(InterStepState.DataverseEnvUrl), r => r.InterStepState.DataverseEnvUrl),
        // T227c (G18): the customer's SPE container — a plain setting (EmailProcessing__DefaultContainerId,
        // Communication__ArchiveContainerId). It was a KV secret nothing could write (H8 runs after H4). H4b ← H8.
        Output("from-h8-output:spe_container_id", HandlerIds.H8, nameof(InterStepState.SpeContainerId), r => r.InterStepState.SpeContainerId),
        Intake("from-intake-parameter:tenant_id", IntakeParameterCatalog.TenantId),
        Intake("from-intake-parameter:container_type_id", IntakeParameterCatalog.ContainerTypeId),
        // T254: the OPTIONAL monthly OpenAI spend limit (G37) — read by a `required: false` entry, so a run without it
        // writes nothing (no limit).
        Intake("from-intake-parameter:openai_monthly_limit_usd", IntakeParameterCatalog.OpenAiMonthlyLimitUsd),
        // T238 (D-14): the run's own customerId — the POST /api/runs body field, validated there by
        // CustomerIdStandard. Read verbatim: no trim, no case change, no derivation (INCOMING-CUSTOMER-
        // RUNTIME-IDENTITY §1.1 — a second derivation is how two components end up with two spellings).
        new("from-intake-parameter:customer_id", "customer_id", null,
            "intake customerId (POST /api/runs body — run.CustomerId)", r => r.CustomerId),
    ];

    /// <summary>Accepted sources, by source key (the part after the colon; ordinal).</summary>
    public static IReadOnlyDictionary<string, PerEnvSource> BySourceKey { get; } =
        Entries.ToDictionary(e => e.SourceKey, StringComparer.Ordinal);

    /// <summary>Every accepted source.</summary>
    public static IReadOnlyList<PerEnvSource> All => Entries;

    /// <summary>
    /// Looks up a full <c>per_env_source</c> expression. False when the source key is unknown
    /// OR the expression's origin prefix differs from the catalog's (a mislabelled origin is as
    /// wrong as an unknown key — it misstates which handler must run first).
    /// </summary>
    public static bool TryGet(string expression, out PerEnvSource source)
    {
        source = null!;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var colon = expression.IndexOf(':');
        if (colon < 0 || colon == expression.Length - 1) return false;
        var key = expression[(colon + 1)..];
        if (!BySourceKey.TryGetValue(key, out var found)) return false;
        if (!string.Equals(found.Expression, expression, StringComparison.Ordinal)) return false;
        source = found;
        return true;
    }

    private static PerEnvSource Output(string expression, string producer, string property, Func<ProvisioningRun, string?> resolve)
        => new(expression, KeyOf(expression), producer, $"InterStepState.{property} ({producer} output)", resolve);

    // Trimmed like H4 and H8 read the same intake values, so one stray space cannot make H4b's
    // app setting differ from the KV secret and the SPE container type.
    private static PerEnvSource Intake(string expression, string intakeKey)
        => new(expression, KeyOf(expression), null, $"intake parameter '{intakeKey}'",
            r => r.Parameters.NonSecret.TryGetValue(intakeKey, out var v) ? v?.Trim() : null);

    private static string KeyOf(string expression) => expression[(expression.IndexOf(':') + 1)..];
}
