namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Options for per-customer-boundary ACS + Event Grid provisioning (messaging-communication-app-r1,
/// task 012, FR-18). Bound from the <c>"AcsProvisioning"</c> configuration section.
/// </summary>
/// <remarks>
/// These options describe the SHAPE of the provisioning plan the orchestrator builds and the
/// runtime-owned values the Event Grid subscription must target (the BFF webhook URL + the
/// dead-letter Storage destination). The actual Azure resource creation is Bicep/operator-driven
/// (<c>infrastructure/bicep/customer.bicep</c> → <c>modules/acs-communication.bicep</c>) — see the
/// task-012 operator runbook. This keeps the BFF publish-size delta ≈0 (no in-process ARM
/// management SDK) while the provisioning wiring stays testable in-process.
///
/// Residency (design §8.7 / decision D-01): ACS data location is IMMUTABLE at create time, so
/// residency is achieved by a SEPARATE ACS resource per boundary with <see cref="DataLocation"/>
/// chosen per boundary at create time.
/// </remarks>
public sealed class AcsProvisioningOptions
{
    public const string SectionName = "AcsProvisioning";

    /// <summary>
    /// ACS data location for the boundary (e.g. "unitedstates", "europe", "australia", "uk").
    /// IMMUTABLE at create time (design §8.7 / D-01): a separate ACS resource per boundary is the
    /// residency mechanism. A per-boundary override may be supplied at provisioning time.
    /// </summary>
    public string DataLocation { get; set; } = "unitedstates";

    /// <summary>
    /// Control-plane location for the ACS resource and the Event Grid system topic. Both are
    /// always "global"; this is exposed only so operators can pin it if Azure ever changes.
    /// </summary>
    public string ResourceLocation { get; set; } = "global";

    /// <summary>Event Grid subscription settings (chat events → BFF webhook + dead-letter).</summary>
    public AcsEventGridOptions EventGrid { get; set; } = new();
}

/// <summary>Event Grid subscription options for the ACS chat-event capture path (design §8.3).</summary>
public sealed class AcsEventGridOptions
{
    /// <summary>
    /// The BFF inbound webhook the Event Grid subscription delivers chat events to (task 030 ingress).
    /// </summary>
    public string WebhookEndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// Chat event types subscribed from day one (design §8.3). Empty falls back to
    /// <see cref="AcsChatEventTypes.Default"/>.
    /// </summary>
    public string[] IncludedEventTypes { get; set; } = AcsChatEventTypes.Default;

    /// <summary>Dead-letter Storage destination — configured from day one (design §8.3).</summary>
    public AcsDeadLetterOptions DeadLetter { get; set; } = new();
}

/// <summary>Dead-letter Storage destination for undeliverable Event Grid events.</summary>
public sealed class AcsDeadLetterOptions
{
    /// <summary>Resource id of the Storage account used for Event Grid dead-lettering.</summary>
    public string StorageAccountResourceId { get; set; } = string.Empty;

    /// <summary>Blob container that receives dead-lettered events.</summary>
    public string ContainerName { get; set; } = "acs-eventgrid-deadletter";
}
