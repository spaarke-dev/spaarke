using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 151 review — the AUTHORIZATION key and the CONTAINER key of a record-keyed
/// upload must always name the same record.
///
/// <para><b>Why this matters.</b> <c>PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}</c>
/// hands ONE route string to two tables. <see cref="EntityAccessFilter"/>'s <c>EntitySetByType</c> turns it into
/// the entity SET the caller's rights are checked against; <see cref="DocumentAssociationMap"/> turns it into the
/// LOGICAL name <c>RecordContainerResolver</c> reads (and whose container the bytes land in). If the two tables
/// ever disagreed about a spelling — "invoice" authorized against <c>sprk_invoices</c> but resolved as some other
/// entity — the caller would be authorized on one record and write into another record's container, and SPE's
/// additive-only permissions mean that cannot be undone. The tables carry a comment-only "LOCKSTEP INVARIANT";
/// this is the test that makes it binding.</para>
///
/// <para><b>Entity-by-entity, not just key-by-key.</b> Agreeing on the KEY SET is not enough: both tables could
/// accept "invoice" while pointing at different entities. So each key is resolved through BOTH tables and the
/// pair is checked against <see cref="VerifiedEntitySetByLogicalName"/> — the logical-name → entity-set pairs
/// read from live Dataverse metadata (spaarkedev1, read-only <c>EntityDefinitions(LogicalName=…)?$select=
/// LogicalName,EntitySetName</c>, 2026-09-30). That table is ground truth from Dataverse, not a copy of either
/// production table: it is what makes "sprk_matters" provably the collection of "sprk_matter".</para>
/// </summary>
public class AssociationTypeLockstepTests
{
    /// <summary>
    /// Entity SET name of each entity a document can be filed to, as live Dataverse metadata reports it
    /// (spaarkedev1, 2026-09-30). Adding an entity to both production tables without adding it here fails
    /// <see cref="EveryLogicalNameReached_HasAVerifiedEntitySet"/> — verify its EntitySetName live, then add it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> VerifiedEntitySetByLogicalName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contact"] = "contacts",
            ["sprk_matter"] = "sprk_matters",
            ["sprk_project"] = "sprk_projects",
            ["sprk_invoice"] = "sprk_invoices",
            ["sprk_workassignment"] = "sprk_workassignments",
            ["sprk_event"] = "sprk_events",
            ["sprk_todo"] = "sprk_todos",
        };

    public static TheoryData<string> AuthorizationKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in EntityAccessFilter.SupportedEntityTypes.OrderBy(k => k, StringComparer.Ordinal))
        {
            data.Add(key);
        }

        return data;
    }

    [Theory(DisplayName = "Task 151: every authorization key resolves, through the container table, to the SAME entity it is authorized against")]
    [MemberData(nameof(AuthorizationKeys))]
    public void EveryAuthorizationKey_NamesTheSameEntityInBothTables(string key)
    {
        EntityAccessFilter.TryResolveEntitySet(key, out var authorizedEntitySet).Should().BeTrue();

        var containerLogicalName = DocumentAssociationMap.ToLogicalName(key);

        containerLogicalName.Should().NotBeNull(
            $"'{key}' is authorized by EntityAccessFilter but unknown to DocumentAssociationMap — an upload "
            + "authorized on it could not be associated, and the resolver would refuse or misread it");

        VerifiedEntitySetByLogicalName.Should().ContainKey(containerLogicalName!);
        authorizedEntitySet.Should().Be(
            VerifiedEntitySetByLogicalName[containerLogicalName!],
            $"'{key}' must be AUTHORIZED against the collection of the entity the resolver READS "
            + $"('{containerLogicalName}'); anything else authorizes one record and writes into another's container");
    }

    [Fact(DisplayName = "Task 151: both tables accept exactly the same spellings (in BOTH or in NEITHER)")]
    public void BothTables_AcceptExactlyTheSameSpellings()
    {
        // The converse direction of the invariant's own wording. A spelling only DocumentAssociationMap knows is
        // one the record-keyed route denies while every other path accepts it — not a disclosure, but the drift
        // the shared map was created to end.
        var authorization = EntityAccessFilter.SupportedEntityTypes
            .Select(k => k.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var association = DocumentAssociationMap.SupportedSpellings
            .Select(k => k.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        authorization.Should().BeEquivalentTo(association);
    }

    [Fact(DisplayName = "Task 151: every entity the tables reach has a live-verified entity set, and none is unreached")]
    public void EveryLogicalNameReached_HasAVerifiedEntitySet()
    {
        // Keeps the verified table honest in both directions: a new entity cannot slip past the entity-by-entity
        // check for want of a ground-truth row, and a stale row cannot linger after an entity is removed.
        var reached = EntityAccessFilter.SupportedEntityTypes
            .Select(DocumentAssociationMap.ToLogicalName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        reached.Should().BeEquivalentTo(VerifiedEntitySetByLogicalName.Keys);
    }
}
