using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Default implementation of <see cref="ICapabilityValidator"/>.
///
/// Applies four checks per candidate capability, in order:
///   1. Kill switch   — IConfiguration["AiCapabilities:KillSwitch:{name}"] == "true"
///   2. Tenant toggle — <see cref="CapabilityManifestEntry.TenantRestrictions"/> non-empty and
///                      caller's <see cref="CapabilityValidationContext.TenantEnvironmentUrl"/> not in the list
///   3. User permission — <see cref="CapabilityManifestEntry"/> RequiredRole not held by the caller
///   4. Context compatibility — required context key absent or not "true" in conversation context
///
/// Failed checks silently exclude the capability (no HTTP error).
/// Every exclusion is logged at Information level with structured properties:
///   CapabilityName, UserId, ExclusionReason.
///
/// OTEL counter: ai_capability_validation_excluded_total
///   Labels: capability_name, reason (kill_switch | tenant_toggle | permission | context)
///
/// ADR-015: user message content is NEVER logged.
/// ADR-010: registered as scoped (per-request) so it can safely read ClaimsPrincipal.
/// </summary>
public sealed class CapabilityValidator : ICapabilityValidator
{
    // ── Exclusion reason constants ─────────────────────────────────────────────

    /// <summary>Exclusion reason label for OTEL and logs: capability kill switch active.</summary>
    public const string ReasonKillSwitch = "kill_switch";

    /// <summary>Exclusion reason label for OTEL and logs: tenant not in allowed list.</summary>
    public const string ReasonTenantToggle = "tenant_toggle";

    /// <summary>Exclusion reason label for OTEL and logs: caller lacks required role.</summary>
    public const string ReasonPermission = "permission";

    /// <summary>Exclusion reason label for OTEL and logs: required context key absent or false.</summary>
    public const string ReasonContext = "context";

    // ── Configuration key prefix ───────────────────────────────────────────────

    /// <summary>
    /// IConfiguration section prefix for per-capability kill switches.
    /// Full key format: AiCapabilities:KillSwitch:{capabilityName}
    /// Set value "true" (case-insensitive) to disable the capability globally.
    /// </summary>
    private const string KillSwitchConfigPrefix = "AiCapabilities:KillSwitch:";

    /// <summary>
    /// Configuration key for a global "kill all" switch.
    /// When true, ALL capabilities are excluded regardless of individual settings.
    /// </summary>
    private const string GlobalKillSwitchKey = "AiCapabilities:KillSwitch:All";

    // ── Metadata key on CapabilityManifestEntry used for required-role and required-context ──
    //
    // The CapabilityManifestEntry record does not currently carry RequiredRole or RequiredContextKey
    // fields (those are planned for AIPU2-017). Until that task ships, CapabilityValidator reads
    // these values from the entry's Description field using a convention:
    //
    //   RequiredRole:    "[RequiredRole=ai.capabilities.legalResearch]" in Description
    //   RequiredContext: "[RequiredContext=MatterLoaded]"               in Description
    //
    // This convention is intentionally simple so it can be replaced without changing the validator's
    // public contract once the Dataverse schema is extended.

    private const string RequiredRolePrefix = "[RequiredRole=";
    private const string RequiredContextPrefix = "[RequiredContext=";

    // ── OTEL metric ───────────────────────────────────────────────────────────

    private static readonly Meter ValidatorMeter = new("Sprk.Bff.Api.Ai", "1.0.0");

    private static readonly Counter<long> ExclusionCounter =
        ValidatorMeter.CreateCounter<long>(
            "ai_capability_validation_excluded_total",
            unit: "{exclusion}",
            description:
                "Count of capability exclusions during per-request validation. " +
                "Labels: capability_name, reason (kill_switch | tenant_toggle | permission | context).");

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IConfiguration _configuration;
    private readonly ILogger<CapabilityValidator> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CapabilityValidator(
        IConfiguration configuration,
        ILogger<CapabilityValidator> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── ICapabilityValidator ──────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<CapabilityManifestEntry>> FilterAsync(
        IReadOnlyList<CapabilityManifestEntry> candidates,
        CapabilityValidationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        // Fast path: empty candidate list.
        if (candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<CapabilityManifestEntry>>([]);
        }

        var userId = context.UserId;
        var allowed = new List<CapabilityManifestEntry>(candidates.Count);

        // Global kill switch — skip per-capability checks when set.
        var globalKillActive = IsGlobalKillSwitchActive();

        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // ── Check 1: Kill switch ────────────────────────────────────────
            if (globalKillActive || IsKillSwitchActive(entry.CapabilityName))
            {
                RecordExclusion(entry.CapabilityName, userId, ReasonKillSwitch);
                continue;
            }

            // ── Check 2: Tenant toggle ──────────────────────────────────────
            if (!IsTenantAllowed(entry, context.TenantEnvironmentUrl))
            {
                RecordExclusion(entry.CapabilityName, userId, ReasonTenantToggle);
                continue;
            }

            // ── Check 3: User permission ────────────────────────────────────
            if (!HasRequiredRole(entry, context))
            {
                RecordExclusion(entry.CapabilityName, userId, ReasonPermission);
                continue;
            }

            // ── Check 4: Context compatibility ──────────────────────────────
            if (!HasRequiredContext(entry, context.ConversationContext))
            {
                RecordExclusion(entry.CapabilityName, userId, ReasonContext);
                continue;
            }

            allowed.Add(entry);
        }

        return Task.FromResult<IReadOnlyList<CapabilityManifestEntry>>(allowed);
    }

    // ── Check implementations ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the global "kill all" configuration switch is active.
    /// Key: AiCapabilities:KillSwitch:All = "true"
    /// </summary>
    private bool IsGlobalKillSwitchActive()
    {
        var value = _configuration[GlobalKillSwitchKey];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the per-capability kill switch is active.
    /// Key: AiCapabilities:KillSwitch:{capabilityName} = "true"
    /// </summary>
    private bool IsKillSwitchActive(string capabilityName)
    {
        var key = $"{KillSwitchConfigPrefix}{capabilityName}";
        var value = _configuration[key];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the entry's <see cref="CapabilityManifestEntry.TenantRestrictions"/> list
    /// is empty (unrestricted) or contains the caller's <paramref name="tenantEnvironmentUrl"/>.
    ///
    /// Comparison is case-insensitive and ignores trailing slashes so that
    /// "https://spaarkedev1.crm.dynamics.com" and "https://spaarkedev1.crm.dynamics.com/"
    /// are treated as equal.
    /// </summary>
    private static bool IsTenantAllowed(CapabilityManifestEntry entry, string tenantEnvironmentUrl)
    {
        // Empty restriction list → available to all tenants.
        if (entry.TenantRestrictions.Count == 0)
            return true;

        var normalised = tenantEnvironmentUrl.TrimEnd('/');
        foreach (var allowed in entry.TenantRestrictions)
        {
            if (string.Equals(allowed.TrimEnd('/'), normalised, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the caller holds the required role (if any) for this capability.
    ///
    /// Required role is parsed from the entry's Description using the convention:
    ///   [RequiredRole=ai.capabilities.legalResearch]
    ///
    /// When no required role annotation is present the check passes (unrestricted).
    /// Role check uses <see cref="System.Security.Claims.ClaimsPrincipal.IsInRole"/> which
    /// evaluates the roles claim from the JWT.
    /// </summary>
    private static bool HasRequiredRole(CapabilityManifestEntry entry, CapabilityValidationContext context)
    {
        var requiredRole = ExtractAnnotation(entry.Description, RequiredRolePrefix);
        if (requiredRole is null)
            return true; // No role requirement → pass.

        return context.User.IsInRole(requiredRole);
    }

    /// <summary>
    /// Returns true when the conversation context satisfies the capability's required context key.
    ///
    /// Required context key is parsed from the entry's Description using the convention:
    ///   [RequiredContext=MatterLoaded]
    ///
    /// When no required context annotation is present the check passes.
    /// The check passes when the key is present in <paramref name="conversationContext"/> with
    /// a value of "true" (case-insensitive).
    /// </summary>
    private static bool HasRequiredContext(
        CapabilityManifestEntry entry,
        IReadOnlyDictionary<string, string> conversationContext)
    {
        var requiredKey = ExtractAnnotation(entry.Description, RequiredContextPrefix);
        if (requiredKey is null)
            return true; // No context requirement → pass.

        return conversationContext.TryGetValue(requiredKey, out var val)
               && string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts a bracketed annotation value from a description string.
    ///
    /// Example: description = "Does legal research. [RequiredRole=ai.legalResearch]"
    ///          prefix       = "[RequiredRole="
    ///          returns      = "ai.legalResearch"
    ///
    /// Returns null when the annotation is absent.
    /// </summary>
    private static string? ExtractAnnotation(string description, string prefix)
    {
        if (string.IsNullOrEmpty(description))
            return null;

        var start = description.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var valueStart = start + prefix.Length;
        var end = description.IndexOf(']', valueStart);
        if (end < 0)
            return null;

        var value = description[valueStart..end].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // ── Exclusion logging and metrics ─────────────────────────────────────────

    /// <summary>
    /// Logs the exclusion at Information level and increments the OTEL counter.
    ///
    /// ADR-015: only CapabilityName, UserId, and ExclusionReason are logged.
    /// User message content is NEVER included.
    /// </summary>
    private void RecordExclusion(string capabilityName, string userId, string reason)
    {
        _logger.LogInformation(
            "Capability '{CapabilityName}' excluded for user '{UserId}': {ExclusionReason}",
            capabilityName,
            userId,
            reason);

        ExclusionCounter.Add(1,
            new KeyValuePair<string, object?>("capability_name", capabilityName),
            new KeyValuePair<string, object?>("reason", reason));
    }
}
