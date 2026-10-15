using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// When the caller last read a Signal's decision plan, as the SERVER saw it (task 043, review K4). The decision-plan route
/// stamps it; the commit route reads it as the time the review started, which decides whether a system closure of the Signal
/// happened DURING the review (re-closed with the decision, step 6) or before it (a conflict). A client-sent time is never
/// trusted for that: a forged early time would let a decision overwrite any system closure.
/// </summary>
/// <remarks>
/// <para>The LATEST read wins: a wizard that re-reads the plan moves the start later, which can only turn a re-close into a
/// conflict (the fail-closed direction). No stamp (expired, never read, cache down) means the review started now.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the commit route's review-id ledger on
/// <see cref="ITenantCache"/>; nothing records a plan read. Extension: this is one more key family on that same tenant cache
/// (ADR-009), not a new store. Cost of doing nothing: the re-close rule trusts the request body's plan-read time.</para>
/// </remarks>
public sealed class DecisionPlanReadStamp
{
    internal const string Resource = "decision-plan-read";
    internal const int Version = 1;

    /// <summary>How long a plan read counts as the start of a review.</summary>
    internal static readonly TimeSpan Ttl = TimeSpan.FromHours(4);

    private readonly ITenantCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<DecisionPlanReadStamp> _logger;

    public DecisionPlanReadStamp(ITenantCache cache, TimeProvider clock, ILogger<DecisionPlanReadStamp> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal sealed record Stamp(DateTimeOffset ReadOn);

    /// <summary>Records that the signed-in caller read <paramref name="signalId"/>'s plan now. Best effort: never fails the read.</summary>
    public async Task StampAsync(HttpContext http, Guid signalId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var tenantId = TenantId(http);
        var callerObjectId = CallerResolution.ResolveObjectId(http.User);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(callerObjectId))
            return;

        await StampAtAsync(tenantId, callerObjectId, signalId, _clock.GetUtcNow(), ct).ConfigureAwait(false);
    }

    internal async Task StampAtAsync(string tenantId, string callerObjectId, Guid signalId, DateTimeOffset readOn, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(tenantId, Resource, Key(callerObjectId, signalId), Version, new Stamp(readOn), Ttl, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Without the stamp the commit treats the review as starting at commit time: stricter, never looser.
            _logger.LogWarning("Decision plan: the plan-read stamp could not be stored ({ExceptionType}).", ex.GetType().Name);
        }
    }

    /// <summary>The time the caller last read the plan, within <see cref="Ttl"/> and not in the future; otherwise null.</summary>
    public async Task<DateTimeOffset?> ReadAsync(string tenantId, string callerObjectId, Guid signalId, CancellationToken ct)
    {
        Stamp? stamp;
        try
        {
            stamp = await _cache.GetAsync<Stamp>(tenantId, Resource, Key(callerObjectId, signalId), Version, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the plan-read stamp could not be read ({ExceptionType}); the review starts now.", ex.GetType().Name);
            return null;
        }

        var now = _clock.GetUtcNow();
        return stamp is { ReadOn: var readOn } && readOn <= now && readOn >= now - Ttl ? readOn : null;
    }

    internal static string Key(string callerObjectId, Guid signalId) => $"{callerObjectId}:{signalId:D}";

    internal static string? TenantId(HttpContext http) =>
        http.User.FindFirst("tid")?.Value ?? http.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
}
