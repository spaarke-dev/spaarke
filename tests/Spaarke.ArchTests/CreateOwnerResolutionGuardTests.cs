using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 015 (FR-09): every create resolves its owner with
/// <c>IRecordOwnershipResolver.ResolveOwnerAsync</c>, the record-first resolution that also reports refusals and the
/// person who asked. No create path may call <c>ResolveOwningTeamAsync</c>, the older answer that collapses a refusal
/// to <see langword="null"/>; the Office quick-create used it for roots, so the same Project under the same Matter got
/// a different owner on Office than on the child route.
/// </summary>
/// <remarks>
/// Enforced here rather than by deleting the interface member: test doubles and the census still name it.
/// A NON-create caller may be listed in <see cref="Allowlist"/> with a reason a reviewer can check.
/// </remarks>
public class CreateOwnerResolutionGuardTests
{
    /// <summary>Files under <c>src/server</c> allowed to call <c>ResolveOwningTeamAsync</c>, with why. Empty today.</summary>
    public static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>();

    /// <summary>A member-access CALL; the interface declaration and the implementation have no dot before the name.</summary>
    internal static readonly Regex CallPattern = new(@"\.ResolveOwningTeamAsync\s*\(", RegexOptions.Compiled);

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void NoServerCodeCallsResolveOwningTeamAsync()
    {
        var serverRoot = Path.Combine(RepoRoot, "src", "server");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Allowlist.ContainsKey(Path.GetFileName(file)))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsCall(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A create must resolve its owner with ResolveOwnerAsync (record-first; refusals typed), not "
            + "ResolveOwningTeamAsync (FR-09, task 015). Offending calls:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("        var team = await _ownership.ResolveOwningTeamAsync(Context(id), ct);")]
    [InlineData("        return await ownershipResolver.ResolveOwningTeamAsync (")]
    [InlineData("        var t = await resolver!.ResolveOwningTeamAsync(context, ct) ?? Guid.Empty;")]
    public void TheRule_FiresOnACall(string line) => Assert.True(IsCall(line), line);

    [Theory]
    [InlineData("    Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct);")]
    [InlineData("    public async Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)")]
    [InlineData("        var resolution = await _ownership.ResolveOwnerAsync(context, ct);")]
    [InlineData("    /// <see cref=\"IRecordOwnershipResolver.ResolveOwningTeamAsync\"/> is not used for creates.")]
    [InlineData("        // _ownership.ResolveOwningTeamAsync(context, ct) was the old call.")]
    public void TheRule_DoesNotFireOnADeclarationAnotherMethodOrAComment(string line) => Assert.False(IsCall(line), line);

    private static bool IsCall(string line)
    {
        var code = line.TrimStart();
        return !code.StartsWith("//", StringComparison.Ordinal)
            && !code.StartsWith('*')
            && CallPattern.IsMatch(line);
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
