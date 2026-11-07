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

    [Fact]
    public void SpaarkesOwnAddKpiQuickCreate_CarriesTheSecureRule_InTheAuthoredRibbon_AndTheMergeScript()
    {
        // "+ Add KPI" opens the KPI assessment QUICK CREATE with the matter prefilled — the "quick create with the parent
        // prefilled" round 28 item 2 names. Under a secure host only the BFF command may show.
        var kpi = XDocument.Load(Path.Combine(Root, "src", "solutions", "SpaarkeCore", "entities", "sprk_matter", "RibbonDiff",
            "add-kpi-ribbon.xml"));
        const string rule = "sprk.SecureChild.sprk_kpiassessment.NativeNewAllowed.EnableRule";
        var command = kpi.Descendants("CommandDefinition").Single(c => (string?)c.Attribute("Id") == "sprk.matter.subgrid.kpi.AddKpiButton.Command");
        Assert.Contains(command.Descendants("EnableRule"), r => (string?)r.Attribute("Id") == rule);
        var definition = kpi.Descendants("EnableRule").Single(r => (string?)r.Attribute("Id") == rule && r.Elements("CustomRule").Any());
        Assert.Equal("Spaarke.SecureChild.Ribbon.nativeNewAllowed", definition.Element("CustomRule")!.Attribute("FunctionName")!.Value);

        var merge = File.ReadAllText(MergeFile);
        Assert.Contains("'sprk.matter.subgrid.kpi.AddKpiButton.Command'", merge, StringComparison.Ordinal);

        // The script's own check: the Quick Create opens only for a record read as NOT secure.
        var actions = File.ReadAllText(Path.Combine(Root, "src", "solutions", "webresources", "sprk_kpi_ribbon_actions.js"));
        Assert.Contains("row.sprk_issecure === false", actions, StringComparison.Ordinal);
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
    /// host, and the platform subgrid "+ New" is closed on every host. Both rules receive the table, the script closes the tables in
    /// <c>NATIVE_NEW_CLOSED</c> before it reads any host flag, and the template and the invoice ribbon pass it.
    /// </summary>
    [Fact]
    public void TheInvoiceWizardCommand_IsAvailableOnEveryHost_AndThePlatformSubgridNew_IsClosedOnEveryHost()
    {
        var script = File.ReadAllText(ScriptFile);
        Assert.Equal(new[] { "sprk_invoice" }, ScriptMapKeys("NATIVE_NEW_CLOSED"));
        Assert.Contains("sprk_invoice", ScriptTables());

        string Body(string name) => Regex.Match(script, $@"ns\.{name}\s*=\s*function[^{{]*\{{(?<body>.*?)\n    \}};", RegexOptions.Singleline)
            .Groups["body"].Value;
        Assert.Matches(@"function \(primaryControl, table\)", Regex.Match(script, @"ns\.nativeNewAllowed\s*=\s*function[^{]*").Value);
        Assert.Matches(@"function \(primaryControl, table\)", Regex.Match(script, @"ns\.newChildAvailable\s*=\s*function[^{]*").Value);
        // The closed table is answered before the flag is read: no host (secure, not secure, unreadable, a party) re-opens it.
        Assert.True(Body("nativeNewAllowed").IndexOf("NATIVE_NEW_CLOSED", StringComparison.Ordinal)
            < Body("nativeNewAllowed").IndexOf("hostSecureFlag", StringComparison.Ordinal));
        Assert.True(Body("newChildAvailable").IndexOf("NATIVE_NEW_CLOSED", StringComparison.Ordinal)
            < Body("newChildAvailable").IndexOf("hostSecureFlag", StringComparison.Ordinal));

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
