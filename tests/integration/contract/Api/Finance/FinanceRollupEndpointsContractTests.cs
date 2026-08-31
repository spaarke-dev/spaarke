using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// Contract tests for the Finance rollup recalculate endpoints
/// (<c>POST /api/finance/matters/{id}/recalculate</c> and the project sibling).
/// </summary>
/// <remarks>
/// <para>
/// Task 023 (code-quality-and-assurance-r3, spec FR-09): these endpoints WRITE derived
/// financial fields to Dataverse under the BFF app identity and were previously
/// <c>.AllowAnonymous()</c>. This test locks the closure: an <b>unauthenticated</b> request
/// MUST receive <c>401</c> — never <c>200</c>, and never an unhandled <c>500</c>. It is the
/// regression guard against re-introducing the anonymous Dataverse-write exposure.
/// </para>
/// <para>
/// <b>Hosting approach</b> (mirrors <c>AnalysisForkEndpointContractTests</c>, ADR-038): a minimal
/// in-process <see cref="WebApplication"/> mapping the REAL <c>MapFinanceRollupEndpoints</c> with a
/// fake authentication scheme (bearer present → authenticated; absent → <c>NoResult</c> → the
/// default <c>RequireAuthorization()</c> policy challenges with 401). The 401 fires at the auth
/// boundary BEFORE the handler, so <see cref="FinanceRollupService"/> is never invoked (its
/// Dataverse dependency is a never-called mock).
/// </para>
/// </remarks>
public class FinanceRollupEndpointsContractTests : IClassFixture<FinanceRollupEndpointsTestFixture>
{
    private readonly FinanceRollupEndpointsTestFixture _fx;

    public FinanceRollupEndpointsContractTests(FinanceRollupEndpointsTestFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task RecalculateMatter_Unauthenticated_Returns401AndDoesNotWrite()
    {
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsync(
            $"/api/finance/matters/{Guid.NewGuid()}/recalculate", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // The auth boundary short-circuits before the handler — no Dataverse write is attempted.
        _fx.DataverseMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecalculateProject_Unauthenticated_Returns401AndDoesNotWrite()
    {
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsync(
            $"/api/finance/projects/{Guid.NewGuid()}/recalculate", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fx.DataverseMock.VerifyNoOtherCalls();
    }
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with the REAL
/// <c>MapFinanceRollupEndpoints</c>, a fake auth scheme, and a no-op "dataverse-query" rate-limit
/// policy. <see cref="FinanceRollupService"/> is registered over a never-called mocked
/// <see cref="IDataverseService"/> so minimal-API parameter inference treats it as a service.
/// </summary>
public sealed class FinanceRollupEndpointsTestFixture : IAsyncLifetime, IDisposable
{
    public Mock<IDataverseService> DataverseMock { get; } = new(MockBehavior.Strict);

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = FinanceTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = FinanceTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FinanceTestAuthHandler>(
                FinanceTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("dataverse-query", _ =>
                RateLimitPartition.GetNoLimiter("dataverse-query-test"));
        });

        // The recalculate handlers inject FinanceRollupService. It is never reached in these
        // 401 tests (auth fails first), but registering the concrete type makes minimal-API
        // treat the parameter as a service and lets the app build.
        builder.Services.AddSingleton(new FinanceRollupService(
            DataverseMock.Object,
            new Mock<IFieldMappingDataverseService>().Object,
            NullLogger<FinanceRollupService>.Instance));

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapFinanceRollupEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public HttpClient CreateUnauthenticatedClient() => _app!.GetTestClient();

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }
}

/// <summary>
/// Fake auth handler: a request with a <c>Bearer</c> header authenticates as a test user;
/// a request without one returns <see cref="AuthenticateResult.NoResult"/> so the default
/// <c>RequireAuthorization()</c> policy challenges with 401.
/// </summary>
public sealed class FinanceTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FinanceTest";

    public FinanceTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") }, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
