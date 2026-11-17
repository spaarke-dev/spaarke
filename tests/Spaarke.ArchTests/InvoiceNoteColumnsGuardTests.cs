using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095f-2s (EB-D-240, EB-D-242): a deploy-order guard for the two decision-note columns, and a must-not-fire guard that
/// keeps the internal one off the firm-facing planes. Copies <see cref="PolicyVersionColumnsGuardTests"/>.
/// </summary>
/// <remarks>
/// <para><b>The columns.</b> <c>sprk_invoice.sprk_decisionnote</c> (the internal decision note) and <c>sprk_invoicecorrection.sprk_note</c> (the
/// note a correction carries), both added by <c>scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1</c>. Dataverse refuses a query or write
/// that names a column the table does not have, so a BFF build that names either column before an environment has it fails the invoice
/// lifecycle at run time, with no build error.</para>
/// <para><b>Deploy order.</b> A BFF source file under <c>src/server/**</c> that names either column (a select, a write, a constant) fails
/// <see cref="EveryNamedColumn_ExistsInTheSourceOrIsPendingSchema"/> unless the column is an attribute of its table in the SpaarkeMaster source
/// (<c>Entities/sprk_Invoice</c>, <c>Entities/sprk_invoicecorrection</c>) or is listed in <see cref="PendingSchema"/> with the script that adds
/// it. A pending entry is the deliberate "this build needs the column; run the script with -Apply and -Verify first" acknowledgement.</para>
/// <para><b>Ratchet.</b> An entry the solution source already carries fails <see cref="PendingSchema_ListsOnlyColumnsTheSourceLacks"/>: the
/// deploy task that syncs SpaarkeMaster must delete it, so the list only shrinks.</para>
/// <para><b><c>sprk_note</c> is matched by name alone.</b> The scan cannot tell which table a literal belongs to, so any whole-word
/// <c>sprk_note</c> under <c>src/server/**</c> is read as the correction note. No other table has a column of that name today
/// (<c>sprk_notes</c> is a different word and does not match). If one is added, this guard will report it; give the new column another
/// name or narrow the scan here.</para>
/// <para><b>Internal means internal (EB-D-240).</b> <see cref="ExternalPlane"/> files (the external server code, the external DI module and
/// the external SPA) never name <c>sprk_decisionnote</c>: not in a read allow-list, a projection, a type or a constant.
/// <see cref="Controls"/> proves both directions on synthetic text and on the real DI module with the column planted in it.</para>
/// </remarks>
public class InvoiceNoteColumnsGuardTests
{
    private const string DecisionNote = "sprk_decisionnote";
    private const string CorrectionNote = "sprk_note";

    /// <summary>Each note column with the SpaarkeMaster entity folder that would carry it.</summary>
    internal static readonly IReadOnlyDictionary<string, string> EntityFolderOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DecisionNote] = "sprk_Invoice",
        [CorrectionNote] = "sprk_invoicecorrection",
    };

    /// <summary>Columns a deploy script adds and the solution source does not carry yet: column, then the script and the reason.</summary>
    internal static readonly IReadOnlyDictionary<string, string> PendingSchema = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DecisionNote] =
            "scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 (task 095f-2s, EB-D-240): sprk_invoice.sprk_decisionnote, the internal decision "
            + "note. NOT yet applied anywhere. Run -Apply (with the owner's go) and -Verify on each environment before a BFF build that names it is "
            + "deployed there; the SpaarkeMaster sync (a deploy task) then carries it.",
        [CorrectionNote] =
            "scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 (task 095f-2s, EB-D-242): sprk_invoicecorrection.sprk_note, the note a correction "
            + "carries. NOT yet applied anywhere. Run -Apply (with the owner's go) and -Verify on each environment before a BFF build that names it is "
            + "deployed there; the SpaarkeMaster sync (a deploy task) then carries it.",
    };

    private static readonly Regex[] ColumnPatterns = EntityFolderOf.Keys
        .Select(c => new Regex($@"(?<![A-Za-z0-9_]){c}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .ToArray();

    private static IReadOnlySet<string> SourceAttributes(string entityFolder)
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", entityFolder, "Entity.xml");
        Assert.True(File.Exists(file), $"The solution source has no {entityFolder}/Entity.xml; the scan went stale.");
        return XDocument.Load(file).Descendants("attribute")
            .Select(a => ((string?)a.Element("LogicalName") ?? (string?)a.Attribute("PhysicalName"))?.ToLowerInvariant())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> SourceAttributesByColumn() =>
        EntityFolderOf.ToDictionary(p => p.Key, p => SourceAttributes(p.Value), StringComparer.Ordinal);

    /// <summary>The note columns <paramref name="code"/> (comments already stripped) names, lower-cased (pure).</summary>
    internal static IReadOnlyList<string> NamedColumns(string code) =>
        EntityFolderOf.Keys.Where((_, i) => ColumnPatterns[i].IsMatch(code)).OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>The (file, column) pairs where a source names a note column that is neither in <paramref name="attributes"/> nor pending (pure).</summary>
    internal static IReadOnlyList<string> Violations(
        IEnumerable<(string File, string Code)> sources,
        IReadOnlyDictionary<string, IReadOnlySet<string>> attributes,
        IReadOnlyDictionary<string, string> pending) =>
        sources
            .SelectMany(s => NamedColumns(s.Code).Select(c => (s.File, Column: c)))
            .Where(h => !attributes[h.Column].Contains(h.Column) && !pending.ContainsKey(h.Column))
            .Select(h => $"{h.File}: {h.Column}")
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>Pending entries the source already carries (pure): stale, to be removed.</summary>
    internal static IReadOnlyList<string> Stale(IReadOnlyDictionary<string, string> pending, IReadOnlyDictionary<string, IReadOnlySet<string>> attributes) =>
        pending.Keys.Where(c => attributes[c].Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>The files that name the internal decision note (pure): the firm-facing planes must have none.</summary>
    internal static IReadOnlyList<string> NamesDecisionNote(IEnumerable<(string File, string Code)> sources) =>
        sources.Where(s => ColumnPatterns[0].IsMatch(s.Code)).Select(s => s.File).OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static IEnumerable<(string File, string Code)> BffSources() =>
        SourceScan.ServerSourceFiles().Select(f => (SourceScan.Relative(f), SourceScan.CodeText(File.ReadLines(f))));

    /// <summary>Files of the firm-facing planes: the external server code, the external DI module, and the external SPA's source.</summary>
    private static IEnumerable<string> ExternalPlaneFiles()
    {
        var bff = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        foreach (var dir in new[] { Path.Combine(bff, "Infrastructure", "ExternalAccess"), Path.Combine(bff, "Api", "ExternalAccess") })
        {
            Assert.True(Directory.Exists(dir), $"{SourceScan.Relative(dir)} is gone; the external-plane scan went stale.");
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)) { yield return f; }
        }

        yield return ExternalAccessDiModule();

        var spa = Path.Combine(SourceScan.RepoRoot, "src", "client", "external-spa", "src");
        Assert.True(Directory.Exists(spa), "src/client/external-spa/src is gone; the external-plane scan went stale.");
        foreach (var f in Directory.EnumerateFiles(spa, "*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            yield return f;
        }
    }

    private static string ExternalAccessDiModule()
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Infrastructure", "DI", "ExternalAccessModule.cs");
        Assert.True(File.Exists(file), "Infrastructure/DI/ExternalAccessModule.cs is gone; the external-plane scan went stale.");
        return file;
    }

    [Fact(DisplayName = "095f-2s: every note column the BFF names exists in the solution source or is pending schema with its deploy script")]
    public void EveryNamedColumn_ExistsInTheSourceOrIsPendingSchema()
    {
        var problems = Violations(BffSources(), SourceAttributesByColumn(), PendingSchema);
        Assert.True(problems.Count == 0,
            "A BFF source names a decision-note column the solution source does not have and no pending-schema entry covers (the class remarks "
            + "say what to do: run scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Apply and -Verify first, then list the column here):\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "095f-2s ratchet: a pending-schema entry the solution source already carries is stale and must be removed")]
    public void PendingSchema_ListsOnlyColumnsTheSourceLacks()
    {
        var stale = Stale(PendingSchema, SourceAttributesByColumn());
        Assert.True(stale.Count == 0, $"Remove these PendingSchema entries; SpaarkeMaster carries them now: {string.Join(", ", stale)}.");
    }

    [Fact(DisplayName = "095f-2s: each pending entry names its deploy script, that script exists, and none claims the column is deployed")]
    public void PendingSchema_EachEntryNamesAnExistingScript_AndClaimsNoDeployment()
    {
        Assert.Equal(EntityFolderOf.Keys.OrderBy(c => c, StringComparer.Ordinal), PendingSchema.Keys.OrderBy(c => c, StringComparer.Ordinal));
        foreach (var (column, reason) in PendingSchema)
        {
            var script = reason.Split(' ')[0];
            Assert.True(script.StartsWith("scripts/", StringComparison.Ordinal) && script.EndsWith(".ps1", StringComparison.Ordinal),
                $"{column}: the entry must start with its deploy script path.");
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, script)), $"{column}: {script} does not exist.");
            Assert.False(Regex.IsMatch(reason, @"\bDeployed\b|\balready (deployed|applied)\b"), $"{column}: an entry may not claim the column is deployed.");
        }
    }

    [Fact(DisplayName = "095f-2s EB-D-240: no firm-facing file (external server code, external DI module, external SPA) names sprk_decisionnote")]
    public void ExternalPlane_NeverNamesTheInternalDecisionNote()
    {
        var files = ExternalPlaneFiles().ToList();
        Assert.NotEmpty(files);
        var hits = NamesDecisionNote(files.Select(f => (SourceScan.Relative(f), SourceScan.CodeText(File.ReadLines(f)))));
        Assert.True(hits.Count == 0,
            "sprk_decisionnote is the INTERNAL decision note (EB-D-240); no firm-facing reader, allow-list or SPA type may name it:\n" + string.Join("\n", hits));
    }

    [Fact(DisplayName = "095f-2s controls: a planted BFF select is reported (must fire); pending or carried columns and lookalike names are not (must not fire)")]
    public void Controls()
    {
        var empty = new Dictionary<string, string>();
        var carried = new Dictionary<string, IReadOnlySet<string>>
        {
            [DecisionNote] = new HashSet<string> { "sprk_correctionnote" },
            [CorrectionNote] = new HashSet<string> { "sprk_resubmissionof" },
        };
        var selectDecisionNote = ("Services/Finance/X.cs", "private static readonly string[] Cols = { \"sprk_invoicenumber\", \"sprk_DecisionNote\" };");
        var writeNote = ("Services/Finance/Y.cs", "row[\"sprk_note\"] = note;");

        // Must fire: neither in Entity.xml nor pending (case-insensitive, whichever shape names it).
        Assert.Equal(["Services/Finance/X.cs: sprk_decisionnote"], Violations([selectDecisionNote], carried, empty));
        Assert.Equal(["Services/Finance/Y.cs: sprk_note"], Violations([writeNote], carried, empty));
        Assert.Equal(["Services/Finance/X.cs: sprk_decisionnote", "Services/Finance/Y.cs: sprk_note"], Violations([selectDecisionNote, writeNote], carried, empty));

        // Must not fire: with both on the pending list, or once the entity source carries the column.
        Assert.Empty(Violations([selectDecisionNote, writeNote], carried, PendingSchema));
        var nowCarried = new Dictionary<string, IReadOnlySet<string>>
        {
            [DecisionNote] = new HashSet<string> { DecisionNote },
            [CorrectionNote] = new HashSet<string> { CorrectionNote },
        };
        Assert.Empty(Violations([selectDecisionNote, writeNote], nowCarried, empty));

        // Must not fire: lookalike names and unrelated columns.
        Assert.Empty(NamedColumns("sprk_notes, sprk_notebook, new_sprk_note, sprk_correctionnote, sprk_decisionnotes, sprk_reason"));

        // The real tree: the BFF names sprk_invoice.sprk_decisionnote (095f-2a's lifecycle write) but not yet sprk_invoicecorrection.sprk_note;
        // the real pending list covers both, so neither is a violation.
        Assert.Empty(Violations(BffSources(), SourceAttributesByColumn(), PendingSchema));

        // The ratchet: an entry the source carries is stale.
        Assert.Equal([DecisionNote], Stale(PendingSchema, new Dictionary<string, IReadOnlySet<string>>
        {
            [DecisionNote] = new HashSet<string> { DecisionNote },
            [CorrectionNote] = new HashSet<string>(),
        }));
    }

    [Fact(DisplayName = "095f-2s EB-D-240 controls: sprk_decisionnote planted in the external allow-list is reported (must fire); the real firm-facing files and sprk_note are not (must not fire)")]
    public void ExternalControls()
    {
        // Must fire, synthetic: a projection, a type and a constant that name the column.
        Assert.Equal(["a.cs"], NamesDecisionNote([("a.cs", "ReadableColumns: new[] { \"sprk_invoicenumber\", \"sprk_DecisionNote\" }"), ("b.cs", "no note here")]));
        Assert.Equal(["t.ts"], NamesDecisionNote([("t.ts", "interface Invoice { sprk_decisionnote?: string }")]));

        // Must fire, real file: the external DI module, which holds the module allow-lists, with the column planted in it.
        var module = SourceScan.CodeText(File.ReadLines(ExternalAccessDiModule()));
        var planted = module + "\nvar ReadableColumns = new[] { \"sprk_decisionnote\" };";
        Assert.Equal(["ExternalAccessModule.cs"], NamesDecisionNote([("ExternalAccessModule.cs", planted)]));

        // Must not fire: the correction note and the firm's own correction text are firm-facing by design; a comment is not code.
        Assert.Empty(NamesDecisionNote([("c.cs", "\"sprk_correctionnote\", \"sprk_note\", \"sprk_notes\"")]));
        Assert.Empty(NamesDecisionNote([("d.cs", SourceScan.CodeText(["var x = 1; // never sprk_decisionnote"]))]));
    }
}
