using Microsoft.Xrm.Sdk;

/// <summary>
/// Field-mapping task 022 fix round 2 (the mapping-source rule): a create reads, with the mapping source's own columns, whether
/// the source is secure — its owner and, on a root, its secure flag — and a source whose owner does not come back maps nothing
/// (fail closed). A fixture's scripted source row states both, as the world it models holds them.
/// </summary>
/// <remarks>Global namespace, as <see cref="TestSessionOwner"/>: shared by seam, contract, auth and data-mutation suites.</remarks>
internal static class MappingSourceGateRows
{
    /// <summary>The ordinary user who owns a scripted source unless the row names its own owner.</summary>
    internal static readonly Guid OrdinaryOwner = Guid.Parse("02200000-0000-4000-8000-0000000000d1");

    /// <summary>
    /// <paramref name="source"/> with the gate's columns it does not already hold: an ordinary user owner, and the secure flag
    /// <paramref name="secure"/> (written on every row; a non-root never has it read). Returns the same instance.
    /// </summary>
    internal static Entity WithGate(this Entity source, bool secure = false)
    {
        if (!source.Attributes.ContainsKey("ownerid"))
            source["ownerid"] = new EntityReference("systemuser", OrdinaryOwner);
        if (!source.Attributes.ContainsKey("sprk_issecure"))
            source["sprk_issecure"] = secure;
        return source;
    }
}
