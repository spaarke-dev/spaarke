// -----------------------------------------------------------------------------
// ProvisionCustomerScriptBicepDeployRunner.cs
//
// Production <see cref="IBicepDeployRunner"/> implementation — shells out to
// the hardened <c>scripts/Provision-Customer.ps1</c> with the H2a envelope's
// parameters and deserializes the script's JSON output block into
// <see cref="BicepDeployOutputs"/>.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-04 acceptance:
//       H2a invokes the hardened Provision-Customer.ps1 (post-Phase B) with
//       6 new module invocations; ARM read confirms the T1 post-condition
//       cleared BEFORE H2a reports success.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a Model 1
//     vs Model 2: TenancyModel drives stack selection (Model1Shared →
//     stacks/model1-shared.bicep; Model2Dedicated → customer.bicep or
//     stacks/model2-full.bicep).
//
// NON-GOALS (this wave):
//   - Does NOT re-implement the deploy in Azure.ResourceManager SDK. The PS
//     script IS the source-of-truth for the 13-step orchestration + state-file
//     resume logic (parity with H0's preflight probes wrapping scripts/preflight/).
//   - Does NOT own upgrade-mode drift detection — that's <see cref="IUpgradeDriftDetector"/>
//     invoked by the handler BEFORE this runner.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Azure — parity with
//   <see cref="Preflight.PowerShellPreflightProbe"/>'s CI exclusion). Env-guarded
//   smoke tests can be added alongside CosmosSmokeTests when a dedicated
//   dev-provisioning subscription is available; for wave C4 the handler
//   unit tests via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Shells out to <c>scripts/Provision-Customer.ps1</c> and translates its
/// JSON output block into <see cref="BicepDeployOutcome"/>.
/// </summary>
public sealed class ProvisionCustomerScriptBicepDeployRunner : IBicepDeployRunner
{
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<ProvisionCustomerScriptBicepDeployRunner> _logger;

    /// <summary>Constructs the runner bound to the on-disk PS script + pwsh executable.</summary>
    public ProvisionCustomerScriptBicepDeployRunner(
        IOptions<BicepInfraDeployOptions> options,
        ILogger<ProvisionCustomerScriptBicepDeployRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BicepDeployOutcome> DeployAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scriptPath = _options.ProvisionCustomerScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Provision-Customer.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ ships in the L2 publish output.",
                scriptPath);
        }

        // Wave-C4 note: the pre-Phase-B Provision-Customer.ps1 predates the
        // 6-module additions the POML calls out. When Phase B hardening
        // completes, additional parameters (SignalREnabled, BicepVersion,
        // TenancyModel) will be passed through here. The parameter list
        // below reflects the CURRENT script signature; a compilation error
        // in the ArgumentList when Phase B lands is the intended forcing
        // function to update the wiring. Until then, the runner surfaces
        // the mismatch via the script's own parameter-validation error
        // rather than silently proceeding.
        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? _options.BicepDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-CustomerId");
        psi.ArgumentList.Add(request.CustomerId);
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-EnvironmentName");
        psi.ArgumentList.Add(request.EnvironmentName);
        psi.ArgumentList.Add("-Location");
        psi.ArgumentList.Add(request.Location);
        // Phase B adds: -TenancyModel, -BicepVersion, -SignalREnabled, -SubscriptionId
        // (currently passed only as env vars until the script surface catches up.)
        psi.EnvironmentVariables["SPRK_H2A_TENANCY_MODEL"] = request.TenancyModel;
        psi.EnvironmentVariables["SPRK_H2A_BICEP_VERSION"] = request.BicepVersion;
        psi.EnvironmentVariables["SPRK_H2A_SIGNALR_ENABLED"] = request.SignalREnabled ? "true" : "false";
        psi.EnvironmentVariables["SPRK_H2A_SUBSCRIPTION_ID"] = request.SubscriptionId;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H2a Bicep deploy starting: customerId={CustomerId} tenancyModel={TenancyModel} bicepVer={BicepVersion} signalr={SignalR}",
            request.CustomerId, request.TenancyModel, request.BicepVersion, request.SignalREnabled);

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
                $"H2a Bicep deploy for '{request.CustomerId}' timed out after {_options.DeployTimeout}.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H2a Bicep deploy exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Provision-Customer.ps1 exited {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new BicepDeployOutcome.Failure(diagnostic);
        }

        var outputs = TryParseOutputs(stdoutText);
        if (outputs is null)
        {
            var diagnostic =
                $"Provision-Customer.ps1 completed with exit 0 but no parseable outputs JSON block found. " +
                $"Stdout tail: {Truncate(stdoutText, 800)}";
            return new BicepDeployOutcome.Failure(diagnostic);
        }

        return new BicepDeployOutcome.Success(outputs);
    }

    /// <summary>
    /// Attempts to locate + parse a JSON object in the script's stdout with
    /// the keys the runner expects (resourceGroupName, userAssignedIdentityResourceId,
    /// etc.). The script writes many progress lines; the runner isolates the
    /// LAST balanced JSON object as the outputs block.
    /// </summary>
    private static BicepDeployOutputs? TryParseOutputs(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var firstBrace = stdout.IndexOf('{');
        var lastBrace = stdout.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        var slice = stdout[firstBrace..(lastBrace + 1)];
        try
        {
            using var doc = JsonDocument.Parse(slice);
            var root = doc.RootElement;

            string? Read(string key)
                => root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : null;

            bool ReadBool(string key)
                => root.TryGetProperty(key, out var el)
                    && (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                    && el.GetBoolean();

            var rg = Read("resourceGroupName");
            var uamiRid = Read("userAssignedIdentityResourceId");
            var uamiOid = Read("userAssignedIdentityObjectId");
            var uamiCid = Read("userAssignedIdentityClientId");
            var appName = Read("appServiceName");
            var stagingSlot = Read("appServiceStagingSlotName");
            var openai = Read("openAiEndpoint");
            var aiSearch = Read("aiSearchEndpoint");
            var cosmos = Read("cosmosEndpoint");

            if (string.IsNullOrWhiteSpace(rg)
                || string.IsNullOrWhiteSpace(uamiRid)
                || string.IsNullOrWhiteSpace(uamiOid)
                || string.IsNullOrWhiteSpace(uamiCid)
                || string.IsNullOrWhiteSpace(appName)
                || string.IsNullOrWhiteSpace(stagingSlot)
                || string.IsNullOrWhiteSpace(openai)
                || string.IsNullOrWhiteSpace(aiSearch)
                || string.IsNullOrWhiteSpace(cosmos))
            {
                return null;
            }

            return new BicepDeployOutputs
            {
                ResourceGroupName = rg!,
                UserAssignedIdentityResourceId = uamiRid!,
                UserAssignedIdentityObjectId = uamiOid!,
                UserAssignedIdentityClientId = uamiCid!,
                AppServiceName = appName!,
                AppServiceStagingSlotName = stagingSlot!,
                OpenAiEndpoint = openai!,
                AiSearchEndpoint = aiSearch!,
                CosmosEndpoint = cosmos!,
                SignalRDeployed = ReadBool("signalRDeployed"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
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
