using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Notifications;

/// <summary>
/// A single row read back from <c>sprk_notificationoutbox</c> (task 011 schema).
/// </summary>
/// <remarks>
/// Projection type — decouples <see cref="OutboxService"/> consumers (task 020 SignalR delivery,
/// task 022 FR-06 poll endpoint) from raw Dataverse <see cref="Entity"/> attribute access.
/// </remarks>
public sealed record OutboxNotification(
    Guid Id,
    Guid OwnerId,
    NotificationKind Kind,
    string EnvelopeJson,
    string? RegardingRecordId,
    string? RegardingRecordType,
    DateTimeOffset? Delivered,
    DateTimeOffset? Dismissed,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Durable-store service (Layer B, spec FR-02) over the <c>sprk_notificationoutbox</c> table
/// created by task 011. Write/read/dismiss operations for the per-user pending-notification
/// outbox — the source of truth that Layer C (SignalR, task 020) accelerates but never replaces
/// (write-before-ping invariant, ADR-041/043).
/// </summary>
/// <remarks>
/// <para>
/// Plain CRUD/Dataverse infrastructure — concrete class per ADR-010 (no interface introduced
/// solely for testability). Registered UNCONDITIONALLY in DI (no kill switch — every producer
/// needs a durable place to write before any delivery mechanism is attempted).
/// </para>
/// <para>
/// <b>Expiry mechanism: read-time filter</b> (not an explicit sweep). <see cref="GetPendingAsync"/>
/// excludes rows whose <c>sprk_expiresat</c> is in the past directly in the Dataverse query — no
/// background job marks/deletes expired rows. This keeps the write path simple (no second
/// "expire" write) and keeps "pending" a pure function of the three timestamp columns at read
/// time, matching the lifecycle model documented in <c>docs/data-model/sprk_notificationoutbox.md</c>
/// §3 (state is derived from column presence/absence, not a separate state machine).
/// </para>
/// <para>
/// MUST NOT take a dependency on <c>Microsoft.Azure.SignalR</c>/<c>.Management</c> or any
/// delivery-layer type (task 020 composes delivery ON TOP of this service's <see cref="WriteAsync{TEnvelope}"/>
/// — outbox-before-ping, not the reverse). MUST NOT inject AI-internal types (<c>IOpenAiClient</c>,
/// <c>IPlaybookService</c>, <c>Services/Ai/</c> internals) — this is plain Dataverse infrastructure.
/// </para>
/// </remarks>
public sealed class OutboxService
{
    private const string EntityLogicalName = "sprk_notificationoutbox";
    private const string IdColumn = "sprk_notificationoutboxid";
    private const string OwnerColumn = "ownerid";
    private const string KindColumn = "sprk_kind";
    private const string EnvelopeColumn = "sprk_envelope";
    private const string RegardingIdColumn = "sprk_regardingrecordid";
    private const string RegardingTypeColumn = "sprk_regardingrecordtype";
    private const string DeliveredColumn = "sprk_delivered";
    private const string DismissedColumn = "sprk_dismissed";
    private const string ExpiresAtColumn = "sprk_expiresat";

    private static readonly string[] PendingReadColumns =
    {
        IdColumn, OwnerColumn, KindColumn, EnvelopeColumn,
        RegardingIdColumn, RegardingTypeColumn, DeliveredColumn, DismissedColumn, ExpiresAtColumn
    };

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<OutboxService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <param name="entityService">Dataverse generic CRUD/query service.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="timeProvider">
    /// BCL clock abstraction (.NET 8 <see cref="TimeProvider"/>), used for the expiry read-time
    /// filter and to stamp <c>sprk_dismissed</c>. Defaults to <see cref="TimeProvider.System"/>
    /// when not injected (production); tests inject a deterministic provider (per
    /// <c>.claude/constraints/testing.md</c> TimeProvider rule) to assert expiry behavior without
    /// wall-clock flakiness.
    /// </param>
    public OutboxService(
        IGenericEntityService entityService,
        ILogger<OutboxService> logger,
        TimeProvider? timeProvider = null)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Writes a new pending outbox row for <paramref name="userId"/>. Serializes
    /// <paramref name="envelope"/> (a <see cref="CommunicationEnvelope"/> or
    /// <see cref="SuggestionEnvelope"/> from task 013 — referenced, never redefined here) into
    /// <c>sprk_envelope</c>. The row starts undelivered/undismissed
    /// (<c>sprk_delivered</c>/<c>sprk_dismissed</c> both null); task 020's SignalR ping sets
    /// <c>sprk_delivered</c> AFTER this write succeeds (write-before-ping, ADR-041/043) — that
    /// happens outside this service.
    /// </summary>
    /// <typeparam name="TEnvelope">The envelope record type (<see cref="CommunicationEnvelope"/> or <see cref="SuggestionEnvelope"/>).</typeparam>
    /// <param name="userId">Dataverse systemuserid of the target user (row owner / per-user pending key).</param>
    /// <param name="kind">The closed <see cref="NotificationKind"/> discriminator.</param>
    /// <param name="envelope">The typed envelope payload — IDs + minimal display metadata only (NFR-02/NFR-03).</param>
    /// <param name="regardingRecordId">Optional regarding record GUID string (ADR-024 MINIMAL pattern; mirrors the envelope's own regarding field).</param>
    /// <param name="regardingRecordType">Optional regarding record logical name (plain text — see data-model §4.2).</param>
    /// <param name="expiresAt">Optional expiry boundary. Null means the row never expires via the read-time filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new outbox row's <c>sprk_notificationoutboxid</c>.</returns>
    public async Task<Guid> WriteAsync<TEnvelope>(
        Guid userId,
        NotificationKind kind,
        TEnvelope envelope,
        string? regardingRecordId = null,
        string? regardingRecordType = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
        where TEnvelope : notnull
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Target user id is required.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(envelope);

        var envelopeJson = JsonSerializer.Serialize(envelope);

        var entity = new Entity(EntityLogicalName)
        {
            [OwnerColumn] = new EntityReference("systemuser", userId),
            [KindColumn] = ToWireKind(kind),
            [EnvelopeColumn] = envelopeJson
        };

        if (!string.IsNullOrWhiteSpace(regardingRecordId))
        {
            entity[RegardingIdColumn] = regardingRecordId;
        }

        if (!string.IsNullOrWhiteSpace(regardingRecordType))
        {
            entity[RegardingTypeColumn] = regardingRecordType;
        }

        if (expiresAt.HasValue)
        {
            entity[ExpiresAtColumn] = expiresAt.Value.UtcDateTime;
        }

        var outboxId = await _entityService.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Wrote outbox row {OutboxId} kind={Kind} user={UserId} expiresAt={ExpiresAt}",
            outboxId, kind, userId, expiresAt?.ToString("O") ?? "(none)");

        return outboxId;
    }

    /// <summary>
    /// Reads the unexpired, undismissed pending rows for <paramref name="userId"/> (spec FR-02/FR-06).
    /// Expiry is enforced as a READ-TIME FILTER (see class remarks) — a row past
    /// <c>sprk_expiresat</c> is excluded here rather than by a separate sweep/mark step.
    /// </summary>
    /// <param name="userId">Dataverse systemuserid of the target user (row owner).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pending rows for the user, most-recently-created first is NOT guaranteed — callers needing order should sort.</returns>
    public async Task<IReadOnlyList<OutboxNotification>> GetPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Target user id is required.", nameof(userId));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(PendingReadColumns),
            Criteria = new FilterExpression(LogicalOperator.And)
        };

        query.Criteria.AddCondition(OwnerColumn, ConditionOperator.Equal, userId);
        query.Criteria.AddCondition(DismissedColumn, ConditionOperator.Null);

        // Read-time expiry filter: include rows with no expiry OR an expiry still in the future.
        var expiryFilter = new FilterExpression(LogicalOperator.Or);
        expiryFilter.AddCondition(ExpiresAtColumn, ConditionOperator.Null);
        expiryFilter.AddCondition(ExpiresAtColumn, ConditionOperator.GreaterThan, now);
        query.Criteria.AddFilter(expiryFilter);

        var result = await _entityService.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

        return result.Entities.Select(MapToNotification).ToList();
    }

    /// <summary>
    /// Dismisses a pending outbox row — stamps <c>sprk_dismissed</c> with the current time so a
    /// subsequent <see cref="GetPendingAsync"/> for that row's owner no longer returns it.
    /// </summary>
    /// <param name="outboxRowId">The <c>sprk_notificationoutboxid</c> to dismiss.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DismissAsync(Guid outboxRowId, CancellationToken cancellationToken = default)
    {
        if (outboxRowId == Guid.Empty)
        {
            throw new ArgumentException("Outbox row id is required.", nameof(outboxRowId));
        }

        var fields = new Dictionary<string, object>
        {
            [DismissedColumn] = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _entityService.UpdateAsync(EntityLogicalName, outboxRowId, fields, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Dismissed outbox row {OutboxId}", outboxRowId);
    }

    /// <summary>
    /// Serializes a <see cref="NotificationKind"/> to its closed kebab-case wire string via the
    /// SAME <see cref="NotificationKindJsonConverter"/> task 013 defines for envelope JSON — single
    /// source of truth for the wire mapping, no duplicated switch statement here.
    /// </summary>
    private static string ToWireKind(NotificationKind kind)
        => JsonSerializer.Serialize(kind).Trim('"');

    /// <summary>
    /// Deserializes the <c>sprk_kind</c> text column back to a <see cref="NotificationKind"/> via
    /// the same fail-closed converter — an unrecognized value throws rather than silently mapping
    /// to a default kind.
    /// </summary>
    private static NotificationKind ParseWireKind(string wireKind)
        => JsonSerializer.Deserialize<NotificationKind>($"\"{wireKind}\"");

    private static OutboxNotification MapToNotification(Entity entity)
    {
        var ownerRef = entity.GetAttributeValue<EntityReference>(OwnerColumn);
        var kindWire = entity.GetAttributeValue<string>(KindColumn) ?? string.Empty;

        return new OutboxNotification(
            Id: entity.Id,
            OwnerId: ownerRef?.Id ?? Guid.Empty,
            Kind: ParseWireKind(kindWire),
            EnvelopeJson: entity.GetAttributeValue<string>(EnvelopeColumn) ?? string.Empty,
            RegardingRecordId: entity.GetAttributeValue<string>(RegardingIdColumn),
            RegardingRecordType: entity.GetAttributeValue<string>(RegardingTypeColumn),
            Delivered: ToOffset(entity.GetAttributeValue<DateTime?>(DeliveredColumn)),
            Dismissed: ToOffset(entity.GetAttributeValue<DateTime?>(DismissedColumn)),
            ExpiresAt: ToOffset(entity.GetAttributeValue<DateTime?>(ExpiresAtColumn)));
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null;
}
