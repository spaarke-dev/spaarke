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
public sealed class DailyBriefingNarrator
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
    /// Execute the /narrate workflow end-to-end. Loads Actions from Dataverse, calls the LLM
    /// once for TL;DR + once per channel, validates groundedness, returns the assembled response.
    /// </summary>
    public async Task<DailyBriefingNarrateResponse> NarrateAsync(
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
    ///   - <c>PrimaryEntityType/Id/Name</c>: the first matched item with a usable
    ///     RegardingId — provides the widget with a click-through target
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

        if (matches.Count == 0)
        {
            return new NarrativeBulletDto { Narrative = narrativeText };
        }

        // Primary = first matched item that has a usable RegardingId (so the widget
        // can build a navigation link). Falls back to first match overall.
        var primary = matches.FirstOrDefault(m => !string.IsNullOrEmpty(m.RegardingId))
                      ?? matches[0];

        return new NarrativeBulletDto
        {
            Narrative = narrativeText,
            ItemIds = matches
                .Select(m => m.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToArray(),
            PrimaryEntityType = primary.RegardingEntityType ?? string.Empty,
            PrimaryEntityId = primary.RegardingId ?? string.Empty,
            PrimaryEntityName = primary.RegardingName ?? string.Empty,
        };
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
