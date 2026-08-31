// -----------------------------------------------------------------------------
// IPerEnvSettingsManifest.cs
//
// Task 201 — Reader abstraction over the Phase H canonical secret-catalog
// manifest's NEW top-level `per_env_settings:` list (added task 201 alongside
// the existing `secrets:` list). Sibling of IKvSecretManifest (task 084/126) —
// intentionally split so per-env LITERAL settings (URIs, public GUIDs,
// generated signing keys) do not muddle the secrets-focused KV manifest
// contract.
//
// PURPOSE (per H4b):
//   H4b BulkAppSettings handler resolves each per_env_settings entry from
//   HandlerEnvelope.Parameters.NonSecret via the entry's `per_env_source`, then
//   shells to the generated Configure-AppServiceSettings.generated.ps1 script
//   which writes ALL settings (KV-refs + per-env literals) in ONE batched
//   `az webapp config appsettings set --settings @settings` call → ONE App
//   Service restart cycle.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="FilePerEnvSettingsManifest"/> — reads the SAME
//       embedded manifest.yaml resource IKvSecretManifest already embeds
//       (single source of truth per task 084's contract).
//     - Tests: hand-rolled fakes in
//       H4bBulkAppSettingsHandlerTests.FakePerEnvSettingsManifest.
//   Split from IKvSecretManifest rather than shoehorning per-env entries onto
//   KvSecretEntry / KvSecretManifestReadResult because per-env-literal settings
//   have a DIFFERENT contract (no vault target, no rotation, no ADR-028
//   cleartext-never-in-handler rule — literals are cleartext BY DEFINITION),
//   and widening KvSecretEntry to hold both flavors would corrupt the
//   secrets-focused contract every other H4/H4-shared reader consumes.
//
// THREAD-SAFETY:
//   Implementations MUST be thread-safe (Singleton lifetime). The manifest is
//   effectively immutable for the lifetime of the L2 process — reload requires
//   a redeploy (parity with IKvSecretManifest + H12a ISeedManifestReader).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Reads the per-env-literal app-settings list from the canonical secret-catalog
/// manifest (scripts/canonical-secret-catalog/manifest.yaml `per_env_settings:`
/// top-level list, added task 201). Consumed by
/// <see cref="H4bBulkAppSettingsHandler"/>.
/// </summary>
public interface IPerEnvSettingsManifest
{
    /// <summary>
    /// Loads + returns the per-env-settings entries. Returns a
    /// <see cref="PerEnvSettingsManifestReadResult.Success"/> on success (with
    /// possibly empty <c>Entries</c> when the manifest omits the list) or a
    /// <see cref="PerEnvSettingsManifestReadResult.Failure"/> with an
    /// operator-facing diagnostic when the manifest source is unreadable.
    /// Domain outcomes (success / no entries) never throw; only genuine
    /// infra faults (I/O) throw so the handler can classify per §4C.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PerEnvSettingsManifestReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One per_env_settings entry — one App Service app-setting the H4b handler
/// resolves + emits.
/// </summary>
/// <param name="Key">
/// App Service app-setting name (double-underscore for nested .NET config,
/// e.g. <c>SpeAdmin__KeyVaultUri</c>).
/// </param>
/// <param name="PerEnvSource">
/// Parsed source: <see cref="PerEnvSettingSource.Literal"/> means the value
/// comes from <see cref="LiteralValue"/>; every other kind means the value
/// comes from <c>envelope.Parameters.NonSecret[<see cref="ParameterKey"/>]</c>.
/// </param>
/// <param name="LiteralValue">
/// Populated iff <see cref="PerEnvSource"/> is <see cref="PerEnvSettingSource.Literal"/>.
/// Null otherwise.
/// </param>
/// <param name="ParameterKey">
/// Populated for every non-literal source (the key H4b looks up in
/// <c>envelope.Parameters.NonSecret</c>). Null for literals.
/// </param>
/// <param name="Required">
/// When true, a missing / empty resolved value = Resumable Failure BEFORE
/// any script call. When false, missing = skip entry silently. Defaults true
/// per manifest schema.
/// </param>
/// <param name="IOptionsModuleName">
/// Documentation only — which BFF IOptions module consumes this app-setting.
/// H4b includes this in its diagnostic on missing-input failures so operators
/// can trace fail-fast root cause immediately.
/// </param>
public sealed record PerEnvSettingEntry(
    string Key,
    PerEnvSettingSource PerEnvSource,
    string? LiteralValue,
    string? ParameterKey,
    bool Required,
    string IOptionsModuleName);

/// <summary>
/// Parsed form of the manifest's <c>per_env_source</c> string.
/// </summary>
public enum PerEnvSettingSource
{
    /// <summary>Value embedded verbatim in <see cref="PerEnvSettingEntry.LiteralValue"/>.</summary>
    Literal = 1,

    /// <summary>Value comes from an upstream handler's InterStepState output (envelope.Parameters.NonSecret).</summary>
    FromHandlerOutput = 2,

    /// <summary>Value comes from an operator/API-supplied run parameter (envelope.Parameters.NonSecret).</summary>
    FromHandlerParameter = 3,
}

/// <summary>
/// Discriminated result of <see cref="IPerEnvSettingsManifest.ReadAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record PerEnvSettingsManifestReadResult
{
    private PerEnvSettingsManifestReadResult() { }

    /// <summary>Manifest read OK — entries in canonical (alphabetical-by-Key) order.</summary>
    public sealed record Success(IReadOnlyList<PerEnvSettingEntry> Entries) : PerEnvSettingsManifestReadResult;

    /// <summary>Manifest read failed — operator-facing diagnostic.</summary>
    public sealed record Failure(string Diagnostic) : PerEnvSettingsManifestReadResult;
}
