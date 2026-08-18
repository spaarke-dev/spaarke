namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Categorises a <see cref="MemoryFact"/> stored in matter memory.
///
/// The type drives both the Cosmos DB schema discriminator and the section heading
/// emitted by <see cref="IMemoryItemStore.ToRecordPromptFragmentAsync"/>.
///
/// ADR-015 Tier 3: fact values are user-owned content; GDPR erasure via
/// <see cref="IMemoryItemStore.EraseSubjectAsync"/> removes every fact for the subject.
/// </summary>
public enum MemoryFactType
{
    /// <summary>
    /// A party to the matter — plaintiff, defendant, counsel, expert witness, etc.
    /// </summary>
    Party = 0,

    /// <summary>
    /// A significant date or deadline — filing deadline, hearing date, statute of limitations, etc.
    /// </summary>
    KeyDate = 1,

    /// <summary>
    /// A reference to a prior AI analysis session — links sessionId, date, and a short summary.
    /// </summary>
    PriorAnalysis = 2,

    /// <summary>
    /// Any other structured fact about the matter — contract value, governing law, jurisdiction, etc.
    /// </summary>
    KeyFact = 3,

    /// <summary>
    /// A standing user preference / directive (e.g. "always summarize my tasks first") — the governed
    /// preference channel the E3 feedback→memory loop writes to (FR-07). Recalled into the User-scope
    /// "About You" fragment each turn so standing directives can bias behavior. NOTE: adding this
    /// member does NOT implement ADR-042's deferred hard-governance (trustLevel enforcement,
    /// untrusted-origin ban, poisoning evals) — those remain deferred to security project #616;
    /// a preference item is a normal Tier-3 user-owned memory item with <c>trustLevel</c> carried inert.
    /// </summary>
    Preference = 4,
}
