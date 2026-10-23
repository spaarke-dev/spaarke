using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 015 (FR-09): every create resolves its owner with
/// <c>IRecordOwnershipResolver.ResolveOwnerAsync</c>, the record-first resolution that also reports refusals and the
/// person who asked. No create path may use <c>ResolveOwningTeamAsync</c>, the older answer that collapses a refusal to
/// <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>Any mention of the name in server code counts — a call, a method group, a delegate, a call split across lines, a
/// <c>nameof</c> — except the member's own declaration in <c>RecordOwnershipResolver.cs</c> and the files in
/// <see cref="Allowlist"/>. Whole-line comments are not code and are skipped. Enforced here rather than by deleting the
/// interface member: test doubles and the census still name it.</para>
/// <para>An allow-list entry is keyed by repo-relative path, carries the number of uses it allows, the reason and when it
/// expires. The file must have exactly that many uses: a second use added to an allow-listed file fails like any other, and
/// an entry whose use is gone is removed (<see cref="EveryAllowlistEntry_StillUsesTheName"/>), so the list only shrinks.</para>
/// <para>Known limit: the rule matches the member's name, not its behaviour. A reflection lookup by a computed string is
/// not seen; both members run the same owner decision, so the owner written would be the same.</para>
/// </remarks>
public class CreateOwnerResolutionGuardTests
{
    /// <summary>Repo-relative paths allowed to use <c>ResolveOwningTeamAsync</c>: how many uses, why and until when.</summary>
    public static readonly IReadOnlyDictionary<string, (int Uses, string Reason)> Allowlist =
        new Dictionary<string, (int Uses, string Reason)>
        {
            // Empty: the last entry (the Office creation core, #1614) expired at task 018 and went with that core (018c).
        };

    /// <summary>The file that declares the member (the interface and its implementation).</summary>
    internal const string DeclaringFile = "src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs";

    internal static readonly Regex NamePattern = new(@"\bResolveOwningTeamAsync\b", RegexOptions.Compiled);

    /// <summary>The member's declaration: the interface member or the implementation's signature.</summary>
    internal static readonly Regex DeclarationPattern = new(
        @"^\s*(?:public\s+)?(?:async\s+)?Task<Guid\?>\s+ResolveOwningTeamAsync\s*\(", RegexOptions.Compiled);

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void NoServerCodeUsesResolveOwningTeamAsync()
    {
        var offenders = new List<string>();
        foreach (var (path, lines) in ServerFiles())
        {
            if (Allowlist.TryGetValue(path, out var allowed))
            {
                var uses = lines.Count(l => IsUse(l, declaringFile: false));
                if (uses > allowed.Uses)
                    offenders.Add($"{path}: {uses} uses, the allow-list permits {allowed.Uses} ({allowed.Reason})");
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (IsUse(lines[i], declaringFile: path == DeclaringFile))
                    offenders.Add($"{path}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A create must resolve its owner with ResolveOwnerAsync (record-first; refusals typed), not "
            + "ResolveOwningTeamAsync (FR-09, task 015). Offending uses:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryAllowlistEntry_StillUsesTheName()
    {
        var files = ServerFiles().ToDictionary(f => f.Path, f => f.Lines);
        var stale = Allowlist.Keys
            .Where(path => !files.TryGetValue(path, out var lines) || !lines.Any(l => IsUse(l, declaringFile: false)))
            .ToList();

        Assert.True(stale.Count == 0,
            "Remove these allow-list entries: the file no longer uses ResolveOwningTeamAsync (or is gone):\n"
            + string.Join("\n", stale));
    }

    [Theory]
    [InlineData("        var team = await _ownership.ResolveOwningTeamAsync(Context(id), ct);")]
    [InlineData("        return await ownershipResolver.ResolveOwningTeamAsync (")]
    [InlineData("        Func<RecordOwnershipContext, CancellationToken, Task<Guid?>> ask = _ownership.ResolveOwningTeamAsync;")] // method group
    [InlineData("            .ResolveOwningTeamAsync(context, ct)")] // the second line of a call split across lines
    [InlineData("        var name = nameof(IRecordOwnershipResolver.ResolveOwningTeamAsync);")]
    [InlineData("    Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct);")] // declared elsewhere
    public void TheRule_FiresOnAnyUse(string line) => Assert.True(IsUse(line, declaringFile: false), line);

    [Theory]
    [InlineData("    Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct);")]
    [InlineData("    public async Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)")]
    public void TheRule_DoesNotFireOnTheDeclaration_InTheDeclaringFile(string line) =>
        Assert.False(IsUse(line, declaringFile: true), line);

    [Fact]
    public void TheRule_FiresOnACall_EvenInTheDeclaringFile() =>
        Assert.True(IsUse("        var team = await ResolveOwningTeamAsync(context, ct);", declaringFile: true));

    [Theory]
    [InlineData("        var resolution = await _ownership.ResolveOwnerAsync(context, ct);")]
    [InlineData("    /// <see cref=\"IRecordOwnershipResolver.ResolveOwningTeamAsync\"/> is not used for creates.")]
    [InlineData("        // _ownership.ResolveOwningTeamAsync(context, ct) was the old call.")]
    [InlineData("        var t = await _ownership.ResolveOwningTeamAsyncLegacy(context, ct);")]
    public void TheRule_DoesNotFireOnAnotherMethodOrAComment(string line) => Assert.False(IsUse(line, declaringFile: false), line);

    private static bool IsUse(string line, bool declaringFile)
    {
        var code = line.TrimStart();
        if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
            return false;
        if (declaringFile && DeclarationPattern.IsMatch(line))
            return false;
        return NamePattern.IsMatch(line);
    }

    private static IEnumerable<(string Path, string[] Lines)> ServerFiles()
    {
        var serverRoot = Path.Combine(RepoRoot, "src", "server");
        foreach (var file in Directory.EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (path.Contains("/obj/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal))
                continue;
            yield return (path, File.ReadAllLines(file));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate the repository root from the test output directory.");
    }
}
