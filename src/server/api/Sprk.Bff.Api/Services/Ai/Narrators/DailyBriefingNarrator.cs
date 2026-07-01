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
//   GenerateChannelNarratives    | Load channelAction → Task.WhenAll over req.Channels
//   ValidateEntityNames          | Build allowList from req → scrub combined narrative
//   ReturnResponse               | return new DailyBriefingNarrateResponse {...}
//
// The Start node (just binds the request to scope `start`) and LoadKnowledge node
// (R4 placeholder pass-through) collapse into method arguments — no equivalents needed
// because data flow is C# method arguments, not template references.
//
// FEATURE FLAG: gated by Features:NarrateUseCodeBasedNarrator (default false). HandleNarrate
// branches on this flag; flag-off path is unchanged (existing playbook engine via
// IInvokePlaybookAi). Toggle on at App Service level to compare.
//
// References:
//   - projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md
//   - projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-t116-narrate-systematic-assessment.md
//   - projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json
//   - Action JPS bodies: projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-*.action.json

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Code-defined narrator for the Daily Briefing /narrate endpoint. Replaces the playbook
/// engine path with explicit C# calls when feature flag <c>Features:NarrateUseCodeBasedNarrator</c>
/// is enabled.
/// </summary>
/// <remarks>
/// Unsealed (R7 Wave 12 post-T135 CI fix 2026-06-30 — PR #520) so
/// <see cref="NullDailyBriefingNarrator"/> can subclass it for the compound-OFF kill-switch
/// path (mirrors <see cref="Chat.NullSessionSummarizeOrchestrator"/> + ADR-032 §F.1).
/// </remarks>
public class DailyBriefingNarrator
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";
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
    /// dereferenced. Matches the canonical pattern in <see cref="Chat.SessionSummarizeOrchestrator"/>.
    /// </summary>
    protected DailyBriefingNarrator(ILogger<DailyBriefingNarrator> logger)
    {
        _actions = null!;
        _llm = null!;
        _scrubber = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute the /narrate workflow end-to-end. Loads Actions from Dataverse, calls the LLM
    /// once for TL;DR + once per channel, validates groundedness, returns the assembled response.
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

        var tldrPayload = new
        {
            categories = req.Categories,
            priorityItems = req.PriorityItems,
            channels = req.Channels,
            totalNotificationCount = req.TotalNotificationCount
        };
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

        // ── Step 3 (playbook node: GenerateChannelNarratives) — fan-out over channels ──
        var channelAction = await _actions.GetActionByCodeAsync(ChannelActionCode, ct)
            ?? throw new InvalidOperationException(
                $"DailyBriefingNarrator: Action {ChannelActionCode} not found in Dataverse.");

        if (string.IsNullOrWhiteSpace(channelAction.OutputSchemaJson))
        {
            throw new InvalidOperationException(
                $"DailyBriefingNarrator: Action {ChannelActionCode} has no OutputSchemaJson.");
        }

        // ── R7 Wave 12 T132 — TLDR ↔ Activity Notes consistency ──
        // Chain the TLDR result as an additional input to per-channel narrative generation.
        // Operator UAT requirement (wave12 plan §2.1): items mentioned in TLDR.keyTakeaways
        // or TLDR.topAction MUST have corresponding details in Activity Notes bullets.
        //
        // The TLDR is computed FIRST (above). We pass its summary / keyTakeaways / topAction
        // into each per-channel call as a `tldr` payload field so the LLM can ensure its
        // narrative bullets cover TLDR-referenced items.
        //
        // The BRIEF-NARRATE-CHANNEL Action's `sprk_systemprompt` is amended to instruct
        // the LLM to use this `tldr` input as a coverage requirement (operator-tunable
        // surface — preserves the no-hardcoded-LLM-behavior-in-C# rule from §G Home A).
        var tldrContextForChannels = new
        {
            summary = tldr.Summary,
            keyTakeaways = tldr.KeyTakeaways,
            topAction = tldr.TopAction
        };

        var channelTasks = req.Channels.Select(async ch =>
        {
            var channelPayload = new { channel = ch.Label, items = ch.Items, tldr = tldrContextForChannels };
            var channelRaw = await CallLlmStructuredAsync(
                actionCode: ChannelActionCode,
                systemPrompt: channelAction.SystemPrompt,
                inputPayload: channelPayload,
                outputSchemaJson: channelAction.OutputSchemaJson!,
                temperature: channelAction.Temperature,
                ct);

            var llmOut = JsonSerializer.Deserialize<ChannelLlmOutput>(channelRaw, OutputDeserializerOptions)
                ?? new ChannelLlmOutput { Channel = ch.Label, Narrative = Array.Empty<string>() };
            return (ch, llmOut);
        });
        var channels = await Task.WhenAll(channelTasks);

        // ── Step 4 (playbook node: ValidateEntityNames) — defense-in-depth scrub ──
        var allowList = BuildAllowList(req);
        var candidateText = BuildCandidateText(tldr, channels.Select(c => c.llmOut));
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
            ChannelNarratives = channels.Select(c => new ChannelNarrationResult
            {
                Category = c.ch.Category,
                Bullets = c.llmOut.Narrative
                    .Select(n => EnrichBulletWithEntityRefs(n, c.ch.Items))
                    .ToArray()
            }).ToArray(),
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
    /// Equivalent of the playbook's candidateText expression:
    ///   join '\n\n' tldrResult.summary tldrResult.keyTakeaways tldrResult.topAction
    ///               (join '\n' (flatten (map channelNarrationResults 'narrative')))
    /// Done as straight string.Join. Compiler enforces field names.
    /// </summary>
    private static string BuildCandidateText(TldrResult tldr, IEnumerable<ChannelLlmOutput> channels) =>
        string.Join("\n\n",
            tldr.Summary,
            string.Join("\n", tldr.KeyTakeaways),
            tldr.TopAction,
            string.Join("\n", channels.SelectMany(c => c.Narrative)));

    /// <summary>
    /// Enrich a single LLM-emitted narrative bullet with per-bullet entity link metadata.
    /// Matches the narrative text against the channel's input items by `regardingName` OR
    /// `title` (case-insensitive substring; names must be ≥3 chars to avoid noise).
    ///
    /// Output:
    ///   - <c>ItemIds</c>: every input item whose name appears in the narrative
    ///   - <c>PrimaryEntityType/Id/Name</c>: click-through target for the widget.
    ///     Resolution order (R7 Wave 12 task 135):
    ///       1. First match whose <c>RegardingId</c> is populated → navigate to that
    ///          regarding record (matter or project). This is the dominant case across
    ///          all 6 entity types — the collector projection sets RegardingId to the
    ///          parent matter/project GUID (or to self for Matter/Project rows).
    ///       2. First match whose <c>SourceEntityType</c> is populated → navigate to
    ///          the source record itself (orphan fallback — e.g., a Task with no
    ///          sprk_regardingmatter, a To Do with no regarding). Without this fallback
    ///          orphan bullets render with no link in the widget (NarrativeBullet hides
    ///          the link node when primaryEntityType/Id are empty).
    ///       3. First match overall → name-only fallback (no link).
    ///
    /// If no match is found, returns a bullet with text only (widget renders as plain text).
    /// This is best-effort post-processing; the LLM does not emit per-bullet IDs.
    /// </summary>
    private static NarrativeBulletDto EnrichBulletWithEntityRefs(
        string narrativeText,
        ChannelItemDto[] channelItems)
    {
        if (string.IsNullOrWhiteSpace(narrativeText) || channelItems is null || channelItems.Length == 0)
        {
            return new NarrativeBulletDto { Narrative = narrativeText ?? string.Empty };
        }

        var matches = new List<ChannelItemDto>();
        foreach (var item in channelItems)
        {
            if (TextMentionsName(narrativeText, item.RegardingName) ||
                TextMentionsName(narrativeText, item.Title))
            {
                matches.Add(item);
            }
        }

        // R7 W12 feedback items 2/3/4 (2026-07-01) — build per-bullet references[]
        // for widget-side inline citations. Two categories:
        //   - Mentioned refs: item's RegardingName or Title appears in the narrative
        //     text. Widget wraps the name in a clickable Link (opens modal).
        //   - Implicit refs: bullet aggregates additional channel items whose names
        //     don't appear in text (LLM said "several others" instead of naming).
        //     Widget renders as trailing [N] citations.
        //
        // Coverage rule: when the LLM emits aggregated statements ("several to-dos
        // added recently"), the collector's channel items provide the ground truth.
        // If the LLM mentioned only ONE item explicitly but the channel has 5 items,
        // we surface all 5 — mentioned=true for the named one, mentioned=false for
        // the other 4. Operator can click any of them.
        var references = BuildBulletReferences(narrativeText, matches, channelItems);

        if (matches.Count == 0)
        {
            return new NarrativeBulletDto
            {
                Narrative = narrativeText,
                References = references,
            };
        }

        var itemIds = matches
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();

        // Tier 1 — first match with a usable RegardingId (matter/project link).
        // Dominant case across all 6 entity types when the source row has a
        // regarding matter (or is a self-regarding Matter/Project row).
        var primaryByRegarding = matches.FirstOrDefault(m => !string.IsNullOrEmpty(m.RegardingId));
        if (primaryByRegarding is not null)
        {
            return new NarrativeBulletDto
            {
                Narrative = narrativeText,
                ItemIds = itemIds,
                PrimaryEntityType = primaryByRegarding.RegardingEntityType ?? string.Empty,
                PrimaryEntityId = primaryByRegarding.RegardingId ?? string.Empty,
                PrimaryEntityName = primaryByRegarding.RegardingName ?? string.Empty,
                References = references,
            };
        }

        // Tier 2 — orphan fallback (R7 Wave 12 task 135). Use the source entity
        // type + the bullet's own Id + Title so the widget can navigate to the
        // source record (e.g., a Task with no regarding matter still gets a
        // clickable link to its sprk_event row).
        var primaryBySource = matches.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.SourceEntityType) && !string.IsNullOrEmpty(m.Id));
        if (primaryBySource is not null)
        {
            return new NarrativeBulletDto
            {
                Narrative = narrativeText,
                ItemIds = itemIds,
                PrimaryEntityType = primaryBySource.SourceEntityType ?? string.Empty,
                PrimaryEntityId = primaryBySource.Id ?? string.Empty,
                PrimaryEntityName = !string.IsNullOrEmpty(primaryBySource.Title)
                    ? primaryBySource.Title
                    : (primaryBySource.RegardingName ?? string.Empty),
                References = references,
            };
        }

        // Tier 3 — neither regarding nor source-entity-type usable. Widget will
        // hide the link node; bullet still renders text + actions.
        var primary = matches[0];
        return new NarrativeBulletDto
        {
            Narrative = narrativeText,
            ItemIds = itemIds,
            PrimaryEntityType = primary.RegardingEntityType ?? string.Empty,
            PrimaryEntityId = primary.RegardingId ?? string.Empty,
            PrimaryEntityName = primary.RegardingName ?? string.Empty,
            References = references,
        };
    }

    /// <summary>
    /// R7 W12 feedback items 2/3/4 — build the References array for a bullet.
    /// Combines two sources:
    ///   1. Explicit mentions (RegardingName or Title appears in narrativeText) →
    ///      <c>Mentioned=true</c>, widget renders name as inline Link.
    ///   2. Implicit refs from remaining <paramref name="channelItems"/> whose
    ///      names don't appear in text — <c>Mentioned=false</c>, widget renders
    ///      as trailing <c>[N]</c> citations.
    ///
    /// Each reference resolves to a click-through target using the same tiered
    /// logic as the primary entity (regarding first, then source entity type).
    /// Dedupes by target (EntityType + EntityId) so a single record cited by
    /// both RegardingName and Title only produces one reference.
    ///
    /// Ordering: mentioned refs by first-appearance in text (left-to-right),
    /// then implicit refs by channel-items order. Index numbers reflect this
    /// order so trailing [1][2][3] citations line up with the narrative reading.
    /// </summary>
    private static NarrativeBulletReferenceDto[] BuildBulletReferences(
        string narrativeText,
        IReadOnlyList<ChannelItemDto> matches,
        ChannelItemDto[] channelItems)
    {
        if (channelItems is null || channelItems.Length == 0)
        {
            return Array.Empty<NarrativeBulletReferenceDto>();
        }

        // (a) Score matches by first-appearance in narrative text so inline
        //     links render in reading order. Items not found in text get
        //     int.MaxValue and sort last.
        var scored = matches
            .Select(m => new
            {
                Item = m,
                Position = FirstMentionIndex(narrativeText, m),
            })
            .OrderBy(x => x.Position)
            .ToList();

        // (b) Add channel items that WEREN'T mentioned — implicit refs.
        var mentionedIds = new HashSet<string>(
            matches.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)),
            StringComparer.OrdinalIgnoreCase);
        var implicitItems = channelItems
            .Where(ci => !string.IsNullOrEmpty(ci.Id) && !mentionedIds.Contains(ci.Id))
            .ToList();

        var references = new List<NarrativeBulletReferenceDto>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int index = 1;

        foreach (var scoredMatch in scored)
        {
            var refDto = BuildReferenceFor(scoredMatch.Item, index, mentioned: true);
            var targetKey = refDto.EntityType + "|" + refDto.EntityId;
            if (string.IsNullOrEmpty(refDto.EntityId) || seenTargets.Add(targetKey))
            {
                references.Add(refDto);
                index++;
            }
        }

        foreach (var implicitItem in implicitItems)
        {
            var refDto = BuildReferenceFor(implicitItem, index, mentioned: false);
            var targetKey = refDto.EntityType + "|" + refDto.EntityId;
            if (string.IsNullOrEmpty(refDto.EntityId) || seenTargets.Add(targetKey))
            {
                references.Add(refDto);
                index++;
            }
        }

        return references.ToArray();
    }

    /// <summary>
    /// Build a NarrativeBulletReferenceDto for one channel item using the same
    /// tiered click-target logic as the primary entity resolver: regarding first,
    /// then source entity type, then bare fields as fallback.
    /// </summary>
    private static NarrativeBulletReferenceDto BuildReferenceFor(ChannelItemDto item, int index, bool mentioned)
    {
        string entityType;
        string entityId;
        string entityName;

        if (!string.IsNullOrEmpty(item.RegardingId))
        {
            entityType = item.RegardingEntityType ?? string.Empty;
            entityId = item.RegardingId ?? string.Empty;
            entityName = item.RegardingName ?? string.Empty;
        }
        else if (!string.IsNullOrEmpty(item.SourceEntityType) && !string.IsNullOrEmpty(item.Id))
        {
            entityType = item.SourceEntityType ?? string.Empty;
            entityId = item.Id ?? string.Empty;
            entityName = !string.IsNullOrEmpty(item.Title) ? item.Title : (item.RegardingName ?? string.Empty);
        }
        else
        {
            entityType = item.RegardingEntityType ?? string.Empty;
            entityId = item.RegardingId ?? string.Empty;
            entityName = item.RegardingName ?? string.Empty;
        }

        return new NarrativeBulletReferenceDto
        {
            Index = index,
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            Mentioned = mentioned,
        };
    }

    /// <summary>
    /// Returns the character index of the first mention of a channel item in
    /// <paramref name="narrativeText"/>, or int.MaxValue if not mentioned.
    /// Checks RegardingName first, then Title. Case-insensitive, ≥3-char guard.
    /// </summary>
    private static int FirstMentionIndex(string narrativeText, ChannelItemDto item)
    {
        int regardingIdx = NameIndex(narrativeText, item.RegardingName);
        int titleIdx = NameIndex(narrativeText, item.Title);
        int best = int.MaxValue;
        if (regardingIdx >= 0 && regardingIdx < best) best = regardingIdx;
        if (titleIdx >= 0 && titleIdx < best) best = titleIdx;
        return best;
    }

    private static int NameIndex(string narrativeText, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3) return -1;
        return narrativeText.IndexOf(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the narrative text contains the entity name. Ignores names &lt; 3 chars
    /// to avoid false positives on common short tokens (e.g., "A", "Of"). Case-insensitive.
    /// </summary>
    private static bool TextMentionsName(string narrativeText, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3) return false;
        return narrativeText.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shape the BRIEF-NARRATE-CHANNEL LLM emits per iteration. Local DTO — the wire
    /// contract for what we return to the widget is mapped onto ChannelNarrationResult
    /// in the response composition above.
    /// </summary>
    internal sealed record ChannelLlmOutput
    {
        [JsonPropertyName("channel")]
        public string Channel { get; init; } = string.Empty;

        [JsonPropertyName("narrative")]
        public string[] Narrative { get; init; } = Array.Empty<string>();

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; init; }

        [JsonPropertyName("bulletCount")]
        public int BulletCount { get; init; }
    }
}
