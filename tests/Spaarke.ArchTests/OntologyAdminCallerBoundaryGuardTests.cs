using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarke-ontology-platform-r1 task 104 (fix rounds K2, then the second adversarial pass F3/K3/K4): the "writes are authorized as
/// the caller" promise of the Ontology admin route group (D-22, FR-56), made executable. It was only a comment in
/// <c>OntologyAdminEndpoints.cs</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a guard.</b> The Console shows the "Ontology admin" tab from the capability probe's answer. That answer is display
/// data: if any server code read it (or its cache entry) to decide whether a write may proceed, the probe would become the
/// security boundary, which D-22 forbids, and a stale or forged answer would grant access. The same promise breaks if an admin
/// route writes through an application-identity Dataverse client, because Dataverse would then enforce the BFF's rights instead
/// of the human's, and the audit trail would name the service principal.</para>
/// <para><b>Rule A - nothing outside the probe file touches the capability answer.</b> The probe, its per-caller cache and the
/// <c>OntologyAdminCapabilities</c> record live in <c>OntologyAdminCapabilityProbe.cs</c>, the only exempt file. Every other
/// <c>src/server/**</c> file (the route file <c>OntologyAdminEndpoints.cs</c> included, and the tasks 100/101 routes that will be
/// added to it) is scanned with comments stripped:</para>
/// <list type="bullet">
/// <item>Everywhere: no <c>OntologyAdminCapabilities</c>, <c>OntologyAdminCapabilityProbe</c> or <c>ontology-admin-capability</c>.</item>
/// <item>In the Ontology files (rule B's scope): also no <c>ProbeAsync</c>, <c>TryGetCachedAsync</c>, <c>TrySetCachedAsync</c>,
/// <c>CacheLifetime</c>, <c>CacheResource</c>, <c>CacheVersion</c>, <c>MaxCacheLifetime</c>, <c>PrivilegeQuery</c> or
/// <c>AuthorPoliciesPrivilege</c>.</item>
/// <item>The only exceptions, both in <c>OntologyAdminEndpoints.cs</c> and each at most ONCE: the single entry-point call
/// <c>OntologyAdminCapabilityProbe.ResolveAsync(</c> made by the capability handler, and the OpenAPI metadata
/// <c>Produces&lt;OntologyAdminCapabilities&gt;(</c>. A second call, or any other member, is a violation.</item>
/// </list>
/// <para>Tests are not scanned: they are the endpoint's own tests.</para>
/// <para><b>Rule B - the group's files use the caller's identity.</b> In every server file under <c>Api/Ontology/</c>, whose name
/// starts <c>OntologyAdmin</c>, or that declares the <c>/api/v1/ontology/admin</c> group, no application-identity Dataverse type
/// (<see cref="AppIdentityDataverseTypes"/>) is named. The route's Dataverse access is <c>IDataverseUserClient</c> (the on-behalf-of
/// client); the route file must still name it, so the rule cannot pass vacuously. The ban is on DIRECT use of those client types
/// in an admin file. It is not a ban on every app-identity read: a service such as <c>SignalEvaluator</c> that reads on the
/// application's behalf is not named here and is not banned.</para>
/// <para><b>Not enforced.</b> An admin route file outside <c>Api/Ontology/</c> that receives the group as a parameter and has neither
/// the group literal nor an <c>OntologyAdmin*</c> name, and reflection or <c>dynamic</c> access.</para>
/// <para><b>Controls.</b> Must-fire: planted violations of each rule (fixture strings), including a second probe or cache call in
/// route-file text. Must-not-fire: the real tree passes, and the sanctioned shapes (the probe file, a comment, the mapping extension,
/// the one entry-point call, the caller's client) are not flagged.</para>
/// </remarks>
public class OntologyAdminCallerBoundaryGuardTests
{
    private const string ProbeFileName = "OntologyAdminCapabilityProbe.cs";
    private const string RouteFileName = "OntologyAdminEndpoints.cs";
    private const string GroupLiteral = "/api/v1/ontology/admin";
    private const string ProbePath = "src/server/api/Sprk.Bff.Api/Api/Ontology/" + ProbeFileName;
    private const string RoutePath = "src/server/api/Sprk.Bff.Api/Api/Ontology/" + RouteFileName;

    /// <summary>Application-identity Dataverse access: the BFF's own rights, not the caller's.</summary>
    private static readonly string[] AppIdentityDataverseTypes =
    {
        "IDataverseService",
        "IGenericEntityService",
        "DataverseWebApiClient",
        "DataverseServiceClientImpl",
        "ServiceClient",
    };

    /// <summary>Named nowhere outside the probe file, in any server file.</summary>
    private static readonly Regex GlobalCapabilityTokens = new(
        @"\bOntologyAdminCapabilities\b|\bOntologyAdminCapabilityProbe\b|ontology-admin-capability",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Probe internals, named nowhere outside the probe file in the Ontology files (generic words elsewhere).</summary>
    private static readonly Regex ScopedProbeTokens = new(
        @"\b(?:ProbeAsync|TryGetCachedAsync|TrySetCachedAsync|CacheLifetime|CacheResource|CacheVersion|MaxCacheLifetime|PrivilegeQuery|AuthorPoliciesPrivilege)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The one sanctioned call into the probe, and the one OpenAPI mention of the record, in the route file.</summary>
    private static readonly Regex ProbeCall = new(
        @"\bOntologyAdminCapabilityProbe\.ResolveAsync\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProducesMetadata = new(
        @"\.Produces<OntologyAdminCapabilities>\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AppIdentityClient = new(
        @"\b(?:" + string.Join("|", AppIdentityDataverseTypes) + @")\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact(DisplayName = "Task 104 rule A: no server code outside the probe file reads the capability answer or its cache entry, or calls the probe twice")]
    public void NothingOutsideTheProbeFileReadsTheCapabilityAnswer()
    {
        var violations = new List<string>();
        var sawProbeFile = false;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            if (string.Equals(Path.GetFileName(file), ProbeFileName, StringComparison.Ordinal))
            {
                sawProbeFile = true;
            }

            violations.AddRange(FindCapabilityReferences(Relative(file), File.ReadAllLines(file)));
        }

        // Non-vacuity: the one exempt file still exists under the name this guard matches on.
        Assert.True(sawProbeFile, $"{ProbeFileName} was not found under src/server; the guard's file name is stale.");
        Assert.True(
            violations.Count == 0,
            "Task 104 violation (D-22): the Ontology admin capability answer is display data, never an authorization input. " +
            $"Only {ProbeFileName} may touch the probe, its cache entry or OntologyAdminCapabilities; the route file reaches it " +
            "through ONE call, OntologyAdminCapabilityProbe.ResolveAsync, made by the capability handler. Authorize every write " +
            "as the caller (IDataverseUserClient) and let Dataverse decide. Violations: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "Task 104 rule A non-vacuity: the route file reaches the probe through exactly one ResolveAsync call")]
    public void TheRouteFileMakesExactlyOneProbeCall()
    {
        var routeFile = SourceScan.ServerSourceFiles()
            .Single(f => string.Equals(Path.GetFileName(f), RouteFileName, StringComparison.Ordinal));
        var calls = File.ReadAllLines(routeFile).Sum(l => ProbeCall.Matches(SourceScan.StripLineComment(l)).Count);

        Assert.Equal(1, calls);
    }

    [Fact(DisplayName = "Task 104 rule B: the Ontology admin files take IDataverseUserClient, never a direct app-identity Dataverse client")]
    public void TheAdminGroupUsesTheCallersDataverseClient()
    {
        var violations = new List<string>();
        var inScope = new List<string>();
        var routeFileNamesUserClient = false;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var relative = Relative(file);
            if (!IsOntologyAdminFile(relative, lines))
            {
                continue;
            }

            inScope.Add(relative);
            violations.AddRange(FindAppIdentityClients(relative, lines));

            if (string.Equals(Path.GetFileName(file), RouteFileName, StringComparison.Ordinal)
                && lines.Any(l => Regex.IsMatch(SourceScan.StripLineComment(l), @"\bIDataverseUserClient\b")))
            {
                routeFileNamesUserClient = true;
            }
        }

        // Non-vacuity: the route file and the probe file are in scope, and the route file really does take the caller's client.
        Assert.Contains(inScope, p => p.EndsWith("/" + RouteFileName, StringComparison.Ordinal));
        Assert.Contains(inScope, p => p.EndsWith("/" + ProbeFileName, StringComparison.Ordinal));
        Assert.True(routeFileNamesUserClient, $"{RouteFileName} no longer names IDataverseUserClient; the rule would pass vacuously.");
        Assert.True(
            violations.Count == 0,
            "Task 104 violation (D-22, FR-56): an Ontology admin file must not name an application-identity Dataverse client " +
            "type directly. Admin routes run Dataverse access AS THE CALLER (IDataverseUserClient), so Dataverse enforces the " +
            "Spaarke Ontology Administrator role and the audit names the human; an application-identity client would enforce the " +
            "BFF's rights instead. (This bans the client TYPES in these files, not every app-identity read: evaluator reads go " +
            "through SignalEvaluator, which is not banned.) Violations: " + string.Join("; ", violations));
    }

    // ---------------------------------------------------------------------------------------------
    // Controls. A detector that silently stops matching must fail here, not pass vacuously.
    // ---------------------------------------------------------------------------------------------

    [Theory(DisplayName = "Task 104 control (must fire): rule A flags a planted read of the capability answer or its cache entry in any file")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Planted/Writer.cs", "var c = new OntologyAdminCapabilities(true);")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Planted/Writer.cs", "var v = OntologyAdminCapabilityProbe.CacheResource;")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Planted/Writer.cs", "var r = await cache.GetAsync<bool>(tenant, \"ontology-admin-capability\", oid, 1);")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Planted/Writer.cs", "var r = await OntologyAdminCapabilityProbe.ResolveAsync(p, u, c, t, l, ct);")]
    public void RuleAFiresOnAPlantedViolationInAnyFile(string path, string plantedLine)
    {
        Assert.NotEmpty(FindCapabilityReferences(path, new[] { plantedLine }));
    }

    [Theory(DisplayName = "Task 104 control (must fire): rule A flags a second probe or cache call planted in the route file")]
    [InlineData("var again = await OntologyAdminCapabilityProbe.ResolveAsync(p, u, c, t, l, ct);")]
    [InlineData("var gate = await ProbeAsync(user, logger, ct);")]
    [InlineData("var hit = await TryGetCachedAsync(cache, tenantId, objectId, logger, ct);")]
    [InlineData("await TrySetCachedAsync(cache, tenantId, objectId, body, ttl, logger, ct);")]
    [InlineData("var key = CacheResource;")]
    [InlineData("var ttl = CacheLifetime(principal, now);")]
    [InlineData("var body = new OntologyAdminCapabilities(true);")]
    public void RuleAFiresOnASecondProbeUseInTheRouteFile(string plantedLine)
    {
        var routeFile = new[]
        {
            "var first = await OntologyAdminCapabilityProbe.ResolveAsync(httpContext.User, user, cache, clock, logger, ct);",
            ".Produces<OntologyAdminCapabilities>(200)",
            plantedLine,
        };

        var found = FindCapabilityReferences(RoutePath, routeFile);

        Assert.Single(found);
        Assert.Contains(":3 ", found[0]);
    }

    [Fact(DisplayName = "Task 104 control (must fire): a second Produces mention of the record in the route file is flagged")]
    public void RuleAFiresOnASecondProducesMention()
    {
        var routeFile = new[] { ".Produces<OntologyAdminCapabilities>(200)", ".Produces<OntologyAdminCapabilities>(201)" };

        Assert.Single(FindCapabilityReferences(RoutePath, routeFile));
    }

    [Fact(DisplayName = "Task 104 control (must not fire): rule A leaves the route file's one call, the probe file, comments and the mapping extension alone")]
    public void RuleADoesNotFireOnSanctionedShapes()
    {
        // the route file as it should look: one entry-point call, the OpenAPI metadata, comments naming the probe
        Assert.Empty(FindCapabilityReferences(RoutePath, new[]
        {
            "/// <see cref=\"OntologyAdminCapabilityProbe\"/> holds the probe and its cache",
            "// OntologyAdminCapabilityProbe.ResolveAsync is the only entry point; ProbeAsync stays private to the probe file",
            ".Produces<OntologyAdminCapabilities>(200)",
            "var resolution = await OntologyAdminCapabilityProbe.ResolveAsync(httpContext.User, user, cache, clock, logger, ct);",
            "case OntologyAdminProbeOutcome.CallerUnresolved:",
        }));

        // the probe file itself is exempt
        Assert.Empty(FindCapabilityReferences(ProbePath, new[]
        {
            "internal const string CacheResource = \"ontology-admin-capability\";",
            "var probe = await ProbeAsync(user, logger, ct);",
            "public sealed record OntologyAdminCapabilities(bool CanAuthorPolicies);",
        }));

        // other files: comments, the mapping extension, a similarly named type, generic words outside the Ontology files
        Assert.Empty(FindCapabilityReferences("src/server/api/Sprk.Bff.Api/Services/Other.cs", new[]
        {
            "/// see <see cref=\"OntologyAdminCapabilities\"/> for the probe",
            "// the OntologyAdminCapabilityProbe answer is display data",
            "var x = new OntologyAdminRuleEndpoints();",
            "var ttl = CacheLifetime(principal, now);",
            "var again = await ProbeAsync(thing, ct);",
        }));
        Assert.Empty(FindCapabilityReferences(
            "src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs", new[] { "app.MapOntologyAdminEndpoints();" }));
    }

    [Fact(DisplayName = "Task 104 control (must fire): a probe internal named in ANOTHER Ontology route file is flagged, and the entry point is allowed only in the route file")]
    public void RuleAScopesProbeInternalsToTheOntologyFiles()
    {
        const string otherRoutes = "src/server/api/Sprk.Bff.Api/Api/Ontology/OntologyRulesEndpoints.cs";

        Assert.Single(FindCapabilityReferences(otherRoutes, new[] { "var gate = await ProbeAsync(user, logger, ct);" }));
        Assert.Single(FindCapabilityReferences(
            otherRoutes, new[] { "var a = await OntologyAdminCapabilityProbe.ResolveAsync(p, u, c, t, l, ct);" }));
    }

    [Theory(DisplayName = "Task 104 control (must fire): rule B flags a planted app-identity client in an admin route file")]
    [InlineData("IDataverseService")]
    [InlineData("IGenericEntityService")]
    [InlineData("DataverseWebApiClient")]
    [InlineData("ServiceClient")]
    public void RuleBFiresOnAPlantedViolation(string appIdentityType)
    {
        var planted = new[]
        {
            "app.MapGroup(\"/api/v1/ontology/admin\");",
            $"static Task<IResult> Save(HttpContext ctx, {appIdentityType} dataverse, CancellationToken ct) => null!;",
        };

        Assert.True(IsOntologyAdminFile("src/server/api/Sprk.Bff.Api/Api/Ontology/SomeRules.cs", planted));
        Assert.Single(FindAppIdentityClients("src/server/api/Sprk.Bff.Api/Api/Ontology/SomeRules.cs", planted));
    }

    [Fact(DisplayName = "Task 104 control: rule B scope covers every Api/Ontology file, OntologyAdmin* names and the group literal, and nothing else")]
    public void RuleBScopeIsTheOntologyFiles()
    {
        Assert.True(IsOntologyAdminFile("src/server/api/Sprk.Bff.Api/Api/Ontology/AnythingEndpoints.cs", new[] { "// no markers" }));
        Assert.True(IsOntologyAdminFile("src/server/api/Sprk.Bff.Api/Api/OntologyAdminRulesEndpoints.cs", new[] { "// passed-in group" }));
        Assert.True(IsOntologyAdminFile("src/server/api/Sprk.Bff.Api/Api/Other.cs", new[] { "var g = \"/api/v1/ontology/admin\";" }));
        Assert.False(IsOntologyAdminFile("src/server/api/Sprk.Bff.Api/Api/UnrelatedEndpoints.cs", new[] { "var s = serviceClient;" }));
    }

    [Fact(DisplayName = "Task 104 control (must not fire): rule B leaves the caller's client and comments alone")]
    public void RuleBDoesNotFireOnTheCallersClient()
    {
        var sanctioned = new[]
        {
            "app.MapGroup(\"/api/v1/ontology/admin\");",
            "static Task<IResult> Save(HttpContext ctx, IDataverseUserClient user, CancellationToken ct) => null!;",
            "// never IDataverseService here: writes run as the caller",
            "var transportClient = 1; // ServiceClient mentioned only in a comment",
        };

        Assert.Empty(FindAppIdentityClients("src/server/api/Sprk.Bff.Api/Api/Ontology/SomeRules.cs", sanctioned));
    }

    // ---------------------------------------------------------------------------------------------
    // Detectors (pure over lines, so the controls drive exactly what the real scan runs).
    // ---------------------------------------------------------------------------------------------

    private static List<string> FindCapabilityReferences(string relativePath, IReadOnlyList<string> lines)
    {
        var found = new List<string>();
        var fileName = Path.GetFileName(relativePath);
        if (string.Equals(fileName, ProbeFileName, StringComparison.Ordinal))
        {
            return found;
        }

        var isRouteFile = string.Equals(fileName, RouteFileName, StringComparison.Ordinal);
        var scoped = IsOntologyAdminFile(relativePath, lines);
        var callBudget = isRouteFile ? 1 : 0;
        var producesBudget = isRouteFile ? 1 : 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);

            // The two sanctioned mentions, each at most once: spend the budget, blank the text. Any further occurrence stays in
            // the line and trips the token scan below.
            code = Spend(ProbeCall, code, ref callBudget);
            code = Spend(ProducesMetadata, code, ref producesBudget);

            var match = GlobalCapabilityTokens.Match(code);
            if (!match.Success && scoped)
            {
                match = ScopedProbeTokens.Match(code);
            }

            if (match.Success)
            {
                found.Add($"{relativePath}:{i + 1} ({match.Value})");
            }
        }

        return found;
    }

    private static string Spend(Regex allowance, string code, ref int budget)
    {
        if (budget == 0)
        {
            return code;
        }

        var remaining = budget;
        var result = allowance.Replace(code, m => remaining-- > 0 ? string.Empty : m.Value);
        budget = remaining;
        return result;
    }

    private static List<string> FindAppIdentityClients(string relativePath, IReadOnlyList<string> lines)
    {
        var found = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var match = AppIdentityClient.Match(SourceScan.StripLineComment(lines[i]));
            if (match.Success)
            {
                found.Add($"{relativePath}:{i + 1} ({match.Value})");
            }
        }

        return found;
    }

    /// <summary>The Ontology admin files: everything under Api/Ontology/, any OntologyAdmin* name, any file declaring the group.</summary>
    private static bool IsOntologyAdminFile(string relativePath, IReadOnlyList<string> lines) =>
        relativePath.Contains("/Api/Ontology/", StringComparison.Ordinal)
        || Path.GetFileName(relativePath).StartsWith("OntologyAdmin", StringComparison.Ordinal)
        || lines.Any(l => SourceScan.StripLineComment(l).Contains(GroupLiteral, StringComparison.Ordinal));

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');
}
