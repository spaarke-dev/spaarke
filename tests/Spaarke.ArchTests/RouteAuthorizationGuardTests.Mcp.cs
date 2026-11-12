using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE MCP CREDIT (spaarkeai-mcp-bff-api-r1 task 021; design §8 B-7, B-8; spec FR-01, FR-02, FR-07; ADR-028 Amendment A10).
///
/// <para>An MCP route (<c>app.MapMcp("/mcp/{area}")</c>) has no handler of its own and takes no record id: the SDK's transport
/// dispatches a JSON-RPC call to a TOOL, and the tool decides what is read, as the caller. So the route cannot earn Rule A's
/// per-resource credit, and a waiver would describe it wrongly. It is credited instead by two ledgers, judged together:</para>
/// <list type="number">
/// <item><b><see cref="McpRoutes"/></b> — the route is listed, is a <c>MapMcp</c> registration, is not anonymous, carries
/// <c>.RequireAuthorization()</c> with NO named policy (the default workforce scheme: never <c>ExternalCollaboration</c>) and
/// <c>.RequireMcpConnectorToken()</c> (the A10 token rule), and has no waiver.</item>
/// <item><b><see cref="McpTools"/></b> — every class tagged <c>[McpArea]</c> in the BFF is listed, and each carries
/// <c>[Authorize]</c> on the class with exactly the ledger's policy (none = the default policy), names no authentication scheme,
/// no forbidden policy and no <c>[AllowAnonymous]</c>; sets <c>ReadOnly = true|false</c> and <c>Title =</c> explicitly; injects
/// no type declared under <c>Services/Ai/**</c> outside <c>PublicContracts</c> (ADR-013); and names its declared caller seam.
/// No <c>[McpServerTool]</c> attribute or SDK assembly scan exists anywhere (it would bypass the catalog and its rules).</item>
/// </list>
/// <para>The route counts as declared for Rule A only while BOTH ledgers are clean (<see cref="McpCreditedRoutes"/>). A tool that
/// breaks its rule therefore turns the route itself red.</para>
/// <para><b>Maintenance.</b> Adding a tool: add its class to <see cref="McpTools"/> with its policy (an EXISTING workforce policy
/// or "" for the default), the seam it calls as the caller (e.g. <c>IDecisionCapability</c>) and a reason of 60+ characters.
/// Adding an area: add its route to <see cref="McpRoutes"/> with the same chain. Never credit an MCP route with a waiver.
/// <b>Limits, stated:</b> the checks read TEXT. A seam named but not called passes; what each tool does with it is the
/// reviewer's to read, and the A10 suite (<c>tests/integration/auth/Mcp/McpAuthTests.cs</c>) proves the runtime behaviour.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private sealed record McpRoute(string Route, string Area, string Reason);

    /// <summary>One MCP tool. <paramref name="Policy"/> "" means bare <c>[Authorize]</c> (the default policy).</summary>
    private sealed record McpTool(string Class, string Policy, string Seam, string Reason);

    /// <summary>The policies an MCP tool may never name (FR-07): each authenticates a scheme other than the A10 workforce one.</summary>
    private static readonly IReadOnlySet<string> McpForbiddenPolicies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ExternalCollaboration", "CiamExternal", "RagApiKey",
    };

    private static readonly IReadOnlyList<McpRoute> McpRoutes = new[]
    {
        new McpRoute("POST /mcp/legal-operations", "legal-operations",
            "ADR-028 A10 (spaarkeai-mcp-bff-api-r1 task 021, FR-01/FR-02): the stateless MCP endpoint of the legal-operations area. "
            + "RequireAuthorization() on the default workforce scheme, then RequireMcpConnectorToken (McpToolAuthorization.cs: Spaarke "
            + "tenant, exact v2 issuer, aud = the BFF app, user_impersonation, a user; CIAM and A9 home tokens refused with 401 before any "
            + "tool runs). It takes no record id; each tool reads as the caller through the seam McpTools names."),
    };

    // No production tool yet: tasks 022-024 add spaarke_show_decisions, spaarke_get_decision_plan, spaarke_search_decision_lookup and
    // spaarke_complete_decision, each with its entry here, in the diff that adds the tool.
    private static readonly IReadOnlyList<McpTool> McpTools = Array.Empty<McpTool>();

    private static readonly Regex McpToolClass = new(
        @"(?<attrs>(?:\[[^\[\]]*\]\s*)+)(?:(?:public|internal|private|protected|sealed|static|partial|file)\s+)*class\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled);

    private static readonly Regex McpAuthorizeAttribute = new(@"\bAuthorize\s*(?:\((?<args>[^)]*)\))?\s*[\],]", RegexOptions.Compiled);

    private static readonly Regex McpSdkToolRegistration = new(@"\[\s*McpServerTool\b|\.\s*WithTools(?:FromAssembly)?\s*[<(]", RegexOptions.Compiled);

    /// <summary>The simple names of every type declared under the BFF's <c>Services/Ai/**</c>, <c>PublicContracts</c> excepted (ADR-013).</summary>
    private static IReadOnlySet<string> AiInternalTypeNames(IEnumerable<SourceUnit> bffUnits) =>
        bffUnits.Where(u => u.Path.StartsWith("Services/Ai/", StringComparison.Ordinal)
                            && !u.Path.StartsWith("Services/Ai/PublicContracts/", StringComparison.Ordinal))
            .SelectMany(u => u.Types.Select(t => t.Name))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every violation of the MCP tool rule over <paramref name="bffUnits"/> (paths relative to the BFF root).</summary>
    private static List<string> McpToolViolations(IReadOnlyList<SourceUnit> bffUnits, IReadOnlyList<McpTool> ledger, IReadOnlySet<string> aiTypeNames)
    {
        var violations = new List<string>();
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var unit in bffUnits)
        {
            if (McpSdkToolRegistration.Match(unit.Code) is { Success: true } sdk)
            {
                violations.Add($"{unit.Path}:{unit.LineOf(sdk.Index)}: an SDK tool attribute or assembly scan ([McpServerTool] / WithTools...) bypasses "
                               + "McpToolCatalog and its per-tool rule; build the tool as an [McpArea] McpToolDefinition instead.");
            }

            foreach (Match m in McpToolClass.Matches(unit.Code))
            {
                var attrsCode = m.Groups["attrs"].Value;
                if (!Regex.IsMatch(attrsCode, @"\bMcpArea\s*\("))
                {
                    continue;
                }

                var name = m.Groups["name"].Value;
                var at = $"{unit.Path}:{unit.LineOf(m.Index)} {name}";
                found.Add(name);
                var attrsText = unit.Text.Substring(m.Groups["attrs"].Index, m.Groups["attrs"].Length);
                var decl = unit.Types.FirstOrDefault(t => t.Name == name && t.BodyStart > m.Index);
                var body = decl is null ? string.Empty : unit.Code[decl.BodyStart..decl.BodyEnd];

                var entry = ledger.FirstOrDefault(t => t.Class == name);
                if (entry is null)
                {
                    violations.Add($"{at}: an MCP tool missing from McpTools — add it with its policy, caller seam and reason.");
                    continue;
                }

                var authorize = McpAuthorizeAttribute.Matches(attrsCode).ToList();
                if (authorize.Count != 1)
                {
                    violations.Add($"{at}: needs exactly one [Authorize] on the tool class (B-7); found {authorize.Count}.");
                }
                else
                {
                    var argsText = attrsText.Substring(authorize[0].Groups["args"].Index, authorize[0].Groups["args"].Length);
                    var policy = Regex.Match(argsText, @"^\s*""(?<p>[^""]*)""|Policy\s*=\s*""(?<p>[^""]*)""") is { Success: true } p
                        ? p.Groups["p"].Value
                        : string.Empty;
                    if (Regex.IsMatch(argsText, @"\bAuthenticationSchemes\b"))
                    {
                        violations.Add($"{at}: [Authorize] names an authentication scheme; MCP tools run on the A10 workforce scheme only.");
                    }

                    if (McpForbiddenPolicies.Contains(policy) || McpForbiddenPolicies.Any(f => ContainsToken(argsText, f)))
                    {
                        violations.Add($"{at}: [Authorize] names a forbidden policy (ExternalCollaboration, CiamExternal, RagApiKey; FR-07).");
                    }
                    else if (policy.Length == 0 && argsText.Trim().Length > 0 && !Regex.IsMatch(argsText, @"\bPolicy\b|^\s*"""))
                    {
                        violations.Add($"{at}: [Authorize({argsText.Trim()})] is not a policy the guard can read; name it as a literal.");
                    }
                    else if (!string.Equals(policy, entry.Policy, StringComparison.Ordinal))
                    {
                        violations.Add($"{at}: [Authorize] policy \"{policy}\" is not the ledger's \"{entry.Policy}\".");
                    }
                }

                if (ContainsToken(attrsCode, "AllowAnonymous") || ContainsToken(body, "AllowAnonymous"))
                {
                    violations.Add($"{at}: [AllowAnonymous] on an MCP tool (FR-02).");
                }

                if (!Regex.IsMatch(body, @"\bReadOnly\s*=\s*(?:true|false)\b"))
                {
                    violations.Add($"{at}: does not set ReadOnly = true|false explicitly (FR-03, B-8).");
                }

                if (!Regex.IsMatch(body, @"\bTitle\s*="))
                {
                    violations.Add($"{at}: does not set a Title (FR-03, B-8).");
                }

                foreach (var aiType in aiTypeNames.Where(t => ContainsToken(body, t)).OrderBy(t => t, StringComparer.Ordinal))
                {
                    violations.Add($"{at}: uses {aiType}, an AI-internal type (Services/Ai/**); go through Services/Ai/PublicContracts (ADR-013).");
                }

                if (!ContainsToken(body, entry.Seam))
                {
                    violations.Add($"{at}: never names its ledger seam {entry.Seam}.");
                }
            }
        }

        foreach (var entry in ledger)
        {
            if (!found.Contains(entry.Class))
            {
                violations.Add($"McpTools {entry.Class}: no [McpArea] class of that name — delete the stale entry.");
            }

            if (entry.Reason.Trim().Length < 60)
            {
                violations.Add($"McpTools {entry.Class}: the reason is under 60 characters.");
            }

            if (McpForbiddenPolicies.Contains(entry.Policy))
            {
                violations.Add($"McpTools {entry.Class}: the ledger names a forbidden policy (FR-07).");
            }
        }

        return violations;
    }

    /// <summary>Null when <paramref name="assessment"/> earns the MCP credit under <paramref name="ledger"/>; otherwise why not.</summary>
    private static string? McpRouteCreditProblem(Assessment assessment, IReadOnlyList<McpRoute> ledger, int toolViolations, IReadOnlyList<Waiver> waivers)
    {
        var route = assessment.Route;
        var entry = ledger.FirstOrDefault(r => r.Route == route.Key);
        if (entry is null)
        {
            return "not in McpRoutes";
        }

        var line = route.Unit is null ? string.Empty : route.Unit.Code.Split('\n').ElementAtOrDefault(route.Line - 1) ?? string.Empty;
        if (!Regex.IsMatch(line, @"\.\s*MapMcp\s*\("))
        {
            return "not a MapMcp registration";
        }

        if (route.Anonymous)
        {
            return "anonymous";
        }

        if (!route.Forms.Contains("RequireAuthorization()"))
        {
            return "no .RequireAuthorization() (the default workforce scheme)";
        }

        if (route.Forms.Any(f => NamedPolicyForm.IsMatch(f)))
        {
            return "a named policy on the route (MCP routes use the default workforce policy only; never ExternalCollaboration, FR-07)";
        }

        if (!route.Forms.Contains("RequireMcpConnectorToken"))
        {
            return "no .RequireMcpConnectorToken() (the ADR-028 A10 token rule)";
        }

        if (waivers.Any(w => w.Route == route.Key))
        {
            return "it also carries a waiver; an MCP route is declared by McpRoutes alone";
        }

        if (entry.Reason.Trim().Length < 60 || !entry.Reason.Contains("A10", StringComparison.Ordinal))
        {
            return "the McpRoutes reason must be 60+ characters and cite ADR-028 A10";
        }

        return toolViolations == 0 ? null : $"{toolViolations} MCP tool violation(s) (McpToolViolations)";
    }

    private static readonly Lazy<IReadOnlyList<string>> RealMcpToolViolations = new(() =>
        McpToolViolations(Real.Set.Units, McpTools, AiInternalTypeNames(Real.Set.Units)), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The live routes the MCP credit holds for — what Rule A counts as declared.</summary>
    private static readonly Lazy<IReadOnlySet<string>> McpCreditedRoutes = new(() =>
        RealAssessments.Value
            .Where(a => McpRouteCreditProblem(a, McpRoutes, RealMcpToolViolations.Value.Count, Waivers) is null)
            .Select(a => a.Key)
            .ToHashSet(StringComparer.Ordinal), LazyThreadSafetyMode.ExecutionAndPublication);

    // ── the real ledgers ────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "spaarkeai-mcp-bff-api-r1 task 021: every MCP route and tool is in its ledger, and the MCP credit holds for every listed route")]
    public void EveryMcpRouteAndToolIsDeclared()
    {
        var violations = RealMcpToolViolations.Value.ToList();
        foreach (var entry in McpRoutes)
        {
            var live = RealAssessments.Value.Where(a => a.Key == entry.Route).ToList();
            if (live.Count != 1)
            {
                violations.Add($"McpRoutes {entry.Route}: {live.Count} live registrations (expected exactly 1).");
                continue;
            }

            if (McpRouteCreditProblem(live[0], McpRoutes, RealMcpToolViolations.Value.Count, Waivers) is { } why)
            {
                violations.Add($"McpRoutes {entry.Route} at {live[0].Route.File}:{live[0].Route.Line}: the MCP credit does not hold — {why}.");
            }
        }

        Assert.True(violations.Count == 0, "MCP routes and tools (B-7, B-8, FR-07):\n" + string.Join("\n", violations));
        Assert.Equal(McpRoutes.Select(r => r.Route).OrderBy(r => r, StringComparer.Ordinal), McpCreditedRoutes.Value.OrderBy(r => r, StringComparer.Ordinal));
        Assert.DoesNotContain(Waivers, w => w.Route.StartsWith("POST /mcp/", StringComparison.Ordinal));
    }

    // ── controls ──────────────────────────────────────────────────────────────────────────────────────────────

    private const string McpRouteFixtureKey = "POST /mcp/zz-fixture";

    private static readonly IReadOnlyList<McpRoute> McpRouteFixtureLedger = new[]
    {
        new McpRoute(McpRouteFixtureKey, "zz-fixture",
            "Seeded by the MCP credit controls: ADR-028 A10 fixture route, RequireAuthorization() then RequireMcpConnectorToken."),
    };

    private static Assessment McpFixtureRoute(string chain)
    {
        var routes = ScanFixtures(new[]
        {
            ("Api/Mcp/FakeMcpEndpoints.cs",
                "public static class FakeMcpEndpoints\n{\n    public const string Route = \"/mcp/zz-fixture\";\n\n"
                + "    public static void MapFakeMcp(this WebApplication app)\n    {\n"
                + "        app.MapMcp(Route)" + chain + ";\n    }\n}"),
            ("Program.cs", "var app = builder.Build();\napp.MapFakeMcp();"),
        }, "Api/Mcp/FakeMcpEndpoints.cs");
        var route = Assert.Single(routes);
        Assert.False(route.Unparseable, route.Problem);
        return Assess(route, Array.Empty<HandlerDecision>());
    }

    [Fact(DisplayName = "task 021 controls: the MCP route credit holds only for MapMcp + RequireAuthorization() + RequireMcpConnectorToken, no named policy, no waiver, clean tools")]
    public void McpRouteCredit_Controls()
    {
        const string sanctioned = ".RequireAuthorization().RequireMcpConnectorToken().AddEndpointFilter(DisableResponseBufferingAsync)";

        // MUST NOT FIRE: the real chain shape, listed, clean tools, no waiver.
        Assert.Null(McpRouteCreditProblem(McpFixtureRoute(sanctioned), McpRouteFixtureLedger, 0, Array.Empty<Waiver>()));

        // MUST FIRE, one cause each.
        Assert.Equal("not in McpRoutes", McpRouteCreditProblem(McpFixtureRoute(sanctioned), Array.Empty<McpRoute>(), 0, Array.Empty<Waiver>()));
        Assert.StartsWith("no .RequireAuthorization()", McpRouteCreditProblem(McpFixtureRoute(".RequireMcpConnectorToken()"), McpRouteFixtureLedger, 0, Array.Empty<Waiver>()));
        Assert.StartsWith("no .RequireMcpConnectorToken()", McpRouteCreditProblem(McpFixtureRoute(".RequireAuthorization()"), McpRouteFixtureLedger, 0, Array.Empty<Waiver>()));
        Assert.StartsWith("a named policy", McpRouteCreditProblem(
            McpFixtureRoute(".RequireAuthorization(\"ExternalCollaboration\").RequireAuthorization().RequireMcpConnectorToken()"),
            McpRouteFixtureLedger, 0, Array.Empty<Waiver>()));
        Assert.StartsWith("it also carries a waiver", McpRouteCreditProblem(McpFixtureRoute(sanctioned), McpRouteFixtureLedger, 0,
            new[] { Pending(McpRouteFixtureKey, "UNOWNED-NEW", Gap.NoDecision, "Seeded by McpRouteCredit_Controls: a waiver beside the MCP ledger entry.") }));
        Assert.Contains("MCP tool violation", McpRouteCreditProblem(McpFixtureRoute(sanctioned), McpRouteFixtureLedger, 1, Array.Empty<Waiver>()));
        Assert.Contains("cite ADR-028 A10", McpRouteCreditProblem(McpFixtureRoute(sanctioned),
            new[] { new McpRoute(McpRouteFixtureKey, "zz-fixture", "A reason long enough to pass the length rule but citing no amendment at all.") }, 0, Array.Empty<Waiver>()));

        // RequireMcpConnectorToken is classified (NotAuthorizationForms) and is not a credited per-resource form: without the MCP
        // credit the route is still Credit.None, so Rule A names it.
        Assert.Equal(Credit.None, McpFixtureRoute(sanctioned).Credit);
    }

    [Fact(DisplayName = "task 021 seeding proof: removing either gate from the REAL McpEndpoints.cs chain makes the MCP credit fail")]
    public void McpRouteCredit_SeedingProof_OnTheRealSource()
    {
        var raw = ReadRepoFile("src/server/api/Sprk.Bff.Api/Api/Mcp/McpEndpoints.cs")!;
        Assert.Contains(".RequireMcpConnectorToken()", raw, StringComparison.Ordinal);

        string? ProblemFor(string source)
        {
            var route = Assert.Single(
                ScanFixtures(new[] { ("Api/Mcp/McpEndpoints.cs", source), ("Program.cs", "var app = builder.Build();\napp.MapMcpEndpoints();") }, "Api/Mcp/McpEndpoints.cs"),
                r => r.Key == "POST /mcp/legal-operations");
            return McpRouteCreditProblem(Assess(route, Array.Empty<HandlerDecision>()), McpRoutes, 0, Array.Empty<Waiver>());
        }

        Assert.Null(ProblemFor(raw));
        Assert.StartsWith("no .RequireMcpConnectorToken()", ProblemFor(raw.Replace(".RequireMcpConnectorToken()", string.Empty, StringComparison.Ordinal)));
        Assert.StartsWith("no .RequireAuthorization()", ProblemFor(Regex.Replace(raw, @"\.RequireAuthorization\(\)\s*", string.Empty)));
    }

    private const string McpToolFixtureHeader = "using Microsoft.AspNetCore.Authorization;\nnamespace Sprk.Bff.Api.Api.Mcp.LegalOperations;\n\n";

    private static string McpToolFixture(string attributes, string options = "ReadOnly = true, Title = \"Show\"", string parameters = "IDecisionCapability capability") =>
        McpToolFixtureHeader
        + attributes + "\ninternal sealed class FakeShowTool : McpToolDefinition\n{\n"
        + "    public override McpServerTool Create(IServiceProvider services) =>\n"
        + "        McpServerTool.Create(Run, new McpServerToolCreateOptions { Services = services, Name = \"fake_show\", " + options + " });\n\n"
        + "    private static string Run(" + parameters + ") => \"x\";\n}\n";

    private static List<string> McpToolFixtureViolations(string source, string policy = "", IReadOnlyList<SourceUnit>? extra = null)
    {
        var units = new List<SourceUnit> { new("Api/Mcp/LegalOperations/FakeShowTool.cs", source) };
        units.AddRange(extra ?? Array.Empty<SourceUnit>());
        var ai = AiInternalTypeNames(units);
        return McpToolViolations(units, new[] { new McpTool("FakeShowTool", policy, "IDecisionCapability",
            "Seeded by the MCP tool controls: reads the caller's decisions through IDecisionCapability as the caller.") }, ai);
    }

    [Fact(DisplayName = "task 021 controls: the tool rule fires on each broken shape and not on the sanctioned one")]
    public void McpToolRule_Controls()
    {
        const string area = "[McpArea(McpEndpoints.LegalOperationsArea)]\n";
        var aiUnit = new SourceUnit("Services/Ai/Chat/ChatSessionManager.cs", "namespace X;\npublic sealed class ChatSessionManager { }\n");
        var publicContract = new SourceUnit("Services/Ai/PublicContracts/IAiSummaryFacade.cs", "namespace X;\npublic interface IAiSummaryFacade { }\n");

        // MUST NOT FIRE: [Authorize] (default policy) or an existing named workforce policy; explicit ReadOnly and Title; the seam;
        // a PublicContracts facade is allowed.
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]")));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SystemAdmin\")]"), policy: "SystemAdmin"));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", parameters: "IDecisionCapability capability, IAiSummaryFacade summary"),
            extra: new[] { aiUnit, publicContract }));

        // MUST FIRE, one cause each.
        static void Fires(List<string> violations, string expected) =>
            Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));

        Fires(McpToolFixtureViolations(McpToolFixture(area)), "needs exactly one [Authorize]");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]\n[AllowAnonymous]")), "[AllowAnonymous]");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"ExternalCollaboration\")]"), policy: "ExternalCollaboration"), "forbidden policy");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = AuthPolicies.ExternalCollaboration)]")), "forbidden policy");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(AuthenticationSchemes = \"Ciam\")]")), "authentication scheme");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SystemAdmin\")]")), "is not the ledger's");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", options: "Title = \"Show\"")), "ReadOnly = true|false");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", options: "ReadOnly = true")), "does not set a Title");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", parameters: "IDecisionCapability capability, ChatSessionManager chat"),
            extra: new[] { aiUnit }), "uses ChatSessionManager, an AI-internal type");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", parameters: "IHttpContextAccessor accessor")), "never names its ledger seam");
        Fires(McpToolViolations(new[] { new SourceUnit("Api/Mcp/LegalOperations/FakeShowTool.cs", McpToolFixture(area + "[Authorize]")) },
            Array.Empty<McpTool>(), new HashSet<string>()), "missing from McpTools");
        Fires(McpToolViolations(Array.Empty<SourceUnit>(), new[] { new McpTool("GoneTool", "", "IDecisionCapability",
            "Seeded by the MCP tool controls: a ledger entry whose tool class no longer exists in source.") }, new HashSet<string>()), "stale entry");
        Fires(McpToolViolations(new[] { new SourceUnit("Api/Mcp/Bad.cs", "public static class Bad\n{\n    [McpServerTool(ReadOnly = true)]\n    public static string Run() => \"x\";\n}\n") },
            Array.Empty<McpTool>(), new HashSet<string>()), "bypasses McpToolCatalog");
        Fires(McpToolViolations(new[] { new SourceUnit("Infrastructure/DI/Bad.cs", "services.AddMcpServer().WithToolsFromAssembly();") },
            Array.Empty<McpTool>(), new HashSet<string>()), "bypasses McpToolCatalog");

        // A class without [McpArea] is not a tool (the A10 suite's tool-shape fixtures live in tests, never in the BFF).
        Assert.Empty(McpToolViolations(new[] { new SourceUnit("Api/Mcp/Other.cs", "[Authorize]\ninternal sealed class NotATool { }\n") },
            Array.Empty<McpTool>(), new HashSet<string>()));
    }
}
