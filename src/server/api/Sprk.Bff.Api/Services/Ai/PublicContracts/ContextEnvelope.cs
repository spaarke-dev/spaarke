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
/// <b>Budgets are PLACEHOLDERS in v1</b>. Task 054 (FR-B-05) fixes the binding numbers against the
/// FR-P0-02 measured baseline (<c>notes/prompt-assembly-baseline.md</c>), which found the a-priori
/// D-M2 estimates understate reality (Environment measured ~111 vs ≤50; Business ~1,118 vs ≤1,200;
/// Conversation structurally unbounded to ~8,000 vs ≤2,000). <see cref="PlaceholderBudgets"/> seeds
/// values with headroom above those measurements and flags every slice budget
/// <see cref="SliceMeta.BudgetIsProvisional"/> = true.
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
    /// Placeholder per-slice token budget. <b>v1 placeholder</b> — task 054 (FR-B-05) sets the binding
    /// value against the FR-P0-02 measurement. Null means "no budget declared for this slice in r2"
    /// (Organizational/Semantic interface-only).
    /// </summary>
    public int? BudgetTokens { get; init; }

    /// <summary>Always <c>true</c> in v1: the budget is a placeholder pending task 054.</summary>
    public bool BudgetIsProvisional { get; init; } = true;

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
/// <b>v1 PLACEHOLDER budgets.</b> Task 054 (FR-B-05) replaces these with binding values fixed against
/// the FR-P0-02 measured baseline (<c>notes/prompt-assembly-baseline.md</c>). Seeded here with headroom
/// ABOVE the measured reality so a walking-skeleton envelope never trips a placeholder ceiling that the
/// real assembly already exceeds:
/// <list type="bullet">
///   <item><description>Workspace/Environment measured ~111 (baseline §4 finding 1; a-priori ≤50 was written before the clock directive existed) → placeholder 150.</description></item>
///   <item><description>Business measured ~1,118 and structurally at/over the ≤1,200 estimate (baseline §4 finding 2) → placeholder 1,500.</description></item>
///   <item><description>Memory.Conversation structurally UNBOUNDED to ~8,000 (baseline §4 finding 3, the most consequential) vs ≤2,000 estimate → placeholder 2,000 with a standing note that task 054 must reconcile the structural ceiling, not just adopt this number.</description></item>
/// </list>
/// </summary>
public static class PlaceholderBudgets
{
    /// <summary>User slice placeholder (D-M2 estimate ≤300; baseline measured ~40).</summary>
    public const int User = 300;

    /// <summary>Workspace/Environment placeholder — headroom above measured ~111 (D-M2 estimate ≤50 understated).</summary>
    public const int Workspace = 150;

    /// <summary>Business placeholder — headroom above measured ~1,118 (D-M2 estimate ≤1,200 at-ceiling).</summary>
    public const int Business = 1500;

    /// <summary>Record/User memory-items placeholder (D-M2 estimate ≤600).</summary>
    public const int RecordMemoryItems = 600;

    /// <summary>Memory.Conversation ledger-tail placeholder (D-M2 estimate ≤2,000; NOTE structurally unbounded to ~8,000 — task 054 must reconcile).</summary>
    public const int MemoryConversation = 2000;

    /// <summary>Envelope ceiling placeholder (D-M2 estimate ≤4,200).</summary>
    public const int EnvelopeCeiling = 4200;
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
                    BudgetTokens = PlaceholderBudgets.User,
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
                    BudgetTokens = PlaceholderBudgets.Workspace,
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
                    BudgetTokens = PlaceholderBudgets.Business,
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
                    BudgetTokens = PlaceholderBudgets.MemoryConversation,
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
