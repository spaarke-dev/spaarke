using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (design note <c>notes/task-056-private-message-design.md</c> §3.4, condition C5). A
/// private message names the core record it is about in one of three reference columns (<see cref="PrivateContextColumns"/>). The
/// reference decides where the message is SHOWN to its two participants, never who READS it, so it must never become a filing,
/// ownership, inheritance, grant or record-scoped selector.
/// </summary>
/// <remarks>
/// <para><b>Rule 1, registries.</b> The three columns are in none of the registries that turn a column into a filing edge or a
/// record scope: <c>RegardingFieldMap</c>, <c>SecureChildLineage.Children</c> (lookups and lineage columns, which also feed
/// <c>DirectMessageParticipantShares.FilingColumns</c>), <c>RecordFamilyEdges</c>, <c>InvoiceLinks</c>, the core-ancestor maps of
/// <c>CoreAncestorResolver</c> and the external root typing of <c>ExternalModuleDescriptor</c>. These are read at run time. The
/// external module registry's scope dimensions (<c>ExternalAccessModule</c>), <c>ExternalModuleRegistry</c> and the Tier-2 scope
/// injection are built from DI or are generic over the dimensions they are given, so their source is scanned instead.</para>
/// <para><b>Rule 2, the allow-list.</b> Only the files in <see cref="AllowedFiles"/> under <c>src/server</c> may name the columns:
/// the column literal (any form, <c>_{column}_value</c> included), the <c>PrivateContextColumns</c> type, or a string constant that
/// holds a column (found to a fixed point: a constant defined from a known one is one too) used through its declaring type from
/// another file. <c>Api/CommunicationEndpoints.cs</c> may use <c>PrivateContextColumns.TryParseAbout</c> and nothing else. Any file
/// may call the members that hide the column names (<see cref="EncapsulatedMembers"/>: the never-filed predicate and its read
/// helpers, UAC-r2 2026-11-22). The
/// send path's writer (<c>PrivateMessageSendGate.cs</c>) may be present or absent. Line comments are stripped; collections that
/// hold a column (for example <c>ParentLineage.BffOwnedCommunicationColumns</c>) are not aliases, so code that uses such a set
/// generically does not name the columns.</para>
/// <para><b>Rule 3, citations.</b> Each column is a citation of <c>sprk_communication</c>
/// (<c>RecordOwnershipResolver.IsCitation</c>), so every parent derivation that applies that one predicate leaves it out.</para>
/// <para><b>Limits.</b> A column name built at run time from pieces, a block comment and a <c>using static</c> import of an alias's
/// type are not seen. Files outside <c>src/server</c> (scripts, tests) are not scanned.</para>
/// <para><b>Controls.</b> <see cref="AllowList_MustFireAndMustNotFire"/> and <see cref="Registries_MustFireAndMustNotFire"/> run the
/// same rules over seeded input.</para>
/// <para><b>Maintenance.</b> A new reader or writer of the reference is a design change (the design note's single-reader rule):
/// agree it with the design note's owner first, then add the file to <see cref="AllowedFiles"/> with its reason. Never add a column
/// to a registry above; a private message that should be record-visible is filed through <c>PATCH /api/communications/{id}/filing</c>.</para>
/// </remarks>
public class PrivateContextColumnGuardTests
{
    private const string Bff = "src/server/api/Sprk.Bff.Api/";
    private const string EndpointsFile = Bff + "Api/CommunicationEndpoints.cs";

    /// <summary>The files that may name the reference columns, each with its reason (design note §3.4 rule 2).</summary>
    private static readonly IReadOnlyList<(string File, bool Required, string Reason)> AllowedFiles = new[]
    {
        (Bff + "Services/Communication/Access/PrivateContextColumns.cs", true, "the column names and the about parser"),
        (Bff + "Services/Communication/Access/PrivateMessageSendGate.cs", false, "the send path's writer: sets the reference once, at create"),
        (Bff + "Services/Communication/CommunicationThreadReadService.cs", true, "the per-viewer reader: the impersonated about reads and privateContext"),
        (Bff + "Services/Dataverse/ParentLineage.cs", true, "BffOwnedCommunicationColumns: the columns are refused to every generic writer"),
        (Bff + "Services/Ai/Membership/MembershipFieldDiscoveryService.cs", true,
            "DiscoverLookupsTargetingAsync never returns the columns on sprk_communication, so includeRelated never reads by them"),
        (Bff + "Services/Dataverse/RecordOwnershipResolver.cs", true,
            "CitationLookups: the columns are citations, so no parent derivation (ownership, secure-if-any, reparent) reads them; "
            + "ReparentAsync: a row holding one is a private message, never filed or moved to another thread (Q5)"),
        (Bff + "Services/Communication/Access/DirectMessageOwnerStep.cs", true,
            "ClearPrivateFilingAsync (owner 2026-11-22, no filing): reads the reference only to find a private message filed outside " +
            "the BFF, and clears its filing columns; it never writes the reference"),
    };

    /// <summary>The registry source files the scan checks by name (rule 1, the parts not read at run time).</summary>
    private static readonly IReadOnlyList<string> RegistrySourceFiles = new[]
    {
        Bff + "Infrastructure/DI/ExternalAccessModule.cs",
        Bff + "Infrastructure/ExternalAccess/ExternalModuleRegistry.cs",
        Bff + "Api/ExternalAccess/Tier2ScopeFilterInjector.cs",
        Bff + "Services/Dataverse/CoreAncestorResolver.cs",
        Bff + "Services/Communication/Engine/RegardingFieldMap.cs",
        Bff + "Services/Access/SecureChildLineage.cs",
        Bff + "Services/Dataverse/RecordFamilyEdges.cs",
        Bff + "Infrastructure/ExternalAccess/InvoiceLinks.cs",
    };

    // ── rule 1: registries ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056: the private-context columns are in no filing, lineage, family, invoice, ancestor or external-root registry")]
    public void TheReferenceColumnsAreInNoRegistry()
    {
        var entries = RuntimeRegistryEntries().ToList();

        // Non-vacuity: each registry was read.
        foreach (var registry in new[]
        {
            "RegardingFieldMap", "SecureChildLineage", "RecordFamilyEdges", "InvoiceLinks", "CoreAncestorResolver",
        })
        {
            Assert.Contains(entries, e => e.Registry.StartsWith(registry, StringComparison.Ordinal));
        }

        Assert.Contains(entries, e => e.Value == "sprk_regardingmatter");
        Assert.Contains(entries, e => e.Registry.StartsWith("SecureChildLineage", StringComparison.Ordinal) && e.Value == "sprk_communicationthread");

        var problems = RegistryViolations(entries);
        foreach (var column in PrivateContextColumns.All)
        {
            if (ExternalModuleDescriptor.RootTypeOfScopeAttribute(column) is { } root)
                problems.Add($"ExternalModuleDescriptor.RootTypeOfScopeAttribute types {column} as a {root} root");
        }

        Assert.True(problems.Count == 0,
            "A private message's reference decides where it is shown, never who reads it (fm task 056); it must not be a filing or " +
            "scope column:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 056: the external scope and core-ancestor sources do not name the private-context columns")]
    public void TheScopeAndAncestorSourcesDoNotNameTheReferenceColumns()
    {
        var files = ServerFiles();
        var aliases = AliasFixedPoint(files);
        var problems = new List<string>();
        foreach (var path in RegistrySourceFiles)
        {
            var file = files.SingleOrDefault(f => f.File == path);
            Assert.True(file.Lines is not null, $"{path} is missing: update RegistrySourceFiles.");
            problems.AddRange(Namings(file.File, file.Lines!, aliases).Where(n => !n.IsEncapsulatedCall).Select(n => $"{n.File}:{n.Line}: {n.Text}"));
        }

        Assert.True(problems.Count == 0, "These registries must not name the private-context columns (fm task 056):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 056 guard control: a seeded registry entry naming a reference column fires; an ordinary column does not")]
    public void Registries_MustFireAndMustNotFire()
    {
        var problems = RegistryViolations(new List<(string Registry, string Value)>
        {
            ("seed.Lookups", "sprk_privatecontextmatter"),
            ("seed.LineageColumns", "_sprk_privatecontextproject_value"),
            ("seed.Stamp", "SPRK_PRIVATECONTEXTWORKASSIGNMENT"),
            ("seed.Ok", "sprk_regardingmatter"),
            ("seed.Ok", "sprk_communicationthread"),
        });

        Assert.Equal(3, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains("seed.Ok", StringComparison.Ordinal));
    }

    // ── rule 2: the allow-list ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056: only the allow-listed files name the private-context columns; the endpoints use TryParseAbout only")]
    public void OnlyTheAllowListedFilesNameTheReferenceColumns()
    {
        var files = ServerFiles();
        foreach (var (file, required, _) in AllowedFiles.Where(a => a.Required))
            Assert.True(files.Any(f => f.File == file), $"STALE allow-list entry {file}: the file is gone.");

        var problems = Judge(files, AllowedFiles.Select(a => a.File).ToHashSet(StringComparer.Ordinal), EndpointsFile);

        // Non-vacuity: the definition and the reader are seen naming the columns.
        var aliases = AliasFixedPoint(files);
        foreach (var path in new[] { AllowedFiles[0].File, AllowedFiles[2].File })
            Assert.NotEmpty(Namings(path, files.Single(f => f.File == path).Lines, aliases));

        Assert.True(problems.Count == 0,
            "Only the send-path writer, the per-viewer reader, the column definition, the BFF-owned column list and the " +
            "membership-discovery exclusion may name the " +
            "private-context columns (fm task 056, design note §3.4); see this guard's maintenance note:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 056 guard control: a literal, the type, an alias (two hops) and a non-parser endpoint use fire; comments, the parser, the encapsulated members and a generic set do not")]
    public void AllowList_MustFireAndMustNotFire()
    {
        var files = new List<(string File, IReadOnlyList<string> Lines)>
        {
            ("allowed/Columns.cs", Lines("""
                internal static class PrivateContextColumns
                {
                    internal const string Matter = "sprk_privatecontextmatter";
                }
                """)),
            ("allowed/Gate.cs", Lines("""
                internal static class Gate
                {
                    internal const string MatterKey = PrivateContextColumns.Matter;
                    internal const string TwiceKey = MatterKey;
                    private const string NameKey = "sprk_name";
                    internal const string OtherKey = NameKey;
                    internal static readonly IReadOnlySet<string> Owned = new HashSet<string> { MatterKey };
                }
                """)),
            ("other/Literal.cs", Lines("""var select = "_sprk_privatecontextproject_value,sprk_name";""")),
            ("other/TypeUse.cs", Lines("""var all = PrivateContextColumns.All;""")),
            ("other/AliasUse.cs", Lines("""var key = Gate.MatterKey;""")),
            ("other/TwoHopAliasUse.cs", Lines("""row[Gate.TwiceKey] = id;""")),
            ("other/EncapsulatedUse.cs", Lines("""
                if (PrivateContextColumns.IsPrivate(row)) return;
                var clause = await PrivateContextColumns.NotPrivateODataFilterAsync(entities, ct);
                columns.AddRange(PrivateContextColumns.PresentColumnsOf(table, host));
                """)),
            ("other/EncapsulatedWithColumn.cs", Lines(
                """if (PrivateContextColumns.IsPrivate(row) && row.Contains(PrivateContextColumns.Matter)) return;""")),
            ("other/CommentOnly.cs", Lines("""
                // reads sprk_privatecontextmatter? No: this is a comment. PrivateContextColumns.Matter too.
                var ok = Gate.OtherKey;
                var generic = Gate.Owned.Contains(column);
                """)),
            ("endpoints/Endpoints.cs", Lines("""
                if (!PrivateContextColumns.TryParseAbout(about, out var table, out var id)) return BadRequest();
                var column = PrivateContextColumns.ColumnByTable[table];
                var literal = "sprk_privatecontextworkassignment";
                """)),
            ("endpoints/CleanEndpoints.cs", Lines("""
                if (!PrivateContextColumns.TryParseAbout(about, out var table, out var id)) return BadRequest();
                """)),
        };

        var aliases = AliasFixedPoint(files);
        Assert.Equal(
            new[] { "Gate.MatterKey", "Gate.TwiceKey", "PrivateContextColumns.Matter" },
            aliases.Select(a => a.Type + "." + a.Name).OrderBy(a => a, StringComparer.Ordinal).ToArray());

        var allowed = new HashSet<string>(StringComparer.Ordinal) { "allowed/Columns.cs", "allowed/Gate.cs" };
        var dirty = Judge(files, allowed, "endpoints/Endpoints.cs");
        var clean = Judge(files.Where(f => f.File != "endpoints/Endpoints.cs").ToList(), allowed, "endpoints/CleanEndpoints.cs");

        // Must fire.
        Assert.Contains(dirty, p => p.StartsWith("other/Literal.cs:1", StringComparison.Ordinal));
        Assert.Contains(dirty, p => p.StartsWith("other/TypeUse.cs:1", StringComparison.Ordinal));
        Assert.Contains(dirty, p => p.StartsWith("other/AliasUse.cs:1", StringComparison.Ordinal));
        Assert.Contains(dirty, p => p.StartsWith("other/TwoHopAliasUse.cs:1", StringComparison.Ordinal));
        Assert.Contains(dirty, p => p.StartsWith("endpoints/Endpoints.cs:2", StringComparison.Ordinal));
        Assert.Contains(dirty, p => p.StartsWith("endpoints/Endpoints.cs:3", StringComparison.Ordinal));
        // The parser is allowed in the endpoints file only: the same call in any other file fires.
        Assert.Contains(dirty, p => p.StartsWith("endpoints/CleanEndpoints.cs:1", StringComparison.Ordinal));
        // An encapsulated member on a line that also names a column fires.
        Assert.Contains(dirty, p => p.StartsWith("other/EncapsulatedWithColumn.cs:1", StringComparison.Ordinal));
        Assert.Equal(8, dirty.Count);

        // Must not fire: the allowed files, a comment, an unrelated derived constant, a generic set, the parser call, and the
        // members that hide the column names (EncapsulatedMembers) in any file.
        Assert.DoesNotContain(dirty, p => p.StartsWith("allowed/", StringComparison.Ordinal));
        Assert.DoesNotContain(dirty, p => p.StartsWith("other/CommentOnly.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(dirty, p => p.StartsWith("other/EncapsulatedUse.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(dirty, p => p.StartsWith("endpoints/Endpoints.cs:1", StringComparison.Ordinal));
        Assert.Equal(5, clean.Count);
        Assert.DoesNotContain(clean, p => p.StartsWith("endpoints/", StringComparison.Ordinal));
    }

    // ── rule 3: every reference column is a citation ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056: every private-context column is a citation of sprk_communication, so no parent derivation reads it as a parent")]
    public void EveryReferenceColumnIsACitation()
    {
        var notCitations = PrivateContextColumns.All
            .Where(column => !RecordOwnershipResolver.IsCitation("sprk_communication", column))
            .ToList();

        Assert.True(notCitations.Count == 0,
            "A private message's reference names a record without filing the message under it (fm task 056), so each reference " +
            "column must be in RecordOwnershipResolver.CitationLookups for sprk_communication. Missing: " + string.Join(", ", notCitations));
    }

    [Fact(DisplayName = "fm 056 guard control: a filing lookup of sprk_communication and a reference column of another table are not citations")]
    public void EveryReferenceColumnIsACitation_MustNotFireOnAFilingLookup()
    {
        Assert.False(RecordOwnershipResolver.IsCitation("sprk_communication", "sprk_regardingmatter"));
        Assert.False(RecordOwnershipResolver.IsCitation("sprk_communication", "sprk_communicationthread"));
        Assert.False(RecordOwnershipResolver.IsCitation("sprk_event", PrivateContextColumns.Matter),
            "the citation is declared for sprk_communication only");
    }

    // ── the registry view ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<(string Registry, string Value)> RuntimeRegistryEntries()
    {
        foreach (var (entity, field) in RegardingFieldMap.All)
        {
            yield return ("RegardingFieldMap.All", entity);
            yield return ("RegardingFieldMap.All", field);
        }

        foreach (var field in RegardingFieldMap.AllRegardingFields)
            yield return ("RegardingFieldMap.AllRegardingFields", field);

        foreach (var (table, lineage) in SecureChildLineage.Children)
        {
            foreach (var (column, parent) in lineage.Lookups)
            {
                yield return ($"SecureChildLineage.Children[{table}].Lookups", column);
                yield return ($"SecureChildLineage.Children[{table}].Lookups", parent);
            }

            foreach (var column in lineage.LineageColumns.Concat(lineage.LookupOrder))
                yield return ($"SecureChildLineage.Children[{table}].LineageColumns", column);
        }

        foreach (var edge in RecordFamilyEdges.All)
        {
            yield return ("RecordFamilyEdges.All", edge.LookupColumn);
            yield return ("RecordFamilyEdges.All", edge.ChildTable);
            yield return ("RecordFamilyEdges.All", edge.ParentTable);
        }

        foreach (var column in RecordFamilyEdges.StampColumns.Keys)
            yield return ("RecordFamilyEdges.StampColumns", column);
        foreach (var (table, lookups) in RecordFamilyEdges.SecureLineageView())
        {
            foreach (var column in lookups.Keys)
                yield return ($"RecordFamilyEdges.SecureLineageView[{table}]", column);
        }

        foreach (var (table, columns) in RecordFamilyEdges.CoreAncestorChildrenView())
        {
            foreach (var (column, _) in columns)
                yield return ($"RecordFamilyEdges.CoreAncestorChildrenView[{table}]", column);
        }

        foreach (var (table, linked) in InvoiceLinks.Tables)
        {
            foreach (var column in linked.LinkColumns)
                yield return ($"InvoiceLinks.Tables[{table}]", column);
        }

        foreach (var (table, descendant) in InvoiceLinks.Descendants)
        {
            foreach (var parent in descendant.Parents)
                yield return ($"InvoiceLinks.Descendants[{table}]", parent.Column);
        }

        foreach (var link in InvoiceLinks.Supplemental)
            yield return ("InvoiceLinks.Supplemental", link.Column);

        foreach (var (_, lookup) in CoreAncestorResolver.CoreAncestorLookups)
            yield return ("CoreAncestorResolver.CoreAncestorLookups", lookup);
        foreach (var (table, columns) in CoreAncestorResolver.IntermediateRootColumns)
        {
            foreach (var (column, _) in columns)
                yield return ($"CoreAncestorResolver.IntermediateRootColumns[{table}]", column);
        }

        foreach (var (table, columns) in CoreAncestorResolver.StampSourceColumns)
        {
            foreach (var (column, _) in columns)
                yield return ($"CoreAncestorResolver.StampSourceColumns[{table}]", column);
        }

        foreach (var (table, columns) in CoreAncestorResolver.PartyRegardingColumns)
        {
            foreach (var (column, _) in columns)
                yield return ($"CoreAncestorResolver.PartyRegardingColumns[{table}]", column);
        }

        foreach (var column in ExternalModuleRegistry.PointerColumns)
            yield return ("ExternalModuleRegistry.PointerColumns", column);
    }

    private static readonly Regex ColumnName = new(
        @"sprk_privatecontext(?:matter|project|workassignment)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static List<string> RegistryViolations(IEnumerable<(string Registry, string Value)> entries) =>
        entries.Where(e => ColumnName.IsMatch(e.Value)).Select(e => $"{e.Registry} holds {e.Value}").ToList();

    // ── the source scan ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Naming(string File, int Line, string Text, bool IsParserCall, bool IsEncapsulatedCall);

    private sealed record AliasRef(string File, string Type, string Name);

    private static List<(string File, IReadOnlyList<string> Lines)> ServerFiles() =>
        SourceScan.ServerSourceFiles()
            .Select(f => (File: SourceScan.Relative(f).Replace('\\', '/'), Lines: (IReadOnlyList<string>)File.ReadAllLines(f)))
            .ToList();

    private static IReadOnlyList<string> Lines(string source) => source.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    private static readonly Regex TypeUse = new(@"\bPrivateContextColumns\b(?:\s*\.\s*(\w+))?", RegexOptions.Compiled);

    private static readonly Regex TypeDeclaration = new(@"\b(?:class|record|struct)\s+(\w+)", RegexOptions.Compiled);

    /// <summary>A <c>string</c> constant and its single-line initialiser.</summary>
    private static readonly Regex StringConstant = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*([^;]*);", RegexOptions.Compiled);

    /// <summary>
    /// Every string constant that holds a reference column: one whose initialiser holds the literal or a
    /// <c>PrivateContextColumns</c> member, then, to a fixed point, one whose initialiser names a constant already found (bare or
    /// qualified). Matched by name, so a same-named unrelated source over-reports (fails closed).
    /// </summary>
    private static List<AliasRef> AliasFixedPoint(IReadOnlyList<(string File, IReadOnlyList<string> Lines)> files)
    {
        var declarations = new List<(AliasRef Alias, string Initializer)>();
        foreach (var (file, lines) in files)
        {
            var type = "(none)";
            foreach (var line in lines.Select(SourceScan.StripLineComment))
            {
                if (TypeDeclaration.Match(line) is { Success: true } declared)
                    type = declared.Groups[1].Value;
                foreach (Match m in StringConstant.Matches(line))
                    declarations.Add((new AliasRef(file, type, m.Groups[1].Value), m.Groups[2].Value));
            }
        }

        var found = declarations
            .Where(d => ColumnName.IsMatch(d.Initializer) || TypeUse.Match(d.Initializer) is { Success: true, Groups: [_, { Success: true }] })
            .Select(d => d.Alias)
            .ToList();

        bool grew;
        do
        {
            grew = false;
            foreach (var (alias, initializer) in declarations)
            {
                if (found.Contains(alias))
                    continue;
                if (found.Any(f => Regex.IsMatch(initializer, @"(?<![\w.])(?:" + Regex.Escape(f.Type) + @"\s*\.\s*)?" + Regex.Escape(f.Name) + @"\b")))
                {
                    found.Add(alias);
                    grew = true;
                }
            }
        }
        while (grew);

        return found.Distinct().ToList();
    }

    /// <summary>Every line of <paramref name="lines"/> that names a reference column (line comments stripped).</summary>
    private static IEnumerable<Naming> Namings(string file, IReadOnlyList<string> lines, IReadOnlyList<AliasRef> aliases)
    {
        var qualified = aliases
            .Where(a => a.File != file)
            .Select(a => new Regex(@"\b" + Regex.Escape(a.Type) + @"\s*\.\s*" + Regex.Escape(a.Name) + @"\b"))
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            var typeUses = TypeUse.Matches(code).ToList();
            var other = ColumnName.IsMatch(code) || qualified.Any(r => r.IsMatch(code));
            if (typeUses.Count == 0 && !other)
                continue;

            var onlyParser = !other && typeUses.All(m => m.Groups[1].Value == "TryParseAbout");
            var onlyEncapsulated = !other && typeUses.All(m => EncapsulatedMembers.Contains(m.Groups[1].Value));
            yield return new Naming(file, i + 1, code.Trim(), onlyParser, onlyEncapsulated);
        }
    }

    /// <summary>
    /// The <c>PrivateContextColumns</c> members any file may call (UAC-r2, 2026-11-22: a private message is never filed, so every
    /// reader of a filing fails closed on one). Each hides the column names from its caller: the predicate
    /// (<c>IsPrivate</c>), the columns a read adds so the predicate can answer (<c>PresentColumnsOf</c>) and the OData clause
    /// that keeps private messages out of a read (<c>NotPrivateODataFilterAsync</c>). None can turn a column into a filing,
    /// scope or ownership selector. A line that also names a column, an alias or any other member still fires.
    /// </summary>
    private static readonly IReadOnlySet<string> EncapsulatedMembers =
        new HashSet<string>(StringComparer.Ordinal) { "IsPrivate", "PresentColumnsOf", "NotPrivateODataFilterAsync" };

    private static List<string> Judge(
        IReadOnlyList<(string File, IReadOnlyList<string> Lines)> files, IReadOnlySet<string> allowed, string endpointsFile)
    {
        var aliases = AliasFixedPoint(files);
        return files
            .Where(f => !allowed.Contains(f.File))
            .SelectMany(f => Namings(f.File, f.Lines, aliases))
            .Where(n => !n.IsEncapsulatedCall && !(n.File == endpointsFile && n.IsParserCall))
            .OrderBy(n => n.File, StringComparer.Ordinal).ThenBy(n => n.Line)
            .Select(n => $"{n.File}:{n.Line}: {n.Text}")
            .ToList();
    }
}
