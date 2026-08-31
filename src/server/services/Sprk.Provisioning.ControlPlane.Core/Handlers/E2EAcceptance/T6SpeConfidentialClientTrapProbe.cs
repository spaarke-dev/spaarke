// -----------------------------------------------------------------------------
// T6SpeConfidentialClientTrapProbe.cs
//
// Task 175 (Wave G-7, pipelined with H8 / task 131) -- REAL T6 probe replacing
// the single-outcome InfraFault branch in PlaceholderTrapVerifier for
// TrapKind.T6SpeConfidentialClient. Per DS-4 s6 T6 row: "KV secret presence +
// Graph containerType GET under ClientCertificateCredential (app-only proof)".
//
// PROBE CONTRACT (this class -- ONE trap, standalone; the aggregate
// IE2ETrapVerifier surface is out of scope for this task; task 185 assembly
// composes this probe + 5 sibling probes into the real IE2ETrapVerifier):
//   - Passed          : (1) KV cert secret readable (proves T6 cert exists
//                       where H8 expects it) AND (2) a Graph GET issued under
//                       ClientCertificateCredential succeeded (proves the
//                       confidential-client cert-based auth model is
//                       genuinely IN EFFECT, not merely configured -- H13's
//                       R7 "assert EFFECTS not intentions" principle).
//   - Failed          : Graph responded with the "public client not allowed"
//                       trap signature (per SpeConfidentialClientGraphFactory.
//                       DelegatedTokenTrapPhrase -- the retired PS scripts'
//                       identical stdout-scan). This is the SILENT-FAIL
//                       ACTUALLY MANIFESTED, mapped to QuarantineRequired in
//                       H13's decision logic.
//   - InfraFault      : Verifier could not verdict -- classified into H13
//                       Resumable. Cases:
//                         * KV cert secret missing/unreadable/malformed.
//                         * Graph 404 Not Found -- documented signature of
//                           SPE's up-to-24h container-type replication
//                           window (design.md s4.1 H8 row); NOT a failure,
//                           NOT a T6 trap (POML T6 row).
//                         * Any other Graph error (401/403 without trap
//                           phrase, 5xx, transport, timeout).
//
// WHY THE GRAPH GET IS NOT MOCKED IN CI:
//   Parity with the H8 collaborators GraphContainerTypeProvisioner /
//   GraphAppOnlyContainerVerifier -- see each file's "NOT UNIT-TESTED IN THE
//   CI SUITE" header. Graph half of this probe factored behind
//   IT6GraphAppOnlyProbe; test impls return canned outcomes.
//
// SIBLING-COORDINATION NOTE (parent dispatch 2026-08-20):
//   5 sibling agents are simultaneously authoring per-probe classes for
//   T2 (task 177), I1 (task 170 / 182), I2 (task 173), I3 (task 174),
//   I5 (task 179). All 6 tasks write to disjoint per-probe .cs files to
//   avoid guaranteed merge conflicts on the shared PlaceholderTrapVerifier.cs
//   / PlaceholderInvariantVerifier.cs / E2EAcceptanceModule.cs. The DI wire-up
//   that swaps this real probe into the aggregate verifier is DEFERRED to
//   assembly task 185.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- SpeConfidentialClientGraphFactory (task 131) + IE2ETrapVerifier /
//     TrapVerificationOutcome (task 055). PlaceholderTrapVerifier returns
//     InfraFault for every trap unconditionally.
//   Extension -- REUSES task 131 factory verbatim; adds thin orchestration +
//     outcome-mapping layer.
//   Cost-of-doing-nothing -- H13 cannot go green for T6 (DS-4 s6);
//     acceptance-target transition (tasks 184/185/186) blocked.
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T6 (SPE confidential-client) probe per DS-4 s6. Produces exactly one
/// <see cref="TrapVerificationOutcome"/> for <see cref="TrapKind.T6SpeConfidentialClient"/>.
/// </summary>
public sealed class T6SpeConfidentialClientTrapProbe : ITrapProbe
{
    /// <summary>Canonical KV secret name for the SPE owner cert -- mirrors <see cref="SpeContainerOptions.DefaultCertSecretName"/>. Flagged for Phase H reconciliation (task 084).</summary>
    internal const string SpeOwnerCertSecretName = "SPE-OwnerCert-Pfx";

    /// <summary>Fixed trap identity for this probe -- ALWAYS T6.</summary>
    public TrapKind Kind => TrapKind.T6SpeConfidentialClient;

    private readonly TokenCredential _sharedCredential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly IT6GraphAppOnlyProbe _graphProbe;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<T6SpeConfidentialClientTrapProbe> _logger;

    /// <summary>Production constructor.</summary>
    public T6SpeConfidentialClientTrapProbe(
        TokenCredential sharedCredential,
        IT6GraphAppOnlyProbe graphProbe,
        IOptions<H13AcceptanceOptions> options,
        ILogger<T6SpeConfidentialClientTrapProbe> logger)
        : this(sharedCredential, clientOptions: null, graphProbe, options, logger)
    {
    }

    /// <summary>Test seam constructor -- injects a fake-transport SecretClientOptions.</summary>
    internal T6SpeConfidentialClientTrapProbe(
        TokenCredential sharedCredential,
        SecretClientOptions? clientOptions,
        IT6GraphAppOnlyProbe graphProbe,
        IOptions<H13AcceptanceOptions> options,
        ILogger<T6SpeConfidentialClientTrapProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedCredential);
        ArgumentNullException.ThrowIfNull(graphProbe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _sharedCredential = sharedCredential;
        _clientOptions = clientOptions;
        _graphProbe = graphProbe;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T6 probe: KV cert secret presence + Graph containerType GET
    /// under ClientCertificateCredential. Never throws for probe-level fault
    /// modes -- returns InfraFault instead.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BffAppRegId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyVaultName);

        _logger.LogInformation(
            "T6 probe starting: customerId={CustomerId} runId={RunId} tenantId={TenantId} " +
            "vaultName={VaultName} owningAppId={OwningAppId}",
            request.CustomerId, request.RunId, request.TenantId,
            request.KeyVaultName, request.BffAppRegId);

        // (1) KV cert-load half.
        X509Certificate2 cert;
        try
        {
            cert = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
                _sharedCredential, _clientOptions, request.KeyVaultName,
                SpeOwnerCertSecretName, _options.TrapVerifierTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "T6 probe InfraFault -- KV cert secret unreadable: customerId={CustomerId} " +
                "vaultName={VaultName} secretName={SecretName} status={Status}",
                request.CustomerId, request.KeyVaultName, SpeOwnerCertSecretName, ex.Status);
            return new TrapVerificationOutcome.InfraFault(
                TrapKind.T6SpeConfidentialClient,
                $"T6 verdict deferred: KV secret '{SpeOwnerCertSecretName}' on vault " +
                $"'{request.KeyVaultName}' was unreadable (RequestFailedException status {ex.Status}: " +
                $"{ex.ErrorCode ?? "(no code)"}). Verify H4 populated the cert secret and that the " +
                $"platform UAMI has Key Vault Secrets User RBAC on this vault. Re-run H13 after fixing.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "T6 probe InfraFault -- KV cert secret malformed: customerId={CustomerId} " +
                "vaultName={VaultName} secretName={SecretName}",
                request.CustomerId, request.KeyVaultName, SpeOwnerCertSecretName);
            return new TrapVerificationOutcome.InfraFault(
                TrapKind.T6SpeConfidentialClient,
                $"T6 verdict deferred: KV secret '{SpeOwnerCertSecretName}' on vault " +
                $"'{request.KeyVaultName}' is present but not a usable base64-encoded PFX " +
                $"({ex.Message}). Re-upload the cert via H4 / scripts/common/Get-SpeConfidentialClientToken.ps1 " +
                $"conventions and re-run H13.");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex,
                "T6 probe InfraFault -- KV cert secret read timeout: customerId={CustomerId} " +
                "vaultName={VaultName} timeout={Timeout}",
                request.CustomerId, request.KeyVaultName, _options.TrapVerifierTimeout);
            return new TrapVerificationOutcome.InfraFault(
                TrapKind.T6SpeConfidentialClient,
                $"T6 verdict deferred: KV cert secret read from vault '{request.KeyVaultName}' " +
                $"exceeded configured timeout {_options.TrapVerifierTimeout}. Vault reachability or " +
                $"credential-chain latency issue. Re-run H13 once transient is resolved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "T6 probe InfraFault -- KV cert secret unexpected exception: customerId={CustomerId} " +
                "type={ExType}", request.CustomerId, ex.GetType().Name);
            return new TrapVerificationOutcome.InfraFault(
                TrapKind.T6SpeConfidentialClient,
                $"T6 verdict deferred: unexpected {ex.GetType().Name} loading KV secret " +
                $"'{SpeOwnerCertSecretName}' from vault '{request.KeyVaultName}': {ex.Message}.");
        }

        // (2) Graph confidential-client GET half.
        using (cert)
        {
            T6GraphAppOnlyProbeResult graphResult;
            try
            {
                graphResult = await _graphProbe.ProbeAsync(
                    request.TenantId, request.BffAppRegId, cert, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "T6 probe InfraFault -- Graph probe seam threw {ExType}: customerId={CustomerId}",
                    ex.GetType().Name, request.CustomerId);
                return new TrapVerificationOutcome.InfraFault(
                    TrapKind.T6SpeConfidentialClient,
                    $"T6 verdict deferred: Graph app-only probe seam threw unexpected " +
                    $"{ex.GetType().Name}: {ex.Message}.");
            }

            return graphResult switch
            {
                T6GraphAppOnlyProbeResult.SucceededResult =>
                    new TrapVerificationOutcome.Passed(TrapKind.T6SpeConfidentialClient),

                T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult trap =>
                    new TrapVerificationOutcome.Failed(
                        TrapKind.T6SpeConfidentialClient,
                        $"T6 silent-fail trap MANIFESTED: Graph app-only GET rejected under " +
                        $"ClientCertificateCredential with the delegated-token trap phrase " +
                        $"('{SpeConfidentialClientGraphFactory.DelegatedTokenTrapPhrase}'). " +
                        $"Observed status={trap.StatusCode}. Confidential-client (app-only) auth " +
                        $"MUST be used for SPE container-type operations per spec.md FR-33 / T6 -- " +
                        $"the deployed configuration is not honoring this. {trap.Diagnostic}"),

                T6GraphAppOnlyProbeResult.ReplicationPendingResult pending =>
                    new TrapVerificationOutcome.InfraFault(
                        TrapKind.T6SpeConfidentialClient,
                        $"T6 verdict deferred: Graph app-only GET returned 404 Not Found -- consistent " +
                        $"with SPE's up-to-24h container-type replication window (design.md s4.1 H8 row). " +
                        $"This is NOT a T6 trap; the confidential-client cert-based credential itself is " +
                        $"presumed valid but cannot be conclusively verdicted until replication completes. " +
                        $"Re-run H13 after the SPE replication window has elapsed. {pending.Diagnostic}"),

                T6GraphAppOnlyProbeResult.InfraFaultResult infra =>
                    new TrapVerificationOutcome.InfraFault(
                        TrapKind.T6SpeConfidentialClient,
                        $"T6 verdict deferred: Graph app-only probe could not run to a verdict. " +
                        $"{infra.Diagnostic}"),

                _ => new TrapVerificationOutcome.InfraFault(
                    TrapKind.T6SpeConfidentialClient,
                    $"T6 verdict deferred: unrecognized Graph probe result variant " +
                    $"'{graphResult.GetType().Name}'."),
            };
        }
    }
}
