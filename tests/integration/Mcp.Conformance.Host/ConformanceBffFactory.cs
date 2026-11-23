using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Mcp;
using AspNetStartupFilter = Microsoft.AspNetCore.Hosting.IStartupFilter;

namespace Spaarke.Mcp.ConformanceHost;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 025, NFR-09). The real BFF (<c>Program</c>) with the A10 test scheme of
/// <see cref="McpProgramHostFactory"/>, on two real loopback Kestrel listeners:
/// <list type="bullet">
/// <item>the direct port takes requests exactly as they come; an unauthenticated one gets the route's own 401.</item>
/// <item>the injected port is the one the conformance CLI is pointed at. The CLI cannot send an Authorization header, so
/// <see cref="TokenInjector"/> adds a freshly signed test token (a new caller per request, so the per-caller 60/min rate limit of the
/// <c>mcp</c> policy is not what the suite measures) to a request that has none. The token passes through the REAL authorization pipeline:
/// the test key only replaces the signature key of a JwtBearer scheme; the ADR-028 A10 filter, the route's <c>RequireAuthorization()</c> and
/// the per-tool policies all run unchanged.</item>
/// </list>
/// Everything that lets a test token in is in tests/: no file under src/ names a test scheme, a switch or a bypass.
/// </summary>
internal sealed class ConformanceBffFactory : McpProgramHostFactory
{
    private readonly int _injectedPort;

    public ConformanceBffFactory(int directPort, int injectedPort)
    {
        _injectedPort = injectedPort;
        UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, directPort);
            options.Listen(IPAddress.Loopback, injectedPort);
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // WebApplicationFactory finds the content root from the test assembly's manifest; this Exe is not a test assembly, so name it.
        builder.UseContentRoot(FindBffContentRoot());
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddSingleton<AspNetStartupFilter>(new TokenInjector(_injectedPort))); // first in the pipeline: the token is there when UseAuthentication runs
    }

    /// <summary>Seeded fakes at the module boundary: the decision capability and the lookup search (Dataverse is not in this run).</summary>
    protected override void ConfigureSeededServices(IServiceCollection services)
    {
        services.RemoveAll<IDecisionCapability>();
        services.AddSingleton<IDecisionCapability, SeededDecisionCapability>();
        services.RemoveAll<IDecisionLookupSearch>();
        services.AddSingleton<IDecisionLookupSearch, SeededLookupSearch>();
    }

    private static string FindBffContentRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api");
            if (File.Exists(Path.Combine(candidate, "Sprk.Bff.Api.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/server/api/Sprk.Bff.Api was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Adds the test token on the injected port to a request that carries no Authorization header.</summary>
    private sealed class TokenInjector(int injectedPort) : AspNetStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                // Only the MCP route gets a token: every other route on the injected port (the BFF's REST surface) keeps the auth it would
                // have anywhere else, so a request that has none is a 401 there as well.
                if (context.Connection.LocalPort == injectedPort
                    && context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Headers.ContainsKey("Authorization"))
                {
                    context.Request.Headers.Authorization = "Bearer " + McpProgramHostFactory.MintToken(Guid.NewGuid());
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>A small seeded Decide lane for the tools the conformance run lists; nothing else is read or written.</summary>
internal sealed class SeededDecisionCapability : IDecisionCapability
{
    private static readonly Guid MatterId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly IReadOnlyList<WorklistItemSummary> Items =
    [
        .. Enumerable.Range(1, 3).Select(n => new WorklistItemSummary(
            Guid.Parse($"bbbbbbbb-0000-4000-8000-00000000000{n}"),
            MatterId,
            "Decide",
            "Active",
            $"Title {n}",
            $"Subject {n}",
            "Budget gap",
            null,
            "late",
            DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            null,
            null,
            null,
            false)),
    ];

    public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes) =>
        Task.FromResult(new SignalReadResult(SignalReadOutcome.Ok, new WorklistResponse("lanes", [])));

    public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) =>
        Task.FromResult(DecidePageResult.Ok(
            Items,
            new Dictionary<Guid, WorklistCoreSummary> { [MatterId] = new("Alpha matter", "MAT-0001") },
            nextCursor: null));

    public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
        Task.FromResult(DecisionPlanReadResult.AccessUnavailable);

    public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
        Task.FromResult(SuppressionReadResult.Unavailable);

    public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) =>
        throw new NotSupportedException("The conformance host seeds no commit path.");
}

internal sealed class SeededLookupSearch : IDecisionLookupSearch
{
    public Task<DecisionLookupResult> SearchAsync(HttpContext? http, Guid signalId, string? parameterCode, string? query, CancellationToken ct) =>
        Task.FromResult(DecisionLookupResult.InvalidParameter);
}
