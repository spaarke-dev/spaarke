// -----------------------------------------------------------------------------
// FileBicepTemplateInspector.cs
//
// Production <see cref="IBicepTemplateInspector"/> implementation — reads the
// active Bicep template + module set from disk and applies two structural
// rules:
//   R1 (Redis presence, Q-E FR-12): grep the top-level template + composed
//      modules for `module <name> 'modules/redis.bicep'` references.
//   R2 (Model pinning, ADR-020): parse `openai.bicep` for model deployment
//      version literals; fail if any is `latest`, empty, or missing.
//
// SCOPE:
//   - Production impl. Not exercised by CI unit suite (file I/O — parity with
//     PowerShellPreflightProbe's shell-out exclusion). Unit tests use stubs
//     that return canned <see cref="BicepTemplateInspectionResult"/> instances.
//   - Env-guarded integration coverage can be added alongside CosmosSmokeTests
//     when needed; for wave C4 the handler-level tests are the coverage.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Reads Bicep source files from
/// <see cref="BicepInfraDeployOptions.BicepDirectory"/> + applies the R1
/// (Redis presence) + R2 (model-pin) structural checks.
/// </summary>
public sealed class FileBicepTemplateInspector : IBicepTemplateInspector
{
    // Matches: `module <ident> '<any-path>redis<any-path>' = ...`
    // Handles the two common shapes:
    //   module redis 'modules/redis.bicep' = { ... }
    //   module redisCache 'stacks/../modules/redis.bicep' = { ... }
    private static readonly Regex RedisModuleRegex = new(
        @"^\s*module\s+\w+\s+'[^']*redis[^']*\.bicep'\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Matches the `version:` line inside an `openai.bicep` model deployment
    // block. Captures the version literal (single- or double-quoted).
    private static readonly Regex ModelVersionRegex = new(
        @"version:\s*['""]([^'""]*)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches a deployment `name:` line so the diagnostic can cite which
    // deployment failed the pin check.
    private static readonly Regex DeploymentNameRegex = new(
        @"name:\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // HANDLER-10 (Wave 2 pre-dispatch remediation 2026-08-27) — F16 verbatim.
    // Detects the invalid literal `keyVaultReferenceIdentity: 'SystemAssigned'`
    // pattern. Spaarke templates must assign a UAMI resource-id expression
    // (e.g. `uami.outputs.resourceId` or a param that resolves to a UAMI RID)
    // — the literal 'SystemAssigned' silently breaks every @Microsoft.KeyVault
    // ref at runtime when the App Service has UAMI-only attached (which is
    // the ADR-028 default).
    private static readonly Regex InvalidKvRefIdentityRegex = new(
        @"keyVaultReferenceIdentity\s*:\s*['""]SystemAssigned['""]",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<FileBicepTemplateInspector> _logger;

    /// <summary>Constructs the inspector bound to the on-disk Bicep tree.</summary>
    public FileBicepTemplateInspector(
        IOptions<BicepInfraDeployOptions> options,
        ILogger<FileBicepTemplateInspector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BicepTemplateInspectionResult> InspectAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var templatePath = ResolveTemplatePath(request.TenancyModel);
        if (!File.Exists(templatePath))
        {
            // Configuration / deployment error — matches PowerShellPreflightProbe
            // behavior: fail loud with a specific FileNotFoundException so the
            // operator fixes the deploy, not silently treating the check as
            // passed (spec.md NFR-05 fail-fast).
            throw new FileNotFoundException(
                $"Bicep template for tenancy model '{request.TenancyModel}' not found at '{templatePath}'. " +
                $"Verify BicepInfraDeploy:BicepDirectory ('{_options.BicepDirectory}') resolves correctly for the deployed L2 layout.",
                templatePath);
        }

        // R1: Redis presence — walks the top-level template + composed
        // modules (`stacks/*.bicep`, `customer.bicep`) and any *.bicep in the
        // BicepDirectory recursively. The strict rule is "no per-customer
        // Redis at all"; scanning every reachable file is cheaper than
        // walking Bicep's include graph.
        var (redisFound, redisRef) = await ScanForRedisAsync(cancellationToken).ConfigureAwait(false);

        // R2: Model version pinning — reads openai.bicep specifically. If
        // the file is missing (Model 1 shared stack may consume the platform
        // OpenAI), skip the check silently.
        var (unpinnedFound, unpinnedRef) = await ScanForUnpinnedModelsAsync(cancellationToken).ConfigureAwait(false);

        // R3 (HANDLER-10 / Wave 2 pre-dispatch remediation 2026-08-27) — F16:
        // detect the invalid `keyVaultReferenceIdentity: 'SystemAssigned'`
        // literal anywhere under BicepDirectory.
        var (kvRefIdentityInvalid, kvRefIdentityRef) = await ScanForInvalidKvRefIdentityAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "BicepTemplateInspector: tenancyModel={TenancyModel} redisFound={RedisFound} unpinnedFound={UnpinnedFound} kvRefIdentityInvalid={KvRefIdentityInvalid}",
            request.TenancyModel, redisFound, unpinnedFound, kvRefIdentityInvalid);

        return new BicepTemplateInspectionResult(
            ContainsRedisResource: redisFound,
            RedisReference: redisRef,
            HasUnpinnedModelDeployment: unpinnedFound,
            UnpinnedModelReference: unpinnedRef,
            HasInvalidKvRefIdentity: kvRefIdentityInvalid,
            KvRefIdentityReference: kvRefIdentityRef);
    }

    private async Task<(bool Found, string Reference)> ScanForInvalidKvRefIdentityAsync(CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(_options.BicepDirectory, "*.bicep", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var match = InvalidKvRefIdentityRegex.Match(text);
            if (match.Success)
            {
                var relPath = Path.GetRelativePath(_options.BicepDirectory, file);
                return (true, $"{relPath}: {match.Value.Trim()}");
            }
        }
        return (false, string.Empty);
    }

    private string ResolveTemplatePath(string tenancyModel)
        => string.Equals(tenancyModel, "Model1Shared", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_options.BicepDirectory, "stacks", "model1-shared.bicep")
            : Path.Combine(_options.BicepDirectory, "customer.bicep");

    private async Task<(bool Found, string Reference)> ScanForRedisAsync(CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(_options.BicepDirectory, "*.bicep", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var match = RedisModuleRegex.Match(text);
            if (match.Success)
            {
                // Prefer a compact citation the operator can grep for.
                var relPath = Path.GetRelativePath(_options.BicepDirectory, file);
                return (true, $"{relPath}: {match.Value.Trim()}");
            }
        }
        return (false, string.Empty);
    }

    private async Task<(bool Found, string Reference)> ScanForUnpinnedModelsAsync(CancellationToken cancellationToken)
    {
        var openaiFile = Path.Combine(_options.BicepDirectory, "modules", "openai.bicep");
        if (!File.Exists(openaiFile))
        {
            return (false, string.Empty);
        }

        var text = await File.ReadAllTextAsync(openaiFile, cancellationToken).ConfigureAwait(false);
        var versionMatches = ModelVersionRegex.Matches(text);
        var deploymentMatches = DeploymentNameRegex.Matches(text);

        foreach (Match m in versionMatches)
        {
            var version = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(version)
                || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
            {
                // Find the nearest preceding `name:` for citation context.
                var nameMatch = FindPrecedingMatch(deploymentMatches, m.Index);
                var deploymentName = nameMatch?.Groups[1].Value ?? "(unknown)";
                return (true, $"modules/openai.bicep: deployment '{deploymentName}' version '{version}'");
            }
        }
        return (false, string.Empty);
    }

    private static Match? FindPrecedingMatch(MatchCollection matches, int position)
    {
        Match? best = null;
        foreach (Match m in matches)
        {
            if (m.Index < position && (best is null || m.Index > best.Index))
            {
                best = m;
            }
        }
        return best;
    }
}
