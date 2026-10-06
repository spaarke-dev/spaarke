using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Diagnostics;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

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
/// </remarks>
public static class KeylessProofEndpoints
{
    /// <summary>Maps the keyless-proof route.</summary>
    public static IEndpointRouteBuilder MapKeylessProofEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(KeylessProofContract.Route, ProveAsync)
            .RequireAuthorization()
            .AddKeylessProofAuthorizationFilter()
            .RequireRateLimiting("job-submission")
            .WithTags("Platform")
            .WithName("KeylessProof")
            .WithSummary("Prove one managed-identity call per stamp service (provisioning acceptance)")
            .Produces<KeylessProofResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ProveAsync(KeylessProofService service, CancellationToken cancellationToken)
    {
        var results = await service.ProveAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new KeylessProofResponse(results));
    }
}

/// <summary>The keyless-proof response: one result per service.</summary>
public sealed record KeylessProofResponse(IReadOnlyList<KeylessProbeResult> Services);
