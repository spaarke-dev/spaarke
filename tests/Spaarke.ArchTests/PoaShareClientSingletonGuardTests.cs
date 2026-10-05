using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
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
    // AccessCacheInvalidationTests.Shares proves it per write). That covers a share write only if it reaches POA through the
    // seam. WHAT THIS GUARD ENFORCES — exactly the rules below, and nothing beyond them (owner round 48 (c)): every compiled
    // call path to the client's POA writes, a ban on UnsafeAccessor, and a ban on choosing or invoking a method by reflection.
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
    //   C3. A write ON the seam that does not evict — on any path. (a) The seam references the client's writes only from
    //       the three methods the interface map binds to IDataverseRecordShareService's Grant/Modify/RevokeAccessAsync,
    //       compared by METADATA identity (so a same-name overload that writes — task 132 f1-v1c verifier seed N2 — is
    //       outside them), each its own write (TheSeamWritesPoaOnlyFromItsThreeInterfaceMethods). (b) In each of the three,
    //       EVERY path through the compiled body (IlPathScan: the async state machine, its exceptions, suspensions and
    //       resumptions, every finally) that makes the client write first awaits it, then calls the seam's EVICTION with the
    //       SAME entitySetName and recordId, and awaits that, before the method completes — returned, thrown or cancelled
    //       (EveryPathThroughEachSeamWriteAwaitsItThenEvictsThatRecord; seed N1, a team-only early return that wrote with no
    //       eviction, is exactly a path this rejects). (c) What counts as the eviction is found in the IL, not named: a seam
    //       method the writes call that, on every path, calls IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync
    //       with its own entitySetName and recordId and awaits it — and one must exist
    //       (TheSeamsWritesCallAnEvictionThatInvalidatesThatRecordOnEveryPath). A renamed helper is followed; a helper with
    //       a path that skips the invalidation is no eviction. What the invalidator then evicts for those arguments is the
    //       behaviour tests' (AccessCacheInvalidationTests.Shares: every write x every principal kind x each outcome, over
    //       the production seam and client).
    //   C4. A POA action name loaded as a string constant anywhere but the client — an SDK OrganizationRequest by name, a
    //       hand-built POST or $batch, a name the compiler folded from constant pieces, in any letter case — and a POA write
    //       method's name loaded as a constant (reflection, nameof, a `dynamic` call's binder name)
    //       (NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite).
    //   C5. The SDK's GrantAccessRequest / ModifyAccessRequest / RevokeAccessRequest, however they are imported (a global
    //       using defeats the text rule T2) (NoCompiledCodeUsesTheSdkPoaMessages).
    //   C6. A POA action name carried by METADATA rather than a load — a type, member, enum value or parameter NAMED as one
    //       (`enum PoaAction { GrantAccess }` + `action.ToString()`: f1-v1c verifier observation), and a POA action or write
    //       name in a const field, a default parameter value, a custom attribute argument, an embedded resource, or constant
    //       DATA — a UTF-8 literal (`"…"u8`) or a byte / char array initializer, an RVA field's bytes
    //       (NoCompiledMetadataOutsideTheClientNamesAPoaAction).
    //   C7. No method CHOSEN or INVOKED by reflection, and no code the compiled scan cannot read — on ANY type, so on the
    //       Dataverse service types and on any Type obtained from them, however obtained (typeof, GetType() on the client,
    //       a type parameter, a type found by enumeration): no method or member lookup on Type / TypeInfo / IReflect, their
    //       extension spellings or a Module (by name, signature or metadata token), no property or event accessor method,
    //       no name-based Expression.Call, no MethodBase.Invoke, InvokeMember, Delegate / MethodInfo.CreateDelegate,
    //       MethodInvoker / ConstructorInvoker, method function pointer, compiled expression tree or `dynamic` member access,
    //       no System.Reflection.Emit, no assembly loaded at run time (NoCompiledCodeChoosesOrInvokesAMethodByReflection;
    //       task 132 f1-v1c-v1 verifier seed R chose RevokeAccessAsync by its SIGNATURE and invoked it — owner round 48 (b)).
    //       Not banned, because none of it reaches a method: reading properties, fields and attributes, a type's name,
    //       constructing by type, embedded resources, an expression tree a LINQ provider translates.
    //   C8. No [UnsafeAccessor] / [UnsafeAccessorType] in the compiled metadata of the scan set, however the attribute was
    //       spelled or aliased, generated code included (NoCompiledCodeCarriesAnUnsafeAccessor; verifier seed U: an
    //       UnsafeAccessor extern that IS the client's RevokeAccessAsync, a call C1 cannot see — owner round 48 (a)).
    // TEXT — every src/server .cs file, compiled into the BFF or not:
    //   T1. A `.XAccessAsync(` call whose receiver is not an identifier the file declares ONLY as
    //       IDataverseRecordShareService: an expression receiver (S5), a `var`, `dynamic` or undeclared name, a name the
    //       file also declares as another type (seed S6). Conservative — it may flag a legal call; C1 is the precise rule.
    //       (EveryPoaShareWriteGoesThroughTheEvictingSeam; per named writer: EachNamedShareWriterWritesOnlyThroughTheSeam)
    //   T2. The SDK messages (NoServerFileSendsSdkPoaMessages). T3. A second POA payload (task 060's
    //       PoaActionPayloadsAreBuiltInExactlyOnePlace, above). T4. A POA write method named as a string
    //       (NoServerFileNamesTheClientsPoaWritesAsAString).
    //   T5. A POA action or write name in the configuration the BFF is deployed with — src/server configuration files and
    //       the Bicep / ARM under infra/ and infrastructure/bicep/ (NoDeployedConfigurationNamesAPoaAction).
    //   T6. UnsafeAccessor named in any .cs / .csproj / .props / .targets file under src/server — the projects outside the
    //       compiled scan set included — or in the Directory.Build files above it (NoServerSourceNamesUnsafeAccessor).
    //
    // NOT ENFORCED — the build proves nothing about these; they are review's to catch: a POA write by any route that uses
    // none of the mechanisms above — above all a raw HTTP call whose action URL exists only at RUN time (assembled from
    // pieces none of which is the action name — fragments joined by a call, an enum value's name plus a runtime suffix,
    // single characters, a decoding — or read from a LIVE store no file in this repository holds: an App Service setting
    // set by hand, Key Vault, Dataverse, an HTTP response); native code; code outside the scan set (it cannot name the
    // client and does not run in the BFF). Not this guard's job: POA writes made OUTSIDE the BFF (MDA sharing, flows,
    // operator scripts) — their staleness is bounded by the caches' TTLs (caching-architecture.md, owner R3/R4).
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
    /// A string constant that carries a POA action name as a whole word, in ANY letter case (a lower-case constant upper-cased
    /// at run time is still the action): the bare action (the Web API route segment, the SDK's <c>RequestName</c>), a path
    /// or URL ending in it (<c>…/RevokeAccess</c>), a qualified name (<c>Microsoft.Dynamics.CRM.ModifyAccess</c>), a
    /// <c>$batch</c> line. Deliberately conservative: a log message that names an action as a word is flagged too (reword
    /// it); <c>GrantAccessAsync</c> and <c>GrantAccessRequest</c> are other words.
    /// </summary>
    private static readonly Regex PoaActionConstant = new(
        @"\b(GrantAccess|ModifyAccess|RevokeAccess)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

    /// <summary>Every assembly the compiled rules scan (loading each; a failure to load fails the rule).</summary>
    private static readonly Lazy<IReadOnlyList<Assembly>> ScannedAssemblies = new(() =>
        ProjectsTheCompiledScanCovers()
            .Select(p => Assembly.Load(new AssemblyName(p.AssemblyName)))
            .ToList());

    /// <summary>Every type of every assembly the compiled rules scan.</summary>
    private static readonly Lazy<IReadOnlyList<Type>> ScannedTypes = new(() =>
        ScannedAssemblies.Value.SelectMany(a => a.GetTypes()).ToList());

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

    // ── C3. the seam's writes: only its three interface methods, and every path through each evicts ──────

    /// <summary>The seam's write methods, by the interface member each implements — never by name.</summary>
    private static readonly string[] SeamWriteNames =
    {
        nameof(IDataverseRecordShareService.GrantAccessAsync),
        nameof(IDataverseRecordShareService.ModifyAccessAsync),
        nameof(IDataverseRecordShareService.RevokeAccessAsync),
    };

    /// <summary>The key both a write and its eviction carry: the record whose caches the write stales.</summary>
    private static readonly string[] ShareKey = { "entitySetName", "recordId" };

    /// <summary>
    /// The seam method the interface map binds to <see cref="IDataverseRecordShareService"/>'s write of that name — the
    /// method every caller of the seam actually runs. A same-named overload on the seam is not it (task 132 seed N2).
    /// </summary>
    internal static MethodInfo SeamWriteMethod(string write)
    {
        var map = typeof(DataverseRecordShareService).GetInterfaceMap(typeof(IDataverseRecordShareService));
        var slot = Enumerable.Range(0, map.InterfaceMethods.Length).Single(i => map.InterfaceMethods[i].Name == write);
        return map.TargetMethods[slot];
    }

    /// <summary>
    /// The seam's eviction, found in the IL rather than named: every method declared on the seam that the three writes'
    /// compiled bodies call (the writes themselves excepted), with what the every-path analysis found in it. A renamed or
    /// replaced helper is followed; a seam method that does not invalidate its record on every path — a logging helper, or
    /// an eviction helper with a path that skips the invalidation — does not count as the eviction.
    /// </summary>
    internal static IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> SeamEvictionCandidates()
    {
        var writes = SeamWriteNames.Select(SeamWriteMethod).ToList();
        return IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DataverseRecordShareService)))
            .Where(r => IlCallScan.SourceMethod(r.Caller, r.CallerMethod) is { } source
                        && writes.Any(w => w.HasSameMetadataDefinitionAs(source)))
            .Select(r => r.Target)
            .OfType<MethodInfo>()
            .Where(t => t.DeclaringType == typeof(DataverseRecordShareService) && !writes.Any(w => w.HasSameMetadataDefinitionAs(t)))
            .DistinctBy(t => t.MetadataToken)
            .Select(t => (t, ShareKey.All(k => t.GetParameters().Any(p => p.Name == k))
                ? IlPathScan.Analyse(t, EvictionHelperRule)
                : new IlPathScan.Result(new[] { "takes no entitySetName / recordId to invalidate" }, Array.Empty<int>(), Array.Empty<int>())))
            .ToList();
    }

    /// <summary>Whether <paramref name="target"/> is one of <paramref name="candidates"/> that invalidates its record on every path.</summary>
    internal static bool IsAnEviction(IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> candidates, MethodBase target) =>
        candidates.Any(c => c.Result.Violations.Count == 0
                            && c.Result.DischargingCalls.Count > 0
                            && c.Method.HasSameMetadataDefinitionAs(target));

    /// <summary>C3 (b): a client POA write opens the obligation; an eviction of the same record discharges it.</summary>
    internal static IlPathScan.Rule SeamWriteRule(Func<MethodBase, bool> isEviction) => new()
    {
        Opens = IsTheClientsPoaWrite,
        Discharges = isEviction,
        KeyParameters = ShareKey,
        Opening = "the POA write",
        Discharge = "the eviction",
    };

    /// <summary>C3 (c): a seam method counts as the eviction when it owes, from entry, the invalidator's share-change eviction of its own record — and pays it on every path.</summary>
    internal static readonly IlPathScan.Rule EvictionHelperRule = new()
    {
        Opens = _ => false,
        Discharges = target => target.DeclaringType == typeof(IMembershipCacheInvalidator)
                               && target.Name == nameof(IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync),
        KeyParameters = ShareKey,
        OwedAtEntry = true,
        Opening = "the helper",
        Discharge = "the invalidation",
    };

    /// <summary>
    /// The seam's writes that are NOT one of <paramref name="allowed"/> calling its own same-named client write, as
    /// <c>"{seam method(signature)} → {client write}"</c>. Methods are compared by metadata identity, so an overload that
    /// shares a pinned method's name is reported; an async state machine counts as its method; a lambda or local function
    /// counts as itself (never as the method that holds it).
    /// </summary>
    internal static IReadOnlyList<string> SeamWritesOutside(
        IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> references,
        IReadOnlyCollection<MethodInfo> allowed) =>
        references
            .Where(r => IsTheClientsPoaWrite(r.Target))
            .Select(r => (Source: IlCallScan.SourceMethod(r.Caller, r.CallerMethod), r.Caller, r.CallerMethod, r.Target))
            .Where(w => w.Source is null
                        || w.Source.Name != w.Target.Name
                        || !allowed.Any(a => a.HasSameMetadataDefinitionAs(w.Source)))
            .Select(w => $"{(w.Source is null ? $"{w.Caller.Name}.{w.CallerMethod.Name}" : Signature(w.Source))} → {w.Target.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    private static string Signature(MethodBase method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    [Fact(DisplayName = "Task 132: the seam writes POA only from its three interface methods, each its own write")]
    public void TheSeamWritesPoaOnlyFromItsThreeInterfaceMethods()
    {
        var writeMethods = SeamWriteNames.Select(SeamWriteMethod).ToList();
        var references = IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DataverseRecordShareService))).ToList();

        // Vacuity: each of the three is seen writing.
        var writers = references
            .Where(r => IsTheClientsPoaWrite(r.Target))
            .Select(r => IlCallScan.SourceMethod(r.Caller, r.CallerMethod))
            .ToList();
        Assert.True(
            writeMethods.All(m => writers.Any(w => w is not null && m.HasSameMetadataDefinitionAs(w))),
            "precondition: the compiled scan sees each of the seam's three interface methods write");

        var outside = SeamWritesOutside(references, writeMethods);

        Assert.True(
            outside.Count == 0,
            "the seam may write POA only from the three methods that implement IDataverseRecordShareService's "
            + "Grant/Modify/RevokeAccessAsync — the three whose every path the rule below proves evicts. A write from any other "
            + "seam method (an overload, a helper, a lambda) is a write no rule follows to its eviction (task 132): make it "
            + "through one of the three."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", outside)}");
    }

    [Theory(DisplayName = "Task 132: every path through each seam write awaits the write, then evicts that record and awaits it")]
    [InlineData(nameof(IDataverseRecordShareService.GrantAccessAsync))]
    [InlineData(nameof(IDataverseRecordShareService.ModifyAccessAsync))]
    [InlineData(nameof(IDataverseRecordShareService.RevokeAccessAsync))]
    public void EveryPathThroughEachSeamWriteAwaitsItThenEvictsThatRecord(string write)
    {
        var candidates = SeamEvictionCandidates();
        var result = IlPathScan.Analyse(SeamWriteMethod(write), SeamWriteRule(target => IsAnEviction(candidates, target)));

        Assert.True(result.OpeningCalls.Count > 0, $"precondition: the path analysis reaches {write}'s client write");
        Assert.True(
            result.Violations.Count == 0,
            $"DataverseRecordShareService.{write} has a path on which the POA write is not followed — after it completes, with "
            + "the same entitySetName and recordId, awaited — by a call to a seam method that invalidates that record on every "
            + "path, before the method returns, throws or is cancelled (task 132). On that path the sharee's root set and every "
            + "snapshot of the record stay stale for their TTLs: an unshare that keeps access. Keep the write inside the try "
            + "whose finally evicts."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", result.Violations)}"
            + $"{Environment.NewLine}  the seam methods the writes call: {Describe(candidates)}");
    }

    [Fact(DisplayName = "Task 132: the seam's writes call an eviction that invalidates that record on every path and awaits it")]
    public void TheSeamsWritesCallAnEvictionThatInvalidatesThatRecordOnEveryPath()
    {
        var candidates = SeamEvictionCandidates();

        Assert.True(
            candidates.Any(c => IsAnEviction(candidates, c.Method)),
            "none of the seam methods the three writes call invalidates, on every path, the record it is given — "
            + "IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync with its own entitySetName and recordId, awaited "
            + "(task 132): every share write would leave the record's access caches stale."
            + $"{Environment.NewLine}  {Describe(candidates)}");
    }

    private static string Describe(IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> candidates) =>
        candidates.Count == 0
            ? "(none)"
            : string.Join(
                Environment.NewLine + "  ",
                candidates.Select(c => $"{Signature(c.Method)}: "
                                       + (c.Result.Violations.Count == 0 && c.Result.DischargingCalls.Count > 0
                                           ? "invalidates on every path"
                                           : string.Join("; ", c.Result.Violations.DefaultIfEmpty("never invalidates")))));

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

    // ── C6. POA action names carried by metadata ─────────────────────────────────────────────────

    /// <summary>A POA write method's name as a whole word in free text (an embedded resource).</summary>
    private static readonly Regex PoaWriteNameWord = new(
        @"\b(GrantAccessAsync|ModifyAccessAsync|RevokeAccessAsync)\b",
        RegexOptions.CultureInvariant);

    /// <summary>Whether bytes, read as UTF-8 or as UTF-16, name a POA action (any case) or a POA write method as a word.</summary>
    private static bool TextNamesAPoaActionOrWrite(byte[] bytes) =>
        new[] { Encoding.UTF8.GetString(bytes), Encoding.Unicode.GetString(bytes) }
            .Any(text => PoaActionConstant.IsMatch(text) || PoaWriteNameWord.IsMatch(text));

    /// <summary>The methods that load <paramref name="field"/> (its address, value or token), or its own name when none does.</summary>
    private static string UsersOf(FieldInfo field)
    {
        var users = field.Module.Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(DeclaredMembers).Cast<MethodBase>().Concat(t.GetConstructors(DeclaredMembers)))
            .Where(m => IlCallScan.Instructions(m).Any(i =>
                i.OpCode.OperandType is OperandType.InlineField or OperandType.InlineTok && i.Operand == field.MetadataToken))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        return users.Count > 0 ? string.Join(", ", users) : $"{field.Module.Assembly.GetName().Name} {field.DeclaringType?.FullName}.{field.Name}";
    }

    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// Every place in <paramref name="types"/> and <paramref name="assemblies"/> where METADATA, not a loaded constant,
    /// carries a POA action outside the client: a type, member, enum value or parameter NAMED as one (an
    /// <c>enum PoaAction { GrantAccess }</c> becomes the action through <c>ToString()</c> with no <c>ldstr</c> at all —
    /// task 132 f1-v1c verifier observation), and a POA action or write-method name held as a const field's value, a
    /// default parameter value, a custom attribute argument (types, members, parameters, return values, the assembly and
    /// its modules), the text of an embedded resource, or constant DATA (a UTF-8 literal <c>"…"u8</c>, a byte or char array
    /// initializer — an RVA field's bytes, read with no <c>ldstr</c>) — all of which reflection, a resource read or a span hands
    /// the code at run time. Names are matched against the action words only (a member named <c>GrantAccessAsync</c> is
    /// another word). Constant data is reported under the methods that use it.
    /// </summary>
    internal static IReadOnlyList<string> CompiledPoaMetadata(IEnumerable<Type> types, IEnumerable<Assembly> assemblies)
    {
        var found = new List<string>();

        static IEnumerable<object?> Flatten(CustomAttributeTypedArgument argument) =>
            argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items ? items.SelectMany(Flatten) : new[] { argument.Value };

        void Attributes(string where, IEnumerable<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                foreach (var value in attribute.ConstructorArguments
                             .Concat(attribute.NamedArguments.Select(n => n.TypedValue))
                             .SelectMany(Flatten))
                {
                    if (value is string text && NamesAPoaActionOrWrite(text))
                    {
                        found.Add($"{where}: [{attribute.AttributeType.Name}(\"{text}\")]");
                    }
                }
            }
        }

        void Named(string where, string name, string what)
        {
            if (PoaActionConstant.IsMatch(name))
            {
                found.Add($"{where}: {what} named \"{name}\"");
            }
        }

        foreach (var type in types.Where(t => IlCallScan.Outermost(t) != typeof(DataverseWebApiService)))
        {
            Named(type.FullName!, type.Name, "type");
            Attributes(type.FullName!, type.CustomAttributes);

            foreach (var member in type.GetMembers(DeclaredMembers).Where(m => m.MemberType != MemberTypes.NestedType))
            {
                var where = $"{type.FullName}.{member.Name}";
                Named(where, member.Name, type.IsEnum ? "enum value" : "member");
                Attributes(where, member.CustomAttributes);

                if (member is FieldInfo { IsLiteral: true } field
                    && field.GetRawConstantValue() is string constant
                    && NamesAPoaActionOrWrite(constant))
                {
                    found.Add($"{where}: const \"{constant}\"");
                }

                // Constant DATA the compiler stores outside the string heap: a UTF-8 literal ("…"u8) and a byte or char
                // array initializer are an RVA field's bytes, read with no ldstr and no metadata name.
                if (member is FieldInfo { IsStatic: true } data
                    && data.Attributes.HasFlag(FieldAttributes.HasFieldRVA)
                    && TextNamesAPoaActionOrWrite(RuntimeHelpers.CreateSpan<byte>(data.FieldHandle).ToArray()))
                {
                    found.Add($"{UsersOf(data)}: constant data (a UTF-8 literal or a byte / char array initializer) holding a POA action");
                }

                if (member is not MethodBase method)
                {
                    continue;
                }

                foreach (var parameter in method.GetParameters())
                {
                    Named($"{where}({parameter.Name})", parameter.Name ?? string.Empty, "parameter");
                    Attributes($"{where}({parameter.Name})", parameter.CustomAttributes);
                    if (parameter.HasDefaultValue && parameter.RawDefaultValue is string defaultValue && NamesAPoaActionOrWrite(defaultValue))
                    {
                        found.Add($"{where}({parameter.Name}): default \"{defaultValue}\"");
                    }
                }

                if (method is MethodInfo withReturn)
                {
                    Attributes($"{where} (return)", withReturn.ReturnParameter.CustomAttributes);
                }
            }
        }

        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name!;
            Attributes(name, assembly.CustomAttributes);
            foreach (var module in assembly.GetModules())
            {
                Attributes($"{name} module {module.Name}", module.CustomAttributes);
            }

            foreach (var resource in assembly.GetManifestResourceNames())
            {
                using var stream = assembly.GetManifestResourceStream(resource)
                                   ?? throw new InvalidOperationException($"{name}: embedded resource {resource} cannot be read.");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                if (TextNamesAPoaActionOrWrite(buffer.ToArray()))
                {
                    found.Add($"{name}: embedded resource {resource}");
                }
            }
        }

        return found.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    [Fact(DisplayName = "Task 132: no compiled metadata outside the client names a POA action (names, enum values, consts, attributes, resources)")]
    public void NoCompiledMetadataOutsideTheClientNamesAPoaAction()
    {
        var offenders = CompiledPoaMetadata(ScannedTypes.Value, ScannedAssemblies.Value);

        Assert.True(
            offenders.Count == 0,
            "a POA action named by metadata — a type, member, enum value or parameter of that name, or a const, default "
            + "value, attribute argument or embedded resource holding it — reaches a raw POST or an SDK request through "
            + "ToString(), reflection or a resource read with no constant loaded, and without the seam's access-cache "
            + "eviction (task 132). Call IDataverseRecordShareService; rename or reword the rest."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── C7. no method chosen or invoked by reflection, and no code the compiled scan cannot read ──────
    //
    // C1 reads the method tokens the COMPILER resolved. A method the code chooses at RUN time has no such token: task 132
    // f1-v1c-v1 verifier seed R found the client's RevokeAccessAsync by its signature (`typeof(DataverseWebApiService)
    // .GetMethods().First(x => <4 parameters, the third a DataversePrincipalRef, returns Task>)`) and invoked it — no name,
    // no constant, all 622 ArchTests green. Owner round 48 (b): close the CLASS. The rule bans the mechanism — choosing a
    // method by reflection, invoking one reflectively, or bringing in code the scan cannot read — on ANY type, so where the
    // Type came from (typeof, GetType() on the client, a type parameter, a type found by enumeration) does not matter.

    private const string ChoosesAMethod = "chooses a method by reflection";
    private const string InvokesReflectively = "invokes reflectively";
    private const string RunsUnscannedCode = "runs code the compiled scan cannot read";

    /// <summary>Member lookups on <see cref="Type"/> / <c>TypeInfo</c> / <see cref="IReflect"/> that can return a method.</summary>
    private static readonly HashSet<string> TypeMethodLookups = new(StringComparer.Ordinal)
    {
        "GetMethod", "GetMethods", "GetMember", "GetMembers", "GetDefaultMembers", "FindMembers",
        "GetMemberWithSameMetadataDefinitionAs", "GetInterfaceMap", "get_DeclaredMethods", "get_DeclaredMembers",
        "GetDeclaredMethod", "GetDeclaredMethods", "get_DeclaringMethod",
    };

    /// <summary>The accessor METHODS of a property or an event.</summary>
    private static readonly HashSet<string> AccessorLookups = new(StringComparer.Ordinal)
    {
        "get_GetMethod", "get_SetMethod", "GetGetMethod", "GetSetMethod", "GetAccessors",
        "get_AddMethod", "get_RemoveMethod", "get_RaiseMethod", "GetAddMethod", "GetRemoveMethod", "GetRaiseMethod", "GetOtherMethods",
    };

    /// <summary>The extension-method spellings of the same lookups (matched by full type name: the test need not reference them).</summary>
    private static readonly HashSet<string> ReflectionExtensionTypes = new(StringComparer.Ordinal)
    {
        "System.Reflection.RuntimeReflectionExtensions", "System.Reflection.TypeExtensions",
        "System.Reflection.PropertyInfoExtensions", "System.Reflection.EventInfoExtensions",
    };

    private static readonly HashSet<string> ExtensionLookups = new(StringComparer.Ordinal)
    {
        "GetRuntimeMethod", "GetRuntimeMethods", "GetRuntimeInterfaceMap", "GetMethodInfo",
        "GetMethod", "GetMethods", "GetMember", "GetMembers", "GetDefaultMembers",
        "GetGetMethod", "GetSetMethod", "GetAccessors", "GetAddMethod", "GetRemoveMethod", "GetRaiseMethod",
    };

    /// <summary>
    /// What a reference to <paramref name="target"/> does, when it is one of the reflection APIs C7 bans; otherwise
    /// <c>null</c>. Banned: (1) CHOOSING a method — a method or member lookup on <see cref="Type"/> / <c>TypeInfo</c> /
    /// <see cref="IReflect"/> (<see cref="TypeMethodLookups"/>), its extension spellings, a <see cref="Module"/> lookup by
    /// name or metadata token, a property's or event's accessor methods, a name-based <c>Expression.Call</c>, a function
    /// pointer turned back into a handle; (2) INVOKING reflectively — <c>MethodBase.Invoke</c> (methods and constructors),
    /// <c>InvokeMember</c>, <c>Delegate.CreateDelegate</c> / <c>MethodInfo.CreateDelegate</c>,
    /// <c>MethodInvoker</c> / <c>ConstructorInvoker</c>, a method's function pointer, a delegate for a function pointer,
    /// compiling an expression tree, a <c>dynamic</c> member access (the C# run-time binder); (3) RUNNING CODE THE SCAN
    /// CANNOT READ — <c>System.Reflection.Emit</c>, an assembly loaded at run time (from bytes, a path or a name). Not
    /// banned, because none of it reaches a method: reading properties, fields and attributes, a type's name, constructing
    /// by type (<c>Activator.CreateInstance(Type)</c>), embedded resources, building an expression tree for a LINQ provider
    /// to translate.
    /// </summary>
    internal static string? ReflectiveMethodAccess(MethodBase target)
    {
        if (target.DeclaringType is not { } declaring)
        {
            return null;
        }

        var name = target.Name;
        var fullName = (declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring).FullName ?? declaring.Name;
        bool Is<T>() => typeof(T).IsAssignableFrom(declaring);

        return name switch
        {
            "InvokeMember" when Is<Type>() || declaring == typeof(IReflect) => InvokesReflectively,
            _ when (Is<Type>() || declaring == typeof(IReflect)) && TypeMethodLookups.Contains(name) => ChoosesAMethod,
            _ when (Is<PropertyInfo>() || Is<EventInfo>()) && AccessorLookups.Contains(name) => ChoosesAMethod,
            _ when ReflectionExtensionTypes.Contains(fullName) && ExtensionLookups.Contains(name) => ChoosesAMethod,
            "ResolveMethod" or "ResolveMember" or "GetMethod" or "GetMethods" when Is<Module>() => ChoosesAMethod,
            "Call" when declaring == typeof(Expression) && target.GetParameters().Any(p => p.ParameterType == typeof(string)) => ChoosesAMethod,
            "FromIntPtr" when declaring == typeof(RuntimeMethodHandle) => ChoosesAMethod,
            "Invoke" or "CreateDelegate" when Is<MethodBase>() => InvokesReflectively,
            "CreateDelegate" when Is<Delegate>() => InvokesReflectively,
            "GetFunctionPointer" when declaring == typeof(RuntimeMethodHandle) => InvokesReflectively,
            "GetDelegateForFunctionPointer" when declaring == typeof(System.Runtime.InteropServices.Marshal) => InvokesReflectively,
            "Compile" or "CompileToMethod" when Is<LambdaExpression>() => InvokesReflectively,
            _ when fullName is "System.Reflection.MethodInvoker" or "System.Reflection.ConstructorInvoker" => InvokesReflectively,
            _ when fullName == "Microsoft.CSharp.RuntimeBinder.Binder" => InvokesReflectively,
            _ when declaring.Namespace == "System.Reflection.Emit" => RunsUnscannedCode,
            "Load" or "LoadFrom" or "LoadFile" or "UnsafeLoadFrom" or "LoadWithPartialName" or "ReflectionOnlyLoad" or "ReflectionOnlyLoadFrom"
                when Is<Assembly>() => RunsUnscannedCode,
            _ when Is<System.Runtime.Loader.AssemblyLoadContext>() && name.StartsWith("LoadFrom", StringComparison.Ordinal) => RunsUnscannedCode,
            "Load" or "ExecuteAssembly" or "ExecuteAssemblyByName" or "CreateInstance" or "CreateInstanceAndUnwrap" or "CreateInstanceFrom"
                or "CreateInstanceFromAndUnwrap" when declaring == typeof(AppDomain) => RunsUnscannedCode,
            "CreateInstanceFrom" when declaring == typeof(Activator) => RunsUnscannedCode,
            "CreateInstance" when declaring == typeof(Activator) && target.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(string)
                => RunsUnscannedCode,
            _ => null,
        };
    }

    /// <summary>Every reference in <paramref name="references"/> to an API <see cref="ReflectiveMethodAccess"/> bans.</summary>
    internal static IReadOnlyList<string> CompiledReflectiveMethodAccess(
        IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> references) =>
        references
            .Select(r => (r.Caller, r.Target, What: ReflectiveMethodAccess(r.Target)))
            .Where(r => r.What is not null)
            .Select(r => $"{IlCallScan.Outermost(r.Caller).FullName} → {r.Target.DeclaringType!.Name}.{r.Target.Name} ({r.What})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>The scanned references to a reflection API C7 bans.</summary>
    private static readonly Lazy<IReadOnlyList<(Type Caller, MethodBase CallerMethod, MethodBase Target)>> ScannedReflectiveReferences = new(() =>
        IlCallScan.MethodReferences(ScannedTypes.Value)
            .Where(r => ReflectiveMethodAccess(r.Target) is not null)
            .ToList());

    [Fact(DisplayName = "Task 132: no compiled code chooses or invokes a method by reflection, or runs code the scan cannot read")]
    public void NoCompiledCodeChoosesOrInvokesAMethodByReflection()
    {
        var offenders = CompiledReflectiveMethodAccess(ScannedReflectiveReferences.Value);

        Assert.True(
            offenders.Count == 0,
            "a method chosen by reflection (by name, signature, attribute or position), invoked reflectively, or run from code "
            + "the compiled scan cannot read (emitted IL, an assembly loaded at run time) has no method token for the compiled "
            + "rules to read — task 132 verifier seed R reached DataverseWebApiService.RevokeAccessAsync by its signature, without "
            + "the seam's access-cache eviction. Owner round 48 (b) bans the mechanism in every assembly the BFF runs, on any type "
            + "(so on the Dataverse service types and on any Type obtained from them, however obtained). Call the method "
            + "directly; share through IDataverseRecordShareService. A genuine need for reflection is a review decision: name the "
            + "site and the type it reflects over here, and that type must not be a Dataverse service type."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── C8 / T6. no [UnsafeAccessor] anywhere in src/server ───────────────────────────────────────
    //
    // Task 132 f1-v1c-v1 verifier seed U: `[UnsafeAccessor(UnsafeAccessorKind.Method)] private static extern Task
    // RevokeAccessAsync(DataverseWebApiService client, …)` called unqualified IS the client's write — a compiled route whose
    // call target is declared on the probe type (C1 misses it), with no string and no reflection; it reached Dataverse with no
    // eviction. Spaarke has no legitimate use for UnsafeAccessor: it reaches private members regardless of visibility. Owner
    // round 48 (a): none anywhere in src/server — in the compiled metadata of the scan set (generated code included, however
    // the attribute was spelled or aliased) and in the text of every source and MSBuild file under src/server.

    /// <summary><c>[UnsafeAccessor]</c> and its companions (<c>[UnsafeAccessorType]</c>, .NET 10).</summary>
    internal static bool IsUnsafeAccessorAttribute(Type attributeType) =>
        attributeType.Namespace == "System.Runtime.CompilerServices"
        && attributeType.Name.StartsWith("UnsafeAccessor", StringComparison.Ordinal);

    /// <summary>
    /// Every place in <paramref name="types"/> and <paramref name="assemblies"/> that carries an UnsafeAccessor attribute: a
    /// type, a member (an <c>extern</c> accessor method above all), a parameter, a return value, a generic parameter, the
    /// assembly or a module.
    /// </summary>
    internal static IReadOnlyList<string> CompiledUnsafeAccessors(IEnumerable<Type> types, IEnumerable<Assembly> assemblies)
    {
        var found = new List<string>();

        void Check(string where, IEnumerable<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes.Where(a => IsUnsafeAccessorAttribute(a.AttributeType)))
            {
                found.Add($"{where}: [{attribute.AttributeType.Name}]");
            }
        }

        void CheckGenericParameters(string where, IEnumerable<Type> parameters)
        {
            foreach (var parameter in parameters)
            {
                Check($"{where}<{parameter.Name}>", parameter.CustomAttributes);
            }
        }

        foreach (var type in types)
        {
            Check(type.FullName!, type.CustomAttributes);
            if (type.IsGenericTypeDefinition)
            {
                CheckGenericParameters(type.FullName!, type.GetGenericArguments());
            }

            foreach (var member in type.GetMembers(DeclaredMembers).Where(m => m.MemberType != MemberTypes.NestedType))
            {
                var where = $"{type.FullName}.{member.Name}";
                Check(where, member.CustomAttributes);
                if (member is not MethodBase method)
                {
                    continue;
                }

                foreach (var parameter in method.GetParameters())
                {
                    Check($"{where}({parameter.Name})", parameter.CustomAttributes);
                }

                if (method is MethodInfo withReturn)
                {
                    Check($"{where} (return)", withReturn.ReturnParameter.CustomAttributes);
                }

                if (method.IsGenericMethodDefinition)
                {
                    CheckGenericParameters(where, method.GetGenericArguments());
                }
            }
        }

        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name!;
            Check(name, assembly.CustomAttributes);
            foreach (var module in assembly.GetModules())
            {
                Check($"{name} module {module.Name}", module.CustomAttributes);
            }
        }

        return found.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    [Fact(DisplayName = "Task 132: no compiled code the BFF runs, or that can name the client, carries an UnsafeAccessor")]
    public void NoCompiledCodeCarriesAnUnsafeAccessor()
    {
        var offenders = CompiledUnsafeAccessors(ScannedTypes.Value, ScannedAssemblies.Value);

        Assert.True(
            offenders.Count == 0,
            "an [UnsafeAccessor] extern reaches a member regardless of its visibility, through a call whose target is declared "
            + "on the accessor's own type — task 132 verifier seed U called DataverseWebApiService.RevokeAccessAsync that way, "
            + "around the seam and its access-cache eviction, with every other rule green. Spaarke has no legitimate use for it "
            + "(owner round 48 (a)): call the member directly, or expose what you need."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>
    /// The files T6 reads: every <c>.cs</c>, <c>.csproj</c>, <c>.props</c> and <c>.targets</c> under <c>src/server</c>
    /// (build output excluded), and the <c>Directory.Build.props</c> / <c>.targets</c> above it that those projects import
    /// — where a global <c>using</c> alias for the attribute could otherwise hide its name from the source that uses it.
    /// </summary>
    internal static IReadOnlyList<string> ServerSourceAndBuildFiles()
    {
        var sep = Path.DirectorySeparatorChar;
        var serverRoot = Path.Combine(SourceScan.RepoRoot, "src", "server");
        var underServer = Directory.EnumerateFiles(serverRoot, "*", SearchOption.AllDirectories)
            .Where(f => new[] { ".cs", ".csproj", ".props", ".targets" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                        && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}node_modules{sep}", StringComparison.Ordinal));
        var above = new[] { SourceScan.RepoRoot, Path.Combine(SourceScan.RepoRoot, "src") }
            .SelectMany(dir => new[] { "Directory.Build.props", "Directory.Build.targets" }.Select(f => Path.Combine(dir, f)))
            .Where(File.Exists);

        return underServer.Concat(above)
            .Select(RelativePath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether a file's text names UnsafeAccessor (the attribute, its kind enum, its type companion, an alias's target)
    /// outside a whole-line <c>//</c> comment (doc comments included). Conservative: a block comment, an XML comment or a
    /// string that names it is flagged too — reword it.
    /// </summary>
    internal static bool NamesUnsafeAccessor(string text) =>
        text.Split('\n').Any(line =>
            !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && line.Contains("UnsafeAccessor", StringComparison.Ordinal));

    [Fact(DisplayName = "Task 132: no source or MSBuild file under src/server names UnsafeAccessor")]
    public void NoServerSourceNamesUnsafeAccessor()
    {
        var files = ServerSourceAndBuildFiles();
        Assert.True(
            new[]
            {
                "src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj",
                "src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs",
                "src/server/services/Sprk.Provisioning.ControlPlane.Worker/Sprk.Provisioning.ControlPlane.Worker.csproj",
                "Directory.Build.props",
            }.All(files.Contains),
            "precondition: the walk reaches the BFF project, the client, a project outside the compiled scan set, and the "
            + "repository's Directory.Build.props");

        var offenders = files
            .Where(f => NamesUnsafeAccessor(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, f))))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "UnsafeAccessor is banned anywhere in src/server (owner round 48 (a); task 132 verifier seed U): it reaches private "
            + "members regardless of visibility, which is how a POA write can skip the evicting seam. Call the member "
            + "directly, or expose what you need."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── T5. POA action names in the configuration the BFF is deployed with ────────────────────────

    /// <summary>
    /// The configuration files the BFF process reads or is deployed with: <c>src/server/**</c> settings files (appsettings
    /// and their templates, XML / config / resx / YAML), everything under <c>infra/</c> (the Bicep and the Dataverse rows —
    /// playbooks, actions, tools — the BFF reads back at run time) and the Bicep / ARM under <c>infrastructure/bicep/</c>
    /// that sets its App Service.
    /// </summary>
    internal static IReadOnlyList<string> DeployedConfigurationFiles()
    {
        var sep = Path.DirectorySeparatorChar;

        IEnumerable<string> Under(string relative, params string[] extensions)
        {
            var root = Path.Combine(SourceScan.RepoRoot, relative);
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                                && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                                && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                                && !f.Contains($"{sep}node_modules{sep}", StringComparison.Ordinal))
                : Enumerable.Empty<string>();
        }

        return Under("src/server", ".json", ".xml", ".config", ".resx", ".yml", ".yaml", ".template")
            .Concat(Under("infra", ".bicep", ".bicepparam", ".json", ".yml", ".yaml", ".xml"))
            .Concat(Under("infrastructure/bicep", ".bicep", ".bicepparam", ".json", ".yml", ".yaml"))
            .Select(RelativePath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether a configuration file's text names a POA action (any case) or a POA write method as a word.</summary>
    internal static bool ConfigurationNamesAPoaAction(string text) => PoaActionConstant.IsMatch(text) || PoaWriteNameWord.IsMatch(text);

    [Fact(DisplayName = "Task 132: no configuration the BFF is deployed with names a POA action")]
    public void NoDeployedConfigurationNamesAPoaAction()
    {
        var files = DeployedConfigurationFiles();
        Assert.True(
            new[] { "src/server/api/Sprk.Bff.Api/appsettings.template.json", "infrastructure/bicep/customer.bicep" }.All(files.Contains),
            "precondition: the configuration walk reaches the BFF's appsettings template and the customer Bicep stack");

        var offenders = files
            .Where(f => ConfigurationNamesAPoaAction(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, f))))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "a POA action (or a POA write method) named in configuration is an action name the code reads at run time and "
            + "can POST or invoke by reflection — without the seam's access-cache eviction (task 132). Share through "
            + "IDataverseRecordShareService; do not configure a POA action."
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

        // C3 (a)'s pin, by metadata identity: the bound method passes; a same-name overload (task 132 seed N2) and a lambda
        // inside a pinned-name method are both reported; an async method's state machine maps back to its method.
        var pinControl = IlCallScan.WithNested(typeof(PoaSeamPinControl)).ToList();
        var bound = typeof(PoaSeamPinControl).GetMethod(
            nameof(PoaSeamPinControl.RevokeAccessAsync),
            BindingFlags.Instance | BindingFlags.NonPublic,
            new[] { typeof(string), typeof(Guid), typeof(DataversePrincipalRef), typeof(CancellationToken) })!;
        var granting = typeof(PoaSeamPinControl).GetMethod(nameof(PoaSeamPinControl.GrantAccessAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
        var outside = SeamWritesOutside(IlCallScan.MethodReferences(pinControl), new[] { bound, granting });
        Assert.Equal(2, outside.Count);
        Assert.Contains("RevokeAccessAsync(String, Guid, DataversePrincipalRef, Boolean, CancellationToken) → RevokeAccessAsync", outside);
        Assert.Contains(outside, w => w.EndsWith("→ GrantAccessAsync", StringComparison.Ordinal) && !w.StartsWith("GrantAccessAsync(", StringComparison.Ordinal));
        Assert.Equal(
            new[] { bound.Name },
            IlCallScan.MethodReferences(pinControl)
                .Where(r => IsTheClientsPoaWrite(r.Target))
                .Select(r => IlCallScan.SourceMethod(r.Caller, r.CallerMethod))
                .Where(m => m is not null && m.HasSameMetadataDefinitionAs(bound))
                .Select(m => m!.Name)
                .Distinct());

        // C8 — task 132 f1-v1c-v1 verifier seed U, verbatim: an [UnsafeAccessor] extern that IS the client's write. Every
        // rule before C8 passes it — C1 sees a call to the control's own extern, C4 / C6 see no POA word — which is the
        // finding; C8 flags the attribute however it is used (a method accessor, a field accessor, [UnsafeAccessorType]).
        var seedU = IlCallScan.WithNested(typeof(PoaBypassControl_UnsafeAccessor)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(seedU)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(seedU)));
        Assert.Empty(CompiledPoaMetadata(seedU, Array.Empty<Assembly>()));
        Assert.Equal(
            new[] { $"{typeof(PoaBypassControl_UnsafeAccessor).FullName}.RevokeAccessAsync: [UnsafeAccessorAttribute]" },
            CompiledUnsafeAccessors(seedU, Array.Empty<Assembly>()));
        Assert.Equal(
            new[]
            {
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.ClientOf: [UnsafeAccessorAttribute]",
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.DisposeByTypeName(target): [UnsafeAccessorTypeAttribute]",
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.DisposeByTypeName: [UnsafeAccessorAttribute]",
            },
            CompiledUnsafeAccessors(IlCallScan.WithNested(typeof(PoaBypassControl_UnsafeAccessorOtherShapes)), Array.Empty<Assembly>()));

        // C7 — task 132 f1-v1c-v1 verifier seed R, verbatim: the client's write chosen by its signature and invoked. C1, C4
        // and C6 pass it (no token for the write, no name, no constant); C7 flags the lookup and the invocation.
        var seedR = IlCallScan.WithNested(typeof(PoaBypassControl_ReflectionBySignature)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(seedR)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(seedR)));
        var bySignature = typeof(PoaBypassControl_ReflectionBySignature).FullName;
        Assert.Equal(
            new[]
            {
                $"{bySignature} → MethodBase.Invoke ({InvokesReflectively})",
                $"{bySignature} → Type.GetMethods ({ChoosesAMethod})",
            },
            CompiledReflectiveMethodAccess(IlCallScan.MethodReferences(seedR)));

        // C7's other shapes: each mechanism flagged, by the API it reaches (one line per API, so each shape is asserted).
        var reflection = typeof(PoaBypassControl_Reflection).FullName;
        Assert.Equal(
            new[]
            {
                $"{reflection} → Assembly.Load ({RunsUnscannedCode})",
                $"{reflection} → AssemblyLoadContext.LoadFromStream ({RunsUnscannedCode})",
                $"{reflection} → Binder.InvokeMember ({InvokesReflectively})",
                $"{reflection} → Delegate.CreateDelegate ({InvokesReflectively})",
                $"{reflection} → DynamicMethod..ctor ({RunsUnscannedCode})",
                $"{reflection} → Expression.Call ({ChoosesAMethod})",
                $"{reflection} → Expression`1.Compile ({InvokesReflectively})",
                $"{reflection} → IReflect.GetMethods ({ChoosesAMethod})",
                $"{reflection} → MethodInfo.CreateDelegate ({InvokesReflectively})",
                $"{reflection} → MethodInvoker.Create ({InvokesReflectively})",
                $"{reflection} → Module.ResolveMethod ({ChoosesAMethod})",
                $"{reflection} → PropertyInfo.get_GetMethod ({ChoosesAMethod})",
                $"{reflection} → RuntimeMethodHandle.GetFunctionPointer ({InvokesReflectively})",
                $"{reflection} → RuntimeReflectionExtensions.GetRuntimeMethods ({ChoosesAMethod})",
                $"{reflection} → Type.GetInterfaceMap ({ChoosesAMethod})",
                $"{reflection} → Type.GetMember ({ChoosesAMethod})",
                $"{reflection} → Type.GetMethods ({ChoosesAMethod})",
                $"{reflection} → Type.InvokeMember ({InvokesReflectively})",
                $"{reflection} → TypeInfo.get_DeclaredMethods ({ChoosesAMethod})",
            },
            CompiledReflectiveMethodAccess(IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(PoaBypassControl_Reflection)))));

        // ... and the reflection the BFF does use, which reaches no method, is not.
        var permitted = IlCallScan.WithNested(typeof(PoaReflectionPermittedControl)).ToList();
        Assert.Empty(CompiledReflectiveMethodAccess(IlCallScan.MethodReferences(permitted)));
        Assert.Empty(CompiledUnsafeAccessors(permitted, Array.Empty<Assembly>()));

        // The sanctioned shape — every write through the seam's interface — is none of these.
        var sanctioned = IlCallScan.WithNested(typeof(PoaWriteThroughSeamControl)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledSdkPoaMessageUses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(sanctioned)));
        Assert.Empty(CompiledPoaMetadata(sanctioned, Array.Empty<Assembly>()));
        Assert.Empty(CompiledReflectiveMethodAccess(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledUnsafeAccessors(sanctioned, Array.Empty<Assembly>()));
    }

    /// <summary>
    /// Negative and positive controls for C3 (b) and (c)'s path analysis: each shape below writes and then evicts on every
    /// path, or has one path that does not — task 132 seed N1 (a team-only early return) among them.
    /// </summary>
    [Fact(DisplayName = "Task 132: the path analysis flags every non-evicting write shape and passes the evicting ones")]
    public void PathAnalysis_FlagsEveryNonEvictingShape_AndPassesTheEvictingOnes()
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
        var controls = typeof(PoaEvictionPathControls);
        var evict = controls.GetMethod(nameof(PoaEvictionPathControls.EvictAsync), Any)!;
        var invalidate = controls.GetMethod(nameof(PoaEvictionPathControls.InvalidateAsync), Any)!;
        var writeRule = SeamWriteRule(target => target.HasSameMetadataDefinitionAs(evict));
        var helperRule = new IlPathScan.Rule
        {
            Opens = _ => false,
            Discharges = target => target.HasSameMetadataDefinitionAs(invalidate),
            KeyParameters = ShareKey,
            OwedAtEntry = true,
            Opening = "the helper",
            Discharge = "the invalidation",
        };

        IReadOnlyList<string> Violations(string method, IlPathScan.Rule rule) =>
            IlPathScan.Analyse(controls.GetMethod(method, Any)!, rule).Violations;

        // Evict on every path: the seam's own shape, a caught failure that returns inside the try, a real finally (using).
        foreach (var evicting in new[]
                 {
                     nameof(PoaEvictionPathControls.Canonical),
                     nameof(PoaEvictionPathControls.CatchAndReturnInsideTheTry),
                     nameof(PoaEvictionPathControls.UnderAUsing),
                 })
        {
            var result = IlPathScan.Analyse(controls.GetMethod(evicting, Any)!, writeRule);
            Assert.True(result.Violations.Count == 0, $"{evicting}: {string.Join("; ", result.Violations)}");
            Assert.NotEmpty(result.OpeningCalls);
            Assert.NotEmpty(result.DischargingCalls);
        }

        Assert.Empty(Violations(nameof(PoaEvictionPathControls.HelperCanonical), helperRule));

        // A path that writes and does not evict, each named by what it does wrong.
        var expected = new (string Method, IlPathScan.Rule Rule, string Reason)[]
        {
            (nameof(PoaEvictionPathControls.EarlyReturnForTeams), writeRule, "completes without the eviction the POA write owes"),
            (nameof(PoaEvictionPathControls.ConditionalEviction), writeRule, "completes without the eviction the POA write owes"),
            (nameof(PoaEvictionPathControls.EvictsBeforeWriting), writeRule, "completes without the eviction the POA write owes"),
            (nameof(PoaEvictionPathControls.SwallowsTheFailureAndReturns), writeRule, "completes without the eviction the POA write owes"),
            (nameof(PoaEvictionPathControls.WriteNotAwaited), writeRule, "the eviction runs before the POA write's task has completed"),
            (nameof(PoaEvictionPathControls.EvictionNotAwaited), writeRule, "completes without awaiting the eviction"),
            (nameof(PoaEvictionPathControls.EvictsAnotherRecord), writeRule, "the eviction is given (entitySetName, ?) where the POA write was given (entitySetName, recordId)"),
            (nameof(PoaEvictionPathControls.ReassignsTheRecordFirst), writeRule, "the eviction is given (entitySetName, ?) where the POA write was given (entitySetName, recordId)"),
            (nameof(PoaEvictionPathControls.PassThrough), writeRule, "completes with the POA write's task neither awaited nor followed by the eviction"),
            (nameof(PoaEvictionPathControls.TwoWrites), writeRule, "a second call to the POA write while the eviction of the first is still owed"),
            (nameof(PoaEvictionPathControls.HelperSkipsAnEntitySet), helperRule, "completes without the invalidation the helper owes"),
            (nameof(PoaEvictionPathControls.HelperNotAwaited), helperRule, "completes without awaiting the invalidation"),
            (nameof(PoaEvictionPathControls.HelperOtherRecord), helperRule, "the invalidation is given (entitySetName, ?) where the helper was given (entitySetName, recordId)"),
        };

        foreach (var (method, rule, reason) in expected)
        {
            var violations = Violations(method, rule);
            Assert.True(
                violations.Any(v => v.Contains(reason, StringComparison.Ordinal)),
                $"{method}: expected a violation containing \"{reason}\"; got: {string.Join("; ", violations)}");
        }
    }

    /// <summary>Negative and positive controls for C6 (metadata) and T5 (configuration).</summary>
    [Fact(DisplayName = "Task 132: the metadata and configuration detectors flag every carrier and pass other words")]
    public void MetadataAndConfigurationDetectors_FlagEveryCarrier_AndPassOtherWords()
    {
        var control = typeof(PoaBypassControl_Metadata);

        // Constant data lives on the assembly's <PrivateImplementationDetails>, not on the type that uses it.
        var constantData = control.Assembly.GetTypes().Where(t => t.Name == "<PrivateImplementationDetails>").SelectMany(IlCallScan.WithNested);
        var found = CompiledPoaMetadata(IlCallScan.WithNested(control).Concat(constantData), Array.Empty<Assembly>());

        Assert.Equal(
            new[]
            {
                $"{typeof(PoaBypassControl_Metadata.PoaAction).FullName}.GrantAccess: enum value named \"GrantAccess\"",
                $"{control.FullName}.Attributed: [DescriptionAttribute(\"ModifyAccess\")]",
                $"{control.FullName}.CharArrayAction: constant data (a UTF-8 literal or a byte / char array initializer) holding a POA action",
                $"{control.FullName}.ConstAction: const \"RevokeAccess\"",
                $"{control.FullName}.DefaultValue(method): default \"GrantAccessAsync\"",
                $"{control.FullName}.Parameter(modifyAccess): parameter named \"modifyAccess\"",
                $"{control.FullName}.RevokeAccess: member named \"RevokeAccess\"",
                $"{control.FullName}.get_Utf8Action: constant data (a UTF-8 literal or a byte / char array initializer) holding a POA action",
            },
            found);

        Assert.True(ConfigurationNamesAPoaAction("{ \"Sharing\": { \"Action\": \"grantaccess\" } }"));
        Assert.True(ConfigurationNamesAPoaAction("param poaWrite string = 'RevokeAccessAsync'"));
        Assert.False(
            ConfigurationNamesAPoaAction("{ \"Route\": \"/api/external/GrantAccessRequest\", \"Handler\": \"ModifyAccessible\" }"),
            "other words are not POA actions");
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

        // T6 — UnsafeAccessor in source, however it is spelled: task 132 verifier seed U verbatim, the fully qualified
        // attribute, a using alias (the alias's TARGET names it), a global using in an MSBuild file, and the kind enum an
        // aliased attribute still needs — but not a whole-line or doc comment that only mentions it.
        Assert.True(NamesUnsafeAccessor(
            "[UnsafeAccessor(UnsafeAccessorKind.Method)] private static extern Task RevokeAccessAsync(DataverseWebApiService client, string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct);"));
        Assert.True(NamesUnsafeAccessor("[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field)]"));
        Assert.True(NamesUnsafeAccessor("global using Fast = System.Runtime.CompilerServices.UnsafeAccessorAttribute;"));
        Assert.True(NamesUnsafeAccessor("<Using Include=\"System.Runtime.CompilerServices.UnsafeAccessorAttribute\" Alias=\"Fast\" />"));
        Assert.True(NamesUnsafeAccessor("var x = \"https://h\"; [Fast((UnsafeAccessorKind)1)] static extern void M();"),
            "a // inside a string on the line does not hide the rest of it");
        Assert.False(NamesUnsafeAccessor("/// no [UnsafeAccessor] here\n    // nor UnsafeAccessorKind\nstatic void M() { }"));
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
