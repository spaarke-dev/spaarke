using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2257 fix round 3 (K, rule 3): only the field-mapping push asks <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> as
/// <c>WriteIdentity.Caller</c>. That identity skips the #2251-and-later sets (<c>AccessTables</c>, <c>SecureRootColumns</c>,
/// <c>AccessColumns</c>) and leaves them to the caller's own Dataverse privileges, which is right only for the push: it runs
/// as its caller and its caller may edit those columns through the form anyway. A playbook writer, Repair or any app-only
/// writer that passed it would let a configuration or a model write an access table, un-secure a matter or re-point a
/// contact's identity binding.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped, so the
/// <c>&lt;see cref="WriteIdentity.Caller"/&gt;</c> doc comments are not seen) for <c>WriteIdentity.Caller</c>, bare or
/// qualified (<c>InheritedAccessPermission.WriteIdentity.Caller</c>), used as a VALUE. A comparison (<c>==</c>, <c>!=</c>,
/// <c>is</c>, <c>case</c>) only reads it and is not counted; the enum's declaration in <c>ParentLineage.cs</c>
/// (<c>Caller</c> inside <c>enum WriteIdentity</c>) is not qualified and is not counted. A <c>using static</c> import of the
/// bare member, a cast from an int and code outside <c>src/server</c> are not seen. Enforced by: this guard (ArchTests in CI
/// and the Stop hook) and the remarks on <c>InheritedAccessPermission.WriteIdentity</c>.</para>
/// </remarks>
public class RefuseAppOnlyWriteCallerIdentityGuardTests
{
    /// <summary>The one file that may ask as the caller, and why.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedCallerIdentityUsers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs"] =
            "the field-mapping push runs as its caller, whose own privileges and field security decide the #2251-and-later sets",
    };

    private static readonly Regex CallerIdentityUse = new(
        @"(?<!(?:==|!=|\bis|\bnot|\bcase)\s*)(?<![\w.])(?:[\w.]+\.)?WriteIdentity\s*\.\s*Caller\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The number of uses of <c>WriteIdentity.Caller</c> as a value in <paramref name="source"/>'s code.</summary>
    internal static int CallerIdentityUses(string source) =>
        CallerIdentityUse.Matches(SourceScan.CodeText(source.Split('\n'))).Count;

    [Fact(DisplayName = "#2257 fix round 3: only the field-mapping push asks RefuseAppOnlyWrite as WriteIdentity.Caller")]
    public void OnlyTheFieldMappingPush_AsksAsTheCaller()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var uses = CallerIdentityUses(File.ReadAllText(file));
            if (uses == 0)
                continue;

            seen.Add(relative);
            if (!AllowedCallerIdentityUsers.ContainsKey(relative))
                problems.Add($"{relative}: {uses} use(s) of WriteIdentity.Caller outside the field-mapping push");
        }

        foreach (var (allowed, why) in AllowedCallerIdentityUsers)
        {
            if (!seen.Contains(allowed))
                problems.Add($"{allowed}: listed ({why}) but uses WriteIdentity.Caller nowhere: the scan lost its signal or the entry is stale");
        }

        Assert.True(problems.Count == 0,
            "WriteIdentity.Caller skips the access-table, secure-root and access-column refusals, so only the field-mapping push "
            + "(which runs as its caller) may ask as it; every other generic writer asks as WriteIdentity.AppOnly (#2257 fix round 3):\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2257 fix round 3 guard control (must fire): WriteIdentity.Caller passed, assigned or defaulted is counted")]
    public void Control_AUseElsewhere_IsCounted()
    {
        Assert.Equal(1, CallerIdentityUses("InheritedAccessPermission.RefuseAppOnlyWrite(table, keys, InheritedAccessPermission.WriteIdentity.Caller) is { } r"));
        Assert.Equal(1, CallerIdentityUses("RefuseAppOnlyWrite(table, payload.Keys, Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.WriteIdentity.Caller);"));
        Assert.Equal(1, CallerIdentityUses("var identity = WriteIdentity\n    .Caller;"));
        Assert.Equal(1, CallerIdentityUses("internal static R? Refuse(string t, WriteIdentity identity = WriteIdentity.Caller)"));
    }

    [Fact(DisplayName = "#2257 fix round 3 guard control (must not fire): a comparison, the declaration, a comment or AppOnly is not a use")]
    public void Control_NonUses_AreNotCounted()
    {
        Assert.Equal(0, CallerIdentityUses("var appOnly = identity != WriteIdentity.Caller;"));
        Assert.Equal(0, CallerIdentityUses("if (identity == InheritedAccessPermission.WriteIdentity.Caller) { }"));
        Assert.Equal(0, CallerIdentityUses("if (identity is WriteIdentity.Caller) { } if (identity is not WriteIdentity.Caller) { }"));
        Assert.Equal(0, CallerIdentityUses("case WriteIdentity.Caller: break;"));
        Assert.Equal(0, CallerIdentityUses("internal enum WriteIdentity\n{\n    AppOnly,\n    Caller,\n}"));
        Assert.Equal(0, CallerIdentityUses("/// <see cref=\"WriteIdentity.Caller\"/> asks the pre-#2251 sets only"));
        Assert.Equal(0, CallerIdentityUses("RefuseAppOnlyWrite(table, keys, InheritedAccessPermission.WriteIdentity.AppOnly);"));
        Assert.Equal(0, CallerIdentityUses("var x = PlaybookRunWriteIdentity.Caller;"));
    }
}
