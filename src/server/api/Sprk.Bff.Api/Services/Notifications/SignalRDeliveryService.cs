using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Notifications;

/// <summary>
/// The signal-only payload the spine pushes over SignalR (spec NFR-02/NFR-03). Carries ONLY
/// routing/signal information — the outbox row id (so the client knows WHICH pending row changed)
/// and the closed <see cref="NotificationKind"/> discriminator (so it knows WHAT kind fired). It
/// deliberately carries NO message body, NO privileged content, NO snippet, and NO pre-authorized
/// action token. Clients re-fetch the full envelope through the access-checked BFF poll endpoint
/// (task 022) — the spine is a "changed → fetch" accelerator, never an authorization bypass.
/// </summary>
/// <remarks>
/// The envelope shape (<see cref="CommunicationEnvelope"/> / <see cref="SuggestionEnvelope"/>) lives
/// in the outbox (task 013) and is NEVER placed on the wire here. This type is intentionally the
/// smallest possible routing tuple — a reflection assertion in the seam test pins its exact shape.
/// </remarks>
public sealed record NotificationSignal(
    [property: JsonPropertyName("outboxRowId")] Guid OutboxRowId,
    [property: JsonPropertyName("kind")] NotificationKind Kind);

/// <summary>
/// Connection info returned by the negotiate endpoint (spec FR-04). The client access URL + a
/// short-lived access token minted by the Management SDK, scoped server-side to a SINGLE user's oid.
/// </summary>
public sealed record SignalRConnectionInfo(string Url, string AccessToken);

/// <summary>
/// Layer-C SignalR delivery leg of the notification spine (spec FR-04 / NFR-04). Sends an
/// at-most-once, best-effort, SIGNAL-ONLY ping to a connected client AFTER a producer has durably
/// written its outbox row (task 012 <see cref="OutboxService.WriteAsync{TEnvelope}"/>), and mints
/// per-user connection info for the negotiate endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Azure SignalR Serverless mode</b> (<c>Microsoft.Azure.SignalR.Management</c>, transient
/// transport) per the FR-01 spike (<c>notes/spikes/fr-01-signalr-footprint.md</c>). There is NO
/// hosted hub and NO <c>IHubContext</c>/<c>MapHub</c>: the Management SDK's
/// <see cref="ServiceHubContext"/> is the producer entrypoint — built lazily on first use, so no
/// persistent outbound service connection is held at startup (≈zero cold-start impact per the spike).
/// </para>
/// <para>
/// <b>Write-before-ping is structural, not by discipline.</b> Every ping entrypoint REQUIRES an
/// <c>outboxRowId</c> — a value that only exists AFTER the durable outbox write — so a producer
/// cannot call ping without having written first (spec MUST rule / root CLAUDE.md
/// "outbox BEFORE ping"; ADR-041/043 store-before-render).
/// </para>
/// <para>
/// <b>Best-effort at-most-once.</b> A transient SignalR failure is swallowed and logged — it NEVER
/// fails the producer's own success path (the outbox row is the durable truth; task 022's poll
/// fallback delivers it). This is why the Null-Object degrade path (<see cref="NullSignalRDeliveryService"/>)
/// is a quiet no-op on ping (ADR-032 P2) rather than a throwing kill-switch.
/// </para>
/// <para>
/// <b>ADR-010/032 Null-Object shape.</b> Concrete class (NOT sealed) with a <c>protected</c>
/// logger-only ctor and <c>virtual</c> entrypoints, so <see cref="NullSignalRDeliveryService"/>
/// subclasses it — no interface introduced solely for the Null-Object (ADR-032 preferred pattern).
/// Producers (tasks 024/040/042/050) inject the concrete <see cref="SignalRDeliveryService"/>; DI
/// resolves either the real impl or the Null-Object depending on configuration.
/// </para>
/// </remarks>
public class SignalRDeliveryService
{
    /// <summary>The SignalR client method name clients subscribe to (<c>connection.on("notification", …)</c>).</summary>
    private const string SignalMethod = "notification";

    private readonly ILogger<SignalRDeliveryService> _logger;
    private readonly Lazy<Task<ServiceHubContext>> _lazyHub;

    /// <summary>
    /// Resolves the recipient's Dataverse <c>systemuserid</c> to the Azure AD <c>oid</c> that
    /// <see cref="NegotiateAsync"/> registered the connection under (the SignalR user identity). Null on
    /// the Null-Object path (which overrides <see cref="PingUserAsync"/> to a pure no-op and never uses it).
    /// </summary>
    private readonly ISystemUserIdentityResolver? _identityResolver;

    /// <summary>
    /// Production ctor. Builds the Serverless <see cref="ServiceHubContext"/> lazily from
    /// <paramref name="options"/> on first ping/negotiate — never at construction/startup.
    /// </summary>
    public SignalRDeliveryService(
        IOptions<SignalRDeliveryOptions> options,
        ISystemUserIdentityResolver identityResolver,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identityResolver);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var opts = options.Value;
        _identityResolver = identityResolver;
        _logger = loggerFactory.CreateLogger<SignalRDeliveryService>();
        _lazyHub = new Lazy<Task<ServiceHubContext>>(() => BuildHubContextAsync(opts));
    }

    /// <summary>
    /// ADR-032 Null-Object / test-double ctor — minimal deps, per ADR-032 "keep Null-Object constructors
    /// minimal". The lazy hub is unusable and never reached because the Null-Object overrides every
    /// entrypoint; a test double overrides <see cref="GetHubContextAsync"/> to supply a boundary double and
    /// supplies a resolver double so the real <see cref="PingUserAsync"/> logic runs. The Null-Object passes
    /// <paramref name="identityResolver"/> as <c>null</c> (it does not resolve — its ping is a pure no-op).
    /// </summary>
    protected SignalRDeliveryService(ISystemUserIdentityResolver? identityResolver, ILogger<SignalRDeliveryService> logger)
    {
        _identityResolver = identityResolver;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lazyHub = new Lazy<Task<ServiceHubContext>>(() =>
            Task.FromException<ServiceHubContext>(new InvalidOperationException(
                "This SignalRDeliveryService instance has no SignalR configuration (Null-Object or test double); the hub context is unavailable.")));
    }

    /// <summary>
    /// Sends an at-most-once, signal-only ping to the recipient identified by their Dataverse
    /// <paramref name="recipientSystemUserId"/> (the outbox <c>OwnerId</c>) AFTER the caller has durably
    /// written outbox row <paramref name="outboxRowId"/>. The <c>systemuserid</c> is resolved to the Azure
    /// AD <c>oid</c> that the negotiate endpoint registered the connection under (via
    /// <see cref="ISystemUserIdentityResolver"/>, cached), then dispatched to <c>Clients.User(oid)</c>.
    /// When the recipient has no AAD mapping the ping is a quiet no-op (the outbox + poll fallback carry the
    /// durable truth). Best-effort: a delivery failure is logged and swallowed. Requiring
    /// <paramref name="outboxRowId"/> makes the write-before-ping ordering structural (spec MUST rule).
    /// </summary>
    /// <param name="outboxRowId">The <c>sprk_notificationoutbox</c> row id returned by the producer's prior write. MUST be non-empty.</param>
    /// <param name="recipientSystemUserId">The recipient's Dataverse <c>systemuserid</c> (== the outbox row's <c>OwnerId</c>). Resolved server-side to the target oid.</param>
    /// <param name="kind">The closed notification kind discriminator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="outboxRowId"/> is empty (ping-before-write).</exception>
    public virtual async Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
    {
        RequireWrittenRow(outboxRowId);

        // Resolve systemuserid → oid (the SignalR user identity negotiate registered). Producers key by the
        // durable outbox OwnerId (systemuserid); SignalR connections are keyed by the JWT oid.
        var oid = await _identityResolver!.ResolveOidAsync(recipientSystemUserId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(oid))
        {
            // No AAD mapping (unmapped/disabled user, or empty recipient) — quiet no-op. The outbox row is
            // the durable truth and task 022's poll fallback delivers it; a missing live signal is not
            // caller-visible error state (NFR-04).
            _logger.LogInformation(
                "SignalR user-ping skipped: recipient systemuserid {RecipientSystemUserId} has no Azure AD oid mapping " +
                "(quiet no-op; outbox row {OutboxRowId} + poll fallback carry the durable truth).",
                recipientSystemUserId, outboxRowId);
            return;
        }

        var signal = new NotificationSignal(outboxRowId, kind);
        try
        {
            var hub = await GetHubContextAsync(ct).ConfigureAwait(false);
            await hub.Clients.User(oid).SendAsync(SignalMethod, signal, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort at-most-once (NFR-04): a live-signal failure is NOT caller-visible error state.
            _logger.LogWarning(ex,
                "Best-effort SignalR user-ping failed (non-fatal; outbox row {OutboxRowId} + poll fallback carry the durable truth).",
                outboxRowId);
        }
    }

    /// <summary>
    /// Group-targeting primitive (<c>Clients.Group(name)</c>) for task 023 to build fan-out on top of.
    /// Same signal-only, best-effort, write-before-ping contract as <see cref="PingUserAsync"/>. This
    /// task exposes the primitive ONLY — the actual fan-out/targeting decision logic (which users, which
    /// group membership) is task 023's job and is NOT designed here.
    /// </summary>
    /// <param name="outboxRowId">The outbox row id backing this signal. MUST be non-empty.</param>
    /// <param name="groupName">The SignalR group to target.</param>
    /// <param name="kind">The closed notification kind discriminator.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task PingGroupAsync(Guid outboxRowId, string groupName, NotificationKind kind, CancellationToken ct = default)
    {
        RequireWrittenRow(outboxRowId);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("Group name is required.", nameof(groupName));
        }

        var signal = new NotificationSignal(outboxRowId, kind);
        try
        {
            var hub = await GetHubContextAsync(ct).ConfigureAwait(false);
            await hub.Clients.Group(groupName).SendAsync(SignalMethod, signal, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Best-effort SignalR group-ping failed (non-fatal; outbox row {OutboxRowId} + poll fallback carry the durable truth).",
                outboxRowId);
        }
    }

    /// <summary>
    /// Mints Serverless connection info (client access URL + short-lived token) scoped to
    /// <paramref name="userOid"/> ONLY (spec FR-04). The oid is supplied by the negotiate endpoint,
    /// which derives it server-side from the validated JWT — it is NEVER client-supplied.
    /// </summary>
    /// <param name="userOid">The caller's Azure AD object id (oid), derived server-side.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connection info the client uses to open its SignalR connection AS that user.</returns>
    public virtual async Task<SignalRConnectionInfo> NegotiateAsync(string userOid, CancellationToken ct = default)
    {
        RequireOid(userOid);

        // Uses the concrete ServiceHubContext directly (NegotiateAsync is not on IServiceHubContext).
        var hub = await _lazyHub.Value.ConfigureAwait(false);
        var negotiation = await hub.NegotiateAsync(new NegotiationOptions { UserId = userOid }, ct).ConfigureAwait(false);
        // A negotiation missing Url or AccessToken cannot yield a usable client connection — fail loud
        // rather than hand the client a null-bearing connection info (the Azure SDK types both nullable).
        if (string.IsNullOrEmpty(negotiation.Url) || string.IsNullOrEmpty(negotiation.AccessToken))
        {
            throw new InvalidOperationException(
                "Azure SignalR negotiation returned no Url/AccessToken; cannot mint client connection info.");
        }
        return new SignalRConnectionInfo(negotiation.Url, negotiation.AccessToken);
    }

    /// <summary>
    /// Module boundary the ping entrypoints dispatch through — the Serverless
    /// <see cref="ServiceHubContext"/> (typed as <see cref="IServiceHubContext"/>). Overridable so a
    /// seam test can substitute a boundary double WITHOUT a live Azure SignalR resource (ADR-038
    /// "mock at module boundaries, not the HTTP-handler level").
    /// </summary>
    protected virtual async Task<IServiceHubContext> GetHubContextAsync(CancellationToken ct)
        => await _lazyHub.Value.ConfigureAwait(false);

    private static async Task<ServiceHubContext> BuildHubContextAsync(SignalRDeliveryOptions opts)
    {
        var serviceManager = new ServiceManagerBuilder()
            .WithOptions(o =>
            {
                o.ConnectionString = opts.ConnectionString;
                // Serverless: REST-based, no persistent hub connection at startup (FR-01 spike).
                o.ServiceTransportType = ServiceTransportType.Transient;
            })
            .BuildServiceManager();

        return await serviceManager
            .CreateHubContextAsync(opts.HubName, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static void RequireWrittenRow(Guid outboxRowId)
    {
        if (outboxRowId == Guid.Empty)
        {
            // Structural enforcement of write-before-ping: an empty row id means the caller never wrote.
            throw new ArgumentException(
                "outboxRowId is required — the durable outbox row MUST be written BEFORE pinging SignalR (write-before-ping, ADR-041/043).",
                nameof(outboxRowId));
        }
    }

    private static void RequireOid(string userOid)
    {
        if (string.IsNullOrWhiteSpace(userOid))
        {
            throw new ArgumentException("Target user oid is required.", nameof(userOid));
        }
    }
}

/// <summary>
/// ADR-032 Null-Object for <see cref="SignalRDeliveryService"/>, registered UNCONDITIONALLY by
/// <c>NotificationsModule</c> when Azure SignalR is absent/disabled. Ping is a P2 quiet no-op
/// (absence of a live signal is not caller-visible error state — producers call it fire-and-forget
/// after their own outbox write already succeeded); negotiate is a P3 fail-fast throwing
/// <see cref="FeatureDisabledException"/> so the negotiate endpoint returns 503 and the client falls
/// back to the poll endpoint (FR-06) rather than being misled into thinking a live channel exists.
/// </summary>
internal sealed class NullSignalRDeliveryService : SignalRDeliveryService
{
    public NullSignalRDeliveryService(ILogger<SignalRDeliveryService> logger)
        : base(null, logger)
    {
    }

    /// <summary>P2 quiet no-op — the outbox + poll fallback carry the durable truth. Does NOT resolve identity.</summary>
    public override Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>P2 quiet no-op — the outbox + poll fallback carry the durable truth.</summary>
    public override Task PingGroupAsync(Guid outboxRowId, string groupName, NotificationKind kind, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>P3 fail-fast — 503 so the client falls back to the poll endpoint (FR-06).</summary>
    public override Task<SignalRConnectionInfo> NegotiateAsync(string userOid, CancellationToken ct = default)
        => throw new FeatureDisabledException(
            "notifications.signalr.disabled",
            "Real-time notification delivery (Azure SignalR) is not configured for this environment; clients fall back to the poll endpoint.");
}
