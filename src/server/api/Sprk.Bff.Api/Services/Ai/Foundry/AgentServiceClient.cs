using System.Diagnostics;
using System.Runtime.CompilerServices;
using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Wraps the Azure AI Projects SDK (<see cref="AgentsClient"/>) to provide thread and run
/// operations for AI Foundry Agents.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Kill switch (ADR-018): every public method checks <see cref="AgentServiceOptions.Enabled"/>
///         and throws <see cref="FeatureDisabledException"/> when disabled.</item>
///   <item>Concurrency gate (ADR-016): a <see cref="SemaphoreSlim"/> limits concurrent Foundry
///         operations to <see cref="AgentServiceOptions.MaxConcurrency"/>. Callers that cannot
///         acquire within 30 s receive <see cref="ConcurrencyLimitExceededException"/> (HTTP 429).</item>
///   <item>Thread ID persistence (ADR-009): thread IDs are stored in Redis via
///         <see cref="IDistributedCache"/> with sliding expiry so resumable conversations
///         survive BFF restarts.</item>
///   <item>Data governance (ADR-015): only thread IDs, run IDs, timing, and status codes are
///         logged. Message content and model responses are never logged.</item>
///   <item>Additive (ADR-013): does NOT modify the existing direct-pipeline code paths.</item>
/// </list>
///
/// Lifetime: Singleton — <see cref="AgentsClient"/> is thread-safe, and the
/// <see cref="SemaphoreSlim"/> must be shared across all requests to enforce the global cap.
/// </summary>
public sealed class AgentServiceClient : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────
    private readonly AgentServiceOptions _options;

    // ── Azure AI Projects SDK ─────────────────────────────────────────────────
    // Lazily created so the singleton can be registered even when Enabled = false.
    private readonly Lazy<AgentsClient> _agentsClient;

    // ── Concurrency gate (ADR-016) ────────────────────────────────────────────
    // SemaphoreSlim with MaxConcurrency permits; WaitAsync timeout = 30 s.
    private readonly SemaphoreSlim _concurrencyGate;

    // Timeout to wait for the concurrency semaphore before raising 429.
    private static readonly TimeSpan ConcurrencyWaitTimeout = TimeSpan.FromSeconds(30);

    // ── Redis cache (ADR-009 + FR-05) ─────────────────────────────────────────
    private readonly ITenantCache _cache;

    // ITenantCache resource name. Final on-wire key:
    //     spaarke:tenant:{tenantId}:agent-thread:{conversationScope}:v2
    //
    // 🔴 THE ID IS THE CONVERSATION, AND IT IS NOT A CONSTANT (task 122, D-12 item 6, ADR-009).
    //
    // It used to be `thread` — a compile-time constant — so the whole key varied by tenantId alone. A
    // Foundry thread is a server-side CONVERSATION: CreateMessageAsync appends to it and the agent replies
    // with the whole accumulated thread as context. So two people in the same tenant, chatting within the
    // 60-minute TTL, rejoined the SAME conversation, and the agent's answer to the second was conditioned
    // on the first one's messages and could quote them back.
    //
    // The scope is the CONVERSATION, not the user, and that is the stronger choice: a chat session belongs
    // to exactly one user, so session-keying subsumes user-keying for the leak AND additionally stops one
    // user's unrelated questions sharing context (ask about Matter X, then Matter Y forty minutes later —
    // under user-keying X is still in the model's context).
    //
    // ⚠️ The tenant segment is NOT a customer boundary. Every Model 1 customer presents Spaarke's tenantId
    // (D-12), so it separates Entra tenants, not customers. Customer separation is the DEDICATED PER-CUSTOMER
    // REDIS INSTANCE — Redis access control is per-instance, not per-keyspace (ADR-009 as amended). Do not
    // read this key as providing it. See notes/D-14-customer-discriminator.md.
    //
    // Version bumped 1 → 2 because the key's MEANING changed: a v1 entry is a tenant-wide thread, and
    // silently resuming one under the new scheme would reintroduce the very sharing this removes.
    private const string CacheResource = "agent-thread";
    private const int CacheVersion = 2;

    // ── Managed Identity credential (UAMI-pinned via DI singleton) ────────────
    private readonly TokenCredential _credential;

    // ── Logging (ADR-015: IDs, timing, status only — never content) ───────────
    private readonly ILogger<AgentServiceClient> _logger;

    /// <summary>
    /// Cache key pattern for thread ID storage — tenant-scoped AND conversation-scoped.
    /// </summary>
    /// <remarks>
    /// Retained for tests asserting key composition. Runtime uses <c>ITenantCache</c> directly with
    /// <c>(tenantId, CacheResource, conversationScope, CacheVersion)</c>.
    /// <para>The <paramref name="conversationScope"/> is what makes two conversations two threads. Before
    /// task 122 this method took only a tenant and the id segment was the constant <c>thread</c>, which is
    /// why every user in a tenant shared one conversation.</para>
    /// </remarks>
    internal static string BuildThreadCacheKey(string tenantId, string conversationScope) =>
        $"agent-thread:{tenantId}:{conversationScope}";

    /// <summary>
    /// Validates a conversation scope. FAILS CLOSED (ADR-003): an absent scope is rejected rather than
    /// defaulted, because any shared default — a constant, an empty string, the tenant id — is exactly the
    /// defect task 122 removed, and it would return silently as "everyone shares one conversation".
    /// </summary>
    private static string RequireConversationScope(string conversationScope)
    {
        if (string.IsNullOrWhiteSpace(conversationScope))
        {
            throw new ArgumentException(
                "A conversation scope (chat session id, playbook run id, or equivalent) is required. "
                + "A Foundry thread is a conversation, so an unscoped thread is shared by every caller "
                + "that resumes it. There is deliberately no default — see task 122 / ADR-009.",
                nameof(conversationScope));
        }

        return conversationScope;
    }

    /// <summary>
    /// Initialises the client. The underlying <see cref="AgentsClient"/> is not created until
    /// the first operation so that startup does not fail when <see cref="AgentServiceOptions.Enabled"/>
    /// is false.
    /// </summary>
    public AgentServiceClient(
        IOptions<AgentServiceOptions> options,
        ITenantCache cache,
        TokenCredential credential,
        ILogger<AgentServiceClient> logger)
    {
        _options = options.Value;
        _cache = cache;
        _credential = credential;
        _logger = logger;

        // SemaphoreSlim: initialCount = maxCount = MaxConcurrency (ADR-016).
        _concurrencyGate = new SemaphoreSlim(
            initialCount: _options.MaxConcurrency,
            maxCount: _options.MaxConcurrency);

        // Lazily create the AgentsClient so DI wiring works even when the feature is disabled.
        _agentsClient = new Lazy<AgentsClient>(CreateAgentsClient, isThreadSafe: true);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the existing thread ID for THIS CONVERSATION from Redis, or creates a new Foundry
    /// thread and persists its ID.
    ///
    /// Cache key: <c>agent-thread:{tenantId}:{conversationScope}:v2</c>, with the TTL from
    /// <see cref="AgentServiceOptions.ThreadCacheExpiryMinutes"/> refreshed on each access.
    /// </summary>
    /// <param name="tenantId">Tenant identifier. ⚠️ NOT a customer boundary — every Model 1 customer
    /// presents Spaarke's tenant id (D-12); customer separation is the dedicated per-customer Redis
    /// instance.</param>
    /// <param name="conversationScope">
    /// Identifies the ONE conversation this thread belongs to — a chat session id, a playbook run id, or
    /// equivalent. REQUIRED, and deliberately has no default: a Foundry thread accumulates messages and
    /// the agent replies with the whole thread as context, so any shared scope means shared conversation
    /// content between callers.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Foundry thread ID (new or resumed).</returns>
    /// <exception cref="ArgumentException">When <paramref name="conversationScope"/> is absent (ADR-003 fail closed).</exception>
    /// <exception cref="FeatureDisabledException">When kill switch is off (ADR-018).</exception>
    /// <exception cref="ConcurrencyLimitExceededException">When concurrency gate times out (ADR-016).</exception>
    public async Task<string> CreateOrResumeThreadAsync(
        string tenantId,
        string conversationScope,
        CancellationToken cancellationToken = default)
    {
        var cacheId = RequireConversationScope(conversationScope);
        GuardEnabled();

        // OTEL span: ai.agent.create_or_resume_thread
        // ADR-015: tag only operation metadata — no tenant PII, no content.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.agent.create_or_resume_thread", ActivityKind.Client);

        await AcquireConcurrencyGateAsync(cancellationToken);
        try
        {
            // Try Redis first (ADR-009 Redis-first + FR-05 ITenantCache).
            // NOTE: ITenantCache supports only AbsoluteExpiration; the previous SlidingExpiration
            // semantic is now effective-absolute (TTL resets on every refresh write below).
            var cachedThreadId = await _cache.GetAsync<string>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
            if (!string.IsNullOrEmpty(cachedThreadId))
            {
                // ADR-015: log only ID — never content.
                _logger.LogDebug(
                    "Resumed existing Foundry thread: tenantId={TenantId}, threadId={ThreadId}",
                    tenantId, cachedThreadId);

                // Refresh expiry on every access by re-writing with the configured TTL.
                await SetThreadCacheAsync(tenantId, cacheId, cachedThreadId, cancellationToken);

                // ADR-015: thread.id is an opaque identifier, not PII or content.
                activity?.SetTag("agent.thread.id", cachedThreadId);
                activity?.SetTag("agent.thread.cache_hit", true);
                return cachedThreadId;
            }

            // Cache miss — create a new thread via the SDK.
            var sw = Stopwatch.StartNew();
            var response = await _agentsClient.Value
                .CreateThreadAsync(cancellationToken: cancellationToken);
            sw.Stop();

            var threadId = response.Value.Id;

            // ADR-015: log only thread ID and timing.
            _logger.LogInformation(
                "Created new Foundry thread: tenantId={TenantId}, threadId={ThreadId}, durationMs={DurationMs}",
                tenantId, threadId, sw.ElapsedMilliseconds);

            // ADR-015: thread.id is an opaque SDK-assigned identifier, not PII.
            activity?.SetTag("agent.thread.id", threadId);
            activity?.SetTag("agent.thread.cache_hit", false);
            activity?.SetTag("agent.thread.created_ms", sw.ElapsedMilliseconds);

            await SetThreadCacheAsync(tenantId, cacheId, threadId, cancellationToken);
            return threadId;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Creates a NEW Foundry thread that is never cached and never resumed.
    /// </summary>
    /// <remarks>
    /// For stateless, single-shot uses — the Code Interpreter sandbox is the shipped one — where the
    /// conversation must not outlive the call.
    /// <para>Added by task 122. It exists so that "I want a throwaway thread" has an honest way to say so.
    /// The previous approach was to call <see cref="CreateOrResumeThreadAsync"/> with a constant in the
    /// tenant argument, which cached and RESUMED under a key shared by every caller in every tenant — the
    /// exact opposite of ephemeral, while reading as ephemeral at the call site.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fresh Foundry thread ID. Not cached.</returns>
    /// <exception cref="FeatureDisabledException">When kill switch is off (ADR-018).</exception>
    /// <exception cref="ConcurrencyLimitExceededException">When concurrency gate times out (ADR-016).</exception>
    public async Task<string> CreateEphemeralThreadAsync(CancellationToken cancellationToken = default)
    {
        GuardEnabled();

        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.agent.create_ephemeral_thread", ActivityKind.Client);

        await AcquireConcurrencyGateAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            var response = await _agentsClient.Value
                .CreateThreadAsync(cancellationToken: cancellationToken);
            sw.Stop();

            var threadId = response.Value.Id;

            // ADR-015: thread ID and timing only.
            _logger.LogInformation(
                "Created ephemeral Foundry thread: threadId={ThreadId}, durationMs={DurationMs}",
                threadId, sw.ElapsedMilliseconds);

            activity?.SetTag("agent.thread.id", threadId);
            activity?.SetTag("agent.thread.ephemeral", true);
            activity?.SetTag("agent.thread.created_ms", sw.ElapsedMilliseconds);

            // Deliberately NOT cached. That is the whole point of this method.
            return threadId;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Appends a user message to an existing Foundry thread.
    ///
    /// ADR-015: message content is never logged; only thread ID and timing are recorded.
    /// </summary>
    /// <param name="threadId">Foundry thread ID (from <see cref="CreateOrResumeThreadAsync"/>).</param>
    /// <param name="message">User message text (not logged per ADR-015).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="FeatureDisabledException">When kill switch is off (ADR-018).</exception>
    /// <exception cref="ConcurrencyLimitExceededException">When concurrency gate times out (ADR-016).</exception>
    public async Task SendMessageAsync(
        string threadId,
        string message,
        CancellationToken cancellationToken = default)
    {
        GuardEnabled();

        // OTEL span: ai.agent.send_message
        // ADR-015: only thread.id and timing tagged — message content is never recorded.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.agent.send_message", ActivityKind.Client);
        activity?.SetTag("agent.thread.id", threadId);

        await AcquireConcurrencyGateAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();

            await _agentsClient.Value.CreateMessageAsync(
                threadId,
                MessageRole.User,
                message,
                cancellationToken: cancellationToken);

            sw.Stop();

            // ADR-015: log thread ID and timing only — never message content.
            _logger.LogDebug(
                "Sent user message to Foundry thread: threadId={ThreadId}, durationMs={DurationMs}",
                threadId, sw.ElapsedMilliseconds);

            activity?.SetTag("agent.send_message.duration_ms", sw.ElapsedMilliseconds);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Creates a streaming run on the specified thread using the configured agent, and yields
    /// text delta tokens as they arrive from the Foundry service.
    ///
    /// The semaphore is held for the full duration of the stream so that the concurrency cap
    /// applies to long-running streaming operations (ADR-016).
    ///
    /// ADR-015: only thread ID, run ID, timing, and final status are logged.
    ///          The token stream content is never logged.
    /// </summary>
    /// <param name="threadId">Foundry thread ID containing the conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of text delta tokens.</returns>
    /// <exception cref="FeatureDisabledException">When kill switch is off (ADR-018).</exception>
    /// <exception cref="ConcurrencyLimitExceededException">When concurrency gate times out (ADR-016).</exception>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string threadId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GuardEnabled();

        // OTEL span: ai.agent.stream_response
        // Span covers the full streaming run lifecycle (thread creation → final token).
        // ADR-015: only thread.id, run.id, and timing are tagged — token content is never recorded.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.agent.stream_response", ActivityKind.Client);
        activity?.SetTag("agent.thread.id", threadId);
        activity?.SetTag("agent.agent_id", _options.AgentId);

        // Acquire the semaphore before entering the streaming loop.
        // Held for the full stream duration to bound concurrent long-running Foundry runs.
        await AcquireConcurrencyGateAsync(cancellationToken);

        string? runId = null;
        var sw = Stopwatch.StartNew();
        var tokenCount = 0;

        try
        {
            // ADR-015: log only IDs — never content.
            _logger.LogDebug(
                "Starting streaming run: threadId={ThreadId}, agentId={AgentId}",
                threadId, _options.AgentId);

            var streamingUpdates = _agentsClient.Value.CreateRunStreamingAsync(
                threadId,
                _options.AgentId,
                cancellationToken: cancellationToken);

            await foreach (var update in streamingUpdates
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                // Capture run ID from the first run update (ADR-015: log only the ID).
                if (runId is null && update is RunUpdate runUpdate)
                {
                    runId = runUpdate.Value.Id;
                    // ADR-015: run.id is an opaque SDK-assigned identifier, not PII.
                    activity?.SetTag("agent.run.id", runId);
                    _logger.LogDebug(
                        "Foundry run started: threadId={ThreadId}, runId={RunId}",
                        threadId, runId);
                }

                // Yield text delta tokens to the caller.
                // ADR-015: content is never logged — only passed upstream.
                if (update is MessageContentUpdate contentUpdate &&
                    contentUpdate.TextAnnotation is null &&
                    !string.IsNullOrEmpty(contentUpdate.Text))
                {
                    tokenCount++;
                    yield return contentUpdate.Text;
                }
            }

            sw.Stop();
            // ADR-015: log only IDs, timing, and completion status.
            _logger.LogInformation(
                "Foundry streaming run completed: threadId={ThreadId}, runId={RunId}, durationMs={DurationMs}",
                threadId, runId ?? "unknown", sw.ElapsedMilliseconds);

            // ADR-015: token count is metadata (not content); duration is latency telemetry.
            activity?.SetTag("agent.stream.duration_ms", sw.ElapsedMilliseconds);
            activity?.SetTag("agent.stream.token_count", tokenCount);
            activity?.SetTag("agent.stream.status", "completed");
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Evicts the cached thread ID for ONE CONVERSATION, forcing <see cref="CreateOrResumeThreadAsync"/>
    /// to create a fresh thread on the next call. Use when that conversation should be restarted.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="conversationScope">
    /// The conversation to evict — the same scope passed to <see cref="CreateOrResumeThreadAsync"/>.
    /// Required for the same reason: before task 122 this evicted the single tenant-wide thread, so one
    /// caller restarting its conversation restarted everyone's.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InvalidateThreadCacheAsync(
        string tenantId,
        string conversationScope,
        CancellationToken cancellationToken = default)
    {
        var cacheId = RequireConversationScope(conversationScope);
        GuardEnabled();
        await _cache.RemoveAsync(tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
        _logger.LogInformation(
            "Invalidated Foundry thread cache for tenant={TenantId}, conversation={ConversationScope}",
            tenantId, conversationScope);
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        _concurrencyGate.Dispose();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks the kill switch (ADR-018). Throws <see cref="FeatureDisabledException"/> if disabled.
    /// Called at the top of every public method before any other work.
    /// </summary>
    private void GuardEnabled()
    {
        if (!_options.Enabled)
        {
            throw new FeatureDisabledException(
                "AgentService is disabled via kill switch (AgentService:Enabled = false). " +
                "Set this to true in configuration to enable Azure AI Foundry Agent operations.");
        }
    }

    /// <summary>
    /// Attempts to acquire the concurrency semaphore with the configured timeout (ADR-016).
    /// Throws <see cref="ConcurrencyLimitExceededException"/> on timeout.
    /// </summary>
    private async Task AcquireConcurrencyGateAsync(CancellationToken cancellationToken)
    {
        var acquired = await _concurrencyGate.WaitAsync(ConcurrencyWaitTimeout, cancellationToken);
        if (!acquired)
        {
            _logger.LogWarning(
                "Foundry concurrency gate timed out after {TimeoutSeconds}s. " +
                "MaxConcurrency={MaxConcurrency}. Returning 429.",
                ConcurrencyWaitTimeout.TotalSeconds, _options.MaxConcurrency);

            throw new ConcurrencyLimitExceededException(
                $"AgentService concurrency limit ({_options.MaxConcurrency}) exceeded. " +
                "Retry after a short delay.");
        }
    }

    /// <summary>
    /// Writes the thread ID to the tenant cache. Post FR-05 migration (ITenantCache) the wrapper
    /// supports only AbsoluteExpiration; sliding semantics are emulated by re-writing on every
    /// cache HIT in <see cref="CreateOrResumeThreadAsync"/>.
    /// </summary>
    private Task SetThreadCacheAsync(
        string tenantId,
        string cacheId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var expiry = TimeSpan.FromMinutes(_options.ThreadCacheExpiryMinutes);
        return _cache.SetAsync(
            tenantId, CacheResource, cacheId, CacheVersion,
            threadId, expiry, ct: cancellationToken);
    }

    /// <summary>
    /// Creates the <see cref="AgentsClient"/> using <see cref="DefaultAzureCredential"/>
    /// (Managed Identity in Azure, developer credential locally — consistent with the
    /// <c>AzureOpenAIClient</c> pattern in <c>AiModule.cs</c>).
    ///
    /// ADR-015: endpoint URI is logged; credentials are never logged.
    /// </summary>
    private AgentsClient CreateAgentsClient()
    {
        // R6 Wave B-G11 (2026-06-10) — use-site validation for AgentServiceOptions.
        // The [Required] attributes on Endpoint + AgentId were removed (mirror of
        // B-G8 BingGroundingOptions hardening) so DI startup doesn't crash in tests
        // or in Spaarke Dev when Foundry isn't configured. Validation moved here
        // because this method only runs on the FIRST actual use (Lazy<AgentsClient>);
        // GuardEnabled() upstream already prevents reaching here when Enabled=false.
        if (_options.Endpoint is null)
        {
            throw new InvalidOperationException(
                "AgentService:Endpoint must be set in configuration when " +
                "AgentService:Enabled=true. See AgentServiceOptions.Endpoint XML doc.");
        }
        if (string.IsNullOrWhiteSpace(_options.AgentId))
        {
            throw new InvalidOperationException(
                "AgentService:AgentId must be set in configuration when " +
                "AgentService:Enabled=true. See AgentServiceOptions.AgentId XML doc.");
        }

        _logger.LogInformation(
            "Initialising AgentsClient: endpoint={Endpoint}",
            _options.Endpoint);

        return new AgentsClient(_options.Endpoint.ToString(), _credential);
    }
}
