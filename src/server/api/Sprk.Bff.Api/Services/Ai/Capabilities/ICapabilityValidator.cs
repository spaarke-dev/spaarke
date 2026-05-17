namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Filters a candidate list of capabilities down to the effective set visible to a given request.
///
/// Four checks are applied per capability, in order:
///   1. Kill switch   — is the capability globally disabled via configuration?
///   2. Tenant toggle — is the capability restricted to a specific tenant URL list?
///   3. User permission — does the caller hold the required role claim?
///   4. Context compatibility — does the conversation context satisfy the capability's preconditions?
///
/// Capabilities that fail any check are silently excluded (no HTTP error surfaced).
/// Every exclusion is logged at Information level and counted in the OTEL counter
/// <c>ai_capability_validation_excluded_total</c>.
///
/// ADR-015: user message content is NEVER logged. Only capability names, user IDs, and
/// structured exclusion reasons are written to logs and spans.
/// </summary>
public interface ICapabilityValidator
{
    /// <summary>
    /// Filters <paramref name="candidates"/> to the subset that is valid for the given
    /// <paramref name="context"/>, applying kill-switch, tenant, permission, and context checks.
    /// </summary>
    /// <param name="candidates">
    /// The full list of candidates to evaluate (typically from <see cref="ICapabilityManifest.GetAll"/>).
    /// </param>
    /// <param name="context">
    /// Per-request context carrying the caller's identity, tenant URL, and conversation state.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Filtered list containing only capabilities that passed all four validation checks.
    /// Never null; may be empty if all candidates are excluded.
    /// </returns>
    Task<IReadOnlyList<CapabilityManifestEntry>> FilterAsync(
        IReadOnlyList<CapabilityManifestEntry> candidates,
        CapabilityValidationContext context,
        CancellationToken ct = default);
}
