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

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ScanResult(IReadOnlyDictionary<string, HashSet<string>> Surfaces, IReadOnlySet<string> Reaches);

    private static ScanResult Scan(IEnumerable<Type> types)
    {
        var surfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reaches = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var source = SourceKey(caller, callerMethod);
            if (IsNarrowing(target))
                reaches.Add(source);
            if (SurfacePrimitive(target) is { } primitive && !IsPrimitiveOwner(caller))
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
            || (declaring == typeof(AppOnlyDocumentNarrowing) && method.Name == nameof(AppOnlyDocumentNarrowing.Invoke)));

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
