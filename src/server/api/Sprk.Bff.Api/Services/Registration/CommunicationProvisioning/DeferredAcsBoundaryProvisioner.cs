using Azure.Core;

namespace Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;

/// <summary>
/// ADR-032 Null-Object implementation of <see cref="IAcsBoundaryProvisioner"/>. R1 provisions the
/// per-boundary ACS resource + Event Grid subscription via Bicep/operator runbook (no in-process ARM
/// management SDK — keeps the BFF publish-size delta ≈0). This no-op records the intended plan and
/// returns a <see cref="AcsProvisioningStatus.Deferred"/> result so the orchestrator is fully wired
/// and testable now.
/// </summary>
/// <remarks>
/// Injects the central <see cref="TokenCredential"/> from DI (registered in <c>Program.cs</c> via
/// <c>ManagedIdentityCredentialFactory</c>, ADR-028 / NFR-05). A future live implementation
/// authenticates management-plane calls with this credential; this type MUST NOT construct a
/// credential with <c>new</c>.
/// </remarks>
public sealed class DeferredAcsBoundaryProvisioner : IAcsBoundaryProvisioner
{
    private readonly TokenCredential _credential;
    private readonly ILogger<DeferredAcsBoundaryProvisioner> _logger;

    public DeferredAcsBoundaryProvisioner(
        TokenCredential credential,
        ILogger<DeferredAcsBoundaryProvisioner> logger)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AcsProvisioningResult> ProvisionAsync(AcsProvisioningPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // The injected central credential (ADR-028) is what a live management-plane call would use;
        // logging its type makes explicit that no credential is constructed here (NFR-05).
        _logger.LogDebug(
            "ACS management-plane auth would use injected credential {CredentialType} (ADR-028; no credential constructed here).",
            _credential.GetType().Name);

        _logger.LogInformation(
            "ACS boundary provisioning DEFERRED for boundary {BoundaryId}: resource '{AcsResource}' " +
            "(data location {DataLocation}, immutable — D-01); Event Grid system topic '{Topic}' " +
            "subscription '{Subscription}' → {Webhook} for {EventCount} chat events, dead-letter '{DeadLetter}'. " +
            "Run the operator runbook (projects/messaging-communication-app-r1/notes/012-acs-provisioning-runbook.md) " +
            "to create the live resources.",
            plan.BoundaryId,
            plan.AcsResourceName,
            plan.DataLocation,
            plan.EventSubscription.SystemTopicName,
            plan.EventSubscription.SubscriptionName,
            plan.EventSubscription.WebhookEndpointUrl,
            plan.EventSubscription.IncludedEventTypes.Count,
            plan.EventSubscription.DeadLetterContainerName);

        return Task.FromResult(AcsProvisioningResult.Deferred(
            plan.BoundaryId,
            "Live ACS + Event Grid provisioning is Bicep/operator-driven in R1 (see task-012 runbook); " +
            "no in-process management call performed."));
    }
}
