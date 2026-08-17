// -----------------------------------------------------------------------------
// DeployAllIndexesScriptProvisioner.cs
//
// Production <see cref="IAiSearchIndexProvisioner"/> — shells out to
// <c>scripts/ai-search/Deploy-AllIndexes.ps1</c> with the customer's Model-2
// dedicated AI Search endpoint's environment name and (optionally) a
// filtered <c>-Indexes</c> subset. Auth is DELEGATED to the script's
// operator/UAMI `az` credential chain (ADR-028 MI-outbound; script resolves
// the AI Search admin key via `az search admin-key show` under the operator's
// RBAC — the L2 App Service UAMI must have Search Service Contributor on the
// customer's search service).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05
//     acceptance: 7 canonical indexes per §11.2 catalog + per-index
//     invariant verifier passes. This provisioner runs the DEPLOY step;
//     invariant verification is <see cref="IAiSearchIndexVerifier"/>'s job
//     (called after this).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a
//     Model 2: dedicated per-customer AI Search — this provisioner is
//     Model-2-only (Model 1 branch does not invoke it).
//   - scripts/ai-search/Deploy-AllIndexes.ps1 — the FR-05 / FR-07 catalog
//     authority per task 002 audit § 1.
//
// NON-GOALS (this wave):
//   - Does NOT re-implement the deploy in Azure.Search.Documents SDK. Same
//     rationale as H2a's <see cref="BicepInfraDeploy.ProvisionCustomerScriptBicepDeployRunner"/>:
//     the PS script IS the source-of-truth for the catalog + PUT-idempotent
//     invariant assertion (Invoke-PostDeployVerifier lines 377–487).
//   - Does NOT own retired-index rejection — that's <see cref="H2bAiSearchIndexHandler"/>'s
//     pre-check (rejects on <see cref="AiSearchIndexRejectionCodes.RetiredIndexProvisioningForbidden"/>
//     BEFORE this provisioner runs).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real AI Search — parity with
//   <see cref="BicepInfraDeploy.ProvisionCustomerScriptBicepDeployRunner"/>'s
//   exclusion). Env-guarded smoke tests can be added alongside
//   <c>CosmosSmokeTests</c> when a dedicated dev-provisioning AI Search
//   service is available; for wave C4 the handler unit tests via stubs are
//   the coverage.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Shells out to <c>scripts/ai-search/Deploy-AllIndexes.ps1</c> and translates
/// its exit code / stdout into <see cref="AiSearchIndexProvisionOutcome"/>.
/// </summary>
public sealed class DeployAllIndexesScriptProvisioner : IAiSearchIndexProvisioner
{
    private readonly AiSearchIndexOptions _options;
    private readonly ILogger<DeployAllIndexesScriptProvisioner> _logger;

    /// <summary>Constructs the provisioner bound to the on-disk PS script + pwsh executable.</summary>
    public DeployAllIndexesScriptProvisioner(
        IOptions<AiSearchIndexOptions> options,
        ILogger<DeployAllIndexesScriptProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiSearchIndexProvisionOutcome> ProvisionAsync(
        AiSearchIndexProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scriptPath = _options.DeployAllIndexesScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Deploy-AllIndexes.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ai-search/ ships in the L2 publish output " +
                "(AiSearchIndex:DeployAllIndexesScriptPath app-setting).",
                scriptPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-Environment");
        psi.ArgumentList.Add(request.EnvironmentName);

        // The script defaults to deploying all 7 indexes when -Indexes is
        // absent; only pass a filtered subset when the handler was invoked
        // with a specific catalog. Mapping canonical names → script short keys
        // is out-of-scope for wave C4 (H2b currently always requests all 7);
        // when a subset use case emerges the mapping goes here.
        // Env-var handoff for the AI Search endpoint override (script defaults
        // to spaarke-search-{env}; H2a-provisioned endpoints follow the same
        // convention so no override is needed today, but the env-var is set
        // for parity with H2a's runner + future scripting).
        psi.EnvironmentVariables["SPRK_H2B_SEARCH_ENDPOINT"] = request.SearchEndpoint;
        psi.EnvironmentVariables["SPRK_H2B_CUSTOMER_ID"] = request.CustomerId;
        psi.EnvironmentVariables["SPRK_H2B_TENANT_ID"] = request.TenantId;
        psi.EnvironmentVariables["SPRK_H2B_INDEX_VERSION"] = request.IndexVersion;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H2b AI Search deploy starting: customerId={CustomerId} environment={Environment} endpoint={Endpoint} indexVer={IndexVersion}",
            request.CustomerId, request.EnvironmentName, request.SearchEndpoint, request.IndexVersion);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.DeployTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H2b AI Search deploy for '{request.CustomerId}' timed out after {_options.DeployTimeout} " +
                $"(NFR-12 target &lt; 30 min).");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H2b AI Search deploy exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Deploy-AllIndexes.ps1 exited {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new AiSearchIndexProvisionOutcome.Failure(diagnostic);
        }

        // Script exit 0 = all requested indexes provisioned + post-deploy
        // invariant verifier passed. The handler's separate
        // <see cref="IAiSearchIndexVerifier"/> call re-asserts invariants
        // independently as belt-and-suspenders (the script's verifier can
        // theoretically pass while an out-of-band change breaks an index;
        // rare, but cheap to double-check).
        var provisioned = request.RequestedIndexNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty // handler substitutes the canonical 7
            : request.RequestedIndexNames;
        return new AiSearchIndexProvisionOutcome.Success(provisioned);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between HasExited check + Kill — race is benign.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
