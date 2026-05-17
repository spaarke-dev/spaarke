namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Categorises a <see cref="MemoryFact"/> stored in matter memory.
///
/// The type drives both the Cosmos DB schema discriminator and the section heading
/// emitted by <see cref="IMatterMemoryService.ToSystemPromptFragmentAsync"/>.
///
/// ADR-015 Tier 3: fact values are user-owned content; GDPR erasure via
/// <see cref="IMatterMemoryService.ClearMemoryAsync"/> removes the entire document.
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
}
