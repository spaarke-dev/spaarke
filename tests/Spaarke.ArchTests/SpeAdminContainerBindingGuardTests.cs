using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165, owner round 20 items 1-3 — three source guards that keep the container →
/// business-unit binding whole as the SPE admin plane grows.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><b>Every container the BFF creates is stamped at creation.</b> Each <c>FileStorage.Containers…PostAsync(</c>
///   in <c>Sprk.Bff.Api</c> sits in a method that binds the new container (<c>BindNewContainerAsync(</c>) — a creation
///   path without it would mint unbound containers that only a root-unit admin reaches and the backfill must chase.</item>
///   <item><b>Every app-only container-TYPE route carries the type rule.</b> Narrowing the identity check (round 20 item
///   3) lets configs of different customers share a type and owning app; an app-only <c>/containertypes/{typeId}</c>
///   route acting as that shared app without <c>.WithSpeAdminContainerTypeScope(</c> could change what every customer of
///   the type shares. Routes that act with the CALLER's own delegated token are listed, with the reason.</item>
///   <item><b>The backfill script and the BFF agree</b> on the property name, the authoritative sources and the audit
///   operation (<c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1</c>).</item>
/// </list>
/// Each guard's analyser is exercised on seeded snippets too, so a regression in the guard itself reddens.
/// </remarks>
public sealed class SpeAdminContainerBindingGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Stamped at creation
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Regex ContainerCreate = new(@"FileStorage\s*\.\s*Containers\s*\.\s*PostAsync\s*\(", RegexOptions.Compiled);

    [Fact(DisplayName = "Every SPE container the BFF creates is bound to its business unit in the creating method")]
    public void EveryContainerCreationPath_BindsTheNewContainer()
    {
        var bffRoot = Path.Combine(RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        var creators = Directory.EnumerateFiles(bffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (File: f, Violations: UnboundCreations(File.ReadAllText(f))))
            .ToList();

        Assert.True(
            creators.Count(c => ContainerCreate.IsMatch(StripComments(File.ReadAllText(c.File)))) >= 2,
            "the scan must find the two creation paths (SpeAdminGraphService, ContainerOperations), or it passes vacuously");

        var violations = creators.Where(c => c.Violations.Count > 0)
            .SelectMany(c => c.Violations.Select(v => $"{Path.GetRelativePath(RepoRoot, c.File)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "These methods create an SPE container without binding it to its owning business unit (owner round 20 item 1; " +
            "call BindNewContainerAsync in the same method):\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "The creation analyser flags a seeded unbound creation and passes a bound one")]
    public void TheCreationAnalyser_BitesOnASeededUnboundCreation()
    {
        const string bound = """
            public async Task<X> CreateAsync()
            {
                var c = await graphClient.Storage.FileStorage.Containers.PostAsync(body, cancellationToken: ct);
                await BindNewContainerAsync(graphClient, c.Id, unit, ct);
                return c;
            }
            """;
        const string unbound = """
            public async Task<X> CreateAsync()
            {
                var c = await graphClient.Storage.FileStorage.Containers
                    .PostAsync(body, cancellationToken: ct);
                return c;
            }
            """;

        Assert.Empty(UnboundCreations(bound));
        Assert.NotEmpty(UnboundCreations(unbound));
    }

    /// <summary>For each container creation, the enclosing method body must call <c>BindNewContainerAsync(</c>.</summary>
    private static List<string> UnboundCreations(string source)
    {
        var code = StripComments(source);
        var violations = new List<string>();
        foreach (Match m in ContainerCreate.Matches(code))
        {
            var body = EnclosingMethodBody(code, m.Index);
            if (!body.Contains("BindNewContainerAsync(", StringComparison.Ordinal))
            {
                violations.Add($"creation at offset {m.Index} is not bound in its method");
            }
        }

        return violations;
    }

    /// <summary>The brace-balanced body of the member declaration that precedes <paramref name="index"/>.</summary>
    private static string EnclosingMethodBody(string code, int index)
    {
        var header = Regex.Matches(code, @"^[ \t]*(?:public|private|internal|protected)\b[^;{=]*\(", RegexOptions.Multiline)
            .LastOrDefault(h => h.Index < index);
        if (header is null)
        {
            return string.Empty;
        }

        var start = code.IndexOf('{', header.Index);
        if (start < 0)
        {
            return string.Empty;
        }

        var level = 0;
        for (var i = start; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                level++;
            }
            else if (code[i] == '}')
            {
                level--;
                if (level == 0)
                {
                    return code[start..(i + 1)];
                }
            }
        }

        return code[start..];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. App-only container-type routes carry the type rule
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>/containertypes/{typeId}…</c> routes that act with the CALLER's own delegated token (the <c>…ForUserAsync</c>
    /// Graph methods): Graph authorizes them by the caller's own Entra role, so they need no type rule. Key: "VERB template".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DelegatedTypeRoutes = new Dictionary<string, string>
    {
        ["GET /containertypes/{typeId}"] = "GetContainerTypeForUserAsync — the caller's delegated token",
        ["PUT /containertypes/{typeId}/settings"] = "UpdateContainerTypeSettingsForUserAsync — the caller's delegated token",
        ["GET /containertypes/{typeId}/owners"] = "ListContainerTypeOwnersForUserAsync — the caller's delegated token",
        ["POST /containertypes/{typeId}/owners"] = "AddContainerTypeOwnerForUserAsync — the caller's delegated token",
        ["DELETE /containertypes/{typeId}/owners/{permissionId}"] = "RemoveContainerTypeOwnerForUserAsync — the caller's delegated token",
    };

    private static readonly Regex MapCall = new(
        @"\.Map(?<verb>Get|Post|Put|Patch|Delete)\s*\(\s*""(?<route>[^""]*\{typeId\}[^""]*)""(?<chain>[^;]*);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact(DisplayName = "Every app-only /containertypes/{typeId} route is marked with the container-type rule")]
    public void EveryAppOnlyContainerTypeRoute_CarriesTheTypeRule()
    {
        var dir = Path.Combine(RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Api", "SpeAdmin");
        var routes = Directory.EnumerateFiles(dir, "*.cs")
            .SelectMany(f => TypeRoutes(File.ReadAllText(f)))
            .ToList();

        Assert.True(routes.Count >= 9, $"the scan must find the container-type routes (found {routes.Count}), or it passes vacuously");

        var unmarked = routes
            .Where(r => !r.Marked && !DelegatedTypeRoutes.ContainsKey(r.Key))
            .Select(r => r.Key)
            .ToList();

        Assert.True(unmarked.Count == 0,
            "These app-only container-type routes lack .WithSpeAdminContainerTypeScope(...) — with Model 1 configs sharing a " +
            "type and owning app (owner round 20 item 3) they could change what every customer of the type shares:\n  " +
            string.Join("\n  ", unmarked));

        var stale = DelegatedTypeRoutes.Keys.Where(k => !routes.Any(r => r.Key == k)).ToList();
        Assert.True(stale.Count == 0, "Delegated-route entries naming no route — delete them: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "The type-route analyser flags a seeded unmarked app-only route")]
    public void TheTypeRouteAnalyser_BitesOnASeededUnmarkedRoute()
    {
        const string marked = """
            group.MapPost("/containertypes/{typeId}/consumers", RegisterConsumerAsync)
                .WithSpeAdminContainerTypeScope(SpeAdminContainerTypeOperation.Write)
                .WithName("x");
            """;
        const string unmarked = """
            group.MapPost("/containertypes/{typeId}/consumers", RegisterConsumerAsync)
                .WithName("x");
            """;

        Assert.True(TypeRoutes(marked).Single().Marked);
        Assert.False(TypeRoutes(unmarked).Single().Marked);
    }

    private static IEnumerable<(string Key, bool Marked)> TypeRoutes(string source) =>
        MapCall.Matches(StripComments(source)).Select(m => (
            Key: $"{m.Groups["verb"].Value.ToUpperInvariant()} {m.Groups["route"].Value}",
            Marked: m.Groups["chain"].Value.Contains(".WithSpeAdminContainerTypeScope(", StringComparison.Ordinal)));

    // ─────────────────────────────────────────────────────────────────────────
    // 3. The backfill script agrees with the BFF
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "The backfill script stamps the property the BFF reads, from the sources the BFF writes")]
    public void TheBackfillScript_AgreesWithTheBff()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Backfill-SpeContainerBusinessUnitStamp.ps1"));

        Assert.Equal(SpeContainerBusinessUnitStamp.PropertyName, ScriptVariable(script, "StampProperty"));

        // The admin-plane create writes this audit operation (ContainerEndpoints) — the script's third source.
        var endpoints = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Api", "SpeAdmin", "ContainerEndpoints.cs"));
        var operation = ScriptVariable(script, "CreateContainerOperation");
        Assert.Contains($"operation: \"{operation}\"", endpoints);

        // The secure roots whose own container the script reads are exactly the roots provisioning creates one for.
        var scriptSets = Regex.Matches(script, @"^\s*'(?<set>sprk_\w+)'\s*=\s*'sprk_\w+id'", RegexOptions.Multiline)
            .Select(m => m.Groups["set"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(SecureRootEntitySets(), scriptSets);
    }

    [Fact(DisplayName = "The script-variable reader reads a seeded assignment")]
    public void TheScriptVariableReader_ReadsASeededAssignment()
    {
        Assert.Equal("abc", ScriptVariable("$Other = 'x'\n$StampProperty = 'abc'\n", "StampProperty"));
        Assert.Null(ScriptVariable("$Other = 'x'\n", "StampProperty"));
    }

    private static string? ScriptVariable(string script, string name)
    {
        var m = Regex.Match(script, $@"^\s*\${name}\s*=\s*'(?<value>[^']*)'", RegexOptions.Multiline);
        return m.Success ? m.Groups["value"].Value : null;
    }

    private static HashSet<string> SecureRootEntitySets()
    {
        var rootType = typeof(SpeContainerBusinessUnitStamp).Assembly.GetType("Sprk.Bff.Api.Api.ExternalAccess.SecureRecordRoot")
            ?? throw new InvalidOperationException("SecureRecordRoot not found — update this guard.");

        return rootType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == rootType)
            .Select(f => (string)rootType.GetProperty("EntitySet")!.GetValue(f.GetValue(null))!)
            .ToHashSet(StringComparer.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string StripComments(string source) =>
        Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
