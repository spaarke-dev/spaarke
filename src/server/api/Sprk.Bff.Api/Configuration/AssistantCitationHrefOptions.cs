namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for citation <c>href</c> URL projection in
/// <see cref="Services.Ai.Insights.AssistantToolCallHandler"/> (Wave F task 052 / FR-F3).
/// Bound from <c>Insights:CitationHref</c> in <c>appsettings.{env}.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b>: lets the Assistant render clickable citations whose URLs route through
/// the existing BFF preview endpoint (<c>GET /api/documents/{documentId}/preview</c>) —
/// auth is enforced naturally via OBO + Graph/Dataverse ACL, so no URL signing or token
/// embedding is required (per AIPU2-027 privilege filtering).
/// </para>
/// <para>
/// <b>Contract</b>: the configured <see cref="BffBaseUrl"/> is the absolute origin (no
/// trailing slash) prepended to <c>/api/documents/{id}/preview</c> when projecting
/// citation <c>Href</c> values. When unset (null / empty), citation projection emits
/// <c>Href = null</c> for ALL citations and consumers fall back to display-name-only
/// rendering per contract v1.1 §3.5 back-compat.
/// </para>
/// <para>
/// <b>Environment-specific</b>: Production deployments must set this to the
/// public-facing BFF origin (e.g.,
/// <c>https://spaarke-bff-prod.azurewebsites.net</c>); Dev environments use
/// <c>https://spaarke-bff-dev.azurewebsites.net</c>. Local-dev defaults are
/// intentionally absent — local runs without explicit configuration emit
/// <c>Href = null</c> which is the documented "no preview available" behavior.
/// </para>
/// </remarks>
public sealed class AssistantCitationHrefOptions
{
    /// <summary>Configuration section name binding.</summary>
    public const string SectionName = "Insights:CitationHref";

    /// <summary>
    /// Absolute origin of the BFF used to build citation <c>href</c> URLs. Format:
    /// <c>https://{host}</c> (no path, no trailing slash). When null / empty,
    /// citation projection emits <c>Href = null</c> for all citations.
    /// </summary>
    public string? BffBaseUrl { get; set; }
}
