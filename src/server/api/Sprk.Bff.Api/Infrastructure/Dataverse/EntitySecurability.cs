namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The ONE answer <see cref="ISecurableEntityRegistry.ClassifyEntityAsync"/> gives about an entity name: is it
/// an entity at all, and if so can it be marked secure? (unified-access-control-r2 task 151, #1038.)
/// </summary>
/// <remarks>
/// <para><b>Why one three-way answer instead of two yes/no questions.</b> The resolver needs both answers for
/// every name it is given. Asked as two questions ("is it securable?", then "is it an entity?") each one
/// fetched the registry's catalog, so on a cache miss — Redis down, or the first call after the TTL — an
/// ordinary non-securable upload paid for TWO full-org metadata round trips. One question, one catalog
/// lookup, one round trip at most.</para>
///
/// <para><b><see cref="NotAnEntity"/> is the zero value on purpose.</b> An unconfigured test double, or any
/// code path that ends up with <c>default</c>, therefore REFUSES (the resolver throws
/// <c>container_entity_unknown</c>) rather than reading as "a real entity that cannot be secure" — which is
/// the fail-open #1038 was.</para>
/// </remarks>
public enum EntitySecurability
{
    /// <summary>
    /// Not the logical name of any entity in this org — an alias, an entity SET name, a misspelling.
    /// </summary>
    NotAnEntity = 0,

    /// <summary>A real entity that does not carry <c>sprk_issecure</c>, so none of its records can be secure.</summary>
    NotSecurable = 1,

    /// <summary>A real entity carrying <c>sprk_issecure</c>; whether a given record IS secure needs a record read.</summary>
    Securable = 2,
}
