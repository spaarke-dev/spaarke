using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165, owner rounds 20 and 35 — source guards that keep the container → business-unit
/// binding whole as the code that creates SPE containers grows.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><b>Every container-creation path stamps</b> (round 35 item 1 — not only the BFF's). In <c>src/</c>: each
///   <c>FileStorage.Containers…PostAsync(</c> sits in a method that binds the new container (<c>BindNewContainerAsync(</c>),
///   or is listed in <see cref="DeferredBinders"/> with the step that binds it, and that step is verified. In
///   <c>scripts/</c>: each container-create call (a POST to the containers collection) is followed, in the same function
///   (or the same top-level script), by <c>Invoke-SpeContainerBindOrRemove</c>, and the script dot-sources
///   <c>common/SpeContainerBinding.ps1</c>. An unstamped container is reached by NO admin route (round 35 item 2).</item>
///   <item><b>ONE constant on each side.</b> The property name is spelled as a literal in exactly one <c>.cs</c> file under
///   <c>src/</c> (the source-linked contract) and exactly one script under <c>scripts/</c> (the common module), and the
///   two are equal. The Key Vault secret-name prefix (round 35 item 3) likewise agrees between the BFF and the
///   <c>-Verify</c> script.</item>
///   <item><b>Every app-only container-TYPE route carries the type rule</b> (round 20 item 3; round 35 item 5).</item>
///   <item><b>The backfill script and the BFF agree</b> on the property, the authoritative sources and the audit
///   operation.</item>
/// </list>
/// Each analyser is exercised on seeded snippets too, so a regression in the guard itself reddens.
/// </remarks>
public sealed class SpeAdminContainerBindingGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // ─────────────────────────────────────────────────────────────────────────
    // 1a. Stamped at creation — C# (all of src/)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Regex ContainerCreate = new(@"FileStorage\s*\.\s*Containers\s*\.\s*PostAsync\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Creation sites whose binding is a LATER step of the same handler run, not the creating method — key: the creating
    /// file (repo-relative, '/' separators). The guard verifies the named step instead (<see cref="TheDeferredBinderIsReal"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeferredBinders = new Dictionary<string, string>
    {
        // H8 creates the root container in ProvisionAsync and binds it after the app-only GET verification (an SPE
        // container may be unaddressable for up to 24h after creation — binding earlier would delete a healthy one).
        ["src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SpeContainerType/GraphContainerTypeProvisioner.cs"] =
            "H8SpeContainerTypeHandler.HandleAsync -> ISpeContainerTypeProvisioner.BindRootContainerAsync -> BindNewContainerAsync",
    };

    [Fact(DisplayName = "Every SPE container created anywhere in src/ is bound to its business unit")]
    public void EveryContainerCreationPath_BindsTheNewContainer()
    {
        var files = SourceFiles(Path.Combine(RepoRoot, "src"), "*.cs")
            .Select(f => (Rel: Rel(f), Code: StripComments(File.ReadAllText(f))))
            .ToList();

        var creators = files.Where(f => ContainerCreate.IsMatch(f.Code)).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the three C# creation paths (SpeAdminGraphService, ContainerOperations, the L2 H8 " +
            $"provisioner) — found {creators.Count}, so it would pass vacuously");

        var violations = new List<string>();
        foreach (var (rel, code) in creators)
        {
            if (DeferredBinders.ContainsKey(rel))
            {
                continue;
            }

            violations.AddRange(UnboundCreations(code).Select(v => $"{rel}: {v}"));
        }

        Assert.True(violations.Count == 0,
            "These methods create an SPE container without binding it to its owning business unit (owner rounds 20/35; " +
            "call BindNewContainerAsync in the same method):\n  " + string.Join("\n  ", violations));

        var stale = DeferredBinders.Keys.Where(k => !creators.Any(c => c.Rel == k)).ToList();
        Assert.True(stale.Count == 0, "Deferred-binder entries naming no creation site — delete them: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "The L2 H8 root container is bound by the handler after verification, before the KV write and the H7 handoff")]
    public void TheDeferredBinderIsReal()
    {
        var provisioner = StripComments(File.ReadAllText(Path.Combine(RepoRoot,
            "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers", "SpeContainerType", "GraphContainerTypeProvisioner.cs")));
        var handler = StripComments(File.ReadAllText(Path.Combine(RepoRoot,
            "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers", "SpeContainerType", "H8SpeContainerTypeHandler.cs")));

        // The provisioner's bind step really stamps, reads back and removes.
        var bindStep = MethodBody(provisioner, "BindNewContainerAsync(");
        Assert.Contains("WriteStampAsync(", bindStep);
        Assert.Contains("customProperties", bindStep);
        Assert.Contains(".DeleteAsync(", bindStep);
        Assert.Contains("BindNewContainerAsync(", MethodBody(provisioner, "BindRootContainerAsync("));

        // The handler binds the verified container BEFORE the KV write and before the success state hands it to H7.
        var handle = MethodBody(handler, "HandleAsync(");
        var verify = handle.IndexOf("_verifier.VerifyAsync(", StringComparison.Ordinal);
        var bind = handle.IndexOf("_provisioner.BindRootContainerAsync(", StringComparison.Ordinal);
        var kv = handle.IndexOf("_kvWriter.WriteAsync(", StringComparison.Ordinal);
        var complete = handle.IndexOf("MarkCompleteAsync(", StringComparison.Ordinal);
        Assert.True(verify > 0 && bind > verify && kv > bind && complete > kv,
            $"H8 must verify, then bind, then write the KV secret, then complete (offsets: verify {verify}, bind {bind}, kv {kv}, complete {complete})");
    }

    [Fact(DisplayName = "The C# creation analyser flags a seeded unbound creation and passes a bound one")]
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

    // ─────────────────────────────────────────────────────────────────────────
    // 1b. Stamped at creation — PowerShell (all of scripts/)
    // ─────────────────────────────────────────────────────────────────────────

    private const string ScriptBinder = "Invoke-SpeContainerBindOrRemove";
    private const string ScriptBindingModule = "common/SpeContainerBinding.ps1";

    /// <summary>A URI that ends at the containers COLLECTION (a create when POSTed; a GET of it lists).</summary>
    private static readonly Regex ScriptContainersCollection = new(
        @"fileStorage/containers[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact(DisplayName = "Every SPE container a script creates is bound to its business unit, or removed")]
    public void EveryScriptThatCreatesAContainer_BindsIt()
    {
        var scripts = SourceFiles(Path.Combine(RepoRoot, "scripts"), "*.ps1")
            .Concat(SourceFiles(Path.Combine(RepoRoot, "scripts"), "*.psm1"))
            .Select(f => (Rel: Rel(f), Text: File.ReadAllText(f)))
            .ToList();

        var creators = scripts.Where(s => ScriptCreations(s.Text).Count > 0).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the script creation paths (New-BusinessUnitContainer, Provision-Customer, " +
            $"Create-NewContainerType) — found {creators.Count}, so it would pass vacuously");

        var violations = creators.SelectMany(s => UnboundScriptCreations(s.Text).Select(v => $"{s.Rel}: {v}")).ToList();
        Assert.True(violations.Count == 0,
            "These scripts create an SPE container without binding it (owner round 35 item 1: call " + ScriptBinder +
            " after the create, in the same function, and dot-source " + ScriptBindingModule + "):\n  " +
            string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "The script creation analyser flags a seeded unbound creation and passes a bound one")]
    public void TheScriptCreationAnalyser_BitesOnASeededUnboundCreation()
    {
        const string bound = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            function New-Thing {
                $c = Invoke-RestMethod `
                    -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
                    -Method Post `
                    -Body $b
                Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $c.id -BusinessUnitId $u
            }
            """;
        const string unbound = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            function New-Thing {
                $c = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Method Post -Body $b
            }
            function Other { Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $x -BusinessUnitId $u }
            """;
        const string notACreate = """
            $list = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Method Get
            """;
        const string noModule = """
            $c = Invoke-RestMethod -Uri 'https://graph.microsoft.com/beta/storage/fileStorage/containers' -Method Post -Body $b
            Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $c.id -BusinessUnitId $u
            """;

        Assert.Empty(UnboundScriptCreations(bound));
        Assert.NotEmpty(UnboundScriptCreations(unbound));
        Assert.Empty(ScriptCreations(notACreate));
        Assert.NotEmpty(UnboundScriptCreations(noModule));
    }

    /// <summary>The offsets of every container CREATE in a script: a containers-collection URI in a POST command.</summary>
    private static List<int> ScriptCreations(string script)
    {
        var text = StripPowerShellComments(script);
        var creations = new List<int>();
        foreach (Match m in ScriptContainersCollection.Matches(text))
        {
            if (Regex.IsMatch(LogicalCommand(text, m.Index), @"-Method\s+['""]?Post\b|\s'Post'\s", RegexOptions.IgnoreCase))
            {
                creations.Add(m.Index);
            }
        }

        return creations;
    }

    private static List<string> UnboundScriptCreations(string script)
    {
        var text = StripPowerShellComments(script);
        var violations = new List<string>();
        var creations = ScriptCreations(script);
        if (creations.Count > 0 && !text.Contains(ScriptBindingModule, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"creates a container but does not dot-source {ScriptBindingModule}");
        }

        foreach (var index in creations)
        {
            var (scopeStart, scopeEnd) = EnclosingPowerShellScope(text, index);
            var after = text[index..scopeEnd];
            if (!after.Contains(ScriptBinder, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"the create at line {LineOf(text, index)} is not followed by {ScriptBinder} in the same scope " +
                               $"(lines {LineOf(text, scopeStart)}-{LineOf(text, scopeEnd)})");
            }
        }

        return violations;
    }

    /// <summary>The command a match sits in, joined across backtick line continuations.</summary>
    private static string LogicalCommand(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        while (start > 1)
        {
            var prevEnd = start - 1;                                   // the '\n' ending the previous line
            var prevStart = text.LastIndexOf('\n', Math.Max(0, prevEnd - 1)) + 1;
            var prev = text[prevStart..prevEnd].TrimEnd('\r', ' ', '\t');
            if (!prev.EndsWith('`')) break;
            start = prevStart;
        }

        var end = index;
        while (end < text.Length)
        {
            var lineEnd = text.IndexOf('\n', end);
            if (lineEnd < 0) { end = text.Length; break; }
            var line = text[end..lineEnd].TrimEnd('\r', ' ', '\t');
            end = lineEnd + 1;
            if (!line.EndsWith('`')) break;
        }

        return text[start..end];
    }

    /// <summary>The brace-balanced body of the PowerShell function around <paramref name="index"/>, or the whole file.</summary>
    private static (int Start, int End) EnclosingPowerShellScope(string text, int index)
    {
        foreach (Match f in Regex.Matches(text, @"^[ \t]*function\s+[\w-]+[^{]*\{", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var open = f.Index + f.Length - 1;
            var level = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{') level++;
                else if (text[i] == '}' && --level == 0)
                {
                    if (index > open && index < i) return (f.Index, i);
                    break;
                }
            }
        }

        return (0, text.Length);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. ONE constant on each side
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "The stamp's property name is ONE constant in C# and ONE in PowerShell, and they agree")]
    public void ThePropertyName_IsOneConstantOnEachSide_AndTheyAgree()
    {
        var literal = SpeContainerBusinessUnitStamp.PropertyName;

        var csFiles = SourceFiles(Path.Combine(RepoRoot, "src"), "*.cs")
            .Where(f => File.ReadAllText(f).Contains($"\"{literal}\"", StringComparison.Ordinal))
            .Select(Rel)
            .ToList();
        Assert.Equal(new[] { "src/server/shared/Contracts/SpeContainerBusinessUnitBinding.cs" }, csFiles);

        var scriptFiles = SourceFiles(Path.Combine(RepoRoot, "scripts"), "*.ps1")
            .Concat(SourceFiles(Path.Combine(RepoRoot, "scripts"), "*.psm1"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains($"'{literal}'", StringComparison.OrdinalIgnoreCase)
                       || text.Contains($"\"{literal}\"", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Rel)
            .ToList();
        Assert.Equal(new[] { "scripts/common/SpeContainerBinding.ps1" }, scriptFiles);

        var module = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "common", "SpeContainerBinding.ps1"));
        Assert.Equal(literal, ScriptVariable(module, "SpeContainerStampProperty"));
    }

    [Fact(DisplayName = "The secret-name -Verify script checks the prefix the BFF enforces")]
    public void TheSecretNameVerifyScript_AgreesWithTheBff()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Test-SpeConfigSecretNames.ps1"));
        Assert.Equal(SpeConfigSecretNamePolicy.RequiredPrefix, ScriptVariable(script, "SpeConfigSecretNamePrefix"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. App-only container-type routes carry the type rule
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
            "These app-only container-type routes lack .WithSpeAdminContainerTypeScope() — with Model 1 configs sharing a " +
            "type and owning app (owner round 20 item 3) they could read or change what every customer of the type " +
            "shares:\n  " + string.Join("\n  ", unmarked));

        var stale = DelegatedTypeRoutes.Keys.Where(k => !routes.Any(r => r.Key == k)).ToList();
        Assert.True(stale.Count == 0, "Delegated-route entries naming no route — delete them: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "The type-route analyser flags a seeded unmarked app-only route")]
    public void TheTypeRouteAnalyser_BitesOnASeededUnmarkedRoute()
    {
        const string marked = """
            group.MapPost("/containertypes/{typeId}/consumers", RegisterConsumerAsync)
                .WithSpeAdminContainerTypeScope()
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
    // 4. The backfill script agrees with the BFF
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "The backfill script stamps the property the BFF reads, from the sources the BFF writes")]
    public void TheBackfillScript_AgreesWithTheBff()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Backfill-SpeContainerBusinessUnitStamp.ps1"));

        // The property comes from THE PowerShell constant (pinned equal to the C# one above) — never spelled again here.
        Assert.Contains(ScriptBindingModule, script, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"^\s*\$StampProperty\s*=\s*\$SpeContainerStampProperty\s*$", RegexOptions.Multiline), script);

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

        // Round 35 item 2: -Bind exists, and -Verify fails on every still-unbound container.
        Assert.Matches(new Regex(@"\[string\[\]\]\$Bind\b"), script);
        Assert.Matches(new Regex(@"if \(\$Verify\)\s*\{[^}]*\$unbound\.Count", RegexOptions.Singleline), script);
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

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Rel(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static int LineOf(string text, int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

    /// <summary>The brace-balanced body of the member declaration that precedes <paramref name="index"/>.</summary>
    private static string EnclosingMethodBody(string code, int index)
    {
        var header = Regex.Matches(code, @"^[ \t]*(?:public|private|internal|protected)\b[^;{=]*\(", RegexOptions.Multiline)
            .LastOrDefault(h => h.Index < index);
        return header is null ? string.Empty : BraceBody(code, code.IndexOf('{', header.Index));
    }

    /// <summary>The body of the first member whose declaration contains <paramref name="signature"/>.</summary>
    private static string MethodBody(string code, string signature)
    {
        var header = Regex.Matches(code, @"^[ \t]*(?:public|private|internal|protected)\b[^;{=]*\(", RegexOptions.Multiline)
            .FirstOrDefault(h => code.AsSpan(h.Index, h.Length).Contains(signature, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No member '{signature}' — update this guard.");
        return BraceBody(code, code.IndexOf('{', header.Index));
    }

    private static string BraceBody(string code, int start)
    {
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

    private static string StripComments(string source) =>
        Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>Removes <c>&lt;# … #&gt;</c> blocks and <c>#</c> line comments (keeping line breaks so line numbers hold).</summary>
    private static string StripPowerShellComments(string script)
    {
        var noBlocks = Regex.Replace(script, @"<#.*?#>", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        return Regex.Replace(noBlocks, @"(?m)^[ \t]*#[^\n]*", string.Empty);
    }

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
