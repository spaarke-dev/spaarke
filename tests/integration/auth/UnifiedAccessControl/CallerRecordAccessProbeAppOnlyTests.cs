using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 036, owner decision (a) (2026-10-18): for a WorkforceHome-linked systemuser, whose
/// customer-directory token cannot be exchanged for Dataverse, <see cref="CallerRecordAccessProbe"/> asks the SAME two
/// questions app-only, as the BFF application user, naming that systemuser — <c>RetrievePrincipalAccess</c> with
/// <c>Principal = systemusers(id)</c>, and <c>RetrieveUserSetOfPrivilegesByNames</c> on that systemuser — through the
/// existing app-only client (<see cref="DataverseWebApiService"/>), and fails closed on every fault.
/// </summary>
/// <remarks>
/// <para>The production client runs over a scripted handler through its protected test constructor, as
/// <c>DataverseRecordShareWireTests</c> does: the wire (the URL, the named principal, the impersonation header, the exact
/// privilege name) is the thing under test. The probe's own OBO transport is never configured here, so any answer comes
/// from the app-only path.</para>
/// Placement: <c>tests/integration/auth/**</c> — the ADR-038 §2 security-auth KEEP path.
/// </remarks>
public sealed class CallerRecordAccessProbeAppOnlyTests
{
    private static readonly Guid SystemUser = Guid.Parse("0e036a00-0000-4000-8000-0000000000a1");
    private static readonly Guid Project = Guid.Parse("0e036a00-0000-4000-8000-0000000000b2");

    private static CallerRecordAccessProbe Probe(ScriptedHandler? handler) => new(
        new HttpClient(new ScriptedHandler(_ => throw new InvalidOperationException("the OBO transport must not be used"))),
        new ConfigurationBuilder().Build(),
        NullLogger<CallerRecordAccessProbe>.Instance,
        confidentialClients: null,
        appOnlyDataverse: handler is null ? null : new OfflineService(handler));

    [Fact(DisplayName = "Task 036 (a): app-only RetrievePrincipalAccess names the resolved systemuser and returns Dataverse's rights")]
    public async Task GetPrincipalRightsAppOnly_NamesTheSystemUser_AndReturnsItsRights()
    {
        var handler = new ScriptedHandler(_ => Json("""{"AccessRights":"ReadAccess, WriteAccess, AppendToAccess"}"""));

        var rights = await Probe(handler).GetPrincipalRightsAppOnlyAsync(SystemUser, "sprk_projects", Project);

        rights.Should().Be(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Url.Should().Contain($"systemusers({SystemUser})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)");
        sent.Url.Should().Contain($"sprk_projects({Project})");
        sent.CallerId.Should().Be(SystemUser.ToString("D"),
            "the existing wrapper also asks AS that user (MSCRMCallerID), the A5 shape — never as the bare application");
        sent.Authorization.Should().Be("Bearer app-only-token", "the BFF's own app-only credential, not a caller token");
    }

    [Theory(DisplayName = "Task 036 (a) (NFR-01): an app-only rights question that cannot run or faults answers None — no fallback")]
    [InlineData("empty-principal")]
    [InlineData("no-client")]
    [InlineData("forbidden")]
    [InlineData("not-found")]
    [InlineData("server-fault")]
    [InlineData("unreadable")]
    [InlineData("transport")]
    public async Task GetPrincipalRightsAppOnly_FailsClosed(string shape)
    {
        var handler = new ScriptedHandler(_ => shape switch
        {
            "forbidden" => Status(HttpStatusCode.Forbidden),
            "not-found" => Status(HttpStatusCode.NotFound),
            "server-fault" => Status(HttpStatusCode.ServiceUnavailable),
            "unreadable" => Json("{not json"),
            "transport" => throw new HttpRequestException("connection reset"),
            _ => Json("""{"AccessRights":"ReadAccess, WriteAccess"}"""),
        });
        var probe = Probe(shape == "no-client" ? null : handler);

        var rights = await probe.GetPrincipalRightsAppOnlyAsync(
            shape == "empty-principal" ? Guid.Empty : SystemUser, "sprk_projects", Project);

        rights.Should().Be(AccessRights.None);
        if (shape is "empty-principal" or "no-client")
        {
            handler.Requests.Should().BeEmpty("nothing is asked without a resolved principal and the app-only client");
        }
    }

    [Theory(DisplayName = "Task 036 (a): the app-only privilege read names the systemuser and sends the exact, case-sensitive privilege name")]
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvCreatesprk_Todo","BusinessUnitId":"00000000-0000-0000-0000-000000000001"}]}""", true)]
    [InlineData("""{"RolePrivileges":[]}""", false)]
    [InlineData("""{"RolePrivileges":[{"Depth":"Global","PrivilegeName":"prvCreatesprk_Event"}]}""", false)]
    public async Task PrincipalHoldsPrivilegeAppOnly_NamesTheSystemUser_AndReadsTheAnswer(string body, bool expected)
    {
        var handler = new ScriptedHandler(_ => Json(body));

        var holds = await Probe(handler).PrincipalHoldsPrivilegeAppOnlyAsync(SystemUser, "prvCreatesprk_Todo");

        holds.Should().Be(expected);
        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Url.Should().Contain(
            $"systemusers({SystemUser})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)");
        sent.Url.Should().Contain("""["prvCreatesprk_Todo"]""", "Dataverse privilege names are case-sensitive");
        sent.CallerId.Should().Be(SystemUser.ToString("D"));
    }

    [Theory(DisplayName = "Task 036 (a) (NFR-01): an app-only privilege read that cannot run or faults answers false")]
    [InlineData("empty-principal")]
    [InlineData("blank-name")]
    [InlineData("no-client")]
    [InlineData("forbidden")]
    [InlineData("server-fault")]
    [InlineData("unreadable")]
    [InlineData("transport")]
    public async Task PrincipalHoldsPrivilegeAppOnly_FailsClosed(string shape)
    {
        var handler = new ScriptedHandler(_ => shape switch
        {
            "forbidden" => Status(HttpStatusCode.Forbidden),
            "server-fault" => Status(HttpStatusCode.InternalServerError),
            "unreadable" => Json("{not json"),
            "transport" => throw new HttpRequestException("connection reset"),
            _ => Json("""{"RolePrivileges":[{"PrivilegeName":"prvCreatesprk_Todo"}]}"""),
        });
        var probe = Probe(shape == "no-client" ? null : handler);

        var holds = await probe.PrincipalHoldsPrivilegeAppOnlyAsync(
            shape == "empty-principal" ? Guid.Empty : SystemUser,
            shape == "blank-name" ? " " : "prvCreatesprk_Todo");

        holds.Should().BeFalse();
        if (shape is "empty-principal" or "blank-name" or "no-client")
        {
            handler.Requests.Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "Task 036 (a): the caller's own cancellation is rethrown, never read as an answer")]
    public async Task AppOnlyQuestions_RethrowTheCallersCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probe = Probe(new ScriptedHandler(_ => Json("""{"AccessRights":"ReadAccess"}""")));

        var rights = () => probe.GetPrincipalRightsAppOnlyAsync(SystemUser, "sprk_projects", Project, cts.Token);
        var privilege = () => probe.PrincipalHoldsPrivilegeAppOnlyAsync(SystemUser, "prvCreatesprk_Todo", cts.Token);

        await rights.Should().ThrowAsync<OperationCanceledException>();
        await privilege.Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status) =>
        new(status) { Content = new StringContent(string.Empty) };

    /// <summary>The production app-only client, built through its protected test constructor with a static token.</summary>
    private sealed class OfflineService(HttpMessageHandler handler) : DataverseWebApiService(
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

    /// <summary>A request as it left the client, copied before disposal.</summary>
    internal sealed record SentRequest(string Url, string? CallerId, string? Authorization);

    internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new SentRequest(
                Uri.UnescapeDataString(request.RequestUri!.ToString()),
                request.Headers.TryGetValues(DataverseImpersonation.CallerIdHeader, out var ids) ? ids.Single() : null,
                request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("app-only-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
