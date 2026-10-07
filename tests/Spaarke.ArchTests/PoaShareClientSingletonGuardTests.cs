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
    // Task 132 — the share-write eviction, and the guards kept behind it as defence in depth
    // ---------------------------------------------------------------------------------------------
    // A POA grant / rights change / revoke changes who can read a record exactly as an owner change does, so the access
    // caches must be evicted after it. WHERE THE EVICTION LIVES (main-session round 55): in the write itself.
    // DataverseWebApiService.GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync send through one private method that,
    // on every path, notifies the client's IRecordShareWriteObserver — in the BFF, IMembershipCacheInvalidator, whose
    // default member runs InvalidateRecordShareChangeAsync. So every call of those three methods evicts, whoever makes it
    // and however (directly, through the seam, a delegate, reflection, a late binder, an expression tree); the behaviour
    // tests prove it (AccessCacheInvalidationTests.Shares: every write x every principal kind x each outcome, cancellation,
    // an observer that throws, exactly one eviction per write and after it, the BFF's own composition, and the verifier's
    // seeds V and M — a late-bound call and a token-resolved call run by a LINQ provider).
    //
    // WHAT THE RULES BELOW CHECK — exactly these, nothing beyond them. Eviction does not depend on C1, C2, T1 or the
    // per-writer rule (they keep task 060's single seam); C3 checks the client's notification structurally; C4-C6, T2-T5
    // reject a POA write that would go around DataverseWebApiService when its action is named by a constant, metadata,
    // constant data or configuration; C7 and C8 / T6 ban the APIs listed.
    //
    // COMPILED — an IL scan (IlCallScan) of every assembly the BFF runs from src/ and every src/ assembly that can name
    // DataverseWebApiService. The set is derived from the csproj graph, and each member must load
    // (EveryBffOrClientReachingProjectIsScanned), so a new project cannot fall outside it unnoticed. C8 alone scans more:
    // every src/server project's compiled assembly.
    //   C1. Task 060's single seam: no reference to the concrete client's GrantAccessAsync / ModifyAccessAsync /
    //       RevokeAccessAsync from ANY type but DataverseRecordShareService (the client included), through any receiver
    //       expression — a field, `sp.GetRequiredService<DataverseWebApiService>()` (seed S5), a cast, an indexer — inside
    //       a lambda, local function or async method, as a method group, or as an expression tree's method handle
    //       (EveryCompiledReferenceToTheClientsPoaWritesIsTheSeams).
    //   C2. The client methods that load a POA action name are exactly those three, so C1 knows every POA write the
    //       client has by name (TheClientsPoaWritesAreExactlyTheThreeTheGuardNames).
    //   C3. The client's notification, structurally. (a) The client methods the three writes reach (transitively, through
    //       calls to the client's own methods) that make an HTTP send are its share-write senders; every path through
    //       each (IlPathScan: the async state machine, its exceptions, suspensions and resumptions, every finally) that
    //       makes the send awaits it and then calls the notification with the method's own entitySetName and recordId,
    //       and awaits that, before the method completes — returned, thrown or cancelled
    //       (EveryPathThroughEachClientShareWriteSenderNotifiesThatRecord; seed N1's shape, a branch that sends with no
    //       notification, is a path this rejects). (b) What counts as the notification is found in the IL, not named: the
    //       observer's OnRecordShareWrittenAsync itself, or a client method a sender calls that, on every path, calls it
    //       with its own entitySetName and recordId and awaits it — and one must exist
    //       (TheClientsNotificationCallsTheObserverForThatRecordOnEveryPath). What the observer then evicts is the
    //       behaviour tests'.
    //   C4. A POA action name loaded as a string constant anywhere but the client — an SDK OrganizationRequest by name, a
    //       hand-built POST or $batch, a name the compiler folded from constant pieces, in any letter case — and a POA write
    //       method's name loaded as a constant (reflection, nameof, a `dynamic` call's binder name)
    //       (NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite).
    //   C5. The SDK's GrantAccessRequest / ModifyAccessRequest / RevokeAccessRequest, however they are imported (a global
    //       using defeats the text rule T2) (NoCompiledCodeUsesTheSdkPoaMessages).
    //   C6. A POA action name carried by METADATA rather than a load — a type, member, enum value or parameter NAMED as one
    //       (`enum PoaAction { GrantAccess }` + `action.ToString()`), and a POA action or write name in a const field, a
    //       default parameter value, a custom attribute argument, an embedded resource, or constant DATA — a UTF-8 literal
    //       (`"…"u8`) or a byte / char array initializer, an RVA field's bytes (NoCompiledMetadataOutsideTheClientNamesAPoaAction).
    //   C7. No reference to the reflection APIs ReflectiveMethodAccess LISTS — and only those: the method and member
    //       lookups on Type / TypeInfo / IReflect and their extension spellings, Module.ResolveMethod / ResolveMember /
    //       GetMethod(s), property and event accessor-method lookups, the name-based Expression.Call,
    //       RuntimeMethodHandle.FromIntPtr / GetFunctionPointer, MethodBase.Invoke, InvokeMember, Delegate /
    //       MethodInfo.CreateDelegate, MethodInvoker / ConstructorInvoker, Marshal.GetDelegateForFunctionPointer,
    //       LambdaExpression.Compile / CompileToMethod, the C# run-time binder (`dynamic`), System.Reflection.Emit, the
    //       assembly loaders and Activator's by-assembly-name factories (NoCompiledCodeReferencesTheListedReflectionApis).
    //       It does NOT ban choosing or invoking a method by reflection in general: a late binder (Microsoft.VisualBasic
    //       Interaction.CallByName, seed V), a method resolved from a handle or metadata token
    //       (ModuleHandle.ResolveMethodHandle + MethodBase.GetMethodFromHandle) run by a LINQ provider that compiles the
    //       tree internally (seed M), and other APIs not on the list pass it. Such a call to the three share writes still
    //       evicts (the client's own notification; behaviour tests V and M).
    //   C8. No [UnsafeAccessor] / [UnsafeAccessorType] in the custom-attribute table of ANY src/server project's compiled
    //       assembly — whatever the source spelled (an alias, a Unicode escape), generated code included
    //       (NoCompiledSrcServerAssemblyCarriesAnUnsafeAccessor; round 55 item 3, after verifier seed U5).
    // TEXT — every src/server .cs file, compiled into the BFF or not:
    //   T1. Task 060's single seam: a `.XAccessAsync(` call whose receiver is not an identifier the file declares ONLY as
    //       IDataverseRecordShareService: an expression receiver (S5), a `var`, `dynamic` or undeclared name, a name the
    //       file also declares as another type (seed S6). Conservative — it may flag a legal call; C1 is the precise rule.
    //       (EveryPoaShareWriteGoesThroughTheSingleSeam; per named writer: EachNamedShareWriterWritesOnlyThroughTheSeam)
    //   T2. The SDK messages (NoServerFileSendsSdkPoaMessages). T3. A second POA payload (task 060's
    //       PoaActionPayloadsAreBuiltInExactlyOnePlace, above). T4. A POA write method named as a string
    //       (NoServerFileNamesTheClientsPoaWritesAsAString).
    //   T5. A POA action or write name in the configuration the BFF is deployed with — src/server configuration files and
    //       the Bicep / ARM under infra/ and infrastructure/bicep/ (NoDeployedConfigurationNamesAPoaAction).
    //   T6. The text `UnsafeAccessor` in any .cs / .csproj / .props / .targets file under src/server, or in the
    //       Directory.Build files above it, outside a whole-line comment (NoServerSourceNamesUnsafeAccessor). It reads the
    //       text as written: a spelling text does not show (a Unicode escape in the identifier) is C8's to catch.
    //
    // NOT ENFORCED — the build proves nothing about these (task 132 notes, "Known limits"): a POA write that does not go
    // through the three share writes — a raw HTTP call whose action URL exists only at run time, including one assembled
    // from the client's private members reached by reflection (its generic POST helper, request builder or HttpClient), or
    // code that replaces the client's observer by reflection; native code; code outside the scan set for C1-C7. POA writes
    // made OUTSIDE the BFF (MDA sharing, flows, operator scripts) are bounded by the caches' TTLs (caching-architecture.md,
    // owner R3/R4). Every rule above has a control that was seen to fail.
    // =============================================================================================

    /// <summary>The seam (task 060) — the one file allowed to call the concrete client's POA writes.</summary>
    private const string SingleSeam = "src/server/api/Sprk.Bff.Api/Services/Access/IDataverseRecordShareService.cs";

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
            + "so a POA write in it that goes around the seam or the client (task 060 / 132) would pass unseen. Add a "
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

    [Fact(DisplayName = "Task 060 / 132: only the seam references the concrete client's POA writes (compiled)")]
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
            "a compiled call to DataverseWebApiService's POA writes outside DataverseRecordShareService bypasses task 060's "
            + "single POA seam — the one entry point every writer injects and every writer's test substitutes (ADR-010). "
            + "(The access-cache eviction is the client's own and still runs — task 132 round 55.) Inject "
            + "IDataverseRecordShareService."
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
            "the client methods that name a POA action must be exactly GrantAccessAsync / ModifyAccessAsync / "
            + "RevokeAccessAsync: the single-seam rule above recognises a client POA write BY THOSE NAMES, so a fourth would "
            + "be one any type could call around the seam (task 060). Send it through the client's notifying share-write "
            + "sender (task 132 round 55), expose it through IDataverseRecordShareService and add its name to "
            + "PoaWriteMethodNames — a review decision."
            + $"{Environment.NewLine}  found: {string.Join(", ", senders)}");
    }

    // ── C3. the client's share-write senders: every path that sends notifies the observer for that record ──────

    /// <summary>The key both a send and its notification carry: the record whose caches the write stales.</summary>
    private static readonly string[] ShareKey = { "entitySetName", "recordId" };

    /// <summary>An HTTP send: <c>Send</c> / <c>SendAsync</c> on <see cref="HttpMessageInvoker"/> (and so <see cref="HttpClient"/>).</summary>
    internal static bool IsAnHttpSend(MethodBase target) =>
        target.Name is "Send" or "SendAsync"
        && target.DeclaringType is { } declaring
        && typeof(HttpMessageInvoker).IsAssignableFrom(declaring);

    /// <summary>The observer call the notification must make: <see cref="IRecordShareWriteObserver.OnRecordShareWrittenAsync"/>.</summary>
    internal static bool IsTheObserverCall(MethodBase target) =>
        target.DeclaringType == typeof(IRecordShareWriteObserver)
        && target.Name == nameof(IRecordShareWriteObserver.OnRecordShareWrittenAsync);

    /// <summary>
    /// The client's public POA writes, by the three names C1 / C2 pin (compile-checked through <c>nameof</c>) — EVERY overload
    /// of each: <c>RevokeAccessAsync</c> has two (app-only, and the one that revokes the record owner's own share as the
    /// owner — unified-access-control-r2, 0x80040223), and each must reach a notifying sender.
    /// </summary>
    private static IReadOnlyList<MethodInfo> ClientPoaWrites() =>
        PoaWriteMethodNames
            .SelectMany(name =>
            {
                var overloads = typeof(DataverseWebApiService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == name)
                    .ToList();
                return overloads.Count > 0
                    ? overloads
                    : throw new InvalidOperationException($"DataverseWebApiService.{name} is not a public instance method.");
            })
            .ToList();

    /// <summary>
    /// The client's share-write SENDERS, found in the IL rather than named: every method of
    /// <see cref="DataverseWebApiService"/> the three POA writes reach — themselves included, then transitively through calls
    /// to the client's own methods — whose compiled body makes an HTTP send (<see cref="IsAnHttpSend"/>). A helper that only
    /// builds the request, or a token, is reached but sends nothing, so it is not one.
    /// </summary>
    internal static IReadOnlyList<MethodInfo> ClientShareWriteSenders()
    {
        var references = IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DataverseWebApiService)))
            .Select(r => (Source: IlCallScan.SourceMethod(r.Caller, r.CallerMethod), r.Target))
            .Where(r => r.Source is not null)
            .ToList();

        var reached = new List<MethodBase>();
        var pending = new Stack<MethodBase>(ClientPoaWrites());
        while (pending.Count > 0)
        {
            var method = pending.Pop();
            if (reached.Any(m => m.HasSameMetadataDefinitionAs(method)))
            {
                continue;
            }

            reached.Add(method);
            foreach (var callee in references
                         .Where(r => r.Source!.HasSameMetadataDefinitionAs(method)
                                     && r.Target.DeclaringType == typeof(DataverseWebApiService))
                         .Select(r => r.Target))
            {
                pending.Push(callee);
            }
        }

        return reached
            .OfType<MethodInfo>()
            .Where(m => references.Any(r => r.Source!.HasSameMetadataDefinitionAs(m) && IsAnHttpSend(r.Target)))
            .ToList();
    }

    /// <summary>
    /// The client's notification, found in the IL rather than named: every method declared on the client that a sender
    /// calls (the senders excepted), with what the every-path analysis found in it. A renamed or replaced helper is
    /// followed; a client method that does not call the observer for its own record on every path — a logging helper, or a
    /// notification with a path that skips the observer — does not count as the notification.
    /// </summary>
    internal static IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> ClientNotificationCandidates(IReadOnlyList<MethodInfo> senders) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DataverseWebApiService)))
            .Where(r => IlCallScan.SourceMethod(r.Caller, r.CallerMethod) is { } source
                        && senders.Any(s => s.HasSameMetadataDefinitionAs(source)))
            .Select(r => r.Target)
            .OfType<MethodInfo>()
            .Where(t => t.DeclaringType == typeof(DataverseWebApiService) && !senders.Any(s => s.HasSameMetadataDefinitionAs(t)))
            .DistinctBy(t => t.MetadataToken)
            .Select(t => (t, ShareKey.All(k => t.GetParameters().Any(p => p.Name == k))
                ? IlPathScan.Analyse(t, NotificationRule)
                : new IlPathScan.Result(new[] { "takes no entitySetName / recordId to notify for" }, Array.Empty<int>(), Array.Empty<int>())))
            .ToList();

    /// <summary>Whether <paramref name="target"/> is the observer call itself, or one of <paramref name="candidates"/> that makes it on every path.</summary>
    internal static bool IsANotification(IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> candidates, MethodBase target) =>
        IsTheObserverCall(target)
        || candidates.Any(c => c.Result.Violations.Count == 0
                               && c.Result.DischargingCalls.Count > 0
                               && c.Method.HasSameMetadataDefinitionAs(target));

    /// <summary>C3 (a): an HTTP send opens the obligation, keyed by the sender's own record; a notification of that record discharges it.</summary>
    internal static IlPathScan.Rule SenderRule(Func<MethodBase, bool> isNotification) => new()
    {
        Opens = IsAnHttpSend,
        Discharges = isNotification,
        KeyParameters = ShareKey,
        OpeningKeysFromSource = true,
        Opening = "the share write's send",
        Discharge = "the notification",
    };

    /// <summary>C3 (b): a client method counts as the notification when it owes, from entry, the observer call for its own record — and makes it on every path.</summary>
    internal static readonly IlPathScan.Rule NotificationRule = new()
    {
        Opens = _ => false,
        Discharges = IsTheObserverCall,
        KeyParameters = ShareKey,
        OwedAtEntry = true,
        Opening = "the helper",
        Discharge = "the observer call",
    };

    private static string Signature(MethodBase method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    [Fact(DisplayName = "Task 132: every path through each of the client's share-write senders notifies the observer for that record")]
    public void EveryPathThroughEachClientShareWriteSenderNotifiesThatRecord()
    {
        var senders = ClientShareWriteSenders();
        Assert.True(senders.Count > 0, "precondition: the three POA writes reach a client method that makes the HTTP send");

        var candidates = ClientNotificationCandidates(senders);
        var violations = new List<string>();
        foreach (var sender in senders)
        {
            if (!ShareKey.All(k => sender.GetParameters().Any(p => p.Name == k)))
            {
                violations.Add($"{Signature(sender)}: makes a share write's send but takes no entitySetName / recordId to notify for");
                continue;
            }

            var result = IlPathScan.Analyse(sender, SenderRule(target => IsANotification(candidates, target)));
            Assert.True(result.OpeningCalls.Count > 0, $"precondition: the path analysis reaches {Signature(sender)}'s send");
            violations.AddRange(result.Violations.Select(v => $"{Signature(sender)}: {v}"));
        }

        Assert.True(
            violations.Count == 0,
            "a path through the client's share-write sender makes the send and is not followed — after the send completes, "
            + "with the same entitySetName and recordId, awaited — by the observer notification, before the method returns, "
            + "throws or is cancelled (task 132 round 55). On that path the access caches stay stale for their TTLs: an "
            + "unshare that keeps access. Keep the send inside the try whose finally notifies."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", violations)}"
            + $"{Environment.NewLine}  the client methods the senders call: {Describe(candidates)}");
    }

    [Fact(DisplayName = "Task 132: the client's notification calls the observer for that record on every path and awaits it")]
    public void TheClientsNotificationCallsTheObserverForThatRecordOnEveryPath()
    {
        var candidates = ClientNotificationCandidates(ClientShareWriteSenders());

        Assert.True(
            candidates.Any(c => c.Result.Violations.Count == 0 && c.Result.DischargingCalls.Count > 0),
            "none of the client methods the share-write senders call notifies, on every path, the observer of the record it is "
            + "given — IRecordShareWriteObserver.OnRecordShareWrittenAsync with its own entitySetName and recordId, awaited "
            + "(task 132 round 55): share writes would leave the record's access caches stale."
            + $"{Environment.NewLine}  {Describe(candidates)}");
    }

    private static string Describe(IReadOnlyList<(MethodInfo Method, IlPathScan.Result Result)> candidates) =>
        candidates.Count == 0
            ? "(none)"
            : string.Join(
                Environment.NewLine + "  ",
                candidates.Select(c => $"{Signature(c.Method)}: "
                                       + (c.Result.Violations.Count == 0 && c.Result.DischargingCalls.Count > 0
                                           ? "notifies on every path"
                                           : string.Join("; ", c.Result.Violations.DefaultIfEmpty("never notifies")))));

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
            + "outside the client is how a write reaches Dataverse without DataverseWebApiService — an SDK OrganizationRequest "
            + "by name, a hand-built POST — and so without its share-write notification and the access-cache eviction "
            + "behind it (task 132). Call "
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
            "an SDK POA message bypasses DataverseWebApiService — and with it the share-write notification and access-cache "
            + "eviction (task 132) and the payload "
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
            + "ToString(), reflection or a resource read with no constant loaded — around DataverseWebApiService and its "
            + "share-write notification (task 132). Call IDataverseRecordShareService; rename or reword the rest."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── C7. no reference to the reflection APIs ReflectiveMethodAccess lists ──────────────────────
    //
    // C1 reads the method tokens the COMPILER resolved; a method the code chooses at RUN time has none. Task 132 f1-v1c-v1
    // verifier seed R found the client's RevokeAccessAsync by its signature (`typeof(DataverseWebApiService).GetMethods()
    // .First(x => <4 parameters, the third a DataversePrincipalRef, returns Task>)`) and invoked it. Owner round 48 (b)
    // banned the reflection APIs that seed and its siblings use — on ANY type, so where the Type came from does not matter.
    // This rule bans exactly the APIs listed below and NOTHING more: it is not "no method chosen or invoked by reflection".
    // A late binder (seed V: Microsoft.VisualBasic Interaction.CallByName) and a method resolved from a handle or metadata
    // token and run by a LINQ provider (seed M: ModuleHandle.ResolveMethodHandle + MethodBase.GetMethodFromHandle +
    // EnumerableQuery) pass it, as do other APIs not on the list. Since round 55 that no longer matters for the eviction —
    // a call of the client's share writes notifies however it was chosen (AccessCacheInvalidationTests.Shares runs seeds V
    // and M) — so the rule is defence in depth, and its gaps are task 132's recorded known limits (owner round 56, class d).

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
    /// <c>null</c>. The list is exactly what is banned — other ways to choose or invoke a method (a late binder; a method
    /// resolved from a handle or token, run by a LINQ provider) are not on it.
    /// Banned: (1) CHOOSING a method — a method or member lookup on <see cref="Type"/> / <c>TypeInfo</c> /
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

    [Fact(DisplayName = "Task 132: no compiled code references the reflection APIs the guard lists (C7)")]
    public void NoCompiledCodeReferencesTheListedReflectionApis()
    {
        var offenders = CompiledReflectiveMethodAccess(ScannedReflectiveReferences.Value);

        Assert.True(
            offenders.Count == 0,
            "a reflection API on C7's list — a method or member lookup, a reflective invocation, emitted IL or an assembly "
            + "loaded at run time — has no method token for the compiled rules to read (task 132 verifier seed R reached "
            + "DataverseWebApiService.RevokeAccessAsync by its signature). Owner round 48 (b) bans these APIs in every assembly "
            + "the BFF runs, on any type. Call the method directly; share through IDataverseRecordShareService. A genuine need "
            + "for one is a review decision: name the site and the type it reflects over here, and that type must not be a "
            + "Dataverse service type."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    // ── C8 / T6. no [UnsafeAccessor] anywhere in src/server ───────────────────────────────────────
    //
    // Task 132 f1-v1c-v1 verifier seed U: `[UnsafeAccessor(UnsafeAccessorKind.Method)] private static extern Task
    // RevokeAccessAsync(DataverseWebApiService client, …)` called unqualified IS the client's write — a compiled route whose
    // call target is declared on the probe type (C1 misses it), with no string and no reflection. Spaarke has no legitimate
    // use for UnsafeAccessor: it reaches private members regardless of visibility. Owner round 48 (a): none anywhere in
    // src/server. Round 55 item 3 (after verifier seed U5, a Unicode-escaped attribute in the L2 Worker that the text rule
    // could not read): C8 reads the COMPILED custom-attribute table of every src/server project's assembly, so the source
    // spelling does not matter. T6 still reads the text, as written.

    /// <summary><c>[UnsafeAccessor]</c> and its companions (<c>[UnsafeAccessorType]</c>, .NET 10), by namespace and name.</summary>
    internal static bool IsUnsafeAccessorAttribute(string? attributeNamespace, string attributeName) =>
        attributeNamespace == "System.Runtime.CompilerServices"
        && attributeName.StartsWith("UnsafeAccessor", StringComparison.Ordinal);

    /// <summary>
    /// Every custom attribute in the compiled assembly at <paramref name="path"/> whose type is an UnsafeAccessor attribute,
    /// as <c>"{where}: [{attribute}]"</c>. Read from the assembly's custom-attribute TABLE (System.Reflection.Metadata, no
    /// load): every row is seen — on a type, member, parameter, return value, generic parameter, the assembly or a module —
    /// whatever the source spelled (an alias, a Unicode escape) and generated code included, without resolving the
    /// assembly's dependencies.
    /// </summary>
    internal static IReadOnlyList<string> UnsafeAccessorsIn(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        var found = new List<string>();
        foreach (var handle in md.CustomAttributes)
        {
            var attribute = md.GetCustomAttribute(handle);
            var (ns, name) = AttributeTypeName(md, attribute.Constructor);
            if (name is not null && IsUnsafeAccessorAttribute(ns, name))
            {
                found.Add($"{MetadataOwner(md, attribute.Parent)}: [{name}]");
            }
        }

        return found.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>C8's findings for one control type (and its nested types), read from this test assembly's own DLL.</summary>
    internal static IReadOnlyList<string> UnsafeAccessorsOn(Type control) =>
        UnsafeAccessorsIn(typeof(PoaShareClientSingletonGuardTests).Assembly.Location)
            .Where(found => found.StartsWith(control.FullName + ".", StringComparison.Ordinal)
                            || found.StartsWith(control.FullName + "+", StringComparison.Ordinal)
                            || found.StartsWith(control.FullName + "<", StringComparison.Ordinal)
                            || found.StartsWith(control.FullName + ":", StringComparison.Ordinal))
            .ToList();

    /// <summary>The namespace and name of the type whose constructor an attribute row names (null name: a generic attribute's type spec).</summary>
    private static (string? Namespace, string? Name) AttributeTypeName(
        System.Reflection.Metadata.MetadataReader md, System.Reflection.Metadata.EntityHandle constructor)
    {
        var type = constructor.Kind switch
        {
            System.Reflection.Metadata.HandleKind.MemberReference =>
                md.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)constructor).Parent,
            System.Reflection.Metadata.HandleKind.MethodDefinition =>
                md.GetMethodDefinition((System.Reflection.Metadata.MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => throw new InvalidOperationException($"An attribute constructor of kind {constructor.Kind} — the scan would under-report."),
        };

        switch (type.Kind)
        {
            case System.Reflection.Metadata.HandleKind.TypeReference:
                var reference = md.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)type);
                return (md.GetString(reference.Namespace), md.GetString(reference.Name));
            case System.Reflection.Metadata.HandleKind.TypeDefinition:
                var definition = md.GetTypeDefinition((System.Reflection.Metadata.TypeDefinitionHandle)type);
                return (md.GetString(definition.Namespace), md.GetString(definition.Name));
            default:
                return (null, null); // a TypeSpecification: a generic attribute type, which no UnsafeAccessor attribute is
        }
    }

    /// <summary>What an attribute row is attached to, in reflection's spelling (<c>Ns.Type.Member(parameter)</c>).</summary>
    private static string MetadataOwner(System.Reflection.Metadata.MetadataReader md, System.Reflection.Metadata.EntityHandle parent)
    {
        string TypeName(System.Reflection.Metadata.TypeDefinitionHandle handle)
        {
            var type = md.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            return declaring.IsNil
                ? (md.GetString(type.Namespace) is { Length: > 0 } ns ? $"{ns}.{md.GetString(type.Name)}" : md.GetString(type.Name))
                : $"{TypeName(declaring)}+{md.GetString(type.Name)}";
        }

        string MethodName(System.Reflection.Metadata.MethodDefinitionHandle handle)
        {
            var method = md.GetMethodDefinition(handle);
            return $"{TypeName(method.GetDeclaringType())}.{md.GetString(method.Name)}";
        }

        switch (parent.Kind)
        {
            case System.Reflection.Metadata.HandleKind.TypeDefinition:
                return TypeName((System.Reflection.Metadata.TypeDefinitionHandle)parent);
            case System.Reflection.Metadata.HandleKind.MethodDefinition:
                return MethodName((System.Reflection.Metadata.MethodDefinitionHandle)parent);
            case System.Reflection.Metadata.HandleKind.FieldDefinition:
                var field = md.GetFieldDefinition((System.Reflection.Metadata.FieldDefinitionHandle)parent);
                return $"{TypeName(field.GetDeclaringType())}.{md.GetString(field.Name)}";
            case System.Reflection.Metadata.HandleKind.Parameter:
                var parameterHandle = (System.Reflection.Metadata.ParameterHandle)parent;
                var parameter = md.GetParameter(parameterHandle);
                var owner = md.MethodDefinitions.First(m => md.GetMethodDefinition(m).GetParameters().Contains(parameterHandle));
                return parameter.SequenceNumber == 0
                    ? $"{MethodName(owner)} (return)"
                    : $"{MethodName(owner)}({md.GetString(parameter.Name)})";
            case System.Reflection.Metadata.HandleKind.PropertyDefinition:
                var propertyHandle = (System.Reflection.Metadata.PropertyDefinitionHandle)parent;
                var propertyType = md.TypeDefinitions.First(t => md.GetTypeDefinition(t).GetProperties().Contains(propertyHandle));
                return $"{TypeName(propertyType)}.{md.GetString(md.GetPropertyDefinition(propertyHandle).Name)}";
            case System.Reflection.Metadata.HandleKind.EventDefinition:
                var eventHandle = (System.Reflection.Metadata.EventDefinitionHandle)parent;
                var eventType = md.TypeDefinitions.First(t => md.GetTypeDefinition(t).GetEvents().Contains(eventHandle));
                return $"{TypeName(eventType)}.{md.GetString(md.GetEventDefinition(eventHandle).Name)}";
            case System.Reflection.Metadata.HandleKind.GenericParameter:
                var generic = md.GetGenericParameter((System.Reflection.Metadata.GenericParameterHandle)parent);
                var genericOwner = generic.Parent.Kind == System.Reflection.Metadata.HandleKind.TypeDefinition
                    ? TypeName((System.Reflection.Metadata.TypeDefinitionHandle)generic.Parent)
                    : MethodName((System.Reflection.Metadata.MethodDefinitionHandle)generic.Parent);
                return $"{genericOwner}<{md.GetString(generic.Name)}>";
            case System.Reflection.Metadata.HandleKind.AssemblyDefinition:
                return "(assembly)";
            case System.Reflection.Metadata.HandleKind.ModuleDefinition:
                return "(module)";
            default:
                return $"({parent.Kind})";
        }
    }

    /// <summary>
    /// The compiled assembly of EVERY project under <c>src/server</c> (round 55 item 3), as (csproj path, the DLL or null
    /// when it is not built): the newest <c>{its assembly name}.dll</c> under its own <c>bin/{this test's
    /// configuration}/{its target framework}/</c>, a runtime-identifier folder included (the Web SDK projects build to
    /// <c>linux-x64/</c>), reference assemblies and publish output excluded. The projects the ArchTests do not reference
    /// are built by the <c>BuildServerProjectsForCompiledScans</c> target in <c>Spaarke.ArchTests.csproj</c>.
    /// </summary>
    internal static IReadOnlyList<(string Project, string? Assembly)> ServerProjectAssemblies()
    {
        var sep = Path.DirectorySeparatorChar;
        var configuration = typeof(PoaShareClientSingletonGuardTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

        return Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "server"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}node_modules{sep}", StringComparison.Ordinal))
            .Select(project =>
            {
                var xml = XDocument.Load(project);
                string? Property(string name) =>
                    xml.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

                var output = Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, Property("TargetFramework") ?? "net10.0");
                var dll = Directory.Exists(output)
                    ? Directory.EnumerateFiles(output, (Property("AssemblyName") ?? Path.GetFileNameWithoutExtension(project)) + ".dll", SearchOption.AllDirectories)
                        .Where(f => !Path.GetRelativePath(output, f).Split(sep).Any(part => part is "ref" or "refint" or "publish"))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault()
                    : null;
                return (Project: RelativePath(project), Assembly: dll);
            })
            .OrderBy(p => p.Project, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "Task 132: no src/server project's compiled assembly carries an UnsafeAccessor (C8)")]
    public void NoCompiledSrcServerAssemblyCarriesAnUnsafeAccessor()
    {
        var assemblies = ServerProjectAssemblies();
        Assert.True(
            new[]
            {
                "src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj",
                "src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj",
                "src/server/services/Sprk.Provisioning.ControlPlane.Worker/Sprk.Provisioning.ControlPlane.Worker.csproj",
            }.All(p => assemblies.Any(a => a.Project == p)),
            "precondition: the walk finds the BFF, the client's project and a project outside the IL scan set");

        var unbuilt = assemblies.Where(a => a.Assembly is null).Select(a => a.Project).ToList();
        Assert.True(
            unbuilt.Count == 0,
            "a src/server project has no compiled assembly for the UnsafeAccessor scan to read, so an accessor in it would pass "
            + "unseen (round 55 item 3). Build it from the BuildServerProjectsForCompiledScans target in "
            + "tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", unbuilt)}");

        var offenders = assemblies
            .SelectMany(a => UnsafeAccessorsIn(a.Assembly!).Select(found => $"{Path.GetFileName(a.Assembly)}: {found}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "an [UnsafeAccessor] extern reaches a member regardless of its visibility, through a call whose target is declared "
            + "on the accessor's own type — task 132 verifier seed U called DataverseWebApiService.RevokeAccessAsync that way, "
            + "around the seam, with every other rule green. Spaarke has no legitimate use for it (owner round 48 (a)): call the "
            + "member directly, or expose what you need."
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
    /// outside a whole-line <c>//</c> comment (doc comments included). It reads the text as written: a spelling the text does
    /// not show (a Unicode escape in the identifier, task 132 seed U5) is C8's to catch, in the compiled assembly.
    /// Conservative: a block comment, an XML comment or a string that names it is flagged too — reword it.
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
            + "members regardless of visibility. Call the member directly, or expose what you need."
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
            + "can POST around DataverseWebApiService — and so without its share-write notification (task 132). Share through "
            + "IDataverseRecordShareService; do not configure a POA action."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>Negative and positive controls for the compiled detectors (C1, C4, C5, C7, C8).</summary>
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

        // C8 — task 132 f1-v1c-v1 verifier seed U, verbatim: an [UnsafeAccessor] extern that IS the client's write. Every
        // rule before C8 passes it — C1 sees a call to the control's own extern, C4 / C6 see no POA word — which is the
        // finding; C8 flags the attribute in the compiled custom-attribute table however it is used (a method accessor, a
        // field accessor, [UnsafeAccessorType] on a parameter) — read here from THIS assembly's own DLL, as C8 reads every
        // src/server project's.
        var seedU = IlCallScan.WithNested(typeof(PoaBypassControl_UnsafeAccessor)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(seedU)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(seedU)));
        Assert.Empty(CompiledPoaMetadata(seedU, Array.Empty<Assembly>()));
        Assert.Equal(
            new[] { $"{typeof(PoaBypassControl_UnsafeAccessor).FullName}.RevokeAccessAsync: [UnsafeAccessorAttribute]" },
            UnsafeAccessorsOn(typeof(PoaBypassControl_UnsafeAccessor)));
        Assert.Equal(
            new[]
            {
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.ClientOf: [UnsafeAccessorAttribute]",
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.DisposeByTypeName(target): [UnsafeAccessorTypeAttribute]",
                $"{typeof(PoaBypassControl_UnsafeAccessorOtherShapes).FullName}.DisposeByTypeName: [UnsafeAccessorAttribute]",
            },
            UnsafeAccessorsOn(typeof(PoaBypassControl_UnsafeAccessorOtherShapes)));

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
        Assert.Empty(UnsafeAccessorsOn(typeof(PoaReflectionPermittedControl)));

        // The sanctioned shape — every write through the seam's interface — is none of these.
        var sanctioned = IlCallScan.WithNested(typeof(PoaWriteThroughSeamControl)).ToList();
        Assert.Empty(CompiledPoaWriteBypasses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledSdkPoaMessageUses(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(CompiledPoaNameLoads(IlCallScan.StringLoads(sanctioned)));
        Assert.Empty(CompiledPoaMetadata(sanctioned, Array.Empty<Assembly>()));
        Assert.Empty(CompiledReflectiveMethodAccess(IlCallScan.MethodReferences(sanctioned)));
        Assert.Empty(UnsafeAccessorsOn(typeof(PoaWriteThroughSeamControl)));
    }

    /// <summary>
    /// Negative and positive controls for C3's path analysis: each shape below sends and then notifies on every path, or has
    /// one path that does not — task 132 seed N1's shape (a team-only early return) among them.
    /// </summary>
    [Fact(DisplayName = "Task 132: the path analysis flags every send shape that can skip the notification and passes the rest")]
    public void PathAnalysis_FlagsEveryNonNotifyingShape_AndPassesTheNotifyingOnes()
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
        var controls = typeof(PoaShareWriteSenderControls);
        var notify = controls.GetMethod(nameof(PoaShareWriteSenderControls.NotifyAsync), Any)!;
        var observe = controls.GetMethod(nameof(PoaShareWriteSenderControls.ObserveAsync), Any)!;
        var senderRule = SenderRule(target => target.HasSameMetadataDefinitionAs(notify));
        var helperRule = new IlPathScan.Rule
        {
            Opens = _ => false,
            Discharges = target => target.HasSameMetadataDefinitionAs(observe),
            KeyParameters = ShareKey,
            OwedAtEntry = true,
            Opening = "the helper",
            Discharge = "the observer call",
        };

        IReadOnlyList<string> Violations(string method, IlPathScan.Rule rule) =>
            IlPathScan.Analyse(controls.GetMethod(method, Any)!, rule).Violations;

        // Notify on every path: the client's own shape, a caught failure that returns inside the try, a real finally (using).
        foreach (var notifying in new[]
                 {
                     nameof(PoaShareWriteSenderControls.Canonical),
                     nameof(PoaShareWriteSenderControls.CatchAndReturnInsideTheTry),
                     nameof(PoaShareWriteSenderControls.UnderAUsing),
                 })
        {
            var result = IlPathScan.Analyse(controls.GetMethod(notifying, Any)!, senderRule);
            Assert.True(result.Violations.Count == 0, $"{notifying}: {string.Join("; ", result.Violations)}");
            Assert.NotEmpty(result.OpeningCalls);
            Assert.NotEmpty(result.DischargingCalls);
        }

        Assert.Empty(Violations(nameof(PoaShareWriteSenderControls.HelperCanonical), helperRule));

        // A path that sends and does not notify, each named by what it does wrong.
        const string Owes = "completes without the notification the share write's send owes";
        const string OtherRecord = "the notification is given (entitySetName, ?) where the share write's send was given (entitySetName, recordId)";
        var expected = new (string Method, IlPathScan.Rule Rule, string Reason)[]
        {
            (nameof(PoaShareWriteSenderControls.EarlyReturnForTeams), senderRule, Owes),
            (nameof(PoaShareWriteSenderControls.ConditionalNotification), senderRule, Owes),
            (nameof(PoaShareWriteSenderControls.NotifiesBeforeSending), senderRule, Owes),
            (nameof(PoaShareWriteSenderControls.SwallowsTheFailureAndReturns), senderRule, Owes),
            (nameof(PoaShareWriteSenderControls.SendNotAwaited), senderRule, "the notification runs before the share write's send's task has completed"),
            (nameof(PoaShareWriteSenderControls.NotificationNotAwaited), senderRule, "completes without awaiting the notification"),
            (nameof(PoaShareWriteSenderControls.NotifiesAnotherRecord), senderRule, OtherRecord),
            (nameof(PoaShareWriteSenderControls.ReassignsTheRecordFirst), senderRule, OtherRecord),
            (nameof(PoaShareWriteSenderControls.PassThrough), senderRule, "completes with the share write's send's task neither awaited nor followed by the notification"),
            (nameof(PoaShareWriteSenderControls.TwoSends), senderRule, "a second call to the share write's send while the notification of the first is still owed"),
            (nameof(PoaShareWriteSenderControls.HelperSkipsAnEntitySet), helperRule, "completes without the observer call the helper owes"),
            (nameof(PoaShareWriteSenderControls.HelperNotAwaited), helperRule, "completes without awaiting the observer call"),
            (nameof(PoaShareWriteSenderControls.HelperOtherRecord), helperRule, "the observer call is given (entitySetName, ?) where the helper was given (entitySetName, recordId)"),
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

    [Fact(DisplayName = "Task 060 / 132: every POA share write in src/server goes through the single seam")]
    public void EveryPoaShareWriteGoesThroughTheSingleSeam()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Select(file => (Path: RelativePath(file), Text: File.ReadAllText(file)))
            .Where(f => f.Path != SingleSeam && f.Path != CanonicalClient)
            .SelectMany(f => PoaWriteCalls(f.Text)
                .Where(c => !c.ThroughSeam)
                .Select(c => $"{f.Path}: {c.Receiver}.{c.Method}(…)"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "a POA share write that does not provably go through IDataverseRecordShareService bypasses task 060's single "
            + "POA seam, the entry point every writer injects and every writer's test substitutes (the access-cache eviction "
            + "is the client's own since task 132 round 55). Call it on an identifier declared as IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>
    /// The share writers the batch 4 integration named (plus the provisioning file, whose creator share, its restore and
    /// the resume error paths all live there), each still VISIBLE to the rule above — at least one write found, every
    /// one through the seam. A refactor that hid a writer from the detector would otherwise pass the rule vacuously.
    /// </summary>
    [Theory(DisplayName = "Task 060 / 132: each named share-writing path writes POA only through the single seam")]
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
            + "does not go through IDataverseRecordShareService, task 060's single POA seam.");
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
            "an SDK POA message bypasses DataverseWebApiService — and with it the share-write notification and access-cache "
            + "eviction (task 132) and the payload "
            + "consolidation (task 060). Use IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(", ", offenders)}");
    }

    [Fact(DisplayName = "Task 132: no server file names a POA write method as a string (reflection around the seam)")]
    public void NoServerFileNamesTheClientsPoaWritesAsAString()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Select(file => (Path: RelativePath(file), Text: File.ReadAllText(file)))
            .Where(f => f.Path != SingleSeam && f.Path != CanonicalClient && NamesAPoaWriteAsAString(f.Text))
            .Select(f => f.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "naming GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync as a string is how a reflective call reaches "
            + "the concrete client's POA writes without a call the compiled scan can see, around task 060's single seam. "
            + "Call IDataverseRecordShareService."
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
