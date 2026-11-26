using System.Net;
using System.Net.Http.Headers;
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
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// <see cref="FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync"/> (spaarkeai-mcp-bff-api-r2 task 008, spec FR-11): THE rule of
/// <c>GET /api/finance/matters/{matterId}/summary</c> — step 0, then <c>finance.read</c> on the matter — for a host that is not the route
/// (task 033's MCP tool). Each case calls the method directly, as task 033 will, over the REAL step-0 resolvers, <see cref="AuthorizationService"/>
/// and <see cref="OperationAccessRule"/>, substituted only at the Dataverse seams; each refusal is then sent to the REAL route under the same
/// caller and rights, and the two <c>reasonCode</c>s must be equal (FR-11 acceptance: "refused with the same reasonCode").
/// </summary>
/// <remarks>
/// For an internal caller without Read the route answers 403 <c>sdap.access.deny.insufficient_rights</c> (the route runs
/// <see cref="FinanceDenial.Forbidden"/>), not the uniform 404 the task POML assumed; the method returns what the route returns
/// (code wins, recorded in <c>projects/spaarkeai-mcp-bff-api-r2/notes/008-decisions.md</c>).
/// </remarks>
public sealed class MatterFinanceReadSharedMethodTests
{
    private const string Matters = FinanceAuthorizationFilter.MatterEntitySet;

    // ── Allowed ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "An internal caller (flag false or blank) with Read on the matter is Allowed; the one rights question is Record on sprk_matters(matterId) with the caller's token")]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    [InlineData(StepZeroCallerKind.FlagBlank)]
    public async Task AuthorizeMatterFinanceRead_InternalReader_IsAllowed(StepZeroCallerKind kind)
    {
        var matterId = Guid.NewGuid();
        var direct = new DirectCaller(kind);
        direct.Access.Grant(Matters, matterId, AccessRights.Read);

        var result = await direct.AuthorizeAsync(matterId);

        result.Should().BeSameAs(MatterFinanceReadAuthorization.Allowed);
        result.ReasonCode.Should().BeNull();
        direct.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Record, Matters, matterId, HasToken: true));

        await using var host = await FinanceAuthHost.StartAsync();
        host.StepZero.Kind = kind;
        host.Access.Grant(Matters, matterId, AccessRights.Read);
        host.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceSummaryDto { MatterId = matterId, CurrentSpend = 10m });
        (await host.SendAsync(SummaryRequest(matterId))).StatusCode.Should().Be(HttpStatusCode.OK, "the route admits the same caller");
    }

    // ── Step 0 refusals ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Step 0 refuses a flagged-external caller, one with no systemuser row, and one whose flag or row cannot be read, with the route's reasonCode and before any rights question")]
    [InlineData(StepZeroCallerKind.FlagTrue, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(StepZeroCallerKind.FlagReadFault, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(StepZeroCallerKind.LookupFault, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    public async Task AuthorizeMatterFinanceRead_StepZeroRefusal_CarriesTheRoutesReasonCode(StepZeroCallerKind kind, string expectedReasonCode)
    {
        var matterId = Guid.NewGuid();
        var direct = new DirectCaller(kind);
        direct.Access.Grant(Matters, matterId, AccessRights.Read); // readable: only step 0 can refuse

        var result = await direct.AuthorizeAsync(matterId);

        result.Outcome.Should().Be(MatterFinanceReadOutcome.Refused);
        result.ReasonCode.Should().Be(expectedReasonCode);
        direct.Caller.Queries.Should().NotBeEmpty("the refusal comes from the step-0 lookup");
        direct.Access.Calls.Should().BeEmpty("step 0 refuses before the record check");

        (await RestReasonCodeAsync(kind, matterId, host => host.Access.Grant(Matters, matterId, AccessRights.Read)))
            .Should().Be(result.ReasonCode);
    }

    // ── Record-check refusals ───────────────────────────────────────────────────────────────────────────────────────

    public enum RecordCase
    {
        /// <summary>Rights on the matter, but not Read.</summary>
        RightsWithoutRead,

        /// <summary>No rights at all on the matter (unreadable or absent: the same answer).</summary>
        NoRights,

        /// <summary>Dataverse gave no full answer and the record is not readable (a faulted snapshot carrying None).</summary>
        NoRightsOnAFault,

        /// <summary>The rights question threw.</summary>
        RightsQueryThrows,

        /// <summary>The empty id: no resolvable target.</summary>
        EmptyMatterId,
    }

    [Theory(DisplayName = "An internal caller the record check refuses gets the route's reasonCode (insufficient_rights, system_failure or no_target)")]
    [InlineData(RecordCase.RightsWithoutRead, FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(RecordCase.NoRights, FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(RecordCase.NoRightsOnAFault, FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(RecordCase.RightsQueryThrows, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(RecordCase.EmptyMatterId, FinanceAuthorizationFilter.NoTargetReasonCode)]
    public async Task AuthorizeMatterFinanceRead_RecordCheckRefusal_CarriesTheRoutesReasonCode(RecordCase @case, string expectedReasonCode)
    {
        var matterId = @case == RecordCase.EmptyMatterId ? Guid.Empty : Guid.NewGuid();
        var direct = new DirectCaller(StepZeroCallerKind.FlagFalse);
        Arrange(direct.Access, @case, matterId);

        var result = await direct.AuthorizeAsync(matterId);

        result.Outcome.Should().Be(MatterFinanceReadOutcome.Refused);
        result.ReasonCode.Should().Be(expectedReasonCode);

        (await RestReasonCodeAsync(StepZeroCallerKind.FlagFalse, matterId, host => Arrange(host.Access, @case, matterId)))
            .Should().Be(result.ReasonCode);
    }

    // ── No caller ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A null HttpContext is UserContextRequired")]
    public async Task AuthorizeMatterFinanceRead_NullHttpContext_IsUserContextRequired()
    {
        var result = await FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync(null, Guid.NewGuid(), CancellationToken.None);

        result.Should().BeSameAs(MatterFinanceReadAuthorization.UserContextRequired);
        result.ReasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    [Theory(DisplayName = "An unauthenticated principal, or an authenticated one with no Entra oid, is UserContextRequired with no Dataverse call")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AuthorizeMatterFinanceRead_NoSignedInCaller_IsUserContextRequired_AndNothingIsRead(bool authenticated, bool withOid)
    {
        var matterId = Guid.NewGuid();
        var direct = new DirectCaller(StepZeroCallerKind.FlagFalse);
        direct.Access.Grant(Matters, matterId, AccessRights.Read);
        var claims = withOid ? new[] { new Claim("oid", FinanceAuthzTestAuthHandler.CallerObjectId) } : Array.Empty<Claim>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null));

        var result = await direct.AuthorizeAsync(matterId, principal);

        result.Should().BeSameAs(MatterFinanceReadAuthorization.UserContextRequired);
        direct.Caller.Queries.Should().BeEmpty("no systemuser lookup runs without a signed-in caller");
        direct.Access.Calls.Should().BeEmpty("no rights question runs without a signed-in caller");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void Arrange(RecordingAccessDataSource access, RecordCase @case, Guid matterId)
    {
        switch (@case)
        {
            case RecordCase.RightsWithoutRead:
                access.Grant(Matters, matterId, AccessRights.AppendTo);
                break;
            case RecordCase.NoRightsOnAFault:
                access.Grant(Matters, matterId, AccessRights.AppendTo);
                access.Fault(Matters, matterId);
                break;
            case RecordCase.RightsQueryThrows:
                access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");
                break;
            case RecordCase.NoRights:
            case RecordCase.EmptyMatterId:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }
    }

    /// <summary>The <c>reasonCode</c> the REAL summary route returns for the same caller and rights (a 403; the route renders nothing else).</summary>
    private static async Task<string?> RestReasonCodeAsync(StepZeroCallerKind kind, Guid matterId, Action<FinanceAuthHost> arrange)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        host.StepZero.Kind = kind;
        arrange(host);

        var response = await host.SendAsync(SummaryRequest(matterId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNoServiceWasInvoked();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]?.GetValue<string>();
    }

    private static HttpRequestMessage SummaryRequest(Guid matterId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/matters/{matterId}/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    /// <summary>
    /// A request context as a non-HTTP host holds it: the caller's principal and bearer token, and request services carrying the REAL
    /// step-0 resolvers (<see cref="StatedCaller"/>), <see cref="AuthorizationService"/> and <see cref="OperationAccessRule"/> over the
    /// recording rights source.
    /// </summary>
    private sealed class DirectCaller
    {
        private readonly IServiceProvider _services;

        public DirectCaller(StepZeroCallerKind kind)
        {
            var services = new ServiceCollection().AddLogging();
            Caller = StatedCaller.Register(services, kind);
            services.AddSingleton<IAccessDataSource>(Access);
            services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            services.AddScoped<AuthorizationService>();
            services.AddSingleton<CallerRecordAccessProbe>(new RecordingPrivilegeProbe());
            _services = services.BuildServiceProvider().CreateScope().ServiceProvider;
        }

        public StatedCaller Caller { get; }

        public RecordingAccessDataSource Access { get; } = new();

        public Task<MatterFinanceReadAuthorization> AuthorizeAsync(Guid matterId, ClaimsPrincipal? principal = null)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = _services,
                User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("oid", FinanceAuthzTestAuthHandler.CallerObjectId) }, "Test")),
            };
            http.Request.Headers.Authorization = "Bearer caller-token";
            return FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync(http, matterId, CancellationToken.None);
        }
    }
}
