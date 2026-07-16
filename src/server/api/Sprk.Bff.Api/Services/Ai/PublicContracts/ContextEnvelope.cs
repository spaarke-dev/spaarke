using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <b>ContextEnvelope v1</b> — the canonical per-turn context contract for the Reasoning Runtime
/// (spaarke-ai-architecture-redesign-r2 task AIR2-015, spec FR-A0-01, design D-M2 / FR-B-04).
/// </summary>
/// <remarks>
/// <para>
/// The Context Binder (FR-B-04, task 053) assembles exactly ONE envelope per turn from the six
/// R1 context primitives it generalizes; everything downstream (the prompt assembler, the trace
/// widget's ContextEnvelope fingerprint, the eval budget check) consumes THIS shape. This v1
/// contract freezes the <b>slice set</b>, the <b>stability classes</b>, and the <b>per-slice budget
/// fields</b> so the Binder, the budgets (task 054 / FR-B-05), the MemoryItem contract
/// (task 016 / FR-A0-03) and the Business-slice determinism verdict (task 003 / FR-P0-03) all bind
/// to a stable target. It ships as a walking skeleton: this contract + a thin reference producer
/// (<see cref="ContextEnvelopeReferenceProducer"/>) + a thin reference consumer
/// (<see cref="ContextEnvelopeReferenceConsumer"/>) + a contract test — not a paper spec.
/// </para>
/// <para>
/// <b>Six canonical slices</b> {User, Workspace, Business, Memory, Organizational, Semantic}
/// (FR-A0-01, design §diagram line 160 + D-M2). Each carries a <see cref="SliceStability"/> class
/// and a placeholder token budget. The <see cref="CanonicalAssemblyOrder"/> places the stable-prefix
/// slices BEFORE the volatile ledger tail (<c>Memory.Conversation</c>) — the prompt-cache-stability
/// invariant (NFR-04).
/// </para>
/// <para>
/// <b>ADR-040 (no parallel session cache)</b>: <c>Memory.Conversation</c> IS the ledger facade. It
/// travels as <see cref="LedgerEntryReference"/>s — addressable pointers into the append-only session
/// ledger — NEVER as copied prior-output payloads. The reference type deliberately exposes NO
/// content-bearing member so "copy the ledger content into the envelope" cannot be expressed. Other
/// slices (Workspace environment facts, Business schema card) legitimately carry assembled prompt
/// text; the reference-not-copy rule is specific to the ledger tail (ADR-040 "ledger entries travel
/// as references, not copied prior output").
/// </para>
/// <para>
/// <b>NFR-07 (no-content telemetry)</b>: <see cref="SliceMeta"/> budget/count fields carry COUNTS
/// only (token estimates + reference counts). The reference consumer's presence/order summary is
/// identifiers-and-counts only; slice CONTENT is never logged.
/// </para>
/// <para>
/// <b>Budgets are BINDING as of task 054 (FR-B-05)</b>. <see cref="EnvelopeBudget"/> carries the
/// per-slice ceilings ratified against the FR-P0-02 measured baseline
/// (<c>notes/prompt-assembly-baseline.md</c>, reconciliation in <c>notes/054-budget-reconciliation.md</c>),
/// which found the a-priori D-M2 estimates understated reality (Environment measured ~111 vs ≤50;
/// Business ~1,118 vs ≤1,200; Conversation structurally unbounded to ~8,000 vs ≤2,000). The ratified
/// values keep headroom above the measured floors and every budgeted slice now flags
/// <see cref="SliceMeta.BudgetIsProvisional"/> = false. Enforcement is <see cref="EnvelopeBudget.Evaluate"/>
/// (per-slice + ceiling); a breach is a HARD eval failure wired into the golden-utterance merge gate
/// (FR-D-02) — the assembly never silently truncates to fit.
/// </para>
/// <para>
/// <b>Tolerant reader</b>: additive-only evolution. Unknown extra slices/fields are ignored
/// (System.Text.Json default). Slices are nullable — a null slice is a legitimate partial/missing
/// state (not every turn assembles every slice; Organizational/Semantic are interface-only in r2).
/// Never rename or remove a slice/field in v1; add and bump the version for anything else.
/// </para>
/// </remarks>
public sealed record ContextEnvelope
{
    /// <summary>The versioned contract identifier. Tolerant readers accept any <c>context-envelope/v*</c>.</summary>
    public const string SchemaVersionValue = "context-envelope/v1";

    /// <summary>
    /// Canonical wire (de)serialization options for the contract: camelCase, case-insensitive reads,
    /// integer-omission tolerance. Case-insensitive + additive-only is the tolerant-reader posture —
    /// unknown properties are ignored, casing variance never breaks a read.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Version stamp carried on every instance (defaults to <see cref="SchemaVersionValue"/>).</summary>
    public string Version { get; init; } = SchemaVersionValue;

    /// <summary>Current-turn user message + deterministically-resolved caller contact + preferences (FR-B-07). Stable prefix.</summary>
    public UserSlice? User { get; init; }

    /// <summary>Environment facts (clock/timezone — <c>BuildCurrentDateDirective</c>) + host workspace/record context. Stable prefix.</summary>
    public WorkspaceSlice? Workspace { get; init; }

    /// <summary>Host-record identity + Dataverse schema card + per-table write contracts. Stable prefix (determinism-verified by task 003).</summary>
    public BusinessSlice? Business { get; init; }

    /// <summary>Ledger-facade conversation tail (references, ADR-040) + Record/User memory-item references. The volatile tail.</summary>
    public MemorySlice? Memory { get; init; }

    /// <summary>Inbound organizational context (Work IQ candidate). Provider interface only in r2 — empty.</summary>
    public OrganizationalSlice? Organizational { get; init; }

    /// <summary>Semantic-retrieval context over Azure AI Search / SPE. Provider interface only in r2 — empty; retrieval carries its OWN provenance and is kept structurally separate from Memory (design D-M3).</summary>
    public SemanticSlice? Semantic { get; init; }

    /// <summary>
    /// The canonical per-turn assembly order. Stable-prefix slices first (identity, environment facts,
    /// schema cards), then the semi-stable provider slices, then the volatile ledger tail
    /// (<c>Memory.Conversation</c>) LAST — the NFR-04 prompt-cache-stability ordering.
    /// </summary>
    public static readonly IReadOnlyList<ContextSliceKind> CanonicalAssemblyOrder = new[]
    {
        ContextSliceKind.User,
        ContextSliceKind.Workspace,
        ContextSliceKind.Business,
        ContextSliceKind.Organizational,
        ContextSliceKind.Semantic,
        ContextSliceKind.Memory,
    };

    /// <summary>Returns the <see cref="SliceMeta"/> for a slice kind, or <c>null</c> when that slice is absent this turn.</summary>
    public SliceMeta? MetaFor(ContextSliceKind kind) => kind switch
    {
        ContextSliceKind.User => User?.Meta,
        ContextSliceKind.Workspace => Workspace?.Meta,
        ContextSliceKind.Business => Business?.Meta,
        ContextSliceKind.Memory => Memory?.Meta,
        ContextSliceKind.Organizational => Organizational?.Meta,
        ContextSliceKind.Semantic => Semantic?.Meta,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown context slice kind."),
    };
}

/// <summary>The six canonical context slices (FR-A0-01).</summary>
public enum ContextSliceKind
{
    User,
    Workspace,
    Business,
    Memory,
    Organizational,
    Semantic,
}

/// <summary>
/// Prompt-cache stability class of a slice (NFR-04). Assembly places <see cref="StablePrefix"/>
/// slices ahead of <see cref="VolatileTail"/> so the cacheable prefix stays byte-stable across turns.
/// </summary>
public enum SliceStability
{
    /// <summary>Byte-stable across turns for the same session (identity, environment facts, schema cards, preferences).</summary>
    StablePrefix,

    /// <summary>Changes across turns but not every turn (memory items, inbound provider context).</summary>
    SemiStable,

    /// <summary>Changes every turn — the ledger tail (<c>Memory.Conversation</c>). MUST sort last.</summary>
    VolatileTail,
}

/// <summary>
/// Per-slice metadata: stability class + placeholder token budget + counts-only telemetry fields.
/// Carries COUNTS only (NFR-07) — deliberately no string/content member.
/// </summary>
public sealed record SliceMeta
{
    /// <summary>Prompt-cache stability class (NFR-04).</summary>
    public required SliceStability Stability { get; init; }

    /// <summary>
    /// Per-slice token budget (<see cref="EnvelopeBudget"/>). <b>Binding as of task 054 (FR-B-05)</b>,
    /// ratified against the FR-P0-02 measurement. Null means "no budget declared for this slice in r2"
    /// (Organizational/Semantic interface-only).
    /// </summary>
    public int? BudgetTokens { get; init; }

    /// <summary>
    /// <c>false</c> for the budgeted slices once task 054 (FR-B-05) ratified the ceilings against the
    /// FR-P0-02 measurement. Retained on the contract so a future re-opened budget can be flagged
    /// provisional again without a schema change.
    /// </summary>
    public bool BudgetIsProvisional { get; init; }

    /// <summary>Estimated token count of the slice's assembled content (counts only, NFR-07). Zero for reference-only slices whose content lives in the ledger, not the envelope.</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>Count of references this slice carries (ledger entries / memory items / retrieval hits) — counts only (NFR-07).</summary>
    public int ReferenceCount { get; init; }

    /// <summary>Whether this slice contributed anything this turn.</summary>
    public bool Present { get; init; }
}

/// <summary>
/// A reference into the append-only session ledger (ADR-040). Carries the addressable key and
/// identifiers ONLY — <b>never</b> the entry's payload. The absence of any content/payload/text member
/// is the contract's structural guarantee that the Memory.Conversation slice references ledger entries
/// rather than copying them into the envelope (ADR-040 "no parallel session cache").
/// </summary>
public sealed record LedgerEntryReference
{
    /// <summary>Addressable ledger key, e.g. <c>{bindingId}@t{n}</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Use-case / capability id that produced the entry (identifier only).</summary>
    public string? UcId { get; init; }

    /// <summary>The entry's rendering disposition (identifier only).</summary>
    public string? Disposition { get; init; }
}

/// <summary>
/// A reference to a stored MemoryItem (FR-A0-03 / task 016). Carries the item id + scope + provenance
/// class — identifiers only, never the item body.
/// </summary>
public sealed record MemoryItemReference
{
    /// <summary>Stable id of the referenced memory item.</summary>
    public required string ItemId { get; init; }

    /// <summary>Memory scope: <c>record</c> ((entityType,entityId)) or <c>user</c> (userId).</summary>
    public string? Scope { get; init; }

    /// <summary>Provenance class of the item (e.g. <c>explicit</c>, <c>semantic_retrieval</c>) — design D-M3.</summary>
    public string? Source { get; init; }
}

/// <summary>
/// A semantic-retrieval reference (design D-M3). Kept as its OWN type, structurally separate from
/// <see cref="MemoryItemReference"/> — retrieval results are never implicitly promoted to memory and
/// carry their own provenance class + originating index/document reference.
/// </summary>
public sealed record RetrievalReference
{
    /// <summary>Originating document/chunk reference in the semantic index.</summary>
    public required string DocumentRef { get; init; }

    /// <summary>Originating index id.</summary>
    public string? IndexId { get; init; }

    /// <summary>Provenance class carried into the envelope (never promoted to memory implicitly).</summary>
    public string ProvenanceClass { get; init; } = "semantic_retrieval";
}

/// <summary>User slice — current-turn message, deterministically-resolved caller contact, preferences.</summary>
public sealed record UserSlice
{
    /// <summary>Slice metadata (stability + budget + counts).</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>Server-resolved caller contact id (claims→contact, FR-B-07) — reference, not free text.</summary>
    public string? CallerContactId { get; init; }

    /// <summary>Assembled user-facing fragment for the prompt (current-turn message + preferences).</summary>
    public string? Fragment { get; init; }
}

/// <summary>Workspace slice — environment facts (clock/timezone) + host workspace/record context. Stable prefix.</summary>
public sealed record WorkspaceSlice
{
    /// <summary>Slice metadata (stability + budget + counts).</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>Assembled environment/workspace fragment (e.g. the <c>BuildCurrentDateDirective</c> clock line).</summary>
    public string? Fragment { get; init; }
}

/// <summary>Business slice — host-record identity + Dataverse schema card + per-table write contracts. Stable prefix (determinism-verified, task 003).</summary>
public sealed record BusinessSlice
{
    /// <summary>Slice metadata (stability + budget + counts).</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>Assembled business fragment (host identity line + schema card). MUST render deterministically (NFR-04, task 003).</summary>
    public string? Fragment { get; init; }
}

/// <summary>
/// Memory slice — the volatile tail. Carries the ledger-facade <see cref="Conversation"/> (references,
/// ADR-040) plus semi-stable Record/User <see cref="Items"/> references. No content is copied here.
/// </summary>
public sealed record MemorySlice
{
    /// <summary>Slice metadata (stability + budget + counts). Stability is <see cref="SliceStability.VolatileTail"/>.</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>The ledger conversation tail as references (ADR-040 facade) — never copied payloads.</summary>
    public IReadOnlyList<LedgerEntryReference> Conversation { get; init; } = Array.Empty<LedgerEntryReference>();

    /// <summary>Record/User memory-item references (FR-A0-03 / task 016). Semi-stable.</summary>
    public IReadOnlyList<MemoryItemReference> Items { get; init; } = Array.Empty<MemoryItemReference>();
}

/// <summary>Organizational slice — inbound provider interface only in r2 (Work IQ candidate). Empty until a provider is wired.</summary>
public sealed record OrganizationalSlice
{
    /// <summary>Slice metadata (stability + budget + counts).</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>Whether a real provider is wired (false in r2 — interface only).</summary>
    public bool ProviderImplemented { get; init; }
}

/// <summary>Semantic slice — provider interface over Azure AI Search / SPE. Empty in r2; retrieval carries its own provenance (design D-M3).</summary>
public sealed record SemanticSlice
{
    /// <summary>Slice metadata (stability + budget + counts).</summary>
    public required SliceMeta Meta { get; init; }

    /// <summary>Retrieval references (own provenance class) — structurally separate from Memory. Empty in r2.</summary>
    public IReadOnlyList<RetrievalReference> Retrieval { get; init; } = Array.Empty<RetrievalReference>();

    /// <summary>Whether a real provider is wired (false in r2 — interface exists / implementation deferred).</summary>
    public bool ProviderImplemented { get; init; }
}

/// <summary>
/// <b>BINDING per-slice token budgets</b> (task AIR2-054 / FR-B-05), ratified against the FR-P0-02
/// measured baseline (<c>notes/prompt-assembly-baseline.md</c>; reconciliation table
/// <c>notes/054-budget-reconciliation.md</c>). The measurement CONFIRMED the walking-skeleton values
/// carried the right headroom above the measured floors, so the integers are unchanged from the v1
/// seed — what changes is their STATUS (provisional → binding) and that a breach is now a HARD eval
/// failure via <see cref="Evaluate"/> (wired into the golden-utterance merge gate, FR-D-02):
/// <list type="bullet">
///   <item><description><b>Environment</b> 150 — measured ~111 (baseline §4 finding 1; a-priori ≤50 predated the clock directive). Ratified with headroom over the measured every-turn floor.</description></item>
///   <item><description><b>User</b> 700 — <b>amended by spaarkeai-assistant-enhancements-r1 task 032 (NFR-01, ADR-tension #2 Path B)</b> from the r2-ratified 300. The r2 baseline measured ~40 when the User slice carried only the current message; R1's FR-E2 stated-profile producer now composes TWO producers into this one slice — the uncapped stated-profile block (realistic worst ~282 tokens: role label + N:N practice areas + free-text focus/office/preferences) AHEAD of the 250-token-capped user-memory recall (<c>MemoryItemStore.MaxUserFragmentTokens</c>) — a realistic worst of ~532 the ≤300 estimate predated. 700 = ~532 + ~32% headroom. A pathological (verbose free-text) profile still surfaces as a breach warning, never truncated (FR-B-05). See <c>projects/spaarkeai-assistant-enhancements-r1/notes/032-budget-and-caching-decision.md</c>.</description></item>
///   <item><description><b>Business</b> 1,500 — measured ~1,118 and structurally at/over ≤1,200: two unconditional directives (SideEffectHonesty 779 + compact-formatting 189 = 968) consume ~65% of 1,500 as a "protocol floor" BEFORE any playbook persona/knowledge/skills (baseline §4 finding 2). Raised from the ≤1,200 estimate so realistic playbook content fits above the floor.</description></item>
///   <item><description><b>RecordMemory</b> 600 — measured 157 this fixture; kept (interface docs target 200–500 at higher fact density).</description></item>
///   <item><description><b>Conversation</b> 2,000 — measured ~620–970 on a normal turn but structurally UNBOUNDED to ~8,000 (baseline §4 finding 3, the most consequential). The budget stays at the D-M2 estimate; the structural worst-case is now GATED by the eval (breach-fails-eval) rather than tightening the byte-verbatim R1 constants (053 froze <c>MaxContextOutputs</c>×<c>MaxContextPayloadChars</c>). See the reconciliation note for the path decision.</description></item>
///   <item><description><b>EnvelopeCeiling</b> 4,200 — measured ~2,025–2,410 normal (~50–57% of ceiling). Kept as an INDEPENDENT bound tighter than the 4,550 sum-of-maxes (slices rarely max together).</description></item>
/// </list>
/// </summary>
public static class EnvelopeBudget
{
    /// <summary>Workspace/Environment slice ceiling — headroom above measured ~111 (D-M2 estimate ≤50 understated).</summary>
    public const int Environment = 150;

    /// <summary>
    /// User slice ceiling. <b>Amended 300 → 700 by spaarkeai-assistant-enhancements-r1 task 032 (NFR-01,
    /// ADR-tension #2 Path B)</b>: R1's FR-E2 stated-profile producer composes the uncapped stated-profile
    /// block (realistic worst ~282) AHEAD of the 250-capped user-memory recall — a ~532 realistic worst the
    /// r2 ≤300 estimate (message-only slice) predated. 700 keeps ~32% headroom above ~532.
    /// </summary>
    public const int User = 700;

    /// <summary>Business slice ceiling — raised from ≤1,200 so playbook content fits above the ~968 unconditional-directive protocol floor (baseline §4 finding 2).</summary>
    public const int Business = 1500;

    /// <summary>Record/User memory-items ceiling (D-M2 estimate ≤600).</summary>
    public const int RecordMemory = 600;

    /// <summary>Memory.Conversation ledger-tail ceiling (D-M2 estimate ≤2,000; structural worst-case ~8,000 is gated by the breach-fails-eval mechanism, not by tightening R1 constants).</summary>
    public const int Conversation = 2000;

    /// <summary>Envelope ceiling (D-M2 estimate ≤4,200) — an independent bound, tighter than the sum of per-slice maxes.</summary>
    public const int EnvelopeCeiling = 4200;

    /// <summary>chars/4 token heuristic — the codebase-wide budgeting convention (delegates to the shared estimator).</summary>
    public static int EstimateTokens(string? fragment) => ContextEnvelopeReferenceProducer.EstimateTokens(fragment);

    /// <summary>
    /// Evaluates an assembled <paramref name="envelope"/> against the binding per-slice + ceiling budgets
    /// (FR-B-05). The stable-prefix fragment slices (Environment/User/Business) are read from the envelope's
    /// own <see cref="SliceMeta.EstimatedTokens"/> (chars/4 of the assembled fragment). The Conversation
    /// ledger tail and Record-memory items carry NO content in the envelope (ADR-040 references-only), so
    /// their rendered token counts are supplied by the caller that rendered them
    /// (<paramref name="conversationTokens"/> / <paramref name="recordMemoryTokens"/>); absent → 0. The
    /// returned <see cref="ContextBudgetReport"/> is counts-only (NFR-07) and NEVER truncates — a breach is
    /// FLAGGED for the caller (telemetry in production; a hard eval failure in the merge gate).
    /// </summary>
    public static ContextBudgetReport Evaluate(
        ContextEnvelope envelope,
        int conversationTokens = 0,
        int recordMemoryTokens = 0)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        int Est(ContextSliceKind kind) => envelope.MetaFor(kind)?.EstimatedTokens ?? 0;

        var envTokens = Est(ContextSliceKind.Workspace);
        var userTokens = Est(ContextSliceKind.User);
        var bizTokens = Est(ContextSliceKind.Business);

        var lines = new List<SliceBudgetLine>(6)
        {
            new() { Slice = ContextBudgetSlice.Environment, BudgetTokens = Environment, ActualTokens = envTokens },
            new() { Slice = ContextBudgetSlice.User, BudgetTokens = User, ActualTokens = userTokens },
            new() { Slice = ContextBudgetSlice.Business, BudgetTokens = Business, ActualTokens = bizTokens },
            new() { Slice = ContextBudgetSlice.RecordMemory, BudgetTokens = RecordMemory, ActualTokens = recordMemoryTokens },
            new() { Slice = ContextBudgetSlice.Conversation, BudgetTokens = Conversation, ActualTokens = conversationTokens },
        };

        var total = envTokens + userTokens + bizTokens + recordMemoryTokens + conversationTokens;
        lines.Add(new SliceBudgetLine { Slice = ContextBudgetSlice.Envelope, BudgetTokens = EnvelopeCeiling, ActualTokens = total });

        return new ContextBudgetReport
        {
            Lines = lines,
            TotalTokens = total,
            HasBreach = lines.Any(l => l.Breached),
        };
    }
}

/// <summary>
/// The FR-B-05 budget vocabulary. Maps the ContextEnvelope slices onto the named budgets: Environment is
/// the Workspace slice; the Memory slice is split into its Conversation (ledger tail) and RecordMemory
/// (items) budgets; <see cref="Envelope"/> is the whole-envelope ceiling.
/// </summary>
public enum ContextBudgetSlice
{
    Environment,
    User,
    Business,
    RecordMemory,
    Conversation,
    Envelope,
}

/// <summary>One slice's budget line: the ceiling, the measured token count, and whether it breached. Counts only (NFR-07).</summary>
public sealed record SliceBudgetLine
{
    /// <summary>Which budget this line covers.</summary>
    public required ContextBudgetSlice Slice { get; init; }

    /// <summary>The binding budget ceiling for the slice.</summary>
    public required int BudgetTokens { get; init; }

    /// <summary>The measured token count (chars/4) for the slice this turn.</summary>
    public required int ActualTokens { get; init; }

    /// <summary>True when the slice's measured tokens exceed its budget — the breach signal (never truncated to fit).</summary>
    public bool Breached => ActualTokens > BudgetTokens;
}

/// <summary>
/// The per-turn budget report (FR-B-05): one <see cref="SliceBudgetLine"/> per slice plus the envelope
/// ceiling line. Carries identifiers/counts only (NFR-07) — deliberately no fragment/content member, so it
/// is safe to log every turn. A breach is surfaced, never silently corrected: production logs it as
/// telemetry; the golden-utterance eval gate turns a breach into a HARD test failure (FR-D-02).
/// </summary>
public sealed record ContextBudgetReport
{
    /// <summary>Per-slice budget lines plus the envelope-ceiling line.</summary>
    public required IReadOnlyList<SliceBudgetLine> Lines { get; init; }

    /// <summary>Sum of the per-slice measured tokens (the ceiling line's actual).</summary>
    public required int TotalTokens { get; init; }

    /// <summary>True when ANY per-slice budget OR the envelope ceiling breached.</summary>
    public required bool HasBreach { get; init; }

    /// <summary>The slices (and/or the envelope ceiling) that breached — identifiers only (NFR-07).</summary>
    public IReadOnlyList<ContextBudgetSlice> BreachedSlices =>
        Lines.Where(l => l.Breached).Select(l => l.Slice).ToList();

    /// <summary>
    /// Counts-only, content-free summary for per-turn telemetry (NFR-07): e.g.
    /// <c>Environment=111/150 | Business=1300/1500 | Conversation=8000/2000! | Envelope=9700/4200!</c>.
    /// A trailing <c>!</c> marks a breached line. Never carries slice content.
    /// </summary>
    public string RenderSummary() =>
        string.Join(" | ", Lines.Select(l => $"{l.Slice}={l.ActualTokens}/{l.BudgetTokens}{(l.Breached ? "!" : string.Empty)}"));
}

/// <summary>
/// Thin REFERENCE PRODUCER (walking skeleton, task AIR2-015). Assembles a minimal
/// <see cref="ContextEnvelope"/> from already-rendered/counted primitives. Pure + DI-free by design —
/// the Context Binder (FR-B-04, task 053) supersedes this with the real per-turn assembly wiring.
/// Conversation arrives as ledger REFERENCES (ADR-040 facade), never copied payloads.
/// </summary>
public static class ContextEnvelopeReferenceProducer
{
    /// <summary>chars/4 token heuristic — the same convention used throughout this codebase's budgeting.</summary>
    public static int EstimateTokens(string? fragment) => string.IsNullOrEmpty(fragment) ? 0 : (fragment!.Length + 3) / 4;

    /// <summary>
    /// Assembles a minimal envelope. <paramref name="conversation"/> is the ledger facade (references
    /// only). Organizational/Semantic are emitted present-but-empty (interface-only in r2).
    /// </summary>
    public static ContextEnvelope Assemble(
        string? userFragment = null,
        string? workspaceFragment = null,
        string? businessFragment = null,
        IReadOnlyList<LedgerEntryReference>? conversation = null,
        IReadOnlyList<MemoryItemReference>? memoryItems = null,
        string? callerContactId = null)
    {
        var ledger = conversation ?? Array.Empty<LedgerEntryReference>();
        var items = memoryItems ?? Array.Empty<MemoryItemReference>();

        return new ContextEnvelope
        {
            User = new UserSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.StablePrefix,
                    BudgetTokens = EnvelopeBudget.User,
                    BudgetIsProvisional = false,
                    EstimatedTokens = EstimateTokens(userFragment),
                    ReferenceCount = callerContactId is null ? 0 : 1,
                    Present = userFragment is not null || callerContactId is not null,
                },
                CallerContactId = callerContactId,
                Fragment = userFragment,
            },
            Workspace = new WorkspaceSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.StablePrefix,
                    BudgetTokens = EnvelopeBudget.Environment,
                    BudgetIsProvisional = false,
                    EstimatedTokens = EstimateTokens(workspaceFragment),
                    Present = workspaceFragment is not null,
                },
                Fragment = workspaceFragment,
            },
            Business = new BusinessSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.StablePrefix,
                    BudgetTokens = EnvelopeBudget.Business,
                    BudgetIsProvisional = false,
                    EstimatedTokens = EstimateTokens(businessFragment),
                    Present = businessFragment is not null,
                },
                Fragment = businessFragment,
            },
            Organizational = new OrganizationalSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.SemiStable,
                    BudgetTokens = null,
                    EstimatedTokens = 0,
                    Present = false,
                },
                ProviderImplemented = false,
            },
            Semantic = new SemanticSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.SemiStable,
                    BudgetTokens = null,
                    EstimatedTokens = 0,
                    Present = false,
                },
                Retrieval = Array.Empty<RetrievalReference>(),
                ProviderImplemented = false,
            },
            // Volatile tail — the ledger facade. EstimatedTokens is 0 because no content is copied
            // into the envelope (the payloads live in the ledger); ReferenceCount carries the count.
            Memory = new MemorySlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.VolatileTail,
                    BudgetTokens = EnvelopeBudget.Conversation,
                    BudgetIsProvisional = false,
                    EstimatedTokens = 0,
                    ReferenceCount = ledger.Count + items.Count,
                    Present = ledger.Count > 0 || items.Count > 0,
                },
                Conversation = ledger,
                Items = items,
            },
        };
    }
}

/// <summary>
/// Thin REFERENCE CONSUMER (walking skeleton, task AIR2-015). Reads a <see cref="ContextEnvelope"/> and
/// renders slice presence/order — identifiers and counts ONLY (NFR-07), never slice content.
/// </summary>
public static class ContextEnvelopeReferenceConsumer
{
    /// <summary>Present slices in canonical assembly order (absent/null slices excluded).</summary>
    public static IReadOnlyList<ContextSliceKind> PresentSlicesInOrder(ContextEnvelope envelope) =>
        ContextEnvelope.CanonicalAssemblyOrder
            .Where(kind => envelope.MetaFor(kind)?.Present == true)
            .ToList();

    /// <summary>
    /// Identifiers-and-counts-only presence/order summary (NFR-07). Emits stability class, token estimate
    /// and reference count per slice — never the slice's assembled content.
    /// </summary>
    public static string RenderPresenceSummary(ContextEnvelope envelope) =>
        string.Join(
            " | ",
            ContextEnvelope.CanonicalAssemblyOrder.Select(kind =>
            {
                var meta = envelope.MetaFor(kind);
                return meta is null
                    ? $"{kind}=absent"
                    : $"{kind}={meta.Stability},est={meta.EstimatedTokens},refs={meta.ReferenceCount},budget={(meta.BudgetTokens?.ToString() ?? "n/a")}";
            }));
}
