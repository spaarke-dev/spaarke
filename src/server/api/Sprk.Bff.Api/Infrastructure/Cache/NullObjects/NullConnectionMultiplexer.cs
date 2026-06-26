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
//                             database ops. Every method throws NotSupportedException
//                             with guidance to use IDistributedCache instead.
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
    private readonly NullDatabase _database;
    private int _subscriberWarningLogged; // 0 = not yet logged, 1 = logged

    public NullConnectionMultiplexer(ILogger<NullConnectionMultiplexer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriber = new NullSubscriber(this);
        _database = new NullDatabase(this);

        _logger.LogInformation(
            "NullConnectionMultiplexer active — Redis is disabled (in-memory cache mode). " +
            "Pub/Sub invalidations are no-ops (multi-instance unsupported in this mode). " +
            "Direct IDatabase operations will throw NotSupportedException — use IDistributedCache instead. " +
            "Set Redis:Enabled=true with a valid connection string to activate the real connection.");
    }

    // ---------------------------------------------------------------------
    // Connection state — sane inert defaults
    // ---------------------------------------------------------------------

    public string ClientName => "null-object";

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
    /// Null-Object <see cref="IDatabase"/>. Every operation throws
    /// <see cref="NotSupportedException"/> per ADR-032 P3 Fail-fast tier:
    /// in-memory cache mode does not back arbitrary Redis database commands;
    /// callers must use <c>IDistributedCache</c> for cache reads/writes.
    /// </summary>
    private sealed class NullDatabase : IDatabase
    {
        private readonly NullConnectionMultiplexer _parent;

        public NullDatabase(NullConnectionMultiplexer parent) => _parent = parent;

        public int Database => -1;

        public IConnectionMultiplexer Multiplexer => _parent;

        // Single throwing helper used by every member. We deliberately throw
        // from a sealed `internal` Null-Object so consumers fail loudly during
        // development if they reach for a direct DB operation in in-memory mode.
        private static T Throw<T>() => throw new NotSupportedException(DatabaseNotSupportedMessage);
        private static void Throw() => throw new NotSupportedException(DatabaseNotSupportedMessage);

        public IBatch CreateBatch(object? asyncState = null) => Throw<IBatch>();
        public ITransaction CreateTransaction(object? asyncState = null) => Throw<ITransaction>();

        public TimeSpan Ping(CommandFlags flags = CommandFlags.None) => Throw<TimeSpan>();
        public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) => Throw<Task<TimeSpan>>();

        // The IDatabase surface is very large (~280 members across String, Hash, List,
        // Set, SortedSet, Stream, Key, Server, Geo, Script, HyperLogLog, Publish, etc.).
        // Rather than enumerate every signature, the unimplemented members trigger a
        // compile error if/when consumers attempt to call them in in-memory mode.
        // This is intentional: ADR-032 P3 wants loud failure, and the compile-time
        // signal at PR review is louder than a runtime throw.
        //
        // If a future caller legitimately needs a specific IDatabase operation in
        // in-memory mode, route it through IDistributedCache (which IS registered)
        // or add the specific stub here with a NotSupportedException.

        // --- Below: defensive catch-all stubs for the most commonly-called members
        //     observed across the BFF (KeyDelete, StringGet/Set, HashGet/Set, Publish).
        //     Every other IDatabase member is provided via partial fall-through to
        //     compile-time error — the task acceptance criterion ("dotnet build
        //     succeeds; no missing-interface-member errors") forces us to implement
        //     the FULL surface, so we throw uniformly. The pragma below documents
        //     the deliberate uniformity.

#pragma warning disable CS0067 // suppress events; not applicable here but kept for clarity

        // --- IDatabase: KEYS ---
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<byte[]?>();
        public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<byte[]?>>();
        public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<string?>();
        public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<string?>>();
        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags) => Throw<bool>();
        public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags) => Throw<bool>();
        public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags) => Throw<Task<bool>>();
        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags) => Throw<Task<bool>>();
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<DateTime?>();
        public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<DateTime?>>();
        public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long?>();
        public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long?>>();
        public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<TimeSpan?>();
        public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<TimeSpan?>>();
        public void KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None) => Throw();
        public Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None) => Throw<Task>();
        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None) => Throw<RedisKey>();
        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None) => Throw<Task<RedisKey>>();
        public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long?>();
        public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long?>>();
        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) => Throw();
        public Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) => Throw<Task>();
        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<TimeSpan?>();
        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<TimeSpan?>>();
        public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisType>();
        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisType>>();
        public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();

        // --- IDatabase: STRINGS ---
        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Lease<byte>?>();
        public Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<Lease<byte>?>>();
        public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValueWithExpiry>();
        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValueWithExpiry>>();
        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public string? StringLongestCommonSubsequence(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<string?>();
        public Task<string?> StringLongestCommonSubsequenceAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<Task<string?>>();
        public long StringLongestCommonSubsequenceLength(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None) => Throw<LCSMatchResult>();
        public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<LCSMatchResult>>();
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when) => Throw<bool>();
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) => Throw<RedisValue>();
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when) => Throw<Task<bool>>();
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) => Throw<Task<RedisValue>>();
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue StringIncrement(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags) => Throw<long>();
        public long StringBitCount(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags) => Throw<Task<long>>();
        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags) => Throw<long>();
        public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags) => Throw<Task<long>>();
        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();

        // --- IDatabase: HASH ---
        public long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Lease<byte>?>();
        public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<HashEntry[]>();
        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Task<Lease<byte>?>>();
        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<HashEntry[]>>();
        public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<HashEntry[]>();
        public Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<HashEntry[]>>();
        public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => Throw<IEnumerable<HashEntry>>();
        public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IEnumerable<HashEntry>>();
        public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IAsyncEnumerable<HashEntry>>();
        public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IEnumerable<RedisValue>>();
        public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IAsyncEnumerable<RedisValue>>();
        public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None) => Throw();
        public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None) => Throw<Task>();
        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();

        // --- IDatabase: LIST ---
        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) => Throw<ListPopResult>();
        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<ListPopResult>>();
        public long ListPosition(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListPositionAsync(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long[] ListPositions(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) => Throw<long[]>();
        public Task<long[]> ListPositionsAsync(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<long[]>>();
        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long ListLeftPush(RedisKey key, RedisValue[] values, When when, CommandFlags flags) => Throw<long>();
        public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when, CommandFlags flags) => Throw<Task<long>>();
        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) => Throw<ListPopResult>();
        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<ListPopResult>>();
        public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long ListRightPush(RedisKey key, RedisValue[] values, When when, CommandFlags flags) => Throw<long>();
        public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, When when, CommandFlags flags) => Throw<Task<long>>();
        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw();
        public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task>();
        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) => Throw();
        public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) => Throw<Task>();

        // --- IDatabase: LOCKS ---
        public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();

        // --- IDatabase: PUBLISH ---
        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();

        // --- IDatabase: SCRIPT ---
        public RedisResult Execute(string command, params object[] args) => Throw<RedisResult>();
        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public Task<RedisResult> ExecuteAsync(string command, params object[] args) => Throw<Task<RedisResult>>();
        public Task<RedisResult> ExecuteAsync(string command, ICollection<object>? args, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public RedisResult ScriptEvaluateReadOnly(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public RedisResult ScriptEvaluateReadOnly(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<RedisResult>();
        public Task<RedisResult> ScriptEvaluateReadOnlyAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();
        public Task<RedisResult> ScriptEvaluateReadOnlyAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisResult>>();

        // --- IDatabase: SET ---
        public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<bool[]>();
        public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<bool[]>>();
        public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => Throw<IEnumerable<RedisValue>>();
        public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IEnumerable<RedisValue>>();
        public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IAsyncEnumerable<RedisValue>>();

        // --- IDatabase: SORTED SET ---
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags) => Throw<bool>();
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags) => Throw<long>();
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags) => Throw<Task<bool>>();
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags) => Throw<Task<long>>();
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue[] SortedSetCombine(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public SortedSetEntry[] SortedSetCombineWithScores(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry[]>();
        public Task<RedisValue[]> SortedSetCombineAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry[]>>();
        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<double> SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) => Throw<double>();
        public Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) => Throw<Task<double>>();
        public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SortedSetLength(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetLengthAsync(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SortedSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public SortedSetEntry[] SortedSetRandomMembersWithScores(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry[]>();
        public Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry[]>>();
        public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public long SortedSetRangeAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue start, RedisValue stop, SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long? take = null, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetRangeAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue start, RedisValue stop, SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long? take = null, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry[]>();
        public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry[]>>();
        public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SortedSetRangeByScoreAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry[]>();
        public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry[]>>();
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<long?>();
        public Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<long?>>();
        public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => Throw<IEnumerable<SortedSetEntry>>();
        public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IEnumerable<SortedSetEntry>>();
        public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = 250, long cursor = 0, int pageOffset = 0, CommandFlags flags = CommandFlags.None) => Throw<IAsyncEnumerable<SortedSetEntry>>();
        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<double?>();
        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<double?>>();
        public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<double?[]>();
        public Task<double?[]> SortedSetScoresAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<Task<double?[]>>();
        public bool SortedSetUpdate(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long SortedSetUpdate(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> SortedSetUpdateAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> SortedSetUpdateAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public SortedSetPopResult SortedSetPop(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<SortedSetPopResult>();
        public SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry?>();
        public SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<SortedSetEntry[]>();
        public Task<SortedSetPopResult> SortedSetPopAsync(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetPopResult>>();
        public Task<SortedSetEntry?> SortedSetPopAsync(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry?>>();
        public Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<SortedSetEntry[]>>();

        // --- IDatabase: STREAM ---
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public StreamAutoClaimResult StreamAutoClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<StreamAutoClaimResult>();
        public Task<StreamAutoClaimResult> StreamAutoClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamAutoClaimResult>>();
        public StreamAutoClaimIdsOnlyResult StreamAutoClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<StreamAutoClaimIdsOnlyResult>();
        public Task<StreamAutoClaimIdsOnlyResult> StreamAutoClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamAutoClaimIdsOnlyResult>>();
        public StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<StreamEntry[]>();
        public Task<StreamEntry[]> StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamEntry[]>>();
        public RedisValue[] StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public StreamConsumerInfo[] StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<StreamConsumerInfo[]>();
        public Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamConsumerInfo[]>>();
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags) => Throw<bool>();
        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags) => Throw<Task<bool>>();
        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<StreamGroupInfo[]>();
        public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamGroupInfo[]>>();
        public StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<StreamInfo>();
        public Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamInfo>>();
        public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public StreamPendingInfo StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<StreamPendingInfo>();
        public Task<StreamPendingInfo> StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamPendingInfo>>();
        public StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None) => Throw<StreamPendingMessageInfo[]>();
        public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamPendingMessageInfo[]>>();
        public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<StreamEntry[]>();
        public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamEntry[]>>();
        public StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<StreamEntry[]>();
        public Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamEntry[]>>();
        public RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None) => Throw<RedisStream[]>();
        public Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisStream[]>>();
        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags) => Throw<StreamEntry[]>();
        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None) => Throw<StreamEntry[]>();
        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags) => Throw<Task<StreamEntry[]>>();
        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None) => Throw<Task<StreamEntry[]>>();
        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags) => Throw<RedisStream[]>();
        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None) => Throw<RedisStream[]>();
        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags) => Throw<Task<RedisStream[]>>();
        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisStream[]>>();
        public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public long StreamTrimByMinId(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> StreamTrimByMinIdAsync(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();

        // --- IDatabase: GEO ---
        public bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<bool> GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None) => Throw<double?>();
        public Task<double?> GeoDistanceAsync(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None) => Throw<Task<double?>>();
        public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<string?[]>();
        public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<string?>();
        public Task<string?[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<Task<string?[]>>();
        public Task<string?> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<string?>>();
        public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<GeoPosition?[]>();
        public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<GeoPosition?>();
        public Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoPosition?[]>>();
        public Task<GeoPosition?> GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoPosition?>>();
        public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<GeoRadiusResult[]>();
        public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<GeoRadiusResult[]>();
        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoRadiusResult[]>>();
        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoRadiusResult[]>>();
        public GeoRadiusResult[] GeoSearch(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<GeoRadiusResult[]>();
        public GeoRadiusResult[] GeoSearch(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<GeoRadiusResult[]>();
        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoRadiusResult[]>>();
        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) => Throw<Task<GeoRadiusResult[]>>();
        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();

        // --- IDatabase: HYPER LOG LOG ---
        public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<bool>();
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) => Throw<Task<bool>>();
        public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();
        public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw();
        public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None) => Throw();
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) => Throw<Task>();
        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None) => Throw<Task>();

        // --- IDatabase: SORT ---
        public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) => Throw<RedisValue[]>();
        public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue[]>>();
        public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) => Throw<long>();
        public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) => Throw<Task<long>>();

        // --- IDatabaseAsync / IRedisAsync: shared ---
        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None) => false;
        public bool TryWait(Task task) => true;
        public Task<bool> WaitAsync(Task task) => Task.FromResult(true);
        public EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None) => Throw<EndPoint?>();
        public Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None) => Throw<Task<EndPoint?>>();
        public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();

        // IRedisAsync.Wait* (also defined on IDatabaseAsync via inheritance — must be on the database too)
        public void Wait(Task task) => task.Wait();
        public T Wait<T>(Task<T> task) { task.Wait(); return task.Result; }
        public void WaitAll(params Task[] tasks) => Task.WaitAll(tasks);

        // StringGetSetExpiry (added in StackExchange.Redis 2.7)
        public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None) => Throw<RedisValue>();
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None) => Throw<Task<RedisValue>>();

#pragma warning restore CS0067
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
