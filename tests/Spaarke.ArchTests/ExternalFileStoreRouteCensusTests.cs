using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 124 (EB-D-163): the file-store route census. The Procedure Library container is retired: every Procedure
/// document is a Stored File (purpose Procedure source, parent its Procedure Version) in the Finance file store, and a Procedure file
/// is never firm-visible. This guard keeps that true as the code changes: no route that admits an external (CIAM) caller reaches the
/// file store except through the firm file gate, which refuses anything but the invoice's own firm-visible Invoice PDF
/// (<c>ExternalInvoiceSubmissionAuthorizationFilter.IsFirmFile</c>).
///
/// <para><b>Complements, does not replace</b>, <c>ExternalDocumentNarrowingGuardTests.EveryExternalStoredFileSurface_IsTheFirmFileGate</c>
/// (task 093), which reads only the external-plane namespaces. This census reads the WHOLE BFF assembly, so a Stored File reader
/// outside those namespaces is found too.</para>
///
/// <list type="number">
///   <item><b>Census.</b> Every type outside the file store's own that references the file store
///   (<see cref="StoredFileService"/>, <see cref="StoredFileBlobs"/> or <see cref="StoredFileEndpoints"/>) is listed in
///   <see cref="Census"/> with a <see cref="Reach"/> and a written reason. A new caller, and a stale entry, fail. A caller in the
///   external-plane namespaces must be <see cref="Reach.ExternalGate"/>, and only those may be.</item>
///   <item><b>Routes.</b> Read from the REAL BFF booted in-process (<see cref="BootedBff"/>, Development and Production): the handler of
///   every route that admits the Ciam scheme must not be a type classified <see cref="Reach.NotOnAnExternalRoute"/>, and the Ciam
///   routes whose handler method itself references the file store are exactly <see cref="PinnedExternalFileRoutes"/>.</item>
///   <item><b>Closure</b> (review fix round 1 of task 124). The two checks above see only a handler's OWN references. For every
///   handler that admits a Ciam caller, <see cref="ClosureProblems"/> follows its calls through the BFF assembly
///   (<see cref="Follow"/>, at most <see cref="MaxDepth"/> calls deep; a closure still open at the bound FAILS, it is never cut silently)
///   over task 093's call graph (<see cref="ExternalDocumentNarrowingGuardTests.Scan"/>), widened with interface and virtual dispatch
///   (a call to <c>IFoo.Bar</c> reaches every implementer's <c>Bar</c>), a static field read (it runs the owner's static constructor) and
///   closed generic types. It fails when the closure reaches: a member of the file store (<see cref="StoredFileService"/>,
///   <see cref="StoredFileBlobs"/>, the <c>StoredFile*</c> endpoints and filters, <see cref="StoredFileSchema"/>) other than the pinned
///   firm file gate's own (<see cref="GateReach"/>); a member of a type classified <see cref="Reach.NotOnAnExternalRoute"/>; a
///   <see cref="StoredFileSchema"/> static field; a type reference to or generic resolution of any of those; or any string literal that
///   contains <c>sprk_storedfile</c> (this covers <c>sprk_storedfiles</c> and <c>StoredFileSchema.Entity</c> / <c>.EntitySet</c>, which the
///   compiler inlines, and an interpolated OData url the compiler folded). A legitimate reach is listed, with its route and why it exposes
///   no Stored File content or metadata, in <see cref="AllowedReach"/>; that list only shrinks (an entry no longer reached fails).</item>
/// </list>
///
/// <para><b>Known limits — what the guard does NOT see.</b> It is a static scan of compiled IL, not a proof. (1) A table name built at run
/// time (a name taken from the request, concatenated pieces the compiler could not fold, a string read from configuration) is not a
/// literal; the routes that take a table name from the request are governed by the table-classification guards (the lineage registry's
/// HardDeny row for the table), not by this one. (2) A call through a delegate or a function field (a seam such as
/// <c>DocumentNarrower</c>) has no call edge; those seams are pinned by <c>ExternalDocumentNarrowingGuardTests</c>. (3) Reflection and
/// <c>dynamic</c> dispatch. (4) Endpoint filters and middleware are not followed (the firm file gate is an endpoint filter: pinned by
/// 093's guard and by <c>ExternalInvoiceFileRoutesTests</c>); only the handler method's closure is. (5) The graph is by NAME: overloads
/// of one method and every lambda of one method merge, so the scan can over-report (an allow-list entry is then needed) but not
/// under-report on that account. (6) Calls into another assembly (the framework, packages) end the walk; the walk does not look inside
/// a package for a Dataverse query. (7) A node reached only through a type the scan cannot map to an implementer (a generic virtual
/// method, an explicit implementation under a renamed member) is not followed.</para>
///
/// <para><b>Maintenance.</b> When this fails: (a) a NEW caller of the file store — add it to <see cref="Census"/> with the reach that is
/// true of it and a reason. If it is called from an external route, it must go through the firm file gate and be a
/// <see cref="Reach.ExternalGate"/> or <see cref="Reach.ExternalStagingWrite"/> entry; ask the unified-access-control owner. (b) A Ciam
/// route that reaches the file store — do not pin it; route it through the gate.</para>
///
/// <para>Controls: <see cref="Census_MustFireAndMustNotFire"/> and <see cref="Routes_MustFireAndMustNotFire"/> over the
/// <c>FileStoreCensusControl*</c> fixtures (end of file); for the closure <see cref="Closure_MustFire"/>,
/// <see cref="Closure_DepthBound_MustFire"/> and <see cref="Closure_MustNotFire_AndTheAllowListRatchets"/> over the
/// <c>ClosureControl*</c> fixtures.</para>
/// </summary>
[Collection(BootedBffCollection.Name)]
public sealed class ExternalFileStoreRouteCensusTests
{
    private readonly BootedBff _booted;

    public ExternalFileStoreRouteCensusTests(BootedBff booted) => _booted = booted;

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    /// <summary>The file store's own types: a reference to one of them is a use of the file store.</summary>
    private static readonly IReadOnlySet<Type> FileStoreTypes = new HashSet<Type>
    {
        typeof(StoredFileService), typeof(StoredFileBlobs), typeof(StoredFileEndpoints),
    };

    private enum Reach
    {
        /// <summary>The firm file gate and its handler: lists and serves only the invoice's own firm-visible Invoice PDF.</summary>
        ExternalGate,

        /// <summary>Reached from an external route, and only to stage, read back, commit or delete the caller's OWN submission files (an
        /// Invoice PDF or supporting file whose parent is the submission's invoice); it lists and serves no other Stored File.</summary>
        ExternalStagingWrite,

        /// <summary>No route that admits a Ciam token reaches it (workforce routes, jobs and internal services only).</summary>
        NotOnAnExternalRoute,
    }

    private sealed record Entry(Reach Reach, string Reason);

    private const string Gate = "Sprk.Bff.Api.Api.ExternalAccess.ExternalInvoiceSubmissionAuthorizationFilter";
    private const string GateHandlers = "Sprk.Bff.Api.Api.ExternalAccess.ExternalInvoiceSubmissionEndpoints";

    /// <summary>Every type outside the file store that references it, by full name.</summary>
    private static readonly IReadOnlyDictionary<string, Entry> Census = new Dictionary<string, Entry>(StringComparer.Ordinal)
    {
        [Gate] = new(Reach.ExternalGate,
            "DecideInvoiceFilesAsync reads the invoice's firm-visible rows (ReadFirmVisibleRowsForInvoiceAsync), re-checks each with IsFirmFile "
            + "(parent = this invoice, purpose Invoice PDF, firm-visible, no source) and judges it servable. A Procedure source file has purpose "
            + "Procedure source and a Procedure Version parent, so it is refused. ExternalDocumentNarrowingGuardTests pins that the gate reaches "
            + "the record gate, the (a') predicate and the firm read."),
        [GateHandlers] = new(Reach.ExternalGate,
            "GetInvoiceFileContentAsync streams only a file of the gate's set (InvoiceFiles); ExternalDocumentNarrowingGuardTests pins it."),

        // The firm's own submission files. The external POST /{matters|projects|workassignments}/{id}/invoice-submissions routes call
        // InvoiceSubmissionService, which stages the caller's own upload (purpose Invoice PDF or Invoice supporting file, parent the
        // submission's invoice) and commits it; none of it lists, reads or serves any other Stored File.
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService"] = new(Reach.ExternalStagingWrite,
            "Stages (StageAsync), deletes (DeleteStagedAsync) and checks (CanStageAsync) the submission's own files; purposes Invoice PDF and "
            + "Invoice supporting file only, parent the submission's invoice."),
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister"] = new(Reach.ExternalStagingWrite,
            "Reads back the submission's own staged supporting files (parent the submission's invoice) and writes their rows in the commit transaction."),
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmissionCommitGate"] = new(Reach.ExternalStagingWrite,
            "Flips the submission's own staged files Servable, or deletes them, by the file ids the commit staged."),

        // Not reachable from a route that admits a Ciam token (the route check below proves no Ciam route's HANDLER is one of these;
        // the rest are services behind workforce routes, jobs and DI).
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmitInvoiceFromEmailOperation"] = new(Reach.NotOnAnExternalRoute,
            "The email channel's submission (FromEmailSubmissionService, a job); stages the email's attachments as the submission's own files."),
        ["Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceSubmissionProcessingJobHandler"] = new(Reach.NotOnAnExternalRoute,
            "A job: reads the staged submission file for processing (ADR-004 job handler)."),
        ["Sprk.Bff.Api.Services.Jobs.Handlers.BillingRuleDraftJobHandler"] = new(Reach.NotOnAnExternalRoute,
            "A job: reads a Procedure Version's source file to draft rules (internal; task 116)."),
        ["Sprk.Bff.Api.Services.Finance.RateCardImportService"] = new(Reach.NotOnAnExternalRoute,
            "Workforce rate-card import: reads a Procedure Version's workbook (task 116) behind POST /api/finance/...; internal."),
        ["Sprk.Bff.Api.Services.Finance.ApExportService"] = new(Reach.NotOnAnExternalRoute,
            "Workforce AP export: writes and lists the batch's Export files (internal)."),
        ["Sprk.Bff.Api.Services.Finance.ApExportDeliveryService"] = new(Reach.NotOnAnExternalRoute,
            "AP export delivery to the AP system (internal)."),
        ["Sprk.Bff.Api.Api.Finance.ApExportEndpoints"] = new(Reach.NotOnAnExternalRoute,
            "Workforce route handler (/api/finance/ap-exports); not a Ciam route (checked below)."),
        ["Sprk.Bff.Api.Api.Finance.ProcedureEndpoints"] = new(Reach.NotOnAnExternalRoute,
            "Workforce route handler (/api/finance/procedures: the Procedure Version source upload and draft); not a Ciam route (checked below)."),
        ["Sprk.Bff.Api.Api.Finance.RateCardImportEndpoints"] = new(Reach.NotOnAnExternalRoute,
            "Workforce route handler (/api/finance/ratecards import); not a Ciam route (checked below)."),
        ["Sprk.Bff.Api.Api.DataverseDocumentsEndpoints"] = new(Reach.NotOnAnExternalRoute,
            "Workforce document route: calls the static StoredFileService.CountCopiesOfDocumentAsync (a count of Stored File copies of a "
            + "document row, never a file or its bytes); not a Ciam route (checked below)."),
        ["Sprk.Bff.Api.Infrastructure.DI.EndpointMappingExtensions"] = new(Reach.NotOnAnExternalRoute,
            "Maps the workforce Stored File routes (MapStoredFileEndpoints, /api/finance/files); the route check below proves none is a Ciam route."),
        ["Sprk.Bff.Api.Infrastructure.DI.FinanceModule"] = new(Reach.NotOnAnExternalRoute,
            "DI registration of the file store and its consumers."),
    };

    private static bool IsFileStoreOwn(Type outermost) =>
        outermost.Namespace == "Sprk.Bff.Api.Services.Finance.Files"
        || (outermost.Namespace == "Sprk.Bff.Api.Api.Finance" && outermost.Name.StartsWith("StoredFile", StringComparison.Ordinal));

    private static bool IsExternalPlane(string outermostFullName) =>
        outermostFullName.StartsWith("Sprk.Bff.Api.Api.ExternalAccess.", StringComparison.Ordinal)
        || outermostFullName.StartsWith("Sprk.Bff.Api.Infrastructure.ExternalAccess.", StringComparison.Ordinal);

    private static bool Mentions(Type type) => FileStoreTypes.Contains(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(Mentions));

    private static bool Mentions(MethodBase method) => (method.DeclaringType is { } declaring && Mentions(declaring))
        || (method is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(Mentions));

    /// <summary>Source method ("{outermost type}::{method}") → used, for every source method outside the file store that references it.</summary>
    private static IReadOnlySet<string> Surfaces(IEnumerable<Type> types)
    {
        var surfaces = new HashSet<string>(StringComparer.Ordinal);
        var list = types.ToList();
        foreach (var (caller, method, target) in IlCallScan.MethodReferences(list))
        {
            if (Mentions(target))
                Add(surfaces, caller, method);
        }

        foreach (var (caller, method, target) in IlCallScan.TypeReferences(list))
        {
            if (Mentions(target))
                Add(surfaces, caller, method);
        }

        return surfaces;

        static void Add(HashSet<string> surfaces, Type caller, MethodBase method)
        {
            var outermost = IlCallScan.Outermost(caller);
            if (!IsFileStoreOwn(outermost))
                surfaces.Add($"{outermost.FullName}::{IlCallScan.SourceMethodName(caller, method)}");
        }
    }

    private static IReadOnlySet<string> CallerTypes(IEnumerable<string> surfaces) =>
        surfaces.Select(s => s[..s.IndexOf("::", StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);

    private static List<string> CensusProblems(IReadOnlySet<string> callers, IReadOnlyDictionary<string, Entry> census)
    {
        var problems = new List<string>();
        foreach (var caller in callers.Where(c => !census.ContainsKey(c)).Order(StringComparer.Ordinal))
            problems.Add($"NEW file-store caller: {caller}");

        foreach (var (name, entry) in census.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!callers.Contains(name))
                problems.Add($"STALE census entry {name}: it no longer references the file store — remove it.");
            if (string.IsNullOrWhiteSpace(entry.Reason))
                problems.Add($"NO REASON for census entry {name}.");
            if (IsExternalPlane(name) && entry.Reach != Reach.ExternalGate)
                problems.Add($"EXTERNAL-PLANE caller {name} is not the firm file gate (it is {entry.Reach}).");
            if (!IsExternalPlane(name) && entry.Reach == Reach.ExternalGate)
                problems.Add($"{name} is classified ExternalGate but is outside the external-plane namespaces.");
        }

        return problems;
    }

    [Fact(DisplayName = "ebilling-r1 124: every type that references the Finance file store is in the census, and every external-plane one is the firm file gate")]
    public void EveryFileStoreCaller_IsInTheCensus()
    {
        var callers = CallerTypes(Surfaces(BffTypes.Value));

        var problems = CensusProblems(callers, Census);

        Assert.True(problems.Count == 0,
            "The Finance file store holds Procedure sources (internal only) beside the firm's invoice PDFs. Every referencing type is "
            + "classified, and an external-plane one must be the firm file gate. See this class's Maintenance note:\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ebilling-r1 124: control — a new caller, a stale entry, a reasonless entry and a misplaced external-plane or gate entry are found; a classified census is not")]
    public void Census_MustFireAndMustNotFire()
    {
        var direct = typeof(FileStoreCensusControlDirect).FullName!;
        var generic = typeof(FileStoreCensusControlViaGenericResolution).FullName!;
        var callers = CallerTypes(Surfaces(new[] { typeof(FileStoreCensusControlDirect), typeof(FileStoreCensusControlViaGenericResolution), typeof(FileStoreCensusControlNone) }));

        // The detector sees a direct member call AND a generic resolution (GetRequiredService<StoredFileService>), and not an unrelated type.
        Assert.Equal(new[] { direct, generic }.Order(StringComparer.Ordinal).ToArray(), callers.Order(StringComparer.Ordinal).ToArray());

        var classified = new Dictionary<string, Entry>
        {
            [direct] = new(Reach.NotOnAnExternalRoute, "a control"),
            [generic] = new(Reach.NotOnAnExternalRoute, "a control"),
        };
        Assert.Empty(CensusProblems(callers, classified));

        // Must fire: a new caller.
        Assert.Contains($"NEW file-store caller: {direct}", CensusProblems(callers, new Dictionary<string, Entry> { [generic] = classified[generic] }));
        // Must fire: a stale entry, and a reasonless one.
        var stale = new Dictionary<string, Entry>(classified) { ["Some.Gone.Type"] = new(Reach.NotOnAnExternalRoute, "gone") };
        Assert.Contains("STALE census entry Some.Gone.Type: it no longer references the file store — remove it.", CensusProblems(callers, stale));
        var reasonless = new Dictionary<string, Entry>(classified) { [direct] = new(Reach.NotOnAnExternalRoute, " ") };
        Assert.Contains($"NO REASON for census entry {direct}.", CensusProblems(callers, reasonless));
        // Must fire: an external-plane caller that is not the gate, and a gate entry outside the external plane.
        var externalName = "Sprk.Bff.Api.Api.ExternalAccess.SomeNewEndpoints";
        Assert.Contains(
            $"EXTERNAL-PLANE caller {externalName} is not the firm file gate (it is ExternalStagingWrite).",
            CensusProblems(new HashSet<string> { externalName }, new Dictionary<string, Entry> { [externalName] = new(Reach.ExternalStagingWrite, "x") }));
        Assert.Contains(
            $"{direct} is classified ExternalGate but is outside the external-plane namespaces.",
            CensusProblems(callers, new Dictionary<string, Entry>(classified) { [direct] = new(Reach.ExternalGate, "x") }));
        // Must not fire: an external-plane caller that IS the gate.
        Assert.Empty(CensusProblems(new HashSet<string> { externalName }, new Dictionary<string, Entry> { [externalName] = new(Reach.ExternalGate, "the gate") }));
    }

    // ── The closure: what a Ciam handler reaches through the BFF's own code ─────────────────────────────────────────

    /// <summary>
    /// How many calls deep a handler's closure is followed. A closure still open at the bound is itself a failure
    /// (<see cref="ClosureProblems"/>): the guard never silently stops looking.
    /// </summary>
    private const int MaxDepth = 30;

    /// <summary>The table-name text the closure treats as a file-store query (a column such as <c>sprk_storedfileid</c> contains it).</summary>
    private const string TableNameText = "sprk_storedfile";

    /// <summary>What a node does that is not a call edge of its own: a literal, a field, a type reference, a generic resolution.</summary>
    private sealed record Use(string Kind, string To);

    /// <summary>One thing a handler's closure reaches: <paramref name="From"/> (a "{type}::{method}" node) does <paramref name="Kind"/> to <paramref name="To"/>.</summary>
    private sealed record Reached(string Kind, string From, string To, string Path)
    {
        /// <summary>The identity an allow-list entry or the gate list names (without the handler and path).</summary>
        public string Id => $"{Kind}: {From} -> {To}";
    }

    /// <summary>
    /// The call graph of the BFF (task 093's <see cref="ExternalDocumentNarrowingGuardTests.Scan"/>: nodes are
    /// "{outermost type}::{source method name}", a lambda, local function or state machine belongs to the method it was written in),
    /// widened with interface and virtual dispatch, static constructors and non-call uses.
    /// </summary>
    private sealed record CallGraph(
        IReadOnlyDictionary<string, HashSet<string>> Calls,
        IReadOnlyDictionary<string, HashSet<Use>> Uses,
        IReadOnlyDictionary<string, string> TerminalTypes);

    /// <summary>A legitimate reach of the file store or a table-name literal from a Ciam handler, with the route and why it exposes no Stored File content or metadata.</summary>
    private sealed record Allowed(string Route, string Why);

    /// <summary>An allow-list handler of "*": the node is shared infrastructure (a static classification or decision table), not one route's code.</summary>
    private const string AnyHandler = "*";

    private static readonly Lazy<CallGraph> BffGraph = new(() => BuildGraph(BffTypes.Value.ToList(), Census));

    private static readonly Regex GenericArgs = new(@"\[\[.*\]\](?=::)", RegexOptions.Compiled);

    /// <summary>
    /// The file store's behaviour, as opposed to its data shapes: the service, the blob store, the workforce endpoints and filters
    /// (<c>StoredFile*</c> in <c>Api.Finance</c>) and <see cref="StoredFileSchema"/> (the table's names). A row, a spec, an id helper or a
    /// result record carries no read of its own, so a call to one is not a reach of the store.
    /// </summary>
    private static bool IsFileStoreBehaviour(Type outermost) =>
        FileStoreTypes.Contains(outermost) || outermost == typeof(StoredFileSchema)
        || (outermost.Namespace == "Sprk.Bff.Api.Api.Finance" && outermost.Name.StartsWith("StoredFile", StringComparison.Ordinal));

    private static string NodeType(string node) => node[..node.IndexOf("::", StringComparison.Ordinal)];

    /// <summary>The kind of a closure-terminal type (the walk stops there and reports the reach), or null.</summary>
    private static string? TerminalKind(Type outermost, IReadOnlyDictionary<string, Entry> census)
    {
        if (IsFileStoreBehaviour(outermost))
            return "file store member";
        if (census.TryGetValue(outermost.FullName ?? string.Empty, out var entry) && entry.Reach == Reach.NotOnAnExternalRoute)
            return "NotOnAnExternalRoute member";
        return null;
    }

    private static string? TypeUse(Type type, IReadOnlyDictionary<string, Entry> census, bool classified)
    {
        var outer = IlCallScan.Outermost(type);
        if (IsFileStoreBehaviour(outer))
            return outer.Name;
        if (classified && census.TryGetValue(outer.FullName ?? string.Empty, out var entry) && entry.Reach == Reach.NotOnAnExternalRoute)
            return outer.Name;
        return null;
    }

    /// <summary>A generic resolution (<c>GetRequiredService&lt;StoredFileService&gt;</c>) or a generic container of a file-store type: a use that is not a call into the file store.</summary>
    private static string? GenericUse(MethodBase target, IReadOnlyDictionary<string, Entry> census)
    {
        if (target.DeclaringType is not { } declaring)
            return null;
        if (declaring.IsGenericType
            && declaring.GetGenericArguments().Select(a => TypeUse(a, census, classified: false)).FirstOrDefault(s => s is not null) is { } held)
            return held;
        if (target is MethodInfo { IsGenericMethod: true } generic
            && generic.GetGenericArguments().Select(a => TypeUse(a, census, classified: true)).FirstOrDefault(s => s is not null) is { } resolved)
            return resolved;
        return null;
    }

    private static CallGraph BuildGraph(IReadOnlyList<Type> types, IReadOnlyDictionary<string, Entry> census)
    {
        var scan = ExternalDocumentNarrowingGuardTests.Scan(types, _ => false, _ => null);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (source, targets) in scan.Calls)
            calls[source] = targets.Select(t => GenericArgs.Replace(t, string.Empty)).ToHashSet(StringComparer.Ordinal);

        var uses = new Dictionary<string, HashSet<Use>>(StringComparer.Ordinal);
        void AddUse(string source, string kind, string to)
        {
            if (!uses.TryGetValue(source, out var set))
                uses[source] = set = new HashSet<Use>();
            set.Add(new Use(kind, to));
        }

        void AddCall(string source, string target)
        {
            if (!calls.TryGetValue(source, out var set))
                calls[source] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(target);
        }

        var terminals = types.Select(IlCallScan.Outermost).Distinct()
            .Select(o => (o.FullName, Kind: TerminalKind(o, census))).Where(t => t.Kind is not null && t.FullName is not null)
            .ToDictionary(t => t.FullName!, t => t.Kind!, StringComparer.Ordinal);

        // Interface and virtual dispatch: a call to IFoo.Bar (or a base's virtual Bar) reaches every implementer's Bar in the scanned types.
        var implementers = new Dictionary<Type, List<Type>>();
        foreach (var type in types.Where(t => !t.IsInterface && !t.IsAbstract))
        {
            foreach (var contract in type.GetInterfaces())
                Implemented(implementers, Definition(contract), type);
            for (var b = type.BaseType; b is not null && b != typeof(object); b = b.BaseType)
                Implemented(implementers, Definition(b), type);
        }

        var dispatched = new HashSet<(Type Declaring, string Name)>();
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            if (GenericUse(target, census) is { } generic)
                AddUse(ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod), "file store type resolved or held", generic);

            if (target.DeclaringType is not { } declaring)
                continue;
            if (declaring.Assembly == caller.Assembly && (declaring.IsInterface || target.IsAbstract || target.IsVirtual))
                dispatched.Add((Definition(declaring), target.Name));

            // Scanning a set of types that is not the BFF itself (the controls): a call from it INTO the BFF's file store or a classified
            // type is still an edge to a terminal.
            var outermost = IlCallScan.Outermost(declaring);
            if (declaring.Assembly != caller.Assembly && TerminalKind(outermost, census) is { } kind)
            {
                terminals[outermost.FullName!] = kind;
                AddCall(ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod), $"{outermost.FullName}::{ExternalDocumentNarrowingGuardTests.Normalize(target.Name)}");
            }
        }

        foreach (var (declaring, name) in dispatched)
        {
            if (!implementers.TryGetValue(declaring, out var impls))
                continue;
            var from = $"{IlCallScan.Outermost(declaring).FullName}::{ExternalDocumentNarrowingGuardTests.Normalize(name)}";
            foreach (var impl in impls)
            {
                foreach (var m in impl.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .Where(m => m.Name == name || m.Name.EndsWith("." + name, StringComparison.Ordinal)))
                    AddCall(from, $"{IlCallScan.Outermost(impl).FullName}::{ExternalDocumentNarrowingGuardTests.Normalize(m.Name)}");
            }
        }

        // A type reference (typeof, a cast, an array).
        foreach (var (caller, callerMethod, target) in IlCallScan.TypeReferences(types))
        {
            if (TypeUse(target, census, classified: true) is { } used)
                AddUse(ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod), "file store type reference", used);
        }

        // A static field read: the field's type runs its static constructor (a static table built from the table name).
        foreach (var type in types)
        {
            foreach (var body in IlCallScan.DeclaredBodies(type))
            {
                var source = ExternalDocumentNarrowingGuardTests.SourceKey(type, body);
                foreach (var instruction in IlCallScan.Instructions(body))
                {
                    if (instruction.OpCode.OperandType != OperandType.InlineField)
                        continue;
                    var field = IlCallScan.Resolve(body, instruction.Operand, (m, t, ta, ma) => m.ResolveField(t, ta, ma));
                    if (field?.DeclaringType is not { } owner || !field.IsStatic)
                        continue;
                    if (IlCallScan.Outermost(owner) == typeof(StoredFileSchema))
                        AddUse(source, "StoredFileSchema field", $"StoredFileSchema.{field.Name}");
                    else if (owner.Assembly == type.Assembly)
                        AddCall(source, $"{IlCallScan.Outermost(owner).FullName}::.cctor");
                }
            }
        }

        // The table's name, however it was spelled in source: a literal, a const (inlined by the compiler), a folded interpolation piece.
        foreach (var (caller, callerMethod, value) in IlCallScan.StringLoads(types))
        {
            if (value.Contains(TableNameText, StringComparison.OrdinalIgnoreCase))
                AddUse(ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod), "table-name literal", TableNameText);
        }

        return new CallGraph(calls, uses, terminals);

        static Type Definition(Type t) => t.IsGenericType ? t.GetGenericTypeDefinition() : t;

        static void Implemented(Dictionary<Type, List<Type>> map, Type contract, Type impl)
        {
            if (!map.TryGetValue(contract, out var list))
                map[contract] = list = new List<Type>();
            list.Add(impl);
        }
    }

    /// <summary>Everything <paramref name="start"/> reaches within <paramref name="maxDepth"/> calls, and whether a node was left unvisited at the bound.</summary>
    private static (List<Reached> Reached, bool Exhausted) Follow(string start, CallGraph graph, int maxDepth)
    {
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal) { [start] = null };
        var depth = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        var reached = new List<Reached>();
        var exhausted = false;

        string PathTo(string node)
        {
            var steps = new List<string>();
            for (var n = node; n is not null; n = parent[n])
                steps.Add(n);
            steps.Reverse();
            return string.Join(" -> ", steps);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (graph.Uses.TryGetValue(node, out var uses))
            {
                foreach (var use in uses.OrderBy(u => u.Kind, StringComparer.Ordinal).ThenBy(u => u.To, StringComparer.Ordinal))
                    reached.Add(new Reached(use.Kind, node, use.To, PathTo(node)));
            }

            if (!graph.Calls.TryGetValue(node, out var next))
                continue;
            foreach (var target in next.Order(StringComparer.Ordinal))
            {
                if (graph.TerminalTypes.TryGetValue(NodeType(target), out var kind))
                {
                    reached.Add(new Reached(kind, node, target, PathTo(node) + " -> " + target));
                    continue;
                }

                if (parent.ContainsKey(target))
                    continue;
                if (depth[node] + 1 > maxDepth)
                {
                    exhausted = true;
                    continue;
                }

                parent[target] = node;
                depth[target] = depth[node] + 1;
                queue.Enqueue(target);
            }
        }

        return (reached, exhausted);
    }

    /// <summary>
    /// The closure check. For every Ciam handler (each distinct handler method once, listing all its routes): follow its calls through
    /// the BFF's own code (<see cref="Follow"/>) and report anything it reaches that is not (a) in the firm file gate's
    /// pinned list <paramref name="gate"/> or (b) in the justified <paramref name="allowed"/> list. An allowed entry that is no longer
    /// reached, or has no route or reason, fails (the list only shrinks).
    /// </summary>
    private static List<string> ClosureProblems(
        IEnumerable<Ciam> ciamRoutes, CallGraph graph, IReadOnlyDictionary<string, Allowed> allowed, IReadOnlySet<string> gate, int maxDepth)
    {
        var problems = new List<string>();
        var handlers = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var route in ciamRoutes.Where(r => r.Handler?.DeclaringType is not null))
        {
            var key = ExternalDocumentNarrowingGuardTests.SourceKey(route.Handler!.DeclaringType!, route.Handler);
            if (!handlers.TryGetValue(key, out var routes))
                handlers[key] = routes = new List<string>();
            routes.Add(route.Route);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (handler, routes) in handlers)
        {
            var (reached, exhausted) = Follow(handler, graph, maxDepth);
            if (exhausted)
                problems.Add($"CLOSURE NOT EXHAUSTED within {maxDepth} calls for {handler} ({string.Join("; ", routes)}): raise the bound or cut the chain.");

            foreach (var item in reached.GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.First()))
            {
                if (gate.Contains(item.Id))
                    continue;
                var id = $"{handler} => {item.Id}";
                var shared = $"{AnyHandler} => {item.Id}";
                if (allowed.ContainsKey(id))
                {
                    used.Add(id);
                    continue;
                }

                if (allowed.ContainsKey(shared))
                {
                    used.Add(shared);
                    continue;
                }

                problems.Add($"CIAM CLOSURE reaches the file store outside the firm file gate: {id}\n    routes: {string.Join("; ", routes)}\n    path: {item.Path}");
            }
        }

        foreach (var (id, entry) in allowed.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(entry.Route) || string.IsNullOrWhiteSpace(entry.Why))
                problems.Add($"ALLOW-LIST entry without a route or a reason: {id}");
            if (!used.Contains(id))
                problems.Add($"STALE allow-list entry (no longer reached): {id}");
        }

        return problems;
    }

    /// <summary>
    /// The pinned firm file gate: what its own nodes may reach. Every entry is "{kind}: {node} -> {member}". The gate is task 093's
    /// <see cref="Reach.ExternalGate"/> pair; <c>ExternalDocumentNarrowingGuardTests</c> pins that it reaches the record gate, the (a')
    /// predicate and the firm read, and its decision point <c>IsFirmFile</c> is covered by <c>ExternalInvoiceFileRoutesTests</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> GateReach = new HashSet<string>(StringComparer.Ordinal)
    {
        // The content route: serves the one file of the gate's set (InvoiceFiles) through the store's own streamer, which scans the
        // content type, sets the safe content type and refuses a file whose scan is pending.
        $"file store member: {GateHandlers}::GetInvoiceFileContentAsync -> {ApiFinance}.StoredFileEndpoints::StreamServableAsync",
        $"file store member: {GateHandlers}::GetInvoiceFileContentAsync -> {ApiFinance}.StoredFileEndpoints::SafeContentType",
        $"file store member: {GateHandlers}::GetInvoiceFileContentAsync -> {ApiFinance}.StoredFileAccessFilter::ScanPending",
    };

    private const string ApiFinance = "Sprk.Bff.Api.Api.Finance";
    private const string Files = "Sprk.Bff.Api.Services.Finance.Files";
    private const string SubmissionService = "Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService";
    private const string SubmitHandler = "Sprk.Bff.Api.Api.ExternalAccess.ExternalInvoiceSubmissionEndpoints::SubmitAsync";
    private const string DeleteDocumentHandler = "Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints::DeleteOwnDocument";

    private static string Id(string handler, string kind, string from, string to) => $"{handler} => {kind}: {from} -> {to}";

    /// <summary>
    /// Legitimate reaches of the file store or its table from a Ciam handler OTHER than the gate, each with the route and why it exposes
    /// no Stored File content or metadata. Keyed "{handler} => {kind}: {node} -> {member}". This list only shrinks.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Allowed> AllowedReach = new Dictionary<string, Allowed>(StringComparer.Ordinal)
    {
        // ── The submission routes: the caller's OWN staged upload (write, delete, existence check); never a read of another file ──
        [Id(SubmitHandler, "file store member", SubmissionService + "::WriteAsync", $"{Files}.StoredFileService::StageAsync")] = new(
            "POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions",
            "Stages the caller's own upload (purpose Invoice PDF or Invoice supporting file, parent the submission's invoice). A write; it lists and returns no file."),
        [Id(SubmitHandler, "file store member", SubmissionService + "::SubmitAsync", $"{Files}.StoredFileService::CanStageAsync")] = new(
            "POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions",
            "Asks whether the store is configured and confirmed for this environment (a yes or no about configuration); it reads no row and no file."),
        [Id(SubmitHandler, "file store member", SubmissionService + "::CompensateAsync", $"{Files}.StoredFileService::DeleteStagedAsync")] = new(
            "POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions",
            "Deletes the staged file of the same request when the submission is refused; by the id this request staged, never a caller-chosen file."),
        [Id(SubmitHandler, "file store member", SubmissionService + "::UploadName", $"{Files}.StoredFileSchema::NameSegmentOf")] = new(
            "POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions",
            "A pure string sanitiser for the display name of the caller's own upload; reads nothing."),

        // ── An existence-only count ──
        [Id(DeleteDocumentHandler, "table-name literal", "Sprk.Bff.Api.Services.DocumentCheckoutService::CountStoredFileCopiesAsync", "sprk_storedfile")] = new(
            "DELETE /api/v1/external/documents/{id}",
            "DocumentCheckoutService.DeleteAsync counts (OData, app-only, ids not returned) the Stored Files that name the caller's own document as their source document, "
            + "and the route answers only a generic 'in use' refusal (ExternalProjectDataEndpoints InvoiceSourceDeleteResult). No Stored File content, name, id or count "
            + "reaches the caller. A Procedure source has no source document, so it can never be counted."),

        // ── Shared infrastructure: static tables that NAME the table, none a query (the closure reaches them through a static field read) ──
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Services.Access.SecureChildLineage::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The lineage registry's classification row for the table: HardDeny for external reach. It denies; it reads nothing."),
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceVisibility::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The (a') visibility decision's lineage map row (entity set, id column, invoice link). A decision input that returns nothing to the caller; "
            + "ExternalInvoiceVisibilityGuardTests pins who may use it."),
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The restricted-content table set (ownership resolution keeps the business unit's depth); a static set, not a query."),
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The list of tables that carry the creating person's column; a static list, not a query."),
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The tables whose access is never inherited from a parent (a deny list); it reads nothing."),
        [Id(AnyHandler, "table-name literal", "Sprk.Bff.Api.Services.Access.ExternalTeamShareSynchronizer::.cctor", "sprk_storedfile")] = new(
            "every Ciam route (shared)",
            "The tables never shared to an external team (a deny list); it reads nothing."),
    };

    /// <summary>
    /// The Ciam routes whose handler METHOD references the file store itself, as "METHOD route": exactly the firm file content route. (The
    /// list route's handler reads the gate's set and references no file-store member; the gate is an endpoint filter.)
    /// </summary>
    private static readonly IReadOnlySet<string> PinnedExternalFileRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        $"{GateHandlers}::GetInvoiceFileContentAsync <= GET /api/v1/external/invoices/{{id:guid}}/files/{{fileId:guid}}/content",
    };

    private sealed record Ciam(string Route, MethodInfo? Handler);

    private const BindingFlags AnyMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static List<string> RouteProblems(
        IEnumerable<Ciam> ciamRoutes, IReadOnlySet<string> surfaces, IReadOnlyDictionary<string, Entry> census, IReadOnlySet<string> pinned)
    {
        var problems = new List<string>();
        var direct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in ciamRoutes)
        {
            if (route.Handler?.DeclaringType is not { } declaring)
            {
                problems.Add($"UNKNOWN handler for Ciam route {route.Route}: it cannot be checked.");
                continue;
            }

            var outermost = IlCallScan.Outermost(declaring);
            if (IsFileStoreOwn(outermost))
                problems.Add($"A Ciam route's handler is a file-store endpoint: {route.Route} -> {outermost.FullName}");
            if (census.TryGetValue(outermost.FullName!, out var entry) && entry.Reach == Reach.NotOnAnExternalRoute)
                problems.Add($"A Ciam route's handler is a file-store caller classified NotOnAnExternalRoute: {route.Route} -> {outermost.FullName}");

            var key = $"{outermost.FullName}::{IlCallScan.SourceMethodName(declaring, route.Handler)}";
            if (surfaces.Contains(key))
                direct.Add($"{key} <= {route.Route}");
        }

        foreach (var surface in direct.Where(d => !pinned.Contains(d)).Order(StringComparer.Ordinal))
            problems.Add($"NEW Ciam route that reaches the file store: {surface}");
        foreach (var stale in pinned.Where(p => !direct.Contains(p)).Order(StringComparer.Ordinal))
            problems.Add($"STALE pinned route (it no longer reaches the file store as pinned): {stale}");
        return problems;
    }

    [Fact(DisplayName = "ebilling-r1 124: the Ciam routes reach the file store only through the firm file gate (handler, then its whole call closure), and no Ciam route's handler is a NotOnAnExternalRoute caller")]
    public async Task CiamRoutes_ReachTheFileStoreOnlyThroughTheFirmFileGate()
    {
        var surfaces = Surfaces(BffTypes.Value);
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
            var ciam = new List<Ciam>();
            foreach (var endpoint in app.Endpoints)
            {
                if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                    continue;
                var policy = await RouteAuthorizationGuardTests.MiddlewarePolicyAsync(provider, endpoint);
                if (policy?.AuthenticationSchemes.Contains(AuthSchemes.Ciam) != true)
                    continue;
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                var verb = methods is { Count: > 0 } ? string.Join(",", methods.Order(StringComparer.Ordinal)) : "*";
                ciam.Add(new Ciam($"{verb} {endpoint.RoutePattern.RawText}", endpoint.Metadata.GetMetadata<MethodInfo>()));
            }

            Assert.True(ciam.Count > 20 && ciam.Any(c => c.Route.StartsWith("GET /api/v1/external/", StringComparison.Ordinal)),
                $"{app.Environment}: the census read {ciam.Count} Ciam routes — the scan is broken.");

            var problems = RouteProblems(ciam, surfaces, Census, PinnedExternalFileRoutes);
            problems.AddRange(ClosureProblems(ciam, BffGraph.Value, AllowedReach, GateReach, MaxDepth));
            Assert.True(problems.Count == 0, $"{app.Environment}:\n" + string.Join("\n", problems));
        }
    }

    [Fact(DisplayName = "ebilling-r1 124: control — a Ciam route whose handler reaches the file store unpinned, one whose handler type is classified NotOnAnExternalRoute, an unknown handler and a stale pin are found; the pinned gate route is not")]
    public void Routes_MustFireAndMustNotFire()
    {
        var types = new[] { typeof(FileStoreCensusControlDirect), typeof(FileStoreCensusControlNone) };
        var surfaces = Surfaces(types);
        var directHandler = typeof(FileStoreCensusControlDirect).GetMethod(nameof(FileStoreCensusControlDirect.Read), AnyMethod)!;
        var noneHandler = typeof(FileStoreCensusControlNone).GetMethod(nameof(FileStoreCensusControlNone.Read), AnyMethod)!;
        var key = $"{typeof(FileStoreCensusControlDirect).FullName}::Read";
        var census = new Dictionary<string, Entry>
        {
            [typeof(FileStoreCensusControlDirect).FullName!] = new(Reach.NotOnAnExternalRoute, "a control"),
        };
        var none = new HashSet<string>();

        // Must fire: a Ciam route whose handler is one of the file store's own endpoints (the workforce Stored File download).
        var storeHandler = typeof(StoredFileEndpoints).GetMethod("GetContent", AnyMethod)!;
        Assert.Contains($"A Ciam route's handler is a file-store endpoint: GET /api/v1/external/f -> {typeof(StoredFileEndpoints).FullName}",
            RouteProblems([new Ciam("GET /api/v1/external/f", storeHandler)], surfaces, census, none));

        // Must fire: a Ciam route whose handler references the file store and is not pinned (also classified NotOnAnExternalRoute).
        var problems = RouteProblems([new Ciam("GET /api/v1/external/x", directHandler)], surfaces, census, none);
        Assert.Contains($"A Ciam route's handler is a file-store caller classified NotOnAnExternalRoute: GET /api/v1/external/x -> {typeof(FileStoreCensusControlDirect).FullName}", problems);
        Assert.Contains($"NEW Ciam route that reaches the file store: {key} <= GET /api/v1/external/x", problems);
        // Must fire: an unknown handler, and a stale pin.
        Assert.Contains("UNKNOWN handler for Ciam route GET /api/v1/external/y: it cannot be checked.",
            RouteProblems([new Ciam("GET /api/v1/external/y", null)], surfaces, census, none));
        Assert.Contains($"STALE pinned route (it no longer reaches the file store as pinned): {key} <= GET /gone",
            RouteProblems([new Ciam("GET /api/v1/external/z", noneHandler)], surfaces, census, new HashSet<string> { $"{key} <= GET /gone" }));
        // Must not fire: the same route pinned and classified as the gate's, and a route whose handler touches nothing.
        var gateCensus = new Dictionary<string, Entry> { [typeof(FileStoreCensusControlDirect).FullName!] = new(Reach.ExternalGate, "the gate") };
        Assert.Empty(RouteProblems([new Ciam("GET /api/v1/external/x", directHandler)], surfaces, gateCensus, new HashSet<string> { $"{key} <= GET /api/v1/external/x" }));
        Assert.Empty(RouteProblems([new Ciam("GET /api/v1/external/z", noneHandler)], surfaces, gateCensus, none));
    }

    // ── Controls for the closure ─────────────────────────────────────────────────────────────────────────────────

    private static MethodInfo HandlerOf(Type type, string name) => type.GetMethod(name, AnyMethod)!;

    private static string NodeOf(Type type, string name) => ExternalDocumentNarrowingGuardTests.SourceKey(type, HandlerOf(type, name));

    private static readonly IReadOnlyDictionary<string, Allowed> NoneAllowed = new Dictionary<string, Allowed>();
    private static readonly IReadOnlySet<string> NoneGate = new HashSet<string>();

    private static readonly Type[] ClosureControlTypes =
    [
        typeof(ClosureControlIndirectHandler), typeof(ClosureControlMiddle), typeof(ClosureControlLeaf),
        typeof(ClosureControlClassifiedHandler), typeof(ClosureControlClassifiedMiddle),
        typeof(ClosureControlQueryHandler), typeof(ClosureControlConstHandler), typeof(ClosureControlQueryClient),
        typeof(ClosureControlInterfaceHandler), typeof(IClosureControlReader), typeof(ClosureControlReader),
        typeof(ClosureControlStaticHandler), typeof(ClosureControlTables),
        typeof(ClosureControlSchemaHandler), typeof(ClosureControlCleanHandler), typeof(ClosureControlCleanMiddle),
    ];

    private static List<string> ControlProblems(
        Type handlerType, string method, IReadOnlyDictionary<string, Allowed>? allowed = null, IReadOnlySet<string>? gate = null, int depth = MaxDepth)
    {
        var graph = BuildGraph(ClosureControlTypes.SelectMany(IlCallScan.WithNested).ToList(), Census);
        return ClosureProblems(
            [new Ciam("GET /api/v1/external/control", HandlerOf(handlerType, method))], graph, allowed ?? NoneAllowed, gate ?? NoneGate, depth);
    }

    [Fact(DisplayName = "ebilling-r1 124: control — the closure finds an indirect call to the file store, an indirect call into a NotOnAnExternalRoute service, a direct table query, a query through an interface, a table name in a static table, and a StoredFileSchema member")]
    public void Closure_MustFire()
    {
        var store = typeof(StoredFileService).FullName;

        // Probe shape 1: a handler that reaches a file-store member two calls away (handler -> middle -> leaf -> StoredFileService).
        var indirect = ControlProblems(typeof(ClosureControlIndirectHandler), "Handle");
        Assert.Contains(indirect, p => p.Contains("file store member") && p.Contains($"{store}::ReadRowsForInvoiceAsync")
            && p.Contains("ClosureControlMiddle::Load") && p.Contains("ClosureControlLeaf::Read"));

        // Probe shape 1b: a handler that reaches a service the census classifies NotOnAnExternalRoute (here the real ApExportService).
        var classified = ControlProblems(typeof(ClosureControlClassifiedHandler), "Handle");
        Assert.Contains(classified, p => p.Contains("NotOnAnExternalRoute member") && p.Contains("ApExportService::GetBatchAsync"));

        // Probe shape 2: a direct query by table name, both as a folded interpolation (the entity set) and as the inlined const.
        Assert.Contains(ControlProblems(typeof(ClosureControlQueryHandler), "Handle"),
            p => p.Contains("table-name literal") && p.Contains("ClosureControlQueryHandler::Handle"));
        Assert.Contains(ControlProblems(typeof(ClosureControlConstHandler), "Handle"),
            p => p.Contains("table-name literal") && p.Contains("ClosureControlConstHandler::Handle"));

        // The same through an interface (dispatch to the implementer), and through a static table (a static field read runs its cctor).
        Assert.Contains(ControlProblems(typeof(ClosureControlInterfaceHandler), "Handle"),
            p => p.Contains("table-name literal") && p.Contains("ClosureControlReader::Read"));
        Assert.Contains(ControlProblems(typeof(ClosureControlStaticHandler), "Handle"),
            p => p.Contains("table-name literal") && p.Contains("ClosureControlTables::.cctor"));

        // A StoredFileSchema member and a StoredFileSchema field.
        Assert.Contains(ControlProblems(typeof(ClosureControlSchemaHandler), "Handle"),
            p => p.Contains("file store member") && p.Contains("StoredFileSchema::TableOf"));
        Assert.Contains(ControlProblems(typeof(ClosureControlSchemaHandler), "Columns"),
            p => p.Contains("StoredFileSchema field") && p.Contains("StoredFileSchema.ReadColumns"));
    }

    [Fact(DisplayName = "ebilling-r1 124: control — a closure still open at the depth bound fails; one that closes within it does not")]
    public void Closure_DepthBound_MustFire()
    {
        // handler -> middle (1) -> leaf (2): the leaf is expanded at depth 2.
        Assert.Contains(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", depth: 1), p => p.StartsWith("CLOSURE NOT EXHAUSTED within 1 calls"));
        Assert.Contains(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", depth: 0), p => p.StartsWith("CLOSURE NOT EXHAUSTED within 0 calls"));
        Assert.DoesNotContain(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", depth: 2), p => p.StartsWith("CLOSURE NOT EXHAUSTED"));
    }

    [Fact(DisplayName = "ebilling-r1 124: control — a handler that touches no file store passes; a pinned gate reach and a justified allow-list entry pass; a stale, reasonless or wrongly keyed entry fails")]
    public void Closure_MustNotFire_AndTheAllowListRatchets()
    {
        // Must not fire: nothing in the closure names the file store (a sibling table's name and an unrelated call).
        Assert.Empty(ControlProblems(typeof(ClosureControlCleanHandler), "Handle"));

        var handler = NodeOf(typeof(ClosureControlIndirectHandler), "Handle");
        var leaf = NodeOf(typeof(ClosureControlLeaf), "Read");
        var member = $"{typeof(StoredFileService).FullName}::ReadRowsForInvoiceAsync";

        // Must not fire: the reach is in the justified allow-list (handler-keyed), or is the gate's pinned reach.
        var allowed = new Dictionary<string, Allowed> { [Id(handler, "file store member", leaf, member)] = new("GET /control", "a control: reads the control's own rows only") };
        Assert.Empty(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", allowed));
        Assert.Empty(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", gate: new HashSet<string> { $"file store member: {leaf} -> {member}" }));

        // Must not fire: a shared ("*") entry covers any handler.
        var shared = new Dictionary<string, Allowed> { [Id(AnyHandler, "file store member", leaf, member)] = new("every route (control)", "a control") };
        Assert.Empty(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", shared));

        // Must fire: an entry keyed on another handler does not cover it, and is stale.
        var wrong = new Dictionary<string, Allowed> { [Id("Some.Other::Handler", "file store member", leaf, member)] = new("GET /control", "a control") };
        var wrongProblems = ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", wrong);
        Assert.Contains(wrongProblems, p => p.StartsWith("CIAM CLOSURE reaches the file store outside the firm file gate"));
        Assert.Contains(wrongProblems, p => p.StartsWith("STALE allow-list entry"));

        // Must fire: an entry nothing reaches any more (the list only shrinks), and an entry with no route or no reason.
        var stale = new Dictionary<string, Allowed> { [Id(NodeOf(typeof(ClosureControlCleanHandler), "Handle"), "table-name literal", "X::Y", "sprk_storedfile")] = new("GET /control", "gone") };
        Assert.Contains(ControlProblems(typeof(ClosureControlCleanHandler), "Handle", stale), p => p.StartsWith("STALE allow-list entry"));
        var reasonless = new Dictionary<string, Allowed> { [Id(handler, "file store member", leaf, member)] = new("", " ") };
        Assert.Contains(ControlProblems(typeof(ClosureControlIndirectHandler), "Handle", reasonless), p => p.StartsWith("ALLOW-LIST entry without a route or a reason"));
    }

    [Fact(DisplayName = "ebilling-r1 124: the closure sees what the guard claims — the real graph has the firm file gate's reach, the allow-list entries and a deep chain")]
    public void RealGraph_HasTheExpectedShape()
    {
        var graph = BffGraph.Value;
        // Interface dispatch is in the graph: the submission route reaches the staging service through its interface.
        Assert.True(graph.Calls.Count > 10_000, $"the BFF call graph has {graph.Calls.Count} nodes — the scan is broken.");
        Assert.Contains(graph.TerminalTypes, t => t.Key.EndsWith(".StoredFileService", StringComparison.Ordinal));
        Assert.Contains(graph.TerminalTypes, t => t.Key.EndsWith(".ApExportService", StringComparison.Ordinal));
        var handler = $"{GateHandlers}::GetInvoiceFileContentAsync";
        var (reached, exhausted) = Follow(handler, graph, MaxDepth);
        Assert.False(exhausted);
        Assert.All(GateReach, id => Assert.Contains(reached, r => r.Id == id));
    }
}

// ── compiled control fixtures for the census controls (never run; IL only) ─────────────────────────────────────────

/// <summary>MUST FIRE: calls a file-store member directly.</summary>
internal sealed class FileStoreCensusControlDirect(StoredFileService store)
{
    internal Task<IReadOnlyList<StoredFileRow>> Read(Guid invoiceId) => store.ReadRowsForInvoiceAsync(invoiceId, 1, CancellationToken.None);
}

/// <summary>MUST FIRE: reaches the file store only through a generic resolution (the shape the firm file gate itself uses).</summary>
internal sealed class FileStoreCensusControlViaGenericResolution(IServiceProvider services)
{
    internal object Resolve() => services.GetRequiredService<StoredFileService>();
}

/// <summary>MUST NOT FIRE: no reference to the file store.</summary>
internal sealed class FileStoreCensusControlNone
{
    internal Task<int> Read(Guid invoiceId) => Task.FromResult(invoiceId == Guid.Empty ? 0 : 1);
}

// ── compiled control fixtures for the closure controls (never run; IL only) ────────────────────────────────────────

/// <summary>MUST FIRE: a handler that reaches a file-store member two calls away (handler -> middle -> leaf -> the store).</summary>
internal sealed class ClosureControlIndirectHandler(ClosureControlMiddle middle)
{
    internal Task<IReadOnlyList<StoredFileRow>> Handle(Guid invoiceId) => middle.Load(invoiceId);
}

internal sealed class ClosureControlMiddle(ClosureControlLeaf leaf)
{
    internal Task<IReadOnlyList<StoredFileRow>> Load(Guid invoiceId) => leaf.Read(invoiceId);
}

internal sealed class ClosureControlLeaf(StoredFileService store)
{
    internal Task<IReadOnlyList<StoredFileRow>> Read(Guid invoiceId) => store.ReadRowsForInvoiceAsync(invoiceId, 1, CancellationToken.None);
}

/// <summary>MUST FIRE: a handler that reaches a service the census classifies NotOnAnExternalRoute (the real <c>ApExportService</c>).</summary>
internal sealed class ClosureControlClassifiedHandler(ClosureControlClassifiedMiddle middle)
{
    internal Task Handle(Guid batchId) => middle.Get(batchId);
}

internal sealed class ClosureControlClassifiedMiddle(Sprk.Bff.Api.Services.Finance.ApExportService export)
{
    internal Task Get(Guid batchId) => export.GetBatchAsync(batchId, CancellationToken.None);
}

/// <summary>MUST FIRE: queries the table by its entity-set name (a folded interpolation of the const).</summary>
internal sealed class ClosureControlQueryHandler(ClosureControlQueryClient client)
{
    internal Task Handle() => client.Get($"{StoredFileSchema.EntitySet}?$top=1");
}

/// <summary>MUST FIRE: passes the table's logical name (an inlined const).</summary>
internal sealed class ClosureControlConstHandler(ClosureControlQueryClient client)
{
    internal Task Handle() => client.Get(StoredFileSchema.Entity);
}

internal sealed class ClosureControlQueryClient
{
    internal Task Get(string url) => url.Length == 0 ? Task.CompletedTask : Task.CompletedTask;
}

/// <summary>MUST FIRE: reaches the table name through an interface (dispatch to the implementer).</summary>
internal sealed class ClosureControlInterfaceHandler(IClosureControlReader reader)
{
    internal Task Handle() => reader.Read();
}

internal interface IClosureControlReader
{
    Task Read();
}

internal sealed class ClosureControlReader : IClosureControlReader
{
    public Task Read() => Task.FromResult(StoredFileSchema.Entity);
}

/// <summary>MUST FIRE: reads a static table whose static constructor holds the table's name.</summary>
internal sealed class ClosureControlStaticHandler
{
    internal int Handle() => ClosureControlTables.Names.Length;
}

internal static class ClosureControlTables
{
    internal static readonly string[] Names = [StoredFileSchema.Entity, "sprk_document"];
}

/// <summary>MUST FIRE: calls a <c>StoredFileSchema</c> member, and reads one of its static fields.</summary>
internal sealed class ClosureControlSchemaHandler
{
    internal string Handle() => StoredFileSchema.TableOf(StoredFileParentKind.Invoice);

    internal object Columns() => StoredFileSchema.ReadColumns;
}

/// <summary>MUST NOT FIRE: a sibling table's name and an unrelated call; nothing names the file store.</summary>
internal sealed class ClosureControlCleanHandler(ClosureControlCleanMiddle middle)
{
    internal string Handle() => middle.Run();
}

internal sealed class ClosureControlCleanMiddle
{
    internal string Run() => "sprk_document" + Guid.Empty.ToString("D");
}
