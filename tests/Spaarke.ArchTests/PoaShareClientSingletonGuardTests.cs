using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 060 — the POA share client stays consolidated.
/// </summary>
/// <remarks>
/// <para>Spaarke had TWO <c>principalobjectaccess</c> (POA) share clients: the systemuser-only,
/// revoke-less one behind <c>IDataverseAccessGrantService</c>, and a private teams-plus-revoke duplicate
/// inside <c>PlaybookSharingService</c>. Task 060 merged them into one seam parameterized by principal
/// kind. Doing the merge once is not the same as it STAYING merged: the cheapest way to add sharing to a
/// new surface is to paste a <c>GrantAccess</c> payload next to the code that needs it, which is exactly
/// how the second client appeared the first time.</para>
///
/// <para>So these guards pin the invariant as a build failure, per root CLAUDE.md §11 (default to reuse).
/// A genuinely new POA construction site is not "make the test pass" — it is a design decision to be
/// argued in review, and the fix is almost always to call the existing seam.</para>
///
/// <para><b>Task 063 added a third POA action</b>, <c>ModifyAccess</c> (a level change replaces the rights; a
/// GrantAccess on an existing share is not documented to). It was added to the canonical client, and the detector
/// below now names it too — otherwise a second client could be born through the one action the guard did not
/// know about.</para>
///
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): these scan text, not syntax. Each rule is
/// paired with the negative control that proves the detector actually fires — verified by seeding a
/// duplicate payload and watching the rule go red before the guard was committed.</para>
/// </remarks>
public class PoaShareClientSingletonGuardTests
{
    /// <summary>The one file allowed to build POA action payloads.</summary>
    private const string CanonicalClient = "src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs";

    /// <summary>The Dataverse POA actions, as the quoted action names a POST argument carries.</summary>
    private static readonly string[] PoaActions = { "\"GrantAccess\"", "\"ModifyAccess\"", "\"RevokeAccess\"" };

    private static string RelativePath(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');

    /// <summary>
    /// A file "builds a POA action payload" when it POSTs to the <c>GrantAccess</c>, <c>ModifyAccess</c> or
    /// <c>RevokeAccess</c> Dataverse action. The action name in a POST argument is the narrowest signature of the
    /// thing being forbidden — mentioning any of these words in prose or a method name is not a client and must not
    /// trip this.
    /// </summary>
    private static IEnumerable<string> FilesPostingPoaActions() =>
        SourceScan.ServerSourceFiles()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return PoaActions.Any(action => text.Contains(action, StringComparison.Ordinal));
            })
            .Select(RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal);

    [Fact(DisplayName = "Task 060: exactly ONE server file constructs GrantAccess/ModifyAccess/RevokeAccess payloads")]
    public void PoaActionPayloadsAreBuiltInExactlyOnePlace()
    {
        var sites = FilesPostingPoaActions().ToList();

        Assert.True(
            sites.Count == 1 && sites[0] == CanonicalClient,
            "task 060 consolidated the two POA share clients into one principal-parameterized seam "
            + "(CLAUDE.md §11). A second construction site means a third client is being born — call "
            + "IDataverseRecordShareService instead of pasting a payload."
            + $"{Environment.NewLine}  expected: {CanonicalClient}"
            + $"{Environment.NewLine}  found   : {string.Join(", ", sites)}");
    }

    [Fact(DisplayName = "Task 060: the POA seam exposes revoke, so internal shares stay revocable")]
    public void TheSeamExposesRevoke()
    {
        var seam = Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Access/IDataverseRecordShareService.cs");

        Assert.True(
            File.Exists(seam),
            "the consolidated POA seam is the write path FR-28 (share-only secure projects) and FR-29 "
            + "(the '+ User' picker) depend on; expected it at " + seam);

        var text = File.ReadAllText(seam);

        Assert.True(
            text.Contains("Task RevokeAccessAsync(", StringComparison.Ordinal),
            "a grant-only seam is what made internal shares permanently unrevocable from the UI — the "
            + "concrete cost-of-doing-nothing task 060 exists to remove");
        Assert.Contains("Task GrantAccessAsync(", text, StringComparison.Ordinal);
        Assert.Contains(
            "Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(",
            text,
            StringComparison.Ordinal);
    }

    // Task 063 deliberately adds NO rule asserting that the seam DECLARES ModifyAccessAsync or
    // GetPrincipalAccessOrThrowAsync. The compiler already enforces both — every implementation and every call site
    // fails to build without them — and a text-scan rule asserting signatures the same commit added can never fail,
    // which is the shape tests/CLAUDE.md warns about ("a detector nobody has seen fail is a detector nobody knows
    // works"); it would also false-red the moment someone legally wraps a signature across lines. The invariant
    // worth guarding is the one above: exactly ONE file builds POA payloads, ModifyAccess included — and that rule
    // has a verified negative control (task 063 perturbation P10 seeded a second "ModifyAccess" POST and watched it
    // go red).

    // =============================================================================================
    // Task 132 (batch 4 integration residual) — every share WRITE goes through the seam that evicts
    // ---------------------------------------------------------------------------------------------
    // A POA grant / rights change / revoke changes who can read a record exactly as an owner change does, so the access
    // caches must be evicted after it. The eviction lives in the ONE seam (DataverseRecordShareService calls
    // IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync after each of its three writes, returned or thrown —
    // AccessCacheInvalidationTests.Shares proves it per write). It is only "by construction" if nothing in the BFF can
    // write POA another way. The routes around the seam, and the rule that closes each:
    //
    // COMPILED — an IL scan (IlCallScan) of every assembly the BFF runs from src/ and every src/ assembly that can name
    // DataverseWebApiService. The set is derived from the csproj graph, and each member must load
    // (EveryBffOrClientReachingProjectIsScanned), so a new project cannot fall outside it unnoticed.
    //   C1. A reference to the concrete client's GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync from ANY type
    //       but the seam (the client included), through any receiver expression — a field,
    //       `sp.GetRequiredService<DataverseWebApiService>()` (task 132 verifier seed S5), a cast, an indexer — inside a
    //       lambda, local function or async method, as a method group, or as an expression tree's method handle.
    //       (EveryCompiledReferenceToTheClientsPoaWritesIsTheSeams)
    //   C2. A FOURTH POA write on the client, which C1 would not know by name: the client methods that load a POA action
    //       name are pinned to exactly those three (TheClientsPoaWritesAreExactlyTheThreeTheGuardNames).
    //   C3. A write ON the seam that does not evict: the seam's references to the client's writes are pinned to its own
    //       three same-named methods — the three the behaviour tests prove evict
    //       (TheSeamWritesPoaOnlyFromItsThreeEvictingMethods).
    //   C4. A POA action name loaded as a string constant anywhere but the client — an SDK OrganizationRequest by name, a
    //       hand-built POST or $batch, a name the compiler folded from constant pieces — and a POA write method's name
    //       loaded as a constant (reflection, nameof, a `dynamic` call's binder name)
    //       (NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite).
    //   C5. The SDK's GrantAccessRequest / ModifyAccessRequest / RevokeAccessRequest, however they are imported (a global
    //       using defeats the text rule T2) (NoCompiledCodeUsesTheSdkPoaMessages).
    // TEXT — every src/server .cs file, compiled into the BFF or not:
    //   T1. A `.XAccessAsync(` call whose receiver is not an identifier the file declares ONLY as
    //       IDataverseRecordShareService: an expression receiver (S5), a `var`, `dynamic` or undeclared name, a name the
    //       file also declares as another type (seed S6). Conservative — it may flag a legal call; C1 is the precise rule.
    //       (EveryPoaShareWriteGoesThroughTheEvictingSeam; per named writer: EachNamedShareWriterWritesOnlyThroughTheSeam)
    //   T2. The SDK messages (NoServerFileSendsSdkPoaMessages). T3. A second POA payload (task 060's
    //       PoaActionPayloadsAreBuiltInExactlyOnePlace, above). T4. A POA write method named as a string
    //       (NoServerFileNamesTheClientsPoaWritesAsAString).
    //
    // OUT OF REACH, by construction: a name or route the code computes or reads at RUN time — a method name or action
    // URL built from non-constant pieces, or read from configuration or reflection metadata — then invoked by reflection
    // or a raw HTTP call. No static scan sees a value that does not exist until the code runs; that is review's to catch.
    // Every rule above has a control that was seen to fail.
    // =============================================================================================

    /// <summary>The seam — the one file allowed to call the concrete client's POA writes.</summary>
    private const string EvictingSeam = "src/server/api/Sprk.Bff.Api/Services/Access/IDataverseRecordShareService.cs";

    /// <summary>The concrete client's three POA write methods.</summary>
    private static readonly HashSet<string> PoaWriteMethodNames = new(StringComparer.Ordinal)
    {
        nameof(DataverseWebApiService.GrantAccessAsync),
        nameof(DataverseWebApiService.ModifyAccessAsync),
        nameof(DataverseWebApiService.RevokeAccessAsync),
    };

    /// <summary>
    /// The SDK's POA write messages, by FULL name (compile-checked through <c>typeof</c>): the BFF's own external-access
    /// DTOs share the short names <c>GrantAccessRequest</c> / <c>RevokeAccessRequest</c> and are not POA writes.
    /// </summary>
    private static readonly HashSet<string> SdkPoaMessageTypes = new(StringComparer.Ordinal)
    {
        typeof(Microsoft.Crm.Sdk.Messages.GrantAccessRequest).FullName!,
        typeof(Microsoft.Crm.Sdk.Messages.ModifyAccessRequest).FullName!,
        typeof(Microsoft.Crm.Sdk.Messages.RevokeAccessRequest).FullName!,
    };

    /// <summary>
    /// A string constant that carries a POA action name as a whole word: the bare action (the Web API route segment, the
    /// SDK's <c>RequestName</c>), a path or URL ending in it (<c>…/RevokeAccess</c>), a qualified name
    /// (<c>Microsoft.Dynamics.CRM.ModifyAccess</c>), a <c>$batch</c> line. Deliberately conservative: a log message that
    /// names an action as a word is flagged too (reword it); <c>GrantAccessAsync</c> and <c>GrantAccessRequest</c> are
    /// other words.
    /// </summary>
    private static readonly Regex PoaActionConstant = new(
        @"\b(GrantAccess|ModifyAccess|RevokeAccess)\b",
        RegexOptions.CultureInvariant);

    // ── the compiled scan set ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>src/**</c> projects the compiled rules scan, as (csproj path, assembly name): every project in
    /// <c>Sprk.Bff.Api</c>'s ProjectReference closure (the process whose access caches a share write stales), and every
    /// project whose closure reaches <c>Spaarke.Dataverse</c> (the code that can name
    /// <see cref="DataverseWebApiService"/>) — a binary or package reference to that assembly counts as reaching it.
    /// </summary>
    internal static IReadOnlyList<(string Project, string AssemblyName)> ProjectsTheCompiledScanCovers()
    {
        var srcRoot = Path.Combine(SourceScan.RepoRoot, "src");
        var sep = Path.DirectorySeparatorChar;
        var projects = Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}node_modules{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .ToDictionary(p => p, p => XDocument.Load(p), StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> Includes(string project, string element) =>
            projects[project].Descendants()
                .Where(e => e.Name.LocalName == element)
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!.Trim());

        IEnumerable<string> References(string project) =>
            Includes(project, "ProjectReference")
                .Select(include => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', sep))));

        bool NamesTheClientBinary(string project) =>
            Includes(project, "Reference").Concat(Includes(project, "PackageReference"))
                .Any(include => include == "Spaarke.Dataverse"
                                || include.StartsWith("Spaarke.Dataverse,", StringComparison.Ordinal)
                                || include.EndsWith("Spaarke.Dataverse.dll", StringComparison.OrdinalIgnoreCase));

        var client = projects.Keys.Single(p => Path.GetFileName(p) == "Spaarke.Dataverse.csproj");
        var bff = projects.Keys.Single(p => Path.GetFileName(p) == "Sprk.Bff.Api.csproj");
        var memo = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        bool Reaches(string project, HashSet<string> visiting)
        {
            if (string.Equals(project, client, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (memo.TryGetValue(project, out var known))
            {
                return known;
            }

            if (!projects.ContainsKey(project) || !visiting.Add(project))
            {
                return false;
            }

            var reaches = NamesTheClientBinary(project) || References(project).Any(r => Reaches(r, visiting));
            visiting.Remove(project);
            return memo[project] = reaches;
        }

        var bffClosure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(new[] { bff });
        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (projects.ContainsKey(project) && bffClosure.Add(project))
            {
                foreach (var reference in References(project))
                {
                    pending.Push(reference);
                }
            }
        }

        return projects.Keys
            .Where(p => bffClosure.Contains(p) || Reaches(p, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .Select(p => (
                Project: RelativePath(p),
                AssemblyName: projects[p].Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value.Trim()
                              ?? Path.GetFileNameWithoutExtension(p)))
            .OrderBy(p => p.Project, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every type of every assembly the compiled rules scan (loading each; a failure to load fails the rule).</summary>
    private static readonly Lazy<IReadOnlyList<Type>> ScannedTypes = new(() =>
        ProjectsTheCompiledScanCovers()
            .Select(p => Assembly.Load(new AssemblyName(p.AssemblyName)))
            .SelectMany(a => a.GetTypes())
            .ToList());

    /// <summary>The scanned method references that touch POA: the client's writes and the SDK's POA messages.</summary>
    private static readonly Lazy<IReadOnlyList<(Type Caller, MethodBase CallerMethod, MethodBase Target)>> ScannedPoaReferences = new(() =>
        IlCallScan.MethodReferences(ScannedTypes.Value)
            .Where(r => IsTheClientsPoaWrite(r.Target) || IsAnSdkPoaMessage(r.Target))
            .ToList());

    /// <summary>The scanned string constants that name a POA action or a POA write method.</summary>
    private static readonly Lazy<IReadOnlyList<(Type Caller, MethodBase CallerMethod, string Value)>> ScannedPoaNameLoads = new(() =>
        IlCallScan.StringLoads(ScannedTypes.Value)
            .Where(l => NamesAPoaActionOrWrite(l.Value))
            .ToList());

    [Fact(DisplayName = "Task 132: every project the BFF runs, or that can name the concrete POA client, is loaded by the compiled scan")]
    public void EveryBffOrClientReachingProjectIsScanned()
    {
        var projects = ProjectsTheCompiledScanCovers();
        var unloadable = new List<string>();
        foreach (var (project, assemblyName) in projects)
        {
            try
            {
                Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                unloadable.Add($"{project} (assembly {assemblyName}): {ex.GetType().Name}");
            }
        }

        var names = projects.Select(p => p.AssemblyName).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            new[] { "Spaarke.Dataverse", "Spaarke.Core", "Spaarke.Scheduling", "Sprk.Bff.Api" }.All(names.Contains),
            "precondition: the csproj walk finds the BFF and the three shared projects it runs; it found only: "
            + string.Join(", ", projects.Select(p => p.Project)));
        Assert.True(
            unloadable.Count == 0,
            "a project the BFF runs, or that can name DataverseWebApiService, is not loadable by the compiled POA-write scan, "
            + "so a share write in it that skips the access-cache eviction (task 132) would pass unseen. Add a "
            + "ProjectReference to it in tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", unloadable)}");
    }

    // ── C1. the client's POA writes are called only by the seam ──────────────────────────────────

    /// <summary>
    /// Whether <paramref name="target"/> is one of the concrete client's POA writes: a method of that name declared on
    /// <see cref="DataverseWebApiService"/>, a subclass of it, or a base type / interface it implements (so a POA write
    /// later surfaced through one of the client's interfaces is still the client's write). The seam's own interface
    /// (<see cref="IDataverseRecordShareService"/>) is none of these — the client does not implement it.
    /// </summary>
    internal static bool IsTheClientsPoaWrite(MethodBase target) =>
        PoaWriteMethodNames.Contains(target.Name)
        && target.DeclaringType is { } declaring
        && (typeof(DataverseWebApiService).IsAssignableFrom(declaring)
            || declaring.IsAssignableFrom(typeof(DataverseWebApiService)));

    /// <summary>Whether <paramref name="target"/> is a member of one of the SDK's POA write messages.</summary>
    internal static bool IsAnSdkPoaMessage(MethodBase target) =>
        target.DeclaringType?.FullName is { } name && SdkPoaMessageTypes.Contains(name);

    /// <summary>Whether a string constant names a POA action (<see cref="PoaActionConstant"/>) or a POA write method.</summary>
    internal static bool NamesAPoaActionOrWrite(string value) =>
        PoaActionConstant.IsMatch(value) || PoaWriteMethodNames.Contains(value);

    /// <summary>
    /// Every reference in <paramref name="references"/> to the concrete client's POA writes that is not made by the seam
    /// (<see cref="DataverseRecordShareService"/>) — the client itself included, so a client method that wraps one is a
    /// bypass too. Closures and async state machines count as their outermost type.
    /// </summary>
    internal static IReadOnlyList<string> CompiledPoaWriteBypasses(
        IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> references) =>
        references
            .Where(r => IsTheClientsPoaWrite(r.Target))
            .Select(r => (Caller: IlCallScan.Outermost(r.Caller), r.Target))
            .Where(r => r.Caller != typeof(DataverseRecordShareService))
            .Select(r => $"{r.Caller.FullName} → {r.Target.DeclaringType!.Name}.{r.Target.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 132: only the evicting seam references the concrete client's POA writes (compiled)")]
    public void EveryCompiledReferenceToTheClientsPoaWritesIsTheSeams()
    {
        var references = ScannedPoaReferences.Value;

        // Vacuity check: the scan must SEE the sanctioned calls — the seam's three writes, made from inside its async
        // state machines — or "no bypass found" means nothing.
        var seamCalls = references
            .Where(r => IlCallScan.Outermost(r.Caller) == typeof(DataverseRecordShareService) && IsTheClientsPoaWrite(r.Target))
            .Select(r => r.Target.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            seamCalls.SetEquals(PoaWriteMethodNames),
            "precondition: the compiled scan sees DataverseRecordShareService call all three client POA writes; it saw "
            + string.Join(", ", seamCalls));

        var bypasses = CompiledPoaWriteBypasses(references);

        Assert.True(
            bypasses.Count == 0,
            "a compiled call to DataverseWebApiService's POA writes outside DataverseRecordShareService skips the "
            + "access-cache eviction (task 132): the sharee's impersonated root set and every snapshot of the record stay "
            + "stale for their TTLs — an unshare that keeps access. Inject IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", bypasses)}");
    }

    // ── C2. the client has exactly three POA writes ──────────────────────────────────────────────

    /// <summary>
    /// The source methods, among <paramref name="loads"/>, that load a POA action name — on the client, its POA writes.
    /// A load inside a lambda is reported under its closure, never under a source method's name.
    /// </summary>
    internal static IReadOnlyList<string> PoaActionSenders(IEnumerable<(Type Caller, MethodBase CallerMethod, string Value)> loads) =>
        loads
            .Where(l => PoaActionConstant.IsMatch(l.Value))
            .Select(l => IlCallScan.SourceMethodName(l.Caller, l.CallerMethod))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 132: the concrete client's POA writes are exactly the three the guard names")]
    public void TheClientsPoaWritesAreExactlyTheThreeTheGuardNames()
    {
        var senders = PoaActionSenders(IlCallScan.StringLoads(IlCallScan.WithNested(typeof(DataverseWebApiService))));

        Assert.True(
            senders.Count == PoaWriteMethodNames.Count && senders.All(PoaWriteMethodNames.Contains),
            "the client methods that send a POA action must be exactly GrantAccessAsync / ModifyAccessAsync / "
            + "RevokeAccessAsync: the compiled rule above recognises a client POA write BY THOSE NAMES, so a fourth would be "
            + "a write any type could call without the seam's eviction (task 132). Expose it through "
            + "IDataverseRecordShareService (which evicts) and add its name to PoaWriteMethodNames — a review decision."
            + $"{Environment.NewLine}  found: {string.Join(", ", senders)}");
    }

    // ── C3. the seam writes only from its three evicting methods ─────────────────────────────────

    /// <summary>
    /// The seam's writes, as <c>"{seam method} → {client write}"</c>: which of the seam's source methods (async state
    /// machines mapped back to their method; a lambda reported under its closure) calls which client POA write.
    /// </summary>
    internal static IReadOnlyList<string> SeamPoaWrites(IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> references) =>
        references
            .Where(r => IsTheClientsPoaWrite(r.Target))
            .Select(r => $"{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} → {r.Target.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 132: the seam writes POA only from its three evicting methods, each its own write")]
    public void TheSeamWritesPoaOnlyFromItsThreeEvictingMethods()
    {
        var writes = SeamPoaWrites(IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DataverseRecordShareService))));

        Assert.True(
            writes.SequenceEqual(new[]
            {
                "GrantAccessAsync → GrantAccessAsync",
                "ModifyAccessAsync → ModifyAccessAsync",
                "RevokeAccessAsync → RevokeAccessAsync",
            }),
            "the seam may write POA only from GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync — the three whose "
            + "eviction (returned, thrown or cancelled) AccessCacheInvalidationTests.Shares proves. A write anywhere else in "
            + "the seam is a write C1 trusts without that proof (task 132): route it through one of the three, or add its "
            + "eviction cases to those tests and its name here — a review decision."
            + $"{Environment.NewLine}  found: {string.Join(", ", writes)}");
    }

    // ── C4. POA actions and write methods named as constants ─────────────────────────────────────

    /// <summary>
    /// Every string constant in <paramref name="loads"/> that names a POA action or a POA write method outside the
    /// client — the one place a POA action may be named (task 060) and the one type that declares those methods.
    /// </summary>
    internal static IReadOnlyList<string> CompiledPoaNameLoads(IEnumerable<(Type Caller, MethodBase CallerMethod, string Value)> loads) =>
        loads
            .Where(l => NamesAPoaActionOrWrite(l.Value))
            .Select(l => (Caller: IlCallScan.Outermost(l.Caller), l.Value))
            .Where(l => l.Caller != typeof(DataverseWebApiService))
            .Select(l => $"{l.Caller.FullName}: \"{l.Value}\"")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 132: no compiled code outside the client names a POA action or POA write method as a constant")]
    public void NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite()
    {
        var loads = ScannedPoaNameLoads.Value;

        // Vacuity check: the scan must see the client's own three action names.
        Assert.True(
            PoaActionSenders(loads.Where(l => IlCallScan.Outermost(l.Caller) == typeof(DataverseWebApiService)))
                .SequenceEqual(PoaWriteMethodNames.OrderBy(s => s, StringComparer.Ordinal)),
            "precondition: the compiled scan sees the client's three POA writes load their action names");

        var offenders = CompiledPoaNameLoads(loads);

        Assert.True(
            offenders.Count == 0,
            "naming a POA action (GrantAccess / ModifyAccess / RevokeAccess) or a client POA write method as a constant "
            + "outside the client is how a write reaches Dataverse without the seam — an SDK OrganizationRequest by name, a "
            + "hand-built POST, a reflective call — and without the access-cache eviction (task 132). Call "
            + "IDataverseRecordShareService. (The rule reads constants, not intent: a log line that names an action as a "
            + "word, or a nameof of an unrelated member that shares a write method's name, is flagged too — reword or "
            + "rename it.)"
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── C5. the SDK's POA messages ───────────────────────────────────────────────────────────────

    /// <summary>Every use, in <paramref name="references"/>, of a member of the SDK's POA write messages.</summary>
    internal static IReadOnlyList<string> CompiledSdkPoaMessageUses(
        IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> references) =>
        references
            .Where(r => IsAnSdkPoaMessage(r.Target))
            .Select(r => $"{IlCallScan.Outermost(r.Caller).FullName} → {r.Target.DeclaringType!.Name}.{r.Target.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 132: no compiled code uses the SDK's GrantAccess/ModifyAccess/RevokeAccess messages")]
    public void NoCompiledCodeUsesTheSdkPoaMessages()
    {
        var offenders = CompiledSdkPoaMessageUses(ScannedPoaReferences.Value);

        Assert.True(
            offenders.Count == 0,
            "an SDK POA message bypasses the one seam — and with it the access-cache eviction (task 132) and the payload "
            + "consolidation (task 060). Use IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>Negative and positive controls for the compiled detectors (C1, C4, C5) and the pins' mapping (C2, C3).</summary>
    [Fact(DisplayName = "Task 132: the compiled detectors flag every bypass shape and pass the seam")]
    public void CompiledDetector_FlagsEveryBypassShape_AndPassesTheSeam()
    {
        var writeControls = new[]
        {
            typeof(PoaBypassControl_ResolvedInline),
            typeof(PoaBypassControl_AsyncLambda),
            typeof(PoaBypassControl_MethodGroup),
            typeof(PoaBypassControl_ExpressionTree),
        };
        var writeReferences = IlCallScan.MethodReferences(writeControls.SelectMany(IlCallScan.WithNested)).ToList();

        Assert.Equal(
            new[]
            {
                $"{typeof(PoaBypassControl_AsyncLambda).FullName} → DataverseWebApiService.GrantAccessAsync",
                $"{typeof(PoaBypassControl_ExpressionTree).FullName} → DataverseWebApiService.RevokeAccessAsync",
                $"{typeof(PoaBypassControl_MethodGroup).FullName} → DataverseWebApiService.ModifyAccessAsync",
                $"{typeof(PoaBypassControl_ResolvedInline).FullName} → DataverseWebApiService.RevokeAccessAsync",
            },
            CompiledPoaWriteBypasses(writeReferences));

        Assert.Equal(
            new[] { $"{typeof(PoaBypassControl_SdkMessage).FullName} → GrantAccessRequest..ctor" },
            CompiledSdkPoaMessageUses(IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(PoaBypassControl_SdkMessage)))));

        var nameControl = typeof(PoaBypassControl_NamedAsConstants);
        Assert.Equal(
            new[]
            {
                $"{nameControl.FullName}: \"/RevokeAccess\"",
                $"{nameControl.FullName}: \"GrantAccess\"",
                $"{nameControl.FullName}: \"ModifyAccessAsync\"",
                $"{nameControl.FullName}: \"POST Microsoft.Dynamics.CRM.ModifyAccess HTTP/1.1\"",
                $"{nameControl.FullName}: \"RevokeAccessAsync\"",
            },
            CompiledPoaNameLoads(IlCallScan.StringLoads(IlCallScan.WithNested(nameControl))));

        // The pins' source-method mapping: an async method's state machine maps back to the method; a lambda does not
        // borrow its enclosing method's name.
        var lambdaWrites = SeamPoaWrites(IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(PoaBypassControl_AsyncLambda))));
        Assert.Single(lambdaWrites);
        Assert.DoesNotContain(lambdaWrites, w => w.StartsWith(nameof(PoaBypassControl_AsyncLambda.InsideAnAsyncLambda) + " →", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "AllThreeWrites", "AllThreeWrites", "AllThreeWrites" },
            IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(PoaWriteThroughSeamControl)))
                .Where(r => PoaWriteMethodNames.Contains(r.Target.Name))
                .Select(r => IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)));

        // The sanctioned shape — every write through the seam's interface — is none of these.
        var sanctioned = IlCallScan.WithNested(typeof(PoaWriteThroughSeamControl)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledSdkPoaMessageUses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(sanctioned)));
    }

    // ── T1-T4. the text rules (every src/server file) ────────────────────────────────────────────

    private static readonly Regex PoaWriteInvocation = new(
        @"\.\s*(?<method>GrantAccessAsync|ModifyAccessAsync|RevokeAccessAsync)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex SdkPoaMessage = new(
        @"\b(GrantAccess|ModifyAccess|RevokeAccess)Request\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex PoaWriteNamedAsString = new(
        @"""(GrantAccessAsync|ModifyAccessAsync|RevokeAccessAsync)""|\bnameof\s*\([^)]*\b(GrantAccessAsync|ModifyAccessAsync|RevokeAccessAsync)\s*\)",
        RegexOptions.CultureInvariant);

    /// <summary>Words that can stand where a declaration's type stands but declare nothing.</summary>
    private static readonly HashSet<string> NotATypeName = new(StringComparer.Ordinal)
    {
        "return", "await", "in", "out", "ref", "is", "as", "case", "throw", "yield", "else", "new", "when", "not",
        "and", "or", "params", "this", "readonly", "static", "const", "using", "goto", "do", "lock", "base",
    };

    /// <summary>The receiver label for a call whose receiver is an expression, not an identifier.</summary>
    internal const string ExpressionReceiver = "<expression>";

    private static bool IsSeamTypeName(string type)
    {
        var bare = type.TrimEnd('?');
        return bare == nameof(IDataverseRecordShareService) || bare.EndsWith("." + nameof(IDataverseRecordShareService), StringComparison.Ordinal);
    }

    /// <summary>The types an identifier is declared with in one file's code (field, property, parameter or local).</summary>
    private static IReadOnlyList<string> DeclaredTypesOf(string code, string name) =>
        Regex.Matches(
                code,
                @"(?<![A-Za-z0-9_.<>?])(?<type>[A-Za-z_][A-Za-z0-9_.]*(?:<[^;{}()]*?>)?\??)\s+" + Regex.Escape(name)
                    + @"(?![A-Za-z0-9_])(?=\s*[;,)={])",
                RegexOptions.CultureInvariant)
            .Select(m => m.Groups["type"].Value)
            .Where(type => !NotATypeName.Contains(type))
            .ToList();

    /// <summary>
    /// The identifier a member access is made on, read backwards from the <c>.</c> at <paramref name="dot"/>: the last
    /// identifier of <c>x.</c>, <c>a.b.x.</c>, <c>x?.</c> or <c>x!.</c>; <see cref="ExpressionReceiver"/> for anything
    /// else (<c>…)</c>, <c>…&gt;</c>, <c>…]</c>), whose type text cannot know.
    /// </summary>
    private static string ReceiverBefore(string code, int dot)
    {
        var i = dot - 1;
        while (i >= 0 && char.IsWhiteSpace(code[i]))
        {
            i--;
        }

        if (i >= 0 && (code[i] == '?' || code[i] == '!'))
        {
            i--;
            while (i >= 0 && char.IsWhiteSpace(code[i]))
            {
                i--;
            }
        }

        var end = i;
        while (i >= 0 && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
        {
            i--;
        }

        if (end == i || char.IsDigit(code[i + 1]))
        {
            return ExpressionReceiver;
        }

        return code.Substring(i + 1, end - i);
    }

    /// <summary>
    /// The POA write calls in one file's text: each <c>….GrantAccessAsync(</c> / <c>ModifyAccessAsync(</c> /
    /// <c>RevokeAccessAsync(</c>, with whether it provably goes through the seam — its receiver is an identifier
    /// (<c>_owner._recordShare</c> resolves to the last one) that the file declares, and declares ONLY as an
    /// <c>IDataverseRecordShareService</c>. An expression receiver (<c>sp.GetRequiredService&lt;X&gt;()</c>,
    /// <c>(dv)</c>, <c>clients[0]</c>), an undeclared one, a <c>var</c> or <c>dynamic</c> one and one the file also
    /// declares as another type are not provably the seam, so they are offenders. Line comments are stripped first.
    /// Crude by design (see <see cref="SourceScan"/>); the compiled rule C1 is the precise one. Paired with
    /// <see cref="Detector_FlagsAWriteOnTheConcreteClient_AndPassesAWriteThroughTheSeam"/>.
    /// </summary>
    internal static IReadOnlyList<(string Receiver, string Method, bool ThroughSeam)> PoaWriteCalls(string text)
    {
        var code = string.Join('\n', text.Split('\n').Select(SourceScan.StripLineComment));
        return PoaWriteInvocation.Matches(code)
            .Select(m =>
            {
                var receiver = ReceiverBefore(code, m.Index);
                var throughSeam = false;
                if (receiver != ExpressionReceiver)
                {
                    var declared = DeclaredTypesOf(code, receiver);
                    throughSeam = declared.Count > 0 && declared.All(IsSeamTypeName);
                }

                return (receiver, m.Groups["method"].Value, throughSeam);
            })
            .ToList();
    }

    /// <summary>Whether one file's text sends a Dataverse SDK POA message (the SDK route around the Web API seam).</summary>
    internal static bool UsesSdkPoaMessages(string text)
    {
        var code = string.Join('\n', text.Split('\n').Select(SourceScan.StripLineComment));
        return code.Contains("Microsoft.Crm.Sdk.Messages", StringComparison.Ordinal) && SdkPoaMessage.IsMatch(code);
    }

    /// <summary>Whether one file's code names a POA write method as a string (the reflection route around the seam).</summary>
    internal static bool NamesAPoaWriteAsAString(string text)
    {
        var code = string.Join('\n', text.Split('\n').Select(SourceScan.StripLineComment));
        return PoaWriteNamedAsString.IsMatch(code);
    }

    [Fact(DisplayName = "Task 132: every POA share write in src/server goes through the evicting seam")]
    public void EveryPoaShareWriteGoesThroughTheEvictingSeam()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Select(file => (Path: RelativePath(file), Text: File.ReadAllText(file)))
            .Where(f => f.Path != EvictingSeam && f.Path != CanonicalClient)
            .SelectMany(f => PoaWriteCalls(f.Text)
                .Where(c => !c.ThroughSeam)
                .Select(c => $"{f.Path}: {c.Receiver}.{c.Method}(…)"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "a POA share write that does not provably go through IDataverseRecordShareService skips the access-cache "
            + "eviction (task 132): the sharee's impersonated root set and every snapshot of the record stay stale for their "
            + "TTLs — an unshare that keeps access. Call it on an identifier declared as IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>
    /// The share writers the batch 4 integration named (plus the provisioning file, whose creator share, its restore and
    /// the resume error paths all live there), each still VISIBLE to the rule above — at least one write found, every
    /// one through the seam. A refactor that hid a writer from the detector would otherwise pass the rule vacuously.
    /// </summary>
    [Theory(DisplayName = "Task 132: each named share-writing path writes POA only through the evicting seam")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/InternalShareEndpoints.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/UnsecureProjectEndpoint.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareSynchronizer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/ExternalAccess/AssignedAccessMaterializer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/NoAccessShareEnforcer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs")]
    public void EachNamedShareWriterWritesOnlyThroughTheSeam(string path)
    {
        var calls = PoaWriteCalls(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, path)));

        Assert.True(calls.Count > 0, $"{path}: no POA share write is visible to the detector any more — re-check it.");
        Assert.True(
            calls.All(c => c.ThroughSeam),
            $"{path}: {string.Join(", ", calls.Where(c => !c.ThroughSeam).Select(c => $"{c.Receiver}.{c.Method}"))} "
            + "does not go through IDataverseRecordShareService, so it skips the share-change eviction (task 132).");
    }

    [Fact(DisplayName = "Task 132: no server file sends the SDK's GrantAccess/ModifyAccess/RevokeAccess messages")]
    public void NoServerFileSendsSdkPoaMessages()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Where(file => UsesSdkPoaMessages(File.ReadAllText(file)))
            .Select(RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "an SDK POA message bypasses the one seam — and with it the access-cache eviction (task 132) and the payload "
            + "consolidation (task 060). Use IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(", ", offenders)}");
    }

    [Fact(DisplayName = "Task 132: no server file names a POA write method as a string (reflection around the seam)")]
    public void NoServerFileNamesTheClientsPoaWritesAsAString()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Select(file => (Path: RelativePath(file), Text: File.ReadAllText(file)))
            .Where(f => f.Path != EvictingSeam && f.Path != CanonicalClient && NamesAPoaWriteAsAString(f.Text))
            .Select(f => f.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "naming GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync as a string is how a reflective call reaches "
            + "the concrete client's POA writes without a call the compiled scan can see — and without the access-cache "
            + "eviction (task 132). Call IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(", ", offenders)}");
    }

    /// <summary>Negative and positive controls for the text detectors above.</summary>
    [Fact(DisplayName = "Task 132: the share-write text detectors fire on a bypass and pass the sanctioned shape")]
    public void Detector_FlagsAWriteOnTheConcreteClient_AndPassesAWriteThroughTheSeam()
    {
        const string bypass = """
            private readonly DataverseWebApiService _dataverse;
            public Task X() => _dataverse.GrantAccessAsync("sprk_projects", id, principal, "ReadAccess");
            """;
        const string throughSeam = """
            internal static async Task Y(IDataverseRecordShareService recordShare)
            {
                await recordShare
                    .RevokeAccessAsync("sprk_projects", id, principal, ct);
                // _dataverse.ModifyAccessAsync( in a comment is not a call
            }
            """;
        const string nestedReceiver = """
            private readonly IDataverseRecordShareService _recordShare;
            void Z() => _owner._recordShare.ModifyAccessAsync(set, id, p, "ReadAccess", _ct);
            """;
        const string nullConditional = """
            private readonly IDataverseRecordShareService? _recordShare;
            Task? Z() => _recordShare?.RevokeAccessAsync(set, id, p, _ct);
            """;

        // Task 132 verifier seed S5: the concrete client resolved inline — the receiver is an expression, not a name.
        const string s5ResolvedInline = """
            private readonly IDataverseRecordShareService _recordShare;
            Task W(IServiceProvider sp) => sp.GetRequiredService<Spaarke.Dataverse.DataverseWebApiService>().RevokeAccessAsync("sprk_projects", id, p, ct);
            """;
        const string parenthesisedReceiver = """
            Task W(DataverseWebApiService dv) => (dv).GrantAccessAsync(set, id, p, "ReadAccess", ct);
            """;
        const string invocationReceiver = """
            Task W() => GetClient().ModifyAccessAsync(set, id, p, "ReadAccess", ct);
            """;
        const string indexerReceiver = """
            Task W(IDataverseRecordShareService[] shares) => shares[0].RevokeAccessAsync(set, id, p, ct);
            """;

        // Task 132 verifier seed S6: a name the file declares as the seam AND as the concrete client.
        const string s6SameNameOtherType = """
            private readonly IDataverseRecordShareService _recordShare;
            static Task V(DataverseWebApiService _recordShare) => _recordShare.GrantAccessAsync(set, id, p, "ReadAccess", ct);
            """;
        const string inferredLocal = """
            var recordShare = sp.GetRequiredService<DataverseWebApiService>();
            await recordShare.RevokeAccessAsync(set, id, p, ct);
            """;
        const string dynamicLocal = """
            dynamic recordShare = client;
            await recordShare.RevokeAccessAsync(set, id, p, ct);
            """;
        const string undeclared = """
            Task U() => recordShare.GrantAccessAsync(set, id, p, "ReadAccess", ct);
            """;

        Assert.Equal(new[] { ("_dataverse", "GrantAccessAsync", false) }, PoaWriteCalls(bypass));
        Assert.Equal(new[] { ("recordShare", "RevokeAccessAsync", true) }, PoaWriteCalls(throughSeam));
        Assert.Equal(new[] { ("_recordShare", "ModifyAccessAsync", true) }, PoaWriteCalls(nestedReceiver));
        Assert.Equal(new[] { ("_recordShare", "RevokeAccessAsync", true) }, PoaWriteCalls(nullConditional));

        Assert.Equal(new[] { (ExpressionReceiver, "RevokeAccessAsync", false) }, PoaWriteCalls(s5ResolvedInline));
        Assert.Equal(new[] { (ExpressionReceiver, "GrantAccessAsync", false) }, PoaWriteCalls(parenthesisedReceiver));
        Assert.Equal(new[] { (ExpressionReceiver, "ModifyAccessAsync", false) }, PoaWriteCalls(invocationReceiver));
        Assert.Equal(new[] { (ExpressionReceiver, "RevokeAccessAsync", false) }, PoaWriteCalls(indexerReceiver));
        Assert.Equal(new[] { ("_recordShare", "GrantAccessAsync", false) }, PoaWriteCalls(s6SameNameOtherType));
        Assert.Equal(new[] { ("recordShare", "RevokeAccessAsync", false) }, PoaWriteCalls(inferredLocal));
        Assert.Equal(new[] { ("recordShare", "RevokeAccessAsync", false) }, PoaWriteCalls(dynamicLocal));
        Assert.Equal(new[] { ("recordShare", "GrantAccessAsync", false) }, PoaWriteCalls(undeclared));

        Assert.True(UsesSdkPoaMessages("using Microsoft.Crm.Sdk.Messages;\nvar r = new GrantAccessRequest { Target = t };"));
        Assert.False(UsesSdkPoaMessages("using Microsoft.Crm.Sdk.Messages;\nvar r = new RetrievePrincipalAccessRequest();"),
            "an SDK message that does not write a share is not a bypass");
        Assert.False(UsesSdkPoaMessages("public record GrantAccessRequest(Guid ContactId);"),
            "the external-access DTO of the same name is not the SDK message");

        Assert.True(NamesAPoaWriteAsAString("var m = typeof(DataverseWebApiService).GetMethod(\"RevokeAccessAsync\");"));
        Assert.True(NamesAPoaWriteAsAString("var m = t.GetMethod(nameof(DataverseWebApiService.GrantAccessAsync));"));
        Assert.False(NamesAPoaWriteAsAString("/// <see cref=\"GrantAccessAsync\"/>\napp.MapPost(\"/grant\", GrantAccessAsync);"),
            "a doc-comment cref and a method-group reference are not names-as-strings (the compiled rule sees the latter)");
    }
    [Fact(DisplayName = "Task 060: PlaybookSharingService holds no private POA client")]
    public void PlaybookSharingServiceDelegatesRatherThanDuplicating()
    {
        var text = File.ReadAllText(Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs"));

        Assert.False(
            text.Contains("\"GrantAccess\"", StringComparison.Ordinal),
            "its private team-grant payload was deleted in favour of the seam (task 060)");
        Assert.False(
            text.Contains("\"RevokeAccess\"", StringComparison.Ordinal),
            "its private team-revoke payload was deleted in favour of the seam (task 060)");
        Assert.False(
            text.Contains("principalobjectaccessset", StringComparison.Ordinal),
            "its private POA read was deleted in favour of IDataverseRecordShareService.GetPrincipalAccessAsync");
        Assert.True(
            text.Contains("_recordShare.", StringComparison.Ordinal),
            "it must reach POA through the one seam");
    }
}

// ── Controls for the compiled rules — never executed, only scanned. One bypass shape per type, because the compiled
//    rules report a bypass by its outermost type; each shape is one the original text detector could not see.

/// <summary>Task 132 verifier seed S5's shape: the concrete client resolved inline — a receiver no text rule can type.</summary>
internal static class PoaBypassControl_ResolvedInline
{
    internal static Task ResolvedInline(IServiceProvider sp) =>
        sp.GetRequiredService<DataverseWebApiService>()
            .RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty));
}

/// <summary>A write inside an async lambda — compiled into a closure's state machine, not the method that holds it.</summary>
internal static class PoaBypassControl_AsyncLambda
{
    internal static Func<Task> InsideAnAsyncLambda(DataverseWebApiService client) =>
        async () => await client.GrantAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty), "ReadAccess");
}

/// <summary>A method-group conversion — an <c>ldftn</c>, no call instruction, and no <c>(</c> for a text rule to find.</summary>
internal static class PoaBypassControl_MethodGroup
{
    internal static Func<string, Guid, DataversePrincipalRef, string, CancellationToken, Task> AsAMethodGroup(DataverseWebApiService client) =>
        client.ModifyAccessAsync;
}

/// <summary>An expression tree — an <c>ldtoken</c> of the method, compiled and invoked at run time.</summary>
internal static class PoaBypassControl_ExpressionTree
{
    internal static Expression<Func<Task>> AsAnExpressionTree(DataverseWebApiService client) =>
        () => client.RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty), CancellationToken.None);
}

/// <summary>The SDK's POA message — however its namespace is imported.</summary>
internal static class PoaBypassControl_SdkMessage
{
    internal static object SdkGrant() => new Microsoft.Crm.Sdk.Messages.GrantAccessRequest();
}

/// <summary>
/// POA actions and write methods named as string constants — and, as the positive half, constants that only share a
/// prefix with one (other words, not names).
/// </summary>
internal static class PoaBypassControl_NamedAsConstants
{
    private const string Grant = "Grant";

    /// <summary>Folded by the compiler into ONE constant, <c>"GrantAccess"</c> — invisible to a text rule.</summary>
    internal static string FoldedFromConstants() => Grant + "Access";

    internal static string ActionUrl(string baseUrl) => $"{baseUrl}/RevokeAccess";

    internal static string BatchLine() => "POST Microsoft.Dynamics.CRM.ModifyAccess HTTP/1.1";

    internal static string WriteMethodName() => nameof(DataverseWebApiService.ModifyAccessAsync);

    /// <summary>A <c>dynamic</c> call: its member name is a binder string, not a method token — C4 sees the string.</summary>
    internal static Task ThroughDynamic(object client) =>
        ((dynamic)client).RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty));

    internal static string OtherWords() => "GrantAccessRequest rejected; RevokeAccessAsyncHandler, ModifyAccessible";
}

/// <summary>Positive control for the compiled rules — the sanctioned shape: every write through the seam's interface.</summary>
internal static class PoaWriteThroughSeamControl
{
    internal static async Task AllThreeWrites(IDataverseRecordShareService seam)
    {
        var principal = DataversePrincipalRef.User(Guid.Empty);
        await seam.GrantAccessAsync("sprk_projects", Guid.Empty, principal, "ReadAccess");
        await seam.ModifyAccessAsync("sprk_projects", Guid.Empty, principal, "ReadAccess");
        await seam.RevokeAccessAsync("sprk_projects", Guid.Empty, principal);
    }
}
