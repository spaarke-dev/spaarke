using System.Reflection;
using System.Text;
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

    /// <summary>
    /// A literal that is just the collection's last segment — <c>"/containers"</c>, or <c>$"{base}/containers"</c>, optionally
    /// with a query — the collection when the same file's strings name fileStorage (a base held elsewhere). Not
    /// <c>"/search/containers"</c> or <c>"/api/spe/containers"</c>, which are other paths.
    /// </summary>
    private static readonly Regex RelativeCollectionPath = new(
        @"^\$*@?\$*""(?:\{[^{}""]*\})?/containers(?:[?#][^""]*)?""$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Signals that a C# member sends a POST: <c>PostAsync(</c> / <c>PostAsJsonAsync(</c> — with or without generic
    /// arguments (round 49: <c>PostAsJsonAsync&lt;object&gt;(…)</c> was missed) — <c>HttpMethod.Post</c>,
    /// <c>Method.POST</c>, or a <c>"POST"</c> literal (e.g. <c>new HttpMethod("POST")</c>).
    /// </summary>
    private static readonly Regex CSharpPostSignal = new(
        @"\.\s*Post(?:AsJson)?Async\s*(?:<(?>[^<>()]+|<(?<g>)|>(?<-g>))*(?(g)(?!))>)?\s*\(|HttpMethod\s*\.\s*Post\b|Method\s*\.\s*POST\b|""POST""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Creation sites whose binding is a LATER step of the same handler run, not the creating method — key: the creating
    /// file (repo-relative, '/' separators). The guard verifies the named step instead (<see cref="TheDeferredBinderIsReal"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeferredBinders = new Dictionary<string, string>
    {
        // H8 creates the container in CreateInTypeAsync, records it, and binds it after the app-only GET verification (an
        // SPE container may be unaddressable for up to 24h after creation — binding earlier would delete a healthy one).
        // Master's single-container H8 (task 214, Handlers/SpeContainer/), with task 165's bind ported onto it at the
        // 2026-10-05 master merge.
        ["src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SpeContainer/GraphContainerProvisioner.cs"] =
            "H8SpeContainerHandler.HandleAsync -> ISpeContainerProvisioner.BindRootContainerAsync -> BindNewContainerAsync",
    };

    [Fact(DisplayName = "Every SPE container created anywhere in src/ is bound to its business unit")]
    public void EveryContainerCreationPath_BindsTheNewContainer()
    {
        // Production sources: every .cs under src/ except test projects (*.Tests) — tests/ is never scanned, and the L2
        // tests that happen to live under src/ drive fake Graph transports that legitimately POST to the collection.
        var analyses = AnalyseCSharpFiles(SourceFiles(Path.Combine(RepoRoot, "src"), "*.cs")
            .Where(f => !IsTestProjectFile(f))
            .Select(f => (Rel: Rel(f), Source: File.ReadAllText(f)))
            .ToList(), out var followed);

        var creators = analyses.Where(a => a.Value.Creations.Count > 0).Select(a => a.Key).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the three C# creation paths (SpeAdminGraphService, ContainerOperations, the L2 H8 " +
            $"provisioner) — found {creators.Count}, so it would pass vacuously");

        var violations = new List<string>();
        foreach (var (rel, analysis) in analyses)
        {
            violations.AddRange(analysis.Opaque.Select(v => $"{rel}: {v}"));
            if (!DeferredBinders.ContainsKey(rel))
            {
                violations.AddRange(analysis.Unbound.Select(v => $"{rel}: {v}"));
            }
        }

        Assert.True(violations.Count == 0,
            "These create an SPE container without binding it to its owning business unit, or reach the containers " +
            "collection in a form the guard cannot judge (owner rounds 20/35/41/49: call BindNewContainerAsync in the same " +
            "member, and use the collection only as a chained .GetAsync( / .PostAsync( on graph.Storage.FileStorage.Containers):\n  " +
            string.Join("\n  ", violations) +
            "\n(names followed from a member that spells the collection URL: " + string.Join(", ", followed.Order()) + ")");

        var stale = DeferredBinders.Keys.Where(k => !creators.Contains(k)).ToList();
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

    [Fact(DisplayName = "The L2 H8 container is bound by the handler after verification, before the H7 handoff")]
    public void TheDeferredBinderIsReal()
    {
        var provisioner = LexCSharp(File.ReadAllText(Path.Combine(RepoRoot,
            "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers", "SpeContainer", "GraphContainerProvisioner.cs")));
        var handlerSource = LexCSharp(File.ReadAllText(Path.Combine(RepoRoot,
            "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers", "SpeContainer", "H8SpeContainerHandler.cs")));
        var handler = handlerSource.Text;

        // The provisioner's bind step really stamps, reads back and removes.
        var bindStep = MethodBody(provisioner, "BindNewContainerAsync(");
        Assert.Contains("WriteStampAsync(", bindStep);
        Assert.Contains("customProperties", bindStep);
        Assert.Contains(".DeleteAsync(", bindStep);
        Assert.Contains("BindNewContainerAsync(", MethodBody(provisioner, "BindRootContainerAsync("));

        // The handler reads its own creation record, creates only without one, RECORDS what it created, then verifies,
        // binds (the container, then any further one on record) and completes — in that order (owner rounds 41 + 49).
        // Master's single-container H8 writes no Key Vault secret, so there is no KV step.
        var handle = MethodBody(handlerSource, "HandleAsync(");
        var recorded = handle.IndexOf("ReadRecordedCreation(", StringComparison.Ordinal);
        var provision = handle.IndexOf("_provisioner.ProvisionAsync(", StringComparison.Ordinal);
        var record = provision < 0 ? -1 : handle.IndexOf("RecordCreationAsync(", provision, StringComparison.Ordinal);
        var verify = handle.IndexOf("_verifier.VerifyAsync(", StringComparison.Ordinal);
        var bind = handle.IndexOf("_provisioner.BindRootContainerAsync(", StringComparison.Ordinal);
        var bindAdopted = handle.IndexOf("BindAdditionalContainersAsync(", StringComparison.Ordinal);
        var complete = handle.IndexOf("MarkCompleteAsync(", StringComparison.Ordinal);
        Assert.True(recorded > 0 && provision > recorded && record > provision && verify > record && bind > verify
                    && bindAdopted > bind && complete > bindAdopted,
            "H8 must read its creation record, create, record, verify, bind (the container, then any further one on " +
            $"record), then complete (offsets: read {recorded}, provision {provision}, record {record}, verify {verify}, " +
            $"bind {bind}, bind further {bindAdopted}, complete {complete})");

        // The resume record is read from TYPED fields only — never from gate evidence (owner round 49 item 2: Newtonsoft
        // stored a JsonElement as {"valueKind":1}).
        var read = MethodBody(handlerSource, "RecordedCreation ReadRecordedCreation(");
        Assert.Contains("SpeContainerCreation", read);
        Assert.DoesNotContain("Evidence", read, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonElement", read, StringComparison.Ordinal);

        // The H7 hand-off (InterStepState.SpeContainerId) is given a container ONLY on completion — after the bind.
        var handOffs = Regex.Matches(handler, @"SpeContainerId\s*=(?!=)(?!\s*null\b)").Count;
        Assert.True(handOffs == 1 && MethodBody(handlerSource, "MarkCompleteAsync(").Contains("SpeContainerId = outputs.RootContainerId", StringComparison.Ordinal),
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
            public sealed class Reader
            {
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

                // Uses the URL internally (a nextLink) and returns its own page — a caller that POSTs gets no URL from it.
                public async Task Caller() { await ListAsync(); await http.PostAsync("https://login.microsoftonline.com/t/oauth2/v2.0/token", c); }
            }
            """;
        const string namespaceOnly = """
            using Microsoft.Graph.Storage.FileStorage.Containers.Item.Permissions;
            public sealed class X { private Microsoft.Graph.Storage.FileStorage.Containers.Item.Drive.DriveRequestBuilder? _d; }
            """;
        // Strings that merely LOOK like code or comments are not code or comments.
        const string stringsAreNotComments = """
            public sealed class Log
            {
                private const string Note = "see https://learn.microsoft.com // not a comment";
                public void Write() => _logger.Log("/* not a comment either */ GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers");
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
        // Owner round 49 — the verifier's surviving C# shapes.
        const string absoluteUrl = """
            public async Task<HttpResponseMessage> CreateAsync()
            {
                return await http.PostAsync("https://graph.microsoft.com/v1.0/storage/fileStorage/containers", content);
            }
            """;
        const string classLevelConstant = """
            public sealed class Creator
            {
                private const string ContainersUrl = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers";

                public async Task<HttpResponseMessage> CreateAsync() => await http.PostAsync(ContainersUrl, content);
            }
            """;
        const string genericPost = """
            public async Task<HttpResponseMessage> CreateAsync()
            {
                return await http.PostAsJsonAsync<object>($"{baseUrl}/storage/fileStorage/containers", body);
            }
            """;
        const string urlFromAHelper = """
            public sealed class Creator
            {
                private static string Collection(string b) => $"{b}/storage/fileStorage/containers";

                public async Task<HttpResponseMessage> CreateAsync() => await http.PostAsync(Collection(baseUrl), content);
            }
            """;
        const string relativeToABase = """
            public sealed class Creator
            {
                private const string FileStorageBase = "https://graph.microsoft.com/v1.0/storage/fileStorage";

                public async Task<HttpResponseMessage> CreateAsync() =>
                    await http.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{FileStorageBase}/containers"));
            }
            """;
        const string usingDeclaration = """
            public async Task CreateAsync()
            {
                using var response = await http.PostAsync("https://graph.microsoft.com/v1.0/storage/fileStorage/containers", content);
            }
            """;
        const string topLevelStatements = """
            var url = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers";
            var created = await http.PostAsync(url, content);
            """;

        Assert.Empty(Problems(bound));
        Assert.Empty(Problems(boundViaWithUrl));
        Assert.Empty(Problems(reads));
        Assert.Empty(Problems(namespaceOnly));
        Assert.Empty(Problems(stringsAreNotComments));
        foreach (var (name, seeded) in new[]
                 {
                     ("unbound", unbound), ("heldCollection", heldCollection), ("heldFileStorage", heldFileStorage),
                     ("passedAlong", passedAlong), ("constructed", constructed), ("rawHttp", rawHttp), ("kiota", kiota),
                     ("absoluteUrl", absoluteUrl), ("classLevelConstant", classLevelConstant), ("genericPost", genericPost),
                     ("urlFromAHelper", urlFromAHelper), ("relativeToABase", relativeToABase),
                     ("usingDeclaration", usingDeclaration), ("topLevelStatements", topLevelStatements),
                 })
        {
            Assert.True(Problems(seeded).Count > 0, $"the analyser must flag the seeded '{name}' creation");
        }

        // Across files: the URL constant in one file, the POST in another (a shared constants class).
        var acrossFiles = AnalyseCSharpFiles(new[]
        {
            ("a/GraphPaths.cs", "public static class GraphPaths { public const string Containers = \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers\"; }"),
            ("b/Creator.cs", "public sealed class Creator { public Task<HttpResponseMessage> CreateAsync() => http.PostAsync(GraphPaths.Containers, content); }"),
        });
        Assert.True(acrossFiles["b/Creator.cs"].Unbound.Count > 0, "the analyser must follow a URL constant to another file");

        static List<string> Problems(string source)
        {
            var analysis = AnalyseCSharp(source);
            return analysis.Unbound.Concat(analysis.Opaque).ToList();
        }
    }

    /// <summary>What the guard finds in one C# file.</summary>
    /// <param name="Creations">Offsets of every container CREATE the guard recognises.</param>
    /// <param name="Unbound">Creations whose member does not bind the new container.</param>
    /// <param name="Opaque">Uses of the containers collection the guard cannot judge (held, passed, constructed, or a collection URL outside any member).</param>
    private sealed record CSharpAnalysis(List<int> Creations, List<string> Unbound, List<string> Opaque);

    private static CSharpAnalysis AnalyseCSharp(string source) =>
        AnalyseCSharpFiles(new[] { ("snippet.cs", source) })["snippet.cs"];

    /// <summary>
    /// The C# analyser over a set of files (owner rounds 41 + 49). Every file is LEXED (<see cref="LexCSharp"/>: comments
    /// removed, string literals known — a URL's <c>//</c> is not a comment) and split into MEMBERS (<see cref="ParseMembers"/>:
    /// methods, properties, fields, constants, top-level statements). Then:
    /// <list type="number">
    ///   <item>SDK: every <c>.FileStorage</c> member access must chain straight into <c>.Containers</c>/<c>.ContainerTypes</c>/…,
    ///   and the containers COLLECTION builder only into <c>.GetAsync(</c>/<c>.PostAsync(</c> — a POST is a create; anything
    ///   else (held, passed, constructed) is OPAQUE.</item>
    ///   <item>Spelled URL: a member HOLDS the collection when one of its string literals is the collection URL (or a
    ///   path ending at <c>/containers</c> in a file whose strings name fileStorage), or when it references a SYMBOL — the
    ///   name of a member, in ANY file, that holds the collection and does not POST (a constant, a field, a property, a
    ///   method that builds the URL). A holder that POSTs, or that calls a member that POSTs, is a create. A collection
    ///   literal outside any member is OPAQUE.</item>
    ///   <item>Every create must call <c>BindNewContainerAsync(</c> in the same member (or be a <see cref="DeferredBinders"/> site).</item>
    /// </list>
    /// </summary>
    private static Dictionary<string, CSharpAnalysis> AnalyseCSharpFiles(IReadOnlyList<(string Rel, string Source)> files) =>
        AnalyseCSharpFiles(files, out _);

    private static Dictionary<string, CSharpAnalysis> AnalyseCSharpFiles(
        IReadOnlyList<(string Rel, string Source)> files, out IReadOnlySet<string> followedSymbols)
    {
        var parsed = files.Select(f =>
        {
            var lexed = LexCSharp(f.Source);
            // using DIRECTIVES are not request builders (blanked, keeping offsets) — never a `using var x = …;` statement.
            var code = Regex.Replace(lexed.Code,
                @"^[ \t]*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?[\w.]+(?:<[\w.,\s<>]*>)?\s*;",
                m => new string(' ', m.Length), RegexOptions.Multiline);
            var literals = lexed.Literals.Select(l => (l.Start, l.End, Text: lexed.Text[l.Start..l.End])).ToList();
            var namesFileStorage = literals.Any(l => l.Text.Contains("filestorage", StringComparison.OrdinalIgnoreCase));
            var members = ParseMembers(code);
            var posting = CSharpPostSignal.Matches(lexed.Text)
                .Select(m => MemberAt(members, m.Index))
                .Where(i => i >= 0)
                .ToHashSet();
            return new ParsedCSharpFile(f.Rel, lexed.Text, code, members,
                literals.Where(l => CollectionUrl.IsMatch(l.Text) || (namesFileStorage && RelativeCollectionPath.IsMatch(l.Text)))
                    .Select(l => l.Start)
                    .ToList(),
                posting);
        }).ToList();
        var byRel = parsed.ToDictionary(p => p.Rel);

        var analyses = parsed.ToDictionary(p => p.Rel, p => new CSharpAnalysis(new List<int>(), new List<string>(), new List<string>()));

        // (1) The SDK builders.
        foreach (var p in parsed)
        {
            var analysis = analyses[p.Rel];
            foreach (Match m in FileStorageMember.Matches(p.Code))
            {
                if (GraphStorageNamespace.IsMatch(p.Code[Math.Max(0, m.Index - 80)..m.Index]))
                {
                    continue; // Microsoft.Graph.Storage.FileStorage… — the namespace, not the request builder
                }

                var member = FileStorageMemberNext.Match(p.Code, m.Index + m.Length);
                if (!member.Success)
                {
                    analysis.Opaque.Add($"line {LineOf(p.Code, m.Index)}: the FileStorage request builder is held or passed — chain it");
                    continue;
                }

                if (member.Groups["member"].Value != "Containers")
                {
                    continue;
                }

                var afterCollection = member.Index + member.Length;
                if (IndexerNext.IsMatch(p.Code, afterCollection))
                {
                    continue; // Containers[id] — one container, not the collection
                }

                var chain = CollectionChainNext.Match(p.Code, afterCollection);
                if (!chain.Success)
                {
                    analysis.Opaque.Add($"line {LineOf(p.Code, m.Index)}: the containers COLLECTION builder is used other than in a chained " +
                                        ".GetAsync( / .PostAsync( — a create through it could not be seen");
                }
                else if (chain.Groups["verb"].Value == "PostAsync")
                {
                    analysis.Creations.Add(m.Index);
                }
            }

            foreach (Match m in Regex.Matches(p.Code, @"\bContainersRequestBuilder\b"))
            {
                analysis.Opaque.Add($"line {LineOf(p.Code, m.Index)}: ContainersRequestBuilder named directly — use graph.Storage.FileStorage.Containers, chained");
            }
        }

        // (2) The spelled-out collection URL. A member HOLDS the collection where its text carries it: a collection literal,
        //     or a reference to a PROVIDER — a member whose VALUE is the collection URL (a constant, field or property
        //     initialised with it; a method that RETURNS it). Providers are followed to a fixpoint, across files. A member
        //     that merely USES the URL internally (a list method's nextLink) is not a provider: its callers do not get the URL.
        var carriers = new Dictionary<(string Rel, int Member), List<int>>(); // offsets where the member carries the collection
        void Carry(string rel, int index, int offset)
        {
            if (!carriers.TryGetValue((rel, index), out var offsets))
            {
                carriers[(rel, index)] = offsets = new List<int>();
            }

            offsets.Add(offset);
        }

        foreach (var p in parsed)
        {
            foreach (var literal in p.CollectionLiterals)
            {
                var index = MemberAt(p.Members, literal);
                if (index < 0)
                {
                    analyses[p.Rel].Opaque.Add($"line {LineOf(p.Text, literal)}: the containers collection URL is spelled outside any " +
                                               "member the guard can follow");
                    continue;
                }

                Carry(p.Rel, index, literal);
            }
        }

        // Every identifier in the files' CODE (string contents blanked) that is one of `names`, as (file, member, offset,
        // name) — excluding a member's mention of its own name (its declaration, recursion). With `callsOnly`, only a call:
        // the name followed by '(' (optionally by generic arguments first). One identifier scan per file.
        IEnumerable<(string Rel, int Member, int Offset, string Name)> References(IReadOnlySet<string> names, bool callsOnly = false)
        {
            if (names.Count == 0)
            {
                yield break;
            }

            foreach (var p in parsed)
            {
                foreach (Match r in Identifier.Matches(p.Code))
                {
                    if (!names.Contains(r.Value) || (callsOnly && !CallFollows.IsMatch(p.Code, r.Index + r.Length)))
                    {
                        continue;
                    }

                    var index = MemberAt(p.Members, r.Index);
                    if (index >= 0 && !p.Members[index].Names.Contains(r.Value))
                    {
                        yield return (p.Rel, index, r.Index, r.Value);
                    }
                }
            }
        }

        var providers = new HashSet<string>(StringComparer.Ordinal);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var ((rel, index), offsets) in carriers.ToList())
            {
                var p = byRel[rel];
                if (IsProvider(p, index, offsets))
                {
                    foreach (var name in p.Members[index].Names)
                    {
                        changed |= providers.Add(name);
                    }
                }
            }

            foreach (var (rel, index, offset, _) in References(providers).ToList())
            {
                if (!carriers.TryGetValue((rel, index), out var offsets) || !offsets.Contains(offset))
                {
                    Carry(rel, index, offset);
                    changed = true;
                }
            }
        }

        followedSymbols = providers;

        // A carrier that POSTs is a create; so is a carrier that CALLS a member that posts (the URL passed along to it).
        var postingNames = parsed
            .SelectMany(p => p.Posting.SelectMany(i => p.Members[i].Names))
            .ToHashSet(StringComparer.Ordinal);
        var callers = References(postingNames, callsOnly: true)
            .Select(r => (r.Rel, r.Member))
            .ToHashSet();

        foreach (var (rel, index) in carriers.Keys)
        {
            if (byRel[rel].Posting.Contains(index) || callers.Contains((rel, index)))
            {
                analyses[rel].Creations.Add(byRel[rel].Members[index].Start);
            }
        }

        foreach (var p in parsed)
        {
            var analysis = analyses[p.Rel];
            foreach (var creation in analysis.Creations)
            {
                var member = p.Members.FirstOrDefault(mm => creation >= mm.Start && creation < mm.End);
                var body = member is null ? string.Empty : p.Text[member.Start..member.End];
                if (!body.Contains("BindNewContainerAsync(", StringComparison.Ordinal))
                {
                    analysis.Unbound.Add($"the create at line {LineOf(p.Text, creation)} is not bound in its member" +
                                         (member is null ? string.Empty : $" ({string.Join(", ", member.Names)})"));
                }
            }
        }

        return analyses;
    }

    private sealed record ParsedCSharpFile(
        string Rel, string Text, string Code, List<CSharpMember> Members, List<int> CollectionLiterals, HashSet<int> Posting);

    private static readonly Regex Identifier = new(@"(?<![\w@])[A-Za-z_]\w*", RegexOptions.Compiled);

    private static readonly Regex CallFollows = new(@"\G\s*(?:<(?>[^<>();{}]+|<(?<g>)|>(?<-g>))*(?(g)(?!))>)?\s*\(", RegexOptions.Compiled);

    /// <summary>The index of the member whose span holds <paramref name="offset"/>, or -1 (members are in file order and never overlap).</summary>
    private static int MemberAt(List<CSharpMember> members, int offset)
    {
        int lo = 0, hi = members.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (offset < members[mid].Start) hi = mid - 1;
            else if (offset >= members[mid].End) lo = mid + 1;
            else return mid;
        }

        return -1;
    }

    /// <summary>One member (or top-level statement) of a C# file: its declared name(s) and its span.</summary>
    private sealed record CSharpMember(IReadOnlyList<string> Names, int Start, int End, bool IsMethod = false, bool ExpressionBodied = false);

    /// <summary>
    /// Whether a member that carries the collection at <paramref name="offsets"/> PROVIDES it as its value: a field,
    /// constant or property does; a method does when it is expression-bodied or RETURNS it (a carrying offset inside a
    /// <c>return …;</c>). A member that POSTs consumes the URL (it is a create), and a method that only uses the URL
    /// internally (a list method's nextLink) hands nothing on — neither is a provider.
    /// </summary>
    private static bool IsProvider(ParsedCSharpFile p, int index, IEnumerable<int> offsets)
    {
        var member = p.Members[index];
        if (p.Posting.Contains(index))
        {
            return false;
        }

        if (!member.IsMethod || member.ExpressionBodied)
        {
            return true;
        }

        foreach (var offset in offsets)
        {
            var before = p.Code[member.Start..Math.Min(offset, p.Code.Length)];
            var returnAt = Regex.Matches(before, @"\breturn\b").LastOrDefault();
            if (returnAt is not null && before.IndexOf(';', returnAt.Index) < 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A C# file with its comments blanked (<see cref="Text"/>), and also its literal contents (<see cref="Code"/>).</summary>
    /// <param name="Text">The source with every comment replaced by spaces (line breaks kept) — string literals intact.</param>
    /// <param name="Code">As <paramref name="Text"/>, with the CONTENT of every string and char literal blanked too (interpolation holes are code and are kept).</param>
    /// <param name="Literals">Every string literal's span (prefix to closing quote) in <paramref name="Text"/>.</param>
    private sealed record CSharpSource(string Text, string Code, IReadOnlyList<(int Start, int End)> Literals);

    /// <summary>
    /// A small C# lexer (owner round 49 item 2: the old <c>//[^\n]*</c> comment stripper erased everything after
    /// <c>https:</c> in a URL literal, so an absolute collection URL was never seen). Knows regular, verbatim, interpolated
    /// (with nested holes), raw (<c>"""</c>, with <c>$$</c> holes) and char literals, and line and block comments. Offsets
    /// are preserved in both outputs.
    /// </summary>
    private static CSharpSource LexCSharp(string source)
    {
        var text = source.ToCharArray();
        var code = source.ToCharArray();
        var literals = new List<(int, int)>();
        var i = 0;
        ScanCSharpCode(source, ref i, text, code, literals, inHole: false);
        return new CSharpSource(new string(text), new string(code), literals);
    }

    private static void Blank(char[] target, string source, int from, int to)
    {
        for (var k = from; k < to && k < source.Length; k++)
        {
            if (source[k] is not ('\n' or '\r'))
            {
                target[k] = ' ';
            }
        }
    }

    /// <summary>Scans code; inside an interpolation hole it stops AT the <c>}</c> that closes the hole (not consumed).</summary>
    private static void ScanCSharpCode(string s, ref int i, char[] text, char[] code, List<(int, int)> literals, bool inHole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var start = i;
                while (i < s.Length && s[i] != '\n') i++;
                Blank(text, s, start, i);
                Blank(code, s, start, i);
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i = Math.Min(s.Length, i + 2);
                Blank(text, s, start, i);
                Blank(code, s, start, i);
                continue;
            }

            if (c == '\'')
            {
                var start = i++;
                if (i < s.Length && s[i] == '\\')
                {
                    i += 2;
                    while (i < s.Length && s[i] != '\'' && s[i] != '\n') i++;
                }
                else
                {
                    i++;
                }

                if (i < s.Length && s[i] == '\'') i++;
                Blank(code, s, start + 1, Math.Max(start + 1, i - 1));
                continue;
            }

            if (TryScanCSharpString(s, ref i, text, code, literals))
            {
                continue;
            }

            if (inHole)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) return;
                    depth--;
                }
            }

            i++;
        }
    }

    private static bool TryScanCSharpString(string s, ref int i, char[] text, char[] code, List<(int, int)> literals)
    {
        // Prefix: $… then optional @, or @ then $…
        var k = i;
        var dollars = 0;
        var verbatim = false;
        if (k < s.Length && s[k] == '@')
        {
            verbatim = true;
            k++;
        }

        while (k < s.Length && s[k] == '$')
        {
            dollars++;
            k++;
        }

        if (!verbatim && k < s.Length && s[k] == '@')
        {
            verbatim = true;
            k++;
        }

        if (k >= s.Length || s[k] != '"')
        {
            return false;
        }

        var start = i;
        var quotes = 0;
        while (k + quotes < s.Length && s[k + quotes] == '"') quotes++;

        if (quotes >= 3 && !verbatim)
        {
            // Raw string literal: closes at the same number of quotes; holes open at `dollars` braces.
            i = k + quotes;
            var contentStart = i;
            while (i < s.Length)
            {
                if (s[i] == '"')
                {
                    var run = 0;
                    while (i + run < s.Length && s[i + run] == '"') run++;
                    if (run >= quotes)
                    {
                        Blank(code, s, contentStart, i);
                        i += quotes;
                        literals.Add((start, i));
                        return true;
                    }

                    i += run;
                    continue;
                }

                if (dollars > 0 && s[i] == '{')
                {
                    var run = 0;
                    while (i + run < s.Length && s[i + run] == '{') run++;
                    if (run >= dollars)
                    {
                        Blank(code, s, contentStart, i + run - dollars);
                        i += run;
                        ScanCSharpCode(s, ref i, text, code, literals, inHole: true);
                        var closed = 0;
                        while (i < s.Length && s[i] == '}' && closed < dollars)
                        {
                            i++;
                            closed++;
                        }

                        contentStart = i;
                        continue;
                    }

                    i += run;
                    continue;
                }

                i++;
            }

            Blank(code, s, contentStart, i);
            literals.Add((start, i));
            return true;
        }

        // Regular / verbatim (both may be interpolated).
        i = k + 1;
        var segmentStart = i;
        while (i < s.Length)
        {
            var c = s[i];
            if (!verbatim && c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '"')
            {
                if (verbatim && i + 1 < s.Length && s[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                Blank(code, s, segmentStart, i);
                i++;
                literals.Add((start, i));
                return true;
            }

            if (dollars > 0 && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }

                Blank(code, s, segmentStart, i);
                i++;
                ScanCSharpCode(s, ref i, text, code, literals, inHole: true);
                if (i < s.Length) i++; // the closing '}'
                segmentStart = i;
                continue;
            }

            if (c == '\n' && !verbatim)
            {
                break; // unterminated — stop at the line end
            }

            i++;
        }

        Blank(code, s, segmentStart, i);
        literals.Add((start, i));
        return true;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal", "static", "readonly", "const", "new", "override", "virtual",
        "abstract", "sealed", "async", "extern", "unsafe", "volatile", "partial", "required", "file", "event", "implicit",
        "explicit", "operator", "return", "await", "var", "void", "using", "if", "for", "foreach", "while", "switch",
        "catch", "lock", "fixed", "nameof", "typeof", "sizeof", "default", "get", "set", "init", "add", "remove", "this",
        "base", "where", "in", "out", "ref", "params", "throw", "is", "as", "class", "struct", "record", "interface", "enum",
    };

    /// <summary>
    /// The members of a file (from <see cref="CSharpSource.Code"/>): every declaration at namespace/type level — method,
    /// constructor, property, field, constant, event — and every top-level statement, with its span and declared name(s).
    /// </summary>
    private static List<CSharpMember> ParseMembers(string code)
    {
        var members = new List<CSharpMember>();
        var stack = new Stack<char>(); // 'C' container (namespace/type), 'M' member body, 'I' inner block
        var declStart = 0;
        var memberStart = -1;
        string? memberHeader = null;
        int topLevelStart = -1, topLevelEnd = -1;
        bool InContainer() => stack.Count == 0 || stack.Peek() == 'C';

        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '{')
            {
                if (InContainer())
                {
                    var header = code[declStart..i];
                    if (IsContainerHeader(header))
                    {
                        stack.Push('C');
                        declStart = i + 1;
                    }
                    else
                    {
                        stack.Push('M');
                        memberStart = declStart;
                        memberHeader = header;
                    }
                }
                else
                {
                    stack.Push('I');
                }
            }
            else if (c == '}')
            {
                var kind = stack.Count > 0 ? stack.Pop() : 'C';
                if (kind == 'M' && InContainer())
                {
                    // A property's accessor block may be followed by an initializer: `{ get; } = "…";` — that ';' ends it.
                    var j = i + 1;
                    while (j < code.Length && char.IsWhiteSpace(code[j])) j++;
                    if (j < code.Length && code[j] == '=' && (j + 1 >= code.Length || code[j + 1] != '='))
                    {
                        declStart = memberStart;
                        continue;
                    }

                    Add(DescribeMember(memberHeader!, memberStart, i + 1));
                    declStart = i + 1;
                }
                else if (kind == 'C')
                {
                    declStart = i + 1;
                }
            }
            else if (c == ';' && InContainer())
            {
                var declaration = code[declStart..i];
                var trimmed = StripAttributes(declaration).Trim();
                if (trimmed.Length > 0
                    && !Regex.IsMatch(trimmed, @"^(?:global\s+)?using\b|^namespace\b|^extern\s+alias\b"))
                {
                    Add(DescribeMember(declaration, declStart, i + 1));
                }

                declStart = i + 1;
            }
        }

        // Top-level statements (a declaration outside every namespace and type) are ONE body — Main's — so a URL in one
        // statement and the POST in another are judged together. C# puts them before any type, so the span overlaps none.
        if (topLevelStart >= 0)
        {
            members.Insert(0, new CSharpMember(new[] { "<top-level statements>" }, topLevelStart, topLevelEnd, IsMethod: true));
        }

        return members;

        void Add(CSharpMember member)
        {
            if (stack.Count == 0)
            {
                topLevelStart = topLevelStart < 0 ? member.Start : Math.Min(topLevelStart, member.Start);
                topLevelEnd = Math.Max(topLevelEnd, member.End);
                return;
            }

            members.Add(member);
        }
    }

    private static bool IsContainerHeader(string header) =>
        Regex.IsMatch(StripAttributes(header), @"\b(?:namespace|class|struct|interface|enum|record)\s+@?[A-Za-z_][\w.]*");

    private static string StripAttributes(string header) =>
        Regex.Replace(header, @"^\s*(?:\[(?>[^\[\]]+|\[(?<a>)|\](?<-a>))*(?(a)(?!))\]\s*)+", string.Empty);

    /// <summary>A member from its declaration text (up to its body): name(s), whether it is a method, whether expression-bodied.</summary>
    private static CSharpMember DescribeMember(string declaration, int start, int end)
    {
        var (names, isMethod) = MemberNames(declaration);
        return new CSharpMember(names, start, end, isMethod, ExpressionBodied: declaration.Contains("=>", StringComparison.Ordinal));
    }

    /// <summary>The name(s) a declaration introduces — a method's name, or a property's / field's declarator(s).</summary>
    private static (IReadOnlyList<string> Names, bool IsMethod) MemberNames(string declaration)
    {
        var d = StripAttributes(declaration);
        int Find(string pattern) => Regex.Match(d, pattern) is { Success: true } m ? m.Index : int.MaxValue;
        var assign = Find(@"(?<![=!<>])=(?![=>])");
        var arrow = Find(@"=>");
        var brace = Find(@"\{");
        var method = Regex.Matches(d, @"(?<name>@?[A-Za-z_]\w*)\s*(?:<(?>[^<>]+|<(?<g>)|>(?<-g>))*(?(g)(?!))>)?\s*\(")
            .FirstOrDefault(m => !CSharpKeywords.Contains(m.Groups["name"].Value.TrimStart('@')));
        if (method is not null && method.Index < Math.Min(assign, Math.Min(arrow, brace)))
        {
            return (new[] { method.Groups["name"].Value.TrimStart('@') }, true);
        }

        var names = Regex.Matches(d, @"(?<name>@?[A-Za-z_]\w*)\s*(?:(?<![=!<>])=(?![=>])|=>|\{|$)")
            .Select(m => m.Groups["name"].Value.TrimStart('@'))
            .Where(n => !CSharpKeywords.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return (names.Count > 0 ? names : new[] { "<statement>" }, false);
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

    /// <summary>
    /// Every way a script asks for a POST: <c>-Method Post</c> and every abbreviation PowerShell accepts for it (round 49:
    /// <c>-Meth Post</c> was missed — any unambiguous prefix of a parameter name binds it, so <c>-Me…</c>), <c>-CustomMethod
    /// POST</c>, a splat's <c>Method = 'Post'</c>, <c>az rest --method post</c>, <c>curl -X POST</c>, an enum
    /// (<c>[…]::Post</c>), an HttpClient's <c>.PostAsync(</c>, a helper's positional <c>'Post'</c>.
    /// </summary>
    private static readonly Regex ScriptPostSignal = new(
        @"-(?:Me|Cu)[a-z]*\s*:?\s*['""]?Post\b|\bMethod\s*=\s*['""]?Post\b|--method\s+['""]?post\b|-X\s+['""]?POST\b|\]::Post\b" +
        @"|\.Post(?:AsJson)?Async\s*\(|(?<![\w-])['""]Post['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The URI a command names: <c>-Uri</c> or any abbreviation of it (<c>-Ur…</c>), with a space or a colon; <c>--uri</c>/<c>--url</c>.</summary>
    private static readonly Regex ScriptNamedUri = new(
        @"(?:-Ur[a-z]*(?:\s*:\s*|\s+)|--ur[il]\s+)(?<arg>""[^""]*""|'[^']*'|\$[\w:]+|\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptQuotedPath = new(@"""[^""\n]*/[^""\n]*""|'[^'\n]*/[^'\n]*'", RegexOptions.Compiled);

    /// <summary>A variable assigned the BARE collection URL (no query — a filtered GET of the collection is not a create target).</summary>
    private static readonly Regex ScriptCollectionAssignment = new(
        @"^\s*\$(?:script:|global:)?(?<name>\w+)\s*=\s*[""'][^""'\n]*fileStorage/containers[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>A dot-sourced or imported script file named on a line (<c>. (Join-Path $PSScriptRoot 'x.ps1')</c>, <c>. "$PSScriptRoot/x.ps1"</c>, <c>Import-Module ./x.psm1</c>).</summary>
    private static readonly Regex ScriptInclude = new(
        @"^\s*(?:\.|Import-Module)\s+(?<rest>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ScriptIncludedFile = new(@"[\w\-./\\]+\.psm?1", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// What a script sees besides its own text (owner round 49: a URL variable defined in a dot-sourced helper and POSTed
    /// in the script was missed, because the script itself never named the collection).
    /// </summary>
    /// <param name="IncludedText">Every file the script dot-sources or imports (resolved against its folder, recursively), comment-stripped.</param>
    /// <param name="CollectionVariables">Variables assigned the bare collection URL in ANY script — a script that uses one it never assigns gets the collection from elsewhere (fail closed).</param>
    private sealed record ScriptContext(string IncludedText, IReadOnlySet<string> CollectionVariables)
    {
        public static ScriptContext None { get; } = new(string.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The context of every script in <paramref name="scripts"/> (full path → text), keyed by full path.</summary>
    private static Dictionary<string, ScriptContext> ScriptContexts(IReadOnlyDictionary<string, string> scripts)
    {
        var collectionVariables = scripts.Values
            .SelectMany(text => ScriptCollectionAssignment.Matches(StripPowerShellComments(text)).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byPath = scripts.ToDictionary(kv => NormalizeScriptPath(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return scripts.Keys.ToDictionary(
            path => path,
            path =>
            {
                var included = new StringBuilder();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeScriptPath(path) };
                Include(path, depth: 0);
                return new ScriptContext(included.ToString(), collectionVariables);

                void Include(string from, int depth)
                {
                    if (depth > 4 || !byPath.TryGetValue(NormalizeScriptPath(from), out var text))
                    {
                        return;
                    }

                    var folder = Path.GetDirectoryName(NormalizeScriptPath(from)) ?? string.Empty;
                    foreach (Match line in ScriptInclude.Matches(StripPowerShellComments(text)))
                    {
                        // $PSScriptRoot is the including script's folder; a path relative to it is relative to `folder`.
                        var rest = Regex.Replace(line.Groups["rest"].Value, @"\$\{?PSScriptRoot\}?[/\\]?", string.Empty, RegexOptions.IgnoreCase);
                        foreach (Match file in ScriptIncludedFile.Matches(rest))
                        {
                            var relative = file.Value.StartsWith("./", StringComparison.Ordinal) || file.Value.StartsWith(".\\", StringComparison.Ordinal)
                                ? file.Value[2..]
                                : file.Value;
                            var target = NormalizeScriptPath(Path.Combine(folder, relative));
                            if (seen.Add(target) && byPath.TryGetValue(target, out var includedText))
                            {
                                included.Append('\n').Append(StripPowerShellComments(includedText));
                                Include(target, depth + 1);
                            }
                        }
                    }
                }
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeScriptPath(string path) =>
        Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

    [Fact(DisplayName = "Every SPE container a script creates is bound to its business unit, or removed")]
    public void EveryScriptThatCreatesAContainer_BindsIt()
    {
        // scripts/, and any PowerShell under src/ (round 35 item 1: "a container-create call site in src/ or scripts/").
        var scripts = new[] { "scripts", "src" }
            .SelectMany(root => SourceFiles(Path.Combine(RepoRoot, root), "*.ps1")
                .Concat(SourceFiles(Path.Combine(RepoRoot, root), "*.psm1")))
            .ToDictionary(f => f, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var contexts = ScriptContexts(scripts);

        var creators = scripts.Where(s => ScriptCreations(s.Value, contexts[s.Key]).Count > 0).ToList();
        Assert.True(creators.Count >= 3,
            "the scan must find the script creation paths (New-BusinessUnitContainer, Provision-Customer, " +
            $"Create-NewContainerType) — found {creators.Count}, so it would pass vacuously");

        var violations = creators
            .SelectMany(s => UnboundScriptCreations(s.Value, contexts[s.Key]).Select(v => $"{Rel(s.Key)}: {v}"))
            .ToList();
        Assert.True(violations.Count == 0,
            "These scripts create (or may create) an SPE container without binding it (owner rounds 35/41/49: call " + ScriptBinder +
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
        // Owner round 49 — the verifier's surviving PowerShell shape: an abbreviated parameter name.
        const string abbreviatedMethod = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $c = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Meth Post -Body $b
            """;
        const string abbreviatedUriAndColon = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $c = Invoke-WebRequest -Ur:"https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -Me:Post -Body $b
            """;
        const string customMethod = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $c = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" -CustomMethod 'POST' -Body $b
            """;
        const string httpClient = """
            . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
            $r = $client.PostAsync("https://graph.microsoft.com/v1.0/storage/fileStorage/containers", $content).Result
            """;

        Assert.Empty(UnboundScriptCreations(bound, ScriptContext.None));
        Assert.Empty(ScriptCreations(otherPosts, ScriptContext.None));
        Assert.Empty(ScriptCreations(notACreate, ScriptContext.None));
        foreach (var (name, seeded) in new[]
                 {
                     ("unbound", unbound), ("noModule", noModule), ("uriInVariable", uriInVariable),
                     ("uriBuiltFromABase", uriBuiltFromABase), ("splatted", splatted), ("uriFromAFunction", uriFromAFunction),
                     ("azRest", azRest), ("sdkCmdlet", sdkCmdlet), ("abbreviatedMethod", abbreviatedMethod),
                     ("abbreviatedUriAndColon", abbreviatedUriAndColon), ("customMethod", customMethod), ("httpClient", httpClient),
                 })
        {
            Assert.True(UnboundScriptCreations(seeded, ScriptContext.None).Count > 0, $"the analyser must flag the seeded '{name}' creation");
        }

        // Owner round 49 — the verifier's cross-file shape: the URL variable defined in a dot-sourced helper, POSTed in a
        // script that never names the collection. Flagged whether or not the helper's path can be resolved.
        var helper = Path.Combine(RepoRoot, "scripts", "seeded", "common", "GraphUris.ps1");
        var poster = Path.Combine(RepoRoot, "scripts", "seeded", "New-Thing.ps1");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [helper] = "$ContainersUri = \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers\"\n",
            [poster] = ". (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')\n. (Join-Path $PSScriptRoot 'common/GraphUris.ps1')\n" +
                       "$c = Invoke-RestMethod -Uri $ContainersUri -Method Post -Body $b\n",
        };
        var resolved = ScriptContexts(files);
        Assert.True(resolved[poster].IncludedText.Contains("ContainersUri", StringComparison.Ordinal), "the dot-sourced helper is read");
        Assert.True(UnboundScriptCreations(files[poster], resolved[poster]).Count > 0,
            "the analyser must follow a URL variable into the script that dot-sources it");
        var unresolved = new ScriptContext(string.Empty, resolved[poster].CollectionVariables);
        Assert.True(UnboundScriptCreations(files[poster], unresolved).Count > 0,
            "a variable some script assigns the collection is the collection even where the helper cannot be resolved");
    }

    /// <summary>
    /// The offsets of every container CREATE (or possible create) in a script. Fail closed (owner rounds 41 + 49): in a
    /// script that names the containers collection — itself, in a file it dot-sources, or through a variable some script
    /// assigns the collection — a POST whose URI the analyser cannot prove is another endpoint (a variable it cannot resolve
    /// to literals, a splat, a function's result) counts as a create.
    /// </summary>
    private static List<int> ScriptCreations(string script, ScriptContext context)
    {
        var text = StripPowerShellComments(script);
        var all = text + "\n" + context.IncludedText;
        var creations = ScriptSdkCreate.Matches(text).Select(m => m.Index).ToList();

        var usesACollectionVariable = Regex.Matches(text, @"\$(?:script:|global:)?(?<name>\w+)")
            .Any(m => context.CollectionVariables.Contains(m.Groups["name"].Value));
        var namesCollection = CollectionUrl.IsMatch(all)
                              || (all.Contains("fileStorage", StringComparison.OrdinalIgnoreCase) && QuotedRelativeCollection.IsMatch(all))
                              || usesACollectionVariable;
        if (!namesCollection)
        {
            return creations;
        }

        foreach (Match post in ScriptPostSignal.Matches(text))
        {
            if (PostMayCreateAContainer(all, LogicalCommand(text, post.Index), context.CollectionVariables))
            {
                creations.Add(post.Index);
            }
        }

        return creations;
    }

    /// <summary>Whether one POST command (in a script that names the collection) may create a container.</summary>
    private static bool PostMayCreateAContainer(string script, string command, IReadOnlySet<string> collectionVariables)
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
                // A variable: every assignment of it the script can see must be a literal that is provably another endpoint.
                var bare = arg.TrimStart('$');
                bare = Regex.Replace(bare, "^(?:script|global):", string.Empty, RegexOptions.IgnoreCase);
                if (collectionVariables.Contains(bare))
                {
                    return true; // some script assigns it the collection
                }

                var name = Regex.Escape(bare);
                var assignments = Regex.Matches(script, $@"^\s*\$(?:script:|global:)?{name}\s*=\s*(?<rhs>.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
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

    private static List<string> UnboundScriptCreations(string script, ScriptContext context)
    {
        var text = StripPowerShellComments(script);
        var violations = new List<string>();
        var creations = ScriptCreations(script, context);
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
        // The exact refusal shape — "if the name is not allowed, throw" — so a weakened condition does not pass as a judge.
        var judge = Regex.Match(backfill,
            @"if\s*\(\s*-not\s*\(\s*Test-SpeConfigSecretNameAllowed\s+\$SecretName\s*\)\s*\)\s*\{\s*throw\b", RegexOptions.IgnoreCase);
        var judged = judge.Success ? judge.Index : -1;
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

    /// <summary>A file of a test project (a directory named <c>*.Tests</c> on its path).</summary>
    private static bool IsTestProjectFile(string path) =>
        Rel(path).Split('/').Any(segment => segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));

    private static int LineOf(string text, int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

    /// <summary>
    /// The text (comments blanked, strings intact) of the first member whose DECLARATION contains
    /// <paramref name="signature"/> — members as <see cref="ParseMembers"/> finds them, so braces in string literals and
    /// expression-bodied members cannot throw the span off.
    /// </summary>
    private static string MethodBody(CSharpSource source, string signature)
    {
        foreach (var member in ParseMembers(source.Code))
        {
            var span = source.Code[member.Start..member.End];
            var bodyAt = new[] { span.IndexOf('{'), span.IndexOf("=>", StringComparison.Ordinal) }.Where(x => x >= 0).DefaultIfEmpty(span.Length).Min();
            if (span[..bodyAt].Contains(signature, StringComparison.Ordinal))
            {
                return source.Text[member.Start..member.End];
            }
        }

        throw new InvalidOperationException($"No member '{signature}' — update this guard.");
    }

    /// <summary>The source with its C# comments blanked (string literals intact — a URL's <c>//</c> is not a comment).</summary>
    private static string StripComments(string source) => LexCSharp(source).Text;

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
