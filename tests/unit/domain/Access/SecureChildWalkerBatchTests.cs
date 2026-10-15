// unified-access-control-r2 K1 on ontology #1891: the read-time walker (SecureChildShareSynchronizer.SecureRootsAboveEachAsync)
// reads the parents and roots of a walk level by level, one query per table, instead of one query per row. The answers must be
// exactly those of the row-by-row walk (SecureRootsAboveAsync, which still walks one row at a time) for every lineage shape.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

public class SecureChildWalkerBatchTests
{
    private static readonly Guid Team = SecureChildShareWorld.SecureTeam;

    /// <summary>One lineage shape: a world, and the groups of rows (table, id) handed to the walker.</summary>
    internal sealed record Shape(SecureChildShareWorld World, (string Table, Guid Id)[][] Groups, (string Table, Guid Id)[] Children);

    public static TheoryData<string> ShapeNames() => new()
    {
        "no-root", "one-root", "several-roots", "nested-secure", "ordinary-under-secure", "user-owned-through", "missing-parent",
        "flagged-not-isolated", "deeper-than-six", "cycle", "signal-and-subject",
    };

    /// <summary>The shapes the walk distinguishes. <see cref="Shape.Children"/>: every child row of the world (for seeding).</summary>
    internal static Shape Build(string name)
    {
        var w = SecureChildShareWorld.Standard();
        var children = new List<(string, Guid)>();
        Guid Child(string table, Func<string, Guid, (string, string, Guid)[], SecureChildShareWorld> add, params (string, string, Guid)[] lookups)
        {
            var id = Guid.NewGuid();
            add(table, id, lookups);
            children.Add((table, id));
            return id;
        }

        Guid Secure(string table, params (string, string, Guid)[] l) => Child(table, w.SecureChild, l);
        Guid Ordinary(string table, params (string, string, Guid)[] l) => Child(table, w.OrdinaryChild, l);
        Guid UserOwned(string table, params (string, string, Guid)[] l) => Child(table, w.UserOwnedChild, l);
        Guid Root(Func<string, Guid, SecureChildShareWorld> add, string table)
        {
            var id = Guid.NewGuid();
            add(table, id);
            return id;
        }

        const string Comm = "sprk_communication", Todo = "sprk_todo", Evt = "sprk_event";
        (string Table, Guid Id)[] G(params (string, Guid)[] rows) => rows;
        var secureMatter = Root(w.SecureRoot, "sprk_matter");
        var secureProject = Root(w.SecureRoot, "sprk_project");
        var ordinaryMatter = Root(w.OrdinaryRoot, "sprk_matter");

        (string Table, Guid Id)[][] groups;
        switch (name)
        {
            case "no-root":
            {
                var bare = Secure(Comm);
                var underOrdinary = UserOwned(Comm, ("sprk_regardingmatter", "sprk_matter", ordinaryMatter));
                groups = [G((Comm, bare)), G((Comm, underOrdinary))];
                break;
            }
            case "one-root":
            {
                var c = Secure(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter));
                groups = [G((Comm, c))];
                break;
            }
            case "several-roots":
            {
                var c = Secure(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter), ("sprk_regardingproject", "sprk_project", secureProject));
                var t = Secure(Todo, ("sprk_regardingcommunication", Comm, c), ("sprk_regardingmatter", "sprk_matter", ordinaryMatter));
                groups = [G((Comm, c)), G((Todo, t)), G((Comm, c), (Todo, t))];
                break;
            }
            case "nested-secure":
            {
                var e = Secure(Evt, ("sprk_regardingproject", "sprk_project", secureProject));
                var c = Secure(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter), ("sprk_regardingevent", Evt, e));
                var t = Secure(Todo, ("sprk_regardingcommunication", Comm, c));
                groups = [G((Todo, t)), G((Comm, c)), G((Evt, e))];
                break;
            }
            case "ordinary-under-secure":
            {
                var c = Ordinary(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter));
                var t = Secure(Todo, ("sprk_regardingcommunication", Comm, c));
                groups = [G((Todo, t)), G((Comm, c))];
                break;
            }
            case "user-owned-through":
            {
                var c = UserOwned(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter));
                var t = Ordinary(Todo, ("sprk_regardingcommunication", Comm, c));
                groups = [G((Todo, t))];
                break;
            }
            case "missing-parent":
            {
                var c = Secure(Comm, ("sprk_regardingmatter", "sprk_matter", Guid.NewGuid()), ("sprk_regardingproject", "sprk_project", secureProject));
                var t = Secure(Todo, ("sprk_regardingcommunication", Comm, Guid.NewGuid()), ("sprk_regardingmatter", "sprk_matter", secureMatter));
                groups = [G((Comm, c)), G((Todo, t)), G((Comm, c), (Todo, t))];
                break;
            }
            case "flagged-not-isolated":
            {
                var flagged = Root(w.FlaggedNotIsolatedRoot, "sprk_matter");
                var masked = Root(w.MaskedFlagNotIsolatedRoot, "sprk_project");
                var c = Secure(Comm, ("sprk_regardingmatter", "sprk_matter", flagged), ("sprk_regardingproject", "sprk_project", secureProject));
                var d = Secure(Comm, ("sprk_regardingproject", "sprk_project", masked));
                groups = [G((Comm, c)), G((Comm, d))];
                break;
            }
            case "deeper-than-six":
            {
                // e0 -> e1 -> ... -> e7 -> secure matter: from e0 the matter is past the bound; from e3 it is within it.
                var chain = new Guid[8];
                chain[7] = Secure(Evt, ("sprk_regardingmatter", "sprk_matter", secureMatter));
                for (var i = 6; i >= 0; i--)
                    chain[i] = Secure(Evt, ("sprk_regardingevent", Evt, chain[i + 1]), ("sprk_regardingproject", "sprk_project", secureProject));
                groups = [G((Evt, chain[0])), G((Evt, chain[3])), G((Evt, chain[1]))];
                break;
            }
            case "cycle":
            {
                var e1 = Guid.NewGuid();
                var e2 = Secure(Evt, ("sprk_regardingevent", Evt, e1), ("sprk_regardingmatter", "sprk_matter", secureMatter));
                w.SecureChild(Evt, e1, ("sprk_regardingevent", Evt, e2));
                children.Add((Evt, e1));
                groups = [G((Evt, e1)), G((Evt, e2))];
                break;
            }
            case "signal-and-subject":
            {
                // As the worklist places a Signal: the Signal and its subject in one group, the subject also a parent of the Signal.
                var c1 = UserOwned(Comm, ("sprk_regardingmatter", "sprk_matter", secureMatter));
                var c2 = Secure(Comm, ("sprk_regardingproject", "sprk_project", secureProject), ("sprk_regardingmatter", "sprk_matter", ordinaryMatter));
                var s1 = Secure("sprk_signal", ("sprk_regardingcommunication", Comm, c1), ("sprk_matter", "sprk_matter", secureMatter));
                var s2 = Ordinary("sprk_signal", ("sprk_regardingcommunication", Comm, c2));
                var s3 = Ordinary("sprk_signal", ("sprk_regardingcommunication", Comm, Guid.NewGuid()));
                groups = [G(("sprk_signal", s1), (Comm, c1)), G(("sprk_signal", s2), (Comm, c2)), G(("sprk_signal", s3))];
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }

        return new Shape(w, groups, children.ToArray());
    }

    internal static SecureChildShareSynchronizer Walker(SecureChildShareWorld world) =>
        new(SecureChildShareWorld.EntitiesOver(() => world).Object, null!, null!, SecureChildShareWorld.Configuration(),
            NullLogger<SecureChildShareSynchronizer>.Instance);

    /// <summary>The rows as the caller read them (every column).</summary>
    internal static List<IReadOnlyCollection<Entity>> Rows(Shape shape) =>
        shape.Groups.Select(g => (IReadOnlyCollection<Entity>)g.Select(r => shape.World.Retrieve(r.Table, r.Id, null)).ToList()).ToList();

    /// <summary>The row-by-row answer for each group: <see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>, one run each.</summary>
    internal static async Task<List<SecureRootsAbove>> RowByRow(Shape shape, List<IReadOnlyCollection<Entity>> rows)
    {
        var answers = new List<SecureRootsAbove>();
        foreach (var group in rows)
            answers.Add(await Walker(shape.World).SecureRootsAboveAsync(group, default));
        return answers;
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task K1_TheBatchedWalk_AnswersExactlyAsTheRowByRowWalk(string shapeName)
    {
        var shape = Build(shapeName);
        var rows = Rows(shape);
        var expected = await RowByRow(shape, rows);

        var batched = await Walker(shape.World).SecureRootsAboveEachAsync(rows, Team, default);

        batched.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(), shapeName);
        expected.Should().OnlyContain(a => a.Status == SecureChildShareSyncStatus.Completed);
    }

    [Fact]
    public async Task K1_TheShapesDifferAsExpected()
    {
        // Guards the equivalence theory against vacuous shapes: each one is the case its name says.
        async Task<IReadOnlyList<SecureRootsAbove>> Of(string name)
        {
            var shape = Build(name);
            return await Walker(shape.World).SecureRootsAboveEachAsync(Rows(shape), Team, default);
        }

        (await Of("no-root")).Should().OnlyContain(a => a.Roots.Count == 0 && a.Undetermined.Count == 0);
        (await Of("one-root"))[0].Roots.Should().ContainSingle();
        (await Of("several-roots"))[2].Roots.Should().HaveCount(2);
        (await Of("nested-secure"))[0].Roots.Should().HaveCount(2);
        (await Of("ordinary-under-secure"))[0].Roots.Should().BeEmpty("an ordinary team's row is not walked through");
        (await Of("user-owned-through"))[0].Roots.Should().ContainSingle("a user-owned row is walked through");
        (await Of("missing-parent")).Should().OnlyContain(a => a.Undetermined.Any(u => u.Contains("does not exist")));
        (await Of("flagged-not-isolated")).Should().OnlyContain(a => a.Undetermined.Any(u => u.Contains("not isolated")));
        var deep = await Of("deeper-than-six");
        deep[0].Undetermined.Should().ContainSingle(u => u.Contains("deeper than"));
        deep[1].Undetermined.Should().BeEmpty();
        deep[1].Roots.Should().HaveCount(2);
        (await Of("cycle")).Should().OnlyContain(a => a.Roots.Count == 1 && a.Undetermined.Count == 0);
        var signals = await Of("signal-and-subject");
        signals[0].Roots.Should().ContainSingle();
        signals[2].Undetermined.Should().ContainSingle(u => u.Contains("does not exist"));
    }

    [Fact]
    public async Task K1_TheReads_DoNotGrowWithTheNumberOfLineages()
    {
        // Each lineage: a To Do -> its own user-owned communication -> its own secure matter. Row by row that is two reads per
        // lineage (48 for 24); batched it is one query per table per level, whatever the number.
        async Task<int> QueriesFor(int lineages)
        {
            var world = SecureChildShareWorld.Standard();
            var rows = new List<IReadOnlyCollection<Entity>>();
            for (var i = 0; i < lineages; i++)
            {
                var (matter, comm, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                world.SecureRoot("sprk_matter", matter)
                    .UserOwnedChild("sprk_communication", comm, ("sprk_regardingmatter", "sprk_matter", matter))
                    .SecureChild("sprk_todo", todo, ("sprk_regardingcommunication", "sprk_communication", comm));
                rows.Add([world.Retrieve("sprk_todo", todo, null)]);
            }

            var before = world.QueriedTables.Count;
            var answers = await Walker(world).SecureRootsAboveEachAsync(rows, Team, default);
            answers.Should().OnlyContain(a => a.Roots.Count == 1 && a.Undetermined.Count == 0);
            return world.QueriedTables.Count - before;
        }

        var few = await QueriesFor(4);
        var many = await QueriesFor(24);
        many.Should().Be(few, $"the reads are per table per level, not per lineage (few={few}, many={many})");
        many.Should().BeLessThanOrEqualTo(2); // measured: 2 (one communication query, one matter query); row by row: 8 and 48
    }

    [Fact]
    public async Task K1_ABatchedRead_NamesAtMost200Ids()
    {
        var world = SecureChildShareWorld.Standard();
        var rows = new List<IReadOnlyCollection<Entity>>();
        for (var i = 0; i < 450; i++)
        {
            var (matter, comm) = (Guid.NewGuid(), Guid.NewGuid());
            world.SecureRoot("sprk_matter", matter).SecureChild("sprk_communication", comm, ("sprk_regardingmatter", "sprk_matter", matter));
            rows.Add([world.Retrieve("sprk_communication", comm, null)]);
        }

        var answers = await Walker(world).SecureRootsAboveEachAsync(rows, Team, default);

        answers.Should().OnlyContain(a => a.Roots.Count == 1);
        world.QueriedTables.Count(t => t == "sprk_matter").Should().Be(3, "450 ids in chunks of at most 200");
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_communication")]
    public async Task K1_AFaultInABatchedRead_Propagates(string failingTable)
    {
        var world = SecureChildShareWorld.Standard();
        var (matter, comm, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild("sprk_communication", comm, ("sprk_regardingmatter", "sprk_matter", matter))
            .SecureChild("sprk_todo", todo, ("sprk_regardingcommunication", "sprk_communication", comm));
        var rows = new List<IReadOnlyCollection<Entity>> { new[] { world.Retrieve("sprk_todo", todo, null) } };
        world.FailingIdListReadsOf(failingTable, failingTable == "sprk_matter" ? matter : comm);

        var act = () => Walker(world).SecureRootsAboveEachAsync(rows, Team, default);

        await act.Should().ThrowAsync<InvalidOperationException>("a read that faults is not an answer: the caller decides nothing");
    }
}
