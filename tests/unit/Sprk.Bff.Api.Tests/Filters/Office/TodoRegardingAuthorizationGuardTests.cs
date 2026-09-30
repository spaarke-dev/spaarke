using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters.Office;

/// <summary>
/// unified-access-control-r2 task 128 / GitHub #1022 — <c>POST /api/office/todo</c> must authorize the
/// REGARDING record it writes against.
/// </summary>
/// <remarks>
/// <para>This was the only live unauthorized WRITE on the Office surface: the route attached an
/// <c>sprk_todo</c> to a caller-named, pre-existing record with nothing checking access to it, through
/// the app identity. <c>/api/office/save</c> had carried <c>.AddEntityAccessFilter()</c> all along;
/// this route simply never got it.</para>
///
/// <para>🔴 The subtle hazard these guard, and the reason they are worth more than the route check
/// alone: <c>EntityAccessFilter.ExtractTargetEntity</c> is the filter's REACH. It returns null for a
/// request shape it does not recognise, and a null target makes the filter PASS THROUGH. So attaching
/// the filter without teaching that method the request type yields a filter that is present, reported
/// as authorized by Rule A of <c>RouteAuthorizationGuardTests</c>, and does nothing at all. The
/// attachment and the extraction have to move together, and only a test can hold them together.</para>
/// </remarks>
[Trait("Category", "Security")]
public class TodoRegardingAuthorizationGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        File.Exists(path).Should().BeTrue($"{path} must exist");
        return File.ReadAllText(path);
    }

    private static string Endpoints() => Read(
        "src", "server", "api", "Sprk.Bff.Api", "Api", "Office", "OfficeEndpoints.cs");

    private static string Filter() => Read(
        "src", "server", "api", "Sprk.Bff.Api", "Api", "Filters", "EntityAccessFilter.cs");

    private static string OfficeService() => Read(
        "src", "server", "api", "Sprk.Bff.Api", "Services", "Office", "OfficeService.cs");

    [Fact]
    public void TheTodoRoute_CarriesTheEntityAccessFilter()
    {
        var source = Endpoints();
        var start = source.IndexOf("group.MapPost(\"/todo\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the /todo route registration must still exist");

        var end = source.IndexOf(");", start, StringComparison.Ordinal);
        var registration = source[start..end];

        registration.Should().Contain(
            "AddEntityAccessFilter",
            "POST /api/office/todo writes against a pre-existing regarding record and must authorize "
            + "the caller against it (GitHub #1022)");
    }

    [Fact]
    public void TheFilterCanActuallySeeTheTodoRequest()
    {
        // Without this, the filter above is decorative: ExtractTargetEntity returns null for an
        // unrecognised request shape and the filter passes through.
        Filter().Should().Contain(
            "CreateTodoRequest",
            "EntityAccessFilter.ExtractTargetEntity must recognise CreateTodoRequest, or attaching the "
            + "filter to /todo authorizes nothing");
    }

    [Fact]
    public void EveryRegardingTypeTheTodoRouteAccepts_IsResolvableByTheFilter()
    {
        // 🔴 The compatibility invariant. EntityAccessFilter REJECTS an unknown entity type with 400,
        // so a regarding type accepted by /todo but absent from the filter's map would turn working
        // requests into bad ones — a gate that breaks the feature instead of guarding it. Checked
        // before the filter was attached; pinned here so a fourth regarding type cannot be added to
        // one map and not the other.
        var todoTypes = Regex
            .Matches(
                OfficeService()[OfficeService().IndexOf("_todoRegardingMap", StringComparison.Ordinal)..],
                @"\[""(?<t>[A-Za-z_]+)""\]\s*=\s*\(""sprk_regarding")
            .Select(m => m.Groups["t"].Value)
            .ToList();

        todoTypes.Should().NotBeEmpty("the /todo regarding map must be parseable by this guard");

        var filterSection = Filter();
        var filterTypes = Regex
            .Matches(filterSection, @"\[""(?<t>[a-z_]+)""\]\s*=\s*""[a-z_]+""")
            .Select(m => m.Groups["t"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in todoTypes)
        {
            filterTypes.Should().Contain(
                t,
                "regarding type '{0}' is accepted by POST /api/office/todo, so EntityAccessFilter must "
                + "be able to resolve it — otherwise gating the route returns 400 for a legitimate "
                + "request. (The filter's dictionary is OrdinalIgnoreCase, which is why the "
                + "capitalised form the add-in sends resolves against a lowercase key.)",
                t);
        }
    }
}
