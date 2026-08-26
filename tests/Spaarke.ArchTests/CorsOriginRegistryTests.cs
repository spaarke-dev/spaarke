using System.Text.Json;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Arch-fitness guards over the <c>requiredCorsOrigins</c> declaration in
/// <c>config/environments.json</c> — the CORS half of the deploy-time forcing function added by
/// <c>spaarke-auth-v4-dataverse-MI</c> task 090.
///
/// <para><b>Why a declaration needs its own guard.</b> The runtime gate lives in
/// <c>Deploy-BffApi.ps1</c> and asserts that every declared origin is present on the target App
/// Service. That gate is only as good as the declaration it reads: a malformed, empty or
/// wildcard-bearing entry makes it pass vacuously while appearing to protect something. These tests
/// guard the input, not the environment — the environment is not reachable from CI and must not be.</para>
///
/// <para><b>What this deliberately does NOT do.</b> It does not contact Azure and does not assert
/// anything about live app settings. Those belong to the deploy-time gate, where the environment is
/// actually in hand. A CI test that tried would either need deployment credentials or would silently
/// no-op — and a silently no-opping gate is worse than an absent one, because it manufactures
/// confidence. Same reasoning as <see cref="CredentialCensusTests"/>.</para>
///
/// <para><b>Origin</b>: commit <c>66a45cf6a</c> removed the blanket credentialed
/// <c>*.azurestaticapps.net</c> CORS rule — correct, because that is an attacker-registrable shared
/// domain — and named a DEPLOYMENT PREREQ to enumerate our own SWA origins explicitly. Nobody ran it,
/// nothing failed, and the gap surfaced only when a human opened the Outlook add-in during UAT.</para>
/// </summary>
public class CorsOriginRegistryTests
{
    private const string ConfigRelativePath = "config/environments.json";

    /// <summary>Suffixes CorsModule matches in code. Declaring one of these adds noise, not protection.</summary>
    private static readonly string[] CodeMatchedSuffixes =
    {
        ".dynamics.com",
        ".powerapps.com",
        ".powerappsportals.com"
    };

    /// <summary>
    /// The origin-shape rule, extracted so it can be exercised directly by the negative and positive
    /// controls below. Per <c>tests/CLAUDE.md</c>, every guard on this path must prove it fires on a
    /// seeded violation and does NOT fire on the sanctioned shape — a detector nobody has seen fail is
    /// a detector nobody knows works, and a guard that flags the thing it protects gets deleted rather
    /// than obeyed.
    /// </summary>
    private static List<string> ValidateOrigin(string? origin)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(origin))
        {
            problems.Add("empty origin entry");
            return problems;
        }

        if (origin.Contains('*'))
            problems.Add($"'{origin}' contains a wildcard — CorsModule rejects '*' outright, and blanket " +
                         "suffix matching on a shared domain is what 66a45cf6a removed");

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            problems.Add($"'{origin}' is not an absolute URI");
            return problems;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
            problems.Add($"'{origin}' is not https");

        if (origin.EndsWith('/') || uri.AbsolutePath != "/")
            problems.Add($"'{origin}' must be scheme+host only, with no path and no trailing slash — " +
                         "CorsModule matches the Origin header by exact string");

        return problems;
    }

    /// <summary>NEGATIVE CONTROL — every malformed shape the rule exists to catch must actually be caught.</summary>
    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "whitespace")]
    [InlineData("https://*.azurestaticapps.net", "wildcard — the exact shape 66a45cf6a removed")]
    [InlineData("http://icy-desert-0bfdbb61e.6.azurestaticapps.net", "http, not https")]
    [InlineData("icy-desert-0bfdbb61e.6.azurestaticapps.net", "no scheme — not an absolute URI")]
    [InlineData("https://icy-desert-0bfdbb61e.6.azurestaticapps.net/", "trailing slash — never matches an Origin header")]
    [InlineData("https://icy-desert-0bfdbb61e.6.azurestaticapps.net/taskpane.html", "has a path")]
    public void ValidateOrigin_GivenMalformedOrigin_ReportsAProblem(string origin, string why)
    {
        var problems = ValidateOrigin(origin);
        Assert.True(problems.Count > 0,
            $"the origin-shape detector FAILED TO FIRE on a deliberately malformed origin ('{origin}' — {why}). " +
            "A rule that does not fire here is not protecting the declaration.");
    }

    /// <summary>POSITIVE CONTROL — the sanctioned shape must pass cleanly, or the guard gets deleted rather than obeyed.</summary>
    [Theory]
    [InlineData("https://icy-desert-0bfdbb61e.6.azurestaticapps.net")]
    [InlineData("https://green-dune-0c4f1221e.7.azurestaticapps.net")]
    public void ValidateOrigin_GivenACanonicalOrigin_ReportsNoProblem(string origin)
    {
        var problems = ValidateOrigin(origin);
        Assert.True(problems.Count == 0,
            $"the detector fired on a VALID origin ('{origin}'): {string.Join("; ", problems)}. " +
            "A guard that flags the shape it exists to protect will be disabled, not obeyed.");
    }

    private static JsonElement LoadEnvironments()
    {
        var path = Path.Combine(SourceScan.RepoRoot, ConfigRelativePath);
        Assert.True(File.Exists(path),
            $"{ConfigRelativePath} is the source the Deploy-BffApi.ps1 CORS gate reads. If it moved, the gate " +
            "silently stops asserting anything — update both together.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("environments").Clone();
    }

    /// <summary>
    /// Every REAL environment (not the <c>_template</c>) declares the key. Absence is not "no origins" —
    /// it is "nobody has looked", and the deploy gate reports it as UNAUDITED for exactly that reason.
    /// </summary>
    [Fact]
    public void EveryEnvironment_DeclaresRequiredCorsOrigins()
    {
        var environments = LoadEnvironments();
        var missing = new List<string>();

        foreach (var env in environments.EnumerateObject())
        {
            if (env.Name.StartsWith('_')) continue; // _template is schema documentation, not an environment
            if (!env.Value.TryGetProperty("requiredCorsOrigins", out _))
                missing.Add(env.Name);
        }

        Assert.True(missing.Count == 0,
            "every environment must state its required CORS origins — an EMPTY ARRAY is a valid and " +
            "meaningful assertion ('this environment has no Static Web App front-ends'), but an ABSENT key " +
            "means the environment has never been audited. Environments missing the key: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// Each declared origin must be an absolute HTTPS scheme+host with no path, no wildcard and no
    /// trailing slash — because CorsModule compares origins by EXACT STRING MATCH
    /// (<c>allowedOrigins.Contains(origin)</c>). A trailing slash or a path silently never matches,
    /// which presents as an unexplained CORS rejection at runtime.
    /// </summary>
    [Fact]
    public void DeclaredOrigins_AreWellFormedHttpsOrigins()
    {
        var environments = LoadEnvironments();
        var problems = new List<string>();

        foreach (var env in environments.EnumerateObject())
        {
            if (!env.Value.TryGetProperty("requiredCorsOrigins", out var origins)) continue;
            if (origins.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{env.Name}: requiredCorsOrigins must be an array");
                continue;
            }

            foreach (var element in origins.EnumerateArray())
            {
                foreach (var problem in ValidateOrigin(element.GetString()))
                    problems.Add($"{env.Name}: {problem}");
            }
        }

        Assert.True(problems.Count == 0,
            "malformed entries make the deploy-time gate pass vacuously while appearing to protect " +
            "something. Findings:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Origins already matched by CorsModule's in-code suffix rules must NOT be declared. They cannot
    /// silently break, so listing them adds maintenance noise without adding protection — and a noisy
    /// gate is a gate that gets disabled.
    /// </summary>
    [Fact]
    public void DeclaredOrigins_ExcludeOriginsAlreadyMatchedInCode()
    {
        var environments = LoadEnvironments();
        var redundant = new List<string>();

        foreach (var env in environments.EnumerateObject())
        {
            if (!env.Value.TryGetProperty("requiredCorsOrigins", out var origins)) continue;
            if (origins.ValueKind != JsonValueKind.Array) continue;

            foreach (var element in origins.EnumerateArray())
            {
                var origin = element.GetString();
                if (string.IsNullOrWhiteSpace(origin)) continue;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) continue;

                foreach (var suffix in CodeMatchedSuffixes)
                {
                    if (uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        redundant.Add($"{env.Name}: '{origin}' (host matches '{suffix}', already allowed by CorsModule)");
                }
            }
        }

        Assert.True(redundant.Count == 0,
            "declare ONLY origins CorsModule does not already match. Redundant entries: " +
            Environment.NewLine + string.Join(Environment.NewLine, redundant));
    }

    /// <summary>
    /// The deploy-time gate must remain wired. If someone removes the assertion from
    /// <c>Deploy-BffApi.ps1</c>, the declaration above becomes decorative — checked-in, well-formed and
    /// enforcing nothing. That is the failure mode this project documents repeatedly: text that reads as
    /// authoritative while nothing acts on it.
    /// </summary>
    [Fact]
    public void DeployScript_StillAssertsTheDeclaredOrigins()
    {
        var deployScript = Path.Combine(SourceScan.RepoRoot, "scripts/Deploy-BffApi.ps1");
        Assert.True(File.Exists(deployScript), "scripts/Deploy-BffApi.ps1 not found");

        var content = File.ReadAllText(deployScript);

        Assert.True(content.Contains("requiredCorsOrigins", StringComparison.Ordinal),
            "Deploy-BffApi.ps1 is where the declaration is enforced. Without this read, config/environments.json " +
            "declares origins that nothing checks.");

        Assert.True(content.Contains("CORS ORIGIN CHECK FAILED", StringComparison.Ordinal),
            "the gate must still be able to FAIL. A check that only ever logs is not a forcing function — " +
            "this exact string was proven to fire by seeding an undeclared origin and observing exit code 1.");
    }
}
