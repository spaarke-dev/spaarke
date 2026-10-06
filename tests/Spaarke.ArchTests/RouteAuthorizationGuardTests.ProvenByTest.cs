using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE ProvenByTest LEDGER CREDIT (main-session round 65 item 2). It is the one generic credit for a sweep fix the scanner
/// cannot see: an owner comparison in a handler, an OBO read that is not a caller-context seam, or a filter mode that
/// decides only for some request types of a shared attachment.
///
/// <para>A PRESENT route's sweep entry may set <c>ProvenByTest = true</c>. It is then resolved, and its route counts as
/// declared for Rule A, ONLY when all of these hold:</para>
/// <list type="number">
/// <item><c>ResolvedBy</c> is set, and the route carries no waiver.</item>
/// <item>The <c>ProofTest</c> exists, is compiled by a CI-run test project, and RUNS: a plain, unskipped xUnit test. This is
/// the same check every ProofTest gets (<see cref="ProofTestViolations"/>).</item>
/// <item>It drives the REAL app. Its class takes, or constructs, a fixture whose base chain in the test sources reaches
/// <c>WebApplicationFactory</c>, or it uses the booted BFF (<c>BootedBff</c>).</item>
/// <item>It references the route: its scope pairs the route's VERB with an inline path its template matches. This is the
/// same reading as the retired-route absence pin (<see cref="ScopePairsVerbWithPath"/>).</item>
/// <item>It asserts the route's authorization outcome both ways: a refusal (401 / 403 / 404) AND a success (2xx).</item>
/// </list>
/// <para><b>The cap</b> (round 65): this is the only new vocabulary, and it loosens no other rule. The credited forms,
/// HandlerDecisions, admin pins, waiver rules and retired-route rules are unchanged. An entry without the flag is judged
/// exactly as before.</para>
/// <para><b>Limits, stated</b> (owner round 56 class d): items 3 to 5 are read from TEXT. A refusal or success assertion
/// behind a false <c>if</c>, or a request whose response is not the one asserted, passes them. The test RUNNING is checked
/// (item 2); what it proves is the reviewer's to read, as for every ProofTest.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private static readonly Regex RefusalAssertion = new(
        @"HttpStatusCode\s*\.\s*(?:Forbidden|NotFound|Unauthorized)\b|StatusCodes\s*\.\s*Status40[134]\w*"
        + @"|\.\s*Be\s*\(\s*40[134]\s*\)|Assert\s*\.\s*Equal\s*\(\s*40[134]\s*,",
        RegexOptions.Compiled);

    private static readonly Regex SuccessAssertion = new(
        @"HttpStatusCode\s*\.\s*(?:OK|Created|NoContent|Accepted)\b|StatusCodes\s*\.\s*Status20[0-4]\w*"
        + @"|\.\s*Be\s*\(\s*20[0-4]\s*\)|Assert\s*\.\s*Equal\s*\(\s*20[0-4]\s*,|IsSuccessStatusCode",
        RegexOptions.Compiled);

    private static readonly Regex TypeWithBases = new(
        @"\b(?:class|record)\s+(?<name>[A-Za-z_]\w*)(?:\s*<[^>{]*>)?(?:\s*\([^){]*\))?\s*:\s*(?<bases>[^{;]+?)\s*(?:where\b[^{]*)?\{",
        RegexOptions.Compiled);

    /// <summary>Every test-source type and the simple names of its base types (generics and namespaces erased).</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> TestTypeBases = new(() =>
        TypeBasesOf(Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TypeBasesOf(IEnumerable<string> sources)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var raw in sources)
        {
            foreach (Match m in TypeWithBases.Matches(raw))
            {
                var bases = SimpleTypeNames(m.Groups["bases"].Value);
                if (!map.TryGetValue(m.Groups["name"].Value, out var list))
                {
                    map[m.Groups["name"].Value] = list = new List<string>();
                }

                list.AddRange(bases);
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Distinct(StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
    }

    /// <summary>"Ns.A&lt;Program&gt;, IClassFixture&lt;B&gt;" → A, IClassFixture, B (each simple name, generic arguments too).</summary>
    private static List<string> SimpleTypeNames(string list) =>
        Regex.Matches(list, @"[A-Za-z_][\w.]*").Select(m => m.Value.Split('.')[^1]).ToList();

    /// <summary>
    /// Null when <paramref name="methodName"/> in <paramref name="raw"/> meets items 3 to 5 of the ProvenByTest credit for
    /// <paramref name="routeKey"/>; otherwise why not.
    /// </summary>
    private static string? ProvenByTestProblem(
        string raw, string methodName, string routeKey, IReadOnlyDictionary<string, IReadOnlyList<string>> typeBases)
    {
        var unit = new SourceUnit("proof-test", raw);
        var methods = unit.Methods.Where(m => m.Name == methodName).ToList();
        if (methods.Count != 1)
        {
            return methods.Count == 0 ? $"no method '{methodName}' is declared there" : $"'{methodName}' is declared {methods.Count} times";
        }

        var method = methods[0];
        var space = routeKey.IndexOf(' ');
        var verb = routeKey[..space];
        var template = routeKey[(space + 1)..];
        var problems = new List<string>();

        // Item 3: the real app. The enclosing type's own bases and its IClassFixture<> arguments, the types it constructs,
        // and their base chains in the test sources.
        var type = unit.Types.Where(t => t.BodyStart <= method.NameIndex && method.NameIndex < t.BodyEnd)
            .OrderBy(t => t.BodyEnd - t.BodyStart).FirstOrDefault();
        var seeds = new List<string>();
        if (type is not null && typeBases.TryGetValue(type.Name, out var ownBases))
        {
            seeds.AddRange(ownBases);
        }

        seeds.AddRange(Regex.Matches(unit.Code, @"\bnew\s+([A-Za-z_][\w.]*)\s*[(<{]").Select(m => m.Groups[1].Value.Split('.')[^1]));
        seeds.AddRange(Regex.Matches(unit.Code, @"\b([A-Za-z_]\w*)\s*\.\s*StartAsync\s*\(").Select(m => m.Groups[1].Value));
        if (!ReachesTheRealApp(seeds, typeBases))
        {
            problems.Add("it does not drive the real app: neither its class's fixture nor anything it constructs derives from "
                         + "WebApplicationFactory (or is the booted BFF)");
        }

        // Item 4: the route. Items 4 and 5 are read over the same scope as every other proof test.
        var scopes = ProofScopes(unit, method);
        if (!scopes.Any(s => ScopePairsVerbWithPath(unit, s.Start, s.End, verb, template)))
        {
            problems.Add($"it names no request pairing {verb} with a path '{template}' matches");
        }

        // Item 5: both outcomes.
        var scopeCode = string.Join("\n", scopes.Select(s => unit.Code[s.Start..s.End]));
        if (!RefusalAssertion.IsMatch(scopeCode))
        {
            problems.Add("it asserts no refusal (401 / 403 / 404) for an unauthorized caller");
        }

        if (!SuccessAssertion.IsMatch(scopeCode))
        {
            problems.Add("it asserts no success (2xx) for an authorized caller");
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    private static bool ReachesTheRealApp(IEnumerable<string> seeds, IReadOnlyDictionary<string, IReadOnlyList<string>> typeBases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>(seeds);
        while (frontier.Count > 0)
        {
            var name = frontier.Dequeue();
            if (!seen.Add(name))
            {
                continue;
            }

            if (name is "WebApplicationFactory" or "BootedBff")
            {
                return true;
            }

            foreach (var b in typeBases.GetValueOrDefault(name) ?? Array.Empty<string>())
            {
                frontier.Enqueue(b);
            }
        }

        return false;
    }

    /// <summary>Every problem that keeps <paramref name="entry"/>'s ProvenByTest credit from holding (empty = it holds).</summary>
    private static List<string> ProvenByTestViolations(
        SweepFinding entry, Func<string, string?> readTestFile, IReadOnlyDictionary<string, IReadOnlyList<string>>? typeBases = null)
    {
        var label = $"{entry.SweepId} {entry.Route}";
        var problems = new List<string>();
        if (entry.ResolvedBy is null)
        {
            problems.Add($"{label}: ProvenByTest needs ResolvedBy (the fixing task)");
            return problems;
        }

        problems.AddRange(ProofTestViolations(entry, readTestFile));
        if (problems.Count > 0)
        {
            return problems;
        }

        var parts = entry.ProofTest!.Split("::");
        var why = ProvenByTestProblem(readTestFile(parts[0])!, parts[1], entry.Route, typeBases ?? TestTypeBases.Value);
        if (why is not null)
        {
            problems.Add($"{label}: ProofTest {entry.ProofTest} does not prove the route by test (round 65 item 2) — {why}");
        }

        return problems;
    }

    /// <summary>The present routes whose sweep entry's ProvenByTest credit HOLDS — what Rule A counts as declared.</summary>
    private static readonly Lazy<IReadOnlySet<string>> ProvenRoutes = new(() =>
        SweepFindings.Where(e => e.ProvenByTest && ProvenByTestViolations(e, ReadRepoFile).Count == 0)
            .Select(e => e.Route)
            .ToHashSet(StringComparer.Ordinal));

    // ── controls ──────────────────────────────────────────────────────────────────────────────────────────────────

    // The fixture names the real-app base through a placeholder, so TestHostCredentialGuardTests (which scans test sources
    // for real host factories) does not read this TEXT as a host that must stub its credential.
    private static readonly string ProvenFixtureFile = """
        public sealed class AppFactory : __REAL_APP_BASE__<Program> { }
        public sealed class ProvenTests : IClassFixture<AppFactory>
        {
            [Fact]
            public async Task Proves()
            {
                var denied = await _client.GetAsync($"/api/agent/playbooks/status/{other}");
                denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
                var allowed = await _client.GetAsync($"/api/agent/playbooks/status/{mine}");
                allowed.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            [Fact]
            public async Task OtherRoute()
            {
                var denied = await _client.GetAsync($"/api/agent/other/{other}");
                denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await _client.GetAsync($"/api/agent/other/{mine}")).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            [Fact]
            public async Task RefusalOnly()
            {
                (await _client.GetAsync($"/api/agent/playbooks/status/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
        }

        public sealed class SlimTests
        {
            [Fact]
            public async Task Slim()
            {
                await using var host = await SlimHost.StartAsync();
                (await host.Client.GetAsync($"/api/agent/playbooks/status/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await host.Client.GetAsync($"/api/agent/playbooks/status/{mine}")).StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
        """.Replace("__REAL_APP_BASE__", "WebApplication" + "Factory", StringComparison.Ordinal);

    [Fact(DisplayName = "Round 65 item 2 controls: ProvenByTest holds only for a real-app test of THIS route asserting refusal AND success")]
    public void ProvenByTest_NegativeControl_EachMissingPartFails()
    {
        const string route = "GET /api/agent/playbooks/status/{jobId:guid}";
        var bases = TypeBasesOf(new[] { ProvenFixtureFile, "public sealed class SlimHost : IAsyncDisposable { }" });

        Assert.Null(ProvenByTestProblem(ProvenFixtureFile, "Proves", route, bases));
        Assert.Contains("names no request pairing GET", ProvenByTestProblem(ProvenFixtureFile, "OtherRoute", route, bases), StringComparison.Ordinal);
        Assert.Contains("asserts no success", ProvenByTestProblem(ProvenFixtureFile, "RefusalOnly", route, bases), StringComparison.Ordinal);
        Assert.Contains("does not drive the real app", ProvenByTestProblem(ProvenFixtureFile, "Slim", route, bases), StringComparison.Ordinal);
        Assert.Contains("no method", ProvenByTestProblem(ProvenFixtureFile, "Missing", route, bases), StringComparison.Ordinal);

        // An entry flagged ProvenByTest without ResolvedBy, or whose ProofTest does not exist, never holds.
        var unresolved = new SweepFinding("S-00", Severity.Low, route, "164", Gap.NoDecision) { ProvenByTest = true };
        Assert.NotEmpty(ProvenByTestViolations(unresolved, _ => ProvenFixtureFile, bases));
        var missingFile = unresolved with { ResolvedBy = "164", ProofTest = "tests/none/Missing.cs::Proves" };
        Assert.Contains(ProvenByTestViolations(missingFile, _ => null, bases), v => v.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Round 65 item 2: every ProvenByTest entry's credit holds, and Rule A counts exactly those routes")]
    public void EveryProvenByTestCreditHolds()
    {
        var flagged = SweepFindings.Where(e => e.ProvenByTest).ToList();
        var violations = flagged.SelectMany(e => ProvenByTestViolations(e, ReadRepoFile)).ToList();
        Assert.True(violations.Count == 0, string.Join("\n", violations));
        Assert.Equal(flagged.Select(e => e.Route).OrderBy(r => r, StringComparer.Ordinal),
            ProvenRoutes.Value.OrderBy(r => r, StringComparer.Ordinal));
    }
}
