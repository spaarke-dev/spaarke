using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 147 r1 (owner round 28 item 2, "E2") — the secure-record child "New" commands agree
/// across the four files that carry them, and every secure-child subgrid on a root form is covered.
/// </summary>
/// <remarks>
/// <para><b>The defect this prevents.</b> The platform subgrid "+ New" creates a child AS THE USER, owned by the user — under
/// a secure record, readable by the user's whole business unit. E2 hides it under a secure host and shows a BFF-backed
/// "New &lt;thing&gt;" instead. The change spans a web-resource script (<c>sprk_secure_child_ribbon.js</c>), a ribbon
/// template, its merge script and the deploy script — four files, two languages, no compiler between them. A table added to
/// one and not the others would ship a hidden platform "+ New" with no replacement, or a replacement that names a create
/// route the BFF refuses. Per ADR-038 Amendment A1, <c>tests/Spaarke.ArchTests/**</c> is a KEEP path.</para>
/// <para><b>The inventory.</b> <see cref="ChildSubgridsOnMainForms"/> is the read-only live inventory of every active main
/// form of spaarkedev1 (2026-10-04; task 147 r1c widened it from the three root forms), filtered to subgrids of ownership
/// child tables. A new such subgrid is added here, and then served (or its native "+ New" hidden some other way, or its
/// host is a party).</para>
/// </remarks>
public class SecureChildNewCommandAgreementTests
{
    private static string Root => SourceScan.RepoRoot;

    private static string ScriptFile => Path.Combine(Root, "src", "client", "webresources", "js", "sprk_secure_child_ribbon.js");
    private static string TemplateFile => Path.Combine(Root, "infrastructure", "dataverse", "ribbon", "SecureChildRibbons", "secure-child-new.template.xml");
    private static string MergeFile => Path.Combine(Root, "infrastructure", "dataverse", "ribbon", "SecureChildRibbons", "Merge-SecureChildRibbon.ps1");
    private static string DeployFile => Path.Combine(Root, "scripts", "Deploy-SecureChildNewCommands.ps1");
    private static string ChildRecordEndpointsFile => Path.Combine(Root, "src", "server", "api", "Sprk.Bff.Api", "Api", "ChildRecordEndpoints.cs");
    private static string AnalysisRibbonFile => Path.Combine(Root, "infrastructure", "dataverse", "ribbon", "AnalysisRibbons", "Entities", "sprk_analysis", "RibbonDiff.xml");
    private static string BillingEventRibbonFile => Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_BillingEvent", "RibbonDiff.xml");
    private static string InvoiceRibbonFile => Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_Invoice", "RibbonDiff.xml");
    private static string ScriptMasterCopyFile => Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "WebResources", "sprk_", "scripts", "secure_child_ribbon.js");

    /// <summary>
    /// EVERY subgrid of an ownership-child table on an active main form (live inventory, spaarkedev1, read-only, 2026-10-04;
    /// task 147 r1c widened it from the three root forms to all forms): (host form table, subgrid table). A child created
    /// by the platform "+ New" on any of these is filed under the host — under a secure record directly (root hosts) or
    /// through it (an event, a document, an invoice, an analysis, a budget of a secure record).
    /// </summary>
    private static readonly (string Host, string Child)[] ChildSubgridsOnMainForms =
    {
        ("sprk_project", "sprk_todo"), ("sprk_project", "sprk_event"), ("sprk_project", "sprk_document"),
        ("sprk_project", "sprk_invoice"), ("sprk_project", "sprk_analysis"), ("sprk_project", "sprk_budget"),
        ("sprk_matter", "sprk_analysis"), ("sprk_matter", "sprk_budget"), ("sprk_matter", "sprk_communication"),
        ("sprk_matter", "sprk_invoice"), ("sprk_matter", "sprk_reportcard"),
        ("sprk_workassignment", "sprk_document"), ("sprk_workassignment", "sprk_event"),
        ("sprk_analysis", "sprk_todo"), ("sprk_budget", "sprk_todo"), ("sprk_document", "sprk_analysis"),
        ("sprk_document", "sprk_todo"), ("sprk_event", "sprk_todo"),
        ("sprk_invoice", "sprk_communication"), ("sprk_invoice", "sprk_document"), ("sprk_invoice", "sprk_event"),
        ("sprk_invoice", "sprk_todo"),
        ("sprk_matter", "sprk_kpiassessment"), ("sprk_project", "sprk_kpiassessment"),
        ("sprk_reportcard", "sprk_kpiassessment"), ("sprk_invoice", "sprk_billingevent"),
        ("contact", "sprk_todo"), ("sprk_organization", "sprk_todo"),
    };

    /// <summary>The party hosts the script lets keep the platform "+ New" (never an ownership parent).</summary>
    private static readonly string[] PartyHosts = { "contact", "sprk_organization", "account" };

    private static string[] ScriptMapKeys(string mapName) =>
        Regex.Matches(
                Regex.Match(File.ReadAllText(ScriptFile), $@"ns\.{mapName}\s*=\s*\{{(?<map>[^;]*)\}};").Groups["map"].Value,
                @"(\w+)\s*:\s*true")
            .Select(m => m.Groups[1].Value).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    private static string[] Quoted(string text) =>
        Regex.Matches(text, "'(sprk_[a-z]+)'|\"(sprk_[a-z]+)\"").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();

    private static string[] ScriptTables() =>
        Quoted(Regex.Match(File.ReadAllText(ScriptFile), @"ns\.TABLES\s*=\s*\[(?<list>[^\]]*)\]").Groups["list"].Value);

    [Fact]
    public void TheServedTables_AreTheSame_InTheScript_TheMergeScript_AndTheDeployScript()
    {
        var script = ScriptTables();
        var merge = Quoted(Regex.Match(File.ReadAllText(MergeFile), @"ValidateSet\((?<list>[^)]*)\)").Groups["list"].Value);
        var deploy = Quoted(Regex.Match(File.ReadAllText(DeployFile), @"\$Tables\s*=\s*@\((?<list>[^)]*)\)").Groups["list"].Value);

        Assert.NotEmpty(script);
        Assert.Equal(script, merge);
        Assert.Equal(script, deploy);
    }

    [Fact]
    public void EveryChildSubgridOnAMainForm_IsServed_OrItsPlatformNewIsHiddenOutright_OrItsHostIsAParty()
    {
        var served = ScriptTables();
        var analysisHidesNative = File.ReadAllText(AnalysisRibbonFile)
            .Contains("Location=\"Mscrm.SubGrid.sprk_analysis.AddNewStandard\"", StringComparison.Ordinal);
        // ebilling-r1 task 096b (EB-D-201): an invoice line's platform "+ New" is hidden outright (no replacement: lines are created only
        // by submission and Add line), read from the SpaarkeMaster source of the table's ribbon.
        var billingEventHidesNative = File.ReadAllText(BillingEventRibbonFile)
            .Contains("Location=\"Mscrm.SubGrid.sprk_billingevent.AddNewStandard\"", StringComparison.Ordinal);

        var uncovered = ChildSubgridsOnMainForms
            .Where(s => !PartyHosts.Contains(s.Host))
            .Where(s => !served.Contains(s.Child)
                && !(s.Child == "sprk_analysis" && analysisHidesNative)
                && !(s.Child == "sprk_billingevent" && billingEventHidesNative))
            .Select(s => $"{s.Host} -> {s.Child}")
            .ToList();

        Assert.True(uncovered.Count == 0,
            "A child subgrid keeps the platform '+ New' (a user-owned create under, or through, a secure record): "
            + string.Join(", ", uncovered));
    }

    /// <summary>
    /// field-mapping-server-write-path-r1 task 066 (owner 2026-11-26, 051 OQ-5: "only +New is required"): the KPI assessment subgrid has ONE
    /// create, "+ New" (the SecureChild create-then-open through the BFF). The second button, "Add KPI Assessment", is retired: no ribbon
    /// source may define or name it. Returns the files whose text names the button id.
    /// </summary>
    private const string RetiredKpiButton = "sprk.matter.subgrid.kpi.AddKpiButton";

    private static string[] FilesNamingTheRetiredButton(IEnumerable<(string Name, string Text)> files) =>
        files.Where(f => f.Text.Contains(RetiredKpiButton, StringComparison.Ordinal)).Select(f => f.Name).ToArray();

    /// <summary>Every ribbon-bearing XML source of the repo: the SpaarkeMaster solution, the ribbon scripts and the SpaarkeCore ribbon fragments.</summary>
    private static IEnumerable<(string Name, string Text)> RibbonXmlSources()
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", ".git" };
        IEnumerable<string> Walk(string dir) =>
            Directory.EnumerateFiles(dir, "*.xml")
                .Concat(Directory.EnumerateDirectories(dir).Where(d => !skip.Contains(Path.GetFileName(d))).SelectMany(Walk));
        foreach (var root in new[]
                 {
                     Path.Combine(Root, "src", "dataverse"),
                     Path.Combine(Root, "src", "solutions"),
                     Path.Combine(Root, "infrastructure", "dataverse", "ribbon"),
                 })
        {
            foreach (var file in Walk(root)) yield return (Path.GetRelativePath(Root, file), File.ReadAllText(file));
        }
    }

    [Fact]
    public void TheKpiAssessmentSubgrid_KeepsItsOneNew_AndNoRepoRibbonCarriesTheAddKpiButton()
    {
        // No ribbon source in the repo defines or names the retired button.
        Assert.Empty(FilesNamingTheRetiredButton(RibbonXmlSources()));

        // The config says it must be absent (so -Verify and the merge enforce it) and no longer requires it.
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Root, "infrastructure", "dataverse", "ribbon", "NativeCreateRibbons", "native-create-surfaces.json")));
        var kpiEntry = config.RootElement.GetProperty("tables").EnumerateArray()
            .Single(e => e.GetProperty("table").GetString() == "sprk_kpiassessment");
        Assert.Contains(RetiredKpiButton, kpiEntry.GetProperty("mustBeAbsent").EnumerateArray().Select(e => e.GetString()));
        Assert.False(kpiEntry.TryGetProperty("alsoPresent", out _), "sprk_kpiassessment must not require any button to be present.");

        // The subgrid's "+ New" (SecureChild create-then-open) and its platform-New rule are unchanged.
        var ribbon = XDocument.Load(Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_KPIAssessment", "RibbonDiff.xml"));
        var button = ribbon.Descendants("Button").Single(b => (string?)b.Attribute("Id") == "sprk.SecureChild.sprk_kpiassessment.SubGrid.New");
        Assert.Equal("sprk.SecureChild.sprk_kpiassessment.New.Command", (string?)button.Attribute("Command"));
        Assert.Single(ribbon.Descendants("CommandDefinition"), c => (string?)c.Attribute("Id") == "sprk.SecureChild.sprk_kpiassessment.New.Command");
        var native = ribbon.Descendants("CommandDefinition").Single(c => (string?)c.Attribute("Id") == "Mscrm.AddNewRecordFromSubGridStandard");
        Assert.Contains(native.Descendants("EnableRule"),
            r => (string?)r.Attribute("Id") == "sprk.SecureChild.sprk_kpiassessment.NativeNewAllowed.EnableRule");
        // The retired command's library is not loaded by any command of the table.
        Assert.DoesNotContain(ribbon.Descendants("JavaScriptFunction"),
            a => ((string?)a.Attribute("Library") ?? "").Contains("kpi_ribbon_actions", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRetiredButtonCheck_Fires_ForARibbonThatStillCarriesIt_AndStaysQuiet_ForTheEditedRibbon()
    {
        // Must fire: the button as it shipped (its CustomAction, and a command or label that only names it).
        var withButton = ("RibbonDiff.xml", "<CustomAction Id=\"" + RetiredKpiButton + ".CustomAction\"><Button Id=\"" + RetiredKpiButton + "\" /></CustomAction>");
        Assert.Equal(new[] { "RibbonDiff.xml" }, FilesNamingTheRetiredButton(new[] { withButton }));
        Assert.NotEmpty(FilesNamingTheRetiredButton(new[] { ("RibbonDiff.xml", "<CommandDefinition Id=\"" + RetiredKpiButton + ".Command\" />") }));

        // Must not fire: the edited KPI ribbon, and the Add Memo ribbon (a button of the same kind that stays).
        var master = Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        Assert.Empty(FilesNamingTheRetiredButton(new[]
        {
            ("sprk_KPIAssessment", File.ReadAllText(Path.Combine(master, "sprk_KPIAssessment", "RibbonDiff.xml"))),
            ("sprk_Event", File.ReadAllText(Path.Combine(master, "sprk_Event", "RibbonDiff.xml"))),
        }));
    }

    [Fact]
    public void ThePartyHosts_AreExactlyTheIdentityTables_TheOwnershipRuleNeverFollows()
    {
        // A party host keeps the platform "+ New" (nativeNewAllowed answers true without a flag read). That is safe only
        // while the table is NOT an ownership parent — otherwise a child created under it could sit under a secure record.
        Assert.Equal(PartyHosts.OrderBy(t => t, StringComparer.Ordinal), ScriptMapKeys("PARTIES"));
        Assert.All(PartyHosts, party =>
            Assert.DoesNotContain(party, Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.OwnershipParentEntities));
        Assert.Equal(new[] { "sprk_matter", "sprk_project", "sprk_workassignment" }, ScriptMapKeys("ROOTS"));
    }

    [Fact]
    public void TheClientOwnershipChildList_IsTheServersOwnershipParentsWithoutTheRoots()
    {
        // Task 147 r1c: a host control (RegardingResolver) refuses to re-file an ownership child it has no BFF route for,
        // instead of writing it through Xrm.WebApi. Its list must be the server's — a table missing on the client side would
        // be re-filed as the user with no owner change.
        var adapter = File.ReadAllText(Path.Combine(Root, "src", "client", "shared", "Spaarke.UI.Components", "src", "utils",
            "adapters", "bffChildWriteAdapter.ts"));
        var client = Quoted(Regex.Match(adapter, @"OWNERSHIP_CHILD_TABLES[^=]*=\s*new Set\(\[(?<list>[^\]]*)\]\)").Groups["list"].Value);

        var roots = new[] { "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_servicerequest" };
        var server = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.OwnershipParentEntities
            .Where(t => !roots.Contains(t, StringComparer.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(client);
        Assert.Equal(server, client);
    }

    /// <summary>
    /// ebilling-r1 task 096b (EB-D-201; before it 164a-1, uac-r2 Q6): tables the script does NOT serve and whose platform "+ New" is
    /// hidden outright in the SpaarkeMaster source (<see cref="BillingEventRibbonFile"/>), because a new row has one server-side owner
    /// elsewhere and the BFF refuses the table's generic create (<c>RecordTableRule.GenericCreateRefused</c>). An invoice line is
    /// created by extraction, a line decision or Add line in the invoice's review.
    /// </summary>
    private static readonly string[] HiddenOutrightWithNoRibbonCreate = { "sprk_billingevent" };

    [Fact]
    public void EveryServedTable_HasACreateSurface_AndEveryCreateThenOpenTable_IsOnTheBffCreateRoute()
    {
        var script = File.ReadAllText(ScriptFile);
        var wizards = Regex.Matches(
                Regex.Match(script, @"ns\.WIZARDS\s*=\s*\{(?<map>.*?)\n\s*\};", RegexOptions.Singleline).Groups["map"].Value,
                @"(sprk_[a-z]+)\s*:")
            .Select(m => m.Groups[1].Value).ToArray();
        var createThenOpen = Regex.Matches(
                Regex.Match(script, @"ns\.CREATE_THEN_OPEN\s*=\s*\{(?<map>[^;]*)\};").Groups["map"].Value, @"(sprk_[a-z]+)\s*:")
            .Select(m => m.Groups[1].Value).ToArray();
        var ownBranches = new[] { "sprk_document", "sprk_communication" }
            .Where(t => script.Contains($"childTable === \"{t}\"", StringComparison.Ordinal)).ToArray();

        var withoutSurface = ScriptTables().Except(wizards).Except(createThenOpen).Except(ownBranches).ToList();
        Assert.True(withoutSurface.Count == 0, "Served with no create surface: " + string.Join(", ", withoutSurface));

        // The no-create tables are not served, have no create surface here, have their platform "+ New", "New" and "Save & New" hidden
        // outright, and are refused by the BFF's generic create, so the exception cannot outlive its reason.
        var billingEventRibbon = File.ReadAllText(BillingEventRibbonFile);
        Assert.All(HiddenOutrightWithNoRibbonCreate, t =>
        {
            Assert.DoesNotContain(t, ScriptTables());
            Assert.DoesNotContain(t, wizards);
            Assert.DoesNotContain(t, createThenOpen);
            Assert.DoesNotContain(t, Sprk.Bff.Api.Api.ChildRecordEndpoints.CreateTables);
            Assert.True(Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.Find(t) is { GenericCreateRefused: true },
                $"{t} has no ribbon create only while the BFF refuses its generic create.");
            foreach (var location in new[] { "Mscrm.Form.{0}.NewRecord", "Mscrm.Form.{0}.SaveAndNew", "Mscrm.HomepageGrid.{0}.NewRecord",
                         "Mscrm.SubGrid.{0}.AddNewStandard", "Mscrm.SubGrid.{0}.NewRecord" })
            {
                Assert.Contains($"Location=\"{string.Format(location, t)}\"", billingEventRibbon, StringComparison.Ordinal);
            }

            // No create command is left behind in the ribbon (the earlier "New Billing Event" button).
            Assert.DoesNotContain("sprk.SecureChild.", billingEventRibbon, StringComparison.Ordinal);
        });

        // EB-D-201: an invoice is created only by submission, so the route's create set holds neither invoice table, while the invoice
        // keeps its Invoice wizard entry (a submission) and its re-file.
        Assert.DoesNotContain("sprk_invoice", Sprk.Bff.Api.Api.ChildRecordEndpoints.CreateTables);
        Assert.Contains("sprk_invoice", wizards);
        Assert.DoesNotContain("sprk_invoice", createThenOpen);
        Assert.Contains("sprk_invoice", Sprk.Bff.Api.Api.ChildRecordEndpoints.RefileTables);

        var createTables = Quoted(Regex.Match(File.ReadAllText(ChildRecordEndpointsFile),
            @"CreateTables\s*=\s*new HashSet<string>\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?<list>[^}]*)\}").Groups["list"].Value);
        var notOnRoute = createThenOpen.Except(createTables).ToList();
        Assert.True(notOnRoute.Count == 0,
            "The ribbon creates these through POST /api/v1/child-records/{table}, which refuses them: " + string.Join(", ", notOnRoute));

        var stamped = Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.StampedChildTables;
        Assert.All(createThenOpen, t => Assert.Contains(t, stamped));
    }

    /// <summary>
    /// ebilling-r1 task 096b (EB-D-201): no user-reachable path creates an invoice outside the submission operation. Every platform
    /// "New" of the invoice is hidden in the SpaarkeMaster source: the three earlier hides (form New, form Save and New, homepage
    /// grid New) and the two subgrid ones (AddNewStandard, NewRecord), by the exact ids the billing event's hides use.
    /// </summary>
    [Fact]
    public void TheInvoiceRibbon_HidesEveryPlatformNew_FormGridAndSubgrid()
    {
        var hides = XDocument.Load(InvoiceRibbonFile).Descendants("HideCustomAction")
            .ToDictionary(h => (string)h.Attribute("HideActionId")!, h => (string)h.Attribute("Location")!, StringComparer.Ordinal);

        foreach (var (id, location) in new[]
                 {
                     ("sprk.Mscrm.Form.sprk_invoice.NewRecord.Hide", "Mscrm.Form.sprk_invoice.NewRecord"),
                     ("sprk.Mscrm.Form.sprk_invoice.SaveAndNew.Hide", "Mscrm.Form.sprk_invoice.SaveAndNew"),
                     ("sprk.Mscrm.HomepageGrid.sprk_invoice.NewRecord.Hide", "Mscrm.HomepageGrid.sprk_invoice.NewRecord"),
                     ("sprk.Mscrm.SubGrid.sprk_invoice.AddNewStandard.Hide", "Mscrm.SubGrid.sprk_invoice.AddNewStandard"),
                     ("sprk.Mscrm.SubGrid.sprk_invoice.NewRecord.Hide", "Mscrm.SubGrid.sprk_invoice.NewRecord"),
                 })
        {
            Assert.True(hides.TryGetValue(id, out var actual), $"The invoice ribbon does not carry the hide {id}.");
            Assert.Equal(location, actual);
        }
    }

    /// <summary>
    /// ebilling-r1 task 096b (EB-D-201): the Invoice wizard ("New Invoice", a submission) is the one "New" of an invoice subgrid on every
    /// core-record host (<c>ROOTS</c>: matter, project, work assignment), and the platform subgrid "+ New" is closed on every host. Both rules receive the table, the script closes the tables in
    /// <c>NATIVE_NEW_CLOSED</c> before it reads any host flag, and the template and the invoice ribbon pass it.
    /// </summary>
    [Fact]
    public void TheInvoiceWizardCommand_IsAvailableOnEveryCoreRecordHost_AndThePlatformSubgridNew_IsClosedOnEveryHost()
    {
        var script = File.ReadAllText(ScriptFile);
        Assert.Equal(new[] { "sprk_invoice" }, ScriptMapKeys("NATIVE_NEW_CLOSED"));
        Assert.Contains("sprk_invoice", ScriptTables());

        string Body(string name) => Regex.Match(script, $@"ns\.{name}\s*=\s*function[^{{]*\{{(?<body>.*?)\n    \}};", RegexOptions.Singleline)
            .Groups["body"].Value;
        Assert.Matches(@"function \(primaryControl, table\)", Regex.Match(script, @"ns\.nativeNewAllowed\s*=\s*function[^{]*").Value);
        Assert.Matches(@"function \(primaryControl, table\)", Regex.Match(script, @"ns\.newChildAvailable\s*=\s*function[^{]*").Value);
        // The closed table is answered before the flag is read: no host (secure, not secure, unreadable, a party) re-opens it.
        // Both indexes are asserted present first: IndexOf is -1 when a guard is absent, and -1 < n would pass vacuously.
        foreach (var function in new[] { "nativeNewAllowed", "newChildAvailable" })
        {
            var body = Body(function);
            var guard = body.IndexOf("NATIVE_NEW_CLOSED", StringComparison.Ordinal);
            var flagRead = body.IndexOf("hostSecureFlag", StringComparison.Ordinal);
            Assert.True(guard >= 0, $"{function} does not consult NATIVE_NEW_CLOSED.");
            Assert.True(flagRead >= 0, $"{function} does not read the host flag.");
            Assert.True(guard < flagRead, $"{function} reads the host flag before it answers for a NATIVE_NEW_CLOSED table.");
        }
        // The wizard is offered only on a core-record host (the records the submission route accepts), never on any saved host.
        Assert.Contains("ns.ROOTS[host.entity]", Body("newChildAvailable"));

        // The table reaches both rules: the template for every entity ({{entity}}), the invoice ribbon for sprk_invoice.
        foreach (var (file, value) in new[] { (TemplateFile, "{{entity}}"), (InvoiceRibbonFile, "sprk_invoice") })
        {
            var rules = XDocument.Load(file).Descendants("CustomRule").ToList();
            foreach (var function in new[] { "nativeNewAllowed", "newChildAvailable" })
            {
                var rule = rules.Single(r => (string?)r.Attribute("FunctionName") == $"Spaarke.SecureChild.Ribbon.{function}");
                Assert.Equal(new[] { "PrimaryControl" }, rule.Elements("CrmParameter").Select(p => (string)p.Attribute("Value")!));
                Assert.Equal(new[] { value }, rule.Elements("StringParameter").Select(p => (string)p.Attribute("Value")!));
            }
        }

        // The invoice's "New Invoice" command opens the wizard and is enabled by the table-aware rule alone.
        var invoiceRibbon = XDocument.Load(InvoiceRibbonFile);
        var command = invoiceRibbon.Descendants("CommandDefinition").Single(c => (string?)c.Attribute("Id") == "sprk.SecureChild.sprk_invoice.New.Command");
        Assert.Equal(new[] { "sprk.SecureChild.sprk_invoice.NewAvailable.EnableRule" }, command.Descendants("EnableRule").Select(r => (string)r.Attribute("Id")!));
        Assert.Contains(command.Descendants("StringParameter"), p => (string?)p.Attribute("Value") == "sprk_invoice");
    }

    /// <summary>
    /// field-mapping-server-write-path-r1 task 051 r1 (F1/F4): the script answers for the redirected tables (NATIVE_NEW_REDIRECTED) only
    /// when a rule hands it the table, as a StringParameter. A rule that names no table gives the script <c>table === undefined</c>, so
    /// the redirect is never consulted and the platform subgrid "+ New" falls through to a create with no field mapping or owner rule.
    /// The table-less rules shipped once (seven tables), so every <c>sprk.SecureChild.&lt;t&gt;.*</c> rule must carry its own table.
    /// </summary>
    private static string[] SecureChildRulesWithoutTheirTable(XDocument ribbon) =>
        ribbon.Descendants("EnableRule")
            .Select(rule => (Rule: rule, Match: Regex.Match((string?)rule.Attribute("Id") ?? "", @"^sprk\.SecureChild\.(?<t>sprk_[a-z]+)\.(NativeNewAllowed|NewAvailable)\.EnableRule$")))
            .Where(r => r.Match.Success)
            .SelectMany(r => r.Rule.Elements("CustomRule")
                .Where(c => ((string?)c.Attribute("FunctionName") ?? "").StartsWith("Spaarke.SecureChild.Ribbon.", StringComparison.Ordinal))
                .Where(c => !c.Elements("StringParameter").Any(p => (string?)p.Attribute("Value") == r.Match.Groups["t"].Value))
                .Select(_ => (string)r.Rule.Attribute("Id")!))
            .ToArray();

    [Fact]
    public void EverySecureChildRule_InTheSpaarkeMasterRibbons_PassesItsOwnTableToTheScript()
    {
        var entities = Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        var withRules = Directory.GetFiles(entities, "RibbonDiff.xml", SearchOption.AllDirectories)
            .Select(file => (File: file, Doc: XDocument.Load(file)))
            .Where(x => x.Doc.Descendants("EnableRule").Any(r => ((string?)r.Attribute("Id") ?? "").StartsWith("sprk.SecureChild.", StringComparison.Ordinal)))
            .ToList();

        // Guard against a scan that matches nothing: every served table's ribbon is among the files read.
        var tablesCovered = withRules
            .SelectMany(x => x.Doc.Descendants("EnableRule"))
            .Select(r => Regex.Match((string?)r.Attribute("Id") ?? "", @"^sprk\.SecureChild\.(?<t>sprk_[a-z]+)\.").Groups["t"].Value)
            .Where(t => t.Length > 0).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(ScriptTables(), tablesCovered);

        var missing = withRules.SelectMany(x => SecureChildRulesWithoutTheirTable(x.Doc).Select(id => $"{Path.GetFileName(Path.GetDirectoryName(x.File))}: {id}")).ToList();
        Assert.True(missing.Count == 0, "A secure-child rule passes no table (the script then never consults NATIVE_NEW_REDIRECTED): " + string.Join(", ", missing));
    }

    [Fact]
    public void TheTableCheck_Fires_ForARuleWithNoTable_OrAnotherTable_AndStaysQuiet_ForOneWithItsOwn()
    {
        static XDocument Ribbon(string parameters) => XDocument.Parse(
            "<RibbonDiffXml><RuleDefinitions><EnableRules>"
            + "<EnableRule Id=\"sprk.SecureChild.sprk_todo.NativeNewAllowed.EnableRule\">"
            + "<CustomRule Library=\"$webresource:sprk_/scripts/secure_child_ribbon.js\" FunctionName=\"Spaarke.SecureChild.Ribbon.nativeNewAllowed\" Default=\"false\">"
            + "<CrmParameter Value=\"PrimaryControl\" />" + parameters + "</CustomRule></EnableRule>"
            + "</EnableRules></RuleDefinitions></RibbonDiffXml>");

        // Must fire: the shipped defect (no StringParameter), and a copy-paste of another table's rule.
        Assert.Equal(new[] { "sprk.SecureChild.sprk_todo.NativeNewAllowed.EnableRule" }, SecureChildRulesWithoutTheirTable(Ribbon("")));
        Assert.NotEmpty(SecureChildRulesWithoutTheirTable(Ribbon("<StringParameter Value=\"sprk_event\" />")));
        // Must not fire: the rule names its own table.
        Assert.Empty(SecureChildRulesWithoutTheirTable(Ribbon("<StringParameter Value=\"sprk_todo\" />")));
        // Must not fire: a rule outside the secure-child family needs no table.
        Assert.Empty(SecureChildRulesWithoutTheirTable(XDocument.Parse(
            "<RibbonDiffXml><RuleDefinitions><EnableRules><EnableRule Id=\"sprk.Other.EnableRule\"><CustomRule FunctionName=\"x\" /></EnableRule></EnableRules></RuleDefinitions></RibbonDiffXml>")));
    }

    /// <summary>
    /// field-mapping-server-write-path-r1 task 051 r1 (F2/F4): the libraries a ribbon command loads must be web resources that exist
    /// UNDER THAT NAME. "Add Memo" and the (since retired) "+ Add KPI" shipped naming <c>sprk_event_ribbon_commands</c> and
    /// <c>sprk_/scripts/kpi_ribbon_actions.js</c>; the web resource is <c>sprk_event_ribbon_commands.js</c>, so the button would load nothing. Returns the libraries of a command's actions that name no
    /// known web resource.
    /// </summary>
    private static string[] LibrariesNamingNoWebResource(XElement command, ISet<string> webResourceNames) =>
        command.Descendants("JavaScriptFunction")
            .Select(a => ((string?)a.Attribute("Library") ?? "").Replace("$webresource:", "", StringComparison.OrdinalIgnoreCase))
            .Where(name => !webResourceNames.Contains(name))
            .ToArray();

    private static string[] LibrariesOf(XElement command) =>
        command.Descendants("JavaScriptFunction")
            .Select(a => ((string?)a.Attribute("Library") ?? "").Replace("$webresource:", "", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    [Fact]
    public void AddMemo_InTheSpaarkeMasterRibbon_LoadsItsHelpersInOrder_UnderTheWebResourceNamesThatExist()
    {
        var master = Path.Combine(Root, "src", "dataverse", "solutions", "SpaarkeMaster");
        var names = Directory.GetFiles(Path.Combine(master, "WebResources"), "*.data.xml", SearchOption.AllDirectories)
            .Select(file => (string?)XDocument.Load(file).Root?.Element("Name"))
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("sprk_event_ribbon_commands.js", names);

        var helpers = new[] { "sprk_/scripts/bff_auth.js", "sprk_/scripts/assignedaccess_postsave.js", "sprk_/scripts/secure_child_ribbon.js" };
        foreach (var (folder, commandId, code, rule) in new[]
                 {
                     ("sprk_Event", "Spaarke.Event.AddMemo.Command", "sprk_event_ribbon_commands.js", "sprk.CreatePrivilege.sprk_memo.DisplayRule"),
                 })
        {
            var ribbon = XDocument.Load(Path.Combine(master, "Entities", folder, "RibbonDiff.xml"));
            var command = ribbon.Descendants("CommandDefinition").Single(c => (string?)c.Attribute("Id") == commandId);
            Assert.Equal(helpers.Append(code).ToArray(), LibrariesOf(command));
            Assert.Empty(LibrariesNamingNoWebResource(command, names));
            Assert.Contains(command.Descendants("DisplayRule"), r => (string?)r.Attribute("Id") == rule);
            // Add Memo creates through the BFF: no native-New rule may close the button.
            Assert.DoesNotContain(command.Descendants("EnableRule"), r => ((string?)r.Attribute("Id") ?? "").Contains("NativeNewAllowed", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheWebResourceNameCheck_Fires_ForAnUnknownLibraryName_AndStaysQuiet_ForKnownOnes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "sprk_event_ribbon_commands.js", "sprk_/scripts/bff_auth.js" };
        static XElement Command(string library) => XElement.Parse(
            "<CommandDefinition Id=\"x\"><Actions><JavaScriptFunction Library=\"" + library + "\" FunctionName=\"f\" /></Actions></CommandDefinition>");

        // Must fire: the shipped names (no .js; a path the web resource never had).
        Assert.Equal(new[] { "sprk_event_ribbon_commands" }, LibrariesNamingNoWebResource(Command("$webresource:sprk_event_ribbon_commands"), names));
        Assert.Equal(new[] { "sprk_/scripts/kpi_ribbon_actions.js" }, LibrariesNamingNoWebResource(Command("$webresource:sprk_/scripts/kpi_ribbon_actions.js"), names));
        // Must not fire: the names as they are.
        Assert.Empty(LibrariesNamingNoWebResource(Command("$webresource:sprk_event_ribbon_commands.js"), names));
        Assert.Empty(LibrariesNamingNoWebResource(Command("$webresource:sprk_/scripts/bff_auth.js"), names));
    }

    [Fact]
    public void TheWebResource_AndItsSpaarkeMasterCopy_AreIdentical()
    {
        Assert.Equal(File.ReadAllText(ScriptFile).ReplaceLineEndings(), File.ReadAllText(ScriptMasterCopyFile).ReplaceLineEndings());
    }

    [Fact]
    public void ThePlatformNew_CarriesTheSecureRule_AndTheTwoRules_CallTheComplementaryFunctions()
    {
        var template = XDocument.Load(TemplateFile);
        var native = template.Descendants("CommandDefinition").Single(c => (string?)c.Attribute("Id") == "Mscrm.AddNewRecordFromSubGridStandard");
        Assert.Contains(native.Descendants("EnableRule"),
            r => (string?)r.Attribute("Id") == "sprk.SecureChild.{{entity}}.NativeNewAllowed.EnableRule");

        string FunctionOf(string ruleId) => template.Descendants("EnableRule")
            .Single(r => (string?)r.Attribute("Id") == ruleId && r.Elements("CustomRule").Any())
            .Elements("CustomRule").Last().Attribute("FunctionName")!.Value;

        Assert.Equal("Spaarke.SecureChild.Ribbon.nativeNewAllowed", FunctionOf("sprk.SecureChild.{{entity}}.NativeNewAllowed.EnableRule"));
        Assert.Equal("Spaarke.SecureChild.Ribbon.newChildAvailable", FunctionOf("sprk.SecureChild.{{entity}}.NewAvailable.EnableRule"));
    }

    [Fact]
    public void ThePlatformNew_IsAllowedOnlyForAHostReadAsNotSecure_FailClosed()
    {
        var script = File.ReadAllText(ScriptFile);
        string Body(string name) => Regex.Match(script, $@"ns\.{name}\s*=\s*function[^{{]*\{{(?<body>.*?)\n    \}};", RegexOptions.Singleline)
            .Groups["body"].Value;

        // Exactly "read and false" allows the platform New; null (unreadable / empty / not a root) and true do not.
        Assert.Contains("flag === false", Body("nativeNewAllowed"), StringComparison.Ordinal);
        Assert.Contains("flag !== false", Body("newChildAvailable"), StringComparison.Ordinal);
        // An unreadable flag resolves null (never rejects into the rule's Default) and is not cached.
        Assert.Contains("return null;", Body("hostSecureFlag"), StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        // Owner round 28 item 2: the rules read sprk_issecure THROUGH Xrm.WebApi (the stored value) — never the form's
        // in-memory attribute, which Make Secure / Remove Secure may not have refreshed yet.
        Assert.Contains("Xrm.WebApi.retrieveRecord(host.entity, host.id, \"?$select=sprk_issecure\")", Body("hostSecureFlag"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("getAttribute", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Task 147 r1c-v1 (verifier item 5): every pac call of the deploy script that reaches an environment names
    /// <c>-EnvironmentUrl</c> explicitly. Without <c>--environment</c>, pac acts on its ACTIVE auth profile's environment,
    /// which need not be the one the script's Web API writes (the web resource) and checks went to — the ribbon would land
    /// in another environment than its script. <c>pac solution pack</c> is local (no environment) and exempt.
    /// </summary>
    [Fact]
    public void TheDeployScriptsPacCalls_ThatReachAnEnvironment_NameEnvironmentUrlExplicitly()
    {
        var pacCalls = File.ReadAllLines(DeployFile)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("pac ", StringComparison.Ordinal))
            .ToList();
        var local = new[] { "pac solution pack " };

        var reaching = pacCalls.Where(c => !local.Any(p => c.StartsWith(p, StringComparison.Ordinal))).ToList();

        // Task 130 (D-83): the import goes through the shared scoped-import function, which runs pac with
        // --environment <EnvironmentUrl> itself and then publishes only the solution's components. The script must name
        // -EnvironmentUrl on that call, and must not run a bare `pac ... import` of its own.
        Assert.Empty(reaching);
        var script = File.ReadAllText(DeployFile);
        Assert.Matches(@"Invoke-ScopedSolutionImport\s+-EnvironmentUrl\s+\$EnvironmentUrl\s", script);
        var module = File.ReadAllText(Path.Combine(Root, "scripts", "lib", "Publish-SolutionComponents.ps1"));
        Assert.Matches(@"solution import --environment \$EnvironmentUrl\s", module);
    }
}
