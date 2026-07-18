using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;

/// <summary>
/// Extends the ADR-027 per-customer provisioning orchestrator with the per-boundary ACS resource +
/// Event Grid system topic/subscription wiring (task 012, FR-18). Builds an
/// <see cref="AcsProvisioningPlan"/> from <see cref="AcsProvisioningOptions"/> for a boundary —
/// choosing the IMMUTABLE data location per boundary (design §8.7 / D-01, the residency mechanism)
/// and subscribing the chat events (design §8.3) to the BFF webhook (task 030 ingress) with a
/// dead-letter Storage destination from day one — then hands it to <see cref="IAcsBoundaryProvisioner"/>.
/// </summary>
/// <remarks>
/// This is an EXTENSION of the existing per-customer isolation flow (ADR-027; same boundary the
/// <c>customer.bicep</c> orchestrator + membership-topic module already provision), NOT a parallel
/// provisioning path (root CLAUDE.md §11). Registered as a concrete type (ADR-010) in the
/// registration/provisioning module.
/// </remarks>
public sealed class AcsBoundaryProvisioningService
{
    private readonly IAcsBoundaryProvisioner _provisioner;
    private readonly AcsProvisioningOptions _options;
    private readonly ILogger<AcsBoundaryProvisioningService> _logger;

    public AcsBoundaryProvisioningService(
        IAcsBoundaryProvisioner provisioner,
        IOptions<AcsProvisioningOptions> options,
        ILogger<AcsBoundaryProvisioningService> logger)
    {
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the per-boundary ACS + Event Grid provisioning plan from options. Deterministic and
    /// side-effect-free (no Azure call) — exposed for provisioning and for unit verification of the
    /// wiring shape.
    /// </summary>
    /// <param name="boundaryId">Customer boundary identifier (drives resource naming).</param>
    /// <param name="dataLocationOverride">
    /// Optional per-boundary data-location override. IMMUTABLE at create time (D-01): if a boundary
    /// requires a residency other than the configured default, it MUST be chosen here at create.
    /// </param>
    public AcsProvisioningPlan BuildPlan(string boundaryId, string? dataLocationOverride = null)
    {
        if (string.IsNullOrWhiteSpace(boundaryId))
        {
            throw new ArgumentException("Boundary id is required.", nameof(boundaryId));
        }

        // Per-boundary data location, immutable at create (D-01) — residency mechanism.
        var dataLocation = string.IsNullOrWhiteSpace(dataLocationOverride)
            ? _options.DataLocation
            : dataLocationOverride!;

        var eg = _options.EventGrid;
        var includedEventTypes = eg.IncludedEventTypes is { Length: > 0 }
            ? eg.IncludedEventTypes
            : AcsChatEventTypes.Default;

        var subscription = new AcsEventSubscriptionPlan
        {
            SystemTopicName = $"sprk-{boundaryId}-acs-egt",
            SubscriptionName = "chat-events-to-bff",
            WebhookEndpointUrl = eg.WebhookEndpointUrl,
            IncludedEventTypes = includedEventTypes,
            DeadLetterStorageAccountResourceId = eg.DeadLetter.StorageAccountResourceId,
            DeadLetterContainerName = eg.DeadLetter.ContainerName,
        };

        return new AcsProvisioningPlan
        {
            BoundaryId = boundaryId,
            AcsResourceName = $"sprk-{boundaryId}-acs",
            DataLocation = dataLocation,
            ResourceLocation = _options.ResourceLocation,
            EventSubscription = subscription,
        };
    }

    /// <summary>
    /// Provisions (or, in R1, records intent to provision via the Null-Object provisioner) the
    /// per-boundary ACS resource + Event Grid subscription for a customer boundary. Extension of the
    /// ADR-027 flow.
    /// </summary>
    public async Task<AcsProvisioningResult> ProvisionBoundaryAsync(
        string boundaryId,
        string? dataLocationOverride = null,
        CancellationToken ct = default)
    {
        var plan = BuildPlan(boundaryId, dataLocationOverride);

        _logger.LogInformation(
            "Provisioning ACS boundary {BoundaryId}: resource {AcsResource} (data location {DataLocation}, " +
            "immutable — D-01); Event Grid subscription to {Webhook} for {EventCount} chat events + dead-letter '{DeadLetter}'.",
            plan.BoundaryId,
            plan.AcsResourceName,
            plan.DataLocation,
            plan.EventSubscription.WebhookEndpointUrl,
            plan.EventSubscription.IncludedEventTypes.Count,
            plan.EventSubscription.DeadLetterContainerName);

        return await _provisioner.ProvisionAsync(plan, ct);
    }
}
