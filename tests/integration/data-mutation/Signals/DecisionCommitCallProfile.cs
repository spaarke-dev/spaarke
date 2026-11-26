using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ISS-010 (#3184): every Dataverse call one decision commit makes, counted at the module boundaries the harness fakes
/// (<c>IGenericEntityService</c>, <c>IFieldMappingDataverseService</c>, the share service, the ontology writer's
/// <c>IOrganizationServiceAsync2</c>, the caller's <c>IDataverseUserClient</c> and the caller-access probe's wire), with the
/// production method that made each call, the decision step it ran in, and an optional fixed latency per call.
/// </summary>
/// <remarks>
/// The live ServiceClient is a private field of <c>DataverseServiceClientImpl</c>, so an SDK call is named by the interface
/// method that issues it (one method is one SDK request: <c>RetrieveAsync</c> is <c>Retrieve</c>, <c>RetrieveMultipleAsync</c>
/// is <c>RetrieveMultiple</c>, and so on) and the table it names. A caller call is named by its Web API path.
/// </remarks>
internal sealed class DecisionCommitCallProfile
{
    private static readonly AsyncLocal<string?> CurrentStage = new();
    private static readonly AsyncLocal<string?> CurrentProbeSite = new();
    private static readonly HashSet<string> ProductionAssemblies = new(StringComparer.Ordinal) { "Sprk.Bff.Api", "Spaarke.Dataverse", "Spaarke.Core" };
    private static readonly Regex GuidPattern = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private static readonly MethodInfo DelayedOfT = typeof(DecisionCommitCallProfile).GetMethod(nameof(DelayedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo StagedOfT = typeof(DecisionCommitCallProfile).GetMethod(nameof(StagedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly ConcurrentQueue<Call> _calls = new();
    private int _seq;

    /// <summary>The fixed latency every counted call waits before it is answered (zero: none).</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>When false, calls are not recorded (a warm-up, or the part of a test that is not measured).</summary>
    public bool Recording { get; set; } = true;

    public IReadOnlyList<Call> Calls => _calls.OrderBy(c => c.Seq).ToList();

    public void Clear()
    {
        while (_calls.TryDequeue(out _))
        {
        }
    }

    /// <summary>One counted call.</summary>
    public sealed record Call(int Seq, string Stage, string Seam, string Operation, string Target, string Key, string Site, bool IsWrite, string? Payload);

    /// <summary>A counting wrapper of <paramref name="inner"/>, named <paramref name="seam"/>.</summary>
    public T Wrap<T>(T inner, string seam) where T : class => CountingProxy.Create(inner, this, seam, staged: false);

    /// <summary>An executor wrapper that names the step (its code, preflight or execute) every call below it ran in.</summary>
    public IDecisionActionExecutor Staged(IDecisionActionExecutor inner) => CountingProxy.Create(inner, this, "executor", staged: true);

    /// <summary>Records a call made by a seam the harness subclasses (the probe), then waits the latency.</summary>
    public async Task RecordAsync(string seam, string operation, string target, string key)
    {
        Add(seam, operation, target, key, CurrentProbeSite.Value ?? "CallerRecordAccessProbe", payload: null);
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency).ConfigureAwait(false);
    }

    /// <summary>Remembers, for the probe's wire calls below it, which production method asked the probe.</summary>
    public static void EnterProbe() => CurrentProbeSite.Value = SiteOf(new StackTrace(1, false), skipProbe: true);

    /// <summary>The writes, in order, with every id replaced by a token in order of first appearance.</summary>
    public string WriteTranscript() => Tokenize(string.Join("\n", Calls.Where(c => c.IsWrite).Select(c => $"{c.Seam} {c.Operation} {c.Target} {c.Payload}")));

    /// <summary>Text with every id replaced by a token in order of first appearance.</summary>
    public static string Tokenize(string text)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return GuidPattern.Replace(text, m =>
        {
            if (!tokens.TryGetValue(m.Value, out var token))
                tokens[m.Value] = token = $"<id{tokens.Count + 1}>";
            return token;
        });
    }

    /// <summary>The per-site profile: one line per (stage, seam, operation, target, site) with its count.</summary>
    public string Table()
    {
        var sb = new StringBuilder();
        foreach (var g in Calls.GroupBy(c => (c.Stage, c.Seam, c.Operation, c.Target, c.Site)).OrderBy(g => g.Min(c => c.Seq)))
            sb.AppendLine(CultureInfo.InvariantCulture, $"{g.Count(),4} | {g.Key.Stage} | {g.Key.Seam} | {g.Key.Operation} | {g.Key.Target} | {g.Key.Site}");
        return sb.ToString();
    }

    /// <summary>The table-metadata reads (<c>EntityDefinitions(…)</c>, sub-paths included) that reached Dataverse.</summary>
    public IReadOnlyList<Call> MetadataReads => Calls.Where(c => c.Target.StartsWith("EntityDefinitions(", StringComparison.Ordinal)).ToList();

    /// <summary>Counts by kind of call, and every metadata path read more than once with the sites that read it.</summary>
    public string Summary()
    {
        var calls = Calls;
        bool Sdk(Call c) => c.Seam.Contains("IGenericEntityService", StringComparison.Ordinal) || c.Seam.Contains("IOrganizationServiceAsync2", StringComparison.Ordinal)
            || c.Seam.Contains("IFieldMappingDataverseService", StringComparison.Ordinal) || c.Seam.Contains("IDataverseRecordShareService", StringComparison.Ordinal);
        var metadata = MetadataReads;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"metadata reads (EntityDefinitions): {metadata.Count}, distinct paths {metadata.Select(c => c.Key).Distinct().Count()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"sprk_recordtype_refs(id) reads: {calls.Count(c => c.Target.StartsWith("sprk_recordtype_refs({id})", StringComparison.Ordinal))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RetrievePrincipalAccess: {calls.Count(c => c.Target.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RetrieveUserSetOfPrivilegesByNames: {calls.Count(c => c.Target.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"WhoAmI: {calls.Count(c => c.Target.StartsWith("WhoAmI", StringComparison.Ordinal))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SDK-side (app-only and writer) calls: {calls.Count(Sdk)} (writes {calls.Count(c => Sdk(c) && c.IsWrite)})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"all calls: {calls.Count}");
        foreach (var repeat in metadata.GroupBy(c => c.Key).Where(g => g.Count() > 1))
            sb.AppendLine(CultureInfo.InvariantCulture, $"  repeated x{repeat.Count()}: {repeat.Key} <- {string.Join(", ", repeat.Select(c => $"{c.Stage}: {c.Site}"))}");
        return sb.ToString();
    }

    internal object? Intercept(object inner, string seam, bool staged, MethodInfo method, object?[]? args)
    {
        if (!typeof(Task).IsAssignableFrom(method.ReturnType))
            return Invoke(inner, method, args); // a property or a synchronous helper: no Dataverse call

        if (staged)
        {
            var code = ((IDecisionActionExecutor)inner).Code;
            var stage = $"{code} {(method.Name.StartsWith("Preflight", StringComparison.Ordinal) ? "preflight" : "execute")}";
            return StagedOfT.MakeGenericMethod(method.ReturnType.GetGenericArguments()[0])
                .Invoke(null, [stage, (Func<object?>)(() => Invoke(inner, method, args))]);
        }

        var site = SiteOf(new StackTrace(2, false), skipProbe: false);
        var (target, key) = TargetOf(args);
        Add(seam, method.Name == "ExecuteAsync" && args?.FirstOrDefault() is OrganizationRequest r ? r.RequestName : method.Name, target, key, site,
            IsWrite(method.Name, args) ? Describe(args) : null);

        if (method.ReturnType == typeof(Task))
            return DelayedPlainAsync(() => (Task)Invoke(inner, method, args)!);
        return DelayedOfT.MakeGenericMethod(method.ReturnType.GetGenericArguments()[0])
            .Invoke(this, [(Func<object?>)(() => Invoke(inner, method, args))]);
    }

    private void Add(string seam, string operation, string target, string key, string site, string? payload)
    {
        if (!Recording)
            return;
        var isWrite = payload is not null;
        _calls.Enqueue(new Call(Interlocked.Increment(ref _seq), CurrentStage.Value ?? "commit", seam, operation, target, key, site, isWrite, payload));
    }

    private async Task DelayedPlainAsync(Func<Task> call)
    {
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency).ConfigureAwait(false);
        await call().ConfigureAwait(false);
    }

    private async Task<T> DelayedAsync<T>(Func<object?> call)
    {
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency).ConfigureAwait(false);
        return await ((Task<T>)call()!).ConfigureAwait(false);
    }

    private static async Task<T> StagedAsync<T>(string stage, Func<object?> call)
    {
        CurrentStage.Value = stage; // flows to every call below this step; an async method's change never flows back
        return await ((Task<T>)call()!).ConfigureAwait(false);
    }

    private static object? Invoke(object inner, MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static readonly string[] WritePrefixes =
        ["Create", "Update", "Delete", "Upsert", "Associate", "Disassociate", "Grant", "Revoke", "Modify", "Assign", "Share", "Post", "Patch", "Set"];

    private static readonly HashSet<string> WriteRequests = new(StringComparer.Ordinal)
        { "Create", "Update", "Delete", "Upsert", "Assign", "GrantAccess", "ModifyAccess", "RevokeAccess", "Associate", "Disassociate" };

    private static bool IsWrite(string method, object?[]? args) =>
        method == "ExecuteAsync"
            ? args?.FirstOrDefault() is OrganizationRequest r && WriteRequests.Contains(r.RequestName)
            : WritePrefixes.Any(p => method.StartsWith(p, StringComparison.Ordinal));

    private static (string Target, string Key) TargetOf(object?[]? args)
    {
        foreach (var arg in args ?? [])
        {
            switch (arg)
            {
                case QueryExpression q:
                    return ($"{q.EntityName} (query)", $"query:{q.EntityName}");
                case FetchExpression f:
                    var table = Regex.Match(f.Query, "<entity name=['\"](\\w+)['\"]").Groups[1].Value;
                    return ($"{table} (fetch)", $"fetch:{table}");
                case Entity e:
                    return (e.LogicalName, e.LogicalName);
                case OrganizationRequest r:
                    var named = r.Parameters.TryGetValue("Target", out var t) ? t switch
                    {
                        Entity te => te.LogicalName,
                        EntityReference tr => tr.LogicalName,
                        _ => null,
                    } : null;
                    return (named ?? r.RequestName, r.RequestName);
                case string s when s.Length > 0:
                    var path = s.Replace("/api/data/v9.2/", string.Empty, StringComparison.Ordinal).TrimStart('/');
                    var keyText = GuidPattern.Replace(Uri.UnescapeDataString(path), "{id}");
                    var cut = keyText.IndexOf('?', StringComparison.Ordinal);
                    return (cut >= 0 ? keyText[..cut] : keyText, keyText);
            }
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>The first production method on the stack: the call site (compiler-generated async and lambda frames unwrapped).</summary>
    internal static string SiteOf(StackTrace trace, bool skipProbe)
    {
        foreach (var frame in trace.GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null || !ProductionAssemblies.Contains(type.Assembly.GetName().Name ?? string.Empty))
                continue;

            var name = method.Name;
            while (type.Name.StartsWith('<') && type.DeclaringType is { } outer)
            {
                var inner = Between(type.Name);
                if (inner.Length > 0)
                    name = inner;
                type = outer;
            }

            if (name.StartsWith('<') && Between(name) is { Length: > 0 } lambdaOwner)
                name = lambdaOwner;
            // The memo and the caller client are pass-through wrappers: the site is the method that asked them.
            if ((skipProbe && type.Name == nameof(CallerRecordAccessProbe)) || type.Name is "CallerMetadataReadMemo" or nameof(DataverseUserClient) or "DecisionAccessWindowCallerClient")
                continue;
            return $"{type.Name}.{name}";
        }

        return "(test)";
    }

    private static string Between(string text)
    {
        var close = text.IndexOf('>', StringComparison.Ordinal);
        return close > 1 ? text[1..close] : string.Empty;
    }

    private static string Describe(object?[]? args) =>
        string.Join(" ", (args ?? []).Where(a => a is not CancellationToken).Select(DescribeValue));

    private static string DescribeValue(object? value) => value switch
    {
        null => "null",
        string s => s,
        Entity e => $"{e.LogicalName}({e.Id}){{{string.Join(";", e.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={DescribeValue(a.Value)}"))}}}",
        EntityReference r => $"{r.LogicalName}:{r.Id}",
        OptionSetValue o => o.Value.ToString(CultureInfo.InvariantCulture),
        Money m => m.Value.ToString(CultureInfo.InvariantCulture),
        OrganizationRequest r => $"{r.RequestName}{{{string.Join(";", r.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={DescribeValue(p.Value)}"))}}}",
        JsonElement j => j.GetRawText(),
        System.Collections.IDictionary d => $"{{{string.Join(";", d.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k, StringComparer.Ordinal).Select(k => $"{k}={DescribeValue(d[k])}"))}}}",
        System.Collections.IEnumerable list => $"[{string.Join(",", list.Cast<object?>().Select(DescribeValue))}]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The proxy itself: every interface call goes to <see cref="Intercept"/>.</summary>
    public class CountingProxy : DispatchProxy
    {
        private object _inner = null!;
        private DecisionCommitCallProfile _profile = null!;
        private string _seam = string.Empty;
        private bool _staged;

        internal static T Create<T>(T inner, DecisionCommitCallProfile profile, string seam, bool staged) where T : class
        {
            var proxy = Create<T, CountingProxy>();
            var self = (CountingProxy)(object)proxy;
            self._inner = inner;
            self._profile = profile;
            self._seam = seam;
            self._staged = staged;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            _profile.Intercept(_inner, _seam, _staged, targetMethod!, args);
    }

    /// <summary>
    /// The caller's Dataverse WIRE, counted: the handler under the REAL <c>DataverseUserClient</c> (so the type production
    /// resolves is what the request measures). It records each request as the interface call that produced it (method name,
    /// Web API path, table) with the production method that asked, waits the latency, and has <paramref name="answers"/> (the
    /// scripted caller's Dataverse) answer it.
    /// </summary>
    public sealed class CallerWire : HttpMessageHandler
    {
        private const string ApiRoot = "/api/data/v9.2/";
        private readonly DecisionCommitCallProfile _profile;
        private readonly string _seam;
        private readonly IDataverseUserClient _answers;

        public CallerWire(DecisionCommitCallProfile profile, string seam, IDataverseUserClient answers)
        {
            _profile = profile;
            _seam = seam;
            _answers = answers;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var site = SiteOf(new StackTrace(1, false), skipProbe: false); // before the first await: the caller's frames are still on the stack
            var original = request.RequestUri!.OriginalString;
            var apiPath = original[original.IndexOf("/api/", StringComparison.Ordinal)..];
            var relative = apiPath.StartsWith(ApiRoot, StringComparison.Ordinal) ? apiPath[ApiRoot.Length..] : apiPath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var isGet = request.Method == HttpMethod.Get;
            var operation = request.Method.Method switch { "GET" => "GetAsync", "POST" => "PostAsync", "PATCH" => "PatchAsync", "DELETE" => "DeleteAsync", var m => m };

            var (target, key) = TargetOf([apiPath]);
            _profile.Add(_seam, operation, target, key, site, isGet ? null : $"{(request.Method == HttpMethod.Post ? apiPath : relative)} {body}");
            if (_profile.Latency > TimeSpan.Zero)
                await Task.Delay(_profile.Latency, cancellationToken).ConfigureAwait(false);

            var response = request.Method.Method switch
            {
                "GET" => await _answers.GetAsync(relative, cancellationToken).ConfigureAwait(false),
                "POST" => await _answers.PostAsync(apiPath, body ?? string.Empty, request.Headers.Contains("Prefer"), cancellationToken).ConfigureAwait(false),
                "PATCH" when request.Headers.TryGetValues("If-Match", out var ifMatch) && ifMatch.FirstOrDefault() is { } etag && etag != "*" =>
                    await _answers.PatchAsync(relative, body ?? string.Empty, long.Parse(etag.Trim('W', '/', '"'), CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false),
                "PATCH" => await _answers.PatchAsync(relative, body ?? string.Empty, cancellationToken).ConfigureAwait(false),
                "DELETE" => await _answers.DeleteAsync(relative, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException(request.Method.Method),
            };
            return ToHttp(response);
        }

        private static HttpResponseMessage ToHttp(DataverseUserResponse response)
        {
            var status = response.StatusCode >= 100 ? response.StatusCode : 500;
            if (!response.IsSuccess)
            {
                return new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { error = new { code = "0x0", message = response.ErrorMessage } }), Encoding.UTF8, "application/json"),
                };
            }

            return response.Body is { } body
                ? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage((HttpStatusCode)status);
        }
    }

    /// <summary>
    /// The SHIPPED probe with only its wire answered in memory and counted (the OBO exchange, <c>WhoAmI</c>,
    /// <c>RetrievePrincipalAccess</c>, <c>RetrieveUserSetOfPrivilegesByNames</c>), as T170's probe does, plus the privilege
    /// question (an HTTP call the probe makes itself). Every right and privilege is granted unless a test takes it away; a
    /// fault answer can be scripted for a privilege.
    /// </summary>
    public sealed class ProfiledProbe : CallerRecordAccessProbe
    {
        private readonly DecisionCommitCallProfile _profile;
        private readonly Guid _caller;

        public ProfiledProbe(DecisionCommitCallProfile profile, Guid caller)
            : this(profile, caller, new PrivilegeWire())
        {
        }

        private ProfiledProbe(DecisionCommitCallProfile profile, Guid caller, PrivilegeWire wire)
            : base(
                new HttpClient(wire),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                }).Build(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
            _profile = profile;
            _caller = caller;
            wire.Owner = this;
        }

        /// <summary>Privileges the caller does not hold.</summary>
        public HashSet<string> NotHeld { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Privileges whose question faults (a 503).</summary>
        public HashSet<string> Faults { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Records the caller is denied every right on.</summary>
        public HashSet<Guid> NoRights { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            EnterProbe();
            return base.GetCallerRightsAsync(callerBearerToken, entitySet, recordId, ct);
        }

        public override Task<IReadOnlyList<AccessRights>> GetCallerRightsForRecordsAsync(
            string? callerBearerToken, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken ct = default)
        {
            EnterProbe();
            return base.GetCallerRightsForRecordsAsync(callerBearerToken, targets, ct);
        }

        public override Task<IReadOnlyList<AccessRights?>> GetCallerRightsForRecordsOrUnknownAsync(
            string? callerBearerToken, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken ct = default)
        {
            EnterProbe();
            return base.GetCallerRightsForRecordsOrUnknownAsync(callerBearerToken, targets, ct);
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            EnterProbe();
            return base.GetCallerSystemUserIdAsync(callerBearerToken, ct);
        }

        public override Task<CallerPrivilegeAnswer> CallerPrivilegeAnswerAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            EnterProbe();
            return base.CallerPrivilegeAnswerAsync(callerBearerToken, privilegeName, ct);
        }

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct) =>
            Task.FromResult<string?>($"{callerBearerToken}-dv"); // the OBO exchange is token-cached live; not a Dataverse call

        protected override async Task<(HttpStatusCode Status, string Body)> SendWhoAmIRequestAsync(string dataverseToken, CancellationToken ct)
        {
            await _profile.RecordAsync("caller access (probe)", "GET", "WhoAmI", "WhoAmI").ConfigureAwait(false);
            return (HttpStatusCode.OK, JsonSerializer.Serialize(new { UserId = _caller.ToString("D") }));
        }

        protected override async Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(string url, string dataverseToken, CancellationToken ct)
        {
            var set = Regex.Match(Uri.UnescapeDataString(url), "\"@odata.id\":\"(\\w+)\\(").Groups[1].Value;
            await _profile.RecordAsync("caller access (probe)", "GET", $"RetrievePrincipalAccess {set}",
                "RetrievePrincipalAccess " + GuidPattern.Replace(Uri.UnescapeDataString(url), "{id}")).ConfigureAwait(false);
            var record = GuidPattern.Matches(Uri.UnescapeDataString(url)).Select(m => Guid.Parse(m.Value)).LastOrDefault();
            return (HttpStatusCode.OK, NoRights.Contains(record)
                ? "{\"AccessRights\":\"None\"}"
                : "{\"AccessRights\":\"ReadAccess, WriteAccess, AppendAccess, AppendToAccess, CreateAccess, DeleteAccess, ShareAccess, AssignAccess\"}");
        }

        private sealed class PrivilegeWire : HttpMessageHandler
        {
            public ProfiledProbe Owner { get; set; } = null!;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
                var name = Regex.Match(url, "\\[\"([^\"]+)\"\\]").Groups[1].Value;
                await Owner._profile.RecordAsync("caller access (probe)", "GET", $"RetrieveUserSetOfPrivilegesByNames {name}",
                    $"RetrieveUserSetOfPrivilegesByNames {name}").ConfigureAwait(false);
                if (Owner.Faults.Contains(name))
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                var body = Owner.NotHeld.Contains(name)
                    ? "{\"RolePrivileges\":[]}"
                    : JsonSerializer.Serialize(new { RolePrivileges = new[] { new { Depth = "Global", PrivilegeName = name } } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }
        }
    }
}
