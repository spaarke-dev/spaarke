using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <b>OutcomeCard v1</b> — the disposition-level rendering contract for a side-effect outcome
/// (spaarke-ai-architecture-redesign-r2 task 011, spec FR-A0-02, design D-F2).
///
/// <para>
/// <b>What it formalizes</b>. R1 already shipped the two hard primitives of a side-effect
/// outcome: (1) a SERVER-composed record/deep link (<c>Api/Agent/HandoffUrlBuilder.cs</c> and the
/// gate-resume <c>{orgUrl}/main.aspx?pagetype=entityrecord…</c> composer) — never a model-invented
/// URL; and (2) an AUDIENCE-SPLIT summary on <c>ToolResult</c> — the user-facing sentence
/// (<c>ToolResultMetadataKeys.UserSummary</c>, ToolResult.cs:326) vs. the model-facing
/// <c>Summary</c>. OutcomeCard v1 wraps BOTH into ONE shape so the Completion Engine (FR-A1-06/07)
/// and Compose r2 render every side-effect outcome through a single contract instead of ad-hoc
/// markdown. It is a NEW shape OVER existing primitives — it changes nothing about how
/// <c>UserSummary</c>/<c>HandoffUrlBuilder</c> behave (task-011 escalation boundary).
/// </para>
///
/// <para>
/// <b>Two hard invariants, enforced by construction</b> (see <see cref="ForStoredOutcome"/>):
/// <list type="bullet">
///   <item><description><b>Store-before-render (ADR-040)</b>: an OutcomeCard renders a STORED
///     ledger output — <see cref="LedgerOutputKey"/> (the <c>{bindingId}@t{n}</c> key of a
///     <c>SessionOutput</c>) MUST be present and non-empty. A card that references no stored
///     outcome cannot be produced.</description></item>
///   <item><description><b>Server-composed link (never model-composed)</b>: <see cref="Link"/>
///     MUST originate from server-side composition (<see cref="OutcomeCardLink.ServerComposed"/>).
///     A model-claimed link (<see cref="OutcomeCardLink.ModelClaimed"/>) is rejected — the model
///     never gets to invent the URL a user clicks.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Tolerant-reader + versioning</b> (project rule; consistent across the seven A0 contracts).
/// <see cref="Version"/> carries <see cref="SchemaVersion"/>. v1 is ADDITIVE-ONLY: new optional
/// fields may be added in later minor revisions; existing fields never change meaning or type.
/// Consumers MUST ignore unknown fields — <see cref="JsonOptions"/> deserializes with unknown
/// members ignored by default (System.Text.Json), so a producer on a newer additive revision never
/// breaks an older consumer. Serialize/deserialize through <see cref="JsonOptions"/> so producer
/// and consumer share one deterministic wire shape.
/// </para>
///
/// <para>
/// <b>ADR-013 placement</b>: this DTO lives under <c>Services/Ai/PublicContracts/</c> and has NO
/// dependency on AI-internal types — CRUD/Compose consumers reach it only through this facade.
/// The reference producer wiring (map a stored <c>SessionOutput</c> → <see cref="LedgerOutputKey"/>
/// and a <c>HandoffUrlBuilder</c> output → <see cref="OutcomeCardLink.ServerComposed"/>) is
/// server-side AI code that lives OUTSIDE this facade; the guard factory below is the seam it
/// calls. ADR-039: OutcomeCard introduces no second dispatch/render mechanism — it is a
/// disposition-level shape over existing side-effect results.
/// </para>
/// </summary>
public sealed record OutcomeCard
{
    /// <summary>Current schema version of the OutcomeCard contract (additive-only within a major).</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Shared serializer options — camelCase wire names, unknown members ignored on read
    /// (tolerant reader), null-valued optional fields omitted. Producer and consumer MUST both use
    /// this so the shape Compose r2 consumes is byte-stable and never a local variant.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // System.Text.Json ignores unknown members on deserialize by default — no throw.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Contract version. Always <see cref="SchemaVersion"/> for a producer on this build.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = SchemaVersion;

    /// <summary>
    /// Addressable ledger key (<c>{bindingId}@t{n}</c>) of the STORED <c>SessionOutput</c> this card
    /// renders. Non-empty by construction (ADR-040 store-before-render).
    /// </summary>
    [JsonPropertyName("ledgerOutputKey")]
    public required string LedgerOutputKey { get; init; }

    /// <summary>Terminal disposition of the side effect this card reports.</summary>
    [JsonPropertyName("status")]
    public required OutcomeStatus Status { get; init; }

    /// <summary>Audience-split summary — user-facing sentence vs. internal detail (from UserSummary).</summary>
    [JsonPropertyName("summary")]
    public required OutcomeSummary Summary { get; init; }

    /// <summary>
    /// Server-composed record/deep link (from <c>HandoffUrlBuilder</c> or the entityrecord deep-link
    /// composer). Optional: a pure/notification outcome may carry no navigable link.
    /// </summary>
    [JsonPropertyName("link")]
    public OutcomeCardLink? Link { get; init; }

    /// <summary>
    /// Declared next-step chips (transition placeholders) the surface may render as clickable
    /// affordances. Empty when the outcome offers no follow-on step.
    /// </summary>
    [JsonPropertyName("nextSteps")]
    public IReadOnlyList<NextStepChip> NextSteps { get; init; } = Array.Empty<NextStepChip>();

    /// <summary>
    /// Completion state the card hosts: single-shot (immediate) OR job-aware (ordered async steps).
    /// Null is equivalent to single-shot-succeeded. When job-aware, <see cref="OutcomeCompletion.Steps"/>
    /// align additively to <c>JobAwareCompletionState v1</c> (task 014).
    /// </summary>
    [JsonPropertyName("completion")]
    public OutcomeCompletion? Completion { get; init; }

    /// <summary>
    /// Reference to the trace record for this outcome (<c>TraceEvent v1</c>, task 013) — a
    /// correlation/trace id a Context-pane trace view can resolve. Identifier only (NFR-07).
    /// </summary>
    [JsonPropertyName("traceRef")]
    public string? TraceRef { get; init; }

    /// <summary>
    /// Guard factory — the ONLY way to produce a valid OutcomeCard. Enforces the two v1 invariants:
    /// store-before-render (<paramref name="ledgerOutputKey"/> non-empty) and server-composed link
    /// (<paramref name="link"/> not model-claimed). A reference producer supplies
    /// <paramref name="ledgerOutputKey"/> from a stored <c>SessionOutput.Key</c> and
    /// <paramref name="link"/> from a <c>HandoffUrlBuilder</c> output.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="ledgerOutputKey"/> is null/blank — the card would reference no stored outcome.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="link"/> is a <see cref="OutcomeCardLink.ModelClaimed"/> link — the link must
    /// be server-composed, never model-invented.
    /// </exception>
    public static OutcomeCard ForStoredOutcome(
        string ledgerOutputKey,
        OutcomeStatus status,
        OutcomeSummary summary,
        OutcomeCardLink? link = null,
        IReadOnlyList<NextStepChip>? nextSteps = null,
        OutcomeCompletion? completion = null,
        string? traceRef = null)
    {
        if (string.IsNullOrWhiteSpace(ledgerOutputKey))
        {
            throw new ArgumentException(
                "OutcomeCard renders a STORED ledger output (ADR-040 store-before-render): " +
                "ledgerOutputKey must be the non-empty {bindingId}@t{n} key of a persisted SessionOutput.",
                nameof(ledgerOutputKey));
        }

        if (link is { IsServerComposed: false })
        {
            throw new InvalidOperationException(
                "OutcomeCard.Link must be SERVER-composed (HandoffUrlBuilder / entityrecord deep-link " +
                "composer). A model-claimed link is rejected — the model never invents a clickable URL.");
        }

        ArgumentNullException.ThrowIfNull(summary);

        return new OutcomeCard
        {
            Version = SchemaVersion,
            LedgerOutputKey = ledgerOutputKey,
            Status = status,
            Summary = summary,
            Link = link,
            NextSteps = nextSteps ?? Array.Empty<NextStepChip>(),
            Completion = completion,
            TraceRef = traceRef,
        };
    }

    /// <summary>Serialize with the shared, deterministic wire options.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Tolerant-reader deserialize — unknown fields are ignored (additive-only forward compat).</summary>
    public static OutcomeCard FromJson(string json) =>
        JsonSerializer.Deserialize<OutcomeCard>(json, JsonOptions)
        ?? throw new JsonException("OutcomeCard payload deserialized to null.");

    /// <summary>
    /// Thin reference consumer — renders the card's USER-facing projection (the round-trip a
    /// surface performs). It surfaces the user-facing summary, the link label, and chip labels; it
    /// NEVER leaks <see cref="OutcomeSummary.Internal"/> into the user projection (the audience
    /// split). Compose r2 renders through this shape (or its own render of the same fields) — never
    /// a local outcome variant.
    /// </summary>
    public OutcomeCardView Render() => new(
        UserSummary: Summary.UserFacing,
        Status: Status,
        LinkLabel: Link?.Label,
        LinkUrl: Link?.Url,
        NextStepLabels: NextSteps.Select(c => c.Label).ToArray(),
        CompletionMode: Completion?.Mode ?? OutcomeCompletionMode.SingleShot);
}

/// <summary>Terminal disposition of a side-effect outcome.</summary>
public enum OutcomeStatus
{
    /// <summary>The side effect completed fully.</summary>
    Succeeded,

    /// <summary>The side effect partially completed (e.g. record created, indexing still running).</summary>
    Partial,

    /// <summary>The side effect failed.</summary>
    Failed,
}

/// <summary>
/// Audience-split summary — formalizes the shipped <c>ToolResult</c> split
/// (<c>UserSummary</c> user-facing sentence vs. model/internal <c>Summary</c>, ToolResult.cs:326).
/// </summary>
/// <param name="UserFacing">
/// Short sentence rendered VERBATIM in the transcript to the operator (from <c>UserSummary</c>).
/// </param>
/// <param name="Internal">
/// Optional internal/model-facing or diagnostic detail — for logs, telemetry, or the model's next
/// turn. MUST NOT be shown to the user (see <see cref="OutcomeCard.Render"/>).
/// </param>
public sealed record OutcomeSummary(
    [property: JsonPropertyName("userFacing")] string UserFacing,
    [property: JsonPropertyName("internal")] string? Internal = null);

/// <summary>
/// A server-composed record/deep link. Provenance (<see cref="IsServerComposed"/>) is enforced by
/// the guard factory but not serialized — the wire shape carries only URL + label + kind.
/// </summary>
public sealed record OutcomeCardLink
{
    /// <summary>
    /// Wire constructor (used by the tolerant reader). A DESERIALIZED link is never trusted as
    /// server-composed — <see cref="IsServerComposed"/> is false — because provenance is not on the
    /// wire. Production code obtains a trusted link only through <see cref="ServerComposed"/>.
    /// </summary>
    [JsonConstructor]
    public OutcomeCardLink(string url, string label, string kind)
        : this(url, label, kind, isServerComposed: false)
    {
    }

    private OutcomeCardLink(string url, string label, string kind, bool isServerComposed)
    {
        Url = url;
        Label = label;
        Kind = kind;
        IsServerComposed = isServerComposed;
    }

    /// <summary>The navigable URL (server-composed).</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; }

    /// <summary>Human-readable link label (e.g. "Open the matter").</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; }

    /// <summary>Link kind vocabulary: <c>record | wizard | workspace | document</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; }

    /// <summary>
    /// True when the URL was composed by trusted server code (HandoffUrlBuilder / deep-link
    /// composer). NOT serialized — this is a construction-time guard, not part of the wire contract.
    /// </summary>
    [JsonIgnore]
    public bool IsServerComposed { get; init; }

    /// <summary>
    /// Create a link from a SERVER-composed URL (the only kind an OutcomeCard accepts). The
    /// reference producer passes a <c>HandoffUrlBuilder</c> output or an entityrecord deep-link here.
    /// </summary>
    public static OutcomeCardLink ServerComposed(string url, string label, string kind) =>
        new(url, label, kind, isServerComposed: true);

    /// <summary>
    /// Create a link marked as MODEL-claimed (invented by the model). Rejected by
    /// <see cref="OutcomeCard.ForStoredOutcome"/> — exists to make the negative invariant testable
    /// and to give callers an explicit, refused shape rather than a silently-trusted string.
    /// </summary>
    public static OutcomeCardLink ModelClaimed(string url, string label, string kind) =>
        new(url, label, kind, isServerComposed: false);
}

/// <summary>A declared next-step chip — a transition placeholder the surface may render.</summary>
/// <param name="Label">Chip caption (e.g. "Analyze the document").</param>
/// <param name="ActionKind">
/// Vocabulary describing what the chip triggers (e.g. <c>navigate | invoke_capability | dismiss</c>).
/// </param>
/// <param name="TargetUrl">
/// Optional server-composed navigation target for a <c>navigate</c> chip. Identifier/URL only.
/// </param>
public sealed record NextStepChip(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("actionKind")] string ActionKind,
    [property: JsonPropertyName("targetUrl")] string? TargetUrl = null);

/// <summary>How the outcome completes: immediately (single-shot) or over ordered async steps (job-aware).</summary>
public enum OutcomeCompletionMode
{
    /// <summary>Immediate, single-shot outcome — no async progression.</summary>
    SingleShot,

    /// <summary>Job-aware outcome — an ordered set of consumer-declared steps progresses asynchronously.</summary>
    JobAware,
}

/// <summary>
/// Completion state the OutcomeCard hosts. For <see cref="OutcomeCompletionMode.SingleShot"/>,
/// <see cref="Steps"/> is empty. For <see cref="OutcomeCompletionMode.JobAware"/>, <see cref="Steps"/>
/// carries the consumer-declared ordered steps — additively compatible with <c>JobAwareCompletionState
/// v1</c> (task 014; Compose declares e.g. <c>container → record → profile-analysis → indexing</c>).
/// </summary>
public sealed record OutcomeCompletion
{
    /// <summary>Single-shot vs. job-aware.</summary>
    [JsonPropertyName("mode")]
    public required OutcomeCompletionMode Mode { get; init; }

    /// <summary>Ordered steps for a job-aware outcome; empty for single-shot.</summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<OutcomeStep> Steps { get; init; } = Array.Empty<OutcomeStep>();

    /// <summary>A single-shot completion (no async steps).</summary>
    public static OutcomeCompletion SingleShot() => new() { Mode = OutcomeCompletionMode.SingleShot };

    /// <summary>A job-aware completion over the supplied ordered steps.</summary>
    public static OutcomeCompletion JobAware(IReadOnlyList<OutcomeStep> steps) =>
        new() { Mode = OutcomeCompletionMode.JobAware, Steps = steps };
}

/// <summary>One ordered step within a job-aware completion.</summary>
/// <param name="Key">Stable, consumer-declared step key (e.g. <c>indexing</c>).</param>
/// <param name="Label">Human-readable step label.</param>
/// <param name="Status">Step state: <c>pending | running | succeeded | failed</c>.</param>
public sealed record OutcomeStep(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// The user-facing projection of an OutcomeCard produced by <see cref="OutcomeCard.Render"/>.
/// Deliberately carries NO internal-audience field — the audience split is preserved at render.
/// </summary>
/// <param name="UserSummary">The user-facing sentence (never the internal detail).</param>
/// <param name="Status">Outcome status.</param>
/// <param name="LinkLabel">Link label, when a link is present.</param>
/// <param name="LinkUrl">Link URL, when a link is present.</param>
/// <param name="NextStepLabels">Chip labels, in declared order.</param>
/// <param name="CompletionMode">Single-shot vs. job-aware.</param>
public sealed record OutcomeCardView(
    string UserSummary,
    OutcomeStatus Status,
    string? LinkLabel,
    string? LinkUrl,
    IReadOnlyList<string> NextStepLabels,
    OutcomeCompletionMode CompletionMode);
