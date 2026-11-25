using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-012 amendment 2026-11-10: the shared-package census. Every package under
/// <c>src/client/shared/</c> is listed here with its status, an ADR citation and a written reason. The
/// list is checked against the folders on disk and against the census table in
/// <c>.claude/adr/ADR-012-shared-components.md</c>, so a new shared package needs both an ADR amendment
/// and a census entry.
///
/// <para><b>What this prevents.</b> The 2026-07-12 amendment sanctioned "the domain component libraries
/// (<c>@spaarke/events-components</c>, etc.)". That "etc." covered 11 of the 15 packages, and nine of
/// them were never named by any ADR. ADR-012 already set a three-test bar for a new sibling package, but
/// nothing ran it, so packages were added without meeting it. The risk here is over-promotion: too many
/// shared packages, not too few. This census turns the bar into a build failure.</para>
///
/// <para><b>Two statuses, deliberately.</b> <i>Sanctioned</i> packages have an ADR-backed contract, and
/// <see cref="EverySanctionedPackageIsNamedByItsCitedAdr"/> checks that the cited ADR names the package
/// outside the census table. <i>Grandfathered</i> packages existed before the census and were never
/// assessed. They may be used and extended, but each one is later either sanctioned by an amendment or
/// consolidated into a sanctioned package. The grandfathered list is a ratchet baseline: it may shrink,
/// and growing it means amending the ADR.</para>
///
/// <para>Source and file analysis only. No build output and no node_modules are read.</para>
/// </summary>
public class SharedPackageCensusTests
{
    // =============================================================================================
    // THE CENSUS
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. A failure here is NOT a prompt to add an entry. It is a prompt to ask why a new shared
    //      package exists. ADR-012 requires ALL THREE of: a distinct contract (a different feature area
    //      is not enough), a dependency worth quarantining, and reuse across 2+ surfaces. If any one
    //      fails, extend an existing package (root CLAUDE.md §11) and delete the new folder.
    //
    //   2. If the package clears that bar, amend ADR-012 (Path B, root CLAUDE.md §6.5; the owner
    //      approves). Then add a row to the census table in .claude/adr/ADR-012-shared-components.md
    //      AND an entry here as Sanctioned, citing the ADR that defines the package's contract. That ADR
    //      must name the package outside the census table.
    //
    //      Pattern D feature packages (ADR-012 amendment 2026-11-18) meet a different, closed bar: 2+
    //      importing surfaces, domain-bound, PCFs deep-import only. The list is PatternDFeaturePackages;
    //      a new name there needs its own amendment, exactly like a new census entry.
    //
    //   3. Never add a Grandfathered entry. That status is only for packages that existed when the
    //      census was taken (2026-11-10). To resolve one, either sanction it (step 2) or consolidate it
    //      into a sanctioned package, then delete its folder, its ADR row and its entry here.
    //
    //   4. Renaming a package or its folder: update the entry, the ADR row and every consumer's
    //      package.json in the same change.
    // =============================================================================================
    private static readonly IReadOnlyList<CensusEntry> Census = new[]
    {
        // ---- Sanctioned -----------------------------------------------------------------------
        new CensusEntry("@spaarke/ui-components", "Spaarke.UI.Components", PackageStatus.Sanctioned, "ADR-012",
            "The default home for reusable UI: UX components and abstracted-I/O services behind IDataService. "
            + "Every other package must justify itself against extending this one."),
        new CensusEntry("@spaarke/visuals", "Spaarke.Visuals", PackageStatus.Sanctioned, "ADR-012",
            "Presentational data-viz with no I/O. Quarantines @fluentui/react-charting and pins @types/react@18 "
            + "so PCF and Code Pages can both use it without casts (2026-07-12 amendment)."),
        new CensusEntry("@spaarke/auth", "Spaarke.Auth", PackageStatus.Sanctioned, "ADR-028",
            "The single SSO, token-acquisition and authenticated-fetch implementation shared by every surface; "
            + "a second copy would break the true-SSO requirement."),
        new CensusEntry("@spaarke/events-components", "Spaarke.Events.Components", PackageStatus.Sanctioned, "ADR-012",
            "Events and Tasks components shared by the EventsPage code page and the SpaarkeAi Calendar widget; "
            + "the one domain library the 2026-07-12 amendment named."),
        new CensusEntry("@spaarke/notifications", "Spaarke.Notifications", PackageStatus.Sanctioned, "ADR-047",
            "Host-agnostic client for the notification spine: SignalR delivery, routing by envelope kind and "
            + "poll fallback. ADR-047 defines the contract it implements."),
        new CensusEntry("@spaarke/ai-widgets", "Spaarke.AI.Widgets", PackageStatus.Sanctioned, "ADR-030",
            "Workspace and context widgets for the three-pane shell, built on the pane event bus (ADR-030) and "
            + "the stage lifecycle (ADR-031). Since 2026-11-24 it also holds the internal output / source pane "
            + "widgets (src/outputs) of the retired @spaarke/ai-outputs."),
        // Pattern D feature packages (ADR-012 amendment 2026-11-18; closed list = PatternDFeaturePackages below).
        new CensusEntry("@spaarke/smart-todo-components", "Spaarke.SmartTodo.Components", PackageStatus.Sanctioned, "ADR-012",
            "Pattern D feature package for Smart To Do: imported by the SmartTodo code page and the LegalWorkspace "
            + "section, and domain-bound (sprk_* schema, BFF child-record writes), so ui-components may not hold it."),
        new CensusEntry("@spaarke/communication-components", "Spaarke.Communication.Components", PackageStatus.Sanctioned, "ADR-012",
            "Pattern D feature package for Communications: imported by nine surfaces, three of them PCFs through "
            + "logic/* and components/* sub-paths; domain-bound (sprk_communication schema, filing writes)."),
        new CensusEntry("@spaarke/daily-briefing-components", "Spaarke.DailyBriefing.Components", PackageStatus.Sanctioned, "ADR-012",
            "Pattern D feature package for Daily Briefing: imported by the DailyBriefing code page and the "
            + "LegalWorkspace section, and domain-bound (sprk_* schema), so ui-components may not hold it."),
        new CensusEntry("@spaarke/compose-components", "Spaarke.Compose.Components", PackageStatus.Sanctioned, "ADR-012",
            "The Compose document-editing model (TipTap document, DOCX bridge, checkout, comments). Quarantines "
            + "TipTap/ProseMirror away from ui-components and the PCFs; used by SpaarkeAi and LegalWorkspace "
            + "(2026-11-24 amendment; quarantine and entity-map scans below)."),

        // ---- Grandfathered — under review (baseline 2026-11-10; may shrink, never grow) ----------
        new CensusEntry("@spaarke/sdap-client", "Spaarke.SdapClient", PackageStatus.Grandfathered, "ADR-012",
            "SDAP API client used by 18 PCF controls and TS apps. Pre-dates the census; whether it overlaps "
            + "the BFF adapters in @spaarke/ui-components has not been assessed."),
        // smart-todo, communication and daily-briefing components: sanctioned 2026-11-18 (Pattern D amendment).
        // compose-components: sanctioned 2026-11-24 (Compose amendment).
        // @spaarke/document-operations: retired 2026-11-13; useDocumentActions moved to @spaarke/ui-components (src/hooks).
        // @spaarke/ai-outputs: retired 2026-11-24; its widgets and types moved into @spaarke/ai-widgets (src/outputs).
        // @spaarke/ai-context: retired 2026-11-13; its types moved to @spaarke/ai-widgets (src/types/aiContext.ts).
        // @spaarke/legal-workspace: retired 2026-11-13; SpaarkeAi depends on the LegalWorkspace solution directly.
    };

    /// <summary>
    /// The closed list of Pattern D feature packages (ADR-012 amendment 2026-11-18). Condition (c) of that
    /// amendment: a PCF reaches them only through a sub-path, never the barrel. Adding a name here needs the
    /// same amendment that sanctions the package.
    /// </summary>
    private static readonly IReadOnlyList<string> PatternDFeaturePackages = new[]
    {
        "@spaarke/smart-todo-components",
        "@spaarke/communication-components",
        "@spaarke/daily-briefing-components",
    };

    /// <summary>
    /// The Compose amendment (ADR-012 2026-11-24). <see cref="ComposeFolder"/> is the only place whose non-test
    /// source may import TipTap / ProseMirror, and <see cref="ComposeSchemaFile"/> is the only non-test source
    /// file in it whose code (outside comments) may name a <c>sprk_</c> entity or column (finding P-8). Moving either needs the amendment
    /// updated in the same change.
    /// </summary>
    private const string ComposeFolder = "Spaarke.Compose.Components";
    private const string ComposeSchemaFile = "src/types/composeDocumentSchema.ts";

    private static readonly Regex EditorEngineImport = new(
        @"(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+|\brequire\s*\(\s*)(['""])(?<module>@tiptap/[^'""]+|prosemirror-[^'""]+)\1",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DataverseName = new(
        @"\bsprk_[a-z0-9_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string ConciseAdrRelativePath = ".claude/adr/ADR-012-shared-components.md";
    private const string CensusStartMarker = "<!-- shared-package-census:start -->";
    private const string CensusEndMarker = "<!-- shared-package-census:end -->";

    private static readonly Regex AdrTableRow = new(
        @"^\|\s*`(?<package>@spaarke/[a-z0-9.-]+)`\s*\|\s*`(?<folder>[^`]+)`\s*\|\s*(?<status>[^|]+?)\s*\|",
        RegexOptions.Compiled);

    // =============================================================================================

    [Fact(DisplayName = "ADR-012: every src/client/shared package is in the census, under the census name")]
    public void EverySharedPackageIsCensused()
    {
        var problems = CompareWithDisk(ScanSharedPackages(), Census);

        Assert.True(
            problems.Count == 0,
            "The shared-package census does not match src/client/shared/.\n\n"
            + "A failure here is NOT a prompt to add a census entry. ADR-012 allows a new shared package only "
            + "when it has a distinct contract, a dependency worth quarantining AND reuse across 2+ surfaces. "
            + "Otherwise extend an existing package. If the package is justified, amend ADR-012 and add a "
            + "Sanctioned entry, following the MAINTENANCE PROCEDURE above the census.\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ADR-012: the census table in the concise ADR matches the test census, row for row")]
    public void AdrCensusTableMatchesTheTestCensus()
    {
        var adrText = File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, ConciseAdrRelativePath));
        var rows = ParseAdrCensusTable(adrText);

        Assert.True(rows.Count > 0, $"No census table found between the markers in {ConciseAdrRelativePath}.");

        var problems = CompareWithAdr(rows, Census);

        Assert.True(
            problems.Count == 0,
            $"The census table in {ConciseAdrRelativePath} and the census in this file disagree. They are "
            + "one decision written in two places: change both in the same PR, and the ADR change needs "
            + "owner approval (root CLAUDE.md §6.5, Path B).\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ADR-012: every census entry carries an ADR citation and a substantive reason")]
    public void EveryCensusEntryIsExplained()
    {
        var unexplained = Census
            .Where(e => !Regex.IsMatch(e.AdrCitation, @"^ADR-\d{3}$")
                        || string.IsNullOrWhiteSpace(e.Reason)
                        || e.Reason.Trim().Length < 60)
            .Select(e => e.Package)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every census entry needs an ADR citation (ADR-NNN) and a reason of at least 60 characters. An "
            + "unexplained entry cannot be told apart from an oversight six months later. Entries missing one: "
            + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "ADR-012: every Sanctioned package is named by its cited ADR outside the census table")]
    public void EverySanctionedPackageIsNamedByItsCitedAdr()
    {
        // "Sanctioned" means an ADR defines the package's contract. The census table names every package,
        // so it is cut out before searching. Otherwise every package would count as named by ADR-012.
        var problems = new List<string>();

        foreach (var entry in Census.Where(e => e.Status == PackageStatus.Sanctioned))
        {
            var adrFiles = AdrFiles(entry.AdrCitation);
            if (adrFiles.Count == 0)
            {
                problems.Add($"{entry.Package}: cites {entry.AdrCitation}, but no file for it exists in .claude/adr or docs/adr.");
                continue;
            }

            var named = adrFiles.Any(f => WithoutCensusTable(File.ReadAllLines(f)).Contains(entry.Package, StringComparison.Ordinal));
            if (!named)
            {
                problems.Add($"{entry.Package}: {entry.AdrCitation} does not name the package outside the census table.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A Sanctioned entry must cite an ADR that defines the package's contract. If no ADR does, the "
            + "package is Grandfathered, not Sanctioned.\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ADR-012: no PCF imports a Pattern D feature package through its barrel")]
    public void NoPcfImportsAPatternDFeaturePackageThroughItsBarrel()
    {
        // Pattern D amendment (2026-11-18), condition (c). The barrel of a feature package re-exports its
        // widgets and code-page components; a PCF that imports it pulls all of that into a React 16 bundle.
        // PCFs take what they need from a sub-path (@spaarke/communication-components/logic/connections).
        var pcfRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "pcf");
        var violations = PcfSourceFiles(pcfRoot)
            .SelectMany(f => BarrelImports(File.ReadAllLines(f), PatternDFeaturePackages)
                .Select(hit => $"{Path.GetRelativePath(SourceScan.RepoRoot, f).Replace('\\', '/')}:{hit.Line} imports {hit.Package}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A PCF imports a Pattern D feature package through its barrel. ADR-012 (2026-11-18 amendment, condition c) "
            + "allows only sub-path imports, e.g. '@spaarke/communication-components/logic/connections'. Import the "
            + "module you need from its sub-path (and map it in the PCF's tsconfig/jest like the existing ones).\n\n"
            + string.Join("\n", violations));
    }

    [Fact(DisplayName = "ADR-012: every Pattern D feature package is Sanctioned in the census")]
    public void EveryPatternDFeaturePackageIsSanctioned()
    {
        var notSanctioned = PatternDFeaturePackages
            .Where(p => !Census.Any(e => e.Package == p && e.Status == PackageStatus.Sanctioned))
            .ToList();

        Assert.True(
            notSanctioned.Count == 0,
            "The Pattern D list names a package that is not a Sanctioned census entry. The list and the census are "
            + "one decision (ADR-012 2026-11-18 amendment): " + string.Join(", ", notSanctioned));
    }

    [Fact(DisplayName = "ADR-012: TipTap and ProseMirror are imported only by the Compose package")]
    public void TipTapIsImportedOnlyByTheComposePackage()
    {
        // Compose amendment (2026-11-24), the quarantine test. Editor-engine code outside Compose either
        // pulls TipTap into another bundle (ui-components → every PCF) or starts a second document editor.
        // Tests may drive a real TipTap editor; production source may not.
        var composeRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", ComposeFolder);
        var roots = new[]
        {
            Path.Combine(SourceScan.RepoRoot, "src", "client"),
            Path.Combine(SourceScan.RepoRoot, "src", "solutions"),
        };

        var violations = roots
            .SelectMany(ClientSourceFiles)
            .Where(f => !IsUnder(f, composeRoot) && !IsTestFile(f))
            .SelectMany(f => EditorEngineImports(File.ReadAllLines(f))
                .Select(hit => $"{Rel(f)}:{hit.Line} imports {hit.Module}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Production source outside @spaarke/compose-components imports TipTap or ProseMirror. ADR-012 (2026-11-24 "
            + "Compose amendment) quarantines the editor engine in that package: document editing goes through "
            + "ComposeEditor; a rich-text field uses RichTextEditor (Lexical) from @spaarke/ui-components.\n\n"
            + string.Join("\n", violations));
    }

    [Fact(DisplayName = "ADR-012: no other shared package and no PCF declares TipTap or ProseMirror")]
    public void NoOtherSharedPackageOrPcfDeclaresTipTap()
    {
        var manifests = Directory
            .EnumerateDirectories(Path.Combine(SourceScan.RepoRoot, "src", "client", "shared"))
            .Where(d => !string.Equals(Path.GetFileName(d), ComposeFolder, StringComparison.Ordinal))
            .Select(d => Path.Combine(d, "package.json"))
            .Where(File.Exists)
            .Concat(PrunedFiles(Path.Combine(SourceScan.RepoRoot, "src", "client", "pcf"))
                .Where(f => string.Equals(Path.GetFileName(f), "package.json", StringComparison.Ordinal)));

        var violations = manifests
            .SelectMany(m => EditorEngineDeclarations(File.ReadAllText(m)).Select(dep => $"{Rel(m)} declares {dep}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A shared package other than @spaarke/compose-components, or a PCF, declares TipTap or ProseMirror. "
            + "ADR-012 (2026-11-24 Compose amendment) keeps the editor engine out of their bundles.\n\n"
            + string.Join("\n", violations));
    }

    [Fact(DisplayName = "ADR-012: Compose keeps its Dataverse names in its one entity map")]
    public void ComposeKeepsDataverseNamesInItsEntityMap()
    {
        // Compose amendment (2026-11-24), finding P-8. ADR-012 forbids entity-name literals in shared packages;
        // Compose is allowed the sprk_document names it needs, in COMPOSE_DOCUMENT_SCHEMA only.
        var composeSrc = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", ComposeFolder, "src");
        var schemaFile = Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", ComposeFolder, ComposeSchemaFile));
        Assert.True(File.Exists(schemaFile), $"The Compose entity map moved or was renamed: {Rel(schemaFile)}. Update ComposeSchemaFile and the amendment.");

        var violations = ClientSourceFiles(composeSrc)
            .Where(f => !IsTestFile(f) && !string.Equals(Path.GetFullPath(f), schemaFile, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => DataverseNamesInCode(File.ReadAllLines(f)).Select(line => $"{Rel(f)}:{line}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A Compose source file names a sprk_ entity or column in code. ADR-012 (2026-11-24 Compose amendment, P-8): add the "
            + $"name to COMPOSE_DOCUMENT_SCHEMA in {ComposeSchemaFile} and use it from there.\n\n"
            + string.Join("\n", violations));
    }

    // =============================================================================================
    // Negative controls: each detector fires on a seeded violation
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: negative control — barrel imports of a Pattern D package are reported in every import form")]
    public void BarrelImports_NegativeControl_ReportsEveryForm()
    {
        var source = new[]
        {
            "import { ConnectionsEditor } from '@spaarke/communication-components';",
            "import type { ITodo } from \"@spaarke/smart-todo-components\";",
            "export * from '@spaarke/daily-briefing-components';",
            "const lazy = import('@spaarke/communication-components');",
            "const old = require(\"@spaarke/smart-todo-components\");",
            "import {",
            "  A,",
            "} from '@spaarke/daily-briefing-components'",
            "import '@spaarke/communication-components';",
        };

        var hits = BarrelImports(source, PatternDFeaturePackages);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 8, 9 }, hits.Select(h => h.Line).ToArray());
    }

    // =============================================================================================
    // Positive controls (Pattern D): sub-path imports and other packages stay silent
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: positive control — sub-path imports, comments and other packages are not barrel imports")]
    public void BarrelImports_PositiveControl_StaysSilentOnTheSanctionedShape()
    {
        var source = new[]
        {
            "import { applyRegardingSelection } from '@spaarke/communication-components/logic/connections';",
            "import { AttachmentList } from '@spaarke/communication-components/components/AttachmentList';",
            "import { KanbanBoard } from '@spaarke/ui-components';",
            "import { x } from '@spaarke/communication-components-extra';",
            "// import { y } from '@spaarke/smart-todo-components';",
            " * see '@spaarke/daily-briefing-components' for the widget",
        };

        Assert.Empty(BarrelImports(source, PatternDFeaturePackages));
    }

    [Fact(DisplayName = "ADR-012: negative control — the PCF scan really reaches the Communication PCFs")]
    public void PcfSourceFiles_NegativeControl_FindsTheCommunicationPcfs()
    {
        // A scan that silently found no files would pass the barrel rule vacuously.
        var pcfRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "pcf");
        var files = PcfSourceFiles(pcfRoot).Select(f => f.Replace('\\', '/')).ToList();

        Assert.Contains(files, f => f.Contains("/CommunicationConnections/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Contains("/node_modules/", StringComparison.Ordinal));
    }

    // =============================================================================================
    // Controls (Compose amendment): quarantine and entity-map detectors
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: negative control — TipTap and ProseMirror imports are reported in every import form")]
    public void EditorEngineImports_NegativeControl_ReportsEveryForm()
    {
        var source = new[]
        {
            "import { Editor } from '@tiptap/core';",
            "import StarterKit from \"@tiptap/starter-kit\";",
            "export * from '@tiptap/react';",
            "const ext = await import('@tiptap/extension-table');",
            "const state = require('prosemirror-state');",
            "import { EditorView } from 'prosemirror-view';",
            "import '@tiptap/extension-link';",
        };

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, EditorEngineImports(source).Select(h => h.Line).ToArray());
    }

    [Fact(DisplayName = "ADR-012: positive control — comments, other editors and look-alike names are not TipTap imports")]
    public void EditorEngineImports_PositiveControl_StaysSilent()
    {
        var source = new[]
        {
            "// import { Editor } from '@tiptap/core';",
            " * see '@tiptap/react' for the hook",
            "import { LexicalComposer } from '@lexical/react/LexicalComposer';",
            "import { ComposeEditor } from '@spaarke/compose-components';",
            "const label = 'prosemirror';",
        };

        Assert.Empty(EditorEngineImports(source));
    }

    [Fact(DisplayName = "ADR-012: negative control — TipTap and ProseMirror declarations are reported in every dependency block")]
    public void EditorEngineDeclarations_NegativeControl_ReportsEveryBlock()
    {
        const string manifest = """
            {
              "name": "@spaarke/x",
              "dependencies": { "@tiptap/core": "^2.10.3", "react": "^19.0.0" },
              "devDependencies": { "prosemirror-model": "^1.0.0" },
              "peerDependencies": { "@tiptap/react": "^2.0.0" }
            }
            """;

        Assert.Equal(
            new[] { "@tiptap/core", "@tiptap/react", "prosemirror-model" },
            EditorEngineDeclarations(manifest).OrderBy(d => d, StringComparer.Ordinal).ToArray());
    }

    [Fact(DisplayName = "ADR-012: positive control — a manifest without TipTap, or naming it outside a dependency block, is silent")]
    public void EditorEngineDeclarations_PositiveControl_StaysSilent()
    {
        const string manifest = """
            {
              "name": "@spaarke/x",
              "keywords": ["tiptap", "@tiptap/core"],
              "dependencies": { "lexical": "^0.12.0", "@spaarke/compose-components": "file:../Spaarke.Compose.Components" }
            }
            """;

        Assert.Empty(EditorEngineDeclarations(manifest));
    }

    [Fact(DisplayName = "ADR-012: negative control — sprk_ names in strings, templates, OData and property access are reported")]
    public void DataverseNamesInCode_NegativeControl_ReportsEveryForm()
    {
        var source = new[]
        {
            "const ENTITY = 'sprk_document';",
            "retrieveRecord(\"sprk_matter\", id);",
            "const select = `?$select=sprk_name`;",
            "const id = record.sprk_documentid;",
            "openRecord('sprk_document', id); // trailing comment",
            "/* a comment */ const e = 'sprk_event';",
        };

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, DataverseNamesInCode(source).ToArray());
    }

    [Fact(DisplayName = "ADR-012: positive control — sprk_ in comments and through the entity map is not reported")]
    public void DataverseNamesInCode_PositiveControl_StaysSilent()
    {
        var source = new[]
        {
            "// opens a `sprk_document` record",
            "/**",
            " * the `sprk_documentid` after first Save",
            " */",
            "{/* \"Memo\" collided with",
            "    sprk_memo, the Notepad entity */}",
            "openRecord(COMPOSE_DOCUMENT_SCHEMA.entityName, id); // the sprk_document record",
            "const x = record[COMPOSE_DOCUMENT_SCHEMA.fields.graphItemId];",
            "const notAName = 'xsprk_value';",
        };

        Assert.Empty(DataverseNamesInCode(source));
    }

    [Fact(DisplayName = "ADR-012: negative control — the Compose scans really reach Compose, SpaarkeAi and the PCFs")]
    public void ClientSourceFiles_NegativeControl_ReachesTheScannedTrees()
    {
        // A scan that silently found no files would pass every Compose rule vacuously.
        var clientFiles = ClientSourceFiles(Path.Combine(SourceScan.RepoRoot, "src", "client")).Select(f => f.Replace('\\', '/')).ToList();
        var solutionFiles = ClientSourceFiles(Path.Combine(SourceScan.RepoRoot, "src", "solutions")).Select(f => f.Replace('\\', '/')).ToList();

        Assert.Contains(clientFiles, f => f.EndsWith($"/{ComposeFolder}/src/widgets/ComposeWorkspace.tsx", StringComparison.Ordinal));
        Assert.Contains(clientFiles, f => f.Contains("/src/client/pcf/", StringComparison.Ordinal));
        Assert.Contains(solutionFiles, f => f.Contains("/SpaarkeAi/src/", StringComparison.Ordinal));
        Assert.DoesNotContain(clientFiles.Concat(solutionFiles), f => f.Contains("/node_modules/", StringComparison.Ordinal));
        Assert.True(IsTestFile(Path.Combine("x", "__tests__", "useEditSupersession.test.tsx")));
        Assert.False(IsTestFile(Path.Combine("x", "widgets", "ComposeWorkspace.tsx")));
    }

    [Fact(DisplayName = "ADR-012: negative control — an unlisted folder, an absent entry and a renamed package are all reported")]
    public void CompareWithDisk_NegativeControl_ReportsEveryKindOfDrift()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/kept", "Spaarke.Kept", PackageStatus.Sanctioned, "ADR-012", Pad("kept")),
            new CensusEntry("@spaarke/gone", "Spaarke.Gone", PackageStatus.Grandfathered, "ADR-012", Pad("gone")),
            new CensusEntry("@spaarke/old-name", "Spaarke.Renamed", PackageStatus.Grandfathered, "ADR-012", Pad("renamed")),
        };
        var disk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Spaarke.Kept"] = "@spaarke/kept",
            ["Spaarke.Renamed"] = "@spaarke/new-name",
            ["Spaarke.Sneaky"] = "@spaarke/sneaky",
        };

        var problems = CompareWithDisk(disk, census);

        Assert.Contains(problems, p => p.StartsWith("UNLISTED", StringComparison.Ordinal) && p.Contains("Spaarke.Sneaky", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("CENSUSED BUT ABSENT", StringComparison.Ordinal) && p.Contains("Spaarke.Gone", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("NAME MISMATCH", StringComparison.Ordinal) && p.Contains("@spaarke/new-name", StringComparison.Ordinal));
        Assert.Equal(3, problems.Count);
    }

    [Fact(DisplayName = "ADR-012: negative control — an ADR row with a different status, a missing row and an extra row are all reported")]
    public void CompareWithAdr_NegativeControl_ReportsEveryKindOfDrift()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/promoted", "Spaarke.Promoted", PackageStatus.Grandfathered, "ADR-012", Pad("promoted")),
            new CensusEntry("@spaarke/unrowed", "Spaarke.Unrowed", PackageStatus.Sanctioned, "ADR-012", Pad("unrowed")),
        };
        var markdown = new[]
        {
            "| Package | Folder | Status | Contract / reason |",
            "|---|---|---|---|",
            CensusStartMarker,
            "| `@spaarke/promoted` | `Spaarke.Promoted` | Sanctioned | quietly promoted in the ADR only |",
            "| `@spaarke/extra` | `Spaarke.Extra` | Grandfathered — under review | added to the ADR only |",
            CensusEndMarker,
        };

        var problems = CompareWithAdr(ParseAdrCensusTable(markdown), census);

        Assert.Contains(problems, p => p.StartsWith("STATUS MISMATCH", StringComparison.Ordinal) && p.Contains("@spaarke/promoted", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("MISSING FROM ADR", StringComparison.Ordinal) && p.Contains("@spaarke/unrowed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("ONLY IN ADR", StringComparison.Ordinal) && p.Contains("@spaarke/extra", StringComparison.Ordinal));
        Assert.Equal(3, problems.Count);
    }

    [Fact(DisplayName = "ADR-012: negative control — the disk scan really reaches src/client/shared")]
    public void ScanSharedPackages_NegativeControl_FindsTheRealPackages()
    {
        // A scan that silently found nothing would turn every entry into CENSUSED BUT ABSENT, and that
        // failure would point at the wrong cause. Prove the scan reads real package names.
        var scanned = ScanSharedPackages();

        Assert.Equal("@spaarke/ui-components", scanned["Spaarke.UI.Components"]);
        Assert.Equal("@spaarke/auth", scanned["Spaarke.Auth"]);
    }

    // =============================================================================================
    // Positive controls: the detectors stay silent on the sanctioned shape
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: positive control — matching disk, census and ADR table report nothing")]
    public void Comparers_PositiveControl_StaySilentWhenEverythingAgrees()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/one", "Spaarke.One", PackageStatus.Sanctioned, "ADR-012", Pad("one")),
            new CensusEntry("@spaarke/two", "Spaarke.Two", PackageStatus.Grandfathered, "ADR-012", Pad("two")),
        };
        var disk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Spaarke.One"] = "@spaarke/one",
            ["Spaarke.Two"] = "@spaarke/two",
        };
        var markdown = new[]
        {
            "Rows outside the markers are ignored:",
            "| `@spaarke/outside` | `Spaarke.Outside` | Sanctioned | not part of the census |",
            CensusStartMarker,
            "| Package | Folder | Status | Contract / reason |",
            "|---|---|---|---|",
            "| `@spaarke/one` | `Spaarke.One` | Sanctioned | reason |",
            "| `@spaarke/two` | `Spaarke.Two` | Grandfathered — under review | reason |",
            CensusEndMarker,
        };

        Assert.Empty(CompareWithDisk(disk, census));
        Assert.Empty(CompareWithAdr(ParseAdrCensusTable(markdown), census));
    }

    // =============================================================================================
    // Machinery
    // =============================================================================================

    private enum PackageStatus
    {
        Sanctioned,
        Grandfathered,
    }

    private sealed record CensusEntry(
        string Package,
        string Folder,
        PackageStatus Status,
        string AdrCitation,
        string Reason);

    private sealed record AdrRow(string Package, string Folder, PackageStatus? Status, string RawStatus);

    /// <summary>Folder name → the <c>name</c> in its package.json, for every direct child of src/client/shared.</summary>
    private static Dictionary<string, string> ScanSharedPackages()
    {
        var sharedRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared");
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dir in Directory.EnumerateDirectories(sharedRoot))
        {
            var manifest = Path.Combine(dir, "package.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var name = json.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            found[Path.GetFileName(dir)] = name;
        }

        return found;
    }

    private static List<string> CompareWithDisk(IReadOnlyDictionary<string, string> disk, IReadOnlyList<CensusEntry> census)
    {
        var problems = new List<string>();
        var byFolder = census.ToDictionary(e => e.Folder, StringComparer.Ordinal);

        foreach (var (folder, name) in disk.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!byFolder.TryGetValue(folder, out var entry))
            {
                problems.Add($"UNLISTED shared package: src/client/shared/{folder} ({name}) is not in the census.");
            }
            else if (!string.Equals(entry.Package, name, StringComparison.Ordinal))
            {
                problems.Add($"NAME MISMATCH: src/client/shared/{folder} is named {name}, but the census lists it as {entry.Package}.");
            }
        }

        foreach (var entry in census.Where(e => !disk.ContainsKey(e.Folder)))
        {
            problems.Add(
                $"CENSUSED BUT ABSENT: {entry.Package} (src/client/shared/{entry.Folder}) has no package.json. "
                + "If it was consolidated or deleted, remove its entry and its ADR row.");
        }

        return problems;
    }

    private static List<string> CompareWithAdr(IReadOnlyList<AdrRow> rows, IReadOnlyList<CensusEntry> census)
    {
        var problems = new List<string>();
        var byPackage = census.ToDictionary(e => e.Package, StringComparer.Ordinal);
        var rowPackages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            rowPackages.Add(row.Package);

            if (!byPackage.TryGetValue(row.Package, out var entry))
            {
                problems.Add($"ONLY IN ADR: {row.Package} has an ADR census row but no entry in this file.");
                continue;
            }

            if (!string.Equals(row.Folder, entry.Folder, StringComparison.Ordinal))
            {
                problems.Add($"FOLDER MISMATCH: {row.Package} is in folder {row.Folder} per the ADR, {entry.Folder} per this file.");
            }

            if (row.Status != entry.Status)
            {
                problems.Add($"STATUS MISMATCH: {row.Package} is '{row.RawStatus}' in the ADR, {entry.Status} in this file.");
            }
        }

        foreach (var entry in census.Where(e => !rowPackages.Contains(e.Package)))
        {
            problems.Add($"MISSING FROM ADR: {entry.Package} is in this file's census but has no row in the ADR census table.");
        }

        return problems;
    }

    /// <summary>The package rows between the census markers. Header and separator rows do not match the row shape.</summary>
    private static List<AdrRow> ParseAdrCensusTable(IReadOnlyList<string> lines)
    {
        var rows = new List<AdrRow>();
        var inside = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == CensusStartMarker) { inside = true; continue; }
            if (trimmed == CensusEndMarker) { inside = false; continue; }
            if (!inside) { continue; }

            var match = AdrTableRow.Match(trimmed);
            if (!match.Success) { continue; }

            var raw = match.Groups["status"].Value.Trim();
            PackageStatus? status =
                raw.StartsWith("Sanctioned", StringComparison.Ordinal) ? PackageStatus.Sanctioned
                : raw.StartsWith("Grandfathered", StringComparison.Ordinal) ? PackageStatus.Grandfathered
                : null;

            rows.Add(new AdrRow(match.Groups["package"].Value, match.Groups["folder"].Value, status, raw));
        }

        return rows;
    }

    private static string WithoutCensusTable(IReadOnlyList<string> lines)
    {
        var kept = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == CensusStartMarker) { inside = true; continue; }
            if (trimmed == CensusEndMarker) { inside = false; continue; }
            if (!inside) { kept.Add(line); }
        }

        return string.Join("\n", kept);
    }

    /// <summary>Concise and full ADR files for a citation such as <c>ADR-047</c>.</summary>
    private static List<string> AdrFiles(string citation)
    {
        var dirs = new[]
        {
            Path.Combine(SourceScan.RepoRoot, ".claude", "adr"),
            Path.Combine(SourceScan.RepoRoot, "docs", "adr"),
        };

        return dirs
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, $"{citation}-*.md"))
            .ToList();
    }

    /// <summary>Hand-written PCF source: <c>.ts</c>/<c>.tsx</c>/<c>.js</c>, skipping installs, build output and generated types.</summary>
    private static IEnumerable<string> PcfSourceFiles(string pcfRoot)
    {
        var skipped = new[] { "node_modules", "out", "dist", "bin", "obj", "generated", "Solution" }
            .Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}")
            .ToArray();

        return Directory
            .EnumerateFiles(pcfRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal)
                        || f.EndsWith(".tsx", StringComparison.Ordinal)
                        || f.EndsWith(".js", StringComparison.Ordinal))
            .Where(f => !skipped.Any(s => f.Contains(s, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Lines that import one of <paramref name="packages"/> by its bare name (the barrel): <c>from '…'</c>,
    /// <c>import '…'</c> (side effect), <c>import('…')</c>, <c>require('…')</c>. A sub-path (<c>'pkg/logic/x'</c>) is not a barrel import.
    /// Comment lines are skipped; a multi-line import is reported on its <c>from</c> line.
    /// </summary>
    private static List<(int Line, string Package)> BarrelImports(IReadOnlyList<string> lines, IReadOnlyList<string> packages)
    {
        var names = string.Join("|", packages.Select(Regex.Escape));
        var pattern = new Regex(
            $@"(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+|\brequire\s*\(\s*)(['""])(?<package>{names})\1",
            RegexOptions.CultureInvariant);

        var hits = new List<(int, string)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                hits.Add((i + 1, match.Groups["package"].Value));
            }
        }

        return hits;
    }

    /// <summary>
    /// Hand-written client source (<c>.ts</c>/<c>.tsx</c>/<c>.js</c>/<c>.mjs</c>) under <paramref name="root"/>, skipping
    /// installs, build output and generated files. Test files are included; callers filter with <see cref="IsTestFile"/>.
    /// </summary>
    private static IEnumerable<string> ClientSourceFiles(string root) =>
        PrunedFiles(root)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal)
                        || f.EndsWith(".tsx", StringComparison.Ordinal)
                        || f.EndsWith(".js", StringComparison.Ordinal)
                        || f.EndsWith(".mjs", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".d.ts", StringComparison.Ordinal));

    private static readonly HashSet<string> PrunedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "out", "dist", "build", "bin", "obj", "generated", "Solution", "coverage", ".git",
    };

    /// <summary>
    /// Every file under <paramref name="root"/>, never descending into installs or build output. The directories
    /// are pruned during the walk, not filtered afterwards: enumerating every node_modules under src/client and
    /// src/solutions takes minutes.
    /// </summary>
    private static IEnumerable<string> PrunedFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!PrunedDirectories.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    private static bool IsTestFile(string path)
    {
        var name = Path.GetFileName(path);
        var sep = Path.DirectorySeparatorChar;
        return name.Contains(".test.", StringComparison.Ordinal)
               || name.Contains(".spec.", StringComparison.Ordinal)
               || path.Contains($"{sep}__tests__{sep}", StringComparison.Ordinal)
               || path.Contains($"{sep}__mocks__{sep}", StringComparison.Ordinal);
    }

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Rel(string path) => Path.GetRelativePath(SourceScan.RepoRoot, path).Replace('\\', '/');

    private static bool IsCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
               || trimmed.StartsWith("*", StringComparison.Ordinal)
               || trimmed.StartsWith("/*", StringComparison.Ordinal)
               || trimmed.StartsWith("{/*", StringComparison.Ordinal);
    }

    /// <summary>Lines that import a <c>@tiptap/*</c> or <c>prosemirror-*</c> module in any import form; comment lines are skipped.</summary>
    private static List<(int Line, string Module)> EditorEngineImports(IReadOnlyList<string> lines)
    {
        var hits = new List<(int, string)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsCommentLine(lines[i]))
            {
                continue;
            }

            var match = EditorEngineImport.Match(lines[i]);
            if (match.Success)
            {
                hits.Add((i + 1, match.Groups["module"].Value));
            }
        }

        return hits;
    }

    /// <summary>The <c>@tiptap/*</c> and <c>prosemirror-*</c> names in a manifest's dependency, devDependency and peerDependency blocks.</summary>
    private static List<string> EditorEngineDeclarations(string manifestJson)
    {
        using var json = JsonDocument.Parse(manifestJson);
        var found = new List<string>();

        foreach (var block in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
        {
            if (!json.RootElement.TryGetProperty(block, out var deps) || deps.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            found.AddRange(deps.EnumerateObject()
                .Select(p => p.Name)
                .Where(n => n.StartsWith("@tiptap/", StringComparison.Ordinal) || n.StartsWith("prosemirror-", StringComparison.Ordinal)));
        }

        return found;
    }

    /// <summary>
    /// Line numbers whose code (comments removed) names a <c>sprk_</c> entity or column: in a string, a template,
    /// an OData <c>$select</c> or a property access.
    /// </summary>
    private static List<int> DataverseNamesInCode(IReadOnlyList<string> lines)
    {
        var code = WithoutComments(lines);
        return Enumerable.Range(0, code.Count)
            .Where(i => DataverseName.IsMatch(code[i]))
            .Select(i => i + 1)
            .ToList();
    }

    /// <summary>
    /// Each line with <c>//</c> line comments and <c>/* … */</c> block comments (also across lines, also JSX
    /// <c>{/* … */}</c>) removed. Strings are not parsed: a <c>//</c> or <c>/*</c> inside a string is treated as a
    /// comment opener, so a string such as <c>'image/*'</c> hides the code after it (up to the next <c>*/</c>) from
    /// the scan. That is a recall limit, not a false-positive risk; no Compose source has such a string today.
    /// </summary>
    private static List<string> WithoutComments(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        var inBlock = false;

        foreach (var line in lines)
        {
            var kept = new System.Text.StringBuilder();
            var i = 0;
            while (i < line.Length)
            {
                if (inBlock)
                {
                    var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) { i = line.Length; } else { inBlock = false; i = end + 2; }
                    continue;
                }

                if (string.CompareOrdinal(line, i, "/*", 0, 2) == 0) { inBlock = true; i += 2; continue; }
                if (string.CompareOrdinal(line, i, "//", 0, 2) == 0) { break; }

                kept.Append(line[i]);
                i++;
            }

            result.Add(kept.ToString());
        }

        return result;
    }

    private static string Pad(string seed) => $"{seed}: a control entry whose reason is long enough to pass the explanation rule.";
}
