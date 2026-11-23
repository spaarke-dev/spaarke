using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Filters;

/// <summary>
/// <see cref="FinanceDenial.UniformNotFoundForRecord"/> (ebilling-r1 task 123, inverted in fix round 1): exactly two deny reasons keep their 403
/// <c>reasonCode</c> (<c>internal_callers_only</c> and <c>system_failure</c>); EVERY other deny reason is the uniform 404, so a reason the mode
/// does not list can never expose itself. The REAL filter over test routes (the upload route is the only production route in this mode).
/// </summary>
public sealed class FinanceUniformNotFoundForRecordTests
{
    private static readonly Guid RecordId = Guid.Parse("0a123000-0000-4000-8000-000000000001");
    private const string Set = "sprk_invoices";

    [Fact(DisplayName = "123 fix round 1 (inverted, must fire): a deny reason that is neither a step-0 refusal nor a system failure (a table privilege not held) is the uniform 404, never its own 403")]
    public async Task AnyOtherReason_IsTheUniform404()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagFalse);

        var response = await host.SendAsync("/privilege/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.HandlerCalls.Should().Be(0);
    }

    [Fact(DisplayName = "123 (must fire): no rights on the record is the uniform 404")]
    public async Task NoRights_IsTheUniform404()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagFalse);

        var response = await host.SendAsync("/record/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
    }

    [Fact(DisplayName = "123 (the other direction, must fire): a system failure (the target declaration threw) keeps its 403 system_failure")]
    public async Task SystemFailure_Is403()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagFalse);

        var response = await host.SendAsync("/throws/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeAsync(response)).Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.HandlerCalls.Should().Be(0);
    }

    [Theory(DisplayName = "123 (the other direction, must fire): a step-0 refusal keeps its 403 internal_callers_only, a step-0 fault its 403 system_failure")]
    [InlineData(StepZeroCallerKind.FlagTrue, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(StepZeroCallerKind.FlagReadFault, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    public async Task StepZero_Is403(StepZeroCallerKind kind, string reasonCode)
    {
        await using var host = await Host.StartAsync(kind);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync("/record/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeAsync(response)).Should().Be(reasonCode);
        host.HandlerCalls.Should().Be(0);
    }

    [Fact(DisplayName = "123 (must not fire): the caller with the rights reaches the handler")]
    public async Task Allowed_ReachesTheHandler()
    {
        await using var host = await Host.StartAsync(StepZeroCallerKind.FlagFalse);
        host.Access.Grant(Set, RecordId, AccessRights.Read);

        var response = await host.SendAsync("/record/", RecordId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.HandlerCalls.Should().Be(1);
    }

    private static async Task<string?> ReasonCodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>();

    /// <summary>A probe whose caller holds no table privilege (Dataverse answered "not held").</summary>
    private sealed class NoPrivilegeProbe() : CallerRecordAccessProbe(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<bool> CallerHoldsPrivilegeAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private int _handlerCalls;

        public FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource Access { get; } = new();
        public int HandlerCalls => _handlerCalls;

        public static async Task<Host> StartAsync(StepZeroCallerKind kind)
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
            builder.Services.AddSingleton<CallerRecordAccessProbe>(new NoPrivilegeProbe());
            StatedCaller.Register(builder.Services, kind);

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();

            var group = host._app.MapGroup("/t").RequireAuthorization().RequireInternalFinanceCaller();
            group.MapGet("/record/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(
                ctx => Check(ctx, FinanceCheckPath.Record, "finance.read"), FinanceDenial.UniformNotFoundForRecord);
            group.MapGet("/privilege/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(
                ctx => Check(ctx, FinanceCheckPath.Privilege, FinanceAuthorizationFilter.CreateInvoicePrivilege), FinanceDenial.UniformNotFoundForRecord);
            group.MapGet("/throws/{id:guid}", host.Handle).AddFinanceAuthorizationFilter(
                _ => throw new InvalidOperationException("declaration fault"), FinanceDenial.UniformNotFoundForRecord);

            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        private static FinanceAuthorizationTargets Check(EndpointFilterInvocationContext context, FinanceCheckPath path, string operation) =>
            FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
            {
                Path = path,
                EntitySetName = Set,
                RecordId = Guid.Parse(context.HttpContext.Request.RouteValues["id"]!.ToString()!),
                Operation = operation,
                Source = "route.id",
            });

        public Task<HttpResponseMessage> SendAsync(string prefix, Guid id)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/t" + prefix + id);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            return _client.SendAsync(request);
        }

        private IResult Handle(Guid id)
        {
            Interlocked.Increment(ref _handlerCalls);
            return Results.Ok();
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
