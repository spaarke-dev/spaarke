using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarke-ontology-platform-r1 task 104 (fix round K2): the "writes are authorized as the caller" promise of the Ontology admin
/// route group (D-22, FR-56), made executable. Until now it was only a comment in <c>OntologyAdminEndpoints.cs</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a guard.</b> The Console shows the "Ontology admin" tab from the capability probe's answer. That answer is display
/// data: if any server code read it (or its cache entry) to decide whether a write may proceed, the probe would become the
/// security boundary, which D-22 forbids, and a stale or forged answer would grant access. The same promise breaks if an admin
/// route writes through an application-identity Dataverse client, because Dataverse would then enforce the BFF's rights instead
/// of the human's, and the audit trail would name the service principal.</para>
/// <para><b>Rule A - nothing reads the capability answer.</b> Outside <c>OntologyAdminEndpoints.cs</c> no <c>src/server/**</c>
/// code (comments excluded) names <c>OntologyAdminCapabilities</c>, <c>OntologyAdminEndpoints</c> (its cache key, resource name,
/// probe and privilege constants are all members of it), or the cache resource literal <c>ontology-admin-capability</c>. The
/// route mapping extension <c>MapOntologyAdminEndpoints</c> is a different identifier and stays allowed. Tests are not scanned:
/// they are the endpoint's own tests.</para>
/// <para><b>Rule B - the group's handlers use the caller's identity.</b> In every server file that declares the
/// <c>/api/v1/ontology/admin</c> group, or whose name starts <c>OntologyAdmin</c>, no application-identity Dataverse type
/// (<see cref="AppIdentityDataverseTypes"/>) appears. The route's Dataverse access is <c>IDataverseUserClient</c> (the on-behalf-of
/// client); the file the probe lives in must still name it, so the rule cannot pass vacuously.</para>
/// <para><b>Not enforced.</b> A future admin route file that receives the group as a parameter and has neither the group literal
/// nor an <c>OntologyAdmin</c> file name, and reflection or <c>dynamic</c> access. Name such a file <c>OntologyAdmin*</c>.</para>
/// <para><b>Controls.</b> Must-fire: planted violations of each rule (fixture strings) are detected. Must-not-fire: the real
/// tree passes, and the sanctioned shapes (the endpoint file itself, a comment, the mapping extension, the caller's client) are
/// not flagged.</para>
/// </remarks>
public class OntologyAdminCallerBoundaryGuardTests
{
    private const string EndpointFileName = "OntologyAdminEndpoints.cs";
    private const string GroupLiteral = "/api/v1/ontology/admin";

    /// <summary>Application-identity Dataverse access: the BFF's own rights, not the caller's.</summary>
    private static readonly string[] AppIdentityDataverseTypes =
    {
        "IDataverseService",
        "IGenericEntityService",
        "DataverseWebApiClient",
        "DataverseServiceClientImpl",
        "ServiceClient",
    };

    private static readonly Regex CapabilityReference = new(
        @"\bOntologyAdminCapabilities\b|\bOntologyAdminEndpoints\b|ontology-admin-capability",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AppIdentityClient = new(
        @"\b(?:" + string.Join("|", AppIdentityDataverseTypes) + @")\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact(DisplayName = "Task 104 K2 rule A: no server code outside OntologyAdminEndpoints.cs reads the capability answer or its cache entry")]
    public void NothingOutsideTheEndpointFileReadsTheCapabilityAnswer()
    {
        var violations = new List<string>();
        var sawEndpointFile = false;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            if (string.Equals(Path.GetFileName(file), EndpointFileName, StringComparison.Ordinal))
            {
                sawEndpointFile = true;
            }

            violations.AddRange(FindCapabilityReferences(Relative(file), File.ReadAllLines(file)));
        }

        // Non-vacuity: the file the rule protects still exists under the name this guard matches on.
        Assert.True(sawEndpointFile, $"{EndpointFileName} was not found under src/server; the guard's file name is stale.");
        Assert.True(
            violations.Count == 0,
            "Task 104 K2 violation (D-22): the Ontology admin capability answer is display data, never an authorization input. " +
            "Server code outside OntologyAdminEndpoints.cs must not read OntologyAdminCapabilities, its cache key or its " +
            "probe; authorize the write as the caller (IDataverseUserClient) and let Dataverse decide. Violations: " +
            string.Join("; ", violations));
    }

    [Fact(DisplayName = "Task 104 K2 rule B: the Ontology admin route group takes IDataverseUserClient, never an app-identity Dataverse client")]
    public void TheAdminGroupUsesTheCallersDataverseClient()
    {
        var violations = new List<string>();
        var inScope = new List<string>();
        var endpointNamesUserClient = false;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            if (!IsAdminGroupFile(Path.GetFileName(file), lines))
            {
                continue;
            }

            inScope.Add(Relative(file));
            violations.AddRange(FindAppIdentityClients(Relative(file), lines));

            if (string.Equals(Path.GetFileName(file), EndpointFileName, StringComparison.Ordinal)
                && lines.Any(l => Regex.IsMatch(SourceScan.StripLineComment(l), @"\bIDataverseUserClient\b")))
            {
                endpointNamesUserClient = true;
            }
        }

        // Non-vacuity: the group's file is in scope, and it really does take the caller's client.
        Assert.Contains(inScope, p => p.EndsWith("/" + EndpointFileName, StringComparison.Ordinal));
        Assert.True(endpointNamesUserClient, $"{EndpointFileName} no longer names IDataverseUserClient; the rule would pass vacuously.");
        Assert.True(
            violations.Count == 0,
            "Task 104 K2 violation (D-22, FR-56): an Ontology admin route must run Dataverse access AS THE CALLER " +
            "(IDataverseUserClient), so Dataverse enforces the Spaarke Ontology Administrator role and the audit names the human. " +
            "An application-identity client would enforce the BFF's rights instead. Violations: " + string.Join("; ", violations));
    }

    // ---------------------------------------------------------------------------------------------
    // Controls. A detector that silently stops matching must fail here, not pass vacuously.
    // ---------------------------------------------------------------------------------------------

    [Theory(DisplayName = "Task 104 K2 control (must fire): rule A flags a planted read of the capability answer or its cache entry")]
    [InlineData("var c = new OntologyAdminCapabilities(true);")]
    [InlineData("var v = OntologyAdminEndpoints.CacheResource;")]
    [InlineData("var r = await cache.GetAsync<bool>(tenant, \"ontology-admin-capability\", oid, 1);")]
    [InlineData("if (await OntologyAdminEndpoints.ProbeAsync(user, log, ct) is { CanAuthorPolicies: true }) { }")]
    public void RuleAFiresOnAPlantedViolation(string plantedLine)
    {
        var found = FindCapabilityReferences("src/server/api/Sprk.Bff.Api/Services/Planted/Writer.cs", new[] { plantedLine });

        Assert.Single(found);
    }

    [Theory(DisplayName = "Task 104 K2 control (must not fire): rule A leaves the sanctioned shapes alone")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/Ontology/OntologyAdminEndpoints.cs", "internal const string CacheResource = \"ontology-admin-capability\";")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Other.cs", "/// see <see cref=\"OntologyAdminCapabilities\"/> for the probe")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Other.cs", "// the OntologyAdminEndpoints probe is display data")]
    [InlineData("src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs", "app.MapOntologyAdminEndpoints();")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Other.cs", "var x = new OntologyAdminRuleEndpoints();")]
    public void RuleADoesNotFireOnSanctionedShapes(string path, string line)
    {
        Assert.Empty(FindCapabilityReferences(path, new[] { line }));
    }

    [Theory(DisplayName = "Task 104 K2 control (must fire): rule B flags a planted app-identity client in an admin route file")]
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

        Assert.True(IsAdminGroupFile("SomeRules.cs", planted));
        Assert.Single(FindAppIdentityClients("src/server/api/Sprk.Bff.Api/Api/Ontology/SomeRules.cs", planted));
    }

    [Fact(DisplayName = "Task 104 K2 control (must fire): a file named OntologyAdmin* is in scope even without the group literal")]
    public void RuleBScopeIncludesFilesNamedOntologyAdmin()
    {
        Assert.True(IsAdminGroupFile("OntologyAdminRulesEndpoints.cs", new[] { "// maps onto a group passed in" }));
        Assert.False(IsAdminGroupFile("UnrelatedEndpoints.cs", new[] { "var s = serviceClient;" }));
    }

    [Fact(DisplayName = "Task 104 K2 control (must not fire): rule B leaves the caller's client and comments alone")]
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
        if (string.Equals(Path.GetFileName(relativePath), EndpointFileName, StringComparison.Ordinal))
        {
            return found;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var match = CapabilityReference.Match(SourceScan.StripLineComment(lines[i]));
            if (match.Success)
            {
                found.Add($"{relativePath}:{i + 1} ({match.Value})");
            }
        }

        return found;
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

    private static bool IsAdminGroupFile(string fileName, IReadOnlyList<string> lines) =>
        fileName.StartsWith("OntologyAdmin", StringComparison.Ordinal)
        || lines.Any(l => SourceScan.StripLineComment(l).Contains(GroupLiteral, StringComparison.Ordinal));

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');
}
