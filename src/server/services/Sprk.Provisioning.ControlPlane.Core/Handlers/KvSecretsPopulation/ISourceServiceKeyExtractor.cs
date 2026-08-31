// -----------------------------------------------------------------------------
// ISourceServiceKeyExtractor.cs
//
// Task 200 — H4-shared source-service key-extraction seam. Reads the current
// cleartext value from an Azure source service (AI Search / Cognitive
// Services / Service Bus / Storage / Redis) so H4-shared can populate the
// shared KV canonical name with the REAL value (not a fabricated placeholder,
// per SESSION 2 F19 finding).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: SdkSourceServiceKeyExtractor — Azure.ResourceManager.*
//       SDK calls per SourceServiceType branch (5 recipes).
//     - Test: per-unit-test fakes returning canned values without any live
//       Azure call — parity with H4's IKvSecretsWriter / IArmKeyVaultRefProbe
//       fake-driven test approach.
//   Interface earns its keep — no NIH.
//
// CLEARTEXT DISCIPLINE (ADR-028 MUST rule):
//   Values pass through this seam ONLY as return values / immediate WriteAsync
//   arguments — NEVER logged. Diagnostics carry canonical names + resource
//   names + status codes ONLY, never the extracted values themselves. The
//   handler wrapping this seam MUST NOT log the return string; only pass it
//   to the KV writer via a channel that also respects the no-log invariant.
//
// FAILURE DISCIPLINE:
//   Domain outcomes (resource not found in RG, auth denied, throttled) are
//   surfaced by throwing <see cref="Azure.RequestFailedException"/> per the
//   Azure SDK convention. H4-shared's HandleAsync wraps that catch at its own
//   boundary and maps to HandlerResult.Failure(QuarantineRequired,
//   "SourceServiceExtractionFailed", diagnostic-naming-the-service). This
//   handler-side wrap is consistent with H4's own writer handling
//   (H4KvSecretsPopulationHandler.cs step (6)).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Extracts the current cleartext value for a manifest entry with
/// <see cref="KvSecretValueSource.FromSharedService"/> by calling the source
/// Azure service directly via the Azure.ResourceManager SDK. Read-only:
/// this seam NEVER writes to the source (rotation is done via Azure Portal /
/// separate operator flow; H4-shared only observes + copies).
/// </summary>
public interface ISourceServiceKeyExtractor
{
    /// <summary>
    /// Reads the current cleartext value from the source service identified by
    /// <paramref name="source"/> living in
    /// <paramref name="subscriptionId"/>/<paramref name="resourceGroupName"/>.
    /// Throws <see cref="Azure.RequestFailedException"/> on any Azure-side
    /// failure — the handler classifies + maps to a HandlerResult.
    /// </summary>
    /// <param name="source">Parsed <c>service_ref</c> — type + resource name.</param>
    /// <param name="subscriptionId">The Azure subscription hosting the source resource.</param>
    /// <param name="resourceGroupName">The resource group hosting the source resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> ExtractAsync(
        SharedKvSecretSource source,
        string subscriptionId,
        string resourceGroupName,
        CancellationToken cancellationToken);
}
