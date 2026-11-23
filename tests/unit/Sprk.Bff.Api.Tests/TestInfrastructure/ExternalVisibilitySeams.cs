using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 191h: test doubles of <see cref="AppOnlyExternalVisibility"/> (THE external visibility
/// predicate as the SPA lists, the <c>dataverse.*</c> tools and the retrieval trim reach it).
/// </summary>
/// <remarks>
/// <para><see cref="AllVisible"/> is for tests about something else (paging, column reads, invoice hiding, ...) whose external
/// caller would otherwise reach the real predicate, whose app-only reads cannot succeed offline. The predicate itself is pinned
/// by <c>ExternalVisibilityTests</c> (191b), and its use on each surface by the 191h tests, which run the REAL predicate
/// (<see cref="Over"/>) over an in-memory Dataverse.</para>
/// </remarks>
internal static class ExternalVisibilitySeams
{
    /// <summary>Every asked row is Visible; nothing is read.</summary>
    public static AppOnlyExternalVisibility AllVisible { get; } = (rows, _) =>
        Task.FromResult(new ExternalRowVisibility(new ExternalVisibilityResult(
            rows.Where(r => r.Id != Guid.Empty && !string.IsNullOrWhiteSpace(r.Table))
                .Select(r => new ExternalVisibility.Node(r.Table, r.Id))
                .Distinct()
                .ToDictionary(
                    node => node,
                    node => new ExternalVisibilityDecision(
                        ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible, node.Table, node.Id)),
            faulted: false)));

    /// <summary>A decision that faulted: no row is visible.</summary>
    public static AppOnlyExternalVisibility Faulted { get; } = (_, _) => Task.FromResult(ExternalRowVisibility.NothingDecided);

    /// <summary>A seam that throws (a dependency outage).</summary>
    public static AppOnlyExternalVisibility Throws { get; } = (_, _) =>
        throw new InvalidOperationException("external visibility decision unavailable");

    /// <summary>THE predicate (<see cref="ExternalVisibility.DecideAsync"/>) over <paramref name="readers"/>, as production
    /// binds it over the app-only readers.</summary>
    public static AppOnlyExternalVisibility Over(Func<ExternalVisibility.Readers> readers) => async (rows, ct) =>
        new ExternalRowVisibility(await ExternalVisibility.DecideAsync(rows, readers(), ct).ConfigureAwait(false));

    /// <summary>A seam that records every batch it is asked, then answers through <paramref name="inner"/>.</summary>
    public static AppOnlyExternalVisibility Recording(
        List<IReadOnlyCollection<(string Table, Guid Id)>> asked, AppOnlyExternalVisibility inner) => (rows, ct) =>
    {
        lock (asked) asked.Add(rows.ToList());
        return inner(rows, ct);
    };
}

/// <summary>
/// uac-r2 task 191h: the #2189 world for the surfaces' tests, over the REAL predicate (an in-memory Dataverse,
/// <see cref="Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.ExternalVisibilityTests.World"/>): an open matter, a Restricted
/// matter, an internal-only email filed to the open matter, an analysis filed ONLY to that email (and the matter), a plain
/// analysis of the matter, and a to-do and an event regarding the email and regarding the matter.
/// </summary>
internal sealed class ExternalVisibilityLeakWorld
{
    public Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.ExternalVisibilityTests.World World { get; } = new();

    public ExternalVisibilityLeakWorld()
    {
        Matter = World.Root();
        RestrictedMatter = World.Root(restricted: true);
        InternalOnlyEmail = World.Communication(internalOnly: true, thread: null, matter: Matter);
        AnalysisAboutTheEmail = World.Add(
            "sprk_analysis", ("sprk_regardingcommunication", InternalOnlyEmail), ("sprk_regardingmatter", Matter));
        PlainAnalysis = World.Add("sprk_analysis", ("sprk_regardingmatter", Matter));
        AnalysisUnderTheRestrictedMatter = World.Add("sprk_analysis", ("sprk_regardingmatter", RestrictedMatter));
    }

    public Guid Matter { get; }

    public Guid RestrictedMatter { get; }

    public Guid InternalOnlyEmail { get; }

    public Guid AnalysisAboutTheEmail { get; }

    public Guid PlainAnalysis { get; }

    public Guid AnalysisUnderTheRestrictedMatter { get; }

    /// <summary>A to-do (or event) regarding the internal-only email and the open matter.</summary>
    public Guid ChildAboutTheEmail(string table, Guid? id = null) =>
        World.Add(table, id, ("sprk_regardingcommunication", InternalOnlyEmail), ("sprk_regardingmatter", Matter));

    /// <summary>A to-do (or event) regarding the open matter only.</summary>
    public Guid PlainChild(string table, Guid? id = null) => World.Add(table, id, ("sprk_regardingmatter", Matter));

    /// <summary>A to-do (or event) regarding the Restricted matter.</summary>
    public Guid ChildUnderTheRestrictedMatter(string table, Guid? id = null) =>
        World.Add(table, id, ("sprk_regardingmatter", RestrictedMatter));

    /// <summary>THE predicate over this world, as production binds it.</summary>
    public AppOnlyExternalVisibility Seam => ExternalVisibilitySeams.Over(World.Readers);
}
