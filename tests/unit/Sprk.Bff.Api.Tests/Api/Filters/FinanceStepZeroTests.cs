using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Filters;

/// <summary>
/// Step 0 inside <see cref="FinanceAuthorizationFilter"/> (ebilling-r1 task 099; procedure-and-file-model section 5.3):
/// on a route flagged <see cref="FinanceInternalCallersOnly"/>, a caller whose systemuser is flagged
/// <c>sprk_isexternal</c>, has no row, or whose flag or lookup faults is refused with the route's own denial BEFORE the
/// target declaration runs and before any record check; a flag that reads false or blank passes. Run against the REAL filter
/// and the REAL caller resolvers (substituted only at the Dataverse seam, <see cref="StatedCaller"/>). The route
/// wiring of every real <c>/api/finance</c> route is proven per route in the Finance contract tests and by
/// FinanceStepZeroCoverageGuardTests (ArchTests).
/// </summary>
public sealed class FinanceStepZeroTests
{
    private static readonly Guid RecordId = Guid.Parse("0a990000-0000-4000-8000-000000000001");
    private const string Set = "sprk_invoices";

    public static TheoryData<StepZeroCallerKind, FinanceDenial> RefusedKinds()
    {
        var data = new TheoryData<StepZeroCallerKind, FinanceDenial>();
        foreach (var kind in new[] { StepZeroCallerKind.FlagTrue, StepZeroCallerKind.NoSystemUserRow, StepZeroCallerKind.FlagReadFault, StepZeroCallerKind.LookupFault })
        {
            data.Add(kind, FinanceDenial.Forbidden);
            data.Add(kind, FinanceDenial.UniformNotFound);
        }

        return data;
    }

    [Theory(DisplayName = "Task 099 must-fire: a flagged-external, row-less or unreadable caller is refused with the route's own denial before the target declaration, any check or the handler")]
    [MemberData(nameof(RefusedKinds))]
    public async Task AFlaggedRoute_RefusesTheCaller_BeforeAnyReadOrCheck(StepZeroCallerKind kind, FinanceDenial denial)
    {
        await using var host = await Host.StartAsync(kind);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync(denial == FinanceDenial.Forbidden ? "/flagged/forbidden/" : "/flagged/uniform/", RecordId);

        if (denial == FinanceDenial.Forbidden)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var expected = kind is StepZeroCallerKind.FlagTrue or StepZeroCallerKind.NoSystemUserRow
                ? FinanceAuthorizationFilter.InternalCallersOnlyReasonCode
                : FinanceAuthorizationFilter.SystemFailureReasonCode;
            (await ReasonCodeAsync(response)).Should().Be(expected);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        }

        host.TargetDeclarations.Should().Be(0, "step 0 runs before the route's target declaration, which may read records");
        host.Access.Calls.Should().BeEmpty("no rights call may happen for a refused caller");
        host.HandlerCalls.Should().Be(0);
        host.StepZero.Queries.Should().NotBeEmpty("the refusal came from the step-0 lookup, not from anything else");
    }

    [Theory(DisplayName = "Task 099 must-not-fire: a flag that reads false or blank is internal; the route behaves as before and the verdict reads Internal")]
    [InlineData(StepZeroCallerKind.FlagFalse, FinanceDenial.Forbidden)]
    [InlineData(StepZeroCallerKind.FlagBlank, FinanceDenial.Forbidden)]
    [InlineData(StepZeroCallerKind.FlagFalse, FinanceDenial.UniformNotFound)]
    [InlineData(StepZeroCallerKind.FlagBlank, FinanceDenial.UniformNotFound)]
    public async Task AFlaggedRoute_AdmitsAnInternalCaller_AndSetsTheVerdict(StepZeroCallerKind kind, FinanceDenial denial)
    {
        await using var host = await Host.StartAsync(kind);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync(denial == FinanceDenial.Forbidden ? "/flagged/forbidden/" : "/flagged/uniform/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("\"verdict:Internal\"");
        host.TargetDeclarations.Should().Be(1);
        host.Access.Calls.Should().ContainSingle(c => c.Set == Set && c.Id == RecordId);
        host.StepZero.Queries.Should().HaveCount(2, "one oid lookup and one flag read");
    }

    [Theory(DisplayName = "Task 099 must-not-fire: an internal caller without rights still gets the route's record denial, and a refused request carries no verdict")]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    [InlineData(StepZeroCallerKind.FlagBlank)]
    public async Task AnInternalCallerWithoutRights_GetsTheRecordDenial_AndNoVerdict(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(kind);

        var response = await host.SendAsync("/flagged/forbidden/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Access.Calls.Should().ContainSingle();
        host.HandlerCalls.Should().Be(0);
        host.VerdictSeenOnDenial.Should().BeFalse("the verdict is set only when the filter lets the request through");
    }

    [Theory(DisplayName = "Task 099: an unflagged route runs no step-0 lookup, admits even a flagged-external caller as before, and carries no verdict")]
    [InlineData(StepZeroCallerKind.FlagTrue)]
    [InlineData(StepZeroCallerKind.LookupFault)]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    public async Task AnUnflaggedRoute_RunsNoStepZero(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(kind);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync("/unflagged/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("\"verdict:absent\"");
        host.StepZero.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 099 fail closed: a host with no caller resolvers registered refuses every caller on a flagged route")]
    public async Task MissingResolvers_Refuse()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagFalse, registerResolvers: false);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync("/flagged/forbidden/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.TargetDeclarations.Should().Be(0);
        host.Access.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 099: a caller with no oid gets the existing 401 before step 0 runs")]
    public async Task NoOid_Is401_BeforeStepZero()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagTrue);

        var response = await host.SendAsync("/flagged/forbidden/", RecordId, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.StepZero.Queries.Should().BeEmpty();
    }

    public static TheoryData<StepZeroCallerKind> RefusedKindsOnly()
    {
        var data = new TheoryData<StepZeroCallerKind>();
        foreach (var kind in StatedCaller.RefusedKinds)
        {
            data.Add(kind);
        }

        return data;
    }

    [Theory(DisplayName = "Task 099: on a UniformNotFound route a step-0 refusal (existing id) is indistinguishable from an internal caller's 404 for an absent id: same status, Content-Type and body but the correlation id")]
    [MemberData(nameof(RefusedKindsOnly))]
    public async Task UniformNotFound_StepZeroRefusal_MatchesAnInternalCallersAbsentIdNotFound(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(kind);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var refused = await host.SendAsync("/flagged/uniform/", RecordId);
        host.StepZero.Queries.Should().NotBeEmpty("the first response is a step-0 refusal");
        host.Access.Calls.Should().BeEmpty("a step-0 refusal asks no rights question");

        host.StepZero.Kind = StepZeroCallerKind.FlagFalse; // the same caller as an INTERNAL one, asking for an id nobody granted
        var absent = await host.SendAsync("/flagged/uniform/", Guid.NewGuid());
        host.Access.Calls.Should().ContainSingle("the internal caller reached the route's own rights check, which denied the absent id");

        await AssertIndistinguishableAsync(refused, absent);
    }

    /// <summary>
    /// Asserts <paramref name="stepZeroRefusal"/> and <paramref name="internalNotFound"/> are the same uniform 404: status,
    /// Content-Type and body equal once the correlation / trace ids are removed; and not vacuously (a non-empty ProblemDetails
    /// body carrying the record-unavailable reason code).
    /// </summary>
    internal static async Task AssertIndistinguishableAsync(HttpResponseMessage stepZeroRefusal, HttpResponseMessage internalNotFound)
    {
        stepZeroRefusal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        internalNotFound.StatusCode.Should().Be(stepZeroRefusal.StatusCode);
        internalNotFound.Content.Headers.ContentType?.ToString().Should().Be(stepZeroRefusal.Content.Headers.ContentType?.ToString());
        stepZeroRefusal.Content.Headers.ContentType.Should().NotBeNull();

        var refusedBody = await stepZeroRefusal.Content.ReadAsStringAsync();
        var internalBody = await internalNotFound.Content.ReadAsStringAsync();
        refusedBody.Should().Contain(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        WithoutTraceId(internalBody).Should().Be(WithoutTraceId(refusedBody));
    }

    internal static string WithoutTraceId(string body)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    private static async Task<string?> ReasonCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private int _targetDeclarations;
        private int _handlerCalls;

        public FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource Access { get; } = new();
        public StatedCaller StepZero { get; private set; } = null!;
        public int TargetDeclarations => _targetDeclarations;
        public int HandlerCalls => _handlerCalls;
        public bool VerdictSeenOnDenial { get; private set; }

        public static async Task<Host> StartAsync(StepZeroCallerKind kind, bool registerResolvers = true)
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();

            var throwaway = new ServiceCollection();
            host.StepZero = StatedCaller.Register(registerResolvers ? builder.Services : throwaway, kind);

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();

            var flagged = host._app.MapGroup("/flagged").RequireAuthorization().RequireInternalFinanceCaller();
            flagged.MapGet("/forbidden/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(host.Declare, FinanceDenial.Forbidden);
            flagged.MapGet("/uniform/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(host.Declare, FinanceDenial.UniformNotFound);
            host._app.MapGroup("/unflagged").RequireAuthorization()
                .MapGet("/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(host.Declare, FinanceDenial.Forbidden);

            // Observes the verdict on a request the finance filter DENIED (it runs outside the filter, after it returns).
            host._app.Use(async (ctx, next) =>
            {
                await next();
                if (ctx.Response.StatusCode >= 400 && FinanceAuthorizationFilter.StepZeroClassedInternal(ctx))
                {
                    host.VerdictSeenOnDenial = true;
                }
            });

            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public Task<HttpResponseMessage> SendAsync(string prefix, Guid id, bool authenticated = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, prefix + id);
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            return _client.SendAsync(request);
        }

        private FinanceAuthorizationTargets Declare(EndpointFilterInvocationContext context)
        {
            Interlocked.Increment(ref _targetDeclarations);
            return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = Set,
                RecordId = Guid.Parse(context.HttpContext.Request.RouteValues["id"]!.ToString()!),
                Operation = "finance.read",
                Source = "route.id",
            });
        }

        private IResult Handle(Guid id, HttpContext ctx)
        {
            Interlocked.Increment(ref _handlerCalls);
            return Results.Json(FinanceAuthorizationFilter.StepZeroClassedInternal(ctx) ? "verdict:Internal" : "verdict:absent");
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
