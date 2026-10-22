using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 036 option C (owner 2026-10-20): no handler or filter file of the <c>/api/v1/external</c>
/// plane calls a token-taking (OBO) question of <c>CallerRecordAccessProbe</c> — <c>GetCallerRightsAsync</c>,
/// <c>GetCallerRightsForRecordsAsync</c>, <c>GetCallerRightsForRecordsOrUnknownAsync</c> (ebilling-r1 081),
/// <c>GetCallerRightsForAuthorizationGateAsync</c>, <c>CallerHoldsPrivilegeAsync</c>,
/// <c>GetCallerSystemUserIdAsync</c> — or any other OBO entry point the BFF has (the MSAL exchange itself, the Graph
/// <c>ForUser*</c> clients, the SpeFileStore <c>*AsUserAsync</c> methods, the OBO Dataverse client
/// <c>IDataverseUserClient</c>, <c>GrantExternalAccessEndpoint.ProbeGrantorCeilingAsync</c>), or reads the caller's bearer
/// token at all (<c>TokenHelper.ExtractBearerToken*</c>: every OBO question needs it, so a plane file never does).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> ADR-028 A1–A3 (restated by A4, A5, A8 and A9) forbid exchanging the caller's token for a downstream
/// token on this plane. The plane's write confirmation asks Dataverse app-only, naming the resolved systemuser
/// (<c>GetPrincipalRightsAppOnlyOrUnknownAsync</c> / <c>PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync</c>, A5's mechanism). The OBO questions
/// stay on the probe for the internal routes. Before option C the plane's readers did call the OBO questions for
/// Spaarke-tenant systemusers (<c>ExternalProjectDataEndpoints.CallerRightsReader</c> / <c>CallerPrivilegeReader</c>),
/// and every behaviour test still passed — so the rule lives here, where the edit that reintroduces it fails the build
/// (CLAUDE.md §16 enforcement ladder). Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// <para><b>Which files</b> (fix round 3, F3-1). <c>Api/ExternalAccess/External*.cs</c>, plus every file DERIVED from the
/// <c>MapGroup("/api/v1/external")</c> mappings (<see cref="BoundFiles"/>): the BFF extension each group or route chain
/// calls (a filter such as <c>AddCallerPrincipalAuthorizationFilter</c>, a mapping such as
/// <c>MapContactGrantEndpoints</c>, a declaration such as <c>RequireWorkforceCeiling</c>), each <c>Type.Member</c> handler
/// or delegate a route chain names (<c>MeEntitlementsEndpoint.Handle</c>, <c>WorkforceRoleCeilingGate.*</c>), and each
/// filter class those files attach (<c>AddEndpointFilter&lt;T&gt;</c>, <c>new …Filter(</c>), followed to closure. Only
/// files under <c>Api/</c> are followed. A filter or handler bound to the group later is scanned from its first commit,
/// with no list to update; <see cref="Control_TheDerivation_FindsTheGroupsKnownFilesAndHandlers"/> fails if the derivation
/// itself stops finding the files bound today, and <see cref="Control_EveryEndpointFilterInExternalAccess_IsScannedOrDeclaredOffThePlane"/>
/// fails if a filter is added to the folder that the derivation does not reach.
/// <see cref="Control_EveryLiveExternalRoute_IsRegisteredInAScannedFile"/> (fix round 4, K1) closes the other direction:
/// every live <c>/api/v1/external</c> route in the route census (<c>RouteAuthorizationGuardTests</c>' whole-BFF scan,
/// <c>Program.cs</c> and <c>Infrastructure/</c> included) must be registered in a scanned file, so a route bound to the
/// plane in a way the derivation does not anticipate fails the build instead of escaping the scan.</para>
/// <para><b>Scope, and what it does not prove.</b> A source scan of those files, with line comments stripped
/// (<see cref="SourceScan"/>); it does not follow calls into services they use (<c>CallerPrincipalResolver</c>,
/// <c>AccessibleRecordSetService</c>, <c>ExternalDataService</c> …). Today none of those services performs OBO (task 036
/// note <c>036-obo-resolution.md</c> §1–§2), and the readers above are the plane's only route to the probe.</para>
/// <para>Each rule has a must-fire and a must-not-fire control below, and vacuity checks on the file set.</para>
/// </remarks>
public class ExternalPlaneNoOboGuardTests
{
    private const string ExternalAccessDir = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess";
    private const string BffApiDir = "src/server/api/Sprk.Bff.Api/Api";
    private const string PlaneRoute = "/api/v1/external";

    /// <summary>The files that must be in the scanned set, so the guard can never pass by scanning nothing.</summary>
    private static readonly string[] MustBeScanned =
    {
        "Api/ExternalAccess/ExternalProjectDataEndpoints.cs",
        "Api/ExternalAccess/ExternalMessageEndpoints.cs",
        "Api/ExternalAccess/MeEntitlementsEndpoint.cs",
        "Api/ExternalAccess/ContactGrantEndpoints.cs",
        "Api/ExternalAccess/ContactGrantorAuthorizationFilter.cs",
        "Api/ExternalAccess/WorkforceCeilingEndpointFilter.cs",
        "Api/Filters/CallerPrincipalAuthorizationFilter.cs",
    };

    /// <summary>
    /// Endpoint filters in <c>Api/ExternalAccess/</c> that are NOT bound to <c>/api/v1/external</c>, each with where it is
    /// bound. Any other filter there must be reached by <see cref="BoundFiles"/>.
    /// </summary>
    private static readonly Dictionary<string, string> FiltersOffThePlane = new(StringComparer.Ordinal)
    {
        ["DelegationRuleFilter.cs"] = "the /api/v1/external-access management group (AddDelegationRuleFilter); asks OBO by design",
    };

    private static readonly (Regex Pattern, string Why)[] Forbidden =
    {
        (new Regex(
                @"\b(GetCallerRightsAsync|GetCallerRightsForRecordsAsync|GetCallerRightsForRecordsOrUnknownAsync|GetCallerRightsForAuthorizationGateAsync|CallerHoldsPrivilegeAsync|GetCallerSystemUserIdAsync)\b",
                RegexOptions.CultureInvariant),
            "a token-taking (OBO) CallerRecordAccessProbe question — use GetPrincipalRightsAppOnlyOrUnknownAsync / "
            + "PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync with the resolved systemuser (ExternalProjectDataEndpoints.CallerRightsReader)"),
        (new Regex(@"\b(AcquireTokenOnBehalfOf|CreateOnBehalfOfClientAsync)\b", RegexOptions.CultureInvariant),
            "an on-behalf-of token exchange"),
        (new Regex(@"\b(ForUserAsync|ForUserBetaAsync)\b", RegexOptions.CultureInvariant),
            "a Graph client built on behalf of the caller (GraphClientFactory OBO)"),
        (new Regex(@"\b\w+AsUserAsync\b", RegexOptions.CultureInvariant),
            "an SPE / Graph call made as the user (SpeFileStore *AsUserAsync, OBO)"),
        (new Regex(@"\bI?DataverseUserClient\b", RegexOptions.CultureInvariant),
            "the OBO Dataverse client (IDataverseUserClient)"),
        (new Regex(@"\bExtractBearerToken\w*", RegexOptions.CultureInvariant),
            "reads the caller's bearer token (TokenHelper.ExtractBearerToken*) — every OBO question needs it, and nothing "
            + "else on this plane does"),
        (new Regex(@"\bProbeGrantorCeilingAsync\b", RegexOptions.CultureInvariant),
            "GrantExternalAccessEndpoint's grantor-ceiling probe, which asks GetCallerRightsAsync with the caller's token "
            + "(OBO) — a contact grantor's ceiling is GrantCeiling.FromContactGrantorRights"),
    };

    /// <summary>The rule over one file's code text (line comments stripped): each violation, with its line.</summary>
    internal static IReadOnlyList<string> Violations(string code)
    {
        var violations = new List<string>();
        foreach (var (pattern, why) in Forbidden)
        {
            foreach (Match match in pattern.Matches(code))
            {
                var line = code.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add($"line {line}: `{match.Value}` — {why}");
            }
        }

        return violations;
    }

    // ── The scanned set ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The plane's files: <c>Api/ExternalAccess/External*.cs</c> plus <see cref="BoundFiles"/>, as full paths.</summary>
    internal static IReadOnlyList<string> ScannedFiles() =>
        Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, ExternalAccessDir), "External*.cs", SearchOption.TopDirectoryOnly)
            .Concat(BoundFiles().Select(relative => Path.Combine(SourceScan.RepoRoot, BffApiDir, relative)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>A scanned path relative to <c>src/server/api/Sprk.Bff.Api/</c>, with forward slashes.</summary>
    private static string BffRelative(string fullPath) =>
        Path.GetRelativePath(Path.Combine(SourceScan.RepoRoot, "src/server/api/Sprk.Bff.Api"), fullPath).Replace('\\', '/');

    private static readonly Regex GroupDeclaration = new(
        @"\b(?:var\s+)?(\w+)\s*=\s*\w+\s*\.\s*MapGroup\s*\(\s*""" + Regex.Escape(PlaneRoute) + @"""\s*\)[^;]*;",
        RegexOptions.CultureInvariant);

    private static readonly Regex RouteStatement = new(
        @"\b\w+\s*\.\s*Map(?:Get|Post|Put|Patch|Delete|Methods|Group)\s*\([^;]*;", RegexOptions.CultureInvariant);

    private static readonly Regex ExtensionCall = new(@"\.\s*(\w+)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex TypeMember = new(@"\b([A-Z]\w*)\s*\.\s*[A-Z]\w*\b", RegexOptions.CultureInvariant);
    private static readonly Regex TypedFilter = new(@"\bAddEndpointFilter\s*<\s*(?:\w+\s*,\s*)?(\w+)\s*>", RegexOptions.CultureInvariant);
    private static readonly Regex NewFilter = new(@"\bnew\s+(\w+Filter)\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex ExtensionDefinition = new(
        @"\bstatic\s+[^=;{}()]*?\b(\w+)\s*(?:<[^<>()]*>)?\s*\(\s*this\b", RegexOptions.CultureInvariant);

    private static readonly Regex TypeDefinition = new(@"\b(?:class|record|struct|interface)\s+(\w+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// What one chunk of mapping code binds to its routes: BFF extensions it calls on a group or route chain, and types it
    /// names as a handler, delegate or filter. Names only; <see cref="BoundFiles"/> resolves them to files under
    /// <c>Api/</c> and drops the rest (framework methods such as <c>WithName</c>, <c>StatusCodes</c>).
    /// </summary>
    internal static (IReadOnlySet<string> Extensions, IReadOnlySet<string> Types) BindingReferences(string routeChunk, string wholeFile)
    {
        var extensions = ExtensionCall.Matches(routeChunk).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var types = TypeMember.Matches(routeChunk).Select(m => m.Groups[1].Value)
            .Concat(TypedFilter.Matches(wholeFile).Select(m => m.Groups[1].Value))
            .Concat(NewFilter.Matches(wholeFile).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        return (extensions, types);
    }

    /// <summary>
    /// The files bound to <c>/api/v1/external</c>, DERIVED from its mappings rather than listed: start at every
    /// <c>MapGroup("/api/v1/external")</c> declaration and the statements on that group variable, then follow each
    /// referenced BFF extension and type (see <see cref="BindingReferences"/>) to its defining file under <c>Api/</c>, and
    /// repeat over each file found (its route statements, and its filter attachments) until nothing new is found.
    /// Paths are relative to <c>src/server/api/Sprk.Bff.Api/Api/</c>.
    /// </summary>
    internal static IReadOnlyList<string> BoundFiles()
    {
        var root = Path.Combine(SourceScan.RepoRoot, BffApiDir);
        var code = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), f => SourceScan.CodeText(File.ReadAllLines(f)),
                StringComparer.Ordinal);

        var extensionFiles = DefinitionIndex(code, ExtensionDefinition);
        var typeFiles = DefinitionIndex(code, TypeDefinition);

        var found = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        void Follow(string chunk, string wholeFile)
        {
            var (extensions, types) = BindingReferences(chunk, wholeFile);
            var files = extensions.SelectMany(n => extensionFiles.TryGetValue(n, out var f) ? f : Enumerable.Empty<string>())
                .Concat(types.SelectMany(n => typeFiles.TryGetValue(n, out var f) ? f : Enumerable.Empty<string>()));
            foreach (var file in files)
            {
                if (found.Add(file))
                {
                    queue.Enqueue(file);
                }
            }
        }

        // The roots: each /api/v1/external group declaration and the statements made on its variable — never the rest of
        // the file (ExternalAccessEndpoints.cs also maps the /api/v1/external-access management group).
        foreach (var (file, text) in code)
        {
            foreach (Match declaration in GroupDeclaration.Matches(text))
            {
                found.Add(file);
                Follow(declaration.Value, string.Empty);
                var onGroup = new Regex(@"(?<![\w.])" + Regex.Escape(declaration.Groups[1].Value) + @"\s*\.[^;]*;",
                    RegexOptions.CultureInvariant);
                foreach (Match statement in onGroup.Matches(text))
                {
                    Follow(statement.Value, string.Empty);
                }
            }
        }

        while (queue.Count > 0)
        {
            var file = queue.Dequeue();
            var text = code[file];
            var routes = string.Join("\n", RouteStatement.Matches(text).Select(m => m.Value));
            Follow(routes, text);
        }

        return found.ToList();
    }

    private static Dictionary<string, List<string>> DefinitionIndex(Dictionary<string, string> code, Regex definition)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (file, text) in code)
        {
            foreach (Match match in definition.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (!index.TryGetValue(name, out var files))
                {
                    index[name] = files = new List<string>();
                }

                if (!files.Contains(file))
                {
                    files.Add(file);
                }
            }
        }

        return index;
    }

    // ── The rule ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 036 C: no /api/v1/external handler or filter file reaches an OBO question or exchange (ADR-028 A1-A3)")]
    public void ExternalPlaneHandlers_CallNoOboEntryPoint()
    {
        var files = ScannedFiles();

        var violations = files
            .SelectMany(file => Violations(SourceScan.CodeText(File.ReadAllLines(file)))
                .Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/')} {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "The /api/v1/external plane must never exchange the caller's token (ADR-028 A1-A3; task 036 option C). "
            + "Confirm a systemuser's write app-only, naming the resolved systemuser:\n  " + string.Join("\n  ", violations));
    }

    // ── Vacuity and derivation controls ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 036 C guard, vacuity control: the scanned set holds the plane's handler and filter files")]
    public void Control_TheScannedSet_IsNotEmpty_AndHoldsTheKnownHandlers()
    {
        var names = ScannedFiles().Select(BffRelative).ToList();

        Assert.NotEmpty(names);
        foreach (var required in MustBeScanned)
        {
            Assert.Contains(required, names);
        }
    }

    [Fact(DisplayName = "Task 036 C guard, derivation control: the /api/v1/external mappings reach every file bound to the group today")]
    public void Control_TheDerivation_FindsTheGroupsKnownFilesAndHandlers()
    {
        // Through the derivation alone, not the External*.cs glob — so a regex that stops matching the mapping code fails
        // here instead of silently shrinking the scanned set.
        var bound = BoundFiles().Select(f => "Api/" + f).ToList();

        foreach (var required in MustBeScanned)
        {
            Assert.Contains(required, bound);
        }

        Assert.Contains("Api/ExternalAccess/WorkforceRoleCeilingGate.cs", bound);
    }

    [Fact(DisplayName = "Task 036 C guard, derivation must-not-fire control: the /api/v1/external-access management group is not followed")]
    public void Control_TheDerivation_DoesNotFollowTheManagementGroup()
    {
        var bound = BoundFiles();

        Assert.DoesNotContain("ExternalAccess/GrantExternalAccessEndpoint.cs", bound);
        Assert.DoesNotContain("ExternalAccess/DelegationRuleFilter.cs", bound);
        Assert.DoesNotContain("ExternalAccess/ProjectClosureEndpoint.cs", bound);
    }

    [Fact(DisplayName = "Task 036 C guard, derivation must-fire control: a new filter, declaration, mapping or handler on a route chain is followed")]
    public void Control_BindingReferences_FindANewBinding()
    {
        const string route = """
            group.MapPost("/things/{id:guid}", NewThingEndpoint.Handle)
                .AddNewThingAuthorizationFilter()
                .RequireNewThingCeiling(NewThingGate.Create)
                .WithName("CreateThing")
                .Produces<ThingResponse>(StatusCodes.Status201Created);
            """;
        const string file = route + "\nbuilder.AddEndpointFilter<TBuilder, NewThingFilter>();\nvar f = new OtherThingFilter(services);";

        var (extensions, types) = BindingReferences(route, file);

        Assert.Contains("AddNewThingAuthorizationFilter", extensions);
        Assert.Contains("RequireNewThingCeiling", extensions);
        Assert.Contains("NewThingEndpoint", types);
        Assert.Contains("NewThingGate", types);
        Assert.Contains("NewThingFilter", types);
        Assert.Contains("OtherThingFilter", types);
    }

    [Fact(DisplayName = "Task 036 C guard, census control: every endpoint filter in Api/ExternalAccess is scanned or declared off the plane")]
    public void Control_EveryEndpointFilterInExternalAccess_IsScannedOrDeclaredOffThePlane()
    {
        var scanned = ScannedFiles().Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var filterFiles = Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, ExternalAccessDir), "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => Regex.IsMatch(SourceScan.CodeText(File.ReadAllLines(f)), @":\s*IEndpointFilter\b|\bAdd\w*Filter\s*<\s*TBuilder\s*>\s*\(\s*this\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(filterFiles);
        var unclassified = filterFiles.Where(f => !scanned.Contains(f!) && !FiltersOffThePlane.ContainsKey(f!)).ToList();
        Assert.True(unclassified.Count == 0,
            "An endpoint filter in Api/ExternalAccess is neither reached from the /api/v1/external mappings nor declared off "
            + "the plane (FiltersOffThePlane, with where it is bound): " + string.Join(", ", unclassified));
    }

    // ── Route census control (fix round 4, K1) ────────────────────────────────────────────────────────

    /// <summary>Whether a route key (<c>VERB /path</c>) is on <c>/api/v1/external</c> itself — never
    /// <c>/api/v1/external-access</c> or another prefix that merely starts with the same letters.</summary>
    internal static bool IsPlaneRoute(string routeKey)
    {
        var space = routeKey.IndexOf(' ');
        var path = space < 0 ? routeKey : routeKey[(space + 1)..];
        return path.Equals(PlaneRoute, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(PlaneRoute + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The plane routes of <paramref name="census"/> registered in a file outside <paramref name="scannedBffRelative"/>
    /// (paths relative to <c>src/server/api/Sprk.Bff.Api/</c>), each as <c>key (file)</c>.
    /// </summary>
    internal static IReadOnlyList<string> PlaneRoutesOutsideTheScan(
        IEnumerable<(string Key, string File)> census, IEnumerable<string> scannedBffRelative)
    {
        var scanned = scannedBffRelative.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return census
            .Where(route => IsPlaneRoute(route.Key) && !scanned.Contains(route.File))
            .Select(route => $"{route.Key} ({route.File})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "Task 036 C guard, census control (K1): every live /api/v1/external route is registered in a scanned file")]
    public void Control_EveryLiveExternalRoute_IsRegisteredInAScannedFile()
    {
        var census = RouteAuthorizationGuardTests.LiveRouteCensus();
        var planeRoutes = census.Where(route => IsPlaneRoute(route.Key)).ToList();

        // Vacuity: the census sees the plane (dozens of routes today), so an empty intersection cannot pass by accident.
        Assert.True(planeRoutes.Count >= 20,
            $"The route census found only {planeRoutes.Count} /api/v1/external routes; the scanner has stopped seeing the plane.");

        var outside = PlaneRoutesOutsideTheScan(census, ScannedFiles().Select(BffRelative));
        Assert.True(outside.Count == 0,
            "A live /api/v1/external route is registered in a file the no-OBO guard does not scan. Bind it through the "
            + "group's mappings so BoundFiles derives it, or widen the derivation:\n  " + string.Join("\n  ", outside));
    }

    [Fact(DisplayName = "Task 036 C guard, census must-fire control (K1): a plane route registered outside the scanned set is reported")]
    public void Control_APlaneRouteOutsideTheScannedSet_IsReported()
    {
        var census = new[]
        {
            ("POST /api/v1/external/things/{id:guid}", "Infrastructure/Things/ThingEndpoints.cs"),
            ("GET /api/v1/external", "Program.cs"),
        };

        var outside = PlaneRoutesOutsideTheScan(census, new[] { "Api/ExternalAccess/ExternalProjectDataEndpoints.cs" });

        Assert.Equal(
            new[]
            {
                "GET /api/v1/external (Program.cs)",
                "POST /api/v1/external/things/{id:guid} (Infrastructure/Things/ThingEndpoints.cs)",
            },
            outside);
    }

    [Fact(DisplayName = "Task 036 C guard, census must-not-fire control (K1): a scanned plane route and an off-plane route are not reported")]
    public void Control_AScannedPlaneRoute_OrAnOffPlaneRoute_IsNotReported()
    {
        var census = new[]
        {
            ("POST /api/v1/external/projects/{id:guid}/todos", "Api/ExternalAccess/ExternalProjectDataEndpoints.cs"),
            ("POST /api/v1/external-access/grant", "Api/ExternalAccess/GrantExternalAccessEndpoint.cs"),
            ("GET /api/v1/externalthings", "Infrastructure/Things/ThingEndpoints.cs"),
        };

        Assert.Empty(PlaneRoutesOutsideTheScan(census, new[] { "Api/ExternalAccess/ExternalProjectDataEndpoints.cs" }));
    }

    // ── Rule controls ─────────────────────────────────────────────────────────────────────────────────

    private const string Sanctioned = """
        internal static Func<string, Guid, CancellationToken, Task<AccessRights?>> CallerRightsReader(
            CallerRecordAccessProbe probe, HttpContext httpContext)
        {
            var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
            return (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyOrUnknownAsync(systemUserId, entitySet, recordId, ct);
        }

        internal static Func<string, CancellationToken, Task<bool?>> CallerPrivilegeReader(
            CallerRecordAccessProbe probe, HttpContext httpContext)
        {
            var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
            return (privilege, ct) => probe.PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync(systemUserId, privilege, ct);
        }

        var stream = await speFileStore.DownloadFileAsync(driveId, itemId, ct);
        var ceiling = GrantCeiling.FromContactGrantorRights(g.Rights);
        var noExtractBearerTokenHere = principal.ContactId;
        """;

    private static IReadOnlyList<string> ViolationsOf(string source) => Violations(SourceScan.CodeText(source.Split('\n')));

    [Fact(DisplayName = "Task 036 C guard, must-not-fire control: the sanctioned app-only readers and the contact grantor's ceiling pass")]
    public void Control_TheSanctionedAppOnlyShape_HasNoViolation()
        => Assert.Empty(ViolationsOf(Sanctioned));

    [Fact(DisplayName = "Task 036 C guard, must-not-fire control: a comment naming an OBO question or the bearer-token read is ignored")]
    public void Control_ACommentNamingAnOboQuestion_HasNoViolation()
        => Assert.Empty(ViolationsOf(Sanctioned
            + "\n// never probe.GetCallerRightsAsync(token, ...) or CallerHoldsPrivilegeAsync here"
            + "\n// never TokenHelper.ExtractBearerTokenOrNull(httpContext) or ProbeGrantorCeilingAsync on this plane"
            + "\n/// <see cref=\"CallerRecordAccessProbe.GetCallerSystemUserIdAsync\"/> serves internal routes only\n"));

    [Theory(DisplayName = "Task 036 C guard, must-fire control: each OBO question, exchange or caller-token read is caught")]
    [InlineData("return (entitySet, recordId, ct) => probe.GetCallerRightsAsync(token, entitySet, recordId, ct);")]
    [InlineData("return (privilege, ct) => probe.CallerHoldsPrivilegeAsync(token, privilege, ct);")]
    [InlineData("var rights = await probe.GetCallerRightsForRecordsAsync(token, \"sprk_projects\", ids, ct);")]
    [InlineData("var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync(token, targets, ct);")]
    [InlineData("var rights = await probe.GetCallerRightsForAuthorizationGateAsync(token, \"sprk_projects\", id, ct);")]
    [InlineData("var me = await probe.GetCallerSystemUserIdAsync(token, ct);")]
    [InlineData("Func<string?, string, Guid, CancellationToken, Task<AccessRights>> ask = probe.GetCallerRightsAsync;")]
    [InlineData("var result = await app.AcquireTokenOnBehalfOf(scopes, new UserAssertion(token)).ExecuteAsync(ct);")]
    [InlineData("var graph = await graphFactory.ForUserAsync(httpContext, ct);")]
    [InlineData("var stream = await speFileStore.DownloadFileAsUserAsync(httpContext, driveId, itemId, ct);")]
    [InlineData("IDataverseUserClient userClient,")]
    [InlineData("var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);")]
    [InlineData("return TokenHelper.ExtractBearerToken(httpContext);")]
    [InlineData("ceiling = await GrantExternalAccessEndpoint.ProbeGrantorCeilingAsync(probe, httpContext, root.Type, root.Id, ct);")]
    public void Control_AnOboEntryPoint_IsCaught(string statement)
        => Assert.NotEmpty(ViolationsOf(Sanctioned + "\n" + statement + "\n"));

    [Fact(DisplayName = "Task 036 C guard, must-fire control: the pre-option-C readers (OBO for a Spaarke-tenant systemuser) are caught")]
    public void Control_ThePreOptionCReaders_AreCaught()
    {
        const string preOptionC = """
            if (AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext)) is { } systemUserId)
            {
                return (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyAsync(systemUserId, entitySet, recordId, ct);
            }

            var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);
            return (entitySet, recordId, ct) => probe.GetCallerRightsAsync(token, entitySet, recordId, ct);
            """;

        var violations = ViolationsOf(preOptionC);
        Assert.Contains(violations, v => v.Contains("GetCallerRightsAsync", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("ExtractBearerTokenOrNull", StringComparison.Ordinal));
    }
}
