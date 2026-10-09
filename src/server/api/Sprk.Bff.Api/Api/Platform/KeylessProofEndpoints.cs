using Spaarke.Contracts.Provisioning;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Diagnostics;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Api.Platform;

/// <summary>
/// <c>POST /api/platform/keyless-proof</c> (task 230b, owner D13): the BFF proves, with its own managed identity, one
/// real call to each Azure service of its stamp. Provisioning's acceptance gate (H13) calls it and refuses Ready
/// unless every service is <c>proved</c>.
/// </summary>
/// <remarks>
/// <para><b>Placement (CLAUDE.md §10).</b> In the BFF because only the stamp's user-assigned identity holds the
/// data-plane roles and it is usable only inside the BFF process (ADR-028) — the proof cannot run anywhere else. Not a
/// <c>/debug/*</c> route (ADR-028 bans them): it is an authenticated acceptance probe behind an application role only
/// the L2 Worker identity holds.</para>
/// <para><b>Response.</b> One <see cref="KeylessProbeResult"/> per service — service id, outcome, HTTP status, elapsed
/// time and a short code. No data, no secret, no exception text.</para>
/// <para>POST, not GET: each call costs (a few tokens of chat and embedding, one Content Safety text record each),
/// so it must not be cached or prefetched.</para>
/// <para><b><c>POST /api/platform/secure-record-isolation-census</c></b> (task 260, ISS-014): H13 also refuses Ready
/// unless the stamp's secure-record isolation census says <c>isolated</c>. The census was reachable only through the
/// admin job routes (SystemAdmin policy), which the L2 Worker does not — and should not — hold. It runs here, behind the
/// same application role, the SAME code the 15-minute job runs (<see cref="SecureRecordIsolationCensus"/>), read-only,
/// returning only the job's result (status, verdict, findings). Placement (CLAUDE.md §10, ADR-052): in the BFF because
/// the census reads Dataverse as the BFF's application user and the evaluator is BFF domain code; synchronous because
/// nine paged reads finish in seconds and H13 needs the answer within its own run.</para>
/// </remarks>
public static class KeylessProofEndpoints
{
    /// <summary>
    /// Maps the keyless-proof route — <see cref="KeylessProofContract.Route"/>, written as literals because the route
    /// authorization guard reads paths from source; the contract test posts to the contract's constant, so the two
    /// cannot differ unnoticed.
    /// </summary>
    public static IEndpointRouteBuilder MapKeylessProofEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform");
        group.MapPost("/keyless-proof", ProveAsync)
            .RequireAuthorization()
            .AddKeylessProofAuthorizationFilter()
            .RequireRateLimiting("job-submission")
            .WithTags("Platform")
            .WithName("KeylessProof")
            .WithSummary("Prove one managed-identity call per stamp service (provisioning acceptance)")
            .Produces<KeylessProofResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Task 260 (ISS-014): the secure-record isolation census, behind the SAME filter — the L2 Worker identity is the
        // only holder of the role, so no new role and no H3 change. Path literal for the route guard; the contract test
        // posts to KeylessProofContract.SecureRecordIsolationCensus.Route.
        group.MapPost("/secure-record-isolation-census", CensusAsync)
            .RequireAuthorization()
            .AddKeylessProofAuthorizationFilter()
            .RequireRateLimiting("job-submission")
            .WithTags("Platform")
            .WithName("SecureRecordIsolationCensus")
            .WithSummary("Run the read-only secure-record isolation census now (provisioning acceptance)")
            .Produces<SecureRecordIsolationCensusResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Bound on one synchronous census (nine paged reads). Below H13's own call timeout
    /// (<c>H13AcceptanceOptions.KeylessProofTimeout</c>, 90 s) so H13 receives <c>error</c> rather than a dropped call.
    /// </summary>
    internal static readonly TimeSpan CensusTimeout = TimeSpan.FromSeconds(60);

    private static async Task<IResult> ProveAsync(KeylessProofService service, CancellationToken cancellationToken)
    {
        var results = await service.ProveAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new KeylessProofResponse(results));
    }

    /// <summary>
    /// Runs <see cref="SecureRecordIsolationCensus"/> — the code the 15-minute job runs — and returns its result. Writes
    /// nothing. A read failure or timeout is <c>error</c> (HTTP 200, no exception text): isolation is unknown, never a pass.
    /// The job keeps the per-finding CRITICAL lines; this logs one summary line per call.
    /// </summary>
    internal static async Task<IResult> CensusAsync(
        IGenericEntityService dataverse,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(KeylessProofEndpoints).FullName + ".SecureRecordIsolationCensus");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CensusTimeout);
        try
        {
            var outcome = await SecureRecordIsolationCensus.EvaluateAsync(dataverse, configuration, timeout.Token)
                .ConfigureAwait(false);
            var result = SecureRecordIsolationCensus.ToResult(outcome);
            logger.Log(
                result.Status == KeylessProofContract.SecureRecordIsolationCensus.Isolated ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CENSUS] acceptance call status={Status} verdict={Verdict} findings={Findings}",
                result.Status, result.Verdict, result.Findings.Count);
            return TypedResults.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller went away
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[SECURE-CENSUS] acceptance call could not read the census (timeout {TimeoutSeconds}s) — isolation UNKNOWN, " +
                "reported as status=error.", CensusTimeout.TotalSeconds);
            return TypedResults.Ok(SecureRecordIsolationCensusResult.Unread);
        }
    }
}

/// <summary>The keyless-proof response: one result per service.</summary>
public sealed record KeylessProofResponse(IReadOnlyList<KeylessProbeResult> Services);
