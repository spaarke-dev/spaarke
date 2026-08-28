// -----------------------------------------------------------------------------
// IArtifactManifestVerifier.cs
//
// L2 abstraction over H9's artifact-resolve + gate-verify step (task 132,
// Wave G-3, DS-4 §5 re-scope). Replaces IR3GateVerifier's dotnet/pwsh
// shell-outs: the r3-era gates (analyzers-as-errors / god-class ratchet /
// tenant-isolation ArchTests / naming-conformance / Graph app-role parity)
// now run in CI (task 116's deploy-bff-api.yml Build job) against the
// platform-BFF artifact BEFORE it is published; this collaborator's job is a
// PURE C# metadata check that reads their recorded results from the
// `latest.json` manifest task 116 publishes to the `provisioning-artifacts`
// blob container — no re-run at provision time (DS-4 §5 "degrades to
// artifact-metadata check" framing).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="ArtifactManifestVerifier"/> downloads +
//       parses latest.json via a shared BlobContainerClient (UAMI RBAC, no
//       stored key).
//     - Test: stubs injected per unit test that construct
//       <see cref="ArtifactManifestVerificationResult"/> directly (see
//       H9BffDeployHandlerTests).
//   Interface earns its keep — no NIH.
//
// HARD-BLOCK CONTRACT (DS-4 §5 / this project's binding rule):
//   VerifyAsync MUST return Rejected — never silently proceed — when: (a) the
//   manifest blob is missing/unparseable, (b) any of the 5 known gate keys is
//   absent from the manifest's `gates` object, (c) any gate value is
//   literally "Failed", or (d) a caller-requested buildId does not match the
//   manifest's buildId (latest.json is a SINGLE mutable pointer — there is no
//   per-historical-build manifest, so a mismatch means this handler has NO
//   verified gate data for the requested build, which is itself a "missing
//   gate results" condition, not a different failure class). This is NOT a
//   warning-and-proceed condition per DS-4 §5 / project CLAUDE.md §10.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Resolves the desired BFF build (run parameter or the latest published
/// manifest) and verifies its CI-recorded r3-gate + size metadata. Production
/// impl reads <c>latest.json</c> from blob storage; test impls return canned
/// results.
/// </summary>
public interface IArtifactManifestVerifier
{
    /// <summary>
    /// Downloads + parses the manifest, resolves the effective buildId, and
    /// applies the hard-block gate rule. Domain failures (missing gates, red
    /// gate, buildId mismatch, unparseable manifest) do NOT throw — they are
    /// carried in <see cref="ArtifactManifestVerificationResult.Rejected"/>.
    /// Infrastructure faults (blob container unreachable, auth failure) MAY
    /// throw.
    /// </summary>
    Task<ArtifactManifestVerificationResult> VerifyAsync(
        ArtifactManifestVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single manifest verification pass.
/// </summary>
/// <param name="RequestedBuildId">
/// The <c>buildId</c> run parameter, if the operator/registry supplied one
/// (e.g. from <c>sprk_bffversion</c>, per DS-4 §5 item 4). Null/empty means
/// "resolve from latest.json" — the manifest's own buildId is used.
/// </param>
public sealed record ArtifactManifestVerificationRequest(string? RequestedBuildId);

/// <summary>
/// Discriminated result of <see cref="IArtifactManifestVerifier.VerifyAsync"/>.
/// </summary>
public abstract record ArtifactManifestVerificationResult
{
    private ArtifactManifestVerificationResult() { }

    /// <summary>Manifest resolved + all gates Passed or Skipped — safe to download + deploy.</summary>
    public sealed record Verified(ArtifactManifest Manifest) : ArtifactManifestVerificationResult;

    /// <summary>
    /// Hard block — <paramref name="Diagnostic"/> is the operator-facing
    /// reason (missing manifest, red gate, missing gate key, or buildId
    /// mismatch). H9 REFUSES to deploy.
    /// </summary>
    public sealed record Rejected(string Diagnostic) : ArtifactManifestVerificationResult;
}

/// <summary>
/// Deserialized <c>latest.json</c> shape — field names + gate-key vocabulary
/// match task 116's <c>deploy-bff-api.yml</c> "Generate + push latest.json
/// manifest" step EXACTLY (buildId/sha/sizeBytes/publishedAt/artifactBlobName +
/// gates.{r3AnalyzersAsErrors,godClassRatchet,archTests,namingConformance,
/// graphAppRoleParity} each one of "Passed"/"Failed"/"Skipped").
/// </summary>
/// <param name="BuildId">CI build identifier (matches the zip's <c>bff-api-{buildId}.zip</c> name).</param>
/// <param name="Sha">Git commit sha the artifact was built from.</param>
/// <param name="SizeBytes">Compressed artifact size in bytes, as measured by CI (NFR-01 cross-check).</param>
/// <param name="ArtifactBlobName">Blob name of the deployable zip (e.g. <c>bff-api-2026.08.19-1.zip</c>).</param>
/// <param name="Gates">Gate-name → status ("Passed"/"Failed"/"Skipped"), exactly as published.</param>
public sealed record ArtifactManifest(
    string BuildId,
    string Sha,
    long SizeBytes,
    string ArtifactBlobName,
    IReadOnlyDictionary<string, string> Gates);
