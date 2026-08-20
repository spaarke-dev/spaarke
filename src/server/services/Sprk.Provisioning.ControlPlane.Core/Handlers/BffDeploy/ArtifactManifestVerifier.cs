// -----------------------------------------------------------------------------
// ArtifactManifestVerifier.cs
//
// Production <see cref="IArtifactManifestVerifier"/> — task 132 (Wave G-3,
// Option D hybrid / DS-4 §5 re-scope). Downloads + parses task 116's
// `latest.json` manifest via a shared BlobContainerClient (Azure.Storage.Blobs,
// UAMI RBAC, no stored key — parity with H2a's ArmDeploymentRunner
// ResolveArmTemplateJsonAsync, which downloads task 117's ARM-JSON manifest
// from the SAME `provisioning-artifacts` container). PURE C# — zero
// Process-spawning shell-out (replaces DotnetR3GateVerifier's dotnet+pwsh
// shell-outs entirely; the r3 gates themselves now run in CI, not here).
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Reads + verifies task 116's <c>latest.json</c> manifest. See
/// <see cref="IArtifactManifestVerifier"/> for the hard-block contract.
/// </summary>
public sealed class ArtifactManifestVerifier : IArtifactManifestVerifier
{
    /// <summary>
    /// The 5 gate keys task 116's manifest ALWAYS publishes (verbatim key
    /// names). A manifest missing any one of these is treated as "missing
    /// gate results" per the hard-block contract — a malformed/truncated
    /// manifest must not silently pass.
    /// </summary>
    internal static readonly string[] RequiredGateKeys =
    {
        "r3AnalyzersAsErrors",
        "godClassRatchet",
        "archTests",
        "namingConformance",
        "graphAppRoleParity",
    };

    private const string FailedGateValue = "Failed";

    private readonly BlobContainerClient _artifactsContainer;
    private readonly BffDeployOptions _options;
    private readonly ILogger<ArtifactManifestVerifier> _logger;

    public ArtifactManifestVerifier(
        BlobContainerClient artifactsContainer,
        Microsoft.Extensions.Options.IOptions<BffDeployOptions> options,
        ILogger<ArtifactManifestVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(artifactsContainer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _artifactsContainer = artifactsContainer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ArtifactManifestVerificationResult> VerifyAsync(
        ArtifactManifestVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var manifestBlob = _artifactsContainer.GetBlobClient(_options.ArtifactManifestBlobName);

        string manifestJson;
        try
        {
            var response = await manifestBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            manifestJson = response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new ArtifactManifestVerificationResult.Rejected(
                $"Artifact manifest '{_options.ArtifactManifestBlobName}' not found in the " +
                "provisioning-artifacts blob container. H9 refuses to deploy without CI-verified gate " +
                "results — verify task 116's deploy-bff-api.yml workflow has published at least one build " +
                "(or that the platform storage account has been provisioned per the live-ceremony backlog).");
        }

        ArtifactManifest manifest;
        try
        {
            manifest = ParseManifest(manifestJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new ArtifactManifestVerificationResult.Rejected(
                $"Artifact manifest '{_options.ArtifactManifestBlobName}' could not be parsed: " +
                $"{ex.GetType().Name}: {ex.Message}. H9 refuses to deploy against an unverifiable manifest.");
        }

        _logger.LogInformation(
            "H9 manifest resolved: buildId={BuildId} sha={Sha} sizeBytes={SizeBytes} artifactBlobName={ArtifactBlobName}",
            manifest.BuildId, manifest.Sha, manifest.SizeBytes, manifest.ArtifactBlobName);

        if (!string.IsNullOrWhiteSpace(request.RequestedBuildId)
            && !string.Equals(request.RequestedBuildId, manifest.BuildId, StringComparison.Ordinal))
        {
            return new ArtifactManifestVerificationResult.Rejected(
                $"Requested buildId '{request.RequestedBuildId}' does not match the latest published manifest's " +
                $"buildId '{manifest.BuildId}'. latest.json is a SINGLE mutable pointer — H9 has no verified " +
                "gate data for any build other than the one latest.json currently records, so this is treated " +
                "as missing gate results (hard block, not a warning).");
        }

        foreach (var requiredKey in RequiredGateKeys)
        {
            if (!manifest.Gates.TryGetValue(requiredKey, out var status) || string.IsNullOrWhiteSpace(status))
            {
                return new ArtifactManifestVerificationResult.Rejected(
                    $"Manifest for buildId '{manifest.BuildId}' is missing gate result '{requiredKey}'. " +
                    "H9 refuses to deploy against an incomplete gate record.");
            }
        }

        var redGates = manifest.Gates
            .Where(kv => string.Equals(kv.Value, FailedGateValue, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
        if (redGates.Count > 0)
        {
            return new ArtifactManifestVerificationResult.Rejected(
                $"Manifest for buildId '{manifest.BuildId}' reports RED gate(s): {string.Join(", ", redGates)}. " +
                "Slot-swap BLOCKED per DS-4 §5 — fix the underlying issue, push a new build, and resume.");
        }

        return new ArtifactManifestVerificationResult.Verified(manifest);
    }

    /// <summary>
    /// Parses the manifest JSON into <see cref="ArtifactManifest"/>. Throws
    /// <see cref="InvalidOperationException"/> (caught by the caller, mapped
    /// to Rejected) when a required top-level field is absent or the wrong
    /// kind — a malformed manifest is a domain failure, not an infra fault.
    /// </summary>
    private static ArtifactManifest ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var buildId = ReadRequiredString(root, "buildId");
        var sha = ReadRequiredString(root, "sha");
        var artifactBlobName = ReadRequiredString(root, "artifactBlobName");

        if (!root.TryGetProperty("sizeBytes", out var sizeElement)
            || sizeElement.ValueKind != JsonValueKind.Number
            || !sizeElement.TryGetInt64(out var sizeBytes))
        {
            throw new InvalidOperationException("Manifest field 'sizeBytes' is missing or not a number.");
        }

        if (!root.TryGetProperty("gates", out var gatesElement) || gatesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Manifest field 'gates' is missing or not an object.");
        }

        var gates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var gateProperty in gatesElement.EnumerateObject())
        {
            gates[gateProperty.Name] = gateProperty.Value.ValueKind == JsonValueKind.String
                ? gateProperty.Value.GetString() ?? string.Empty
                : string.Empty;
        }

        return new ArtifactManifest(buildId, sha, sizeBytes, artifactBlobName, gates);
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"Manifest field '{propertyName}' is missing or empty.");
        }
        return element.GetString()!;
    }
}
