using Microsoft.AspNetCore.Http;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ONE DETACHED-RUN HELPER (spaarkeai-mcp-bff-api-r2 task 030; ADR-052 amendment 2026-11-26, condition 2; owner decision
/// D-030-run-model).
///
/// <para>A <see cref="DefaultHttpContext"/> built by production code is a request context that no request owns: work that runs on it
/// acts as whoever its principal and bearer header say, outside the request's lifetime. The amendment allows exactly one such helper,
/// <c>Sprk.Bff.Api.Services.Ai.PlaybookRuns.DetachedPlaybookRunner</c>, which carries the bounds the amendment requires (memory-only token,
/// no ExecutionContext flow into the request's accessor, a cap, shutdown, fail-closed auth). The three existing builders are a ratchet
/// baseline (Compose's best-effort profile, the Decision route cores' scratch context, the assign-task notifier): they may stay, and they
/// leave the list when they move onto the helper (#3224). A new builder anywhere fails; so does a baseline entry that no longer builds
/// one (the list only shrinks).</para>
///
/// <para>The rule reads the COMPILED code of every production assembly under <c>src/server</c>
/// (<see cref="RouteAuthorizationGuardTests.ServerProductionAssemblies"/>) and finds every <c>newobj</c> of
/// <c>Microsoft.AspNetCore.Http.DefaultHttpContext::.ctor</c>, however it is written (object initializer, target-typed <c>new()</c>,
/// lambda, local function, async state machine): each is an ordinary method body in IL. The owner of a use is the outermost type of the
/// method that holds it.</para>
///
/// <para><b>Two more scans enforce the rest of condition 2</b> (unified-access-control-r2 review, F2), each with its own ratchet baseline
/// and must-fire / must-not-fire controls:</para>
/// <list type="bullet">
/// <item><b>(a) Accessor setters</b>: a <c>call</c> / <c>callvirt</c> of <c>set_HttpContext</c> on <see cref="IHttpContextAccessor"/> or
/// <see cref="HttpContextAccessor"/>. Allowed: the helper. Baseline: <c>ComposeProfileDispatcher</c>.</item>
/// <item><b>(b) Authorization-header readers</b>: a <c>call</c> / <c>callvirt</c> of <c>IHeaderDictionary.get_Authorization</c>, or a
/// body that loads the header's name (<c>ldstr "Authorization"</c>, any case, or <c>ldsfld HeaderNames.Authorization</c>) and touches
/// the request's headers (<c>HttpRequest.get_Headers</c> or any <c>IHeaderDictionary</c> member). Allowed: the OBO token-acquisition
/// code (<c>TokenHelper</c>, the shared reader the OBO factories call, and the factories themselves) and the helper. Baseline:
/// <c>ComposeProfileDispatcher</c>, <c>AuthorizationModule</c>. IL, not source text: the compiler has already resolved the receiver's
/// type, so the incoming <c>IHeaderDictionary.Authorization</c> is told apart from an outgoing <c>HttpRequestMessage</c>'s
/// <c>Headers.Authorization</c> (the BFF sets that one in dozens of places, and a text scan sees the same words), and a name hidden
/// behind a <c>const</c> or a folded string is still one <c>ldstr</c>.</item>
/// </list>
///
/// <para><b>Limits, stated exactly</b>: a context built through reflection, <c>Activator</c>, a factory such as
/// <c>IHttpContextFactory.Create</c>, or a subclass of <see cref="HttpContext"/> or of <see cref="DefaultHttpContext"/> (its constructor
/// is the subclass's, not <see cref="DefaultHttpContext"/>'s) is not seen; the rule reads only <see cref="DefaultHttpContext"/>'s
/// constructor. An accessor written through reflection, or a custom <see cref="IHttpContextAccessor"/> implementation that keeps its own
/// holder, is not seen. A header read is not seen when the name and the header access sit in different methods (a helper taking the
/// name as an argument), through reflection, or through <c>TokenHelper</c> itself: a caller of <c>TokenHelper</c> receives the token and
/// is not scanned. A write of the incoming header BY NAME (<c>Headers["Authorization"] = …</c>) counts as a read; a typed write
/// (<c>Headers.Authorization = …</c>) does not. Test assemblies are not scanned.</para>
/// </summary>
public class DetachedHttpContextGuardTests
{
    private const string DefaultHttpContextType = "Microsoft.AspNetCore.Http.DefaultHttpContext";

    /// <summary>The one helper the amendment allows.</summary>
    private const string Helper = "Sprk.Bff.Api.Services.Ai.PlaybookRuns.DetachedPlaybookRunner";

    /// <summary>The ratchet baseline at task 030: builders that existed before the amendment. Remove an entry when it moves onto the helper.</summary>
    private static readonly IReadOnlySet<string> Baseline = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Compose.ComposeProfileDispatcher",       // best-effort profile after a Compose save (#3224 moves it)
        "Sprk.Bff.Api.Services.Signals.Actions.DecisionRouteCores",      // a scratch context inside the caller's own request
        "Sprk.Bff.Api.Services.Signals.AssignTaskNotifier",              // the assign-task notice (r1 ISS-010 file; not edited here)
    };

    /// <summary>The outermost type of each method that builds a <see cref="DefaultHttpContext"/>, with "file method" for the message.</summary>
    internal static List<(string Owner, string Where)> Builders(IEnumerable<IlCallScan.FileUse> uses) =>
        uses.Where(u => u.OpCode == "newobj" && u.Target is { Type: DefaultHttpContextType, Member: ".ctor" })
            .Select(u => (Owner: OutermostType(u.Method), Where: $"{u.File} {u.Method}"))
            .Distinct()
            .ToList();

    /// <summary>"Ns.Outer+<Inner>d__3::MoveNext" → "Ns.Outer".</summary>
    private static string OutermostType(string method)
    {
        var type = method[..method.IndexOf("::", StringComparison.Ordinal)];
        var nested = type.IndexOf('+');
        return nested < 0 ? type : type[..nested];
    }

    [Fact(DisplayName = "Task 030: only DetachedPlaybookRunner (plus the shrinking baseline) builds a DefaultHttpContext in production code")]
    public void OnlyTheHelperBuildsADetachedHttpContext()
    {
        var assemblies = RouteAuthorizationGuardTests.ServerProductionAssemblies();
        var builders = assemblies.SelectMany(a => Builders(IlCallScan.FileUses(a.Path))).ToList();

        var violations = builders.Where(b => b.Owner != Helper && !Baseline.Contains(b.Owner)).Select(b => b.Where).ToList();
        Assert.True(
            violations.Count == 0,
            "A production DefaultHttpContext is a request context no request owns (ADR-052 amendment 2026-11-26, condition 2). Run detached "
            + "caller work through IPlaybookRunAi / DetachedPlaybookRunner, or bring the change to the owner as an ADR-052 amendment.\n\n  "
            + string.Join("\n  ", violations));

        var stale = Baseline.Where(owner => builders.All(b => b.Owner != owner)).ToList();
        Assert.True(
            stale.Count == 0,
            "These baseline entries no longer build a DefaultHttpContext; remove them from DetachedHttpContextGuardTests.Baseline so the "
            + "list only shrinks:\n  " + string.Join("\n  ", stale));

        // Non-vacuous: the BFF was scanned and the helper itself was found.
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Bff.Api");
        Assert.Contains(builders, b => b.Owner == Helper);
    }

    // Compiled fixtures for the controls: real DefaultHttpContext builds, compiled into THIS assembly (never a scanned one).
    internal static class Fixtures
    {
        internal static HttpContext Direct() => new DefaultHttpContext();

        internal static HttpContext WithInitializer(IServiceProvider services) => new DefaultHttpContext { RequestServices = services };

        internal static HttpContext TargetTyped()
        {
            DefaultHttpContext context = new();
            return context;
        }

        internal static Func<HttpContext> InALambda() => () => new DefaultHttpContext();

        internal static async Task<HttpContext> InAnAsyncMethod()
        {
            await Task.Yield();
            return new DefaultHttpContext();
        }

        internal static string? ReadsAnExistingContext(HttpContext context) => context.TraceIdentifier;

        internal static HttpContextAccessor BuildsAnAccessorOnly() => new();
    }

    [Fact(DisplayName = "Task 030 controls: each compiled DefaultHttpContext build is found; reading a context or building an accessor is not")]
    public void Controls_EachBuildIsFound_AndNothingElse()
    {
        var uses = IlCallScan.FileUses(typeof(DetachedHttpContextGuardTests).Assembly.Location);
        var fixture = typeof(Fixtures).FullName!;
        List<(string Owner, string Where)> Of(string member) =>
            Builders(uses.Where(u => u.Method.StartsWith(fixture, StringComparison.Ordinal) && u.Method.Contains(member, StringComparison.Ordinal)));

        // Must fire.
        Assert.NotEmpty(Of("Direct"));
        Assert.NotEmpty(Of("WithInitializer"));
        Assert.NotEmpty(Of("TargetTyped"));
        Assert.NotEmpty(Of("InALambda"));             // the lambda body is "<InALambda>b__…" in the display class
        Assert.NotEmpty(Of("InAnAsyncMethod"));       // the state machine "<InAnAsyncMethod>d__…::MoveNext"
        Assert.All(Of("InAnAsyncMethod"), b => Assert.Equal(typeof(DetachedHttpContextGuardTests).FullName, b.Owner));

        // Must not fire.
        Assert.Empty(Of("ReadsAnExistingContext"));
        Assert.Empty(Of("BuildsAnAccessorOnly"));
    }

    // ---- (a) setting IHttpContextAccessor.HttpContext (uac-r2 review F2) -----------------------------------------------------------

    /// <summary>The accessor types whose <c>HttpContext</c> setter is the detach (the holder <see cref="IHttpContextAccessor"/> reads).</summary>
    private static readonly IReadOnlySet<string> AccessorTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Http.IHttpContextAccessor",
        "Microsoft.AspNetCore.Http.HttpContextAccessor",
    };

    /// <summary>
    /// The ratchet baseline for accessor setters at the uac-r2 review: code that set <c>IHttpContextAccessor.HttpContext</c> before the
    /// amendment. Remove an entry when it moves onto the helper or stops setting it.
    /// </summary>
    private static readonly IReadOnlySet<string> AccessorSetterBaseline = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Compose.ComposeProfileDispatcher",       // the detached Compose profile (#3224 moves it onto the helper)
    };

    /// <summary>The outermost type of each method that sets <c>HttpContext</c> on an accessor (<c>call</c> / <c>callvirt</c> of the setter).</summary>
    internal static List<(string Owner, string Where)> AccessorSetters(IEnumerable<IlCallScan.FileUse> uses) =>
        uses.Where(u => u.OpCode is "call" or "callvirt"
                        && u.Target is { Member: "set_HttpContext" } target && AccessorTypes.Contains(target.Type))
            .Select(u => (Owner: OutermostType(u.Method), Where: $"{u.File} {u.Method}"))
            .Distinct()
            .ToList();

    [Fact(DisplayName = "Task 030: only DetachedPlaybookRunner (plus the shrinking baseline) sets IHttpContextAccessor.HttpContext in production code")]
    public void OnlyTheHelperSetsTheAccessorsHttpContext()
    {
        var assemblies = RouteAuthorizationGuardTests.ServerProductionAssemblies();
        var setters = assemblies.SelectMany(a => AccessorSetters(IlCallScan.FileUses(a.Path))).ToList();

        var violations = setters.Where(s => s.Owner != Helper && !AccessorSetterBaseline.Contains(s.Owner)).Select(s => s.Where).ToList();
        Assert.True(
            violations.Count == 0,
            "Setting IHttpContextAccessor.HttpContext swaps the request context other code reads as 'the caller' (ADR-052 amendment "
            + "2026-11-26, condition 2; #3224 is what it does to the request that set it). Run detached caller work through IPlaybookRunAi / "
            + "DetachedPlaybookRunner, or bring the change to the owner as an ADR-052 amendment.\n\n  " + string.Join("\n  ", violations));

        var stale = AccessorSetterBaseline.Where(owner => setters.All(s => s.Owner != owner)).ToList();
        Assert.True(
            stale.Count == 0,
            "These baseline entries no longer set IHttpContextAccessor.HttpContext; remove them from "
            + "DetachedHttpContextGuardTests.AccessorSetterBaseline so the list only shrinks:\n  " + string.Join("\n  ", stale));

        Assert.Contains(setters, s => s.Owner == Helper);
    }

    // ---- (b) reading the caller's Authorization header (uac-r2 review F2) -----------------------------------------------------------

    /// <summary>The incoming request's header dictionary types (ASP.NET Core), never an outgoing <c>HttpRequestMessage</c>'s headers.</summary>
    private static readonly IReadOnlySet<string> RequestHeaderTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Http.IHeaderDictionary",
        "Microsoft.AspNetCore.Http.HeaderDictionary",
    };

    private const string HeaderNamesAuthorization = "Microsoft.Net.Http.Headers.HeaderNames::Authorization";

    /// <summary>
    /// Where the caller's bearer token may be read: the token-acquisition (OBO) code and the one helper. <c>TokenHelper</c> is the shared
    /// reader the OBO factories call; the others are listed so a later direct read there is not a violation.
    /// </summary>
    private static readonly IReadOnlySet<string> AuthorizationHeaderAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Helper,
        "Sprk.Bff.Api.Infrastructure.Auth.TokenHelper",
        "Sprk.Bff.Api.Infrastructure.Graph.GraphClientFactory",
        "Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClient",
        "Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe",
        "Spaarke.Dataverse.DataverseAccessDataSource",
    };

    /// <summary>
    /// The ratchet baseline for direct Authorization-header readers at the uac-r2 review, outside the allow-list. Remove an entry when it
    /// stops reading the header (or moves onto <c>TokenHelper</c> / the helper).
    /// </summary>
    private static readonly IReadOnlySet<string> AuthorizationHeaderBaseline = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Compose.ComposeProfileDispatcher",       // copies the header onto its detached context (#3224 moves it)
        "Sprk.Bff.Api.Infrastructure.DI.AuthorizationModule",           // JwtBearer OnAuthenticationFailed: classifies a refused token, logs no claim
    };

    /// <summary>
    /// The outermost type of each method that reads the incoming request's <c>Authorization</c> header: a <c>call</c> / <c>callvirt</c> of
    /// <c>IHeaderDictionary.get_Authorization</c> (the typed property), or a body that loads the name (<c>ldstr "Authorization"</c>, any
    /// case, or <c>ldsfld HeaderNames.Authorization</c>) AND touches the request's headers (<c>HttpRequest.get_Headers</c> or any
    /// <c>IHeaderDictionary</c> member): the <c>Headers["Authorization"]</c> / <c>TryGetValue(HeaderNames.Authorization, …)</c> forms.
    /// </summary>
    internal static List<(string Owner, string Where)> AuthorizationHeaderReaders(IEnumerable<IlCallScan.FileUse> uses, IEnumerable<IlCallScan.FileLoad> loads)
    {
        var useList = uses.ToList();
        var typed = useList
            .Where(u => u.OpCode is "call" or "callvirt" && u.Target is { Member: "get_Authorization" } t && RequestHeaderTypes.Contains(t.Type))
            .Select(u => (u.File, u.Method));

        var touchesHeaders = useList
            .Where(u => u.Target is { } t
                        && ((t.Type == "Microsoft.AspNetCore.Http.HttpRequest" && t.Member == "get_Headers") || RequestHeaderTypes.Contains(t.Type)))
            .Select(u => (u.File, u.Method))
            .ToHashSet();
        var byName = loads
            .Where(l => (l.OpCode == "ldstr" && string.Equals(l.Value, "Authorization", StringComparison.OrdinalIgnoreCase))
                        || (l.OpCode == "ldsfld" && l.Value == HeaderNamesAuthorization))
            .Select(l => (l.File, l.Method))
            .Where(touchesHeaders.Contains);

        return typed.Concat(byName)
            .Select(m => (Owner: OutermostType(m.Method), Where: $"{m.File} {m.Method}"))
            .Distinct()
            .ToList();
    }

    [Fact(DisplayName = "Task 030: only the OBO token-acquisition code and DetachedPlaybookRunner (plus the shrinking baseline) read the caller's Authorization header")]
    public void OnlyTokenAcquisitionAndTheHelperReadTheAuthorizationHeader()
    {
        var assemblies = RouteAuthorizationGuardTests.ServerProductionAssemblies();
        var readers = assemblies.SelectMany(a => AuthorizationHeaderReaders(IlCallScan.FileUses(a.Path), IlCallScan.FileLoads(a.Path))).ToList();

        var violations = readers
            .Where(r => !AuthorizationHeaderAllowed.Contains(r.Owner) && !AuthorizationHeaderBaseline.Contains(r.Owner))
            .Select(r => r.Where)
            .ToList();
        Assert.True(
            violations.Count == 0,
            "Reading the caller's Authorization header is how a bearer token leaves its request (ADR-052 amendment 2026-11-26, conditions 2 "
            + "and 3). Get delegated access through the OBO factories (IGraphClientFactory, IDataverseUserClient), which read it through "
            + "TokenHelper; run detached caller work through IPlaybookRunAi / DetachedPlaybookRunner; or bring the change to the owner.\n\n  "
            + string.Join("\n  ", violations));

        var stale = AuthorizationHeaderBaseline.Where(owner => readers.All(r => r.Owner != owner)).ToList();
        Assert.True(
            stale.Count == 0,
            "These baseline entries no longer read the Authorization header; remove them from "
            + "DetachedHttpContextGuardTests.AuthorizationHeaderBaseline so the list only shrinks:\n  " + string.Join("\n  ", stale));

        Assert.Contains(readers, r => r.Owner == Helper);
        Assert.Contains(readers, r => r.Owner == "Sprk.Bff.Api.Infrastructure.Auth.TokenHelper");
    }

    // Compiled fixtures for the accessor and header controls (THIS assembly, never a scanned one).
    internal static class AccessorAndHeaderFixtures
    {
        internal static void SetsThroughTheInterface(IHttpContextAccessor accessor, HttpContext context) => accessor.HttpContext = context;

        internal static void SetsThroughTheClass(HttpContext context) => new HttpContextAccessor().HttpContext = context;

        internal static Action<IHttpContextAccessor> SetsInALambda() => a => a.HttpContext = null;

        internal static HttpContext? ReadsTheAccessorOnly(IHttpContextAccessor accessor) => accessor.HttpContext;

        internal static string ReadsTheTypedProperty(HttpContext context) => context.Request.Headers.Authorization.ToString();

        internal static string ReadsByLiteralName(HttpContext context) => context.Request.Headers["Authorization"].ToString();

        internal static string ReadsByHeaderNames(HttpContext context) =>
            context.Request.Headers[Microsoft.Net.Http.Headers.HeaderNames.Authorization].ToString();

        internal static bool ReadsByTryGetValue(HttpRequest request) => request.Headers.TryGetValue("authorization", out _);

        internal static async Task<string> ReadsInAnAsyncMethod(HttpContext context)
        {
            await Task.Yield();
            return context.Request.Headers.Authorization.ToString();
        }

        internal static void WritesTheTypedProperty(HttpContext context, string bearer) => context.Request.Headers.Authorization = bearer;

        internal static string? ReadsAnOutgoingRequestsHeader(HttpRequestMessage request) => request.Headers.Authorization?.Parameter;

        internal static string ReadsAnotherHeader(HttpContext context) => context.Request.Headers["X-Correlation-Id"].ToString();

        internal static string NamesTheHeaderWithoutReadingIt() => "Authorization";
    }

    [Fact(DisplayName = "Task 030 controls: each accessor setter and each Authorization-header read form is found; a read of the accessor, a header write, an outgoing header, another header or the bare name is not")]
    public void Controls_AccessorSetters_AndAuthorizationHeaderReads()
    {
        var path = typeof(DetachedHttpContextGuardTests).Assembly.Location;
        var fixture = typeof(AccessorAndHeaderFixtures).FullName!;
        var uses = IlCallScan.FileUses(path).Where(u => u.Method.StartsWith(fixture, StringComparison.Ordinal)).ToList();
        var loads = IlCallScan.FileLoads(path).Where(l => l.Method.StartsWith(fixture, StringComparison.Ordinal)).ToList();
        List<(string Owner, string Where)> Setters(string member) =>
            AccessorSetters(uses.Where(u => u.Method.Contains(member, StringComparison.Ordinal)));
        List<(string Owner, string Where)> Readers(string member) =>
            AuthorizationHeaderReaders(
                uses.Where(u => u.Method.Contains(member, StringComparison.Ordinal)),
                loads.Where(l => l.Method.Contains(member, StringComparison.Ordinal)));

        // (a) must fire / must not fire.
        Assert.NotEmpty(Setters("SetsThroughTheInterface"));
        Assert.NotEmpty(Setters("SetsThroughTheClass"));
        Assert.NotEmpty(Setters("SetsInALambda"));
        Assert.Empty(Setters("ReadsTheAccessorOnly"));

        // (b) must fire.
        Assert.NotEmpty(Readers("ReadsTheTypedProperty"));
        Assert.NotEmpty(Readers("ReadsByLiteralName"));
        Assert.NotEmpty(Readers("ReadsByHeaderNames"));
        Assert.NotEmpty(Readers("ReadsByTryGetValue"));
        Assert.NotEmpty(Readers("ReadsInAnAsyncMethod"));
        Assert.All(Readers("ReadsInAnAsyncMethod"), r => Assert.Equal(typeof(DetachedHttpContextGuardTests).FullName, r.Owner));

        // (b) must not fire.
        Assert.Empty(Readers("WritesTheTypedProperty"));
        Assert.Empty(Readers("ReadsAnOutgoingRequestsHeader"));
        Assert.Empty(Readers("ReadsAnotherHeader"));
        Assert.Empty(Readers("NamesTheHeaderWithoutReadingIt"));
    }
}
