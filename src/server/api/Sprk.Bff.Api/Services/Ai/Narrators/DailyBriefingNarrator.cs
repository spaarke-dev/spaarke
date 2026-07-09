// R7 Wave 11 T116 narrator spike (2026-06-30) — DailyBriefingNarrator.
//
// PURPOSE: Code-defined workflow for /api/ai/daily-briefing/narrate. Tests the architectural
// hypothesis that the /narrate runtime path can be implemented as ~150 lines of direct C#
// calls — bypassing the playbook engine entirely — while preserving 100% of operator value
// (prompts, models, tuning still hot-editable in Dataverse Action rows).
//
// This narrator is hand-written but represents what a "playbook → C# compiler" would emit
// if such a compiler existed. Each chunk traces 1:1 to a node in DAILY-BRIEFING-NARRATE:
//
//   Playbook node                | Code below
//   -----------------------------|-------------------------------------------
//   GenerateTldr                 | Load tldrAction → LLM call with full request payload
//   GenerateChannelNarratives    | REMOVED (R5 task 010, FR-A3) — see amendment below
//   ValidateEntityNames          | Build allowList from req → scrub the TLDR (sole LLM output)
//   ReturnResponse               | return new DailyBriefingNarrateResponse {...}
//
// The Start node (just binds the request to scope `start`) and LoadKnowledge node
// (R4 placeholder pass-through) collapse into method arguments — no equivalents needed
// because data flow is C# method arguments, not template references.
//
// DISPATCH (FR-P3-04, task 043): the R7 spike flag is GONE — this narrator IS the
// briefing's `coded` composite workflow, resolved by class reference from its Action row
// (sprk_workflowclass = "DailyBriefingNarrator") via DailyBriefingCompositeService.
// The Binding table decides; there is no engine fallback (NFR-08 hard cutover).
//
// R5 AMENDMENT (task 010, FR-A3, 2026-07-08) — per-channel LLM narrate leg REMOVED.
// R4 and R7 W12 tried prompt-steering to stop cross-item hallucination and failed; the
// per-channel narrate LLM call (BRIEF-NARRATE-CHANNEL) was the primary hallucination
// source. Channel/section content is now built DETERMINISTICALLY from the collector's
// req.Channels view model (BuildDeterministicBullet below) — one bullet per source item,
// zero LLM authorship, so cross-item hallucination is structurally impossible on this leg.
// The TL;DR call remains the SOLE LLM invocation per briefing run. ChannelActionCode +
// the BRIEF-NARRATE-CHANNEL catalog Action are left in place (unused) — retirement is a
// separate task (012); this change only removes the call path.
//
// R5 AMENDMENT (task 013, FR-A4, 2026-07-08) — TL;DR call now receives DETERMINISTIC
// SCAFFOLDING, not a raw data dump. Previously the TL;DR payload was the raw
// categories/priorityItems/channels/totalNotificationCount blob (every item, every field,
// across every channel) — the LLM could in principle "count" or "date" things itself from
// that payload. It now receives DailyBriefingCollector.BuildTldrFacts's TldrFactsDto: a
// small, purpose-built ground-truth object (totalNotificationCount, categoryCounts,
// priorityItemCount, keyDates[], recordNames[]) computed deterministically in C#. The LLM
// composes prose + prioritizes ONLY over these provided facts; it must never introduce a
// fact absent from them (BRIEF-NARRATE-TLDR system prompt updated to instruct this — see
// projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json).
//
// References:
//   - projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md
//   - projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-t116-narrate-systematic-assessment.md
//   - projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json
//   - Action JPS bodies: projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-*.action.json
//   - projects/spaarke-daily-update-service-r5/spec.md FR-A3; tasks/010-remove-per-channel-narrate-leg.poml

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Code-defined narrator for the Daily Briefing — the platform's first <c>coded</c>
/// composite workflow (FR-P3-04). Dispatched exclusively via its catalog Action/Binding
/// rows through <see cref="DailyBriefingCompositeService"/>; the former playbook-engine
/// path and its parallel-run feature flag were deleted (NFR-08 hard cutover, task 043).
/// </summary>
/// <remarks>
/// <para>
/// Unsealed (R7 Wave 12 post-T135 CI fix 2026-06-30 — PR #520) so
/// <see cref="NullDailyBriefingNarrator"/> can subclass it for the compound-OFF kill-switch
/// path (mirrors <see cref="Chat.NullSessionDispatchOrchestrator"/> + ADR-032 §F.1).
/// </para>
/// <para>
/// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007): first <see cref="ICodedWorkflow"/>
/// instance — resolvable by class reference (<c>"DailyBriefingNarrator"</c>) as a
/// <c>coded</c>-kind Action row's <c>sprk_workflowclass</c> names it. The retrofit is
/// interface adoption only: <see cref="ExecuteAsync"/> delegates to the pre-existing
/// <see cref="NarrateAsync"/>; existing callers and behavior are unchanged.
/// </para>
/// </remarks>
public class DailyBriefingNarrator : ICodedWorkflow
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    /// <summary>
    /// R5 task 010 (FR-A3): the per-channel LLM narrate call path that consumed this code
    /// was REMOVED — channel content is now deterministic (see <see cref="BuildDeterministicBullet"/>).
    /// Left in place intentionally (dead-but-present); retiring the const + the
    /// BRIEF-NARRATE-CHANNEL catalog Action row is a separate task (012).
    /// </summary>
    private const string ChannelActionCode = "BRIEF-NARRATE-CHANNEL";

    private static readonly JsonSerializerOptions InputSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true  // matches PromptSchemaRenderer's `## Input` section formatting
    };

    private static readonly JsonSerializerOptions OutputDeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AnalysisActionService _actions;
    private readonly IOpenAiClient _llm;
    private readonly IEntityNameScrubber _scrubber;
    private readonly ILogger<DailyBriefingNarrator> _logger;

    public DailyBriefingNarrator(
        AnalysisActionService actions,
        IOpenAiClient llm,
        IEntityNameScrubber scrubber,
        ILogger<DailyBriefingNarrator> logger)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullDailyBriefingNarrator"/> so the kill-switch
    /// subclass can be constructed when the compound AI gate is OFF and AI dependencies
    /// (<see cref="AnalysisActionService"/> + <see cref="IOpenAiClient"/>) are absent. The
    /// Null override never reads the nulled fields — it throws
    /// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/> before they are
    /// dereferenced. Matches the canonical pattern in <see cref="Chat.SessionDispatchOrchestrator"/>.
    /// </summary>
    protected DailyBriefingNarrator(ILogger<DailyBriefingNarrator> logger)
    {
        _actions = null!;
        _llm = null!;
        _scrubber = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// FR-P0-06: stable class reference an Action row's <c>sprk_workflowclass</c> carries.
    /// Inherited by <see cref="NullDailyBriefingNarrator"/> — the Null peer is the same
    /// workflow identity on the compound-OFF path (ADR-032).
    /// </remarks>
    public string WorkflowClassRef => nameof(DailyBriefingNarrator);

    /// <inheritdoc />
    /// <remarks>
    /// FR-P0-06 convention entry point: deserializes <see cref="CodedWorkflowContext.ArgumentsJson"/>
    /// into the narrator's request DTO and delegates to <see cref="NarrateAsync"/> (the virtual
    /// method — so the <see cref="NullDailyBriefingNarrator"/> kill-switch override flows
    /// through class-ref invocation unchanged). No behavior change to the existing path.
    /// </remarks>
    public async Task<object?> ExecuteAsync(CodedWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        DailyBriefingNarrateRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<DailyBriefingNarrateRequest>(
                context.ArgumentsJson, OutputDeserializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"DailyBriefingNarrator: CodedWorkflowContext.ArgumentsJson is not a valid {nameof(DailyBriefingNarrateRequest)} payload: {ex.Message}",
                nameof(context), ex);
        }

        if (req is null)
        {
            throw new ArgumentException(
                $"DailyBriefingNarrator: CodedWorkflowContext.ArgumentsJson deserialized to null — a {nameof(DailyBriefingNarrateRequest)} JSON object is required.",
                nameof(context));
        }

        return await NarrateAsync(req, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the /narrate workflow end-to-end. Loads the TL;DR Action from Dataverse and
    /// calls the LLM EXACTLY ONCE (R5 task 010, FR-A3 — the per-channel LLM leg was removed;
    /// channel content is now deterministic), scrubs the TL;DR against a grounded allow-list,
    /// returns the assembled response.
    /// </summary>
    public virtual async Task<DailyBriefingNarrateResponse> NarrateAsync(
        DailyBriefingNarrateRequest req,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "DailyBriefingNarrator starting: Categories={CategoryCount}, PriorityItems={PriorityCount}, Channels={ChannelCount}",
            req.Categories.Length, req.PriorityItems.Length, req.Channels.Length);

        // ── Step 2 (playbook node: GenerateTldr) — single LLM call across whole payload ──
        var tldrAction = await _actions.GetActionByCodeAsync(TldrActionCode, ct)
            ?? throw new InvalidOperationException(
                $"DailyBriefingNarrator: Action {TldrActionCode} not found in Dataverse.");

        if (string.IsNullOrWhiteSpace(tldrAction.OutputSchemaJson))
        {
            throw new InvalidOperationException(
                $"DailyBriefingNarrator: Action {TldrActionCode} has no OutputSchemaJson.");
        }

        // R5 task 013 (FR-A4): the TL;DR call receives ONLY deterministically-computed ground-
        // truth facts — never the raw categories/priorityItems/channels dump (that dumped every
        // record and gave the LLM room to "count" or "date" things itself). Prefer the
        // scaffolding the collector already stamped onto the request (req.TldrFacts, the primary
        // path via DailyBriefingCollector.BuildNarrateRequest); fall back to computing it here —
        // still pure C#, still deterministic, never delegated to the LLM — for callers that
        // construct a DailyBriefingNarrateRequest directly (the legacy /narrate leg, or a test
        // driving NarrateAsync without going through the collector).
        var tldrPayload = req.TldrFacts ?? DailyBriefingCollector.BuildTldrFacts(req);
        var tldrRaw = await CallLlmStructuredAsync(
            actionCode: TldrActionCode,
            systemPrompt: tldrAction.SystemPrompt,
            inputPayload: tldrPayload,
            outputSchemaJson: tldrAction.OutputSchemaJson!,
            temperature: tldrAction.Temperature,
            ct);

        var tldr = JsonSerializer.Deserialize<TldrResult>(tldrRaw, OutputDeserializerOptions)
            ?? throw new InvalidOperationException(
                $"DailyBriefingNarrator: TLDR LLM returned unparseable JSON (length={tldrRaw.Length}).");

        // ── Step 3 (was playbook node: GenerateChannelNarratives) — REMOVED (R5 task 010,
        // FR-A3). Channel content is now built DETERMINISTICALLY, straight from the
        // collector's req.Channels view model — one bullet per source item, no LLM call,
        // so cross-item hallucination is structurally impossible on this leg. See the file
        // header "R5 AMENDMENT" note for the full rationale.
        var channelNarrations = req.Channels
            .Select(ch => new ChannelNarrationResult
            {
                Category = ch.Category,
                Bullets = ch.Items.Select(BuildDeterministicBullet).ToArray()
            })
            .ToArray();

        // ── Step 4 (playbook node: ValidateEntityNames) — defense-in-depth scrub over the
        // TL;DR text, the ONLY remaining LLM output in this pipeline. Channel narrations are
        // sourced verbatim from source records (above) and therefore cannot hallucinate — no
        // scrub needed there.
        var allowList = BuildAllowList(req);
        var candidateText = BuildTldrCandidateText(tldr);
        var scrub = _scrubber.Scrub(candidateText, allowList);
        if (scrub.RemovedTerms.Count > 0)
        {
            _logger.LogWarning(
                "DailyBriefingNarrator scrubber removed {RemovedCount} hallucinated entity term(s): {RemovedTerms}",
                scrub.RemovedTerms.Count, string.Join(" | ", scrub.RemovedTerms));
        }

        // ── Step 5 (playbook node: ReturnResponse) — compose final response ──
        // Per the playbook design (sprk_configjson.composeStrategy._comment): the user-visible
        // tldr + channelNarratives carry the PRE-SCRUB structured fields; scrubber output is
        // metadata for monitoring. The widget contract consumes pre-scrub structure.
        var response = new DailyBriefingNarrateResponse
        {
            Tldr = tldr with
            {
                CategoryCount = req.Categories.Length,
                PriorityItemCount = req.PriorityItems.Length
            },
            ChannelNarratives = channelNarrations,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            // Mirror the original playbook design's _validationMetadata sidecar.
            // Only emit when the scrubber actually removed something — null otherwise so
            // the response payload stays clean on the happy path.
            ValidationMetadata = scrub.RemovedTerms.Count > 0
                ? new ValidationMetadataDto
                {
                    ScrubbedText = scrub.ScrubbedText,
                    RemovedTerms = scrub.RemovedTerms.ToArray()
                }
                : null
        };

        _logger.LogInformation(
            "DailyBriefingNarrator completed in {DurationMs}ms: tldr.summary.len={SummaryLen}, takeaways={TakeawayCount}, channels={ChannelCount}, scrubberRemoved={RemovedCount}",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            response.Tldr.Summary.Length,
            response.Tldr.KeyTakeaways.Length,
            response.ChannelNarratives.Length,
            scrub.RemovedTerms.Count);

        return response;
    }

    /// <summary>
    /// Single LLM call. Composes the prompt as the Action.SystemPrompt followed by a
    /// "## Input" section containing the indented runtime JSON (mirrors what
    /// PromptSchemaRenderer's Layer 2 does in the playbook path, so the LLM sees the
    /// same prompt shape it sees today).
    /// </summary>
    private async Task<string> CallLlmStructuredAsync(
        string actionCode,
        string systemPrompt,
        object inputPayload,
        string outputSchemaJson,
        decimal? temperature,
        CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(inputPayload, InputSerializerOptions);
        var fullPrompt = systemPrompt + "\n\n## Input\n\n" + inputJson + "\n";

        _logger.LogDebug(
            "DailyBriefingNarrator calling LLM: action={ActionCode}, promptLen={PromptLen}, schemaLen={SchemaLen}",
            actionCode, fullPrompt.Length, outputSchemaJson.Length);

        var raw = await _llm.GetStructuredCompletionRawAsync(
            prompt: fullPrompt,
            jsonSchema: BinaryData.FromString(outputSchemaJson),
            schemaName: actionCode.Replace('-', '_'),  // schema names cannot contain hyphens
            model: null,                          // use configured default
            maxOutputTokens: null,
            temperature: temperature.HasValue ? (float)temperature.Value : (float?)null,
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogDebug(
            "DailyBriefingNarrator LLM returned: action={ActionCode}, rawLen={RawLen}",
            actionCode, raw.Length);

        return raw;
    }

    /// <summary>
    /// Equivalent of the playbook's allowList expression:
    ///   distinct (concat (map start.priorityItems 'title')
    ///                    (flatMap start.channels 'items.regardingName')
    ///                    (flatMap start.channels 'items.title'))
    /// Done as straight LINQ. Compiler enforces field names.
    /// </summary>
    private static string[] BuildAllowList(DailyBriefingNarrateRequest req) =>
        req.PriorityItems.Select(p => p.Title)
            .Concat(req.Channels.SelectMany(ch => ch.Items.Select(i => i.RegardingName)))
            .Concat(req.Channels.SelectMany(ch => ch.Items.Select(i => i.Title)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Candidate text for the entity-name scrub — the TL;DR is the ONLY remaining LLM
    /// output in this pipeline (R5 task 010, FR-A3), so this is now just the TL;DR's own
    /// fields. Channel narrations are built from source records (<see cref="BuildDeterministicBullet"/>)
    /// and are excluded — they cannot hallucinate.
    /// </summary>
    private static string BuildTldrCandidateText(TldrResult tldr) =>
        string.Join("\n\n", tldr.Summary, string.Join("\n", tldr.KeyTakeaways), tldr.TopAction);

    /// <summary>
    /// R5 task 010 (FR-A3): build one <see cref="NarrativeBulletDto"/> DIRECTLY from a single
    /// source item — no LLM call, no free-text matching. Each channel item maps 1:1 to a
    /// bullet, so cross-item hallucination (R4 / R7 W12's unresolved failure mode) is
    /// structurally impossible: the bullet can only say what the source record says.
    /// </summary>
    /// <remarks>
    /// Preserves the same 3-tier click-through resolution the pre-R5 LLM-narrative-matching
    /// path used (R7 Wave 12 task 135), now applied directly to the single source item instead
    /// of a text-matched set:
    ///   1. <c>RegardingId</c> populated → navigate to the regarding record (matter/project;
    ///      dominant case — the collector sets RegardingId to the parent, or to self for
    ///      self-regarding Matter/Project rows).
    ///   2. No RegardingId but <c>SourceEntityType</c> + <c>Id</c> populated → orphan fallback,
    ///      navigate to the source record itself (e.g., a Task/To Do with no regarding matter).
    ///   3. Neither usable → plain bullet, no link (widget hides the link node).
    /// </remarks>
    private static NarrativeBulletDto BuildDeterministicBullet(ChannelItemDto item)
    {
        var itemIds = string.IsNullOrEmpty(item.Id) ? Array.Empty<string>() : new[] { item.Id };

        // Tier 1 — regarding record (matter/project link). Dominant case.
        if (!string.IsNullOrEmpty(item.RegardingId))
        {
            return new NarrativeBulletDto
            {
                Narrative = item.Title,
                ItemIds = itemIds,
                PrimaryEntityType = item.RegardingEntityType ?? string.Empty,
                PrimaryEntityId = item.RegardingId ?? string.Empty,
                PrimaryEntityName = item.RegardingName ?? string.Empty,
            };
        }

        // Tier 2 — orphan fallback: navigate to the source record itself.
        if (!string.IsNullOrEmpty(item.SourceEntityType) && !string.IsNullOrEmpty(item.Id))
        {
            return new NarrativeBulletDto
            {
                Narrative = item.Title,
                ItemIds = itemIds,
                PrimaryEntityType = item.SourceEntityType ?? string.Empty,
                PrimaryEntityId = item.Id ?? string.Empty,
                PrimaryEntityName = !string.IsNullOrEmpty(item.Title)
                    ? item.Title
                    : (item.RegardingName ?? string.Empty),
            };
        }

        // Tier 3 — neither usable. Plain bullet, no link.
        return new NarrativeBulletDto
        {
            Narrative = item.Title,
            ItemIds = itemIds,
        };
    }
}
