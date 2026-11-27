using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 DI-051 (#3258; task 064 boundary 3; task 065): the field-secured SOURCE rule of field
/// mapping has ONE definition, <c>FieldSecuredSourceRule</c> (<c>CountsAsSecured</c>, the predicate; <c>Split</c>, the rule
/// split), asked by both writers that apply mapping rules: the unified create (<c>RecordCreationService.Mapping.cs</c>) and the
/// field-mapping push (<c>FieldMappingEndpoints.cs</c>, reading its catalog through <c>FieldMappingPushLookups.cs</c>). Task 065
/// (owner 2026-11-26): each lifts it only for rows filed under the source (the create's filing parent by table and id; the
/// push's rows filed under the source by a listed Filing edge); every other source keeps it. If a writer grew its own reading
/// of <c>IsSecured</c>, the two could disagree about which value may be copied.
/// </summary>
/// <remarks>
/// Enforced by: this source scan (CI and the Stop hook). Each writer must call the shared members, and no field-mapping source
/// file other than the definition may compare an <c>IsSecured</c> value with <c>JsonValueKind</c> itself. Controls:
/// <see cref="TheDetector_FiresOnAPrivateCopyAndOnAMissingSharedCall"/> (must fire) and
/// <see cref="TheDetector_StaysQuietOnTheSharedCalls"/> (must not fire) prove the rule can fail and can pass.
/// </remarks>
public class FieldSecuredSourceRuleGuardTests
{
    private const string Definition = "Services/Dataverse/FieldMapping/FieldSecuredSourceRule.cs";

    /// <summary>A private reading of a column's IsSecured answer (the predicate spelled again).</summary>
    private static readonly Regex PrivateIsSecuredReading = new(
        @"IsSecured[^;]{0,200}JsonValueKind\.(False|True)|JsonValueKind\.(False|True)[^;]{0,200}IsSecured", RegexOptions.Compiled);

    /// <summary>Each writer file and the shared members it must call. Task 067 (UAC-r2 C3): every surface that copies a filing
    /// parent's value past the caller's column view (create and send through the mask's named exception, push, Repair) consults the
    /// one access-model list.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> RequiredCalls = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Services/RecordCreation/RecordCreationService.Mapping.cs"] =
            ["FieldSecuredSourceRule.Split(", "FieldSecuredSourceRule.CountsAsSecured(",
             ".ApplyOrLiftForFilingParentAsync(", "AccessModelSourceColumns.TargetsOfRulesReadingOne("],
        ["Api/FieldMappings/FieldMappingEndpoints.cs"] =
            ["FieldSecuredSourceRule.Split(", "FieldMappingPushLookups.SecuredColumns(", "AccessModelSourceColumns.Contains(",
             "AccessModelSourceColumns.TargetsOfRulesReadingOne("],
        ["Api/FieldMappings/FieldMappingPushLookups.cs"] = ["FieldSecuredSourceRule.CountsAsSecured("],
        ["Services/Dataverse/FieldMapping/SendTimeFieldMapping.cs"] =
            [".ApplyOrLiftForFilingParentAsync(sourceIsFilingParent,", "AccessModelSourceColumns.TargetsOfRulesReadingOne("],
        ["Services/Dataverse/FieldMapping/MappingSourceCallerMask.cs"] = ["mappedColumns.Where(AccessModelSourceColumns.Contains)"],
        ["Services/RecordCreation/RecordCreationService.Repair.cs"] =
            ["answer.Secured.Where(AccessModelSourceColumns.Contains)", "AccessModelSourceColumns.NotCopiedByRepairWarning("],
    };

    /// <summary>Task 067 (C3): the access-model list's own definition; no other field-mapping source spells one of its columns.</summary>
    private const string AccessModelDefinition = "Services/Dataverse/FieldMapping/AccessModelSourceColumns.cs";

    internal static IReadOnlyList<string> Violations(string code, IEnumerable<string> requiredCalls)
    {
        var found = new List<string>();
        foreach (var call in requiredCalls)
        {
            if (!code.Contains(call, StringComparison.Ordinal))
                found.Add($"does not call {call}...)");
        }

        if (PrivateIsSecuredReading.IsMatch(code))
            found.Add("reads IsSecured itself instead of FieldSecuredSourceRule.CountsAsSecured");
        return found;
    }

    private static string BffRoot => Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    private static string CodeOf(string relativePath) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(BffRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("*", StringComparison.Ordinal)));

    [Theory(DisplayName = "DI-051 / task 065: create and push ask ONE field-secured source rule (FieldSecuredSourceRule) and keep no private copy")]
    [InlineData("Services/RecordCreation/RecordCreationService.Mapping.cs")]
    [InlineData("Api/FieldMappings/FieldMappingEndpoints.cs")]
    [InlineData("Api/FieldMappings/FieldMappingPushLookups.cs")]
    [InlineData("Services/Dataverse/FieldMapping/SendTimeFieldMapping.cs")]
    [InlineData("Services/Dataverse/FieldMapping/MappingSourceCallerMask.cs")]
    [InlineData("Services/RecordCreation/RecordCreationService.Repair.cs")]
    public void EachWriter_UsesTheSharedRule_AndKeepsNoPrivateCopy(string relativePath)
    {
        var violations = Violations(CodeOf(relativePath), RequiredCalls[relativePath]);
        Assert.True(violations.Count == 0, relativePath + ": " + string.Join("; ", violations));
    }

    /// <summary>Task 067 (C3): a column of the access-model list spelled as a string literal outside its definition (a second
    /// list).</summary>
    internal static IReadOnlyList<string> PrivateAccessModelColumns(string code) =>
        Sprk.Bff.Api.Services.Dataverse.FieldMapping.AccessModelSourceColumns.Exact
            .Append(Sprk.Bff.Api.Services.Dataverse.FieldMapping.AccessModelSourceColumns.IdentityCollisionPrefix)
            .Where(c => code.Contains($"\"{c}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 067 (C3): no field-mapping source other than AccessModelSourceColumns spells an access-model column")]
    public void NoOtherFieldMappingSource_KeepsItsOwnAccessModelList()
    {
        var folders = new[] { "Api/FieldMappings", "Services/Dataverse/FieldMapping", "Services/RecordCreation" };
        var offenders = folders
            .SelectMany(f => Directory.EnumerateFiles(Path.Combine(BffRoot, f.Replace('/', Path.DirectorySeparatorChar)), "*.cs", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(BffRoot, f).Replace('\\', '/'))
            .Where(p => p != AccessModelDefinition)
            .Select(p => (Path: p, Columns: PrivateAccessModelColumns(CodeOf(p)).Where(IsOnlyAnAccessModelName).ToList()))
            .Where(x => x.Columns.Count > 0)
            .Select(x => $"{x.Path} ({string.Join(", ", x.Columns)})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "spells an access-model column itself; consult AccessModelSourceColumns instead: " + string.Join("; ", offenders));
    }

    /// <summary>The names that are field-mapping-only access-model columns. <c>sprk_issecure</c> and
    /// <c>sprk_accessinheritance</c> are also the secure-root and copy-invariant columns other rules name in these folders, so
    /// they are not counted as a second list.</summary>
    private static bool IsOnlyAnAccessModelName(string column) =>
        column is not ("sprk_issecure" or "sprk_accessinheritance" or "sprk_accesspermissiongrant" or "sprk_standinggrant");

    [Fact(DisplayName = "Task 067 (C3) control: a second access-model list fires; the definition's own consumers do not")]
    public void TheAccessModelDetector_FiresOnASecondList_AndStaysQuietOnTheSharedCall()
    {
        Assert.Equal(new[] { "adx_identity_passwordhash" },
            PrivateAccessModelColumns("var denied = new[] { \"adx_identity_passwordhash\" };").Where(IsOnlyAnAccessModelName));
        Assert.Equal(new[] { "sprk_identitycollision" },
            PrivateAccessModelColumns("if (c.StartsWith(\"sprk_identitycollision\")) continue;").Where(IsOnlyAnAccessModelName));
        Assert.Empty(PrivateAccessModelColumns("if (AccessModelSourceColumns.Contains(column)) continue;"));
    }

    [Fact(DisplayName = "DI-051: no field-mapping source file other than FieldSecuredSourceRule reads IsSecured itself")]
    public void NoOtherFieldMappingSource_ReadsIsSecuredItself()
    {
        var folders = new[] { "Api/FieldMappings", "Services/Dataverse/FieldMapping", "Services/RecordCreation" };
        var offenders = folders
            .SelectMany(f => Directory.EnumerateFiles(Path.Combine(BffRoot, f.Replace('/', Path.DirectorySeparatorChar)), "*.cs", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(BffRoot, f).Replace('\\', '/'))
            .Where(p => p != Definition && PrivateIsSecuredReading.IsMatch(CodeOf(p)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "reads a column's IsSecured itself; ask FieldSecuredSourceRule.CountsAsSecured instead: " + string.Join(", ", offenders));
    }

    [Fact(DisplayName = "DI-051 control: the detector fires on a private IsSecured reading and on a writer that skips the shared calls")]
    public void TheDetector_FiresOnAPrivateCopyAndOnAMissingSharedCall()
    {
        var privateCopy = "var secured = row.TryGetValue(\"IsSecured\", out var v) && v.ValueKind == JsonValueKind.True;"
                          + " var (a, b) = FieldSecuredSourceRule.Split(rules, set);";
        Assert.Equal(
            new[] { "reads IsSecured itself instead of FieldSecuredSourceRule.CountsAsSecured" },
            Violations(privateCopy, ["FieldSecuredSourceRule.Split("]));

        var noSharedCall = "var applied = rules.Where(r => !secured.Contains(r.SourceField)).ToList();";
        Assert.Equal(new[] { "does not call FieldSecuredSourceRule.Split(...)" }, Violations(noSharedCall, ["FieldSecuredSourceRule.Split("]));
    }

    [Fact(DisplayName = "DI-051 control: the detector stays quiet on the shared calls")]
    public void TheDetector_StaysQuietOnTheSharedCalls() =>
        Assert.Empty(Violations(
            "if (FieldSecuredSourceRule.CountsAsSecured(row.TryGetValue(FieldSecuredSourceRule.IsSecuredProperty, out var v) ? v : null)) set.Add(n);"
            + " var (copyable, secured) = FieldSecuredSourceRule.Split(rules, set);",
            ["FieldSecuredSourceRule.Split(", "FieldSecuredSourceRule.CountsAsSecured("]));
}
