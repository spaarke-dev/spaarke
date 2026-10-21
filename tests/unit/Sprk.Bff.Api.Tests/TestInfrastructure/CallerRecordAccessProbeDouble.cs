using System.Collections.Concurrent;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 036 (FR-20): <see cref="CallerRecordAccessProbe"/> substituted at its module boundary —
/// the caller's own Dataverse rights per (entity set, record id), as the write routes ask for them. Unanswered pairs are
/// <see cref="AccessRights.None"/>, the probe's own fail-closed answer. Every call is recorded, so a test can assert that a
/// caller whose access Dataverse did NOT decide is never asked about (flag off is byte-equivalent).
/// </summary>
/// <remarks>Not a transport mock (ADR-038 §7 B1): it overrides the probe's virtual question, never its HTTP. The one
/// exception is <see cref="AppOnlyWire"/> (task 036 fix round 4, F1): set, the app-only rights and privilege questions are
/// answered by the PRODUCTION probe over the production app-only client and a scripted Dataverse response, so a route test
/// drives a real timeout, a real UNKNOWN 403 and a real 5xx end to end (as <c>CallerRecordAccessProbeAppOnlyTests</c> does
/// for the probe alone).</remarks>
public sealed class CallerRecordAccessProbeDouble : CallerRecordAccessProbe
{
    public CallerRecordAccessProbeDouble()
        : this(new WireHandler())
    {
    }

    private CallerRecordAccessProbeDouble(WireHandler wire)
        : base(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<CallerRecordAccessProbe>.Instance,
            confidentialClients: null,
            appOnlyDataverse: new WireService(wire))
    {
        _wire = wire;
    }

    private readonly WireHandler _wire;

    /// <summary>
    /// Task 036 fix round 4 (F1): when set, the app-only questions are answered by the production probe over this scripted
    /// Dataverse response (the request is passed with its token; a handler may wait on it to simulate a Dataverse that
    /// never answers). Unset (the default), the seeded answers below are used.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? AppOnlyWire
    {
        get => _wire.Respond;
        set => _wire.Respond = value;
    }

    /// <summary>The bound on the production app-only questions in wire mode (the production value when unset).</summary>
    public TimeSpan? AppOnlyTimeoutOverride { get; set; }

    /// <summary>The Dataverse shapes <see cref="UseAppOnlyWire"/> scripts (task 036 fix round 4, F1).</summary>
    public static class WireShape
    {
        /// <summary>Dataverse never answers; the bounded question times out (the bound is shortened to 100 ms).</summary>
        public const string Timeout = "timeout";

        /// <summary>A 403 that is not an access answer: the impersonation fault <c>0x8004A110</c> (UNKNOWN).</summary>
        public const string Unknown = "unknown-403";

        /// <summary>A server fault: 503 Service Unavailable.</summary>
        public const string ServerFault = "5xx";

        /// <summary>A real denial: the rights question answers 403 <c>0x80048306</c> ("no ReadAccess"), an answer about the
        /// user. (The privilege question has no denial status; "not held" is <see cref="NotHeld"/>.)</summary>
        public const string Denial = "denial-403";

        /// <summary>The rights question answers Read and AppendTo (a child create may proceed to the privilege question).</summary>
        public const string AppendTo = "append-to";

        /// <summary>The privilege question answers "held" for every child-table Create privilege the routes ask.</summary>
        public const string Held = "held";

        /// <summary>The privilege question answers "not held" (an empty <c>RolePrivileges</c>), an answer about the user.</summary>
        public const string NotHeld = "not-held";
    }

    /// <summary>
    /// Task 036 fix round 4 (F1): answer the app-only questions through the production probe over a scripted Dataverse —
    /// <paramref name="rights"/> for <c>RetrievePrincipalAccess</c>, <paramref name="privilege"/> for
    /// <c>RetrieveUserSetOfPrivilegesByNames</c> (<see cref="WireShape"/>). A timeout shape shortens the bound to 100 ms.
    /// </summary>
    public CallerRecordAccessProbeDouble UseAppOnlyWire(string rights, string privilege = WireShape.Held)
    {
        if (rights == WireShape.Timeout || privilege == WireShape.Timeout)
        {
            AppOnlyTimeoutOverride = TimeSpan.FromMilliseconds(100);
        }

        AppOnlyWire = async (request, ct) =>
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            var shape = url.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal) ? privilege : rights;
            switch (shape)
            {
                case WireShape.Timeout:
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                    throw new InvalidOperationException("unreachable");
                case WireShape.Unknown:
                    return WireError(System.Net.HttpStatusCode.Forbidden, "0x8004A110", "CannotActOnBehalfOfAnotherUser");
                case WireShape.ServerFault:
                    return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent(string.Empty) };
                case WireShape.Denial:
                    return WireError(System.Net.HttpStatusCode.Forbidden, DataverseWebApiService.NoReadAccessErrorCode,
                        "The user does not have ReadAccess right(s)");
                case WireShape.AppendTo:
                    return WireJson("{\"AccessRights\":\"ReadAccess, AppendToAccess\"}");
                case WireShape.NotHeld:
                    return WireJson("{\"RolePrivileges\":[]}");
                case WireShape.Held:
                    return WireJson(
                        "{\"RolePrivileges\":[{\"PrivilegeName\":\"prvCreatesprk_Todo\"},{\"PrivilegeName\":\"prvCreatesprk_Event\"},"
                        + "{\"PrivilegeName\":\"prvCreatesprk_Document\"},{\"PrivilegeName\":\"prvCreatesprk_Communication\"}]}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(rights), shape, "unknown wire shape");
            }
        };
        return this;
    }

    private static HttpResponseMessage WireJson(string body) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage WireError(System.Net.HttpStatusCode status, string code, string message) =>
        new(status)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { error = new { code, message } }),
                System.Text.Encoding.UTF8, "application/json"),
        };

    protected override TimeSpan AppOnlyTimeout => AppOnlyTimeoutOverride ?? base.AppOnlyTimeout;

    /// <summary>Dataverse's answer per (entity set, record id).</summary>
    public ConcurrentDictionary<(string EntitySet, Guid RecordId), AccessRights> Answers { get; } = new();

    /// <summary>When set, the question throws (an unexpected fault; the real probe answers None instead).</summary>
    public Exception? Fault { get; set; }

    /// <summary>Every question asked, in order.</summary>
    public ConcurrentQueue<(string? Token, string EntitySet, Guid RecordId)> Calls { get; } = new();

    public CallerRecordAccessProbeDouble Answer(string entitySet, Guid recordId, AccessRights rights)
    {
        Answers[(entitySet, recordId)] = rights;
        return this;
    }

    /// <summary>Task 036 fix round 1 (F1): the table privileges the caller holds. Any other name is not held, the real
    /// probe's fail-closed answer.</summary>
    public ConcurrentDictionary<string, bool> HeldPrivileges { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, the privilege question throws (an unexpected fault; the real probe answers false instead).</summary>
    public Exception? PrivilegeFault { get; set; }

    /// <summary>Every privilege asked about, in order.</summary>
    public ConcurrentQueue<string> PrivilegeCalls { get; } = new();

    public CallerRecordAccessProbeDouble Holds(params string[] privileges)
    {
        foreach (var privilege in privileges)
        {
            HeldPrivileges[privilege] = true;
        }

        return this;
    }

    // ── Task 036 owner decision (a): the app-only questions naming a WorkforceHome-linked systemuser. Kept apart from the
    //    OBO answers on purpose: a test that seeds only these proves the route asked app-only, because the OBO path (whose
    //    real exchange cannot succeed for a customer-directory token) finds nothing and answers None.

    /// <summary>Dataverse's app-only answer per (named systemuser, entity set, record id); <see langword="null"/> is "no
    /// answer" (a timeout, a fault, an answer not about the user — task 036 fix round 4, F1).</summary>
    public ConcurrentDictionary<(Guid SystemUserId, string EntitySet, Guid RecordId), AccessRights?> AppOnlyAnswers { get; } = new();

    /// <summary>The privileges each named systemuser holds, asked app-only; <see langword="null"/> is "no answer". Any other
    /// pair is not held.</summary>
    public ConcurrentDictionary<(Guid SystemUserId, string Privilege), bool?> AppOnlyHeldPrivileges { get; } = new();

    /// <summary>When set, the app-only rights question throws (an unexpected fault; the real probe answers null).</summary>
    public Exception? AppOnlyFault { get; set; }

    /// <summary>When set, the app-only privilege question throws (the real probe answers null).</summary>
    public Exception? AppOnlyPrivilegeFault { get; set; }

    /// <summary>Every app-only rights question asked, in order.</summary>
    public ConcurrentQueue<(Guid SystemUserId, string EntitySet, Guid RecordId)> AppOnlyCalls { get; } = new();

    /// <summary>Every app-only privilege question asked, in order.</summary>
    public ConcurrentQueue<(Guid SystemUserId, string Privilege)> AppOnlyPrivilegeCalls { get; } = new();

    public CallerRecordAccessProbeDouble AnswerAppOnly(Guid systemUserId, string entitySet, Guid recordId, AccessRights? rights)
    {
        AppOnlyAnswers[(systemUserId, entitySet, recordId)] = rights;
        return this;
    }

    /// <summary>The named systemuser's privilege question gets no answer (task 036 fix round 4, F1).</summary>
    public CallerRecordAccessProbeDouble PrivilegeUnansweredAppOnly(Guid systemUserId, string privilege)
    {
        AppOnlyHeldPrivileges[(systemUserId, privilege)] = null;
        return this;
    }

    public CallerRecordAccessProbeDouble HoldsAppOnly(Guid systemUserId, params string[] privileges)
    {
        foreach (var privilege in privileges)
        {
            AppOnlyHeldPrivileges[(systemUserId, privilege)] = true;
        }

        return this;
    }

    public void Reset()
    {
        Answers.Clear();
        Fault = null;
        Calls.Clear();
        HeldPrivileges.Clear();
        PrivilegeFault = null;
        PrivilegeCalls.Clear();
        AppOnlyAnswers.Clear();
        AppOnlyHeldPrivileges.Clear();
        AppOnlyFault = null;
        AppOnlyPrivilegeFault = null;
        AppOnlyCalls.Clear();
        AppOnlyPrivilegeCalls.Clear();
        AppOnlyWire = null;
        AppOnlyTimeoutOverride = null;
    }

    public override Task<AccessRights?> GetPrincipalRightsAppOnlyOrUnknownAsync(
        Guid principalSystemUserId, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        AppOnlyCalls.Enqueue((principalSystemUserId, entitySet, recordId));
        if (AppOnlyWire is not null)
        {
            return base.GetPrincipalRightsAppOnlyOrUnknownAsync(principalSystemUserId, entitySet, recordId, ct);
        }

        if (AppOnlyFault is not null)
        {
            throw AppOnlyFault;
        }

        return Task.FromResult(AppOnlyAnswers.TryGetValue((principalSystemUserId, entitySet, recordId), out var rights)
            ? rights
            : AccessRights.None);
    }

    public override Task<bool?> PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync(
        Guid principalSystemUserId, string privilegeName, CancellationToken ct = default)
    {
        AppOnlyPrivilegeCalls.Enqueue((principalSystemUserId, privilegeName));
        if (AppOnlyWire is not null)
        {
            return base.PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync(principalSystemUserId, privilegeName, ct);
        }

        if (AppOnlyPrivilegeFault is not null)
        {
            throw AppOnlyPrivilegeFault;
        }

        return Task.FromResult(AppOnlyHeldPrivileges.TryGetValue((principalSystemUserId, privilegeName), out var held)
            ? held
            : false);
    }

    public override Task<bool> CallerHoldsPrivilegeAsync(
        string? callerBearerToken, string privilegeName, CancellationToken ct = default)
    {
        PrivilegeCalls.Enqueue(privilegeName);
        if (PrivilegeFault is not null)
        {
            throw PrivilegeFault;
        }

        return Task.FromResult(HeldPrivileges.TryGetValue(privilegeName, out var held) && held);
    }

    public override Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        Calls.Enqueue((callerBearerToken, entitySet, recordId));
        if (Fault is not null)
        {
            throw Fault;
        }

        return Task.FromResult(Answers.TryGetValue((entitySet, recordId), out var rights) ? rights : AccessRights.None);
    }

    /// <summary>The scripted Dataverse under the production app-only client; answers only in wire mode.</summary>
    private sealed class WireHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Respond is { } respond
                ? respond(request, cancellationToken)
                : throw new InvalidOperationException("The app-only wire is used only when AppOnlyWire is set.");
    }

    /// <summary>The production app-only client, built through its protected test constructor with a static token.</summary>
    private sealed class WireService(HttpMessageHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("app-only-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }
}
