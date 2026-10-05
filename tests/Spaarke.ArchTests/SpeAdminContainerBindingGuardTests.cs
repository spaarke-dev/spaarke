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
///   <c>scripts/</c> (and PowerShell under <c>src/</c>): each container-create call is followed, in the same function
///   (or the same top-level script), by <c>Invoke-SpeContainerBindOrRemove</c>, and the script dot-sources
///   <c>common/SpeContainerBinding.ps1</c>. An unstamped container is reached by NO admin route (round 35 item 2).
///   <b>Round 41 item 5:</b> the guard follows every route to the collection, not one spelling — in C# a collection or
///   FileStorage builder held in a variable, passed along or constructed directly is refused, and a spelled-out collection
///   URL in a method that POSTs is a create; in a script that names the collection, a POST whose URI is not provably
///   another endpoint (a variable, a splat, a function's result) is a create; the Graph SDK cmdlet is a create anywhere;
///   no other source type may address the collection at all.</item>
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

    /// <summary>
    /// A member access to the Graph SDK's <c>FileStorage</c> request builder (the <c>Microsoft.Graph.Storage.FileStorage</c>
    /// NAMESPACE is skipped). Owner round 41 item 5: the guard follows EVERY route to the containers collection, not one
    /// spelling of it — a builder held in a variable, passed along or constructed directly is refused, because nothing
    /// could then tell a create through it from a read.
    /// </summary>
    private static readonly Regex FileStorageMember = new(@"\.\s*FileStorage\b", RegexOptions.Compiled);

    private static readonly Regex GraphStorageNamespace = new(
        @"Microsoft\s*\.\s*Graph\s*\.\s*(?:Beta\s*\.\s*)?Storage\s*$", RegexOptions.Compiled);

    private static readonly Regex FileStorageMemberNext = new(
        @"\G\s*\.\s*(?<member>Containers|ContainerTypes|ContainerTypeRegistrations|DeletedContainers)\b", RegexOptions.Compiled);

    private static readonly Regex IndexerNext = new(@"\G\s*\[", RegexOptions.Compiled);

    private const string Balanced = @"\((?>[^()]+|\((?<d>)|\)(?<-d>))*(?(d)(?!))\)";

    /// <summary>The only uses of the containers COLLECTION builder the guard can judge: a chained GET or POST.</summary>
    private static readonly Regex CollectionChainNext = new(
        @"\G\s*(?:\.\s*WithUrl\s*" + Balanced + @"\s*)?\.\s*(?<verb>GetAsync|PostAsync)\s*\(", RegexOptions.Compiled);

    /// <summary>The collection URL spelled out (raw HTTP / Kiota request information / a script) — not an item URL.</summary>
    private static readonly Regex CollectionUrl = new(@"fileStorage/containers(?![/\w])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Signals that a C# method sends a POST.</summary>
    private static readonly Regex CSharpPostSignal = new(
        @"\.\s*Post(?:AsJson)?Async\s*\(|HttpMethod\s*\.\s*Post\b|Method\s*\.\s*POST\b|""POST""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Creation sites whose binding is a LATER step of the same handler run, not the creating method — key: the creating
    /// file (repo-relative, '/' separators). The guard verifies the named step instead (<see cref="TheDeferredBinderIsReal"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeferredBinders = new Dictionary<string, string>
    {
        // H8 creates the root container in CreateAsync, records it, and binds it after the app-only GET verification (an
        // SPE container may be unaddressable for up to 24h after creation — binding earlier would delete a healthy one).
        ["src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SpeContainerType/GraphContainerTypeProvisioner.cs"] =
            "H8SpeContainerTypeHandler.HandleAsync -> ISpeContainerTypeProvisioner.BindRootContainerAsync -> BindNewContainerAsync",
    };

    [Fact(DisplayName = "Every SPE container created anywhere in src/ is bound to its business unit")]
    public void EveryContainerCreationPath_BindsTheNewContainer()
    {
        var files = SourceFiles(Path.Combine(RepoRoot, "src"), "*.cs")
            .Select(f => (Rel: Rel(f), Analysis: AnalyseCSharp(StripComments(File.ReadAllText(f)))))
            .ToList();

        var creators = files.Where(f => f.Analysis.Creations.Count > 0).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the three C# creation paths (SpeAdminGraphService, ContainerOperations, the L2 H8 " +
            $"provisioner) — found {creators.Count}, so it would pass vacuously");

        var violations = new List<string>();
        foreach (var (rel, analysis) in files)
        {
            violations.AddRange(analysis.Opaque.Select(v => $"{rel}: {v}"));
            if (!DeferredBinders.ContainsKey(rel))
            {
                violations.AddRange(analysis.Unbound.Select(v => $"{rel}: {v}"));
            }
        }

        Assert.True(violations.Count == 0,
            "These create an SPE container without binding it to its owning business unit, or reach the containers " +
            "collection in a form the guard cannot judge (owner rounds 20/35/41: call BindNewContainerAsync in the same " +
            "method, and use the collection only as a chained .GetAsync( / .PostAsync( on graph.Storage.FileStorage.Containers):\n  " +
            string.Join("\n  ", violations));

        var stale = DeferredBinders.Keys.Where(k => !creators.Any(c => c.Rel == k)).ToList();
        Assert.True(stale.Count == 0, "Deferred-binder entries naming no creation site — delete them: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "No other source under src/ addresses the SPE containers collection")]
    public void NoOtherSourceUnderSrc_AddressesTheContainersCollection()
    {
        // TypeScript / JavaScript / Python / shell under src/ has no binder: a container created there would stay unbound.
        // C# and PowerShell under src/ go through their own analysers.
        var patterns = new[] { "*.ts", "*.tsx", "*.js", "*.mjs", "*.cjs", "*.py", "*.sh" };
        var hits = patterns
            .SelectMany(p => SourceFiles(Path.Combine(RepoRoot, "src"), p))
            .Where(f => CollectionUrl.IsMatch(File.ReadAllText(f)) || ScriptSdkCreate.IsMatch(File.ReadAllText(f)))
            .Select(Rel)
            .ToList();

        Assert.True(hits.Count == 0,
            "These src/ files address the SPE containers collection outside the C# and PowerShell creation paths — create " +
            "containers only through a path that binds them:\n  " + string.Join("\n  ", hits));
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

        // The handler reads its own creation record, creates only without one, RECORDS what it created, then verifies,
        // binds, writes the KV secret and completes — in that order (owner round 41 item 1).
        var handle = MethodBody(handler, "HandleAsync(");
        var recorded = handle.IndexOf("ReadRecordedCreation(", StringComparison.Ordinal);
        var provision = handle.IndexOf("_provisioner.ProvisionAsync(", StringComparison.Ordinal);
        var record = provision < 0 ? -1 : handle.IndexOf("RecordCreationAsync(", provision, StringComparison.Ordinal);
        var verify = handle.IndexOf("_verifier.VerifyAsync(", StringComparison.Ordinal);
        var bind = handle.IndexOf("_provisioner.BindRootContainerAsync(", StringComparison.Ordinal);
        var kv = handle.IndexOf("_kvWriter.WriteAsync(", StringComparison.Ordinal);
        var complete = handle.IndexOf("MarkCompleteAsync(", StringComparison.Ordinal);
        Assert.True(recorded > 0 && provision > recorded && record > provision && verify > record && bind > verify && kv > bind
                    && complete > kv,
            "H8 must read its creation record, create, record, verify, bind, write the KV secret, then complete (offsets: " +
            $"read {recorded}, provision {provision}, record {record}, verify {verify}, bind {bind}, kv {kv}, complete {complete})");

        // The H7 hand-off (InterStepState.SpeContainerId) is given a container ONLY on completion — after the bind.
        var handOffs = Regex.Matches(handler, @"SpeContainerId\s*=(?!=)(?!\s*null\b)").Count;
        Assert.True(handOffs == 1 && MethodBody(handler, "MarkCompleteAsync(").Contains("SpeContainerId = outputs.RootContainerId", StringComparison.Ordinal),
            $"InterStepState.SpeContainerId must be given a container in exactly one place, MarkCompleteAsync (found {handOffs})");
    }

    [Fact(DisplayName = "The C# creation analyser flags every seeded unbound or opaque creation and passes the bound and read forms")]
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
        const string boundViaWithUrl = """
            public async Task<X> CreateAsync()
            {
                var c = await graphClient.Storage.FileStorage.Containers.WithUrl(Build(url, "x")).PostAsync(body);
                await BindNewContainerAsync(graphClient, c.Id, unit, ct);
                return c;
            }
            """;
        const string reads = """
            public async Task<X> ListAsync()
            {
                var page = await graphClient.Storage.FileStorage
                    .Containers
                    .GetAsync(c => c.QueryParameters.Top = 5, ct);
                var next = await graphClient.Storage.FileStorage.Containers.WithUrl($"{b}/storage/fileStorage/containers?$skiptoken={t}").GetAsync();
                var item = await graphClient.Storage.FileStorage.Containers[id].Drive.GetAsync();
                return page;
            }

            public Task<T> CreateTypeAsync() => graphClient.Storage.FileStorage.ContainerTypes.PostAsync(t);
            """;
        const string namespaceOnly = """
            using Microsoft.Graph.Storage.FileStorage.Containers.Item.Permissions;
            public sealed class X { private Microsoft.Graph.Storage.FileStorage.Containers.Item.Drive.DriveRequestBuilder? _d; }
            """;
        const string unbound = """
            public async Task<X> CreateAsync()
            {
                var c = await graphClient.Storage.FileStorage.Containers
                    .PostAsync(body, cancellationToken: ct);
                return c;
            }
            """;
        // Owner round 41 item 5 — the verifier's seed H2: the collection builder held in a variable.
        const string heldCollection = """
            public async Task<X> CreateAsync()
            {
                var containers = graph.Storage.FileStorage.Containers;
                return await containers.PostAsync(body);
            }
            """;
        const string heldFileStorage = """
            public async Task<X> CreateAsync()
            {
                var fs = graph.Storage.FileStorage;
                return await fs.Containers.PostAsync(body);
            }
            """;
        const string passedAlong = """
            public Task<X> CreateAsync() => Send(graph.Storage.FileStorage.Containers, body);
            """;
        const string constructed = """
            public async Task<X> CreateAsync()
            {
                return await new ContainersRequestBuilder(url, adapter).PostAsync(body);
            }
            """;
        const string rawHttp = """
            public async Task<HttpResponseMessage> CreateAsync()
            {
                return await http.PostAsync($"{baseUrl}/storage/fileStorage/containers", content);
            }
            """;
        const string kiota = """
            public async Task CreateAsync()
            {
                var info = new RequestInformation { HttpMethod = Method.POST, URI = new Uri(baseUrl + "/storage/fileStorage/containers") };
                await adapter.SendNoContentAsync(info);
            }
            """;

        Assert.Empty(Problems(bound));
        Assert.Empty(Problems(boundViaWithUrl));
        Assert.Empty(Problems(reads));
        Assert.Empty(Problems(namespaceOnly));
        foreach (var (name, seeded) in new[]
                 {
                     ("unbound", unbound), ("heldCollection", heldCollection), ("heldFileStorage", heldFileStorage),
                     ("passedAlong", passedAlong), ("constructed", constructed), ("rawHttp", rawHttp), ("kiota", kiota),
                 })
        {
            Assert.True(Problems(seeded).Count > 0, $"the analyser must flag the seeded '{name}' creation");
        }

        static List<string> Problems(string source)
        {
            var analysis = AnalyseCSharp(StripComments(source));
            return analysis.Unbound.Concat(analysis.Opaque).ToList();
        }
    }

    /// <summary>What the guard finds in one C# file.</summary>
    /// <param name="Creations">Offsets of every container CREATE the guard recognises.</param>
    /// <param name="Unbound">Creations whose method does not bind the new container.</param>
    /// <param name="Opaque">Uses of the containers collection the guard cannot judge (held, passed, constructed).</param>
    private sealed record CSharpAnalysis(List<int> Creations, List<string> Unbound, List<string> Opaque);

    private static CSharpAnalysis AnalyseCSharp(string code)
    {
        // using directives are not request builders (blanked, keeping offsets).
        code = Regex.Replace(code, @"^[ \t]*using\s+[^;\n]+;", m => new string(' ', m.Length), RegexOptions.Multiline);

        var creations = new List<int>();
        var opaque = new List<string>();

        foreach (Match m in FileStorageMember.Matches(code))
        {
            if (GraphStorageNamespace.IsMatch(code[Math.Max(0, m.Index - 80)..m.Index]))
            {
                continue; // Microsoft.Graph.Storage.FileStorage… — the namespace, not the request builder
            }

            var member = FileStorageMemberNext.Match(code, m.Index + m.Length);
            if (!member.Success)
            {
                opaque.Add($"line {LineOf(code, m.Index)}: the FileStorage request builder is held or passed — chain it");
                continue;
            }

            if (member.Groups["member"].Value != "Containers")
            {
                continue;
            }

            var afterCollection = member.Index + member.Length;
            if (IndexerNext.IsMatch(code, afterCollection))
            {
                continue; // Containers[id] — one container, not the collection
            }

            var chain = CollectionChainNext.Match(code, afterCollection);
            if (!chain.Success)
            {
                opaque.Add($"line {LineOf(code, m.Index)}: the containers COLLECTION builder is used other than in a chained " +
                           ".GetAsync( / .PostAsync( — a create through it could not be seen");
            }
            else if (chain.Groups["verb"].Value == "PostAsync")
            {
                creations.Add(m.Index);
            }
        }

        foreach (Match m in Regex.Matches(code, @"\bContainersRequestBuilder\b"))
        {
            opaque.Add($"line {LineOf(code, m.Index)}: ContainersRequestBuilder named directly — use graph.Storage.FileStorage.Containers, chained");
        }

        // The collection URL spelled out: a create when its method sends a POST.
        foreach (Match m in CollectionUrl.Matches(code))
        {
            if (CSharpPostSignal.IsMatch(EnclosingMethodBody(code, m.Index)))
            {
                creations.Add(m.Index);
            }
        }

        var unbound = creations
            .Where(i => !EnclosingMethodBody(code, i).Contains("BindNewContainerAsync(", StringComparison.Ordinal))
            .Select(i => $"the create at line {LineOf(code, i)} is not bound in its method")
            .ToList();

        return new CSharpAnalysis(creations, unbound, opaque);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1b. Stamped at creation — PowerShell (all of scripts/)
    // ─────────────────────────────────────────────────────────────────────────

    private const string ScriptBinder = "Invoke-SpeContainerBindOrRemove";
    private const string ScriptBindingModule = "common/SpeContainerBinding.ps1";

    /// <summary>The Graph PowerShell SDK's container create cmdlet (v1.0 and beta) — a create wherever it appears.</summary>
    private static readonly Regex ScriptSdkCreate = new(
        @"New-Mg(?:Beta)?StorageFileStorageContainer(?![\w-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A quoted string that ends a path at <c>/containers</c> (the collection) — a "$base/containers" spelling.</summary>
    private static readonly Regex QuotedRelativeCollection = new(
        @"[""'][^""'\n]*/containers(?![/\w])[^""'\n]*[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Every way a script asks for a POST: -Method Post, a splat's Method = 'Post', az rest, curl, an enum, a helper's positional 'Post'.</summary>
    private static readonly Regex ScriptPostSignal = new(
        @"-Method\s*:?\s*['""]?Post\b|\bMethod\s*=\s*['""]?Post\b|--method\s+['""]?post\b|-X\s+['""]?POST\b|\]::Post\b|(?<![\w-])['""]Post['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptNamedUri = new(
        @"(?:-Uri\s*:?|--ur[il])\s+(?<arg>""[^""]*""|'[^']*'|\$[\w:]+|\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptQuotedPath = new(@"""[^""\n]*/[^""\n]*""|'[^'\n]*/[^'\n]*'", RegexOptions.Compiled);

    [Fact(DisplayName = "Every SPE container a script creates is bound to its business unit, or removed")]
    public void EveryScriptThatCreatesAContainer_BindsIt()
    {
        // scripts/, and any PowerShell under src/ (round 35 item 1: "a container-create call site in src/ or scripts/").
        var scripts = new[] { "scripts", "src" }
            .SelectMany(root => SourceFiles(Path.Combine(RepoRoot, root), "*.ps1")
                .Concat(SourceFiles(Path.Combine(RepoRoot, root), "*.psm1")))
            .Select(f => (Rel: Rel(f), Text: File.ReadAllText(f)))
            .ToList();

        var creators = scripts.Where(s => ScriptCreations(s.Text).Count > 0).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the script creation paths (New-BusinessUnitContainer, Provision-Customer, " +
            $"Create-NewContainerType) — found {creators.Count}, so it would pass vacuously");

        var violations = creators.SelectMany(s => UnboundScriptCreations(s.Text).Select(v => $"{s.Rel}: {v}")).ToList();
        Assert.True(violations.Count == 0,
            "These scripts create (or may create) an SPE container without binding it (owner rounds 35/41: call " + ScriptBinder +
            " after the create, in the same function, and dot-source " + ScriptBindingModule + "):\n  " +
            string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "No other script type under scripts/ addresses the SPE containers collection")]
    public void NoOtherScriptType_AddressesTheContainersCollection()
    {
        // Shell, Python, JavaScript and pipeline YAML have no binder: a container created there would stay unbound.
        var patterns = new[] { "*.sh", "*.py", "*.js", "*.mjs", "*.cjs", "*.ts", "*.cmd", "*.bat", "*.yml", "*.yaml" };
        var hits = patterns
            .SelectMany(p => SourceFiles(Path.Combine(RepoRoot, "scripts"), p))
            .Where(f => CollectionUrl.IsMatch(File.ReadAllText(f)) || ScriptSdkCreate.IsMatch(File.ReadAllText(f)))
            .Select(Rel)
            .ToList();

        Assert.True(hits.Count == 0,
            "These scripts address the SPE containers collection with no binder — create containers only through a " +
            "PowerShell path that calls " + ScriptBinder + ":\n  " + string.Join("\n  ", hits));
    }

    [Fact(DisplayName = "The script creation analyser flags every seeded unbound creation and passes the bound and read forms")]
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
        const string otherPosts = """
            $list = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers?`$top=5" -Method Get
            $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
            $tok = Invoke-RestMethod -Uri $tokenUrl -Method POST -Body $tb
            Invoke-RestMethod -Uri "$dv/api/data/v9.2/environmentvariablevalues" -Method Post -Body $x
            Invoke-SpeGraph $Token 'Post' "$GraphBase/storage/fileStorage/containers/$id/permissions" $grant
            """;
        const string notACreate = """
            $list = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Method Get
            """;
        const string unbound = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            function New-Thing {
                $c = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Method Post -Body $b
            }
            function Other { Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $x -BusinessUnitId $u }
            """;
        const string noModule = """
            $c = Invoke-RestMethod -Uri 'https://graph.microsoft.com/beta/storage/fileStorage/containers' -Method Post -Body $b
            Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $c.id -BusinessUnitId $u
            """;
        // Owner round 41 item 5 — the verifier's seed H1: the URI held in a variable.
        const string uriInVariable = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $uri = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers"; Invoke-RestMethod -Uri $uri -Method Post
            """;
        const string uriBuiltFromABase = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $base = "https://graph.microsoft.com/v1.0/storage/fileStorage"
            $c = Invoke-RestMethod -Uri "$base/containers" -Method Post -Body $b
            """;
        const string splatted = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $p = @{
                Uri    = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers"
                Method = 'Post'
                Body   = $b
            }
            $c = Invoke-RestMethod @p
            """;
        const string uriFromAFunction = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            function Get-CollectionUri { "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" }
            $c = Invoke-RestMethod -Uri (Get-CollectionUri) -Method Post -Body $b
            """;
        const string azRest = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            az rest --method post --url "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" --body $b
            """;
        const string sdkCmdlet = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $c = New-MgStorageFileStorageContainer -BodyParameter $b
            """;

        Assert.Empty(UnboundScriptCreations(bound));
        Assert.Empty(ScriptCreations(otherPosts));
        Assert.Empty(ScriptCreations(notACreate));
        foreach (var (name, seeded) in new[]
                 {
                     ("unbound", unbound), ("noModule", noModule), ("uriInVariable", uriInVariable),
                     ("uriBuiltFromABase", uriBuiltFromABase), ("splatted", splatted), ("uriFromAFunction", uriFromAFunction),
                     ("azRest", azRest), ("sdkCmdlet", sdkCmdlet),
                 })
        {
            Assert.True(UnboundScriptCreations(seeded).Count > 0, $"the analyser must flag the seeded '{name}' creation");
        }
    }

    /// <summary>
    /// The offsets of every container CREATE (or possible create) in a script. Fail closed (owner round 41 item 5): in a
    /// script that names the containers collection anywhere, a POST whose URI the analyser cannot prove is another
    /// endpoint — a variable it cannot resolve to literals, a splat, a function's result — counts as a create.
    /// </summary>
    private static List<int> ScriptCreations(string script)
    {
        var text = StripPowerShellComments(script);
        var creations = ScriptSdkCreate.Matches(text).Select(m => m.Index).ToList();

        var namesCollection = CollectionUrl.IsMatch(text)
                              || (text.Contains("fileStorage", StringComparison.OrdinalIgnoreCase) && QuotedRelativeCollection.IsMatch(text));
        if (!namesCollection)
        {
            return creations;
        }

        foreach (Match post in ScriptPostSignal.Matches(text))
        {
            if (PostMayCreateAContainer(text, LogicalCommand(text, post.Index)))
            {
                creations.Add(post.Index);
            }
        }

        return creations;
    }

    /// <summary>Whether one POST command (in a script that names the collection) may create a container.</summary>
    private static bool PostMayCreateAContainer(string script, string command)
    {
        static bool IsCollection(string s) => CollectionUrl.IsMatch(s) || Regex.IsMatch(s, @"/containers(?![/\w])", RegexOptions.IgnoreCase);

        var named = ScriptNamedUri.Match(command);
        if (named.Success)
        {
            var arg = named.Groups["arg"].Value;
            if (arg.StartsWith('"') || arg.StartsWith('\''))
            {
                return IsCollection(arg);
            }

            if (Regex.IsMatch(arg, @"^\$[\w:]+$"))
            {
                // A variable: every assignment of it in the script must be a literal that is provably another endpoint.
                var name = Regex.Escape(arg.TrimStart('$').Replace("script:", string.Empty, StringComparison.OrdinalIgnoreCase));
                var assignments = Regex.Matches(script, $@"^\s*\$(?:script:)?{name}\s*=\s*(?<rhs>.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (assignments.Count == 0)
                {
                    return true; // a parameter or computed elsewhere — unknown
                }

                foreach (Match a in assignments)
                {
                    var paths = ScriptQuotedPath.Matches(a.Groups["rhs"].Value).Select(p => p.Value).ToList();
                    if (paths.Count == 0 || paths.Any(IsCollection))
                    {
                        return true;
                    }
                }

                return false;
            }

            return true; // an expression — unknown
        }

        var literals = ScriptQuotedPath.Matches(command).Select(p => p.Value).ToList();
        return literals.Count == 0 || literals.Any(IsCollection);
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

    private const string ScriptSecretNamePolicyModule = "common/SpeConfigSecretNamePolicy.ps1";

    /// <summary>The scripts that judge a config's Key Vault secret name — each must do it through THE module.</summary>
    private static readonly string[] ScriptsJudgingSecretNames =
    {
        "Test-SpeConfigSecretNames.ps1",            // round 35 item 3's -Verify
        "Repair-SpeConfigSecretName.ps1",           // round 41 item 4's dry run / -Apply / -Verify
        "Backfill-SpeContainerBusinessUnitStamp.ps1", // refuses a non-conforming name before reading the vault
    };

    [Fact(DisplayName = "The PowerShell secret-name rule is ONE module that agrees with the BFF, and every script that judges a name uses it")]
    public void TheSecretNameVerifyScript_AgreesWithTheBff()
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "common", "SpeConfigSecretNamePolicy.ps1"));
        Assert.Equal(SpeConfigSecretNamePolicy.RequiredPrefix, ScriptVariable(module, "SpeConfigSecretNamePrefix"));

        // Owner round 41 item 5: the allow-list ends at the END OF THE STRING (\z), never at '$', which in .NET also
        // matches just before a trailing newline — so a name followed by a newline would conform.
        Assert.Contains("+ '}\\z'", module, StringComparison.Ordinal);
        Assert.DoesNotContain("+ '}$'", module, StringComparison.Ordinal);

        foreach (var name in ScriptsJudgingSecretNames)
        {
            var script = StripPowerShellComments(File.ReadAllText(Path.Combine(RepoRoot, "scripts", name)));
            Assert.True(script.Contains(ScriptSecretNamePolicyModule, StringComparison.OrdinalIgnoreCase),
                $"{name} must dot-source {ScriptSecretNamePolicyModule} (the ONE PowerShell copy of the rule)");
            Assert.True(ScriptVariable(script, "SpeConfigSecretNamePrefix") is null && !script.Contains("[A-Za-z0-9-]{1,", StringComparison.Ordinal),
                $"{name} must not spell the rule again");
            Assert.True(script.Contains("Test-SpeConfigSecretNameAllowed", StringComparison.Ordinal),
                $"{name} must judge names with Test-SpeConfigSecretNameAllowed");
        }

        // Any script that takes a config's secret name AND reads a vault must be one of the above (it must judge first).
        var vaultReaders = SourceFiles(Path.Combine(RepoRoot, "scripts"), "*.ps1")
            .Where(f =>
            {
                var text = StripPowerShellComments(File.ReadAllText(f));
                return text.Contains("sprk_keyvaultsecretname", StringComparison.OrdinalIgnoreCase)
                       && Regex.IsMatch(text, @"az\s+keyvault\s+secret\s+show|vault\.azure\.net/secrets", RegexOptions.IgnoreCase);
            })
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(vaultReaders.Count >= 2, $"the scan must find the vault-reading scripts (found {vaultReaders.Count})");
        Assert.True(vaultReaders.All(r => ScriptsJudgingSecretNames.Contains(r)),
            "These scripts read a config's Key Vault secret without the allow-list: " +
            string.Join(", ", vaultReaders.Where(r => !ScriptsJudgingSecretNames.Contains(r))));

        // The backfill refuses a non-conforming name BEFORE it reads the vault (as the BFF does).
        var backfill = StripPowerShellComments(File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Backfill-SpeContainerBusinessUnitStamp.ps1")));
        var judged = backfill.IndexOf("Test-SpeConfigSecretNameAllowed $SecretName", StringComparison.Ordinal);
        var vaultRead = backfill.IndexOf("az keyvault secret show", StringComparison.Ordinal);
        Assert.True(judged > 0 && vaultRead > judged,
            $"the backfill must judge the secret name before it reads the vault (judged at {judged}, read at {vaultRead})");
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
