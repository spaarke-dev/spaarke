using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Deterministic stub for <see cref="IPlaybookLookupService"/> for the by-code endpoint integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Tracks invocation count + simulated cold-path latency so the integration tests can verify
/// the endpoint-level 5-min memory cache (cold miss → service called; warm hit → service NOT called).
/// </para>
/// <para>
/// The endpoint's tenant scoping is implemented in the cache key (per ADR-008) — this stub does not
/// itself enforce tenant scoping because the real <see cref="PlaybookLookupService"/> doesn't either.
/// Tenant isolation is therefore a property of the <c>PlaybookEndpoints.GetPlaybookByCode</c> cache-key
/// shape, not the service. The cross-tenant 404 test verifies this by configuring the stub to throw
/// <see cref="PlaybookNotFoundException"/> for one tenant's code while returning a hit for another.
/// </para>
/// </remarks>
public sealed class StubPlaybookLookupService : IPlaybookLookupService
{
    private readonly Dictionary<string, PlaybookResponse> _codeToPlaybook = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _coldPathDelay = TimeSpan.Zero;
    private int _invocationCount = 0;
    private readonly object _lock = new();

    /// <summary>Total times <see cref="GetByCodeAsync"/> was invoked (across all tenants / codes).</summary>
    public int InvocationCount
    {
        get
        {
            lock (_lock)
            {
                return _invocationCount;
            }
        }
    }

    /// <summary>
    /// Configure a known code to resolve to a playbook response. Call before issuing the request.
    /// </summary>
    public void Setup(string code, PlaybookResponse playbook)
    {
        lock (_lock)
        {
            _codeToPlaybook[code] = playbook;
        }
    }

    /// <summary>
    /// Configure the cold-path simulated delay (default 0). Use to verify warm-cache &lt; cold-path latency.
    /// </summary>
    public void SetColdPathDelay(TimeSpan delay)
    {
        lock (_lock)
        {
            _coldPathDelay = delay;
        }
    }

    /// <summary>Reset all state between tests.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _codeToPlaybook.Clear();
            _coldPathDelay = TimeSpan.Zero;
            _invocationCount = 0;
        }
    }

    public async Task<PlaybookResponse> GetByCodeAsync(string playbookCode, CancellationToken ct = default)
    {
        TimeSpan delay;
        bool hit;
        PlaybookResponse? response;

        lock (_lock)
        {
            _invocationCount++;
            delay = _coldPathDelay;
            hit = _codeToPlaybook.TryGetValue(playbookCode, out response);
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        if (!hit || response is null)
        {
            throw new PlaybookNotFoundException($"Playbook with code '{playbookCode}' not found.");
        }

        return response;
    }

    public void ClearCache(string playbookCode) { /* no-op for stub */ }

    public void ClearAllCache() { /* no-op for stub */ }
}
