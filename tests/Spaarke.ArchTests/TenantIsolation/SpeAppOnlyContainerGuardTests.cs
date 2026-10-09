using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// App-only SharePoint Embedded access in the BFF goes through <c>SpeContainerOwnershipGuard</c> — no other
/// path can obtain an app-only Graph client and point it at a container (task 227d, owner D28/D29).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every Model 1 stamp shares one container type, and each stamp's managed identity holds
/// application <c>full</c> on it, so an app-only token reaches every customer's container — Microsoft has no
/// per-container app scoping. The guard refuses any container the stamp neither configured nor created. A
/// single app-only SPE call that bypasses it reopens cross-customer reach, and nothing at runtime would
/// notice. So the bypass is made a build failure.</para>
///
/// <para><b>The rules.</b></para>
/// <list type="number">
///   <item><c>.ForApp()</c> is called only by the guard itself and by the files in
///   <see cref="NonSpeAppOnlyFiles"/> — mail, users and mail subscriptions, each with its reason. The set is
///   pinned both ways: a new caller fails, and so does a listed file that no longer calls it.</item>
///   <item>Those non-SPE files contain no SharePoint Embedded Graph path (<c>.Storage.FileStorage</c>,
///   <c>.Drives[</c>, <c>fileStorage/</c>, <c>/drives/</c>) — the allow-list cannot become a back door.</item>
///   <item><c>ForTypeWideOperation()</c> (the guard's client for work that names no single container) is
///   called only in <see cref="TypeWideCallers"/>: they filter every result that names a container and mark every
///   container they create. Container-TYPE operations (settings, permissions, creating a type) carry no
///   container data and are not filtered — Graph refuses them app-only, since a stamp's identity holds only
///   <c>FileStorageContainer.Selected</c>.</item>
///   <item><c>SpeAdminGraphService</c>'s type-wide and deleted-bin client accessors and <c>FilterOwnedAsync</c>
///   are used only in <see cref="SpeAdminTypeWideCallers"/>.</item>
///   <item>No type in <c>src/</c> derives from the guard — <c>IsOwnedAsync</c> is virtual only so tests can
///   supply a permissive double.</item>
///   <item>The Graph <c>.default</c> app scope appears only in the files that build Graph clients
///   (<see cref="GraphScopeFiles"/>) — a hand-rolled token for a raw HTTP call would bypass rule 1.</item>
/// </list>
///
/// <para><b>Maintenance.</b> A new app-only Graph caller that is NOT SharePoint Embedded (mail, users,
/// directory): add it to <see cref="NonSpeAppOnlyFiles"/> with the reason and the task. A new SPE caller: do
/// not add it anywhere — get the client from <c>SpeContainerOwnershipGuard.ForOwnedContainerAsync</c>. A new
/// type-wide SPE operation: add it to the facade (<c>ContainerOperations</c> / <c>SpeAdminGraphService</c> /
/// <c>SpeFileStore</c>), filter its results with <c>IsOwnedAsync</c>, and mark what it creates.</para>
///
/// <para>Comments are stripped before matching, so prose naming these calls never counts.</para>
/// </remarks>
public class SpeAppOnlyContainerGuardTests
{
    private const string GuardFile = "Infrastructure/Graph/SpeContainerOwnershipGuard.cs";

    /// <summary>Files allowed to call <c>.ForApp()</c> because they never touch SharePoint Embedded.</summary>
    private static readonly IReadOnlyDictionary<string, string> NonSpeAppOnlyFiles = new Dictionary<string, string>
    {
        ["Services/Registration/GraphUserService.cs"] = "Entra users: create, disable, licence (demo registration).",
        ["Services/Communication/IncomingCommunicationProcessor.cs"] = "Reads mailbox messages; its SPE writes go through SpeFileStore.",
        ["Services/Communication/GraphSubscriptionManager.cs"] = "Mailbox change subscriptions.",
        ["Services/Communication/MailboxVerificationService.cs"] = "Mailbox send/read verification.",
        ["Services/Communication/InboundPollingBackupService.cs"] = "Mailbox polling fallback.",
        ["Services/Communication/GraphMailFolderDeltaReader.cs"] = "Mail-folder delta reads.",
        ["Services/Communication/Channels/EmailChannelSender.cs"] = "sendMail.",
    };

    /// <summary>Facade classes allowed to use the guard's type-wide client (create / list / search / subscriptions).</summary>
    private static readonly IReadOnlySet<string> TypeWideCallers = new HashSet<string>(StringComparer.Ordinal)
    {
        GuardFile,
        "Infrastructure/Graph/ContainerOperations.cs",     // create → MarkOwnedAsync
        "Infrastructure/Graph/SpeAdminGraphService.cs",    // FilterOwnedAsync on lists/search; mark on create
        "Infrastructure/Graph/SpeFileStore.cs",            // subscription renew/delete (no container named)
    };

    /// <summary>Files that may name the Graph <c>.default</c> scope: the client factories and their options.</summary>
    private static readonly IReadOnlySet<string> GraphScopeFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "Infrastructure/Graph/GraphClientFactory.cs",
        "Infrastructure/Graph/CiamGraphClientFactory.cs",  // CIAM tenant users (external sign-in), not SPE
        "Configuration/GraphOptions.cs",
        "Configuration/AgentTokenOptions.cs",
    };

    /// <summary>A call OR a method-group use (<c>_factory.ForApp</c> passed as a delegate) — either hands out the client.</summary>
    private static readonly Regex ForAppCall = new(@"\.\s*ForApp\b", RegexOptions.Compiled);
    private static readonly Regex TypeWideCall = new(@"\bForTypeWideOperation\s*\(", RegexOptions.Compiled);
    private static readonly Regex GuardSubclass = new(@"[:,]\s*(?:[\w.]+\.)?SpeContainerOwnershipGuard\s*(?:\(|\{|,|$|where\b)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GraphScope = new(@"graph\.microsoft\.com/\.default", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>
    /// Graph paths that can reach SharePoint Embedded content: containers, drives, a drive's root, app-only
    /// search (it spans every container the identity reaches), sharing links and site paths.
    /// </summary>
    private static readonly Regex SpeGraphPath = new(
        @"\.\s*Storage\s*\.\s*FileStorage\b|\.\s*Drives\s*\[|\.\s*Drive\b|\.\s*Search\s*\.\s*Query\b|\.\s*Shares\s*\[|\.\s*Sites\s*\["
        + @"|fileStorage/|/drives/|/search/query|/shares/|/sites/",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>SPE Admin's type-wide and deleted-bin client accessors and its result filter.</summary>
    private static readonly Regex SpeAdminTypeWideAccess = new(
        @"\b(GetTypeWideClientForConfigAsync|GetClientForDeletedContainerAsync|FilterOwnedAsync)\s*\(",
        RegexOptions.Compiled);

    /// <summary>Files allowed to use <see cref="SpeAdminTypeWideAccess"/>: the service itself and its dashboard sync (filters).</summary>
    private static readonly IReadOnlySet<string> SpeAdminTypeWideCallers = new HashSet<string>(StringComparer.Ordinal)
    {
        "Infrastructure/Graph/SpeAdminGraphService.cs",
        "Services/SpeAdmin/SpeDashboardSyncService.cs",   // lists the type's containers, then FilterOwnedAsync
    };

    private static readonly string BffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    // ─────────────────────────────────────────────────────────────────────────
    // Rules over the real source
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForApp_IsCalledOnlyByTheGuardAndTheListedNonSpeFiles()
    {
        var callers = FilesMatching(ForAppCall).ToHashSet(StringComparer.Ordinal);
        Assert.True(callers.Contains(GuardFile),
            "The guard is the SPE app-only client source; if it no longer calls ForApp() the scan itself is broken.");

        var unexpected = callers.Where(f => f != GuardFile && !NonSpeAppOnlyFiles.ContainsKey(f)).OrderBy(f => f).ToList();
        Assert.True(unexpected.Count == 0,
            "These files call IGraphClientFactory.ForApp() outside SpeContainerOwnershipGuard. An app-only Graph client " +
            "reaches EVERY customer's container of the shared type (owner D28). SPE work: get the client from " +
            "SpeContainerOwnershipGuard.ForOwnedContainerAsync. Non-SPE work (mail/users): add the file to " +
            "NonSpeAppOnlyFiles with its reason.\n  " + string.Join("\n  ", unexpected));

        var stale = NonSpeAppOnlyFiles.Keys.Where(f => !callers.Contains(f)).OrderBy(f => f).ToList();
        Assert.True(stale.Count == 0,
            "These NonSpeAppOnlyFiles entries no longer call ForApp(); remove them so the list stays an exact census:\n  " +
            string.Join("\n  ", stale));
    }

    [Fact]
    public void NonSpeAppOnlyFiles_ContainNoSharePointEmbeddedGraphPath()
    {
        var violations = NonSpeAppOnlyFiles.Keys
            .SelectMany(f => Matches(f, Code(f), SpeGraphPath))
            .ToList();

        Assert.True(violations.Count == 0,
            "A file allowed to call ForApp() because it is NOT SharePoint Embedded now reaches an SPE Graph path. " +
            "Route that call through SpeFileStore / SpeContainerOwnershipGuard instead:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void ForTypeWideOperation_IsCalledOnlyByTheFacadeClassesThatFilterTheirResults()
    {
        var unexpected = FilesMatching(TypeWideCall).Where(f => !TypeWideCallers.Contains(f)).OrderBy(f => f).ToList();

        Assert.True(unexpected.Count == 0,
            "ForTypeWideOperation() hands out a client that is NOT scoped to one owned container. Only the SPE facade " +
            "may use it, and only for create (then MarkOwnedAsync) or list/search (then IsOwnedAsync filtering):\n  " +
            string.Join("\n  ", unexpected));
    }

    [Fact]
    public void SpeAdminTypeWideClient_IsUsedOnlyWhereItsResultsAreFiltered()
    {
        var unexpected = FilesMatching(SpeAdminTypeWideAccess).Where(f => !SpeAdminTypeWideCallers.Contains(f)).OrderBy(f => f).ToList();

        Assert.True(unexpected.Count == 0,
            "SpeAdminGraphService's type-wide / deleted-bin client is NOT scoped to one owned container. Use " +
            "GetClientForContainerAsync for one container; a new type-wide use must filter with FilterOwnedAsync " +
            "and be added to SpeAdminTypeWideCallers with its reason:\n  " + string.Join("\n  ", unexpected));
    }

    [Fact]
    public void NoTypeInTheBffDerivesFromTheGuard()
    {
        var subclasses = FilesMatching(GuardSubclass).ToList();

        Assert.True(subclasses.Count == 0,
            "A type in src/ derives from SpeContainerOwnershipGuard. IsOwnedAsync is virtual only for test doubles; an " +
            "override in production code would switch the customer boundary off:\n  " + string.Join("\n  ", subclasses));
    }

    [Fact]
    public void GraphAppScope_AppearsOnlyInTheClientFactories()
    {
        var unexpected = FilesMatching(GraphScope).Where(f => !GraphScopeFiles.Contains(f)).OrderBy(f => f).ToList();

        Assert.True(unexpected.Count == 0,
            "These files name the Graph .default scope outside the Graph client factories — a hand-rolled app-only " +
            "token for raw HTTP would bypass SpeContainerOwnershipGuard. Use IGraphClientFactory (and, for SPE, the " +
            "guard):\n  " + string.Join("\n  ", unexpected));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Controls — each detector fires on a seeded violation and not on the sanctioned shape
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Detectors_NegativeControls_FireOnSeededViolations()
    {
        Assert.Matches(ForAppCall, Strip("var g = _graphClientFactory.ForApp();"));
        Assert.Matches(ForAppCall, Strip("var g = _factory\n    .ForApp( );"));
        Assert.Matches(ForAppCall, Strip("Func<GraphServiceClient> get = _factory.ForApp;"));
        Assert.Matches(SpeGraphPath, Strip("var hits = await g.Search.Query.PostAsQueryPostResponseAsync(body);"));
        Assert.Matches(SpeGraphPath, Strip("var item = await g.Shares[encoded].DriveItem.GetAsync();"));
        Assert.Matches(SpeGraphPath, Strip("var root = await g.Sites[siteId].Drive.Root.GetAsync();"));
        Assert.Matches(SpeAdminTypeWideAccess, Strip("var c = await _graphService.GetTypeWideClientForConfigAsync(config, ct);"));
        Assert.Matches(SpeGraphPath, Strip("await g.Storage.FileStorage.Containers[id].Permissions.GetAsync();"));
        Assert.Matches(SpeGraphPath, Strip("await g.Drives[driveId].Items[itemId].Content.GetAsync();"));
        Assert.Matches(SpeGraphPath, Strip("var url = $\"{baseUrl}/storage/fileStorage/containers/{id}\";"));
        Assert.Matches(TypeWideCall, Strip("var c = _ownership.ForTypeWideOperation();"));
        Assert.Matches(GuardSubclass, Strip("internal sealed class Open(IGraphClientFactory f) : SpeContainerOwnershipGuard(f, [], c, l)"));
        Assert.Matches(GuardSubclass, Strip("public class Open : Sprk.Bff.Api.Infrastructure.Graph.SpeContainerOwnershipGuard\n{"));
        Assert.Matches(GraphScope, Strip("var scopes = new[] { \"https://graph.microsoft.com/.default\" };"));
    }

    [Fact]
    public void Detectors_PositiveControls_DoNotFireOnTheSanctionedShape()
    {
        Assert.DoesNotMatch(ForAppCall, Strip("/// Uses <see cref=\"IGraphClientFactory.ForApp\"/>; never call .ForApp() here."));
        Assert.DoesNotMatch(ForAppCall, Strip("var g = await _ownership.ForOwnedContainerAsync(driveId, ct);"));
        Assert.DoesNotMatch(ForAppCall, Strip("GraphServiceClient ForApp();"));
        Assert.DoesNotMatch(ForAppCall, Strip("var g = await _ciam.CreateClientForAppAsync(ct);"));
        Assert.DoesNotMatch(SpeAdminTypeWideAccess, Strip("var c = await _graphService.GetClientForContainerAsync(config, id, ct);"));
        Assert.DoesNotMatch(SpeGraphPath, Strip("await g.Users[mailbox].Messages[id].GetAsync();"));
        Assert.DoesNotMatch(GuardSubclass, Strip("private readonly SpeContainerOwnershipGuard _ownership;"));
        Assert.DoesNotMatch(GuardSubclass, Strip("public Foo(SpeContainerOwnershipGuard ownership, ILogger<Foo> logger)"));
        Assert.DoesNotMatch(GraphScope, Strip("// scope: https://graph.microsoft.com/.default"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scanning
    // ─────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> FilesMatching(Regex pattern)
        => BffFiles().Where(f => pattern.IsMatch(Code(f)));

    private static IEnumerable<string> Matches(string file, string code, Regex pattern)
        => pattern.Matches(code).Select(m => $"{file}:{SourceScan.LineOf(code, m.Index)}: {m.Value.Trim()}");

    private static IEnumerable<string> BffFiles()
        => Directory.EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(BffRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string Code(string bffRelativeFile)
        => Strip(File.ReadAllText(Path.Combine(BffRoot, bffRelativeFile.Replace('/', Path.DirectorySeparatorChar))));

    /// <summary>
    /// Line comments (and therefore XML docs) removed, line structure kept for line numbers. String-aware:
    /// <c>SourceScan.CodeText</c> cuts at the first <c>//</c> anywhere, which would also cut
    /// <c>"https://graph.microsoft.com/.default"</c> and blind the scope rule.
    /// </summary>
    private static string Strip(string text) => string.Join("\n", text.Split('\n').Select(StripLineComment));

    private static string StripLineComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
            {
                return line[..i];
            }
        }

        return line;
    }
}
