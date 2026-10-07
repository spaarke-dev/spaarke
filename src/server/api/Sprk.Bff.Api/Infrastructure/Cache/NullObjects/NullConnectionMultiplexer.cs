// spaarke-redis-cache-remediation-r1 — Task 004 (2026-06-25)
// Null-Object peer for IConnectionMultiplexer (ADR-032 mixed P2/P3 tiers).
//
// Registered by CacheModule when Redis:Enabled = false (in-memory cache mode
// for local dev / CI). Symmetric registration per ADR-032 ensures consumers
// that unconditionally inject IConnectionMultiplexer (MembershipCacheInvalidator,
// MembershipCacheInvalidationSubscriber, JobStatusService, SessionFilesCleanupJob)
// resolve cleanly without DI errors.
//
// Behavior contract:
//   - GetSubscriber():        returns NullSubscriber — P2 Quiet no-op for Pub/Sub.
//                             Publish* returns 0 (no subscribers reached).
//                             Subscribe* accepts callbacks but never delivers.
//                             Log-once warning on first GetSubscriber() call.
//   - GetDatabase():          returns NullDatabase — P3 Fail-fast for direct
//                             database ops. Every Redis command throws NotSupportedException
//                             with guidance to use IDistributedCache instead (the
//                             identity properties and inert wait helpers answer).
//   - Connection state:       safe inert defaults (IsConnected=false,
//                             ClientName="null-object", Configuration="").
//   - Events:                 unused (never raised) — backing fields accept
//                             handlers to satisfy the interface contract.
//
// Multi-instance limitation: Pub/Sub no-op means cross-process cache invalidation
// is disabled in dev. Documented in operational guide; dev is single-instance only
// (project CLAUDE.md "Pub/Sub no-op in dev").
//
// Reference: .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-redis-cache-remediation-r1/spec.md FR-04;
//            Services/Ai/Membership/NullMembershipCacheInvalidator.cs (pattern source).

using System.Net;
using System.Reflection;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace Sprk.Bff.Api.Infrastructure.Cache.NullObjects;

/// <summary>
/// Null-Object implementation of <see cref="IConnectionMultiplexer"/> per
/// <see href="../../../../../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md">ADR-032</see>.
/// Registered when <c>Redis:Enabled=false</c> so that consumers depending on
/// <c>IConnectionMultiplexer</c> (Pub/Sub or direct DB) still resolve from DI.
/// <para>
/// Pub/Sub operations are P2 Quiet no-ops (Publish returns 0; Subscribe never delivers).
/// Direct database operations are P3 Fail-fast (throw <see cref="NotSupportedException"/>).
/// Operators see a single warning log on first <see cref="GetSubscriber"/> call confirming
/// the kill-switch state.
/// </para>
/// </summary>
internal sealed class NullConnectionMultiplexer : IConnectionMultiplexer
{
    private const string DatabaseNotSupportedMessage =
        "In-memory cache mode does not support direct Redis database operations. Use IDistributedCache.";

    private readonly ILogger<NullConnectionMultiplexer> _logger;
    private readonly NullSubscriber _subscriber;
    private readonly IDatabase _database;
    private int _subscriberWarningLogged; // 0 = not yet logged, 1 = logged

    public NullConnectionMultiplexer(ILogger<NullConnectionMultiplexer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriber = new NullSubscriber(this);
        _database = NullDatabase.Create(this);

        _logger.LogInformation(
            "NullConnectionMultiplexer active — Redis is disabled (in-memory cache mode). " +
            "Pub/Sub invalidations are no-ops (multi-instance unsupported in this mode). " +
            "Direct IDatabase operations will throw NotSupportedException — use IDistributedCache instead. " +
            "Set Redis:Enabled=true with Redis:Endpoint (managed identity) to activate the real connection.");
    }

    // ---------------------------------------------------------------------
    // Connection state — sane inert defaults
    // ---------------------------------------------------------------------

    public string ClientName => "null-object";

    // Explicit non-null override: IConnectionMultiplexer.ToString() is declared non-nullable, but the
    // implicitly-inherited object.ToString() returns string? (CS8766). Return a stable inert label.
    public override string ToString() => nameof(NullConnectionMultiplexer);

    public string Configuration => string.Empty;

    public int TimeoutMilliseconds => 0;

    public long OperationCount => 0;

    public bool PreserveAsyncOrder { get; set; } = false;

    public bool IsConnected => false;

    public bool IsConnecting => false;

    public bool IncludeDetailInExceptions { get; set; } = false;

    public int StormLogThreshold { get; set; } = 0;

    // ---------------------------------------------------------------------
    // Events — interface requires; never raised. Backing fields accept
    // subscriptions but no notifications will ever fire from a Null connection.
    // ---------------------------------------------------------------------

    public event EventHandler<RedisErrorEventArgs>? ErrorMessage;

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed;

    public event EventHandler<InternalErrorEventArgs>? InternalError;

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored;

    public event EventHandler<EndPointEventArgs>? ConfigurationChanged;

    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast;

    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent;

    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved;

    // Silence "event is never used" warnings — handlers are accepted but never invoked.
    private void SuppressUnusedEventWarnings()
    {
        ErrorMessage?.Invoke(this, default!);
        ConnectionFailed?.Invoke(this, default!);
        InternalError?.Invoke(this, default!);
        ConnectionRestored?.Invoke(this, default!);
        ConfigurationChanged?.Invoke(this, default!);
        ConfigurationChangedBroadcast?.Invoke(this, default!);
        ServerMaintenanceEvent?.Invoke(this, default!);
        HashSlotMoved?.Invoke(this, default!);
    }

    // ---------------------------------------------------------------------
    // Subscriber + Database accessors
    // ---------------------------------------------------------------------

    public ISubscriber GetSubscriber(object? asyncState = null)
    {
        if (Interlocked.Exchange(ref _subscriberWarningLogged, 1) == 0)
        {
            _logger.LogWarning(
                "NullConnectionMultiplexer.GetSubscriber called — Pub/Sub is disabled (no-op). " +
                "Cross-instance cache invalidation will NOT propagate in this mode.");
        }
        return _subscriber;
    }

    public IDatabase GetDatabase(int db = -1, object? asyncState = null) => _database;

    // ---------------------------------------------------------------------
    // Server enumeration — returns empty arrays / no servers
    // ---------------------------------------------------------------------

    public EndPoint[] GetEndPoints(bool configuredOnly = false) => Array.Empty<EndPoint>();

    public IServer GetServer(string host, int port, object? asyncState = null) =>
        throw new NotSupportedException(DatabaseNotSupportedMessage);

    public IServer GetServer(string hostAndPort, object? asyncState = null) =>
        throw new NotSupportedException(DatabaseNotSupportedMessage);

    public IServer GetServer(IPAddress host, int port) =>
        throw new NotSupportedException(DatabaseNotSupportedMessage);

    public IServer GetServer(EndPoint endpoint, object? asyncState = null) =>
        throw new NotSupportedException(DatabaseNotSupportedMessage);

    public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None) =>
        throw new NotSupportedException(DatabaseNotSupportedMessage);

    public IServer[] GetServers() => Array.Empty<IServer>();

    // ---------------------------------------------------------------------
    // Configuration mutations — no-ops (no real connection to reconfigure)
    // ---------------------------------------------------------------------

    public Task<bool> ConfigureAsync(TextWriter? log = null) => Task.FromResult(false);

    public bool Configure(TextWriter? log = null) => false;

    public string GetStatus() => "NullConnectionMultiplexer (in-memory cache mode — no Redis connection)";

    public void GetStatus(TextWriter log)
    {
        if (log is null) return;
        log.WriteLine(GetStatus());
    }

    public string GetStormLog() => string.Empty;

    public void ResetStormLog() { /* no-op */ }

    public ServerCounters GetCounters() => new(endpoint: null);

    public int GetHashSlot(RedisKey key) => -1;

    public int HashSlot(RedisKey key) => -1;

    public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => 0;

    public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => Task.FromResult(0L);

    public void Wait(Task task) => task.Wait();

    public T Wait<T>(Task<T> task)
    {
        task.Wait();
        return task.Result;
    }

    public void WaitAll(params Task[] tasks) => Task.WaitAll(tasks);

    // ---------------------------------------------------------------------
    // Lifecycle / Profiling — accept calls but do nothing meaningful.
    // ---------------------------------------------------------------------

    public void Close(bool allowCommandsToComplete = true) { /* no-op */ }

    public Task CloseAsync(bool allowCommandsToComplete = true) => Task.CompletedTask;

    public void Dispose() { /* no-op */ }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) { /* no-op */ }

    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All) { /* no-op */ }

    public void AddLibraryNameSuffix(string suffix) { /* no-op */ }

    // ---------------------------------------------------------------------
    // Nested NullDatabase — P3 Fail-fast on every operation.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Null-Object <see cref="IDatabase"/>. Every Redis command throws
    /// <see cref="NotSupportedException"/> per ADR-032 P3 Fail-fast tier:
    /// in-memory cache mode does not back arbitrary Redis database commands;
    /// callers must use <c>IDistributedCache</c> for cache reads/writes.
    /// <para>
    /// Built as a <see cref="DispatchProxy"/> rather than ~460 hand-written stubs (task 242): the
    /// <see cref="IDatabase"/> surface grows with every StackExchange.Redis release (2.7 → 2.13 added
    /// ~160 members), and a hand-written stub stops compiling on each bump. The proxy keeps the previous
    /// stub's contract exactly: the identity properties (<c>Database</c> = -1, <c>Multiplexer</c>) and the
    /// inert connection/wait helpers (<c>IsConnected</c> = false, <c>TryWait</c>/<c>WaitAsync</c> = true,
    /// <c>Wait</c>/<c>WaitAll</c> wait on the given task) answer; every Redis command throws. New members
    /// are covered automatically.
    /// </para>
    /// </summary>
    internal class NullDatabase : DispatchProxy
    {
        private NullConnectionMultiplexer? _parent;

        public static IDatabase Create(NullConnectionMultiplexer parent)
        {
            var database = DispatchProxy.Create<IDatabase, NullDatabase>();
            ((NullDatabase)(object)database)._parent = parent;
            return database;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_Database" => -1,
                "get_Multiplexer" => _parent,
                "IsConnected" => false,
                "TryWait" => true,
                "WaitAsync" => Task.FromResult(true),
                "Wait" => WaitOn((Task)args![0]!, targetMethod),
                "WaitAll" => WaitAll((Task[])args![0]!),
                _ => throw new NotSupportedException(DatabaseNotSupportedMessage),
            };

        private static object? WaitOn(Task task, MethodInfo method)
        {
            task.Wait();
            // Wait<T>(Task<T>) returns the task's result; Wait(Task) returns nothing.
            return method.ReturnType == typeof(void) ? null : task.GetType().GetProperty("Result")!.GetValue(task);
        }

        private static object? WaitAll(Task[] tasks)
        {
            Task.WaitAll(tasks);
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Nested NullSubscriber — P2 Quiet no-op Pub/Sub.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Null-Object <see cref="ISubscriber"/>. Publish returns 0 (no
    /// subscribers reached). Subscribe accepts callbacks but never delivers
    /// — there is no real Redis connection backing this subscriber, so no
    /// messages can arrive. Designed for ADR-032 P2 Quiet tier: dev/CI
    /// continues to function while cross-instance Pub/Sub is disabled.
    /// </summary>
    private sealed class NullSubscriber : ISubscriber
    {
        private readonly NullConnectionMultiplexer _parent;

        public NullSubscriber(NullConnectionMultiplexer parent) => _parent = parent;

        public IConnectionMultiplexer Multiplexer => _parent;

        // --- Publish: returns 0 (no subscribers were notified) ---
        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => 0;
        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => Task.FromResult(0L);

        // --- Subscribe: accept handler but never deliver. Returns void/Task as appropriate. ---
        public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None) { /* no-op */ }
        public Task SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None) => Task.CompletedTask;
        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None) =>
            throw new NotSupportedException(
                "NullSubscriber does not support ChannelMessageQueue subscriptions. " +
                "Use the callback-based Subscribe overload (no-op) or enable Redis to receive messages.");
        public Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None) =>
            throw new NotSupportedException(
                "NullSubscriber does not support ChannelMessageQueue subscriptions. " +
                "Use the callback-based Subscribe overload (no-op) or enable Redis to receive messages.");

        public void Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler = null, CommandFlags flags = CommandFlags.None) { /* no-op */ }
        public Task UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler = null, CommandFlags flags = CommandFlags.None) => Task.CompletedTask;
        public void UnsubscribeAll(CommandFlags flags = CommandFlags.None) { /* no-op */ }
        public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None) => Task.CompletedTask;

        public EndPoint? SubscribedEndpoint(RedisChannel channel) => null;
        public bool IsConnected(RedisChannel channel = default) => false;

        // --- IRedisAsync members (ISubscriber : IRedis : IRedisAsync) ---
        public TimeSpan Ping(CommandFlags flags = CommandFlags.None) => TimeSpan.Zero;
        public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) => Task.FromResult(TimeSpan.Zero);
        public bool TryWait(Task task) => true;
        public void Wait(Task task) => task.Wait();
        public T Wait<T>(Task<T> task) { task.Wait(); return task.Result; }
        public void WaitAll(params Task[] tasks) => Task.WaitAll(tasks);
        public Task<bool> WaitAsync(Task task) => Task.FromResult(true);

        // --- ISubscriber-specific endpoint identification (by channel, not key) ---
        public EndPoint? IdentifyEndpoint(RedisChannel channel = default, CommandFlags flags = CommandFlags.None) => null;
        public Task<EndPoint?> IdentifyEndpointAsync(RedisChannel channel = default, CommandFlags flags = CommandFlags.None) => Task.FromResult<EndPoint?>(null);
    }
}
