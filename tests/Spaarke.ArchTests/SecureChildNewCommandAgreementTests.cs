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
/// <para><b>The inventory.</b> <see cref="RootFormSecureChildSubgrids"/> is the read-only live inventory of spaarkedev1's
/// project, matter and work-assignment main forms (2026-10-04), filtered to the secure-child tables. A new subgrid of a
/// secure-child table on a root form is added here, and then served (or its native "+ New" hidden some other way).</para>
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

    /// <summary>Secure-child tables with a subgrid on a root main form (live inventory, spaarkedev1, 2026-10-04).</summary>
    private static readonly string[] RootFormSecureChildSubgrids =
    {
        "sprk_todo", "sprk_event", "sprk_document", "sprk_invoice", "sprk_analysis", "sprk_budget", "sprk_communication",
        "sprk_reportcard",
    };

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
    public void EverySecureChildSubgridOnARootForm_IsServed_OrItsPlatformNewIsHiddenOutright()
    {
        var served = ScriptTables();
        var analysisHidesNative = File.ReadAllText(AnalysisRibbonFile)
            .Contains("Location=\"Mscrm.SubGrid.sprk_analysis.AddNewStandard\"", StringComparison.Ordinal);

        var uncovered = RootFormSecureChildSubgrids
            .Where(t => !served.Contains(t) && !(t == "sprk_analysis" && analysisHidesNative))
            .ToList();

        Assert.True(uncovered.Count == 0,
            "A secure-child subgrid on a root form keeps the platform '+ New' (a user-owned create under a secure record): "
            + string.Join(", ", uncovered));
    }

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

        var createTables = Quoted(Regex.Match(File.ReadAllText(ChildRecordEndpointsFile),
            @"CreateTables\s*=\s*new HashSet<string>\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?<list>[^}]*)\}").Groups["list"].Value);
        var notOnRoute = createThenOpen.Except(createTables).ToList();
        Assert.True(notOnRoute.Count == 0,
            "The ribbon creates these through POST /api/v1/child-records/{table}, which refuses them: " + string.Join(", ", notOnRoute));

        var stamped = Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.StampedChildTables;
        Assert.All(createThenOpen, t => Assert.Contains(t, stamped));
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
    }
}
