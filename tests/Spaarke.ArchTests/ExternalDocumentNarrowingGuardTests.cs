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
    internal static string SourceKey(Type caller, MethodBase callerMethod)
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

    // ── Task 188 round 4c (F1): document decisions the F1-2 rule could not see ───────────────────────────────────────
    // Self-contained: its own primitives, ratchet baseline, facts and DocumentDecisionControl* fixtures (end of file).
    // Two more shapes decide a document on the root grants alone: the rights SNAPSHOT
    // (AuthorizationService.GetCallerAccessAsync — a capability answer or a Read test) and a RECORD-path check whose
    // entity set is sprk_documents (GetCallerRecordAccessAsync, a FinanceAuthorizationCheck { Path = Record }, or a
    // FinanceAuthorizationTargets.FromRoute / AddFinanceAuthorizationFilter(operation, entitySet, routeKey) declaration). A
    // method that uses one must reach THE external document narrowing, or be listed with its reason.
    // Limit (stated): a Record-path check is seen when sprk_documents is a LITERAL (a const inlines to one) set in the same
    // initializer, or loaded anywhere in the calling method for the call and declaration shapes; an entity set computed at
    // run time from a map is beyond a static scan (the shared EntityAccessFilter map holds no sprk_document).

    private const string DocumentSetLiteral = "sprk_documents";

    /// <summary>
    /// The ratchet baseline of the two round-4c shapes, each with its reason. A new use fails
    /// <see cref="EveryDocumentSnapshotAndRecordPathDocumentCheck_ReachesTheNarrowing"/>; an entry that stops using a shape,
    /// or starts reaching the narrowing, fails it too (remove it).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DocumentDecisionBaseline = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Api.Finance.FinanceEndpoints::ResolveConfirmTargets"] =
            "record path: finance.link_invoice on the confirmed document is declared AFTER the Document-path finance.confirm " +
            "check on the SAME id in the same declaration; the filter evaluates in order and stops at the first deny, and the " +
            "Document path narrows (round 4b, F1-2), so a hidden document never reaches the Record check.",
    };

    [Fact(DisplayName = "Task 188 r4c (F1): every document rights snapshot and every Record-path check on sprk_documents reaches THE narrowing (or is listed)")]
    public void EveryDocumentSnapshotAndRecordPathDocumentCheck_ReachesTheNarrowing()
    {
        var problems = DocumentDecisionProblems(BffTypes.Value, DocumentDecisionBaseline, out var uses);

        // Non-vacuity: the round-4c snapshot sites, and the round-4d sites (a snapshot, two Record-path checks), are seen
        // using a shape.
        foreach (var site in new[]
                 {
                     "Sprk.Bff.Api.Api.Office.OfficeEndpoints::WithholdCollisionIdentityIfUnauthorizedAsync",
                     "Sprk.Bff.Api.Api.PermissionsEndpoints::GetDocumentPermissionsAsync",
                     "Sprk.Bff.Api.Api.PermissionsEndpoints::GetBatchPermissionsAsync",
                     // Round 4d (#2199, #2200): still a snapshot / a Record-path check on sprk_documents, now narrowed.
                     "Sprk.Bff.Api.Api.Filters.SemanticSearchAuthorizationFilter::AuthorizeDocumentIdsScopeAsync",
                     "Sprk.Bff.Api.Api.Ai.RagEndpoints::SendToIndex",
                     "Sprk.Bff.Api.Api.DataverseDocumentsEndpoints::AuthorizeRefileTargetsAsync",
                 })
        {
            Assert.True(uses.ContainsKey(site), $"{site} should be seen using a document decision shape (a snapshot or a Record-path check on sprk_documents) — the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "A document decided on the root grants alone — AuthorizationService.GetCallerAccessAsync (the rights snapshot), or a " +
            "Record-path check on sprk_documents — must reach THE external document narrowing " +
            "(InternalPlaneDocumentNarrowing.ForCallerAsync / AuthorizeDocumentsAsync; a FinanceAuthorizationCheck on a document " +
            "takes FinanceCheckPath.Document), task 188 round 4c, or be listed in DocumentDecisionBaseline with its reason:\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 188 r4c (F1): negative control — a snapshot or a Record-path document check that skips the narrowing is found; the Document path, a narrowed snapshot and a Record check on another set are not")]
    public void DocumentDecisionDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(DocumentDecisionControlSnapshotSkipsTheNarrowing), typeof(DocumentDecisionControlSnapshotNarrows),
            typeof(DocumentDecisionControlRecordCheckOnADocument), typeof(DocumentDecisionControlRecordAccessOnDocuments),
            typeof(DocumentDecisionControlDocumentPath), typeof(DocumentDecisionControlListed), typeof(DocumentGrantControlRecordPath),
        };
        var listed = new Dictionary<string, string>
        {
            [$"{typeof(DocumentDecisionControlListed).FullName}::ReadAsync"] = "control: listed",
            [$"{typeof(DocumentDecisionControlSnapshotNarrows).FullName}::ReadAsync"] = "control: a stale entry (it narrows)",
        };

        var problems = DocumentDecisionProblems(fixtures.SelectMany(IlCallScan.WithNested), listed, out _);

        Assert.Equal(
            new[]
            {
                $"NOT NARROWED (record path on sprk_documents): {typeof(DocumentDecisionControlRecordAccessOnDocuments).FullName}::ReadAsync",
                $"NOT NARROWED (record path on sprk_documents): {typeof(DocumentDecisionControlRecordCheckOnADocument).FullName}::Declare",
                $"NOT NARROWED (snapshot): {typeof(DocumentDecisionControlSnapshotSkipsTheNarrowing).FullName}::ReadAsync",
                $"STALE entry {typeof(DocumentDecisionControlSnapshotNarrows).FullName}::ReadAsync: it now reaches the narrowing — remove it (the ratchet).",
            },
            problems);
    }

    /// <summary>
    /// The source methods that use a round-4c shape (with the shapes each uses), and the problems: a use that neither
    /// reaches the narrowing nor is listed, and a listed entry that no longer uses a shape or now reaches the narrowing.
    /// </summary>
    private static List<string> DocumentDecisionProblems(
        IEnumerable<Type> types, IReadOnlyDictionary<string, string> listed, out IReadOnlyDictionary<string, HashSet<string>> uses)
    {
        var list = types.ToList();
        var scan = Scan(list, IsNarrowing, DocumentSnapshotPrimitive);
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (key, used) in scan.Surfaces)
            found[key] = new HashSet<string>(used, StringComparer.Ordinal);
        foreach (var key in RecordPathDocumentChecks(list))
        {
            if (!found.TryGetValue(key, out var used))
                found[key] = used = new HashSet<string>(StringComparer.Ordinal);
            used.Add("record path on sprk_documents");
        }

        uses = found;
        var problems = new List<string>();
        foreach (var (key, used) in found)
        {
            if (!scan.Reaches.Contains(key) && !listed.ContainsKey(key))
                problems.AddRange(used.Select(u => $"NOT NARROWED ({u}): {key}"));
        }

        problems.Sort(StringComparer.Ordinal);
        foreach (var entry in listed.Keys.Order(StringComparer.Ordinal))
        {
            if (!found.ContainsKey(entry))
                problems.Add($"STALE entry {entry}: it no longer decides a document this way — remove it.");
            else if (scan.Reaches.Contains(entry))
                problems.Add($"STALE entry {entry}: it now reaches the narrowing — remove it (the ratchet).");
        }

        return problems;
    }

    private static string? DocumentSnapshotPrimitive(MethodBase method) =>
        method.DeclaringType == typeof(Spaarke.Core.Auth.AuthorizationService)
        && method.Name == nameof(Spaarke.Core.Auth.AuthorizationService.GetCallerAccessAsync)
            ? "snapshot"
            : null;

    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// The source methods that ask a Record-path question about <c>sprk_documents</c>: a
    /// <see cref="Sprk.Bff.Api.Api.Filters.FinanceAuthorizationCheck"/> initializer that sets <c>Path</c> to
    /// <see cref="Sprk.Bff.Api.Api.Filters.FinanceCheckPath.Record"/> and <c>EntitySetName</c> to the literal; or a call of
    /// <c>GetCallerRecordAccessAsync</c>, <c>FinanceAuthorizationTargets.FromRoute</c> or the
    /// <c>AddFinanceAuthorizationFilter(operation, entitySet, routeKey)</c> overload in a method that loads the literal.
    /// </summary>
    private static IReadOnlySet<string> RecordPathDocumentChecks(IEnumerable<Type> types)
    {
        var checkType = typeof(Sprk.Bff.Api.Api.Filters.FinanceAuthorizationCheck);
        var record = (int)Sprk.Bff.Api.Api.Filters.FinanceCheckPath.Record;
        var loadsLiteral = new HashSet<string>(StringComparer.Ordinal);
        var callsRecordPath = new HashSet<string>(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(DeclaredMembers).Cast<MethodBase>().Concat(type.GetConstructors(DeclaredMembers)))
            {
                var key = SourceKey(type, method);
                int? path = null;
                var documentSet = false;
                IlCallScan.IlInstruction? previous = null;
                foreach (var instruction in IlCallScan.Instructions(method))
                {
                    if (instruction.OpCode.OperandType == System.Reflection.Emit.OperandType.InlineString)
                    {
                        if (method.Module.ResolveString(instruction.Operand) == DocumentSetLiteral)
                            loadsLiteral.Add(key);
                    }
                    else if (instruction.OpCode.OperandType == System.Reflection.Emit.OperandType.InlineMethod
                             && IlCallScan.Resolve(method, instruction.Operand,
                                 (module, token, ta, ma) => module.ResolveMethod(token, ta, ma)) is { } target)
                    {
                        if (target is ConstructorInfo && target.DeclaringType == checkType)
                        {
                            (path, documentSet) = (null, false);
                        }
                        else if (target.DeclaringType == checkType && target.Name == "set_Path")
                        {
                            path = previous is { } p ? IntConstant(p) : null;
                        }
                        else if (target.DeclaringType == checkType && target.Name == "set_EntitySetName")
                        {
                            documentSet = previous is { } p
                                && p.OpCode.OperandType == System.Reflection.Emit.OperandType.InlineString
                                && method.Module.ResolveString(p.Operand) == DocumentSetLiteral;
                        }
                        else if (IsRecordPathCall(target))
                        {
                            callsRecordPath.Add(key);
                        }

                        if (path == record && documentSet)
                        {
                            found.Add(key);
                            (path, documentSet) = (null, false);
                        }
                    }

                    previous = instruction;
                }
            }
        }

        found.UnionWith(callsRecordPath.Where(loadsLiteral.Contains));
        return found;
    }

    private static bool IsRecordPathCall(MethodBase target) =>
        (target.DeclaringType == typeof(Spaarke.Core.Auth.AuthorizationService)
         && target.Name == nameof(Spaarke.Core.Auth.AuthorizationService.GetCallerRecordAccessAsync))
        || (target.DeclaringType == typeof(Sprk.Bff.Api.Api.Filters.FinanceAuthorizationTargets) && target.Name == "FromRoute")
        || (target.Name == "AddFinanceAuthorizationFilter"
            && target.GetParameters().Count(p => p.ParameterType == typeof(string)) == 3);

    /// <summary>The integer an <c>ldc.i4*</c> pushes, or null for any other instruction.</summary>
    private static int? IntConstant(IlCallScan.IlInstruction instruction)
    {
        var op = instruction.OpCode;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4 || op == System.Reflection.Emit.OpCodes.Ldc_I4_S)
            return instruction.Operand;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4_M1) return -1;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4_0) return 0;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4_1) return 1;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4_2) return 2;
        if (op == System.Reflection.Emit.OpCodes.Ldc_I4_3) return 3;
        return null;
    }

    // ── Task 188 round 4e: every app-only generic update writer asks THE refusal (the D7 link columns) ──────────────
    // Self-contained: its own primitives, list, facts and GenericWriterControl* fixtures (end of file).
    // A document's sprk_parentdocument and sprk_relatedcommunication are visibility inputs of THE narrowing (D7 rules 3
    // and 4), and so are its Access Permission and own-narrowing record (task 189). The generic update
    // (IFieldMappingDataverseService.UpdateRecordFieldsAsync) writes whatever payload its caller built (a playbook, an
    // output mapping, a field-mapping rule), so a type that calls it must also call InheritedAccessPermission.RefuseAppOnlyWrite,
    // or be listed with the reason it can never update an existing document.
    // Granularity (UAC-r2 #2170, option c): per MEMBER — the method that issues the write, keyed "{type}::{member}"
    // (SourceKey: a lambda, local function, async state machine or <>c closure counts for the member it was written in). A
    // refusal in one member no longer covers a write in another; a private writer reached only after its caller's refusal is
    // listed with that reason. Limits (stated): overloads share a key (their name); a generic update built on another
    // primitive is not seen — IGenericEntityService.UpdateAsync (typed fixed-key writers, #2213) and a client of its own
    // (OntologyWriterDataverseClient, the Signal writers).

    /// <summary>
    /// Generic update members that never write a document's D7 link columns or its Access Permission, each with its reason
    /// (code review checks it against the payload the member builds).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> GenericWritersThatUpdateNoDocument = new Dictionary<string, string>
    {
        // ebilling-r1 task 094: 050a's InvoiceDocumentRefile is retired - a Submit Invoice source is copied, never linked or re-filed.
        ["Sprk.Bff.Api.Services.Finance.InvoiceReviewService::MarkDocumentConfirmedAsync"] =
            "Writes BuildReviewStatusFields on sprk_document: the invoice review status, reviewed-on and notes (literal keys).",
        ["Sprk.Bff.Api.Services.Finance.InvoiceReviewService::RejectInvoiceAsync"] =
            "Writes BuildReviewStatusFields on sprk_document: the invoice review status, reviewed-on and notes (literal keys).",
        ["Sprk.Bff.Api.Services.Finance.SignalEvaluationService::UpsertSignalAsync"] = "Writes sprk_spendsignal only (SignalEntity).",
        // ebilling-r1 (D-104): the rollup's write moved into WriteRollupAsync, whose one caller passes BuildRollupFields.
        ["Sprk.Bff.Api.Services.Finance.FinanceRollupService::WriteRollupAsync"] =
            "Writes its own rollup fields (BuildRollupFields, then BuildPendingSpendFields) to sprk_matter or sprk_project only " +
            "(UpdateExistingRecordFieldsAsync).",
        // ebilling-r1 typed finance writers: literal keys on finance tables, never sprk_document.
        ["Sprk.Bff.Api.Services.Finance.Coding.CodeSetProposalService::ConfirmAsync"] =
            "Writes only sprk_DefaultBillingCodeSet@odata.bind (DefaultSetBind) on a matter type or practice area reference row.",
        ["Sprk.Bff.Api.Services.Finance.InvoiceLifecycleService::TransitionAsync"] =
            "Writes only statuscode and statecode on sprk_invoice (InvoiceEntity), conditioned on the row version.",
        ["Sprk.Bff.Api.Services.Finance.InvoiceTotalsService::RecalculateAsync"] =
            "Writes only sprk_amount on an adjustment sprk_billingevent and the five totals columns on sprk_invoice (literal keys).",
        // ebilling-r1 task 103: the approval claim, conditioned on the approval portion's row version.
        ["Sprk.Bff.Api.Services.Finance.InvoiceApprovalService::ApproveHeldAsync"] =
            "Writes only sprk_decidedby, sprk_decidedon and sprk_approvedamount (ClaimFields, literal keys) on sprk_invoiceapproval (ApprovalEntity).",
        // ebilling-r1 task 103 fix round (K2): a refused transition clears the claim it wrote, conditional on the version just read.
        ["Sprk.Bff.Api.Services.Finance.InvoiceApprovalService::ReleaseClaimAsync"] =
            "Clears only sprk_decidedby, sprk_decidedon and sprk_approvedamount (DBNull, literal keys) on sprk_invoiceapproval (ApprovalEntity).",
        // ebilling-r1 task 104: the reject / return claim, conditioned on the approval portion's row version.
        ["Sprk.Bff.Api.Services.Finance.InvoiceRejectReturnService::DecideHeldAsync"] =
            "Writes only sprk_decidedby and sprk_decidedon (ClaimFields, literal keys) on sprk_invoiceapproval (ApprovalEntity).",
        // ebilling-r1 task 104: a refused transition clears the claim it wrote, conditional on the version just read.
        ["Sprk.Bff.Api.Services.Finance.InvoiceRejectReturnService::ReleaseClaimAsync"] =
            "Clears only sprk_decidedby and sprk_decidedon (DBNull, literal keys) on sprk_invoiceapproval (ApprovalEntity).",
        // ebilling-r1 task 150: the AP export claims each invoice for its batch, conditional on the row version read, and releases the claim of a failed batch.
        ["Sprk.Bff.Api.Services.Finance.ApExportService::CreateBatchAsync"] =
            "Writes only sprk_billingexport (BatchLookup, an EntityReference to the batch) on sprk_invoice, a literal key (the claim, conditional on the row version read).",
        ["Sprk.Bff.Api.Services.Finance.ApExportService::ReleaseClaimAsync"] =
            "Clears only sprk_billingexport (BatchLookup, DBNull) on sprk_invoice, a literal key, under the version just read.",
        // ebilling-r1 task 090 (found at the merge of master's #2253 guard): the Stored File store's verified flip. It cannot ask the
        // refusal: sprk_storedfile is itself refused whole (AccessTables, uac-r2 F4), and this is its typed writer.
        ["Sprk.Bff.Api.Services.Finance.Files.StoredFileService::FlipToServableAsync"] =
            "Writes only sprk_filestatus (StoredFileSchema.Status, Servable) on sprk_storedfile, a literal key, under the row's own " +
            "version (UpdateIfRowVersionMatchesAsync); never a document.",
        // ebilling-r1 task 094 (uac-r2 re-check (b)): the submission undo withdraws the invoice's file rows before their blobs go.
        ["Sprk.Bff.Api.Services.Finance.Files.StoredFileService::WithdrawForUndoAsync"] =
            "Writes only sprk_filestatus (StoredFileSchema.Status, Removed) on sprk_storedfile, a literal key, under the row's own " +
            "version (UpdateIfRowVersionMatchesAsync); never a document.",
        ["Sprk.Bff.Api.Services.ScorecardCalculatorService::UpdateEntityGradesAsync"] =
            "Writes its own grade fields to sprk_matter or sprk_project only (UpdateExistingRecordFieldsAsync).",
        ["Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService::AssignOwningTeamAsync"] =
            "Writes only ownerid on sprk_communication (UpdateRecordFieldsIfUnchangedAsync, the routed owning team).",
        // Field-mapping task 017 (2d-5): OwnedChildWrite::CreateAsync and ::CreateIntoIsolationAsync were deleted; the
        // service's create (RecordCreationService::CreateAsync, below) replaced both.
        // uac-r2 task 195: OwnedChildWrite::PatchWithBffManagedPairAsync was removed; its sequence is this core's.
        ["Sprk.Bff.Api.Services.Dataverse.ProjectMatterFiling::FileAsync"] =
            "Writes only the BFF-managed pair columns (sprk_regardingrecordid, sprk_regardingrecordtype) on sprk_project, picked " +
            "out of the body by name and conditioned on the row version read (UpdateRecordFieldsIfUnchangedAsync); every other " +
            "column goes in the caller's own PATCH.",
        // ebilling-r1 (#2081): the handler's one write member is PatchAsync (an invoice or line update-only, through
        // InvoiceFamilyCoreRecordGuard.UpdateExistingAsync); WriteAsync and UpdateWithOptimisticConcurrencyAsync reach it.
        ["Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler::PatchAsync"] =
            "private; reached only through WriteAsync and UpdateWithOptimisticConcurrencyAsync, whose sole caller UpdateAsync refuses first",
        ["Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyCoreRecordGuard::UpdateExistingAsync"] =
            "The invoice family's update-only write primitive: it writes its caller's payload, so it is itself a recognised " +
            "generic update (GenericUpdateNames) and each of its callers is held to the refusal here.",
        ["Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver::PutBackFilingAsync"] =
            "Rollback of this re-file's own failed write (#2143): writes back only the values this row held before the re-file " +
            "(`restore`, read from the row before the write), and only while the row is still filed as the re-file left it " +
            "(row-version conditional; `FiledAsResolved`). It never writes a link value the row did not already hold, so D7's " +
            "inputs return to their prior, authorized state.",
        ["Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver::SettleOwnerAsync"] =
            "Writes only ownerid (the team the row's parents resolve to), conditioned on the row version.",
        ["Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver::ReleaseOwnSecureWriteAsync"] =
            "Writes only ownerid (the ordinary team the fresh parents give), conditioned on the row version.",
        ["Sprk.Bff.Api.Services.RecordCreation.RecordCreationService::CreateAsync"] =
            "Creates only (step 11, a fresh Guid upserted; an isolated root too): a link set in a create can only HIDE the new " +
            "row (D7 narrows, never widens), and no existing document is re-pointed or un-linked.",
        ["Sprk.Bff.Api.Services.Communication.EmailArchiveCommunicationLinkSweepJob::LinkAsync"] =
            "the BFF's own typed link writer: sets a NULL sprk_relatedcommunication to the exactly-one communication whose " +
            "internet message id ordinal-equals the document's recorded key; never clears or re-points; conditional on row " +
            "version (uac-r2 task 190)",
        // #2237: IDocumentDataverseService.UpdateDocumentFieldsAsync is scanned. The AI document-profile writers write
        // DocumentProfileOutputMapper.BuildFields's output, which asks the refusal per key and drops every refused one
        // (DocumentProfileWriters below holds each to that: it must reach BuildFields).
        ["Sprk.Bff.Api.Api.Ai.AnalysisEndpoints::ExecuteDocumentProfilePipelineAsync"] =
            "Writes DocumentProfileOutputMapper.BuildFields's output (via BuildDocumentProfileFields), which drops every key " +
            "the refusal refuses (#2237).",
        ["Sprk.Bff.Api.Services.Ai.AppOnlyAnalysisService::ProfileViaLinearActionAsync"] =
            "Writes DocumentProfileOutputMapper.BuildFields's output, which drops every key the refusal refuses (#2237).",
        ["Sprk.Bff.Api.Services.Ai.PublicContracts.DocumentProfileAi::ProfileDocumentAsUserAsync"] =
            "Writes DocumentProfileOutputMapper.BuildFields's output, which drops every key the refusal refuses (#2237).",
        // #2237: typed UpdateDocumentFieldsAsync writers whose keys are fixed literals, none an access or D7 link column.
        ["Sprk.Bff.Api.Services.Ai.AnalysisResultPersistence::StoreDocumentProfileOutputsAsync"] =
            "Writes DocumentProfileFieldMapper.CreateFieldMapping: only the fixed GetFieldName targets (sprk_filetldr, " +
            "sprk_filesummary, sprk_filekeywords, sprk_documenttype, sprk_entities, sprk_searchprofile); unknown outputs map to none.",
        ["Sprk.Bff.Api.Services.Jobs.Handlers.AttachmentClassificationJobHandler::WriteClassificationToDataverseAsync"] =
            "Writes literal keys only: sprk_classification, sprk_classificationconfidence, sprk_invoicehintsjson, " +
            "sprk_mattersuggestionjson, sprk_mattersuggestedref, sprk_invoicereviewstatus.",
        ["Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceExtractionJobHandler::UpdateInvoiceExtractionStatusAsync"] =
            "Writes the literal key sprk_extractionstatus only.",
    };

    /// <summary>
    /// #2237: the listed members whose reason is "writes DocumentProfileOutputMapper.BuildFields's output", each with the
    /// member that calls BuildFields for it (itself, or the private wrapper it calls — #2239: the writer must be seen
    /// calling that wrapper; <see cref="DocumentProfileWriterProblems"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DocumentProfileWriters = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Api.Ai.AnalysisEndpoints::ExecuteDocumentProfilePipelineAsync"] =
            "Sprk.Bff.Api.Api.Ai.AnalysisEndpoints::BuildDocumentProfileFields",
        ["Sprk.Bff.Api.Services.Ai.AppOnlyAnalysisService::ProfileViaLinearActionAsync"] =
            "Sprk.Bff.Api.Services.Ai.AppOnlyAnalysisService::ProfileViaLinearActionAsync",
        ["Sprk.Bff.Api.Services.Ai.PublicContracts.DocumentProfileAi::ProfileDocumentAsUserAsync"] =
            "Sprk.Bff.Api.Services.Ai.PublicContracts.DocumentProfileAi::ProfileDocumentAsUserAsync",
    };

    [Fact(DisplayName = "Task 188 r4e: every app-only generic update writer asks InheritedAccessPermission.RefuseAppOnlyWrite (or is listed)")]
    public void EveryGenericUpdateWriter_AsksTheAppOnlyRefusal()
    {
        var scan = GenericWriterScan(BffTypes.Value);

        // Non-vacuity (must not fire): the members that write a payload they did not build are seen writing AND refusing
        // in the same member — Repair's write member by its own call, not by an entry (#2170 option c).
        foreach (var writer in new[]
                 {
                     "Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore::UpdateAsync",
                     "Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints::ApplyMappingsToChildRecordsAsync",
                     "Sprk.Bff.Api.Services.RecordCreation.RecordCreationService::RepairAsync",
                 })
        {
            Assert.True(scan.TryGetValue(writer, out var use) && use.Writes,
                $"{writer} should be seen calling the generic update — the scan lost its signal.");
            Assert.True(use!.Refuses, $"{writer} should be seen calling RefuseAppOnlyWrite — the scan lost its signal.");
        }

        // DataverseUpdateHandler refuses in UpdateAsync and writes in its private PatchAsync (listed with that reason;
        // ebilling-r1 #2081 moved the write there from WriteAsync, and an invoice or line goes through the invoice-family
        // update, which is a recognised generic update itself).
        Assert.True(scan.TryGetValue("Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler::UpdateAsync", out var handler)
                    && handler.Refuses, "DataverseUpdateHandler.UpdateAsync should be seen calling RefuseAppOnlyWrite.");
        Assert.True(scan.TryGetValue("Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler::PatchAsync", out var write)
                    && write.Writes, "DataverseUpdateHandler.PatchAsync should be seen calling the generic update.");

        // The widened set (#2170 option c): the row-version update is seen.
        Assert.True(scan.TryGetValue("Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver::SettleOwnerAsync", out var settle)
                    && settle.Writes, "UpdateIfRowVersionMatchesAsync should be seen in RecordOwnershipResolver.SettleOwnerAsync.");

        // #2237: the profile mapper asks the refusal, and each writer listed for writing its output still reaches it (the
        // wizard through its private wrapper, which it must itself call — #2239 — and which reaches nothing else that writes).
        Assert.True(scan.TryGetValue("Sprk.Bff.Api.Services.Ai.DocumentProfileOutputMapper::BuildFields", out var mapper)
                    && mapper.Refuses, "DocumentProfileOutputMapper.BuildFields should be seen calling RefuseAppOnlyWrite.");
        var profileWriterProblems = DocumentProfileWriterProblems(BffTypes.Value, scan, DocumentProfileWriters);
        Assert.True(profileWriterProblems.Count == 0, string.Join("\n", profileWriterProblems));

        var problems = GenericWriterProblems(scan, GenericWritersThatUpdateNoDocument);
        Assert.True(problems.Count == 0,
            "A member that calls a generic update (IFieldMappingDataverseService.UpdateRecordFieldsAsync, UpdateExistingRecordFieldsAsync " +
            "or UpdateRecordFieldsIfUnchangedAsync; IGenericEntityService.UpdateIfRowVersionMatchesAsync; " +
            "IDocumentDataverseService.UpdateDocumentFieldsAsync, #2237; InvoiceFamilyCoreRecordGuard.UpdateExistingAsync) writes a payload it may " +
            "not have built. It must ask InheritedAccessPermission.RefuseAppOnlyWrite ITSELF before the write, so a document's D7 " +
            "link columns (sprk_parentdocument, sprk_relatedcommunication) and its Access Permission are never written by it " +
            "(task 188 round 4e, task 189 K5, #2170), or be listed as Type::Member in GenericWritersThatUpdateNoDocument with the " +
            "reason its payload never holds one:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2170 K1: DataverseUpdateHandler's two listed private writers are reached only after UpdateAsync's refusal")]
    public void DataverseUpdateHandlersPrivateWriters_AreReachedOnlyFromUpdateAsync()
    {
        var handler = typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler);
        SortedSet<string> ReferencersOf(string member) => new(
            IlCallScan.MethodReferences(BffTypes.Value)
                .Where(r => r.Target.DeclaringType == handler && r.Target.Name == member)
                .Select(r => SourceKey(r.Caller, r.CallerMethod)),
            StringComparer.Ordinal);

        Assert.Equal(new[] { $"{handler.FullName}::UpdateAsync" }, ReferencersOf("WriteAsync"));
        Assert.Equal(new[] { $"{handler.FullName}::WriteAsync" }, ReferencersOf("UpdateWithOptimisticConcurrencyAsync"));
        // ebilling-r1 #2081: the write member, reached only through the two above.
        Assert.Equal(
            new[] { $"{handler.FullName}::UpdateWithOptimisticConcurrencyAsync", $"{handler.FullName}::WriteAsync" },
            ReferencersOf("PatchAsync"));
    }

    [Fact(DisplayName = "UAC review K1 (on e371dce6bf): FinanceRollupService.WriteRollupAsync, listed as writing only its own rollup fields, has the one production caller that passes them")]
    public void FinanceRollupWriteRollupAsync_IsReachedOnlyFromRecalculateForEntityAsync()
    {
        // WriteRollupAsync takes the parent table and the counted fields as parameters, and stays internal for its unit test
        // (FinanceRollupPendingSpendWriteTests). Its GenericWritersThatUpdateNoDocument reason holds only while its one BFF caller
        // passes BuildRollupFields to sprk_matter or sprk_project; a second caller fails here.
        var rollup = typeof(Sprk.Bff.Api.Services.Finance.FinanceRollupService);
        var referencers = new SortedSet<string>(
            IlCallScan.MethodReferences(BffTypes.Value)
                .Where(r => r.Target.DeclaringType == rollup && r.Target.Name == "WriteRollupAsync")
                .Select(r => SourceKey(r.Caller, r.CallerMethod)),
            StringComparer.Ordinal);

        Assert.Equal(new[] { $"{rollup.FullName}::RecalculateForEntityAsync" }, referencers);
    }

    [Fact(DisplayName = "Task 188 r4e / #2170 / #2237: negative control — per member: a write beside a refusing member, a mention only, the concrete service and an unasked document field update are found; a refusal in a lambda, a listed member, a typed update and an asked document field update are not; stale member entries are reported")]
    public void GenericWriterDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(GenericWriterControlMentionOnly), typeof(GenericWriterControlThroughTheConcreteService),
            typeof(GenericWriterControlRefusesInALambda), typeof(GenericWriterControlListed),
            typeof(GenericWriterControlTypedUpdate), typeof(GenericWriterControlListedButRefuses),
            typeof(GenericWriterControlTwoMembers), typeof(GenericWriterControlDocumentFieldsUnasked),
            typeof(GenericWriterControlDocumentFieldsThroughTheComposite), typeof(GenericWriterControlDocumentFieldsAsked),
            typeof(GenericWriterControlThroughTheInvoiceFamilyUpdate),
        };
        static string Member(Type type, string member) => $"{type.FullName}::{member}";
        var listed = new Dictionary<string, string>
        {
            [Member(typeof(GenericWriterControlListed), "WriteAsync")] = "control: listed",
            [Member(typeof(GenericWriterControlListed), "ReadAsync")] = "control: a stale entry (the member no longer writes)",
            [Member(typeof(GenericWriterControlListedButRefuses), "WriteAsync")] = "control: a stale entry (it refuses)",
            ["Spaarke.ArchTests.GenericWriterControlGone::WriteAsync"] = "control: a stale entry (no such writer)",
        };

        var scan = GenericWriterScan(fixtures.SelectMany(IlCallScan.WithNested));
        var problems = GenericWriterProblems(scan, listed);

        Assert.Equal(
            new[]
            {
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlDocumentFieldsThroughTheComposite), "WriteAsync")}",
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlDocumentFieldsUnasked), "WriteAsync")}",
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlMentionOnly), "WriteAsync")}",
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlThroughTheConcreteService), "WriteAsync")}",
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlThroughTheInvoiceFamilyUpdate), "WriteAsync")}",
                $"NEW generic update writer without the refusal: {Member(typeof(GenericWriterControlTwoMembers), "WritesWithoutRefusingAsync")}",
                "STALE entry Spaarke.ArchTests.GenericWriterControlGone::WriteAsync: it no longer calls the generic update — remove it.",
                $"STALE entry {Member(typeof(GenericWriterControlListed), "ReadAsync")}: it no longer calls the generic update — remove it.",
                $"STALE entry {Member(typeof(GenericWriterControlListedButRefuses), "WriteAsync")}: it now asks the refusal — remove it (the ratchet).",
            },
            problems);

        // Must not fire: the two-member fixture's refusing member (the lambda's refusal counts for the member it is in).
        Assert.True(scan[Member(typeof(GenericWriterControlTwoMembers), "RefusesThenWritesAsync")] is { Writes: true, Refuses: true });
        Assert.True(scan[Member(typeof(GenericWriterControlRefusesInALambda), "WriteAsync")] is { Writes: true, Refuses: true });
        // #2237 must not fire: a sprk_document field update that asks the refusal first.
        Assert.True(scan[Member(typeof(GenericWriterControlDocumentFieldsAsked), "WriteAsync")] is { Writes: true, Refuses: true });
    }

    [Fact(DisplayName = "#2239: negative control — a listed profile writer that does not call its BuildFields wrapper is found; one that writes its wrapper's output is not")]
    public void DocumentProfileWriterCheck_MustFireAndMustNotFire()
    {
        static string Member(Type type, string member) => $"{type.FullName}::{member}";
        var orphaned = typeof(DocumentProfileWriterControlOrphanedWrapper);
        var through = typeof(DocumentProfileWriterControlThroughItsWrapper);
        var types = new[] { orphaned, through }.SelectMany(IlCallScan.WithNested).ToList();
        var writers = new Dictionary<string, string>
        {
            [Member(orphaned, "WriteAsync")] = Member(orphaned, "BuildPayload"),
            [Member(through, "WriteAsync")] = Member(through, "BuildPayload"),
        };

        var problems = DocumentProfileWriterProblems(types, GenericWriterScan(types), writers);

        Assert.Equal(new[] { WrapperNotCalled(Member(orphaned, "WriteAsync"), Member(orphaned, "BuildPayload")) }, problems);
    }

    /// <summary>
    /// #2237 / #2239: each <see cref="DocumentProfileWriters"/> entry still holds — the writer calls UpdateDocumentFieldsAsync,
    /// its mapper caller calls <c>DocumentProfileOutputMapper.BuildFields</c>, and, when that caller is a wrapper, the writer
    /// calls the wrapper (a writer that built its own dictionary and orphaned the wrapper would otherwise keep the exemption).
    /// </summary>
    private static List<string> DocumentProfileWriterProblems(
        IEnumerable<Type> types, IReadOnlyDictionary<string, GenericWriterUse> scan, IReadOnlyDictionary<string, string> writers)
    {
        var references = IlCallScan.MethodReferences(types).ToList();
        var buildFieldsCallers = references
            .Where(r => r.Target.DeclaringType == typeof(Sprk.Bff.Api.Services.Ai.DocumentProfileOutputMapper)
                        && r.Target.Name == nameof(Sprk.Bff.Api.Services.Ai.DocumentProfileOutputMapper.BuildFields))
            .Select(r => SourceKey(r.Caller, r.CallerMethod))
            .ToHashSet(StringComparer.Ordinal);
        // Caller member -> called member, both as "{outermost type}::{member}".
        var calls = references
            .Where(r => r.Target.DeclaringType is not null)
            .Select(r => (From: SourceKey(r.Caller, r.CallerMethod),
                          To: $"{IlCallScan.Outermost(r.Target.DeclaringType!).FullName}::{r.Target.Name}"))
            .ToHashSet();

        var problems = new List<string>();
        foreach (var (writer, mapperCaller) in writers.OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            if (!scan.TryGetValue(writer, out var use) || !use.Writes)
                problems.Add($"{writer} should be seen calling UpdateDocumentFieldsAsync — the scan lost its signal (or remove its entry).");
            if (!buildFieldsCallers.Contains(mapperCaller))
                problems.Add($"{writer} is listed for writing DocumentProfileOutputMapper.BuildFields's output, but {mapperCaller} " +
                             "does not call BuildFields: the reason no longer holds (#2237).");
            if (writer != mapperCaller && !calls.Contains((writer, mapperCaller)))
                problems.Add(WrapperNotCalled(writer, mapperCaller));
        }

        return problems;
    }

    private static string WrapperNotCalled(string writer, string wrapper) =>
        $"{writer} is listed for writing DocumentProfileOutputMapper.BuildFields's output through {wrapper}, but it does not " +
        $"call {wrapper}: what it writes may be a payload it built itself, so the reason no longer holds (#2239).";

    private sealed class GenericWriterUse
    {
        public bool Writes { get; set; }
        public bool Refuses { get; set; }
    }

    /// <summary>Outermost type full name → whether its compiled code calls the generic update, and the refusal.</summary>
    private static Dictionary<string, GenericWriterUse> GenericWriterScan(IEnumerable<Type> types)
    {
        var uses = new Dictionary<string, GenericWriterUse>(StringComparer.Ordinal);
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var owner = IlCallScan.Outermost(caller);
            var writes = IsGenericUpdate(target) && target.DeclaringType != owner;
            var refuses = target.DeclaringType == typeof(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission)
                && target.Name == nameof(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite);
            if (!writes && !refuses)
                continue;

            // The member that issues the write (#2170 option c): a lambda, local function, async state machine or <>c
            // closure is attributed to the member it was written in.
            var key = SourceKey(caller, callerMethod);
            if (!uses.TryGetValue(key, out var use))
                uses[key] = use = new GenericWriterUse();
            use.Writes |= writes;
            use.Refuses |= refuses;
        }

        return uses;
    }

    // The generic app-only updates of an existing row (re-check K2), each with the service it is matched on: each writes
    // whatever payload its caller built. #2170 option (c) adds IGenericEntityService.UpdateIfRowVersionMatchesAsync; its
    // UpdateAsync is NOT scanned (typed fixed-key writers, #2213). #2237 adds IDocumentDataverseService.UpdateDocumentFieldsAsync
    // (it writes any key it is given on sprk_document, app-only).
    private static readonly IReadOnlyDictionary<string, Type> GenericUpdateNames = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [nameof(Spaarke.Dataverse.IFieldMappingDataverseService.UpdateRecordFieldsAsync)] = typeof(Spaarke.Dataverse.IFieldMappingDataverseService),
        [nameof(Spaarke.Dataverse.IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync)] = typeof(Spaarke.Dataverse.IFieldMappingDataverseService),
        [nameof(Spaarke.Dataverse.IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync)] = typeof(Spaarke.Dataverse.IFieldMappingDataverseService),
        [nameof(Spaarke.Dataverse.IGenericEntityService.UpdateIfRowVersionMatchesAsync)] = typeof(Spaarke.Dataverse.IGenericEntityService),
        // #2237: the sprk_document field update; the AI document-profile mapper's output is written through it.
        [nameof(Spaarke.Dataverse.IDocumentDataverseService.UpdateDocumentFieldsAsync)] = typeof(Spaarke.Dataverse.IDocumentDataverseService),
        // ebilling-r1 (#2081): the invoice family's update-only write, which writes whatever payload its caller hands it
        // (DataverseUpdateHandler, the Update Record core and the field-mapping push write an invoice or line through it).
        [nameof(Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyCoreRecordGuard.UpdateExistingAsync)] =
            typeof(Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyCoreRecordGuard),
    };

    private static bool IsGenericUpdate(MethodBase method) =>
        GenericUpdateNames.TryGetValue(method.Name, out var service)
        && method.DeclaringType is { } declaring
        && service.IsAssignableFrom(declaring);

    private static List<string> GenericWriterProblems(
        IReadOnlyDictionary<string, GenericWriterUse> scan, IReadOnlyDictionary<string, string> listed)
    {
        var problems = scan.Where(s => s.Value.Writes && !s.Value.Refuses && !listed.ContainsKey(s.Key))
            .Select(s => $"NEW generic update writer without the refusal: {s.Key}")
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var entry in listed.Keys.Order(StringComparer.Ordinal))
        {
            if (!scan.TryGetValue(entry, out var use) || !use.Writes)
                problems.Add($"STALE entry {entry}: it no longer calls the generic update — remove it.");
            else if (use.Refuses)
                problems.Add($"STALE entry {entry}: it now asks the refusal — remove it (the ratchet).");
        }

        return problems;
    }
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

/// <summary>MUST FIRE (r4c F1): a capability / Read answer from the rights snapshot alone.</summary>
internal sealed class DocumentDecisionControlSnapshotSkipsTheNarrowing(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal async Task<bool> ReadAsync(Guid id) =>
        (await authorization.GetCallerAccessAsync("u", id.ToString(), "t")).AccessRights.HasFlag(Spaarke.Dataverse.AccessRights.Read);
}

/// <summary>MUST NOT FIRE (r4c F1): the snapshot, then THE narrowing (the control lists it as a stale entry).</summary>
internal sealed class DocumentDecisionControlSnapshotNarrows(
    Spaarke.Core.Auth.AuthorizationService authorization, InternalRouteDocumentNarrowing narrow)
{
    internal async Task<bool> ReadAsync(Guid id) =>
        (await authorization.GetCallerAccessAsync("u", id.ToString(), "t")).AccessRights.HasFlag(Spaarke.Dataverse.AccessRights.Read)
        && (await narrow(new HashSet<Guid> { id }, CancellationToken.None)).IsVisible(id);
}

/// <summary>MUST FIRE (r4c F1): a FinanceAuthorizationCheck on the Record path whose entity set is sprk_documents.</summary>
internal static class DocumentDecisionControlRecordCheckOnADocument
{
    internal static Sprk.Bff.Api.Api.Filters.FinanceAuthorizationCheck Declare(Guid id) => new()
    {
        Path = Sprk.Bff.Api.Api.Filters.FinanceCheckPath.Record,
        EntitySetName = Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.DocumentEntitySet,
        RecordId = id,
        Operation = "read",
        Source = "control",
    };
}

/// <summary>MUST FIRE (r4c F1): the entity-generic record read, asked of sprk_documents.</summary>
internal sealed class DocumentDecisionControlRecordAccessOnDocuments(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal Task<Spaarke.Dataverse.AccessSnapshot> ReadAsync(Guid id) =>
        authorization.GetCallerRecordAccessAsync("u", "sprk_documents", id, "t");
}

/// <summary>MUST NOT FIRE (r4c F1): a document on the Document path beside a Record-path check on another set (the
/// round-4c workspace summary's shape).</summary>
internal static class DocumentDecisionControlDocumentPath
{
    internal static Sprk.Bff.Api.Api.Filters.FinanceAuthorizationCheck Declare(Guid id, bool document) => document
        ? new()
        {
            Path = Sprk.Bff.Api.Api.Filters.FinanceCheckPath.Document,
            EntitySetName = Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.DocumentEntitySet,
            RecordId = id,
            Operation = "read",
            Source = "control",
        }
        : new()
        {
            Path = Sprk.Bff.Api.Api.Filters.FinanceCheckPath.Record,
            EntitySetName = Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.MatterEntitySet,
            RecordId = id,
            Operation = "read",
            Source = "control",
        };
}

/// <summary>MUST NOT FIRE (r4c F1): a snapshot listed in the baseline.</summary>
internal sealed class DocumentDecisionControlListed(Spaarke.Core.Auth.AuthorizationService authorization)
{
    internal async Task<bool> ReadAsync(Guid id) =>
        (await authorization.GetCallerAccessAsync("u", id.ToString(), "t")).AccessRights.HasFlag(Spaarke.Dataverse.AccessRights.Read);
}

// ── Task 188 round 4e: compiled control fixtures for GenericWriterDetector_MustFireAndMustNotFire (never run; IL only) ──

/// <summary>MUST FIRE (r4e): a generic update that only MENTIONS the refusal (this comment says it calls RefuseAppOnlyWrite).</summary>
internal sealed class GenericWriterControlMentionOnly(Spaarke.Dataverse.IFieldMappingDataverseService writes)
{
    internal Task<string> WriteAsync(string table, Guid id, Dictionary<string, object?> payload) =>
        writes.UpdateRecordFieldsAsync(table, id, payload, CancellationToken.None)
            .ContinueWith(_ => nameof(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite));
}

/// <summary>MUST FIRE (r4e): the same write through the concrete app-only service, not the interface.</summary>
internal sealed class GenericWriterControlThroughTheConcreteService(Spaarke.Dataverse.DataverseWebApiService writes)
{
    internal Task WriteAsync(string table, Guid id, Dictionary<string, object?> payload) =>
        writes.UpdateRecordFieldsAsync(table, id, payload, CancellationToken.None);
}

/// <summary>MUST NOT FIRE (r4e): asks the refusal inside a lambda (attributed to the member it is written in), then writes.</summary>
internal sealed class GenericWriterControlRefusesInALambda(Spaarke.Dataverse.IFieldMappingDataverseService writes)
{
    internal async Task WriteAsync(string table, Guid id, Dictionary<string, object?> payload)
    {
        Func<bool> refused = () => Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite(table, payload.Keys) is not null;
        if (!refused())
            await writes.UpdateRecordFieldsAsync(table, id, payload, CancellationToken.None);
    }
}

/// <summary>MUST NOT FIRE (r4e): a generic update listed with its reason. Its ReadAsync writes nothing (#2170: an entry
/// naming it is stale).</summary>
internal sealed class GenericWriterControlListed(Spaarke.Dataverse.IFieldMappingDataverseService writes)
{
    internal Task WriteAsync(Guid id) =>
        writes.UpdateRecordFieldsAsync("sprk_spendsignal", id, new Dictionary<string, object?> { ["sprk_name"] = "x" }, CancellationToken.None);

    internal Task<Dictionary<string, object?>> ReadAsync(Guid id) =>
        writes.RetrieveRecordFieldsAsync("sprk_spendsignal", id, new[] { "sprk_name" }, CancellationToken.None);
}

/// <summary>
/// #2170 option (c): one member refuses then writes, the other writes (the row-version update) without refusing. MUST FIRE
/// on WritesWithoutRefusingAsync only — the per-type scan let the first member's refusal cover it.
/// </summary>
internal sealed class GenericWriterControlTwoMembers(
    Spaarke.Dataverse.IFieldMappingDataverseService writes, Spaarke.Dataverse.IGenericEntityService entities)
{
    internal async Task RefusesThenWritesAsync(string table, Guid id, Dictionary<string, object?> payload)
    {
        if (Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite(table, payload.Keys) is null)
            await writes.UpdateRecordFieldsAsync(table, id, payload, CancellationToken.None);
    }

    internal async Task WritesWithoutRefusingAsync(string table, Guid id, string version, Dictionary<string, object> payload) =>
        await entities.UpdateIfRowVersionMatchesAsync(table, id, version, payload, CancellationToken.None);
}

/// <summary>MUST NOT FIRE (r4e): listed, but it asks the refusal (the control lists it as a stale entry).</summary>
internal sealed class GenericWriterControlListedButRefuses(Spaarke.Dataverse.IFieldMappingDataverseService writes)
{
    internal Task WriteAsync(string table, Guid id, Dictionary<string, object?> payload) =>
        Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite(table, payload.Keys) is null
            ? writes.UpdateRecordFieldsAsync(table, id, payload, CancellationToken.None)
            : Task.CompletedTask;
}

/// <summary>MUST FIRE (#2237): writes a payload it did not build to sprk_document through UpdateDocumentFieldsAsync.</summary>
internal sealed class GenericWriterControlDocumentFieldsUnasked(Spaarke.Dataverse.IDocumentDataverseService documents)
{
    internal Task WriteAsync(Guid id, Dictionary<string, object?> payload) =>
        documents.UpdateDocumentFieldsAsync(id.ToString(), payload, CancellationToken.None);
}

/// <summary>MUST FIRE (#2237): the same write through the composite IDataverseService.</summary>
internal sealed class GenericWriterControlDocumentFieldsThroughTheComposite(Spaarke.Dataverse.IDataverseService dataverse)
{
    internal Task WriteAsync(Guid id, Dictionary<string, object?> payload) =>
        dataverse.UpdateDocumentFieldsAsync(id.ToString(), payload, CancellationToken.None);
}

/// <summary>MUST NOT FIRE (#2237): asks the refusal for sprk_document before UpdateDocumentFieldsAsync.</summary>
internal sealed class GenericWriterControlDocumentFieldsAsked(Spaarke.Dataverse.IDocumentDataverseService documents)
{
    internal Task WriteAsync(Guid id, Dictionary<string, object?> payload) =>
        Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", payload.Keys) is null
            ? documents.UpdateDocumentFieldsAsync(id.ToString(), payload, CancellationToken.None)
            : Task.CompletedTask;
}

/// <summary>MUST FIRE (ebilling-r1 #2081): a payload it did not build, written through the invoice family's update-only write.</summary>
internal sealed class GenericWriterControlThroughTheInvoiceFamilyUpdate(Spaarke.Dataverse.IFieldMappingDataverseService writes)
{
    internal Task<string?> WriteAsync(string table, Guid id, Dictionary<string, object?> payload) =>
        Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
            writes, table, id, payload, impersonateSystemUserId: null, notFoundAsReadFailed: false, CancellationToken.None);
}

/// <summary>MUST NOT FIRE (r4e): a typed update with literal keys, not the generic update.</summary>
internal sealed class GenericWriterControlTypedUpdate(Spaarke.Dataverse.IGenericEntityService entities)
{
    internal Task WriteAsync(Guid id) =>
        entities.UpdateAsync("sprk_document", id, new Dictionary<string, object> { ["sprk_documentdescription"] = "x" }, CancellationToken.None);
}

// ── #2239: compiled control fixtures for DocumentProfileWriterCheck_MustFireAndMustNotFire (never run; IL only) ──

/// <summary>
/// MUST FIRE (#2239): listed as writing BuildFields's output through BuildPayload, but WriteAsync builds its own dictionary
/// and never calls BuildPayload (which still calls BuildFields).
/// </summary>
internal sealed class DocumentProfileWriterControlOrphanedWrapper(Spaarke.Dataverse.IDocumentDataverseService documents)
{
    internal Task WriteAsync(Guid id, System.Text.Json.JsonElement output) =>
        documents.UpdateDocumentFieldsAsync(
            id.ToString(), new Dictionary<string, object?> { ["sprk_filesummary"] = output.GetRawText() }, CancellationToken.None);

    internal static Dictionary<string, object?> BuildPayload(Guid id, System.Text.Json.JsonElement output) =>
        Sprk.Bff.Api.Services.Ai.DocumentProfileOutputMapper.BuildFields(
            id, output, "control.pdf", null, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
}

/// <summary>MUST NOT FIRE (#2239): WriteAsync writes what its wrapper BuildPayload returns (BuildPayload calls BuildFields).</summary>
internal sealed class DocumentProfileWriterControlThroughItsWrapper(Spaarke.Dataverse.IDocumentDataverseService documents)
{
    internal Task WriteAsync(Guid id, System.Text.Json.JsonElement output) =>
        documents.UpdateDocumentFieldsAsync(id.ToString(), BuildPayload(id, output), CancellationToken.None);

    internal static Dictionary<string, object?> BuildPayload(Guid id, System.Text.Json.JsonElement output) =>
        Sprk.Bff.Api.Services.Ai.DocumentProfileOutputMapper.BuildFields(
            id, output, "control.pdf", null, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
}
