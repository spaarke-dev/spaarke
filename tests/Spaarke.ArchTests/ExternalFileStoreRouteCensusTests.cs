using System.Reflection;
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
/// </list>
///
/// <para><b>Maintenance.</b> When this fails: (a) a NEW caller of the file store — add it to <see cref="Census"/> with the reach that is
/// true of it and a reason. If it is called from an external route, it must go through the firm file gate and be a
/// <see cref="Reach.ExternalGate"/> or <see cref="Reach.ExternalStagingWrite"/> entry; ask the unified-access-control owner. (b) A Ciam
/// route that reaches the file store — do not pin it; route it through the gate.</para>
///
/// <para>Controls: <see cref="Census_MustFireAndMustNotFire"/> and <see cref="Routes_MustFireAndMustNotFire"/> over the
/// <c>FileStoreCensusControl*</c> fixtures (end of file).</para>
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

    [Fact(DisplayName = "ebilling-r1 124: the Ciam routes that reach the file store are the firm file content route only, and no Ciam route's handler is a NotOnAnExternalRoute caller")]
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
