using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Owns the SPE change-detection substrate for Compose R2 (FR-26): create / renew / delete
/// a Graph change-notification subscription on a container's drive root, and enumerate
/// changed driveItems via delta query. This is the "return-from-Word detection" plumbing
/// (design §2.5): a Word save fires the SPE webhook → the webhook receiver (task 053) calls
/// <see cref="EnumerateChangesAsync"/> to list what changed.
///
/// <para>
/// <b>ADR-007 isolation</b>: this class lives in <c>Services/Compose/</c> and NEVER
/// references a Microsoft.Graph type. All Graph calls go through
/// <see cref="ISpeFileOperations"/> and return the primitive/DTO shapes
/// (<see cref="SpeSubscriptionDto"/>, <see cref="SpeDeltaResult"/>).
/// </para>
///
/// <para>
/// <b>ADR-009 Redis state</b>: subscription id, expiry, delta link, and per-item etag are
/// cross-request and per-container, so they live in Redis (<see cref="IDistributedCache"/>),
/// not <c>IMemoryCache</c>. Mirrors <c>Services/GraphTokenCache</c> key-naming +
/// serialization. A multi-instance BFF therefore shares one subscription per container and
/// one authoritative delta token, avoiding double-subscribe / token loss.
/// </para>
///
/// <para>
/// <b>ADR-028 app-only</b>: the subscription lifecycle runs under the BFF managed identity
/// (no acting user for a background renewal); the facade uses <c>ForApp()</c>.
/// </para>
///
/// <para>
/// <b>Degradation</b>: a create/renew failure is caught, logged, and flips
/// <see cref="ComposeSyncState.FallbackToPolling"/> on the persisted state rather than
/// throwing. The explicit poll endpoint (task 053) reads that flag to keep change detection
/// alive when the webhook path is unhealthy.
/// </para>
/// </summary>
public class SpeSyncOrchestrator
{
    /// <summary>
    /// Maximum lifespan of an SPE driveItem change-notification subscription (Graph ceiling
    /// is 4230 minutes ≈ 2.94 days). New subscriptions are created / renewed to this horizon.
    /// </summary>
    internal static readonly TimeSpan SubscriptionLifetime = TimeSpan.FromMinutes(4230);

    /// <summary>
    /// Renew once the subscription is within this margin of expiry. Chosen comfortably wider
    /// than <see cref="SpeWebhookRenewalHostedService.ScanInterval"/> so the renewal cron
    /// never skips past the window between scans.
    /// </summary>
    internal static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(120);

    private const string StateKeyPrefix = "sdap:compose:sync:sub:";
    private const string IndexKey = "sdap:compose:sync:index";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISpeFileOperations _speFileStore;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SpeSyncOrchestrator> _logger;

    public SpeSyncOrchestrator(
        ISpeFileOperations speFileStore,
        IDistributedCache cache,
        IConfiguration configuration,
        ILogger<SpeSyncOrchestrator> logger,
        TimeProvider? timeProvider = null)
    {
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Ensures a live subscription exists for the container's drive. Creates one if absent or
    /// if the prior attempt fell back to polling. Idempotent: a healthy subscription is
    /// returned unchanged. On create failure the state is persisted with
    /// <see cref="ComposeSyncState.FallbackToPolling"/> = true and returned (no throw).
    /// </summary>
    public async Task<ComposeSyncState> EnsureSubscriptionAsync(string containerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);

        var existing = await LoadStateAsync(containerId, ct).ConfigureAwait(false);
        if (existing is { SubscriptionId: not null, FallbackToPolling: false })
        {
            return existing;
        }

        var driveId = existing?.DriveId;
        if (string.IsNullOrEmpty(driveId))
        {
            driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct).ConfigureAwait(false);
        }

        var notificationUrl = _configuration["Compose:Webhook:NotificationUrl"];
        var clientState = _configuration["Compose:Webhook:ClientState"] ?? string.Empty;

        if (string.IsNullOrEmpty(notificationUrl))
        {
            _logger.LogWarning(
                "Compose:Webhook:NotificationUrl is not configured; container {ContainerId} degrades to delta-poll fallback",
                containerId);
            return await PersistFallbackAsync(containerId, driveId!, existing, ct).ConfigureAwait(false);
        }

        try
        {
            var expiry = _timeProvider.GetUtcNow().Add(SubscriptionLifetime);
            var subscription = await _speFileStore
                .CreateDriveRootSubscriptionAsync(driveId!, notificationUrl, clientState, expiry, ct)
                .ConfigureAwait(false);

            var state = (existing ?? NewState(containerId, driveId!)) with
            {
                DriveId = driveId!,
                SubscriptionId = subscription.SubscriptionId,
                ExpiresAtUtc = subscription.ExpirationDateTime,
                FallbackToPolling = false,
                LastRenewedUtc = _timeProvider.GetUtcNow()
            };

            await SaveStateAsync(state, ct).ConfigureAwait(false);
            await AddToIndexAsync(containerId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Created SPE webhook subscription {SubscriptionId} for container {ContainerId} (drive {DriveId}); expires {Expiry:O}",
                subscription.SubscriptionId, containerId, driveId, subscription.ExpirationDateTime);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create SPE webhook subscription for container {ContainerId}; degrading to delta-poll fallback",
                containerId);
            return await PersistFallbackAsync(containerId, driveId!, existing, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Renews every tracked subscription that is inside the <see cref="RenewalMargin"/> of its
    /// expiry. Called on a cadence by <see cref="SpeWebhookRenewalHostedService"/>. Each
    /// subscription is renewed independently and try-wrapped: a single renewal failure flips
    /// that subscription to polling fallback and never aborts the sweep or crashes the host.
    ///
    /// <para>Marked <c>virtual</c> as a unit-test seam (mirrors
    /// <c>StaleCheckoutSweeperHostedService</c> mocking <c>DocumentCheckoutService</c>).</para>
    /// </summary>
    public virtual async Task RenewDueSubscriptionsAsync(CancellationToken ct = default)
    {
        var containerIds = await GetIndexAsync(ct).ConfigureAwait(false);
        if (containerIds.Count == 0)
        {
            _logger.LogDebug("SPE subscription renewal: no tracked containers");
            return;
        }

        var renewed = 0;
        var skipped = 0;
        var fellBack = 0;

        foreach (var containerId in containerIds)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var state = await LoadStateAsync(containerId, ct).ConfigureAwait(false);
            if (state is null || string.IsNullOrEmpty(state.SubscriptionId))
            {
                continue;
            }

            if (!IsRenewalDue(state))
            {
                skipped++;
                continue;
            }

            try
            {
                var newExpiry = _timeProvider.GetUtcNow().Add(SubscriptionLifetime);
                var subscription = await _speFileStore
                    .RenewSubscriptionAsync(state.SubscriptionId!, newExpiry, ct)
                    .ConfigureAwait(false);

                var updated = state with
                {
                    ExpiresAtUtc = subscription.ExpirationDateTime,
                    FallbackToPolling = false,
                    LastRenewedUtc = _timeProvider.GetUtcNow()
                };
                await SaveStateAsync(updated, ct).ConfigureAwait(false);
                renewed++;

                _logger.LogInformation(
                    "Renewed SPE webhook subscription {SubscriptionId} for container {ContainerId}; new expiry {Expiry:O}",
                    state.SubscriptionId, containerId, subscription.ExpirationDateTime);
            }
            catch (Exception ex)
            {
                fellBack++;
                var degraded = state with { FallbackToPolling = true };
                await SaveStateAsync(degraded, ct).ConfigureAwait(false);

                _logger.LogError(ex,
                    "Renewal failed for SPE subscription {SubscriptionId} (container {ContainerId}); flagged for delta-poll fallback",
                    state.SubscriptionId, containerId);
            }
        }

        _logger.LogInformation(
            "SPE subscription renewal sweep complete: {Renewed} renewed, {Skipped} healthy-skipped, {FellBack} fell back",
            renewed, skipped, fellBack);
    }

    /// <summary>
    /// Enumerates driveItems changed since the container's stored delta link, advances the
    /// stored delta link, and updates the per-item etag map (dropping items whose etag is
    /// unchanged since the last round). Returns the net changed items. Used by both the
    /// webhook receiver and the explicit poll endpoint (task 053).
    /// </summary>
    public async Task<IReadOnlyList<SpeDriveChange>> EnumerateChangesAsync(
        string containerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);

        var state = await LoadStateAsync(containerId, ct).ConfigureAwait(false);
        if (state is null || string.IsNullOrEmpty(state.DriveId))
        {
            var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct).ConfigureAwait(false);
            state = NewState(containerId, driveId);
        }

        var result = await _speFileStore
            .EnumerateDriveDeltaAsync(state.DriveId, state.DeltaLink, ct)
            .ConfigureAwait(false);

        var etags = new Dictionary<string, string>(state.ItemEtags, StringComparer.OrdinalIgnoreCase);
        var netChanges = new List<SpeDriveChange>();

        foreach (var change in result.Changes)
        {
            if (change.Deleted)
            {
                etags.Remove(change.ItemId);
                netChanges.Add(change);
                continue;
            }

            var etag = change.ETag ?? string.Empty;
            if (etags.TryGetValue(change.ItemId, out var previous) &&
                string.Equals(previous, etag, StringComparison.Ordinal))
            {
                // Same etag as last enumeration — no net change; skip.
                continue;
            }

            etags[change.ItemId] = etag;
            netChanges.Add(change);
        }

        var advanced = state with
        {
            DeltaLink = result.DeltaLink ?? state.DeltaLink,
            ItemEtags = etags
        };
        await SaveStateAsync(advanced, ct).ConfigureAwait(false);
        await AddToIndexAsync(containerId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Delta enumeration for container {ContainerId}: {Total} raw changes, {Net} net changes; delta link {Advanced}",
            containerId, result.Changes.Count, netChanges.Count, result.DeltaLink is null ? "unchanged" : "advanced");

        return netChanges;
    }

    /// <summary>
    /// Reads the persisted subscription state for a container (null if none). Exposed for the
    /// task-053 poll endpoint to inspect the <see cref="ComposeSyncState.FallbackToPolling"/>
    /// flag.
    /// </summary>
    public Task<ComposeSyncState?> GetStateAsync(string containerId, CancellationToken ct = default)
        => LoadStateAsync(containerId, ct);

    /// <summary>
    /// Reverse-resolves a Graph driveId (as carried in a change-notification's <c>resource</c>
    /// field, e.g. <c>drives/{driveId}/root</c>) back to the tracked SPE containerId whose
    /// subscription produced it. Redis keys this state by containerId (the business key), not
    /// driveId, and a Graph change notification only carries the drive/resource path — never the
    /// containerId — so the task-053 webhook receiver needs this reverse lookup to know which
    /// container to enumerate. Iterates the (typically small) tracked-container index; returns
    /// null when no tracked container matches (e.g. a stale/orphaned subscription notification),
    /// which the caller treats as "ignore, nothing to enumerate" rather than an error.
    /// </summary>
    public async Task<string?> ResolveContainerIdForDriveIdAsync(string driveId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(driveId);

        var containerIds = await GetIndexAsync(ct).ConfigureAwait(false);
        foreach (var containerId in containerIds)
        {
            var state = await LoadStateAsync(containerId, ct).ConfigureAwait(false);
            if (state is not null && string.Equals(state.DriveId, driveId, StringComparison.OrdinalIgnoreCase))
            {
                return containerId;
            }
        }

        return null;
    }

    /// <summary>
    /// True when a subscription is missing its expiry or is within <see cref="RenewalMargin"/>
    /// of expiring (per the injected <see cref="TimeProvider"/>). Unit tests drive the renewal
    /// window through this method via a fake time provider.
    /// </summary>
    internal bool IsRenewalDue(ComposeSyncState state)
    {
        if (state.ExpiresAtUtc is null)
        {
            return true;
        }

        return _timeProvider.GetUtcNow() >= state.ExpiresAtUtc.Value - RenewalMargin;
    }

    // ---- Redis state helpers (ADR-009) ------------------------------------------------

    private async Task<ComposeSyncState> PersistFallbackAsync(
        string containerId, string driveId, ComposeSyncState? existing, CancellationToken ct)
    {
        var state = (existing ?? NewState(containerId, driveId)) with
        {
            DriveId = driveId,
            FallbackToPolling = true
        };
        await SaveStateAsync(state, ct).ConfigureAwait(false);
        await AddToIndexAsync(containerId, ct).ConfigureAwait(false);
        return state;
    }

    private static ComposeSyncState NewState(string containerId, string driveId) => new()
    {
        ContainerId = containerId,
        DriveId = driveId
    };

    private async Task<ComposeSyncState?> LoadStateAsync(string containerId, CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(StateKeyPrefix + containerId, ct).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<ComposeSyncState>(json, JsonOptions);
    }

    private async Task SaveStateAsync(ComposeSyncState state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await _cache.SetStringAsync(StateKeyPrefix + state.ContainerId, json, ct).ConfigureAwait(false);
    }

    private async Task<List<string>> GetIndexAsync(CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(IndexKey, ct).ConfigureAwait(false);
        return json is null
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
    }

    private async Task AddToIndexAsync(string containerId, CancellationToken ct)
    {
        var index = await GetIndexAsync(ct).ConfigureAwait(false);
        if (index.Contains(containerId, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        index.Add(containerId);
        await _cache.SetStringAsync(IndexKey, JsonSerializer.Serialize(index, JsonOptions), ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Redis-persisted per-container SPE change-detection state (ADR-009). Plain data — no
/// Microsoft.Graph type — so it is safe to reference from <c>Services/Compose/</c>.
/// </summary>
public sealed record ComposeSyncState
{
    /// <summary>SPE container id (business key).</summary>
    public required string ContainerId { get; init; }

    /// <summary>Resolved drive id (<c>b!...</c>) for the container.</summary>
    public required string DriveId { get; init; }

    /// <summary>Active Graph subscription id, or null before the first successful create.</summary>
    public string? SubscriptionId { get; init; }

    /// <summary>Subscription expiry; renewal fires within <see cref="SpeSyncOrchestrator.RenewalMargin"/>.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>Opaque Graph <c>@odata.deltaLink</c> token for the next delta enumeration.</summary>
    public string? DeltaLink { get; init; }

    /// <summary>
    /// True when the webhook path is unhealthy (create/renew failed or no notification URL);
    /// the explicit poll endpoint (task 053) uses this to keep change detection alive.
    /// </summary>
    public bool FallbackToPolling { get; init; }

    /// <summary>Timestamp of the last successful create/renew.</summary>
    public DateTimeOffset? LastRenewedUtc { get; init; }

    /// <summary>Per-item etag map used to suppress no-op delta echoes.</summary>
    public Dictionary<string, string> ItemEtags { get; init; } = new();
}
