// -----------------------------------------------------------------------------
// T6SpeConfidentialClientTrapProbe.cs
//
// Task 175 (Wave G-7, pipelined with H8 / task 131) -- REAL T6 probe replacing
// the single-outcome InfraFault branch in PlaceholderTrapVerifier for
// TrapKind.T6SpeConfidentialClient. Rewritten by task 248 (G28, owner D16): L2 acts
// as the SPE owning app through the Worker UAMI's federated identity credential
// (SpeConfidentialClientGraphFactory), so the old Key Vault certificate half is gone,
// and the Graph half lists the run's container instead of calling the app-only
// containerTypes GET Microsoft documents as 403 (it could never pass).
//
// PROBE CONTRACT (ONE trap, standalone; CompositeTrapVerifier composes it):
//   - Passed      : the app-only containers listing for the run's container type,
//                   made as the owning app, succeeded AND includes the run's
//                   container (InterStepState.SpeContainerId from H8) -- the
//                   app-only owning-app auth model is IN EFFECT for this stamp
//                   (H13's R7 "assert EFFECTS not intentions").
//   - Failed      : (1) Graph responded with the "public client not allowed" trap
//                   signature (SpeConfidentialClientGraphFactory.
//                   DelegatedTokenTrapPhrase) -- the silent-fail trap MANIFESTED;
//                   (2) the listing succeeded but the run's container is ABSENT --
//                   the run recorded a container the owning app cannot see, the
//                   same silent-fail class (L2 believes the stamp has storage it
//                   does not). Both map to QuarantineRequired in H13.
//   - InfraFault  : the verifier could not verdict -- H13 Resumable. Cases: no owner
//                   entry for the container type; no container id on the run; the
//                   owning-app token could not be obtained; Graph 404 (SPE
//                   replication window -- NOT a trap); any other refusal without
//                   the trap phrase (401/403/5xx/transport/timeout).
//
// WHY THE GRAPH CALL IS NOT MOCKED IN CI: parity with H8's GraphContainerProvisioner /
// GraphAppOnlyContainerVerifier -- see each file's "NOT UNIT-TESTED IN THE CI SUITE"
// header. The Graph half sits behind IT6GraphAppOnlyProbe; tests return canned outcomes.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T6 (SPE confidential-client) probe per DS-4 s6. Produces exactly one
/// <see cref="TrapVerificationOutcome"/> for <see cref="TrapKind.T6SpeConfidentialClient"/>.
/// </summary>
public sealed class T6SpeConfidentialClientTrapProbe : ITrapProbe
{
    /// <summary>Fixed trap identity for this probe -- ALWAYS T6.</summary>
    public TrapKind Kind => TrapKind.T6SpeConfidentialClient;

    private readonly IT6GraphAppOnlyProbe _graphProbe;
    private readonly SpeContainerOptions _speOptions;
    private readonly ILogger<T6SpeConfidentialClientTrapProbe> _logger;

    /// <summary>Production constructor.</summary>
    public T6SpeConfidentialClientTrapProbe(
        IT6GraphAppOnlyProbe graphProbe,
        IOptions<SpeContainerOptions> speOptions,
        ILogger<T6SpeConfidentialClientTrapProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(graphProbe);
        ArgumentNullException.ThrowIfNull(speOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _graphProbe = graphProbe;
        _speOptions = speOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T6 probe: app-only containers listing for the run's container type as the owning app,
    /// expecting the run's container. Never throws for probe-level fault modes -- returns InfraFault instead.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        // Task 245b: the owning app of the run's container type -- the SAME SpeContainerOptions.ContainerTypeOwners
        // entry H0 and H8 use. NOT the customer BFF app, which is a separate identity (topology §3A).
        if (!_speOptions.TryGetOwner(request.ContainerTypeId, out var owner))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                string.IsNullOrWhiteSpace(request.ContainerTypeId)
                    ? "T6 probe: the run carries no containerTypeId, so the SPE owning app to test is unknown."
                    : $"T6 probe: no SpeContainerOptions:ContainerTypeOwners entry for container type " +
                      $"'{request.ContainerTypeId.Trim()}' — configure it on the Worker, then re-run H13.");
        }
        if (string.IsNullOrWhiteSpace(request.SpeContainerId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T6 probe: the run carries no SPE container id (InterStepState.SpeContainerId, written by H8), so there " +
                "is no container to look for. Re-run H8, then H13.");
        }

        _logger.LogInformation(
            "T6 probe starting: customerId={CustomerId} runId={RunId} tenantId={TenantId} owningAppId={OwningAppId} " +
            "containerTypeId={ContainerTypeId} containerId={ContainerId}",
            request.CustomerId, request.RunId, request.TenantId, owner.OwnerAppId, owner.ContainerTypeId,
            request.SpeContainerId);

        T6GraphAppOnlyProbeResult graphResult;
        try
        {
            graphResult = await _graphProbe.ProbeAsync(
                request.TenantId, owner.OwnerAppId, owner.ContainerTypeId, request.SpeContainerId.Trim(),
                cancellationToken).ConfigureAwait(false);
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
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T6 verdict deferred: Graph app-only probe seam threw unexpected {ex.GetType().Name}: {ex.Message}.");
        }

        return graphResult switch
        {
            T6GraphAppOnlyProbeResult.SucceededResult =>
                new TrapVerificationOutcome.Passed(Kind),

            T6GraphAppOnlyProbeResult.ContainerAbsentResult absent =>
                new TrapVerificationOutcome.Failed(Kind,
                    $"T6 silent-fail MANIFESTED: the run recorded SPE container '{request.SpeContainerId.Trim()}', but the " +
                    $"owning app '{owner.OwnerAppId}' cannot see it in container type '{owner.ContainerTypeId}' " +
                    $"(app-only listing returned {absent.ListedCount} container(s) without it). {absent.Diagnostic}"),

            T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult trap =>
                new TrapVerificationOutcome.Failed(Kind,
                    $"T6 silent-fail trap MANIFESTED: the Graph app-only containers listing was rejected with the " +
                    $"delegated-token trap phrase ('{SpeConfidentialClientGraphFactory.DelegatedTokenTrapPhrase}'). " +
                    $"Observed status={trap.StatusCode}. Confidential-client (app-only) auth MUST be used for SPE " +
                    $"operations per spec.md FR-33 / T6 -- the deployed configuration is not honoring this. {trap.Diagnostic}"),

            T6GraphAppOnlyProbeResult.ReplicationPendingResult pending =>
                new TrapVerificationOutcome.InfraFault(Kind,
                    $"T6 verdict deferred: Graph app-only containers listing returned 404 Not Found -- consistent with " +
                    $"SPE's up-to-24h container-type replication window (design.md s4.1 H8 row). This is NOT a T6 trap. " +
                    $"Re-run H13 after the replication window has elapsed. {pending.Diagnostic}"),

            T6GraphAppOnlyProbeResult.InfraFaultResult infra =>
                new TrapVerificationOutcome.InfraFault(Kind,
                    $"T6 verdict deferred: Graph app-only probe could not run to a verdict. {infra.Diagnostic}"),

            _ => new TrapVerificationOutcome.InfraFault(Kind,
                $"T6 verdict deferred: unrecognized Graph probe result variant '{graphResult.GetType().Name}'."),
        };
    }
}
