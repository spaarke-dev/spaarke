namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Resolves a consumer (CRUD service, frontend, integration) to a specific
/// <c>sprk_analysisplaybook</c> system PK GUID via the Dataverse-backed
/// <c>sprk_playbookconsumer</c> routing table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1R contract</b> per <c>chat-routing-redesign-r1</c> spec § Phase 1R
/// (FR-1R-02 / FR-1R-03 / FR-1R-04). Replaces the per-consumer
/// <c>Workspace__*PlaybookId</c> environment-variable lookup pattern that
/// shipped in §1.7 Stable-ID migration. The owner-managed Dataverse table is
/// the new source of truth; this facade is the single point of access for all
/// BFF consumers.
/// </para>
/// <para>
/// <b>ADR-013 facade boundary</b>: this interface lives in
/// <c>Services/Ai/PublicContracts/</c> so external CRUD-side callers
/// (<c>MatterPreFillService</c>, <c>ProjectPreFillService</c>,
/// <c>WorkspaceAiService</c>, <c>WorkspaceFileEndpoints</c>,
/// <c>SessionSummarizeOrchestrator</c>, <c>AppOnlyAnalysisService</c>) can
/// inject the routing decision without depending on any AI-internal
/// orchestration, lookup, or Dataverse-internal type. The concrete impl in
/// <c>ConsumerRoutingService</c> queries via <see cref="Spaarke.Dataverse.IGenericEntityService"/>
/// and caches via <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// per the same precedent as <see cref="PlaybookLookupService"/>.
/// </para>
/// <para>
/// <b>ADR-015 telemetry hygiene</b>: implementations log only deterministic
/// identifiers + cache outcome — <c>consumerType</c>, <c>consumerCode</c>,
/// <c>env</c>, <c>cacheHit</c>, <c>durationMs</c>, <c>resolvedPlaybookId</c>.
/// Never log <see cref="IRoutingContext"/> field values that could carry
/// user-supplied content (none today — both <c>MimeType</c> and
/// <c>DocumentType</c> are deterministic identifiers — but the contract is
/// enforced regardless).
/// </para>
/// </remarks>
public interface IConsumerRoutingService
{
    /// <summary>
    /// Resolve the playbook GUID for the given consumer + context. Returns
    /// <c>null</c> when no routing record matches; callers are expected to
    /// fall back to their existing graceful-degrade path (typed-options
    /// <c>WorkspaceOptions.*PlaybookId</c> read, or a feature-disabled
    /// response — caller's choice; this facade is silent on what "no match"
    /// means semantically).
    /// </summary>
    /// <param name="consumerType">
    /// Stable consumer-type code (lower-kebab-case, no spaces). MUST match
    /// the <c>sprk_consumertype</c> value on a <c>sprk_playbookconsumer</c>
    /// row. Examples: <c>matter-pre-fill</c>, <c>project-pre-fill</c>,
    /// <c>ai-summary</c>, <c>summarize-file</c>, <c>chat-summarize</c>,
    /// <c>email-analysis</c>. Required.
    /// </param>
    /// <param name="consumerCode">
    /// Optional sub-discriminator within a consumer type. Defaults to
    /// <c>"default"</c>. Resolution algorithm prefers a record whose
    /// <c>sprk_consumercode</c> matches exactly, then falls back to
    /// <c>"default"</c>. Pass <c>null</c> or empty string to use the default.
    /// </param>
    /// <param name="context">
    /// Optional <see cref="IRoutingContext"/> for compositional matching
    /// against <c>sprk_matchconditions</c> JSON on the routing record.
    /// Pass <c>null</c> when the consumer has no per-request scope keys
    /// (e.g., pre-fill flows that always use the default routing).
    /// </param>
    /// <param name="environment">
    /// Environment scope to match (<c>dev</c>, <c>test</c>, <c>prod</c>). When
    /// <c>null</c>, the implementation reads the current environment from
    /// <c>IHostEnvironment.EnvironmentName</c>. Records whose
    /// <c>sprk_environment</c> equals the parameter, <c>"*"</c>, <c>null</c>,
    /// or empty string match; specific-environment records win over wildcard.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <c>sprk_analysisplaybook</c> system PK GUID
    /// (<c>sprk_analysisplaybookid</c>) of the highest-priority matching
    /// record, or <c>null</c> when no record matches.
    /// </returns>
    Task<Guid?> ResolveAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// R7 Wave 12.3 (2026-07-02) — Linear AI Consumer routing. Same resolution
    /// algorithm as <see cref="ResolveAsync"/> (consumer-code + environment +
    /// match-conditions + priority tiebreak), but returns the row's
    /// <c>sprk_action</c> lookup value (a <c>sprk_analysisaction</c> row id)
    /// instead of the <c>sprk_playbook</c> value. Callers on the Linear path
    /// (<see cref="Sprk.Bff.Api.Services.Ai.LinearConsumers.IActionResolver"/>)
    /// use this to dispatch directly to an Action row without going through the
    /// Playbook Engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dispatch precedence</b>: consumers that support both paths (Linear
    /// primary, engine fallback during migration) invoke
    /// <c>ResolveActionAsync</c> FIRST; if it returns non-null the Linear
    /// dispatch fires; else they fall through to <see cref="ResolveAsync"/> for
    /// the playbook-engine dispatch.
    /// </para>
    /// <para>
    /// <b>Data model</b>: <c>sprk_playbookconsumer</c> rows may populate:
    /// <list type="bullet">
    ///   <item><c>sprk_action</c> only — pure Linear (post-migration state).</item>
    ///   <item><c>sprk_playbook</c> only — pure engine (legacy, non-migrated).</item>
    ///   <item>Both — transitional (Linear-primary + engine-fallback in-place).</item>
    /// </list>
    /// A row with NEITHER populated is an admin error; both resolve methods return null.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The <c>sprk_analysisaction</c> row GUID of the highest-priority matching
    /// record, or <c>null</c> when no record matches or the matched row has an
    /// empty <c>sprk_action</c> lookup value.
    /// </returns>
    Task<Guid?> ResolveActionAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-P0-03 (spaarke-ai-architecture-redesign-r1 task 004) — resolve the
    /// FULL <see cref="Binding"/> contract for the given consumer + context:
    /// the matched <c>sprk_playbookconsumer</c> row (all §6.2 columns —
    /// ucid, tool description, disposition, chip transitions, risk, capture
    /// mode, on-event bindings, surfaces, model-tier override) joined with the
    /// execution fields of its target <c>sprk_analysisaction</c> row (kind,
    /// workflow class, input schema, model tier).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same resolution algorithm as <see cref="ResolveAsync"/> /
    /// <see cref="ResolveActionAsync"/> (consumer-code + environment +
    /// match-conditions + priority tiebreak). A row qualifies when EITHER
    /// target lookup (<c>sprk_playbook</c> or <c>sprk_action</c>) is
    /// populated; rows with neither are admin errors and are skipped.
    /// </para>
    /// <para>
    /// <b>Legacy tolerance</b>: rows created before the task-003 schema
    /// extension resolve without error; null new columns map to the documented
    /// safe defaults on <see cref="Binding"/> (Informational / None /
    /// LoopElicitation / Prompted / empty lists / null tiers).
    /// </para>
    /// <para>
    /// <b>ADR-039</b>: this contract is the ONLY routing contract — later
    /// phases (Event Rules, Output Router, loop tool-projection, confirmation
    /// gate, chips) consume it rather than adding routing config elsewhere.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The full <see cref="Binding"/> contract of the highest-priority
    /// matching record, or <c>null</c> when no record matches (callers
    /// graceful-degrade exactly as for <see cref="ResolveAsync"/>).
    /// </returns>
    Task<Binding?> ResolveBindingAsync(
        string consumerType,
        string? consumerCode = "default",
        IRoutingContext? context = null,
        string? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-P1-03 (spaarke-ai-architecture-redesign-r1 task 022) — resolve the
    /// ORDERED Event-path members for a surface event: every enabled
    /// <c>sprk_playbookconsumer</c> row (any consumer type) whose
    /// <c>sprk_oneventbindings</c> JSON declares membership in
    /// <paramref name="eventName"/>, environment-filtered, ordered by the
    /// membership's <c>order</c> value (ascending; ties break on
    /// <c>sprk_priority</c> then row id for determinism).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ADR-039</b>: this method IS the Event path's routing read — event rules
    /// are declarative Binding data, and this is the only place they are
    /// interpreted. It extends THIS service (rather than introducing a second
    /// query path) so the Binding query + row→contract mapping stay in one
    /// implementation (CLAUDE.md §11 extension-over-new).
    /// </para>
    /// <para>
    /// <b>Resolution semantics vs the per-consumer methods</b>: no consumer-code
    /// or match-conditions filtering (an event fires platform-wide, not for one
    /// consumer); environment filtering matches
    /// <see cref="ResolveBindingAsync"/>. Malformed membership JSON degrades to
    /// non-membership (routing never throws). Results cache 5 minutes per
    /// (event, environment) — same TTL policy as the other resolves.
    /// </para>
    /// </remarks>
    /// <param name="eventName">Closed platform event token (e.g. <c>document_uploaded</c>). Required.</param>
    /// <param name="environment">Environment scope; null reads <c>IHostEnvironment.EnvironmentName</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered members; empty when no enabled Binding declares the event (callers graceful-degrade).</returns>
    Task<IReadOnlyList<Binding>> ResolveEventBindingsAsync(
        string eventName,
        string? environment = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-request routing context passed to
/// <see cref="IConsumerRoutingService.ResolveAsync"/> for compositional
/// matching against <c>sprk_matchconditions</c> JSON predicates on
/// <c>sprk_playbookconsumer</c> records.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extensibility</b>: new scope dimensions are added by extending this
/// interface + updating <c>ConsumerRoutingService.TryMatchConditions</c> + the
/// JSON schema at
/// <c>projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json</c>
/// in lockstep. Unknown keys in <c>sprk_matchconditions</c> are IGNORED (defensive
/// forward-compat — older BFFs handling newer routing records).
/// </para>
/// <para>
/// <b>ADR-015 binding</b>: every field MUST be a deterministic identifier
/// (MIME type, classification label, etc.). MUST NOT carry user-supplied
/// message text, file content, or any payload that crosses the data-governance
/// boundary.
/// </para>
/// </remarks>
public interface IRoutingContext
{
    /// <summary>
    /// Optional MIME type for content-aware routing (e.g., select
    /// <c>summarize-pdf</c> playbook for <c>application/pdf</c>). Matched
    /// against the <c>"mimeType"</c> key of <c>sprk_matchconditions</c>.
    /// </summary>
    string? MimeType { get; }

    /// <summary>
    /// Optional classified document type for routing (e.g., select an
    /// <c>summarize-nda</c> playbook for <c>"nda"</c>). Available after the
    /// Phase 4 classification pipeline; null until then. Matched against the
    /// <c>"documentType"</c> key of <c>sprk_matchconditions</c>.
    /// </summary>
    string? DocumentType { get; }
}

/// <summary>
/// Canonical default record-shape implementation of
/// <see cref="IRoutingContext"/>. Use this from BFF consumers; reserve the
/// interface for advanced cases that need a custom implementation.
/// </summary>
public sealed record RoutingContext : IRoutingContext
{
    /// <inheritdoc />
    public string? MimeType { get; init; }

    /// <inheritdoc />
    public string? DocumentType { get; init; }
}
