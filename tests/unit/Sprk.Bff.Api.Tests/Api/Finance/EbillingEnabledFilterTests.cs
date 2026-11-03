// ebilling-r1 task 040 (FR-45, ADR-018).
//
// ADR-038 note: these prove OBSERVABLE contracts, not DI wiring - (1) a real in-process HTTP pipeline returns
// the stable 503 `ebilling.disabled` ProblemDetails and never runs the handler when Ebilling:Enabled is false,
// passes through untouched when true, and never lets the flag replace the route's authorization filter;
// (2) the real AddFinanceModule options chain makes an out-of-range upload limit fail host STARTUP and leaves
// a host with no "Ebilling" section booting with Enabled=false.

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

public class EbillingEnabledFilterTests
{
    private const string ProbeRoute = "/probe";

    /// <summary>
    /// Starts a minimal real pipeline with one route. <paramref name="authorizationFilter"/> (when given) is
    /// chained BEFORE RequireEbillingEnabled(), the order every eBilling route uses.
    /// </summary>
    private static async Task<(WebApplication App, HttpClient Client, Func<int> HandlerCalls)> StartAsync(
        Dictionary<string, string?> settings,
        Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>>? authorizationFilter = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddOptions<EbillingOptions>()
            .Bind(builder.Configuration.GetSection(EbillingOptions.SectionName));

        var app = builder.Build();
        var handlerCalls = 0;

        var route = app.MapGet(ProbeRoute, () =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Results.Ok(new { reached = true });
        });
        if (authorizationFilter is not null)
        {
            route.AddEndpointFilter(authorizationFilter);
        }
        route.RequireEbillingEnabled();

        await app.StartAsync();
        return (app, app.GetTestClient(), () => Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public async Task FlagOff_Returns503FeatureDisabledProblem_AndDoesNotInvokeHandler()
    {
        var (app, client, handlerCalls) = await StartAsync(new() { ["Ebilling:Enabled"] = "false" });
        await using var _ = app;

        var response = await client.GetAsync(ProbeRoute);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("type").GetString().Should().Be(FeatureDisabledResults.TypeUri);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Feature Disabled");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("ebilling.disabled");
        handlerCalls().Should().Be(0);
    }

    [Fact]
    public async Task NoEbillingSection_DefaultsToDisabled_Returns503()
    {
        var (app, client, handlerCalls) = await StartAsync(new());
        await using var _ = app;

        var response = await client.GetAsync(ProbeRoute);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handlerCalls().Should().Be(0);
    }

    [Fact]
    public async Task FlagOn_InvokesHandler_AndAddsNothingToTheResponse()
    {
        var (app, client, handlerCalls) = await StartAsync(new() { ["Ebilling:Enabled"] = "true" });
        await using var _ = app;

        var response = await client.GetAsync(ProbeRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"reached":true}""");
        response.Headers.Should().BeEmpty();
        handlerCalls().Should().Be(1);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public async Task AuthorizationFilterRunsFirst_FlagNeverReplacesAuthorization(string flag)
    {
        // A denying authorization filter chained before the flag filter must win whether the flag is on or off:
        // the flag can only ADD a refusal, never grant or hide an authorization decision (ADR-018).
        var (app, client, handlerCalls) = await StartAsync(
            new() { ["Ebilling:Enabled"] = flag },
            (_, _) => ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden)));
        await using var _ = app;

        var response = await client.GetAsync(ProbeRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handlerCalls().Should().Be(0);
    }

    [Fact]
    public async Task ConfigChange_AppliesWithoutRestart_WhereHostReloadsConfiguration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ebilling:Enabled"] = "false" });
        builder.Services.AddOptions<EbillingOptions>()
            .Bind(builder.Configuration.GetSection(EbillingOptions.SectionName));
        var app = builder.Build();
        app.MapGet(ProbeRoute, () => Results.Ok()).RequireEbillingEnabled();
        await app.StartAsync();
        await using var _ = app;
        var client = app.GetTestClient();

        (await client.GetAsync(ProbeRoute)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Simulate a provider reload: change the value, then raise the configuration change token that
        // the options Bind registered and IOptionsMonitor listens to.
        app.Configuration["Ebilling:Enabled"] = "true";
        ((IConfigurationRoot)app.Configuration).Reload();

        (await client.GetAsync(ProbeRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------------------------------
    // Options: startup validation through the REAL AddFinanceModule registration.
    // ---------------------------------------------------------------------------------------------

    private static IHost BuildFinanceModuleHost(Dictionary<string, string?> settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, services) => services.AddFinanceModule(ctx.Configuration))
            .Build();

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    public async Task StartAsync_WhenMaxSubmissionFileSizeOutOfRange_FailsAtStartup(string value)
    {
        using var host = BuildFinanceModuleHost(new() { ["Ebilling:MaxSubmissionFileSizeMb"] = value });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(nameof(EbillingOptions.MaxSubmissionFileSizeMb));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("25")]
    [InlineData("100")]
    public async Task StartAsync_WhenMaxSubmissionFileSizeInRange_Boots(string value)
    {
        using var host = BuildFinanceModuleHost(new() { ["Ebilling:MaxSubmissionFileSizeMb"] = value });

        await host.StartAsync();

        host.Services.GetRequiredService<IOptions<EbillingOptions>>().Value
            .MaxSubmissionFileSizeMb.Should().Be(int.Parse(value));
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WithNoEbillingSection_BootsWithDefaults()
    {
        using var host = BuildFinanceModuleHost(new());

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<EbillingOptions>>().Value;
        options.Enabled.Should().BeFalse();
        options.MaxSubmissionFileSizeMb.Should().Be(25);
        await host.StopAsync();
    }
}
