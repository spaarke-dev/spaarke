using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Field-mapping-server-write-path-r1 task 037 (spec FR-36; UAC-r2 conditions 1 and 4): a work assignment's Assigned To
/// defaults to its own Assigned To Internal. The rule is DECLARED once — one <c>CopiedFrom =</c> in
/// <c>RecordTableRules.cs</c> — and APPLIED only by the two server create paths, each through
/// <c>RecordTableRules.CopiedDefaultsFor</c>: the owned create (<c>OwnedChildWrite.CreateAsync</c>, the child-records route)
/// and <c>RecordCreationService</c>'s step 3½. No update, repair pass or push may read it (create only), and no path may keep
/// a second copy of it.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped) for an
/// assignment <c>CopiedFrom =</c> and for a member access <c>.CopiedDefaultsFor</c> (a call or a method group). A copy of the
/// rule that names neither (a hand-written "if the Assigned To is empty, take the Internal" in another writer) is not seen;
/// the registry tests and the two paths' seam tests pin the behaviour. Enforced by: this guard (ArchTests in CI and the
/// Stop hook).</para>
/// </remarks>
public class RecordTableCopiedDefaultGuardTests
{
    private const string Registry = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs";

    /// <summary>The files allowed to apply a copied default, each with the create path it is.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedAppliers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs"] =
            "the owned create (POST /api/v1/child-records/{table}, and the chat create on the same core)",
        ["src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Mapping.cs"] =
            "RecordCreationService's create, step 3½ (never its Repair mode)",
    };

    private static readonly Regex Declaration = new(@"\bCopiedFrom\s*=(?!=)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Application = new(@"\.\s*CopiedDefaultsFor\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static int Declarations(string source) => Declaration.Matches(SourceScan.CodeText(source.Split('\n'))).Count;

    internal static int Applications(string source) => Application.Matches(SourceScan.CodeText(source.Split('\n'))).Count;

    [Fact(DisplayName = "Task 037: the copied default is declared once, in RecordTableRules, and applied only by the two create paths")]
    public void TheCopiedDefault_IsDeclaredOnce_AndAppliedOnlyByTheTwoCreatePaths()
    {
        var problems = new List<string>();
        var declarations = 0;
        var applied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            var declared = Declarations(text);
            if (declared > 0 && relative != Registry)
                problems.Add($"{relative}: {declared} CopiedFrom declaration(s) outside RecordTableRules — declare a copied default once, in the registry");
            if (relative == Registry)
                declarations += declared;

            var applies = Applications(text);
            if (applies == 0)
                continue;
            applied.Add(relative);
            if (!AllowedAppliers.ContainsKey(relative))
                problems.Add($"{relative}: {applies} use(s) of RecordTableRules.CopiedDefaultsFor outside the two create paths (create only)");
        }

        if (declarations != 1)
            problems.Add($"{Registry}: {declarations} CopiedFrom declaration(s); FR-36 declares exactly one (the work assignment's Assigned To)");

        foreach (var (allowed, why) in AllowedAppliers)
        {
            if (!applied.Contains(allowed))
                problems.Add($"{allowed}: listed ({why}) but applies no copied default: the scan lost its signal or the path dropped the rule");
        }

        Assert.True(problems.Count == 0,
            "A work assignment's Assigned To default is declared once and applied by the two server create paths only (task 037, "
            + "UAC-r2 conditions 1 and 4):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 037 guard control (must fire): a declaration and an application are counted")]
    public void Control_ADeclarationAndAnApplication_AreCounted()
    {
        Assert.Equal(1, Declarations("new ServerDefault(\"sprk_x\", \"p\", \"r\") { CopiedFrom = \"sprk_y\" },"));
        Assert.Equal(1, Applications("var fills = RecordTableRules.CopiedDefaultsFor(rule, item);"));
        Assert.Equal(1, Applications("var fills = Sprk.Bff.Api.Services.RecordCreation.RecordTableRules\n    .CopiedDefaultsFor(rule, item);"));
    }

    [Fact(DisplayName = "Task 037 guard control (must not fire): a comment, a comparison or the declaration of the helper is not counted")]
    public void Control_NonMatches_AreNotCounted()
    {
        Assert.Equal(0, Declarations("// { CopiedFrom = \"sprk_y\" }"));
        Assert.Equal(0, Declarations("if (d.CopiedFrom == null) return;"));
        Assert.Equal(0, Declarations("public string? CopiedFrom { get; init; }"));
        Assert.Equal(0, Applications("internal static IReadOnlyList<CopiedDefaultFill> CopiedDefaultsFor(RecordTableRule? rule, MappedItem item)"));
        Assert.Equal(0, Applications("// RecordTableRules.CopiedDefaultsFor(rule, item)"));
    }
}
