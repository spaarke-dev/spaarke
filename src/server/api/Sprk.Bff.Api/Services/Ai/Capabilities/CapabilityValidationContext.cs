using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Per-request context supplied to <see cref="ICapabilityValidator.FilterAsync"/> so that
/// validation checks can evaluate the caller's identity and conversation state.
///
/// Immutable record — safe to pass across async boundaries.
///
/// Fields:
///   <see cref="User"/>                 — caller's <see cref="ClaimsPrincipal"/> from the JWT.
///                                         Used for role checks (kill-switch and tenant checks
///                                         are independent of the user identity).
///   <see cref="TenantEnvironmentUrl"/> — base URL of the Dataverse environment for this tenant
///                                         (e.g. "https://spaarkedev1.crm.dynamics.com").
///                                         Compared against <see cref="CapabilityManifestEntry.TenantRestrictions"/>.
///   <see cref="ConversationContext"/>  — key/value pairs describing the current conversation state
///                                         (e.g. { "MatterLoaded", "true" }, { "DocumentPresent", "true" }).
///                                         Used for context-compatibility checks.
/// </summary>
/// <param name="User">Caller's claims principal from the validated JWT.</param>
/// <param name="TenantEnvironmentUrl">
/// Base URL of the caller's Dataverse environment (scheme + host only, no trailing slash).
/// </param>
/// <param name="ConversationContext">
/// Snapshot of the current conversation's context flags. Keys are case-insensitive strings
/// (e.g. "MatterLoaded", "DocumentPresent"); values are string representations of the state
/// (e.g. "true", "false", or an entity ID). An empty dictionary means no context has been loaded.
/// </param>
public sealed record CapabilityValidationContext(
    ClaimsPrincipal User,
    string TenantEnvironmentUrl,
    IReadOnlyDictionary<string, string> ConversationContext)
{
    /// <summary>
    /// Extracts the user's object ID (OID) from the <c>oid</c> claim for structured logging.
    /// Returns "unknown" when the claim is absent (anonymous or malformed token).
    /// </summary>
    public string UserId =>
        User.FindFirstValue("oid")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";
}
