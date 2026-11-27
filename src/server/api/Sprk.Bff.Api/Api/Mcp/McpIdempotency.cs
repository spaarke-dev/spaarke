using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Api.Mcp;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 031 (spec NFR-06; verifier fix round, F2: one retry helper).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: ITenantCache, and IdempotencyFilter's get-then-set lock (an endpoint filter bound to HTTP POST and the X-Idempotency-Key
//     header, so a tool cannot use it). This helper is the same store and the same lock for a tool call.
//   Extension: it holds the one copy of what StartAnalysisTool had inline (key hash, input fingerprint, read, lock, remember, unlock),
//     so tasks 030 and 036 call it instead of copying it.
//   Cost of doing nothing: each model-callable write would carry its own copy of the retry logic and its own bugs (task 031's review found
//     four in the first copy).
// =====================================================================================================================

/// <summary>What a retry key keeps: the fingerprint of the inputs that wrote, so the same key with different inputs is refused.</summary>
internal interface IMcpRetryEntry
{
    string Fingerprint { get; }
}

/// <summary>Where one tool keeps its retry entries and in-flight locks in <see cref="ITenantCache"/>, and the name its log lines carry.</summary>
internal sealed record McpIdempotencyScope(string ToolName, string RetryResource, string LockResource);

/// <summary>
/// The retry key of a model-callable reversible write (NFR-06; <see cref="McpToolAuthorization.IdempotencyKeyArgument"/>): one place for the
/// key's hash, the inputs' fingerprint, the stored entry, the in-flight lock and the order they run in.
/// </summary>
/// <remarks>
/// <para><b>What it does.</b> <see cref="RunOnceAsync{TEntry}"/> runs the write at most once per caller and key. Per tenant (<c>tid</c>), per
/// caller (<c>oid</c>) and key, the first successful write's <typeparamref name="TEntry"/> is kept for 24 hours with a fingerprint of the
/// inputs: the same key with the same inputs replays that entry without a second write; the same key with different inputs is refused
/// (<see cref="KeyReused"/>); a start still in progress under the key is refused with "try again shortly" (<see cref="KeyInProgress"/>). Only
/// a successful write is kept, so a refused call can be retried with the same key. The cache id is <c>{oid}:{SHA-256 of the key}</c>
/// under <c>tenant:{tid}:{resource}</c>: the key itself is never stored or logged, and caller B's key "k" is not caller A's.</para>
/// <para><b>Order.</b> read the entry, take the lock, read the entry AGAIN (a call that read before the first finished, then took the lock
/// after the first released it, replays instead of writing twice), run, store the entry, release the lock. The entry is stored and the lock
/// released with <see cref="CancellationToken.None"/>: a call whose caller has gone away has already written, and must still leave the key.</para>
/// <para><b>Limit, stated (D-031-1, #3231).</b> The lock is get-then-set, as <c>IdempotencyFilter</c>'s is: <see cref="ITenantCache"/> has
/// no set-if-absent, so two calls with the same key that arrive within one round trip can both pass it and both write. A cache fault fails
/// open (the write runs), as <c>IdempotencyFilter</c> does, so a cache outage can also let a retry write twice. Both belong to the cache
/// abstraction, not to a tool: the fix is an atomic set-if-absent on <see cref="ITenantCache"/>, used here and in the filter.</para>
/// </remarks>
internal static class McpIdempotency
{
    /// <summary>How long a successful write answers its retry key.</summary>
    internal static readonly TimeSpan RetryWindow = TimeSpan.FromHours(24);

    /// <summary>How long an in-flight call holds its key (a crashed call's lock expires).</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    /// <summary>The <c>reasonCode</c> of a retry key reused with different inputs: this host's own.</summary>
    internal const string KeyReusedReasonCode = "mcp.idempotency_key_reused";

    /// <summary>The <c>reasonCode</c> of a retry key whose first call has not finished: this host's own.</summary>
    internal const string KeyInProgressReasonCode = "mcp.idempotency_in_progress";

    private const int CacheVersion = 1;

    internal static McpToolError KeyReused { get; } = new(
        KeyReusedReasonCode,
        $"This {McpToolAuthorization.IdempotencyKeyArgument} was already used for a different request. Use a new {McpToolAuthorization.IdempotencyKeyArgument} for a new request.",
        StatusCodes.Status409Conflict);

    internal static McpToolError KeyInProgress { get; } = new(
        KeyInProgressReasonCode,
        $"A request with this {McpToolAuthorization.IdempotencyKeyArgument} is still in progress. Try again shortly with the same {McpToolAuthorization.IdempotencyKeyArgument}.",
        StatusCodes.Status409Conflict);

    /// <summary>The tenant the key lives under: the token's <c>tid</c> (<c>IdempotencyFilter</c>'s rule).</summary>
    internal static string TenantOf(HttpContext http)
    {
        var tid = http.User.FindFirst("tid")?.Value ?? http.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        return string.IsNullOrWhiteSpace(tid) ? "anonymous" : tid;
    }

    /// <summary>The cache id of a key: the caller's oid and a hash of the key (the key itself is never stored or logged).</summary>
    internal static string RetryIdOf(HttpContext http, string idempotencyKey) =>
        $"{CallerResolution.ResolveObjectId(http.User)}:{Hash(idempotencyKey)}";

    /// <summary>The inputs a retry must repeat, as one hash: the parts joined with <c>|</c>.</summary>
    internal static string FingerprintOf(params string[] parts) => Hash(string.Join('|', parts));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Runs <paramref name="run"/> at most once for this caller and <paramref name="idempotencyKey"/> (see the class remarks).
    /// <paramref name="replay"/> turns an earlier entry into the result (called only when its fingerprint equals
    /// <paramref name="fingerprint"/>); <paramref name="run"/> returns the call's result and, when it wrote, the entry to keep (<c>null</c>
    /// keeps nothing, so a refused or failed call can be retried).
    /// </summary>
    internal static async Task<CallToolResult> RunOnceAsync<TEntry>(
        McpIdempotencyScope scope,
        ITenantCache cache,
        HttpContext http,
        string idempotencyKey,
        string fingerprint,
        Func<TEntry, CallToolResult> replay,
        Func<Task<(CallToolResult Result, TEntry? Entry)>> run,
        ILogger logger,
        CancellationToken cancellationToken)
        where TEntry : class, IMcpRetryEntry
    {
        var tenantId = TenantOf(http);
        var retryId = RetryIdOf(http, idempotencyKey);

        CallToolResult Answer(TEntry earlier) =>
            string.Equals(earlier.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? replay(earlier)
                : McpToolResults.FromError(KeyReused, http.TraceIdentifier);

        // 1. A call this caller already made with this key: the same inputs replay it; different inputs are refused.
        if (await ReadAsync<TEntry>(cache, scope, tenantId, retryId, logger, cancellationToken) is { } earlierEntry)
        {
            return Answer(earlierEntry);
        }

        // 2. One call per key at a time (IdempotencyFilter's get-then-set lock; a cache fault fails open).
        if (!await TryLockAsync(cache, scope, tenantId, retryId, logger, cancellationToken))
        {
            return McpToolResults.FromError(KeyInProgress, http.TraceIdentifier);
        }

        try
        {
            // 3. Read again now the lock is ours: a call that read before the first one finished, and locked after it released,
            //    replays instead of writing a second time.
            if (await ReadAsync<TEntry>(cache, scope, tenantId, retryId, logger, cancellationToken) is { } finishedMeanwhile)
            {
                return Answer(finishedMeanwhile);
            }

            var (result, entry) = await run();
            if (entry is not null)
            {
                await RememberAsync(cache, scope, tenantId, retryId, entry, logger);
            }

            return result;
        }
        finally
        {
            await UnlockAsync(cache, scope, tenantId, retryId, logger);
        }
    }

    private static async Task<TEntry?> ReadAsync<TEntry>(
        ITenantCache cache, McpIdempotencyScope scope, string tenantId, string retryId, ILogger logger, CancellationToken ct)
        where TEntry : class
    {
        try
        {
            return await cache.GetAsync<TEntry>(tenantId, scope.RetryResource, retryId, CacheVersion, ct: ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("MCP tool {ToolName}: the retry-key read failed ({ExceptionType}); running without it.", scope.ToolName, ex.GetType().Name);
            return null;
        }
    }

    private static async Task<bool> TryLockAsync(
        ITenantCache cache, McpIdempotencyScope scope, string tenantId, string retryId, ILogger logger, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrEmpty(await cache.GetAsync<string>(tenantId, scope.LockResource, retryId, CacheVersion, ct: ct)))
            {
                return false;
            }

            await cache.SetAsync(tenantId, scope.LockResource, retryId, CacheVersion, value: "locked", ttl: LockDuration, ct: ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("MCP tool {ToolName}: the retry-key lock failed ({ExceptionType}); running without it.", scope.ToolName, ex.GetType().Name);
            return true;
        }
    }

    private static async Task RememberAsync<TEntry>(
        ITenantCache cache, McpIdempotencyScope scope, string tenantId, string retryId, TEntry entry, ILogger logger)
    {
        try
        {
            // Not the caller's token (as UnlockAsync): the write has happened, so a caller who goes away now must still leave the
            // key behind, or the model's retry writes a second time.
            await cache.SetAsync(tenantId, scope.RetryResource, retryId, CacheVersion, value: entry, ttl: RetryWindow, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("MCP tool {ToolName}: the retry-key write failed ({ExceptionType}).", scope.ToolName, ex.GetType().Name);
        }
    }

    private static async Task UnlockAsync(ITenantCache cache, McpIdempotencyScope scope, string tenantId, string retryId, ILogger logger)
    {
        try
        {
            // Not the caller's token: a call that is going away still frees its key (the lock would expire anyway).
            await cache.RemoveAsync(tenantId, scope.LockResource, retryId, CacheVersion, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("MCP tool {ToolName}: the retry-key unlock failed ({ExceptionType}).", scope.ToolName, ex.GetType().Name);
        }
    }
}
