using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Sprk.Bff.Api.Api.Mcp;
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
/// <item><b><see cref="McpTools"/></b> — every class tagged <c>[McpArea]</c> / <c>[McpAreaAttribute]</c> in the BFF is listed, is
/// not <c>partial</c>, and carries <c>[Authorize]</c> on the class with exactly the ledger's policy (none = the default policy; a
/// literal or an <c>AuthPolicies</c> constant, anything else is unreadable), a policy in production's
/// <c>McpToolAuthorization.AllowedToolPolicies</c>, no authentication scheme or roles and no <c>[AllowAnonymous]</c>; builds the
/// tool from an unqualified method of its own class and never sets <c>Metadata</c> (the SDK enforces the handler's declaring
/// class's <c>[Authorize]</c>, or <c>Metadata</c>); sets <c>ReadOnly = true|false</c> and <c>Title =</c> explicitly; injects no type
/// declared under <c>Services/Ai/**</c> outside <c>PublicContracts</c> (ADR-013); and names its declared caller seam. A reflection
/// cross-check over the compiled BFF assembly (<see cref="McpToolReflectionViolations"/>) holds the same set, policy and ADR-013
/// rules where text cannot see (an alias, a split partial). No SDK tool, prompt or resource route that bypasses the catalog
/// (<see cref="McpSdkToolRegistration"/>) exists anywhere.</item>
/// </list>
/// <para>Every <c>.MapMcp(</c> registration must be in <see cref="McpRoutes"/> (<see cref="UnlistedMapMcpRoutes"/>), so no MCP route is
/// credited by a waiver.</para>
/// <para>The route counts as declared for Rule A only while BOTH ledgers are clean (<see cref="McpCreditedRoutes"/>). A tool that
/// breaks its rule therefore turns the route itself red.</para>
/// <para><b>Maintenance.</b> Adding a tool: add its class to <see cref="McpTools"/> with its policy (one of
/// <c>McpToolAuthorization.AllowedToolPolicies</c>: "" for the default or "SystemAdmin"; widening that set is a production change
/// with its own review), the seam it calls as the caller (e.g. <c>IDecisionCapability</c>) and a reason of 60+ characters.
/// Adding an area: add its route to <see cref="McpRoutes"/> with the same chain. Never credit an MCP route with a waiver.
/// <b>Limits, stated:</b> the checks read TEXT. A seam named but not called passes; what each tool does with it is the
/// reviewer's to read, and the A10 suite (<c>tests/integration/auth/Mcp/McpAuthTests.cs</c>) proves the runtime behaviour.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private sealed record McpRoute(string Route, string Area, string Reason);

    /// <summary>One MCP tool. <paramref name="Policy"/> "" means bare <c>[Authorize]</c> (the default policy).</summary>
    private sealed record McpTool(string Class, string Policy, string Seam, string Reason);

    /// <summary>
    /// The ONLY policies an MCP tool may name (FR-07, B-7), read from production (<see cref="McpToolAuthorization.AllowedToolPolicies"/>),
    /// so the guard and the catalog share one allow-list. "" is bare <c>[Authorize]</c> (the DefaultPolicy).
    /// </summary>
    private static IReadOnlySet<string> McpAllowedPolicies => McpToolAuthorization.AllowedToolPolicies;

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

    /// <summary>
    /// Any SDK route to a tool, prompt or resource that bypasses <c>McpToolCatalog</c> (and so the per-tool rule): an SDK attribute
    /// (<c>[McpServerTool]</c>, <c>[McpServerPrompt]</c>, <c>[McpServerResource]</c>, their <c>*Type</c> forms); a builder call
    /// (<c>.WithTools*</c>, <c>.WithPrompts*</c>, <c>.WithResources*</c>, <c>.With*Handler</c>); a raw handler on the server options
    /// (<c>McpServerHandlers</c>, <c>Handlers.XHandler =</c>, <c>CallToolHandler =</c> and the other list/call/read handlers); or a
    /// prompt or resource collection. 020/021's own code (<c>WithHttpTransport</c>, <c>AddAuthorizationFilters</c>,
    /// <c>ConfigureSessionOptions</c>, <c>serverOptions.ToolCollection = tools</c>) passes (<see cref="McpToolRule_Controls"/>).
    /// </summary>
    private static readonly Regex McpSdkToolRegistration = new(
        @"\.\s*With(?:Tools\w*|\w+Handler|Prompts\w*|Resources\w*)\s*[<(]"
        + @"|\[\s*McpServer(?:Tool|Prompt|Resource)(?:Type)?\b"
        + @"|\bMcpServerHandlers\b"
        + @"|\bHandlers\s*\.\s*\w+Handler\s*=(?!=)"
        + @"|\b(?:ListTools|CallTool|ListPrompts|GetPrompt|ListResources|ListResourceTemplates|ReadResource)Handler\s*=(?!=)"
        + @"|\b(?:Prompt|Resource)Collection\s*=(?!=)",
        RegexOptions.Compiled);

    /// <summary>A tool class carrying the area attribute, in either spelling (an alias is caught by <see cref="McpToolReflectionViolations"/>).</summary>
    private static readonly Regex McpAreaAttributeUse = new(@"\bMcpArea(?:Attribute)?\s*\(", RegexOptions.Compiled);

    /// <summary>The handler argument of <c>McpServerTool.Create(...)</c>.</summary>
    private static readonly Regex McpServerToolCreateCall = new(@"\bMcpServerTool\s*\.\s*Create\s*\(", RegexOptions.Compiled);

    /// <summary>The <c>const string X = "...";</c> members of every type named <c>AuthPolicies</c> in <paramref name="units"/>.</summary>
    private static IReadOnlyDictionary<string, string> AuthPolicyConstantsOf(IEnumerable<SourceUnit> units)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            foreach (var type in unit.Types.Where(t => t.Name == "AuthPolicies"))
            {
                foreach (Match c in AuthPolicyConstant.Matches(unit.Text[type.BodyStart..type.BodyEnd]))
                {
                    constants[c.Groups["name"].Value] = c.Groups["value"].Value;
                }
            }
        }

        return constants;
    }

    /// <summary>The simple names of every type declared under the BFF's <c>Services/Ai/**</c>, <c>PublicContracts</c> excepted (ADR-013).</summary>
    private static IReadOnlySet<string> AiInternalTypeNames(IEnumerable<SourceUnit> bffUnits) =>
        bffUnits.Where(u => u.Path.StartsWith("Services/Ai/", StringComparison.Ordinal)
                            && !u.Path.StartsWith("Services/Ai/PublicContracts/", StringComparison.Ordinal))
            .SelectMany(u => u.Types.Select(t => t.Name))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Every violation of the MCP tool rule over <paramref name="bffUnits"/> (paths relative to the BFF root). The names of the tool
    /// classes the text finds go to <paramref name="found"/> when given (the reflection cross-check compares them).
    /// </summary>
    private static List<string> McpToolViolations(
        IReadOnlyList<SourceUnit> bffUnits, IReadOnlyList<McpTool> ledger, IReadOnlySet<string> aiTypeNames, ISet<string>? found = null)
    {
        var violations = new List<string>();
        found ??= new HashSet<string>(StringComparer.Ordinal);
        var policyConstants = AuthPolicyConstantsOf(bffUnits);

        foreach (var unit in bffUnits)
        {
            if (McpSdkToolRegistration.Match(unit.Code) is { Success: true } sdk)
            {
                violations.Add($"{unit.Path}:{unit.LineOf(sdk.Index)}: an SDK tool, prompt or resource registration ([McpServerTool], WithTools/WithPrompts/"
                               + "WithResources, With*Handler, server Handlers) bypasses McpToolCatalog and its per-tool rule; build the tool as an "
                               + "[McpArea] McpToolDefinition instead.");
            }

            foreach (Match m in McpToolClass.Matches(unit.Code))
            {
                var attrsCode = m.Groups["attrs"].Value;
                if (!McpAreaAttributeUse.IsMatch(attrsCode))
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

                // A split partial class could carry a second [Authorize] or the handler in a part this rule never reads.
                if (decl is { IsPartial: true } || Regex.IsMatch(m.Value, @"\bpartial\b"))
                {
                    violations.Add($"{at}: an MCP tool class may not be partial; declare it in one file so the rule reads all of it.");
                }

                var authorize = McpAuthorizeAttribute.Matches(attrsCode).ToList();
                if (authorize.Count != 1)
                {
                    violations.Add($"{at}: needs exactly one [Authorize] on the tool class (B-7); found {authorize.Count}.");
                }
                else
                {
                    var argsGroup = authorize[0].Groups["args"];
                    var argsText = attrsText.Substring(argsGroup.Index, argsGroup.Length);
                    var policy = McpToolPolicyOf(argsText, policyConstants, out var problem);
                    if (problem is not null)
                    {
                        violations.Add($"{at}: {problem}");
                    }
                    else if (!McpAllowedPolicies.Contains(policy!))
                    {
                        violations.Add($"{at}: [Authorize] names \"{policy}\", which is not in McpToolAuthorization.AllowedToolPolicies (FR-07, B-7).");
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

                // The SDK enforces the [Authorize] of the handler's DECLARING class, or McpServerToolCreateOptions.Metadata: either
                // could make it enforce a policy other than the class's own (the runtime check, ValidateTool, refuses the same).
                if (Regex.Match(body, @"\bMetadata\s*=(?!=)") is { Success: true } metadata)
                {
                    violations.Add($"{at}: sets McpServerToolCreateOptions.Metadata (line {unit.LineOf(decl!.BodyStart + metadata.Index)}); the SDK would "
                                   + "enforce that metadata instead of the class's [Authorize] (B-7).");
                }

                foreach (Match create in McpServerToolCreateCall.Matches(body))
                {
                    var open = create.Index + create.Length - 1;
                    var close = MatchClose(body, open);
                    var spans = close > open ? SplitTopLevel(body, open + 1, close) : new List<(int Start, int End)>();
                    var handler = spans.Count > 0 ? body[spans[0].Start..spans[0].End].Trim() : string.Empty;
                    var ownMethod = Regex.IsMatch(handler, @"^[A-Za-z_]\w*$")
                                    && unit.Methods.Any(x => x.Name == handler && x.NameIndex > decl!.BodyStart && x.NameIndex < decl.BodyEnd);
                    if (!ownMethod)
                    {
                        violations.Add($"{at}: McpServerTool.Create's handler '{handler}' is not an unqualified method group of {name}; the SDK takes "
                                       + "[Authorize] from the handler's declaring class, so a helper class's method (X.Method) or a lambda changes the policy (B-7).");
                    }
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

            if (!McpAllowedPolicies.Contains(entry.Policy))
            {
                violations.Add($"McpTools {entry.Class}: the ledger names \"{entry.Policy}\", which is not in McpToolAuthorization.AllowedToolPolicies (FR-07).");
            }
        }

        return violations;
    }

    /// <summary>
    /// The policy one <c>[Authorize(...)]</c> argument list names: "" for none, a string literal, or an <c>AuthPolicies.X</c>
    /// constant resolved from <paramref name="constants"/> (the way Rule A's sign-in resolver reads it). Anything else — another
    /// constant, an expression, <c>Roles</c>, <c>AuthenticationSchemes</c> — sets <paramref name="problem"/> and returns null, so a
    /// policy the guard cannot read never passes as the default policy.
    /// </summary>
    private static string? McpToolPolicyOf(string argsText, IReadOnlyDictionary<string, string> constants, out string? problem)
    {
        problem = null;
        string? policyExpression = null;
        foreach (var (start, end) in SplitTopLevel(argsText, 0, argsText.Length))
        {
            var arg = argsText[start..end].Trim();
            var named = Regex.Match(arg, @"^(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*(?<expr>.*)$", RegexOptions.Singleline);
            if (!named.Success)
            {
                policyExpression = arg;
                continue;
            }

            switch (named.Groups["name"].Value)
            {
                case "Policy":
                    policyExpression = named.Groups["expr"].Value;
                    break;
                case "AuthenticationSchemes":
                    problem = "[Authorize] names an authentication scheme; MCP tools run on the A10 workforce scheme only.";
                    return null;
                default:
                    problem = $"[Authorize] sets {named.Groups["name"].Value}; an MCP tool's [Authorize] may name only a Policy (FR-07, B-7).";
                    return null;
            }
        }

        if (policyExpression is null)
        {
            return string.Empty;
        }

        var policy = ResolvePolicyName(policyExpression, constants);
        if (policy is null)
        {
            problem = $"[Authorize] policy '{policyExpression.Trim()}' is not a policy the guard can read; name it as a string literal or an AuthPolicies constant.";
        }

        return policy;
    }

    /// <summary>
    /// The reflection half of the tool rule, over the COMPILED types (<paramref name="types"/>; production: the BFF assembly): the
    /// types carrying <see cref="McpAreaAttribute"/> must be exactly the classes the text rule found (<paramref name="textFound"/>; an
    /// alias such as <c>using Area = ...McpAreaAttribute;</c> hides a tool from the text) and the ledger; each carries exactly one
    /// <c>[Authorize]</c> with the ledger's allow-listed policy, no scheme and no roles, and no anonymity attribute (a split partial
    /// class merges its parts' attributes here); and no parameter, field or property of the tool (or its nested types, such as
    /// closures) is a type from <c>Sprk.Bff.Api.Services.Ai</c> outside <c>PublicContracts</c> (ADR-013).
    /// </summary>
    private static List<string> McpToolReflectionViolations(IEnumerable<Type> types, IReadOnlySet<string> textFound, IReadOnlyList<McpTool> ledger)
    {
        var violations = new List<string>();
        var tagged = types.Where(t => t.GetCustomAttribute<McpAreaAttribute>(inherit: false) is not null).ToList();
        var taggedNames = tagged.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in taggedNames.Except(textFound).OrderBy(n => n, StringComparer.Ordinal))
        {
            violations.Add($"{name}: carries McpAreaAttribute in the compiled assembly, but the source rule cannot see it (an alias, or an "
                           + "attribute form the text rule does not read); tag it as [McpArea(...)] on the class.");
        }

        foreach (var name in textFound.Except(taggedNames).OrderBy(n => n, StringComparer.Ordinal))
        {
            violations.Add($"{name}: the source rule reads it as an [McpArea] tool, but no compiled type of that name carries McpAreaAttribute.");
        }

        foreach (var type in tagged.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var at = $"{type.FullName} (reflection)";
            var entry = ledger.FirstOrDefault(e => e.Class == type.Name);
            if (entry is null)
            {
                violations.Add($"{at}: an MCP tool missing from McpTools.");
            }

            var authorize = type.GetCustomAttributes(inherit: false).OfType<IAuthorizeData>().ToList();
            if (authorize.Count != 1)
            {
                violations.Add($"{at}: carries {authorize.Count} [Authorize] attributes; exactly one (B-7).");
            }
            else
            {
                var data = authorize[0];
                var policy = data.Policy ?? string.Empty;
                if (!string.IsNullOrEmpty(data.AuthenticationSchemes) || !string.IsNullOrEmpty(data.Roles))
                {
                    violations.Add($"{at}: [Authorize] names a scheme or roles; an MCP tool's [Authorize] may name only a Policy.");
                }

                if (!McpAllowedPolicies.Contains(policy))
                {
                    violations.Add($"{at}: [Authorize] names \"{policy}\", which is not in McpToolAuthorization.AllowedToolPolicies.");
                }
                else if (entry is not null && !string.Equals(policy, entry.Policy, StringComparison.Ordinal))
                {
                    violations.Add($"{at}: [Authorize] policy \"{policy}\" is not the ledger's \"{entry.Policy}\".");
                }
            }

            if (type.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any())
            {
                violations.Add($"{at}: carries anonymity metadata (FR-02).");
            }

            foreach (var ai in McpToolMemberTypes(type).Where(IsAiInternal).Select(t => t.FullName).Distinct().OrderBy(n => n, StringComparer.Ordinal))
            {
                violations.Add($"{at}: uses {ai}, an AI-internal type (Services/Ai/**); go through Services/Ai/PublicContracts (ADR-013).");
            }
        }

        return violations;
    }

    private const BindingFlags McpDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Every type a tool's methods, constructors, fields and properties mention (generic arguments, arrays and refs unwrapped), nested types included.</summary>
    private static IEnumerable<Type> McpToolMemberTypes(Type type)
    {
        var mentioned = type.GetMethods(McpDeclared).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(type.GetConstructors(McpDeclared).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Concat(type.GetFields(McpDeclared).Select(f => f.FieldType))
            .Concat(type.GetProperties(McpDeclared).Select(p => p.PropertyType));
        return mentioned.SelectMany(Unwrap).Concat(type.GetNestedTypes(McpDeclared).SelectMany(McpToolMemberTypes));

        static IEnumerable<Type> Unwrap(Type t)
        {
            if (t.HasElementType)
            {
                return Unwrap(t.GetElementType()!);
            }

            return t.IsGenericType ? t.GetGenericArguments().SelectMany(Unwrap).Prepend(t) : new[] { t };
        }
    }

    private static bool IsAiInternal(Type t)
    {
        var ns = t.Namespace ?? string.Empty;
        const string ai = "Sprk.Bff.Api.Services.Ai";
        const string contracts = ai + ".PublicContracts";
        var underAi = ns == ai || ns.StartsWith(ai + ".", StringComparison.Ordinal);
        var underContracts = ns == contracts || ns.StartsWith(contracts + ".", StringComparison.Ordinal);
        return underAi && !underContracts;
    }

    /// <summary>The live routes registered by <c>.MapMcp(</c> whose key <paramref name="ledger"/> does not list (none may sit under a waiver).</summary>
    private static List<string> UnlistedMapMcpRoutes(IEnumerable<Assessment> assessments, IReadOnlyList<McpRoute> ledger) =>
        assessments.Where(a => IsMapMcpRegistration(a.Route) && ledger.All(r => r.Route != a.Key))
            .Select(a => $"{a.Key} at {a.Route.File}:{a.Route.Line}")
            .ToList();

    private static bool IsMapMcpRegistration(RouteRegistration route)
    {
        var line = route.Unit is null ? string.Empty : route.Unit.Code.Split('\n').ElementAtOrDefault(route.Line - 1) ?? string.Empty;
        return Regex.IsMatch(line, @"\.\s*MapMcp\s*\(");
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

        if (!IsMapMcpRegistration(route))
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

    // The text rule over the BFF source, then the reflection cross-check over the compiled BFF assembly.
    private static readonly Lazy<IReadOnlyList<string>> RealMcpToolViolations = new(() =>
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var violations = McpToolViolations(Real.Set.Units, McpTools, AiInternalTypeNames(Real.Set.Units), found);
        violations.AddRange(McpToolReflectionViolations(typeof(McpToolDefinition).Assembly.GetTypes(), found, McpTools));
        return violations;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

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

        // Every MapMcp registration is in McpRoutes, so none can be credited by a waiver instead (fix round 1, K1).
        violations.AddRange(UnlistedMapMcpRoutes(RealAssessments.Value, McpRoutes)
            .Select(r => $"{r}: a MapMcp route missing from McpRoutes — list it there; never credit an MCP route with a waiver."));

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

    private static string McpToolFixture(
        string attributes, string options = "ReadOnly = true, Title = \"Show\"", string parameters = "IDecisionCapability capability",
        string handler = "Run", string modifiers = "internal sealed") =>
        McpToolFixtureHeader
        + attributes + "\n" + modifiers + " class FakeShowTool : McpToolDefinition\n{\n"
        + "    public override McpServerTool Create(IServiceProvider services) =>\n"
        + "        McpServerTool.Create(" + handler + ", new McpServerToolCreateOptions { Services = services, Name = \"fake_show\", " + options + " });\n\n"
        + "    private static string Run(" + parameters + ") => \"x\";\n}\n";

    private static List<string> McpToolFixtureViolations(string source, string policy = "", IReadOnlyList<SourceUnit>? extra = null)
    {
        var units = new List<SourceUnit> { new("Api/Mcp/LegalOperations/FakeShowTool.cs", source) };
        units.AddRange(extra ?? Array.Empty<SourceUnit>());
        var ai = AiInternalTypeNames(units);
        return McpToolViolations(units, new[] { new McpTool("FakeShowTool", policy, "IDecisionCapability",
            "Seeded by the MCP tool controls: reads the caller's decisions through IDecisionCapability as the caller.") }, ai);
    }

    private static void Fires(List<string> violations, string expected) =>
        Assert.True(violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"expected a violation containing '{expected}'; got:\n" + string.Join("\n", violations));

    // A property, not a static field: the scanner's regexes are static fields of other partial files, and static initialisation
    // order across partial files is undefined (a field here ran before them: NullReferenceException in the type initializer).
    private static SourceUnit McpFixtureAuthPolicies => new("Infrastructure/Authentication/AuthPolicies.cs",
        "namespace X;\npublic static class AuthPolicies\n{\n    public const string ExternalCollaboration = \"ExternalCollaboration\";\n"
        + "    public const string AdminAlias = \"SystemAdmin\";\n}\n");

    [Fact(DisplayName = "task 021 controls: the tool rule fires on each broken shape and not on the sanctioned one")]
    public void McpToolRule_Controls()
    {
        const string area = "[McpArea(McpEndpoints.LegalOperationsArea)]\n";
        var aiUnit = new SourceUnit("Services/Ai/Chat/ChatSessionManager.cs", "namespace X;\npublic sealed class ChatSessionManager { }\n");
        var publicContract = new SourceUnit("Services/Ai/PublicContracts/IAiSummaryFacade.cs", "namespace X;\npublic interface IAiSummaryFacade { }\n");

        // MUST NOT FIRE: [Authorize] (default policy) or the allow-listed SystemAdmin (as a literal, positionally, or through an
        // AuthPolicies constant); explicit ReadOnly and Title; the handler an unqualified method of the class; the seam; a
        // PublicContracts facade.
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]")));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SystemAdmin\")]"), policy: "SystemAdmin"));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(\"SystemAdmin\")]"), policy: "SystemAdmin"));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = AuthPolicies.AdminAlias)]"), policy: "SystemAdmin",
            extra: new[] { McpFixtureAuthPolicies }));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture("[McpAreaAttribute(McpEndpoints.LegalOperationsArea)]\n[Authorize]")));
        Assert.Empty(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", parameters: "IDecisionCapability capability, IAiSummaryFacade summary"),
            extra: new[] { aiUnit, publicContract }));

        // MUST FIRE, one cause each.
        Fires(McpToolFixtureViolations(McpToolFixture(area)), "needs exactly one [Authorize]");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]\n[AllowAnonymous]")), "[AllowAnonymous]");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"ExternalCollaboration\")]"), policy: "ExternalCollaboration"),
            "not in McpToolAuthorization.AllowedToolPolicies");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = AuthPolicies.ExternalCollaboration)]"), extra: new[] { McpFixtureAuthPolicies }),
            "names \"ExternalCollaboration\", which is not in McpToolAuthorization.AllowedToolPolicies");
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

        // Fix round 1, F4 (item 1): the SDK enforces the handler's DECLARING class's [Authorize], or Metadata.
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SystemAdmin\")]", handler: "Helpers.Run"), policy: "SystemAdmin"),
            "is not an unqualified method group of FakeShowTool");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", handler: "() => \"x\"")), "is not an unqualified method group of FakeShowTool");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", handler: "Missing")), "is not an unqualified method group of FakeShowTool");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SystemAdmin\")]",
            options: "ReadOnly = true, Title = \"Show\", Metadata = [new AuthorizeAttribute()]"), policy: "SystemAdmin"), "sets McpServerToolCreateOptions.Metadata");

        // Fix round 1, F3 (item 2): the long attribute spelling is a tool; a partial tool class is refused.
        Fires(McpToolViolations(new[] { new SourceUnit("Api/Mcp/LegalOperations/FakeShowTool.cs",
            McpToolFixture("[McpAreaAttribute(McpEndpoints.LegalOperationsArea)]\n[Authorize]")) }, Array.Empty<McpTool>(), new HashSet<string>()), "missing from McpTools");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize]", modifiers: "internal sealed partial")), "may not be partial");

        // Fix round 1, F3 (item 3): a policy the guard cannot read never passes as the default policy.
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = SomeConst)]")), "is not a policy the guard can read");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = AuthPolicies.ExternalCollaboration)]")), "is not a policy the guard can read");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Roles = \"SystemAdmin\")]")), "may name only a Policy");

        // Fix round 1, F4 (item 4): an allow-list, read from production, on the class and on the ledger.
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SomeOtherWorkforcePolicy\")]"), policy: "SomeOtherWorkforcePolicy"),
            "names \"SomeOtherWorkforcePolicy\", which is not in McpToolAuthorization.AllowedToolPolicies");
        Fires(McpToolFixtureViolations(McpToolFixture(area + "[Authorize(Policy = \"SomeOtherWorkforcePolicy\")]"), policy: "SomeOtherWorkforcePolicy"),
            "McpTools FakeShowTool: the ledger names \"SomeOtherWorkforcePolicy\"");

        // A class without [McpArea] is not a tool (the A10 suite's tool-shape fixtures live in tests, never in the BFF).
        Assert.Empty(McpToolViolations(new[] { new SourceUnit("Api/Mcp/Other.cs", "[Authorize]\ninternal sealed class NotATool { }\n") },
            Array.Empty<McpTool>(), new HashSet<string>()));
    }

    [Fact(DisplayName = "task 021 fix round 1 controls: every SDK route to a tool, prompt or resource outside McpToolCatalog fires; 020/021's own MCP code does not")]
    public void McpSdkBypass_Controls()
    {
        static List<string> Scan(string path, string source) =>
            McpToolViolations(new[] { new SourceUnit(path, source) }, Array.Empty<McpTool>(), new HashSet<string>());

        // MUST FIRE, one shape each.
        foreach (var bypass in new[]
                 {
                     "public static class Bad\n{\n    [McpServerTool(ReadOnly = true)]\n    public static string Run() => \"x\";\n}\n",
                     "[McpServerToolType]\npublic static class Bad { }\n",
                     "public static class Bad\n{\n    [McpServerPrompt]\n    public static string Prompt() => \"x\";\n}\n",
                     "public static class Bad\n{\n    [McpServerResource(UriTemplate = \"x://y\")]\n    public static string Res() => \"x\";\n}\n",
                     "services.AddMcpServer().WithToolsFromAssembly();",
                     "services.AddMcpServer().WithTools<BadTools>();",
                     "services.AddMcpServer().WithPrompts<BadPrompts>();",
                     "services.AddMcpServer().WithPromptsFromAssembly();",
                     "services.AddMcpServer().WithResources<BadResources>();",
                     "services.AddMcpServer().WithCallToolHandler((ctx, ct) => default);",
                     "services.AddMcpServer().WithListToolsHandler((ctx, ct) => default);",
                     "o.Handlers.CallToolHandler = (ctx, ct) => default;",
                     "o.Handlers = new McpServerHandlers();",
                     "o.Handlers = new() { CallToolHandler = (ctx, ct) => default };",
                     "serverOptions.PromptCollection = prompts;",
                 })
        {
            Fires(Scan("Infrastructure/DI/Bad.cs", bypass), "bypasses McpToolCatalog");
        }

        // MUST NOT FIRE: the real McpModule (WithHttpTransport, AddAuthorizationFilters, ConfigureSessionOptions, ToolCollection),
        // the catalog and the endpoints.
        foreach (var path in new[] { "Infrastructure/DI/McpModule.cs", "Api/Mcp/McpToolCatalog.cs", "Api/Mcp/McpEndpoints.cs", "Api/Mcp/McpToolAuthorization.cs" })
        {
            var source = ReadRepoFile("src/server/api/Sprk.Bff.Api/" + path)!;
            Assert.Empty(Scan(path, source));
        }

        Assert.Contains("ConfigureSessionOptions", ReadRepoFile("src/server/api/Sprk.Bff.Api/Infrastructure/DI/McpModule.cs")!, StringComparison.Ordinal);
    }

    // ── reflection fixtures (compiled into THIS assembly, which no catalog or BFF scan reads) ──────────────────────

    [McpArea("zz-fixture")]
    [Authorize]
    private sealed class McpReflectionSanctionedTool : McpToolDefinition
    {
        public override ModelContextProtocol.Server.McpServerTool Create(IServiceProvider services) => throw new NotSupportedException();

        private static string Run(Sprk.Bff.Api.Services.Ai.PublicContracts.IActionSeam seam) => "x";
    }

    [McpArea("zz-fixture")]
    [Authorize]
    private sealed class McpReflectionAiInternalTool : McpToolDefinition
    {
        public override ModelContextProtocol.Server.McpServerTool Create(IServiceProvider services) => throw new NotSupportedException();

        private static string Run(IEnumerable<Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager> chats) => "x";
    }

    // What a split partial class compiles to: both parts' [Authorize] on one type.
    [McpArea("zz-fixture")]
    [Authorize]
    [Authorize(Policy = "SystemAdmin")]
    private sealed class McpReflectionTwoAuthorizeTool : McpToolDefinition
    {
        public override ModelContextProtocol.Server.McpServerTool Create(IServiceProvider services) => throw new NotSupportedException();
    }

    [McpArea("zz-fixture")]
    [Authorize(Policy = "SomeOtherWorkforcePolicy")]
    private sealed class McpReflectionUnlistedPolicyTool : McpToolDefinition
    {
        public override ModelContextProtocol.Server.McpServerTool Create(IServiceProvider services) => throw new NotSupportedException();
    }

    [McpArea("zz-fixture")]
    [Authorize]
    [AllowAnonymous]
    private sealed class McpReflectionAnonymousTool : McpToolDefinition
    {
        public override ModelContextProtocol.Server.McpServerTool Create(IServiceProvider services) => throw new NotSupportedException();
    }

    private static McpTool McpReflectionEntry(Type type, string policy = "") =>
        new(type.Name, policy, "IActionSeam", "Seeded by the MCP reflection controls: a compiled fixture tool in the ArchTests assembly only.");

    [Fact(DisplayName = "task 021 fix round 1 controls: the reflection cross-check fires on a tool the text cannot see, a merged second [Authorize], an unlisted policy, anonymity and an AI-internal parameter")]
    public void McpToolReflection_Controls()
    {
        static HashSet<string> Seen(params Type[] types) => types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        // MUST NOT FIRE: the text saw it, the ledger lists it, bare [Authorize], a PublicContracts parameter. The real BFF has no
        // tool yet, so the real cross-check is empty too.
        var sanctioned = typeof(McpReflectionSanctionedTool);
        Assert.Empty(McpToolReflectionViolations(new[] { sanctioned }, Seen(sanctioned), new[] { McpReflectionEntry(sanctioned) }));
        Assert.Empty(McpToolReflectionViolations(typeof(McpToolDefinition).Assembly.GetTypes(), new HashSet<string>(), McpTools));

        // MUST FIRE, one cause each.
        Fires(McpToolReflectionViolations(new[] { sanctioned }, new HashSet<string>(), new[] { McpReflectionEntry(sanctioned) }),
            "the source rule cannot see it");
        Fires(McpToolReflectionViolations(Array.Empty<Type>(), Seen(sanctioned), new[] { McpReflectionEntry(sanctioned) }),
            "no compiled type of that name carries McpAreaAttribute");
        Fires(McpToolReflectionViolations(new[] { sanctioned }, Seen(sanctioned), Array.Empty<McpTool>()), "missing from McpTools");
        Fires(McpToolReflectionViolations(new[] { sanctioned }, Seen(sanctioned), new[] { McpReflectionEntry(sanctioned, "SystemAdmin") }), "is not the ledger's");

        var ai = typeof(McpReflectionAiInternalTool);
        Fires(McpToolReflectionViolations(new[] { ai }, Seen(ai), new[] { McpReflectionEntry(ai) }),
            "uses Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager, an AI-internal type");

        var two = typeof(McpReflectionTwoAuthorizeTool);
        Fires(McpToolReflectionViolations(new[] { two }, Seen(two), new[] { McpReflectionEntry(two) }), "carries 2 [Authorize] attributes");

        var unlisted = typeof(McpReflectionUnlistedPolicyTool);
        Fires(McpToolReflectionViolations(new[] { unlisted }, Seen(unlisted), new[] { McpReflectionEntry(unlisted, "SomeOtherWorkforcePolicy") }),
            "not in McpToolAuthorization.AllowedToolPolicies");

        var anonymous = typeof(McpReflectionAnonymousTool);
        Fires(McpToolReflectionViolations(new[] { anonymous }, Seen(anonymous), new[] { McpReflectionEntry(anonymous) }), "carries anonymity metadata");
    }

    [Fact(DisplayName = "task 021 fix round 1 controls: a MapMcp route missing from McpRoutes is named, a listed one is not")]
    public void UnlistedMapMcpRoutes_Controls()
    {
        var route = McpFixtureRoute(".RequireAuthorization().RequireMcpConnectorToken()");

        Assert.Empty(UnlistedMapMcpRoutes(new[] { route }, McpRouteFixtureLedger));
        Assert.Single(UnlistedMapMcpRoutes(new[] { route }, Array.Empty<McpRoute>()), r => r.StartsWith(McpRouteFixtureKey, StringComparison.Ordinal));
    }
}
