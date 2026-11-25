using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Ai;

/// <summary>
/// <see cref="PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync"/> (spaarkeai-mcp-bff-api-r2 task 007, spec FR-05): THE run rule of
/// <c>POST /api/ai/playbooks/{id:guid}/execute</c> — document ids, the parameter policy, the playbook-use decision, every document
/// and record parameter, the run's user — for a host that is not the route (task 030's run tool). Each case calls the method
/// directly, as task 030 will, then sends the same input to the REAL route (<see cref="PlaybookAuthHost"/>) over the SAME
/// substitutes: the route's status and code must be what the method's outcome maps to, and the two must ask Dataverse the same
/// questions in the same order.
/// </summary>
/// <remarks>
/// Real: <see cref="PlaybookAuthorizationFilter"/>, <see cref="AuthorizationService"/>, <see cref="OperationAccessRule"/>, the
/// external document narrowing. Substituted only at the boundaries <see cref="PlaybookRouteAuthorizationContractTests"/> uses: the
/// access-data source, the WhoAmI probe, and the playbook and node service interfaces (ADR-038).
/// </remarks>
public sealed class PlaybookRunAuthorizationSharedMethodTests
{
    private const string Playbooks = "sprk_analysisplaybooks";
    private const string Documents = "sprk_documents";
    private const string Matters = "sprk_matters";
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000007";

    // ── Allowed ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "A caller who may use the playbook (public, or private with Read) and read every document is Allowed, with their systemuserid as the run's user; the route streams")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthorizePlaybookRun_AllowedCaller_IsAllowed_AndTheRouteRuns(bool publicPlaybook)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        if (publicPlaybook)
        {
            host.PublicPlaybook(playbookId);
        }
        else
        {
            host.PrivatePlaybook(playbookId);
            host.Access.Grant(Playbooks, playbookId, AccessRights.Read);
        }

        host.NodesOf(playbookId, ExecutorType.AiAnalysis);
        var documentId = host.ReadableDocument();
        host.RunSucceeds();

        var (direct, rest) = await DirectThenRestAsync(host, playbookId, new[] { documentId });

        direct.Outcome.Should().Be(PlaybookRunAuthorizationOutcome.Allowed);
        direct.IsAllowed.Should().BeTrue();
        direct.ReasonCode.Should().BeNull();
        direct.RunUserId.Should().Be(host.Probe.SystemUserId);
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        rest.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    // ── Playbook-use refusals (uniform 404) ─────────────────────────────────────────────────────────────────────────

    public enum PlaybookCase
    {
        /// <summary>Private, and the caller has no Read on the row.</summary>
        Denied,

        /// <summary>The playbook service finds nothing.</summary>
        Unknown,

        /// <summary>The playbook lookup throws.</summary>
        Faulting,
    }

    [Theory(DisplayName = "A playbook the caller may not use (denied, unknown or faulting) is PlaybookUnavailable with the route's 404 reasonCode, before any document is asked")]
    [InlineData(PlaybookCase.Denied)]
    [InlineData(PlaybookCase.Unknown)]
    [InlineData(PlaybookCase.Faulting)]
    public async Task AuthorizePlaybookRun_PlaybookTheCallerMayNotUse_IsPlaybookUnavailable(PlaybookCase @case)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        switch (@case)
        {
            case PlaybookCase.Denied:
                host.PrivatePlaybook(playbookId);
                break;
            case PlaybookCase.Unknown:
                host.Playbooks.Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PlaybookResponse?)null);
                break;
            case PlaybookCase.Faulting:
                host.Playbooks.Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("dataverse down"));
                break;
        }

        var documentId = host.ReadableDocument();

        var (direct, rest) = await DirectThenRestAsync(host, playbookId, new[] { documentId });

        direct.Should().BeSameAs(PlaybookRunAuthorization.PlaybookUnavailable);
        direct.ReasonCode.Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        direct.RunUserId.Should().BeNull();
        rest.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyCodeAsync(rest, "reasonCode")).Should().Be(direct.ReasonCode);
        host.Access.Calls.Should().NotContain(c => c.Set == Documents, "the playbook-use refusal precedes every document check");
        host.VerifyNothingRan();
    }

    // ── Record refusals (uniform 403) ───────────────────────────────────────────────────────────────────────────────

    public enum RecordCase
    {
        /// <summary>A document the caller cannot read.</summary>
        UnreadableDocument,

        /// <summary>A document the caller can read, given to a playbook whose node writes (Write is required).</summary>
        ReadOnlyDocumentForAWritingRun,

        /// <summary>A record parameter (matterId) the caller cannot read.</summary>
        UnreadableRecordParameter,

        /// <summary>The rights question throws.</summary>
        RightsQueryThrows,
    }

    [Theory(DisplayName = "A document or record parameter the caller may not use is RecordAccessDenied with the route's 403 reasonCode")]
    [InlineData(RecordCase.UnreadableDocument)]
    [InlineData(RecordCase.ReadOnlyDocumentForAWritingRun)]
    [InlineData(RecordCase.UnreadableRecordParameter)]
    [InlineData(RecordCase.RightsQueryThrows)]
    public async Task AuthorizePlaybookRun_RecordTheCallerMayNotUse_IsRecordAccessDenied(RecordCase @case)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        host.PublicPlaybook(playbookId);
        host.NodesOf(playbookId, @case == RecordCase.ReadOnlyDocumentForAWritingRun
            ? new[] { ExecutorType.AiAnalysis, ExecutorType.UpdateRecord }
            : new[] { ExecutorType.AiAnalysis });
        var documentId = @case == RecordCase.UnreadableDocument ? Guid.NewGuid() : host.ReadableDocument();
        Dictionary<string, string>? parameters = null;
        if (@case == RecordCase.UnreadableRecordParameter)
        {
            var matterId = Guid.NewGuid();
            host.Access.Grant(Matters, matterId, AccessRights.AppendTo);
            parameters = new Dictionary<string, string> { ["matterId"] = matterId.ToString() };
        }

        if (@case == RecordCase.RightsQueryThrows)
        {
            host.Access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess failed");
        }

        var (direct, rest) = await DirectThenRestAsync(host, playbookId, new[] { documentId }, parameters);

        direct.Should().BeSameAs(PlaybookRunAuthorization.RecordAccessDenied);
        direct.ReasonCode.Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        rest.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyCodeAsync(rest, "reasonCode")).Should().Be(direct.ReasonCode);
        host.VerifyNothingRan();
    }

    [Fact(DisplayName = "A caller whose systemuserid cannot be resolved is RunUserUnresolved, carrying the route's uniform 403 reasonCode")]
    public async Task AuthorizePlaybookRun_UnresolvableRunUser_IsRunUserUnresolved()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        host.Probe.SystemUserId = null;

        var (direct, rest) = await DirectThenRestAsync(host, playbookId, new[] { documentId });

        direct.Should().BeSameAs(PlaybookRunAuthorization.RunUserUnresolved);
        direct.ReasonCode.Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        rest.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyCodeAsync(rest, "reasonCode")).Should().Be(direct.ReasonCode);
        host.VerifyNothingRan();
    }

    // ── Validation refusals (400, before any rights query) ──────────────────────────────────────────────────────────

    [Theory(DisplayName = "A rejected parameter is ParameterRejected with the route's 400 errorCode and the caller's own key, before any rights query")]
    [InlineData("userId", "6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("matterId", "not-a-guid")]
    public async Task AuthorizePlaybookRun_RejectedParameter_IsParameterRejected(string key, string value)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();

        var (direct, rest) = await DirectThenRestAsync(
            host, playbookId, new[] { documentId }, new Dictionary<string, string> { [key] = value });

        direct.Outcome.Should().Be(PlaybookRunAuthorizationOutcome.ParameterRejected);
        direct.ReasonCode.Should().Be(PlaybookAuthorizationFilter.ParameterRejectedErrorCode);
        direct.RejectedParameterKey.Should().Be(key);
        direct.RejectedParameterReason.Should().NotBeNullOrEmpty();
        rest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyCodeAsync(rest, "errorCode")).Should().Be(direct.ReasonCode);
        host.Access.Calls.Should().BeEmpty();
        host.Playbooks.VerifyNoOtherCalls();
        host.VerifyNothingRan();
    }

    [Fact(DisplayName = "No document ids is DocumentIdsRequired (a new code: the route's 400 carries none), before any rights query")]
    public async Task AuthorizePlaybookRun_NoDocumentIds_IsDocumentIdsRequired()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();

        var (direct, rest) = await DirectThenRestAsync(host, playbookId, Array.Empty<Guid>());

        direct.Should().BeSameAs(PlaybookRunAuthorization.DocumentIdsRequired);
        direct.ReasonCode.Should().Be(PlaybookAuthorizationFilter.DocumentIdsRequiredReasonCode);
        rest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.Playbooks.VerifyNoOtherCalls();
        host.VerifyNothingRan();
    }

    // ── No caller ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A null HttpContext is UserContextRequired")]
    public async Task AuthorizePlaybookRun_NullHttpContext_IsUserContextRequired()
    {
        var result = await PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(
            null, Guid.NewGuid(), new[] { Guid.NewGuid() }, null, CancellationToken.None);

        result.Should().BeSameAs(PlaybookRunAuthorization.UserContextRequired);
        result.ReasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    public enum NoCallerCase
    {
        /// <summary>An identity with an oid but no authentication type.</summary>
        Unauthenticated,

        /// <summary>An authenticated identity with no oid.</summary>
        NoOid,

        /// <summary>An authenticated identity whose oid is not a GUID (the route answers 401).</summary>
        NonGuidOid,
    }

    [Theory(DisplayName = "An unauthenticated principal, or one with no usable Entra oid, is UserContextRequired with no Dataverse call")]
    [InlineData(NoCallerCase.Unauthenticated)]
    [InlineData(NoCallerCase.NoOid)]
    [InlineData(NoCallerCase.NonGuidOid)]
    public async Task AuthorizePlaybookRun_NoSignedInCaller_IsUserContextRequired_AndNothingIsRead(NoCallerCase @case)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var principal = @case switch
        {
            NoCallerCase.Unauthenticated => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", CallerOid) })),
            NoCallerCase.NoOid => new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "Test")),
            _ => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "not-a-guid") }, "Test")),
        };

        var result = await PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(
            DirectContext(host, principal), playbookId, new[] { documentId }, null, CancellationToken.None);

        result.Should().BeSameAs(PlaybookRunAuthorization.UserContextRequired);
        host.Access.Calls.Should().BeEmpty("no rights question runs without a signed-in caller");
        host.Playbooks.VerifyNoOtherCalls();
        host.Nodes.VerifyNoOtherCalls();
    }

    // ── Inputs only a non-route host can give (one line each in notes/007-decisions.md) ─────────────────────────────

    [Fact(DisplayName = "The empty playbook id (the route's :guid binding never passes it) is PlaybookUnavailable with no Dataverse call")]
    public async Task AuthorizePlaybookRun_EmptyPlaybookId_IsPlaybookUnavailable_AndNothingIsRead()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var documentId = host.ReadableDocument();

        var result = await PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(
            DirectContext(host), Guid.Empty, new[] { documentId }, null, CancellationToken.None);

        result.Should().BeSameAs(PlaybookRunAuthorization.PlaybookUnavailable);
        host.Access.Calls.Should().BeEmpty();
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "A host with no IPlaybookService registered is refused PlaybookUnavailable (fail closed), never allowed")]
    public async Task AuthorizePlaybookRun_NoPlaybookService_IsPlaybookUnavailable()
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            User = Caller(),
        };
        http.Request.Headers.Authorization = "Bearer caller-token";

        var result = await PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(
            http, Guid.NewGuid(), new[] { Guid.NewGuid() }, null, CancellationToken.None);

        result.Should().BeSameAs(PlaybookRunAuthorization.PlaybookUnavailable);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the method directly, then sends the same input to the route, over the same substitutes. Asserts that both asked the
    /// access-data boundary the same questions in the same order (the route makes no extra or missing Dataverse call).
    /// </summary>
    private static async Task<(PlaybookRunAuthorization Direct, HttpResponseMessage Route)> DirectThenRestAsync(
        PlaybookAuthHost host, Guid playbookId, Guid[] documentIds, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var before = host.Access.Calls.Count;
        var direct = await PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(
            DirectContext(host), playbookId, documentIds, parameters, CancellationToken.None);
        var directCalls = host.Access.Calls.Skip(before).ToList();

        var middle = host.Access.Calls.Count;
        var rest = await host.SendAsync(ExecuteRequest(playbookId, documentIds, parameters));
        host.Access.Calls.Skip(middle).Should().Equal(directCalls,
            "the method and the route ask Dataverse the same questions in the same order");

        return (direct, rest);
    }

    /// <summary>
    /// A request context as a non-HTTP host holds it: the caller's principal and bearer token, and request services carrying the
    /// REAL authorization stack over the host's own substitutes (so a grant made on the host applies to both calls).
    /// </summary>
    private static HttpContext DirectContext(PlaybookAuthHost host, ClaimsPrincipal? principal = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IAccessDataSource>(host.Access);
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        services.AddScoped<AuthorizationService>();
        services.AddStatedNarrowingCaller(() => host.NarrowingMode, () => host.World);
        services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
        services.AddSingleton(host.Playbooks.Object);
        services.AddSingleton(host.Nodes.Object);

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider,
            User = principal ?? Caller(),
        };
        http.Request.Headers.Authorization = "Bearer caller-token";
        return http;
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(
            new[] { new Claim("oid", CallerOid), new Claim("tid", PlaybookAuthzTestAuthHandler.TenantId) }, "Test"));

    private static HttpRequestMessage ExecuteRequest(
        Guid playbookId, Guid[] documentIds, IReadOnlyDictionary<string, string>? parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/execute");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
        request.Content = JsonContent.Create(new { documentIds, parameters });
        return request;
    }

    private static async Task<string?> BodyCodeAsync(HttpResponseMessage response, string member) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())![member]?.GetValue<string>();
}
