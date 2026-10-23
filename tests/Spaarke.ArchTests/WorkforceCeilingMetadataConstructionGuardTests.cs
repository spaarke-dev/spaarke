using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Only <c>WorkforceCeilingEndpointExtensions</c> creates a <c>WorkforceCeilingMetadata</c> (unified-access-control-r2 task
/// 186, review round 3 K1 -> fix).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The four declaration extensions take a single route's <c>RouteHandlerBuilder</c>, so a group-wide
/// declaration through them does not compile. But <c>externalGroup.WithMetadata(WorkforceCeilingMetadata.Declared(…))</c>
/// compiles, and one such line satisfies the coverage guard for every route the group maps, including a later one that
/// needs a different set. The factories cannot be made private: the unit and arch tests build declarations to prove the
/// filter and the coverage guard (<c>tests/**</c> is not scanned here).</para>
/// <para><b>Rule.</b> In <c>src/server/**</c>, a call to <c>WorkforceCeilingMetadata.ForRoute / ForRequest / Declared</c>, a
/// <c>new WorkforceCeilingMetadata</c>, or a <c>using static</c> / alias of the type appears only in the file that declares it
/// (<see cref="DeclaringFile"/>). Line comments are stripped first (<see cref="SourceScan.CodeText"/>).</para>
/// <para><b>Stated limit.</b> A scan of text: a type reached by reflection or a name built at run time is not seen.
/// Enforced by: this guard (ArchTests, CI and the Stop hook), per CLAUDE.md §16.</para>
/// </remarks>
public class WorkforceCeilingMetadataConstructionGuardTests
{
    private const string DeclaringFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/WorkforceCeilingEndpointFilter.cs";

    private static readonly Regex Creation = new(
        @"\bWorkforceCeilingMetadata\s*\.\s*(ForRoute|ForRequest|Declared)\b"
        + @"|\bnew\s+(?:[\w.]+\.)?WorkforceCeilingMetadata\b"
        + @"|\busing\s+static\s+[\w.]*WorkforceCeilingMetadata\b"
        + @"|\busing\s+\w+\s*=\s*[\w.]*WorkforceCeilingMetadata\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Each creation of the metadata in <paramref name="files"/> outside <see cref="DeclaringFile"/>, as "path:line".</summary>
    private static List<string> Violations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        foreach (var (path, text) in files)
        {
            if (path.Replace('\\', '/').Equals(DeclaringFile, StringComparison.Ordinal))
                continue;

            var code = SourceScan.CodeText(text.Split('\n'));
            foreach (Match match in Creation.Matches(code))
                violations.Add($"{path}:{SourceScan.LineOf(code, match.Index)}  {match.Value}");
        }

        return violations;
    }

    private static IEnumerable<(string Path, string Text)> ServerFiles() =>
        SourceScan.ServerSourceFiles().Select(f => (Path.GetRelativePath(SourceScan.RepoRoot, f), File.ReadAllText(f)));

    [Fact(DisplayName = "Task 186 review round 3: only the declaration extensions create a WorkforceCeilingMetadata")]
    public void NoServerCodeOutsideTheDeclaringFile_CreatesAWorkforceCeilingMetadata()
    {
        var violations = Violations(ServerFiles());

        Assert.True(violations.Count == 0,
            "A WorkforceCeilingMetadata is created only by WorkforceCeilingEndpointExtensions (RequireWorkforceCeiling, "
            + "WorkforceCeilingInHandler, ExemptFromWorkforceCeiling), which take one route's builder. A group-wide "
            + ".WithMetadata(WorkforceCeilingMetadata.…) would satisfy the coverage guard for every route the group maps:\n"
            + string.Join("\n", violations));
    }

    [Fact(DisplayName = "Task 186 review round 3 control: the scan is not vacuous (the declaring file calls the factories it bans elsewhere)")]
    public void TheScanSeesTheFactoryCallsInTheDeclaringFile()
    {
        var declaring = Path.Combine(SourceScan.RepoRoot, DeclaringFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(declaring), $"{DeclaringFile} moved; update the guard.");

        var code = SourceScan.CodeText(File.ReadAllText(declaring).Split('\n'));
        Assert.True(Creation.Matches(code).Count >= 4, "the four extensions call the three factories; the pattern no longer sees them.");
    }

    [Fact(DisplayName = "Task 186 review round 3 must-fire: a group-wide WithMetadata, a new, a using static and an alias are each reported")]
    public void TheGuardFires_OnEachWayToCreateTheMetadata()
    {
        var files = new[]
        {
            ("src/server/api/Sprk.Bff.Api/Api/Other/GroupWide.cs",
                "externalGroup.WithMetadata(WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.Exempt, \"all\"));"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Split.cs",
                "var m = WorkforceCeilingMetadata\n    .ForRoute(set);"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Request.cs", "x.WithMetadata(WorkforceCeilingMetadata.ForRequest(r => null));"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/New.cs", "var m = new WorkforceCeilingMetadata();"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Static.cs", "using static Sprk.Bff.Api.Api.ExternalAccess.WorkforceCeilingMetadata;"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Alias.cs", "using M = Sprk.Bff.Api.Api.ExternalAccess.WorkforceCeilingMetadata;"),
            // Same file name in another folder is not the declaring file.
            ("src/server/api/Sprk.Bff.Api/Api/Other/WorkforceCeilingEndpointFilter.cs", "WorkforceCeilingMetadata.ForRoute(set);"),
        };

        var violations = Violations(files);

        Assert.Equal(7, violations.Count);
        Assert.Contains(violations, v => v.Contains("GroupWide.cs:1", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("Split.cs:1", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("Other/WorkforceCeilingEndpointFilter.cs", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 186 review round 3 must-not-fire: the declaring file, comments, and uses of the type that create nothing")]
    public void TheGuardDoesNotFire_OnTheDeclaringFile_Comments_OrPlainUses()
    {
        var files = new[]
        {
            (DeclaringFile, "return builder.WithMetadata(WorkforceCeilingMetadata.ForRoute(requirements));"),
            (DeclaringFile.Replace('/', '\\'), "WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.Exempt, reason);"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Reader.cs",
                "var d = endpoint.Metadata.GetMetadata<WorkforceCeilingMetadata>();\nswitch (d?.Coverage) { }"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Comment.cs",
                "/// see <see cref=\"WorkforceCeilingMetadata.ForRoute\"/>\n// group.WithMetadata(WorkforceCeilingMetadata.Declared(x, y));\nvar a = 1;"),
            ("src/server/api/Sprk.Bff.Api/Api/Other/Other.cs", "var m = new MyWorkforceCeilingMetadataLike();\nvar n = NotWorkforceCeilingMetadata.ForRoute(1);"),
        };

        Assert.Empty(Violations(files));
    }
}
