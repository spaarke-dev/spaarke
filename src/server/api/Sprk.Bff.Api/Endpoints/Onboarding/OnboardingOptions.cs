using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// Options bound from the <c>Onboarding</c> configuration section. Consumed
/// by the H0.5 consent-callback endpoint (task 042 — customer-provisioning-orchestration-r1).
///
/// <para>
/// Placement Justification (CLAUDE.md §10 + spec.md §10 ADR-013 row): the
/// endpoint receives an Anonymous+HMAC-verified POST from the Microsoft
/// admin-consent redirect (§4.3a.2 — this is the ONE BFF endpoint that
/// MUST be Anonymous because the customer admin is authenticating to THEIR
/// tenant, not to Spaarke's control plane). The HMAC signing key is a
/// shared secret stored as a KV URI reference in the Spaarke platform
/// vault (spec.md ADR Tensions ADR-028 row).
/// </para>
///
/// <para>
/// Tier 1/2/3 classification (per spec.md NFR-05 + r3 task 061 config
/// validation): <b>Tier 1</b> — validated on start via
/// <see cref="OnboardingModule"/>'s <c>.ValidateOnStart()</c> call. The BFF
/// MUST fail fast at boot if the H0.5 signing key is missing in a deployed
/// environment. Placeholder-friendly for dev via
/// <see cref="HmacSigningKey"/> default (empty) plus explicit
/// <see cref="EnableDevBypass"/> escape hatch that IS NOT permitted in
/// deployed environments (enforced by <see cref="OnboardingModule"/>).
/// </para>
/// </summary>
public sealed class OnboardingOptions
{
    /// <summary>Configuration section name (root-level <c>Onboarding</c>).</summary>
    public const string SectionName = "Onboarding";

    /// <summary>
    /// HMAC-SHA256 shared signing key for the consent-callback endpoint.
    /// Bound via KV URI reference from the Spaarke platform vault in
    /// deployed environments. Empty in the dev-scaffold app-settings so
    /// unit tests can substitute a fake key without a live KV round-trip.
    /// </summary>
    /// <remarks>
    /// MUST be non-empty in Production / Staging / Demo (enforced by
    /// <see cref="OnboardingModule"/> at start-up unless
    /// <see cref="EnableDevBypass"/> is set — see that property's remarks
    /// for the deployed-env restriction).
    /// </remarks>
    public string HmacSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Service Bus queue name the endpoint enqueues to. Default matches
    /// L2's <c>sprk-provisioning-jobs</c> queue name so the L2 reconciler
    /// (Wave C5) drains it without extra config.
    /// </summary>
    [Required(ErrorMessage = "Onboarding:QueueName is required")]
    public string QueueName { get; set; } = "sprk-provisioning-jobs";

    /// <summary>
    /// HTTP header name that carries the HMAC-SHA256 signature. Default
    /// mirrors GitHub / GH-Hub convention (<c>X-Signature-256</c>).
    /// </summary>
    [Required(ErrorMessage = "Onboarding:SignatureHeaderName is required")]
    public string SignatureHeaderName { get; set; } = "X-Signature-256";

    /// <summary>
    /// Optional dev-only bypass — when true, missing / empty
    /// <see cref="HmacSigningKey"/> does NOT fail start-up. Intended for
    /// dev-scaffold parity where the endpoint is exercised via unit tests
    /// that override the verifier directly. In Production / Staging /
    /// Demo this MUST be false (enforced by <see cref="OnboardingModule"/>).
    /// </summary>
    public bool EnableDevBypass { get; set; }
}
