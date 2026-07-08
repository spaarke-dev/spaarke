namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// The ambient per-request metering attribution carried from an AI entry seam down to
/// the code that observes a meterable fact (FR-P4-05 / NFR-05, spaarke-ai-architecture-redesign-r1
/// task 054). Identifiers only — never content (NFR-07 / ADR-015).
/// </summary>
/// <param name="TenantId">AAD tenant id ('tid' claim) — the per-tenant rollup dimension.</param>
/// <param name="UserId">AAD user object id ('oid' claim) — the per-user drill-down dimension. Null for service-to-service calls.</param>
/// <param name="EntryPath">Which of the closed entry paths initiated the work — one of <see cref="AiMeteringContext.EntryPathText"/> / <see cref="AiMeteringContext.EntryPathClick"/> / <see cref="AiMeteringContext.EntryPathEvent"/> / <see cref="AiMeteringContext.EntryPathCoded"/>.</param>
public sealed record AiMeteringScope(string? TenantId, string? UserId, string EntryPath);

/// <summary>
/// AsyncLocal carrier for the <see cref="AiMeteringScope"/> (FR-P4-05 per-tenant metering).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — token usage is observed inside <see cref="Services.Ai.OpenAiClient"/>
/// (the only place Azure OpenAI returns <c>Usage</c> for executor calls), but tenant/user
/// identity lives at the HTTP entry seams (ChatEndpoints, DispatchSessionEndpoint,
/// EventRulesService, DailyBriefing endpoints). No existing type bridges the two.
/// (2) <i>Extension</i> — extending <c>IOpenAiClient</c> instead would add tenant/user/entry-path
/// parameters to 9 interface methods and touch every caller + every test mock; an ambient
/// AsyncLocal scope is the standard .NET pattern for exactly this cross-cutting attribution
/// (same mechanism as <c>Activity.Current</c>).
/// (3) <i>Cost-of-doing-nothing</i> — FR-P4-05 acceptance fails concretely: token counters
/// cannot carry tenant/user dimensions, so the KQL pack's per-tenant token rollup returns
/// nothing attributable.
/// </para>
/// <para>
/// <b>Usage</b>: entry seams call <c>using var _ = AiMeteringContext.Begin(tenantId, userId, entryPath);</c>
/// after auth-context extraction. Downstream metering emitters read <see cref="Current"/> and
/// omit dimensions when no scope is set (never throw — metering must not break execution).
/// AsyncLocal flows through awaits within the request; the scope is restored on dispose.
/// </para>
/// </remarks>
public static class AiMeteringContext
{
    /// <summary>The agent-turn loop (chat NL utterance) entry path (FR-P2-01).</summary>
    public const string EntryPathText = "text";

    /// <summary>The Click entry path — chip / dispatch endpoint (FR-P1-04).</summary>
    public const string EntryPathClick = "click";

    /// <summary>The Event entry path — event rules, e.g. document_uploaded (FR-P1-03).</summary>
    public const string EntryPathEvent = "event";

    /// <summary>Coded composite workflows — e.g. the daily-briefing narrate composite (FR-P0-06).</summary>
    public const string EntryPathCoded = "coded";

    private static readonly AsyncLocal<AiMeteringScope?> Scope = new();

    /// <summary>The current ambient metering scope; null when no entry seam has begun one.</summary>
    public static AiMeteringScope? Current => Scope.Value;

    /// <summary>
    /// Begin a metering scope for the current async flow. Dispose restores the previous
    /// scope. Nested scopes overwrite (the innermost seam wins) — in practice entry seams
    /// do not nest, except the text loop invoking the dispatch seam in-process, where the
    /// dispatch code deliberately reads the OUTER ("text") scope for attribution by using
    /// <see cref="Current"/> before beginning any scope of its own.
    /// </summary>
    public static IDisposable Begin(string? tenantId, string? userId, string entryPath)
    {
        var previous = Scope.Value;
        Scope.Value = new AiMeteringScope(tenantId, userId, entryPath);
        return new ScopeRestorer(previous);
    }

    private sealed class ScopeRestorer : IDisposable
    {
        private readonly AiMeteringScope? _previous;

        public ScopeRestorer(AiMeteringScope? previous) => _previous = previous;

        public void Dispose() => Scope.Value = _previous;
    }
}
