namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Inbound-security options for the ACS Event Grid webhook ingress (messaging-communication-app-r1
/// task 030 / FR-02). Bound from the <c>Communication:Acs:EventGridIngress</c> section.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>inbound</b> counterpart to <see cref="AcsProvisioningOptions"/> (which describes the
/// <b>outbound</b> per-boundary provisioning shape — resource, subscription, dead-letter). The two are kept
/// separate on purpose: provisioning options are a per-boundary DTO consumed by the provisioning orchestrator
/// (task 012), whereas these options are a <b>process-wide security boundary</b> for the one public webhook
/// endpoint that receives deliveries from every subscribed boundary's system topic.
/// </para>
/// <para><b>Security (task 030 boundary):</b> the ingress is a public, AllowAnonymous endpoint. Authenticity is
/// enforced by the Event Grid subscription-validation handshake (activation) PLUS the two controls below before
/// any event is enqueued:
/// <list type="number">
///   <item><see cref="AllowedTopics"/> — mandatory, fail-closed origin allow-list (an event whose Event Grid
///   <c>topic</c> is not listed never enqueues; an empty list rejects all real events).</item>
///   <item><see cref="ValidationKey"/> — optional defense-in-depth shared secret carried in the webhook URL's
///   <c>?sig=</c> query (Event Grid cannot HMAC-sign the body the way a Graph relay does, so a URL-embedded
///   secret is the Event-Grid-native analog of the Graph <c>clientState</c>/HMAC control).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AcsEventGridIngressOptions
{
    public const string SectionName = "Communication:Acs:EventGridIngress";

    /// <summary>
    /// Allow-list of ACS system-topic identifiers the ingress trusts. Each entry is matched
    /// (case-insensitive) against the delivered Event Grid event's top-level <c>topic</c> (the ARM resource id
    /// of the ACS resource the system topic sits on). MANDATORY, FAIL-CLOSED: when empty, the ingress rejects
    /// all real chat events (it cannot verify origin) — nothing is enqueued. Populated per environment from the
    /// resource ids provisioned by task 012.
    /// </summary>
    public string[] AllowedTopics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional shared secret embedded in the Event Grid subscription's webhook URL as the <c>?sig=</c> query
    /// parameter. When configured, the ingress requires a constant-time match on EVERY request (handshake and
    /// events) and rejects mismatches with 401 — so a leaked endpoint URL alone cannot forge deliveries. When
    /// null/empty this defense-in-depth layer is skipped and <see cref="AllowedTopics"/> remains the mandatory
    /// origin control. MUST be stored in Key Vault and referenced via <c>@Microsoft.KeyVault(...)</c>.
    /// </summary>
    public string? ValidationKey { get; set; }
}
