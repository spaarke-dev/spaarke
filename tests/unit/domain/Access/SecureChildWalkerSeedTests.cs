// unified-access-control-r2 K1 on ontology #1891: the read-time walker can be handed child rows the caller has already read
// (SecureRootsAboveEachAsync's readRows), with the column set each was read with. A row read with every column the walk reads is
// not read again; a row read with fewer is ignored and read as usual. Seeding never changes an answer.
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

public class SecureChildWalkerSeedTests
{
    private static readonly Guid Team = SecureChildShareWorld.SecureTeam;
    private const string Comm = "sprk_communication";

    private static string[] WalkColumns(string table) =>
        SecureChildLineage.Children[table].Lookups.Keys.Append("owningteam").Append("owninguser").ToArray();

    /// <summary>A Signal filed through its subject, a USER-owned communication (walked through), under a secure matter.</summary>
    private static (SecureChildShareWorld World, Guid Matter, Guid Subject, List<IReadOnlyCollection<Entity>> Rows) SignalOverUserOwnedSubject()
    {
        var world = SecureChildShareWorld.Standard();
        var (matter, subject, signal) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild(Comm, subject, ("sprk_regardingmatter", "sprk_matter", matter))
            .OrdinaryChild("sprk_signal", signal, ("sprk_regardingcommunication", Comm, subject));
        return (world, matter, subject, [new[] { world.Retrieve("sprk_signal", signal, null) }]);
    }

    [Fact]
    public async Task K1_ASeededRowReadWithTheWalksColumns_IsNotReadAgain()
    {
        var (world, matter, subject, rows) = SignalOverUserOwnedSubject();
        var columns = WalkColumns(Comm);
        var seeded = world.Retrieve(Comm, subject, columns);

        var answers = await SecureChildWalkerBatchTests.Walker(world)
            .SecureRootsAboveEachAsync(rows, Team, default, [(seeded, new ColumnSet(columns))]);

        answers[0].Roots.Should().Equal([("sprk_matter", matter)]);
        world.QueriedTables.Should().NotContain(Comm, "the subject was handed over as read");
    }

    [Fact]
    public async Task K1_ASeededRowLackingAColumnTheWalkReads_IsNotUsed()
    {
        // Read without owninguser, the subject would look owned by nobody and the walk would stop there: no root. The walker must
        // ignore that row and read the subject itself, so the matter above it is found.
        var (world, matter, subject, rows) = SignalOverUserOwnedSubject();
        var partial = WalkColumns(Comm).Where(c => c != "owninguser").ToArray();
        var seeded = world.Retrieve(Comm, subject, partial);

        var answers = await SecureChildWalkerBatchTests.Walker(world)
            .SecureRootsAboveEachAsync(rows, Team, default, [(seeded, new ColumnSet(partial))]);

        answers[0].Roots.Should().Equal([("sprk_matter", matter)]);
        world.QueriedTables.Should().Contain(Comm, "a row read with fewer columns than the walk reads is read again");
    }

    [Theory]
    [MemberData(nameof(SecureChildWalkerBatchTests.ShapeNames), MemberType = typeof(SecureChildWalkerBatchTests))]
    public async Task K1_SeedingEveryChildRow_AnswersExactlyAsTheRowByRowWalk(string shapeName)
    {
        var shape = SecureChildWalkerBatchTests.Build(shapeName);
        var rows = SecureChildWalkerBatchTests.Rows(shape);
        var expected = await SecureChildWalkerBatchTests.RowByRow(shape, rows);
        var seeds = shape.Children.Select(c => (shape.World.Retrieve(c.Table, c.Id, null), new ColumnSet(true))).ToList();
        var before = shape.World.QueriedTables.Count;

        var seeded = await SecureChildWalkerBatchTests.Walker(shape.World).SecureRootsAboveEachAsync(rows, Team, default, seeds);

        seeded.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(), shapeName);
        if (shapeName is not ("missing-parent" or "signal-and-subject")) // those name children that do not exist: read, as before
        {
            shape.World.QueriedTables.Skip(before).Should().OnlyContain(t => SecureChildLineage.IsRoot(t),
                "every child row was handed over; only the roots are left to read");
        }
    }
}
