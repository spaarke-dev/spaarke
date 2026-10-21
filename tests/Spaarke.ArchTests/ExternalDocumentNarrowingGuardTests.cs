using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 188 (D7 T7, #2126): every external document surface applies THE document narrowing
/// (<see cref="ExternalDocumentNarrowing"/>). A source method whose compiled code reads documents for an external caller
/// through one of the surface primitives must also REACH the narrowing, or be listed with its reason.
/// </summary>
/// <remarks>
/// <para><b>Surface primitives</b> (what an external document read is built from): the list query
/// (<see cref="ExternalDataService.BuildDocumentListUrl"/>), the single-document read
/// (<see cref="ExternalDataService.GetDocumentParentAndNameAsync"/>) and its root check
/// (<see cref="ExternalDataService.DocumentScopeRead.IsReadableUnder"/>), a module's row scoping
/// (<c>ExternalModuleDescriptor.ScopeRows</c>,
/// which the documents module's fetch uses), and the assistant tools' document test
/// (<see cref="ExternalCallerCommunicationGuard.IsDocumentTable"/>).</para>
/// <para><b>Narrowing primitives</b>: <see cref="ExternalDocumentNarrowing.DecideAsync"/>,
/// <see cref="ExternalDocumentNarrowing.DecideOrFailClosedAsync"/>, <see cref="ExternalDocumentNarrowing.ReadAndDecideAsync"/>,
/// <see cref="ExternalDataService.NarrowDocumentsAsync"/>, <see cref="ExternalDataService.IsDocumentVisibleAsync"/>,
/// <see cref="ExternalDataService.ReadAndNarrowDocumentsAsync"/>, and a <see cref="DocumentNarrower"/> or
/// <see cref="AppOnlyDocumentNarrowing"/> invocation.</para>
/// <para><b>App-only reads</b> (main-session decision 2026-10-20). The core decision
/// (<see cref="ExternalDocumentNarrowing.DecideAsync"/> and its two siblings) takes its reader as an argument, so it is called
/// only where that reader is the BFF's app-only one: from <see cref="ExternalDocumentNarrowing"/> itself and from
/// <see cref="ExternalDataService"/>, whose only reader is app-only. Every other surface reaches it through those seams
/// (the assistant's tools through <see cref="AppOnlyDocumentNarrowing"/>). <see cref="TheCoreDecision_IsCalledOnlyFromTheAppOnlySeams"/>
/// enforces it; <see cref="CoreDecisionDetector_MustFireAndMustNotFire"/> is its control.</para>
/// <para><b>Reach</b> is transitive over the BFF's own methods: a method reaches the narrowing when its compiled body (its
/// lambdas, local functions and async state machines included, attributed to the method they were written in) references
/// a narrowing primitive or a method that reaches it. So a route handler that reads a document and calls a helper that
/// narrows it passes, and a comment, a string or a <c>nameof</c> does not count.</para>
/// <para><b>Granularity and limits.</b> Per SOURCE METHOD, by name. Reach says the method can get to the narrowing, not that
/// every path through it does (the surface tests in <c>ExternalDocumentNarrowingRouteTests</c> and the AI handler tests
/// pin the paths). A document read that uses none of the primitives (a new hand-written <c>sprk_documents</c> query) is not
/// seen; the list of primitives is the contract a reviewer extends.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the
/// <c>NarrowingGuardControl*</c> fixtures: a document read with only a comment and a <c>nameof</c> of the narrowing is found;
/// one that narrows inside a lambda, or through a helper, is not.</para>
/// </remarks>
public class ExternalDocumentNarrowingGuardTests
{
    /// <summary>Callers of a surface primitive that return no document rows or content, each with its reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotDocumentSurfaces = new Dictionary<string, string>
    {
        [$"{typeof(ExternalMessageEndpoints).FullName}::EvaluateRecordGateAsync"] =
            "Scopes the message's REGARDING record (a project / matter / work assignment row) to gate the message; it returns " +
            "no document row, and the message routes refuse attachments.",
        [$"{typeof(ExternalNotificationFeedEndpoints).FullName}::GetFeedCoreAsync"] =
            "Scopes service-request rows for the notification feed; no documents.",
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "Task 188: every external document surface reaches THE document narrowing (or is listed)")]
    public void EveryExternalDocumentSurface_ReachesTheNarrowing()
    {
        var scan = Scan(BffTypes.Value);

        // Non-vacuity: the surfaces task 188 wired are seen reading documents AND reaching the narrowing.
        foreach (var surface in new[]
                 {
                     $"{typeof(ExternalDataService).FullName}::GetDocumentsAsync",
                     $"{typeof(ExternalProjectDataEndpoints).FullName}::DownloadDocumentForParent",
                     $"{typeof(ExternalProjectDataEndpoints).FullName}::ListDocumentVersionsForParent",
                     $"{typeof(ExternalModuleDataEndpoints).FullName}::ExecuteScopedFetchCoreAsync",
                     "Sprk.Bff.Api.Services.Ai.Handlers.DataverseReadQueryHandler::ExecuteChatAsync",
                     "Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler::ExecuteChatAsync",
                     "Sprk.Bff.Api.Services.Ai.Handlers.DataverseDescribeHandler::DescribeRecordAsync",
                 })
        {
            Assert.True(scan.Surfaces.ContainsKey(surface), $"{surface} should be seen reading documents — the scan lost its signal.");
            Assert.True(scan.Reaches.Contains(surface), $"{surface} should be seen reaching the narrowing — the scan lost its signal.");
        }
        Assert.True(scan.Reaches.Contains("Sprk.Bff.Api.Services.Ai.PublicContracts.RetrievalAccessTrim::TrimByRecordAsync"), "Task 188 round 3 (F1-A): the AI retrieval trim must narrow an external caller's document keys (AppOnlyDocumentNarrowing).");

        var problems = new List<string>();
        foreach (var (method, primitives) in scan.Surfaces.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (scan.Reaches.Contains(method) || NotDocumentSurfaces.ContainsKey(method))
                continue;
            problems.Add($"NEW external document surface without the narrowing: {method} (reads through {string.Join(", ", primitives.Order())})");
        }

        foreach (var listed in NotDocumentSurfaces.Keys)
        {
            if (!scan.Surfaces.ContainsKey(listed))
                problems.Add($"STALE entry {listed}: it no longer uses a document surface primitive — remove it.");
            else if (scan.Reaches.Contains(listed))
                problems.Add($"STALE entry {listed}: it now reaches the narrowing — remove it (the ratchet).");
        }

        Assert.True(problems.Count == 0,
            "An external document surface must apply ExternalDocumentNarrowing after the root rule (task 188, I-14 as amended): " +
            "call ExternalDataService.NarrowDocumentsAsync / IsDocumentVisibleAsync (external plane) or the injected " +
            "AppOnlyDocumentNarrowing (assistant tools), or list the method with its reason:\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188: negative control — a read with only a mention of the narrowing is found; a lambda or a helper that narrows is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(NarrowingGuardControlMentionOnly), typeof(NarrowingGuardControlNarrowsInALambda),
            typeof(NarrowingGuardControlNarrowsThroughAHelper), typeof(NarrowingGuardControlNoDocuments),
        };
        var scan = Scan(fixtures.SelectMany(IlCallScan.WithNested));

        var flagged = scan.Surfaces.Keys.Where(k => !scan.Reaches.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { $"{typeof(NarrowingGuardControlMentionOnly).FullName}::ListAsync" }, flagged);
        Assert.Contains($"{typeof(NarrowingGuardControlNarrowsInALambda).FullName}::ListAsync", scan.Reaches);
        Assert.Contains($"{typeof(NarrowingGuardControlNarrowsThroughAHelper).FullName}::ListAsync", scan.Reaches);
        Assert.DoesNotContain(scan.Surfaces.Keys, k => k.StartsWith(typeof(NarrowingGuardControlNoDocuments).FullName!, StringComparison.Ordinal));
    }

    /// <summary>The types whose reader is the BFF's app-only one: the only callers the core decision may have.</summary>
    private static readonly Type[] AppOnlySeams = { typeof(ExternalDocumentNarrowing), typeof(ExternalDataService) };

    [Fact(DisplayName = "Task 188: THE document narrowing reads app-only — its core decision is called only from the app-only seams")]
    public void TheCoreDecision_IsCalledOnlyFromTheAppOnlySeams()
    {
        var callers = CoreDecisionCallers(BffTypes.Value);

        // Non-vacuity: the two seams are seen calling it.
        Assert.Contains($"{typeof(ExternalDataService).FullName}::{nameof(ExternalDataService.NarrowDocumentsAsync)}", callers);
        Assert.Contains($"{typeof(ExternalDataService).FullName}::{nameof(ExternalDataService.ReadAndNarrowDocumentsAsync)}", callers);

        var offenders = callers.Where(c => !AppOnlySeams.Any(seam => c.StartsWith(seam.FullName + "::", StringComparison.Ordinal))).ToArray();
        Assert.True(offenders.Length == 0,
            "THE document narrowing's own reads must run under the BFF's app-only identity on every surface (main-session " +
            "decision 2026-10-20): call ExternalDataService.NarrowDocumentsAsync / IsDocumentVisibleAsync, or inject " +
            "AppOnlyDocumentNarrowing, instead of handing ExternalDocumentNarrowing a reader of your own:\n" +
            string.Join("\n", offenders));
    }

    [Fact(DisplayName = "Task 188: negative control — a core decision over a reader of its own is found; the app-only seams are not")]
    public void CoreDecisionDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(NarrowingGuardControlDecidesOverItsOwnReader), typeof(NarrowingGuardControlNarrowsThroughTheAppOnlySeam),
            typeof(NarrowingGuardControlNarrowsInALambda),
        };

        var callers = CoreDecisionCallers(fixtures.SelectMany(IlCallScan.WithNested));

        Assert.Equal(new[] { $"{typeof(NarrowingGuardControlDecidesOverItsOwnReader).FullName}::ReadAsync" }, callers);
    }

    /// <summary>Every source method that references a core decision method, as "{outermost type}::{source method}".</summary>
    private static IReadOnlyList<string> CoreDecisionCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(ExternalDocumentNarrowing)
                && r.Target.Name is nameof(ExternalDocumentNarrowing.DecideAsync)
                    or nameof(ExternalDocumentNarrowing.DecideOrFailClosedAsync)
                    or nameof(ExternalDocumentNarrowing.ReadAndDecideAsync))
            .Select(r => SourceKey(r.Caller, r.CallerMethod))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    // ── Task 188 round 3 slice B: the internal plane's document routes (self-contained) ──────────────────────────────

    /// <summary>
    /// The internal-plane document surfaces slice B wired, each of which must REACH the narrowing: the single-document filter
    /// behind all 26 document-id routes, the bulk-download filter, and the two container list handlers.
    /// </summary>
    private static readonly string[] InternalPlaneDocumentSurfaces =
    {
        "Sprk.Bff.Api.Api.Filters.DocumentAuthorizationFilter::InvokeAsync",
        "Sprk.Bff.Api.Api.Filters.BulkDownloadAuthorizationFilter::InvokeAsync",
        "Sprk.Bff.Api.Api.DataverseDocumentsEndpoints::ListDocumentsAsync",
        "Sprk.Bff.Api.Api.DataverseDocumentsEndpoints::ListContainerDocumentsAsync",
    };

    [Fact(DisplayName = "Task 188 r3 B: the internal plane's document filters and container list routes reach THE document narrowing")]
    public void TheInternalPlaneDocumentRoutes_ReachTheNarrowing()
    {
        var types = BffTypes.Value;

        // Non-vacuity: both list handlers are seen calling the app-only container listing.
        var listers = ContainerListCallers(types);
        Assert.Contains("Sprk.Bff.Api.Api.DataverseDocumentsEndpoints::ListDocumentsAsync", listers);
        Assert.Contains("Sprk.Bff.Api.Api.DataverseDocumentsEndpoints::ListContainerDocumentsAsync", listers);

        var problems = InternalPlaneProblems(types, InternalPlaneDocumentSurfaces);
        Assert.True(problems.Count == 0,
            "The internal plane's document routes must apply THE external document narrowing for a caller who is not an " +
            "internal systemuser (task 188 round 3 slice B): call InternalPlaneDocumentNarrowing.ForCallerAsync (or the " +
            "injected AppOnlyDocumentNarrowing):" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "Task 188 r3 B: negative control — a filter or container list that does not narrow is found; one that narrows (directly or through a helper) is not")]
    public void InternalPlaneDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(InternalPlaneControlFilterSkipsTheNarrowing), typeof(InternalPlaneControlFilterNarrowsThroughAHelper),
            typeof(InternalPlaneControlListsWithoutNarrowing), typeof(InternalPlaneControlListsAndNarrows),
        };
        var pins = new[]
        {
            $"{typeof(InternalPlaneControlFilterSkipsTheNarrowing).FullName}::InvokeAsync",
            $"{typeof(InternalPlaneControlFilterNarrowsThroughAHelper).FullName}::InvokeAsync",
        };

        var problems = InternalPlaneProblems(fixtures.SelectMany(IlCallScan.WithNested), pins);

        Assert.Equal(
            new[]
            {
                $"NOT NARROWED: {typeof(InternalPlaneControlFilterSkipsTheNarrowing).FullName}::InvokeAsync",
                $"NOT NARROWED: {typeof(InternalPlaneControlListsWithoutNarrowing).FullName}::ListAsync",
            },
            problems);
    }

    /// <summary>
    /// The pinned surfaces that do not reach the narrowing, plus every source method that lists a container's documents
    /// (<c>IDocumentDataverseService.GetDocumentsByContainerAsync</c>, app-only) without reaching it. A pin that is not
    /// found at all is reported too, so a rename cannot silently empty the rule.
    /// </summary>
    private static List<string> InternalPlaneProblems(IEnumerable<Type> types, IReadOnlyCollection<string> pins)
    {
        var list = types.ToList();
        var scan = Scan(list);
        var known = IlCallScan.MethodReferences(list).Select(r => SourceKey(r.Caller, r.CallerMethod)).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var pin in pins.Concat(ContainerListCallers(list)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!known.Contains(pin))
                problems.Add($"MISSING: {pin} is no longer found — update the pin");
            else if (!scan.Reaches.Contains(pin))
                problems.Add($"NOT NARROWED: {pin}");
        }

        return problems;
    }

    /// <summary>Every source method that calls <c>IDocumentDataverseService.GetDocumentsByContainerAsync</c>.</summary>
    private static IReadOnlyList<string> ContainerListCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(Spaarke.Dataverse.IDocumentDataverseService)
                && r.Target.Name == nameof(Spaarke.Dataverse.IDocumentDataverseService.GetDocumentsByContainerAsync))
            .Select(r => SourceKey(r.Caller, r.CallerMethod))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    // ── Task 188 round 3 slice C: rows that derive from a document (sprk_analysis, sprk_analysisoutput) ──────────────

    /// <summary>The derived-row surfaces slice C wired; each must reach the hide-only overload.</summary>
    private static readonly string[] DerivedRowSurfaces =
    {
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseReadQueryHandler::ExecuteChatAsync",
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler::ExecuteChatAsync",
        // F-C1: the one record by id (an analysis or an analysis output).
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseDescribeHandler::DescribeRecordAsync",
    };

    [Fact(DisplayName = "Task 188 r3 C: every surface that returns rows derived from a document reaches the hide-only overload")]
    public void EveryDocumentDerivedSurface_ReachesTheHideOnlyOverload()
    {
        var scan = Scan(BffTypes.Value, IsDerivedNarrowing, DerivedSurfacePrimitive);

        // Non-vacuity: the two tools are seen testing / reading derived rows AND reaching the overload.
        foreach (var surface in DerivedRowSurfaces)
        {
            Assert.True(scan.Surfaces.ContainsKey(surface), $"{surface} should be seen reading derived rows — the scan lost its signal.");
            Assert.True(scan.Reaches.Contains(surface), $"{surface} should be seen reaching the hide-only overload — the scan lost its signal.");
        }

        var problems = scan.Surfaces.Keys.Where(k => !scan.Reaches.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => $"NEW derived-row surface without the narrowing: {k} (uses {string.Join(", ", scan.Surfaces[k].Order())})")
            .ToList();
        Assert.True(problems.Count == 0,
            "A surface that returns rows derived from a document (an analysis, an analysis output) to an external caller must " +
            "hide those whose source document is hidden or undecidable: read the sources with " +
            "ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync and decide them with " +
            "AppOnlyDocumentNarrowing.HiddenDocumentIdsAsync (task 188 round 3 slice C):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188 r3 C: negative control — a derived-row read that skips the overload is found; one that uses it (through a helper) is not")]
    public void DerivedRowDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(DerivedGuardControlSkipsTheOverload), typeof(DerivedGuardControlNarrowsThroughAHelper),
            typeof(DerivedGuardControlNarrowsOnlyDocuments), typeof(NarrowingGuardControlNoDocuments),
        };
        var scan = Scan(fixtures.SelectMany(IlCallScan.WithNested), IsDerivedNarrowing, DerivedSurfacePrimitive);

        var flagged = scan.Surfaces.Keys.Where(k => !scan.Reaches.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                $"{typeof(DerivedGuardControlNarrowsOnlyDocuments).FullName}::ReadAsync",
                $"{typeof(DerivedGuardControlSkipsTheOverload).FullName}::ReadAsync",
            },
            flagged);
        Assert.Contains($"{typeof(DerivedGuardControlNarrowsThroughAHelper).FullName}::ReadAsync", scan.Reaches);
    }

    private static bool IsDerivedNarrowing(MethodBase method) =>
        method.DeclaringType == typeof(AppOnlyDocumentNarrowingExtensions)
        && method.Name == nameof(AppOnlyDocumentNarrowingExtensions.HiddenDocumentIdsAsync);

    private static string? DerivedSurfacePrimitive(MethodBase method) =>
        method.DeclaringType == typeof(ExternalCallerCommunicationGuard)
        && method.Name is nameof(ExternalCallerCommunicationGuard.IsDocumentDerivedTable)
            or nameof(ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync)
            ? $"ExternalCallerCommunicationGuard.{method.Name}"
            : null;

    // ── K7: the consumers of the app-only narrowing seams are an allow-list ────────────────────────────────────────

    /// <summary>The app-only narrowing seams (CLAUDE.md §6.5 path B, owner round 111: a hide decision only).</summary>
    private static readonly Type[] NarrowingSeams = { typeof(AppOnlyDocumentNarrowing), typeof(InternalRouteDocumentNarrowing) };

    /// <summary>
    /// Every BFF type allowed to hold, resolve, invoke or build an app-only narrowing seam, with its reason. A new consumer
    /// fails <see cref="TheAppOnlyNarrowingSeams_HaveOnlyListedConsumers"/> until it is listed here (and reviewed: each one
    /// must use the seam for a hide decision only and return no app-only data).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedSeamConsumers = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule"] =
            "The ONE registrar: binds both seams to ExternalDataService.ReadAndNarrowDocumentsAsync (app-only).",
        ["Sprk.Bff.Api.Infrastructure.ExternalAccess.AppOnlyDocumentNarrowingExtensions"] =
            "Slice C: the hide-only overload (returns the hidden id set; no rows, no reasons).",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseReadQueryHandler"] =
            "Assistant tool (path B): hides sprk_document rows and rows derived from a document from the user's own read.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler"] =
            "Assistant tool (path B): drops document hits and hits derived from a document from the user's own search.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseDescribeHandler"] =
            "Assistant tool (path B): answers a hidden sprk_document record like not-found.",
        ["Sprk.Bff.Api.Services.Ai.PublicContracts.RetrievalAccessTrim"] =
            "Slice A (F1-A): withholds an external caller's hidden document keys from AI retrieval.",
        ["Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing"] =
            "Slice B: the internal plane's document filter, bulk download and container lists refuse or drop hidden documents.",
        // Round 4b (K7 hardening): direct callers of the app-only reader's narrowing entry points count as consumers.
        ["Sprk.Bff.Api.Api.ExternalAccess.ExternalModuleDataEndpoints"] =
            "External plane (round 1): the documents module fetch drops the rows ExternalDataService.NarrowDocumentsAsync hides.",
        ["Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints"] =
            "External plane (round 1): download and versions refuse a document ExternalDataService.IsDocumentVisibleAsync hides.",
    };

    [Fact(DisplayName = "Task 188 r3 C (K7): the app-only narrowing seams have only listed consumers")]
    public void TheAppOnlyNarrowingSeams_HaveOnlyListedConsumers()
    {
        var consumers = SeamConsumers(BffTypes.Value);

        var problems = consumers.Keys.Where(k => !AllowedSeamConsumers.ContainsKey(k)).Order(StringComparer.Ordinal)
            .Select(k => $"NEW consumer of an app-only narrowing seam: {k} ({string.Join("; ", consumers[k].Order())})")
            .Concat(AllowedSeamConsumers.Keys.Where(k => !consumers.ContainsKey(k)).Order(StringComparer.Ordinal)
                .Select(k => $"STALE entry {k}: it no longer uses a seam — remove it (the ratchet)."))
            .ToList();
        Assert.True(problems.Count == 0,
            "The app-only narrowing seams (AppOnlyDocumentNarrowing, InternalRouteDocumentNarrowing) are the ONE exception to " +
            "the AI tools' User-OBO-only rule (owner round 111, CLAUDE.md §6.5 path B): a hide decision only. A new consumer " +
            "must be reviewed for that and listed with its reason in AllowedSeamConsumers:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188 r3 C (K7): negative control — a type that takes, resolves or builds a seam is found; one that does not is not")]
    public void SeamConsumerDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(SeamControlTakesTheSeam), typeof(SeamControlResolvesTheRouteSeam), typeof(SeamControlBuildsTheSeam),
            typeof(SeamControlDynamicInvokeByTypeToken), typeof(SeamControlCallsTheAppOnlyReaderDirectly),
            typeof(SeamControlResolvesBySeamName), typeof(NarrowingGuardControlNoDocuments), typeof(SeamControlMentionsNoSeam),
        };

        var consumers = SeamConsumers(fixtures.SelectMany(IlCallScan.WithNested));

        Assert.Equal(
            new[]
            {
                typeof(SeamControlBuildsTheSeam).FullName, typeof(SeamControlCallsTheAppOnlyReaderDirectly).FullName,
                typeof(SeamControlDynamicInvokeByTypeToken).FullName, typeof(SeamControlResolvesBySeamName).FullName,
                typeof(SeamControlResolvesTheRouteSeam).FullName, typeof(SeamControlTakesTheSeam).FullName,
            }.Order(StringComparer.Ordinal).ToArray(),
            consumers.Keys.Order(StringComparer.Ordinal).ToArray());

        // Round 4b: each of security pass 2's three bypass shapes is found by its own signal (not by a signature).
        Assert.Contains("type token AppOnlyDocumentNarrowing", consumers[typeof(SeamControlDynamicInvokeByTypeToken).FullName!]);
        Assert.Contains("calls ExternalDataService.ReadAndNarrowDocumentsAsync", consumers[typeof(SeamControlCallsTheAppOnlyReaderDirectly).FullName!]);
        Assert.Contains("names a seam in a string", consumers[typeof(SeamControlResolvesBySeamName).FullName!]);
    }

    /// <summary>
    /// The app-only reader's own narrowing entry points (task 188 round 4b, K7): a type that calls one of them directly
    /// (injecting <see cref="ExternalDataService"/>) decides over the app-only reader exactly as a seam consumer does.
    /// </summary>
    private static readonly string[] AppOnlyReaderNarrowingMembers =
    {
        nameof(ExternalDataService.ReadAndNarrowDocumentsAsync), nameof(ExternalDataService.NarrowDocumentsAsync),
        nameof(ExternalDataService.IsDocumentVisibleAsync),
    };

    /// <summary>A string constant that names a seam type: a reflection lookup (<c>Type.GetType("…Seam")</c>) is a reference.</summary>
    private static bool NamesASeam(string value) =>
        NarrowingSeams.Any(seam => value.Contains(seam.Name, StringComparison.Ordinal));

    /// <summary>
    /// Every outermost type that holds a seam (a field, a parameter, a return), resolves one (a generic method instantiated
    /// over it), invokes or builds one (a method of the delegate type) or calls a method that takes one. Round 4b (K7
    /// hardening, security pass 2): also every type that references a seam as an IL TYPE operand (<c>ldtoken</c> — a
    /// <c>typeof</c> —, <c>castclass</c>, <c>isinst</c>, …), names one in a string constant (a reflection lookup by
    /// name), or calls the app-only reader's narrowing entry points (<see cref="AppOnlyReaderNarrowingMembers"/>) directly.
    /// <see cref="ExternalDataService"/> itself (the reader) and the seams are not consumers.
    /// </summary>
    private static IReadOnlyDictionary<string, HashSet<string>> SeamConsumers(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var list = types.Where(t => !NarrowingSeams.Contains(t) && IlCallScan.Outermost(t) != typeof(ExternalDataService)).ToList();
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(Type type, string how)
        {
            var key = IlCallScan.Outermost(type).FullName!;
            if (!found.TryGetValue(key, out var hows))
                found[key] = hows = new HashSet<string>(StringComparer.Ordinal);
            hows.Add(how);
        }

        foreach (var type in list)
        {
            foreach (var field in type.GetFields(all).Where(f => MentionsSeam(f.FieldType)))
                Add(type, $"field {field.FieldType.Name}");
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.GetParameters().Any(p => MentionsSeam(p.ParameterType)) || (method is MethodInfo m && MentionsSeam(m.ReturnType)))
                    Add(type, "signature");
            }
        }

        foreach (var (caller, _, target) in IlCallScan.MethodReferences(list))
        {
            if (MentionsSeam(target.DeclaringType)
                || (target is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(MentionsSeam))
                || target.GetParameters().Any(p => MentionsSeam(p.ParameterType)))
                Add(caller, $"calls {target.DeclaringType?.Name}.{target.Name}");
            if (target.DeclaringType == typeof(ExternalDataService) && AppOnlyReaderNarrowingMembers.Contains(target.Name))
                Add(caller, $"calls ExternalDataService.{target.Name}");
        }

        foreach (var (caller, _, target) in IlCallScan.TypeReferences(list))
        {
            if (MentionsSeam(target))
                Add(caller, $"type token {target.Name}");
        }

        foreach (var (caller, _, value) in IlCallScan.StringLoads(list))
        {
            if (NamesASeam(value))
                Add(caller, "names a seam in a string");
        }

        return found;
    }

    private static bool MentionsSeam(Type? type) =>
        type is not null
        && (NarrowingSeams.Contains(type)
            || (type.IsGenericType && type.GetGenericArguments().Any(MentionsSeam))
            || (type.HasElementType && MentionsSeam(type.GetElementType())));

    // ── Option A condition 2: the all-visible test double never reaches production ────────────────────────────────

    private const string AllVisibleDouble = "AllVisibleDocumentNarrowing";

    private static bool ReferencesTheAllVisibleDouble(string source) => source.Contains(AllVisibleDouble, StringComparison.Ordinal);

    [Fact(DisplayName = "Task 188 r3 (Option A, condition 2): the all-visible narrowing double is never referenced from src/, and production builds the real seams")]
    public void TheAllVisibleTestDouble_NeverReachesProduction()
    {
        // Non-vacuity: the double still exists under this name in the tests.
        Assert.Contains(SourceScan.TestSourceFiles(), f => ReferencesTheAllVisibleDouble(File.ReadAllText(f)));

        var offenders = SourceScan.ServerSourceFiles().Where(f => ReferencesTheAllVisibleDouble(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(SourceScan.RepoRoot, f)).ToArray();
        Assert.True(offenders.Length == 0,
            $"src/ must never reference the test double {AllVisibleDouble} (it shows every document):\n" + string.Join("\n", offenders));

        var testReferences = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "").Where(n => n.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.True(testReferences.Length == 0, "The BFF must reference no test assembly: " + string.Join(", ", testReferences));

        // The production seams are built in ONE place, over the app-only reader (the unit tests
        // TheAssistantsNarrowing_IsRegistered_OverTheAppOnlyReader and TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader
        // resolve them from AddExternalAccess and see them read app-only and decide).
        var builders = IlCallScan.MethodReferences(BffTypes.Value)
            .Where(r => r.Target is ConstructorInfo && NarrowingSeams.Contains(r.Target.DeclaringType))
            .Select(r => IlCallScan.Outermost(r.Caller).FullName!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule" }, builders);
        Assert.Contains("Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule::AddExternalAccess", Scan(BffTypes.Value).Reaches);
    }

    [Fact(DisplayName = "Task 188 r3 (Option A, condition 2): negative control — a source that registers the double is found; the real registration is not")]
    public void AllVisibleDoubleDetector_MustFireAndMustNotFire()
    {
        Assert.True(ReferencesTheAllVisibleDouble("services.UseAllVisibleDocumentNarrowing();"));
        Assert.True(ReferencesTheAllVisibleDouble("services.AddSingleton(AllVisibleDocumentNarrowing.Instance);"));
        Assert.False(ReferencesTheAllVisibleDouble(
            "services.AddScoped<InternalRouteDocumentNarrowing>(sp => (ids, ct) => sp.GetRequiredService<ExternalDataService>().ReadAndNarrowDocumentsAsync(ids, ct));"));
    }

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ScanResult(IReadOnlyDictionary<string, HashSet<string>> Surfaces, IReadOnlySet<string> Reaches);

    private static ScanResult Scan(IEnumerable<Type> types) => Scan(types, IsNarrowing, SurfacePrimitive);

    private static ScanResult Scan(IEnumerable<Type> types, Func<MethodBase, bool> isNarrowing, Func<MethodBase, string?> surfacePrimitive)
    {
        var surfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reaches = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var source = SourceKey(caller, callerMethod);
            if (isNarrowing(target))
                reaches.Add(source);
            if (surfacePrimitive(target) is { } primitive && !IsPrimitiveOwner(caller))
            {
                if (!surfaces.TryGetValue(source, out var used))
                    surfaces[source] = used = new HashSet<string>(StringComparer.Ordinal);
                used.Add(primitive);
            }

            if (target.DeclaringType is { } declaring && declaring.Assembly == caller.Assembly)
            {
                if (!calls.TryGetValue(source, out var targets))
                    calls[source] = targets = new HashSet<string>(StringComparer.Ordinal);
                targets.Add($"{IlCallScan.Outermost(declaring).FullName}::{Normalize(target.Name)}");
            }
        }

        // Transitive reach over the scanned methods.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (source, targets) in calls)
            {
                if (!reaches.Contains(source) && targets.Any(reaches.Contains))
                    changed = reaches.Add(source) || changed;
            }
        }

        return new ScanResult(surfaces, reaches);
    }

    /// <summary>
    /// "{outermost type}::{source method}": a lambda, local function or async state machine is attributed to the method it
    /// was written in (the compiler's <c>&lt;Method&gt;</c> prefix), so a narrowing call inside a closure counts for it.
    /// </summary>
    private static string SourceKey(Type caller, MethodBase callerMethod)
    {
        var outermost = IlCallScan.Outermost(caller);
        if (IlCallScan.SourceMethod(caller, callerMethod) is { } source)
            return $"{outermost.FullName}::{Normalize(source.Name)}";

        // A closure: the method name (<Method>b__0_0, <Method>g__Local|0_0), or for an async lambda its state machine's
        // type name (<<Method>b__0_0>d).
        var name = callerMethod.Name.StartsWith('<') ? callerMethod.Name : caller.Name;
        return $"{outermost.FullName}::{Normalize(name)}";
    }

    private static readonly Regex CompilerPrefix = new(@"^<+([^<>]+)>", RegexOptions.Compiled);

    private static string Normalize(string name) => CompilerPrefix.Match(name) is { Success: true } m ? m.Groups[1].Value : name;

    private static bool IsNarrowing(MethodBase method) =>
        method.DeclaringType is { } declaring
        && ((declaring == typeof(ExternalDocumentNarrowing)
             && method.Name is nameof(ExternalDocumentNarrowing.DecideAsync)
                 or nameof(ExternalDocumentNarrowing.DecideOrFailClosedAsync)
                 or nameof(ExternalDocumentNarrowing.ReadAndDecideAsync))
            || (declaring == typeof(ExternalDataService)
                && method.Name is nameof(ExternalDataService.NarrowDocumentsAsync) or nameof(ExternalDataService.IsDocumentVisibleAsync)
                    or nameof(ExternalDataService.ReadAndNarrowDocumentsAsync))
            || (declaring == typeof(DocumentNarrower) && method.Name == nameof(DocumentNarrower.Invoke))
            || (declaring == typeof(AppOnlyDocumentNarrowing) && method.Name == nameof(AppOnlyDocumentNarrowing.Invoke))
            || (declaring == typeof(InternalRouteDocumentNarrowing) && method.Name == nameof(InternalRouteDocumentNarrowing.Invoke)));

    private static string? SurfacePrimitive(MethodBase method) => method.DeclaringType switch
    {
        { } t when t == typeof(ExternalDataService) && method.Name is nameof(ExternalDataService.BuildDocumentListUrl)
            or nameof(ExternalDataService.GetDocumentParentAndNameAsync) => $"ExternalDataService.{method.Name}",
        { } t when t == typeof(ExternalDataService.DocumentScopeRead) && method.Name == nameof(ExternalDataService.DocumentScopeRead.IsReadableUnder) =>
            "DocumentScopeRead.IsReadableUnder",
        { } t when t == typeof(ExternalModuleDescriptor) && method.Name == nameof(ExternalModuleDescriptor.ScopeRows) =>
            "ExternalModuleDescriptor.ScopeRows",
        { } t when t == typeof(ExternalCallerCommunicationGuard) && method.Name == nameof(ExternalCallerCommunicationGuard.IsDocumentTable) =>
            "ExternalCallerCommunicationGuard.IsDocumentTable",
        _ => null,
    };

    /// <summary>A primitive's own type uses it internally (an overload calling another); that is not a surface.</summary>
    private static bool IsPrimitiveOwner(Type caller) =>
        IlCallScan.Outermost(caller) is var outermost
        && (outermost == typeof(ExternalModuleDescriptor) || outermost == typeof(ExternalCallerCommunicationGuard)
            || caller == typeof(ExternalDataService.DocumentScopeRead));

    // ── Task 188 round 4a (security pass 2, F1-1): AI content primitives ───────────────────────────────────────────
    // Self-contained: its own primitive list, scope, allow-list, facts and AiContentControl* fixtures (end of file).

    /// <summary>The SPE content downloads (<see cref="Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations"/> and
    /// <see cref="Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore"/>) that hand a document's bytes to their caller.</summary>
    private static readonly string[] AiContentDownloadMembers =
    {
        "DownloadFileAsync", "DownloadFileAsUserAsync", "DownloadFileVersionAsync", "DownloadFileVersionAsUserAsync",
    };

    /// <summary>The AI routes' source namespaces: a download called from here feeds an AI surface.</summary>
    private static readonly string[] AiContentScopes = { "Sprk.Bff.Api.Services.Ai.", "Sprk.Bff.Api.Api.Ai." };

    /// <summary>
    /// AI-side downloads that do not narrow THEMSELVES, each with its reason: the document was decided before the method
    /// runs (by a grant that reaches the narrowing), or the bytes never reach a caller. A new download fails
    /// <see cref="EveryAiDocumentGrantAndAiContentDownload_ReachesTheNarrowing"/> until it narrows or is listed here; an
    /// entry that stops downloading, or starts narrowing, fails it too (the ratchet).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AiContentDownloadsDecidedElsewhere = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Services.Ai.AnalysisDocumentLoader::ExtractDocumentTextAsync"] =
            "Analysis execute: the route's AnalysisAuthorizationFilter (DocumentAccess) decided every body document through " +
            "IAiAuthorizationService.AuthorizeAsync, which narrows, before the loader runs.",
        ["Sprk.Bff.Api.Services.Ai.Chat.DocumentContextService::ExtractDocumentTextAsync"] =
            "Chat document context: AiAuthorizationFilter decided the session's documents (DocumentId, AdditionalDocumentIds, " +
            "an sprk_document host, the compose active document) through IAiAuthorizationService.AuthorizeAsync, which narrows.",
        ["Sprk.Bff.Api.Services.Ai.AppOnlyAnalysisService::AnalyzeDocumentAsync"] =
            "Unattended document profile (the AppOnlyDocumentAnalysis and ProfileSummary jobs, the Office profile worker; no " +
            "caller): the profile fields go to Dataverse, which every read path narrows, never to a caller in this call.",
        ["Sprk.Bff.Api.Services.Ai.AppOnlyAnalysisService::ExtractDocumentTextAsync"] =
            "Unattended document profile / analysis pipeline (no caller): the text goes to Dataverse and the index, which every " +
            "read path narrows (slice A trim, the dataverse.* tools), never to a caller in this call.",
        ["Sprk.Bff.Api.Services.Ai.FileIndexingService::IndexFileAsync"] =
            "Indexing: the bytes become index chunks, never a response; every retrieval of them is trimmed (slice A).",
        ["Sprk.Bff.Api.Services.Ai.FileIndexingService::IndexFileAppOnlyAsync"] =
            "Indexing (app-only): the bytes become index chunks, never a response; every retrieval of them is trimmed (slice A).",
    };

    [Fact(DisplayName = "Task 188 r4a: every AI document grant and every AI-side content download reaches THE document narrowing (or is listed)")]
    public void EveryAiDocumentGrantAndAiContentDownload_ReachesTheNarrowing()
    {
        var types = BffTypes.Value;
        var problems = AiContentProblems(
            types, key => AiContentScopes.Any(s => key.StartsWith(s, StringComparison.Ordinal)), AiContentDownloadsDecidedElsewhere,
            out var grants, out var downloads);

        // Non-vacuity: the one grant implementation, the four sites that grant through it, and the downloads are all seen.
        Assert.Contains("Sprk.Bff.Api.Services.Ai.AiAuthorizationService::AuthorizeAsync", grants);
        Assert.Contains("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints::IngestArchiveDocumentAsync", downloads);
        Assert.Contains("Sprk.Bff.Api.Services.Ai.Chat.DocumentContextService::ExtractDocumentTextAsync", downloads);
        var grantCallers = AiGrantCallers(types);
        foreach (var site in new[]
                 {
                     "Sprk.Bff.Api.Api.Filters.AiAuthorizationFilter::AreChatContextChecksAllowedAsync",
                     "Sprk.Bff.Api.Api.Filters.AnalysisAuthorizationFilter::AuthorizeDocumentAccessAsync",
                     "Sprk.Bff.Api.Api.Ai.RagEndpoints::TrimToReadableDocumentsAsync",
                     "Sprk.Bff.Api.Api.Ai.VisualizationEndpoints::AuthorizeRowsAsync",
                 })
        {
            Assert.True(grantCallers.Contains(site), $"{site} should be seen granting through IAiAuthorizationService.AuthorizeAsync — the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "An AI route grants document access through IAiAuthorizationService.AuthorizeAsync and downloads content through " +
            "ISpeFileOperations / SpeFileStore. Every implementation of the grant, and every AI-side download, must reach THE " +
            "external document narrowing (task 188 round 4a; InternalPlaneDocumentNarrowing.ForCallerAsync), or the download " +
            "must be listed in AiContentDownloadsDecidedElsewhere with its reason:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188 r4a: negative control — a grant or an AI download that skips the narrowing is found; one that narrows (through a helper) is not")]
    public void AiContentDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(AiContentControlGrantSkipsTheNarrowing), typeof(AiContentControlGrantNarrowsThroughAHelper),
            typeof(AiContentControlDownloadSkipsTheNarrowing), typeof(AiContentControlDownloadNarrows),
            typeof(AiContentControlListedDownload),
        };
        var listed = new Dictionary<string, string>
        {
            [$"{typeof(AiContentControlListedDownload).FullName}::ReadAsync"] = "control: decided upstream",
            [$"{typeof(AiContentControlDownloadNarrows).FullName}::ReadAsync"] = "control: a stale entry (it narrows)",
        };

        var problems = AiContentProblems(
            fixtures.SelectMany(IlCallScan.WithNested),
            key => key.StartsWith("Spaarke.ArchTests.AiContentControl", StringComparison.Ordinal), listed, out _, out _);

        Assert.Equal(
            new[]
            {
                $"NOT NARROWED (grant): {typeof(AiContentControlGrantSkipsTheNarrowing).FullName}::AuthorizeAsync",
                $"NOT NARROWED (download): {typeof(AiContentControlDownloadSkipsTheNarrowing).FullName}::ReadAsync",
                $"STALE entry {typeof(AiContentControlDownloadNarrows).FullName}::ReadAsync: it now reaches the narrowing — remove it (the ratchet).",
            },
            problems);
    }

    /// <summary>
    /// The grant implementations (every concrete <see cref="Sprk.Bff.Api.Services.Ai.IAiAuthorizationService"/>'s
    /// <c>AuthorizeAsync</c>) and the in-scope download callers that do not reach the narrowing, plus stale allow-list entries.
    /// </summary>
    private static List<string> AiContentProblems(
        IEnumerable<Type> types, Func<string, bool> downloadScope, IReadOnlyDictionary<string, string> listed,
        out IReadOnlyList<string> grants, out IReadOnlyList<string> downloads)
    {
        var list = types.ToList();
        var scan = Scan(list, IsNarrowing, AiContentPrimitive);
        grants = list
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(Sprk.Bff.Api.Services.Ai.IAiAuthorizationService).IsAssignableFrom(t))
            .Select(t => $"{IlCallScan.Outermost(t).FullName}::{nameof(Sprk.Bff.Api.Services.Ai.IAiAuthorizationService.AuthorizeAsync)}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        downloads = scan.Surfaces.Keys.Where(downloadScope).Order(StringComparer.Ordinal).ToList();

        var problems = new List<string>();
        problems.AddRange(grants.Where(g => !scan.Reaches.Contains(g)).Select(g => $"NOT NARROWED (grant): {g}"));
        problems.AddRange(downloads.Where(d => !scan.Reaches.Contains(d) && !listed.ContainsKey(d)).Select(d => $"NOT NARROWED (download): {d}"));
        foreach (var entry in listed.Keys.Order(StringComparer.Ordinal))
        {
            if (!downloads.Contains(entry))
                problems.Add($"STALE entry {entry}: it no longer downloads content — remove it.");
            else if (scan.Reaches.Contains(entry))
                problems.Add($"STALE entry {entry}: it now reaches the narrowing — remove it (the ratchet).");
        }

        return problems;
    }

    /// <summary>Every source method that calls <see cref="Sprk.Bff.Api.Services.Ai.IAiAuthorizationService.AuthorizeAsync"/>.</summary>
    private static IReadOnlySet<string> AiGrantCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(Sprk.Bff.Api.Services.Ai.IAiAuthorizationService)
                && r.Target.Name == nameof(Sprk.Bff.Api.Services.Ai.IAiAuthorizationService.AuthorizeAsync))
            .Select(r => SourceKey(r.Caller, r.CallerMethod))
            .ToHashSet(StringComparer.Ordinal);

    private static string? AiContentPrimitive(MethodBase method) =>
        method.DeclaringType is { } declaring
        && (declaring == typeof(Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations)
            || declaring == typeof(Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore))
        && AiContentDownloadMembers.Contains(method.Name)
            ? $"{declaring.Name}.{method.Name}"
            : null;

    // ── Task 188 round 4b (security pass 2, F1-2): every document-resource grant goes through THE helper ─────────────
    // Self-contained: its own allow-list, ratchet baseline, facts and DocumentGrantControl* fixtures (end of file).

    /// <summary>
    /// The two places a direct document-resource <c>AuthorizeAsync</c> (<see cref="Spaarke.Core.Auth.AuthorizationService"/> /
    /// <see cref="Spaarke.Core.Auth.IAuthorizationService"/>) belongs: THE helper, which narrows after it, and
    /// <c>DocumentAuthorizationFilter</c>, which narrows in the filter (slice B).
    /// </summary>
    private static readonly string[] DocumentGrantOwners =
    {
        "Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing::AuthorizeDocumentsAsync",
        "Sprk.Bff.Api.Api.Filters.DocumentAuthorizationFilter::InvokeAsync",
    };

    /// <summary>
    /// The ratchet baseline: direct document-resource grants that remain outside the helper, each with its reason. A new
    /// direct grant fails <see cref="EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper"/>; an entry that stops
    /// granting directly fails it too (remove it).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DirectDocumentGrantBaseline = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Api.Filters.BulkDownloadAuthorizationFilter::InvokeAsync"] =
            "Bulk download DROPS rather than refuses: it grants per id, then narrows the whole allowed set in ONE " +
            "InternalPlaneDocumentNarrowing.ForCallerAsync call (slice B; pinned by TheInternalPlaneDocumentRoutes_ReachTheNarrowing " +
            "and DocumentRouteExternalNarrowingTests' bulk cases). The helper's first-deny answer cannot express a partial set.",
        ["Sprk.Bff.Api.Infrastructure.Authorization.ResourceAccessHandler::HandleRequirementAsync"] =
            "The handler of the driveitem.* policies (canpreviewfiles, candownloadfiles, …, AuthorizationModule): no route " +
            "requires any of them (scan 2026-10-21: no RequireAuthorization names one), and its resource id is a container, " +
            "drive or route id. If a route ever requires one on a document id, it must go through the helper.",
    };

    [Fact(DisplayName = "Task 188 r4b (F1-2): every direct document-resource AuthorizeAsync is THE helper, DocumentAuthorizationFilter, or listed")]
    public void EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper()
    {
        var callers = DirectDocumentGrantCallers(BffTypes.Value);

        // Non-vacuity: the helper and the filter are seen granting directly.
        foreach (var owner in DocumentGrantOwners)
            Assert.True(callers.Contains(owner), $"{owner} should be seen calling AuthorizationService.AuthorizeAsync — the scan lost its signal.");

        var problems = DocumentGrantProblems(callers, DocumentGrantOwners, DirectDocumentGrantBaseline);
        Assert.True(problems.Count == 0,
            "A direct document-resource AuthorizeAsync (Spaarke.Core.Auth.AuthorizationService / IAuthorizationService) decides " +
            "on the root grants only. Outside DocumentAuthorizationFilter it must go through " +
            "InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync, which applies THE external document narrowing after it " +
            "(task 188 round 4b, F1-2), or be listed in DirectDocumentGrantBaseline with its reason:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188 r4b (F1-2): negative control — a direct grant (class or interface) is found; one through the helper, or the record path, is not")]
    public void DocumentGrantDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(DocumentGrantControlDirect), typeof(DocumentGrantControlDirectThroughTheInterface),
            typeof(DocumentGrantControlThroughTheHelper), typeof(DocumentGrantControlRecordPath),
            typeof(DocumentGrantControlListed),
        };
        var listed = new Dictionary<string, string>
        {
            [$"{typeof(DocumentGrantControlListed).FullName}::GrantAsync"] = "control: listed",
            [$"{typeof(DocumentGrantControlThroughTheHelper).FullName}::GrantAsync"] = "control: a stale entry (no direct grant)",
        };

        var problems = DocumentGrantProblems(DirectDocumentGrantCallers(fixtures.SelectMany(IlCallScan.WithNested)), Array.Empty<string>(), listed);

        Assert.Equal(
            new[]
            {
                $"DIRECT document grant: {typeof(DocumentGrantControlDirect).FullName}::GrantAsync",
                $"DIRECT document grant: {typeof(DocumentGrantControlDirectThroughTheInterface).FullName}::GrantAsync",
                $"STALE entry {typeof(DocumentGrantControlThroughTheHelper).FullName}::GrantAsync: it no longer grants directly — remove it (the ratchet).",
            },
            problems);
    }

    private static List<string> DocumentGrantProblems(
        IReadOnlySet<string> callers, IReadOnlyCollection<string> owners, IReadOnlyDictionary<string, string> listed)
    {
        var problems = callers.Where(c => !owners.Contains(c) && !listed.ContainsKey(c)).Order(StringComparer.Ordinal)
            .Select(c => $"DIRECT document grant: {c}").ToList();
        problems.AddRange(listed.Keys.Where(k => !callers.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => $"STALE entry {k}: it no longer grants directly — remove it (the ratchet)."));
        return problems;
    }

    /// <summary>Every source method that calls the document-resource <c>AuthorizeAsync</c> of Spaarke.Core's authorization service.</summary>
    private static IReadOnlySet<string> DirectDocumentGrantCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => (r.Target.DeclaringType == typeof(Spaarke.Core.Auth.AuthorizationService)
                         || r.Target.DeclaringType == typeof(Spaarke.Core.Auth.IAuthorizationService))
                        && r.Target.Name == nameof(Spaarke.Core.Auth.IAuthorizationService.AuthorizeAsync))
            .Select(r => SourceKey(r.Caller, r.CallerMethod))
            .ToHashSet(StringComparer.Ordinal);
}

// Compiled control fixtures for ExternalDocumentNarrowingGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the
// scan attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: lists documents, and only MENTIONS the narrowing (this comment says it calls NarrowDocumentsAsync).</summary>
internal sealed class NarrowingGuardControlMentionOnly
{
    internal string ListAsync(Guid parent)
    {
        // ExternalDataService.NarrowDocumentsAsync runs after this — in this comment only.
        var url = ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, parent);
        return url + nameof(ExternalDataService.NarrowDocumentsAsync);
    }
}

/// <summary>MUST NOT FIRE: lists documents, then narrows inside a lambda (attributed to the method it was written in).</summary>
internal sealed class NarrowingGuardControlNarrowsInALambda(ExternalDataService data)
{
    internal async Task<string> ListAsync(CallerPrincipal caller, Guid parent)
    {
        var url = ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, parent);
        Func<Task<DocumentNarrowingResult>> narrow = () => data.NarrowDocumentsAsync(caller, Array.Empty<DocumentNarrowingRow>());
        await narrow();
        return url;
    }
}

/// <summary>MUST NOT FIRE: lists documents and narrows them through a helper of its own.</summary>
internal sealed class NarrowingGuardControlNarrowsThroughAHelper(ExternalDataService data)
{
    internal async Task<string> ListAsync(CallerPrincipal caller, Guid parent)
    {
        var url = ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, parent);
        await NarrowAsync(caller);
        return url;
    }

    private Task<DocumentNarrowingResult> NarrowAsync(CallerPrincipal caller) =>
        data.NarrowDocumentsAsync(caller, Array.Empty<DocumentNarrowingRow>());
}

/// <summary>MUST NOT FIRE: reads nothing through a document primitive.</summary>
internal sealed class NarrowingGuardControlNoDocuments
{
    internal string Name(Guid id) => $"record {id}";
}

/// <summary>MUST FIRE (app-only rule): decides documents over a reader of its own (e.g. the user's client).</summary>
internal sealed class NarrowingGuardControlDecidesOverItsOwnReader
{
    internal Task<DocumentNarrowingResult> ReadAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ExternalDocumentNarrowing.ReadAndDecideAsync(
            ids, (_, _, _) => Task.FromResult<IReadOnlyList<Dictionary<string, System.Text.Json.JsonElement>>>(
                Array.Empty<Dictionary<string, System.Text.Json.JsonElement>>()), ct);
}

/// <summary>MUST NOT FIRE (app-only rule): narrows through the injected app-only seam.</summary>
internal sealed class NarrowingGuardControlNarrowsThroughTheAppOnlySeam(AppOnlyDocumentNarrowing narrow)
{
    internal async Task<bool> IsVisibleAsync(Guid id, CancellationToken ct) => (await narrow(new[] { id }, ct)).IsVisible(id);
}

// Compiled control fixtures for ExternalDocumentNarrowingGuardTests.InternalPlaneDetector_MustFireAndMustNotFire (task 188
// round 3 slice B). Never run; only their IL is read.

/// <summary>MUST FIRE: a filter that authorizes and passes on without the narrowing.</summary>
internal sealed class InternalPlaneControlFilterSkipsTheNarrowing
{
    internal Task<bool> InvokeAsync(Guid id) => Task.FromResult(id != Guid.Empty);
}

/// <summary>MUST NOT FIRE: a filter that narrows through a helper of its own.</summary>
internal sealed class InternalPlaneControlFilterNarrowsThroughAHelper(AppOnlyDocumentNarrowing narrow)
{
    internal async Task<bool> InvokeAsync(Guid id, CancellationToken ct) => await IsShownAsync(id, ct);

    private async Task<bool> IsShownAsync(Guid id, CancellationToken ct) => (await narrow(new[] { id }, ct)).IsVisible(id);
}

/// <summary>MUST FIRE: lists a container's documents app-only and returns them unnarrowed.</summary>
internal sealed class InternalPlaneControlListsWithoutNarrowing(Spaarke.Dataverse.IDocumentDataverseService documents)
{
    internal async Task<int> ListAsync(string containerId) => (await documents.GetDocumentsByContainerAsync(containerId)).Count();
}

/// <summary>MUST NOT FIRE: lists a container's documents, then narrows them in one call.</summary>
internal sealed class InternalPlaneControlListsAndNarrows(
    Spaarke.Dataverse.IDocumentDataverseService documents, AppOnlyDocumentNarrowing narrow)
{
    internal async Task<int> ListAsync(string containerId, CancellationToken ct)
    {
        var rows = (await documents.GetDocumentsByContainerAsync(containerId, ct)).Select(d => Guid.Parse(d.Id)).ToList();
        var narrowing = await narrow(rows, ct);
        return rows.Count(narrowing.IsVisible);
    }
}

// Compiled control fixtures for the slice C facts (task 188 round 3). Never run; only their IL and signatures are read.

/// <summary>MUST FIRE (derived rows): reads an analysis page's source documents and returns the rows unnarrowed.</summary>
internal sealed class DerivedGuardControlSkipsTheOverload(Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient user)
{
    internal async Task<int> ReadAsync(IReadOnlyCollection<Guid> rows, CancellationToken ct) =>
        (await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(user, "sprk_analysis", rows, ct)).Count;
}

/// <summary>MUST FIRE (derived rows): tests the derived table, then narrows only through the whole-result seam call.</summary>
internal sealed class DerivedGuardControlNarrowsOnlyDocuments(AppOnlyDocumentNarrowing narrow)
{
    internal async Task<bool> ReadAsync(string table, Guid id, CancellationToken ct) =>
        ExternalCallerCommunicationGuard.IsDocumentDerivedTable(table) && (await narrow(new[] { id }, ct)).IsVisible(id);
}

/// <summary>MUST NOT FIRE (derived rows): reads the sources and hides through the overload, in a helper.</summary>
internal sealed class DerivedGuardControlNarrowsThroughAHelper(
    Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient user, AppOnlyDocumentNarrowing narrow)
{
    internal async Task<int> ReadAsync(IReadOnlyCollection<Guid> rows, CancellationToken ct)
    {
        var sources = await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(user, "sprk_analysis", rows, ct);
        return (await HiddenAsync(sources.Values.SelectMany(documents => documents), ct)).Count;
    }

    private Task<IReadOnlySet<Guid>> HiddenAsync(IEnumerable<Guid> ids, CancellationToken ct) => narrow.HiddenDocumentIdsAsync(ids, ct);
}

/// <summary>MUST FIRE (K7): takes the assistant's seam.</summary>
internal sealed class SeamControlTakesTheSeam(AppOnlyDocumentNarrowing narrow)
{
    internal Task<DocumentNarrowingResult> RunAsync(Guid id) => narrow(new[] { id }, CancellationToken.None);
}

/// <summary>MUST FIRE (K7): resolves the internal routes' seam from the container, holding no field.</summary>
internal sealed class SeamControlResolvesTheRouteSeam
{
    internal static object Resolve(IServiceProvider services) =>
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<InternalRouteDocumentNarrowing>(services);
}

/// <summary>MUST FIRE (K7): builds a seam of its own.</summary>
internal sealed class SeamControlBuildsTheSeam
{
    internal static object Build() => (AppOnlyDocumentNarrowing)((_, _) => Task.FromResult<DocumentNarrowingResult>(null!));
}

// ── Task 188 round 4a: compiled control fixtures for AiContentDetector_MustFireAndMustNotFire (never run; IL only) ────

/// <summary>MUST FIRE (r4a, grant): an AI document grant on Read alone.</summary>
internal sealed class AiContentControlGrantSkipsTheNarrowing : Sprk.Bff.Api.Services.Ai.IAiAuthorizationService
{
    public Task<Sprk.Bff.Api.Services.Ai.AuthorizationResult> AuthorizeAsync(
        System.Security.Claims.ClaimsPrincipal user, IReadOnlyList<Guid> documentIds,
        Microsoft.AspNetCore.Http.HttpContext httpContext, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sprk.Bff.Api.Services.Ai.AuthorizationResult.Authorized(documentIds));
}

/// <summary>MUST NOT FIRE (r4a, grant): narrows the granted ids through a helper.</summary>
internal sealed class AiContentControlGrantNarrowsThroughAHelper(InternalRouteDocumentNarrowing narrow)
    : Sprk.Bff.Api.Services.Ai.IAiAuthorizationService
{
    public async Task<Sprk.Bff.Api.Services.Ai.AuthorizationResult> AuthorizeAsync(
        System.Security.Claims.ClaimsPrincipal user, IReadOnlyList<Guid> documentIds,
        Microsoft.AspNetCore.Http.HttpContext httpContext, CancellationToken cancellationToken = default) =>
        Sprk.Bff.Api.Services.Ai.AuthorizationResult.Authorized(await VisibleAsync(documentIds));

    private async Task<IReadOnlyList<Guid>> VisibleAsync(IReadOnlyList<Guid> documentIds)
    {
        var decision = await narrow(documentIds, CancellationToken.None);
        return documentIds.Where(decision.IsVisible).ToList();
    }
}

/// <summary>MUST FIRE (r4a, download): downloads a document's bytes for an AI route without the narrowing.</summary>
internal sealed class AiContentControlDownloadSkipsTheNarrowing(Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations files)
{
    internal Task<Stream?> ReadAsync(string driveId, string itemId) => files.DownloadFileAsync(driveId, itemId);
}

/// <summary>MUST NOT FIRE (r4a, download): narrows before it downloads (and so is a STALE entry when listed).</summary>
internal sealed class AiContentControlDownloadNarrows(
    Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations files, InternalRouteDocumentNarrowing narrow)
{
    internal async Task<Stream?> ReadAsync(Guid documentId, string driveId, string itemId) =>
        (await narrow(new[] { documentId }, CancellationToken.None)).IsVisible(documentId)
            ? await files.DownloadFileAsync(driveId, itemId)
            : null;
}

/// <summary>MUST NOT FIRE (r4a, download): does not narrow, but is listed with its reason.</summary>
internal sealed class AiContentControlListedDownload(Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore files)
{
    internal Task<Stream?> ReadAsync(string driveId, string itemId) => files.DownloadFileAsync(driveId, itemId);
}

// ── Task 188 round 4b: compiled control fixtures (never run; IL only) ────────────────────────────────────────────────

/// <summary>MUST FIRE (r4b K7, security pass 2 shape 1): resolves a seam by a <c>typeof</c> and invokes it late-bound.</summary>
internal sealed class SeamControlDynamicInvokeByTypeToken
{
    internal static object? Run(IServiceProvider services, IReadOnlyCollection<Guid> ids) =>
        ((Delegate)services.GetService(typeof(AppOnlyDocumentNarrowing))!).DynamicInvoke(ids, CancellationToken.None);
}

/// <summary>MUST FIRE (r4b K7, shape 2): injects the app-only reader and calls its narrowing directly.</summary>
internal sealed class SeamControlCallsTheAppOnlyReaderDirectly(ExternalDataService data)
{
    internal Task<DocumentNarrowingResult> RunAsync(IReadOnlyCollection<Guid> ids) =>
        data.ReadAndNarrowDocumentsAsync(ids, CancellationToken.None);
}

/// <summary>MUST FIRE (r4b K7, shape 3): resolves a seam by its type NAME (reflection).</summary>
internal sealed class SeamControlResolvesBySeamName
{
    internal static object? Run(IServiceProvider services) =>
        services.GetService(Type.GetType("Sprk.Bff.Api.Infrastructure.ExternalAccess.InternalRouteDocumentNarrowing, Sprk.Bff.Api")!);
}

/// <summary>MUST NOT FIRE (r4b K7): resolves an unrelated service by a <c>typeof</c> and a name, and casts to Delegate.</summary>
internal sealed class SeamControlMentionsNoSeam
{
    internal static object? Run(IServiceProvider services) =>
        ((Delegate?)services.GetService(typeof(Func<int>)))?.DynamicInvoke() ?? services.GetService(Type.GetType("System.String")!);
}

/// <summary>MUST FIRE (r4b F1-2): a document grant on the root grants alone, through the class.</summary>
internal sealed class DocumentGrantControlDirect(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal Task<Spaarke.Core.Auth.AuthorizationResult> GrantAsync(Guid id) =>
        authorization.AuthorizeAsync(new Spaarke.Core.Auth.AuthorizationContext
        {
            UserId = "u", ResourceId = id.ToString(), Operation = "read", UserAccessToken = null,
        });
}

/// <summary>MUST FIRE (r4b F1-2): the same through the interface.</summary>
internal sealed class DocumentGrantControlDirectThroughTheInterface(Spaarke.Core.Auth.IAuthorizationService authorization)
{
    internal Task<Spaarke.Core.Auth.AuthorizationResult> GrantAsync(Guid id) =>
        authorization.AuthorizeAsync(new Spaarke.Core.Auth.AuthorizationContext
        {
            UserId = "u", ResourceId = id.ToString(), Operation = "read", UserAccessToken = null,
        });
}

/// <summary>MUST NOT FIRE (r4b F1-2): grants through THE helper.</summary>
internal sealed class DocumentGrantControlThroughTheHelper(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal Task<Spaarke.Core.Auth.AuthorizationResult> GrantAsync(Microsoft.AspNetCore.Http.HttpContext http, Guid id) =>
        Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync(
            http, authorization, "u", new[] { id }, "read", null, "control", null, CancellationToken.None);
}

/// <summary>MUST NOT FIRE (r4b F1-2): the entity-generic record path is not a document grant.</summary>
internal sealed class DocumentGrantControlRecordPath(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal Task<Spaarke.Dataverse.AccessSnapshot> ReadAsync(Guid id) =>
        authorization.GetCallerRecordAccessAsync("u", "sprk_matters", id, null);
}

/// <summary>MUST NOT FIRE (r4b F1-2): a direct grant listed in the baseline.</summary>
internal sealed class DocumentGrantControlListed(Spaarke.Core.Auth.IAuthorizationService authorization)
{
    internal Task<Spaarke.Core.Auth.AuthorizationResult> GrantAsync(Guid id) =>
        authorization.AuthorizeAsync(new Spaarke.Core.Auth.AuthorizationContext
        {
            UserId = "u", ResourceId = id.ToString(), Operation = "read", UserAccessToken = null,
        });
}
