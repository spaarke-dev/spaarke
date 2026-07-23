namespace Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;

/// <summary>
/// Immutable plan describing the per-boundary ACS resource + Event Grid system topic/subscription
/// to provision for one customer boundary (task 012, FR-18). Built deterministically by
/// <see cref="AcsBoundaryProvisioningService"/> from <c>AcsProvisioningOptions</c>; consumed by an
/// <see cref="IAcsBoundaryProvisioner"/>. Building the plan performs NO Azure call.
/// </summary>
public sealed record AcsProvisioningPlan
{
    /// <summary>The customer boundary this plan provisions for.</summary>
    public required string BoundaryId { get; init; }

    /// <summary>Name of the per-boundary ACS resource.</summary>
    public required string AcsResourceName { get; init; }

    /// <summary>
    /// ACS data location — IMMUTABLE at create time (design §8.7 / D-01). The residency mechanism:
    /// a separate ACS resource per boundary with this location fixed at create.
    /// </summary>
    public required string DataLocation { get; init; }

    /// <summary>Control-plane location for the ACS resource + Event Grid system topic (global).</summary>
    public required string ResourceLocation { get; init; }

    /// <summary>The Event Grid system topic + subscription that feeds the BFF webhook.</summary>
    public required AcsEventSubscriptionPlan EventSubscription { get; init; }
}

/// <summary>
/// The Event Grid system-topic + subscription portion of an <see cref="AcsProvisioningPlan"/> —
/// chat events → BFF webhook + dead-letter Storage (design §8.3).
/// </summary>
public sealed record AcsEventSubscriptionPlan
{
    public required string SystemTopicName { get; init; }
    public required string SubscriptionName { get; init; }

    /// <summary>BFF inbound webhook the subscription delivers to (task 030 ingress).</summary>
    public required string WebhookEndpointUrl { get; init; }

    /// <summary>Chat event types subscribed from day one (design §8.3).</summary>
    public required IReadOnlyList<string> IncludedEventTypes { get; init; }

    /// <summary>Dead-letter Storage account resource id (configured from day one).</summary>
    public required string DeadLetterStorageAccountResourceId { get; init; }

    /// <summary>Dead-letter blob container name.</summary>
    public required string DeadLetterContainerName { get; init; }
}

/// <summary>Outcome of an <see cref="IAcsBoundaryProvisioner.ProvisionAsync"/> call.</summary>
public sealed record AcsProvisioningResult
{
    public required string BoundaryId { get; init; }
    public required AcsProvisioningStatus Status { get; init; }

    /// <summary>The ACS host name, when a live resource was created.</summary>
    public string? AcsHostName { get; init; }

    /// <summary>Human-readable detail (e.g. why provisioning was deferred).</summary>
    public string? Detail { get; init; }

    public static AcsProvisioningResult Deferred(string boundaryId, string detail) =>
        new() { BoundaryId = boundaryId, Status = AcsProvisioningStatus.Deferred, Detail = detail };

    public static AcsProvisioningResult Provisioned(string boundaryId, string acsHostName) =>
        new() { BoundaryId = boundaryId, Status = AcsProvisioningStatus.Provisioned, AcsHostName = acsHostName };
}

/// <summary>Status of a per-boundary ACS provisioning attempt.</summary>
public enum AcsProvisioningStatus
{
    /// <summary>Live ACS resource + Event Grid subscription created.</summary>
    Provisioned,

    /// <summary>
    /// Live provisioning is Bicep/operator-driven in R1 (see task-012 runbook); the in-process
    /// orchestrator recorded intent and produced the plan but made no management-plane call.
    /// </summary>
    Deferred,

    /// <summary>Provisioning skipped (e.g. messaging not enabled for this boundary).</summary>
    Skipped,
}
