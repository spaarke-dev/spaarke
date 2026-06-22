// R3 Part 1 Phase 2 — Subscription handler real implementation (task 084).
//
// Applies a `MembershipChangedEvent` to the `sprk_userentityassociation`
// junction table. Idempotent per spec FR-2P2.4 via Dataverse alternate-key
// lookup on the 5-tuple {personId, personIdType, entityLogicalName,
// entityRecordId, sourceField} — see docs/data-model/
// sprk_userentityassociation.md §"Composite Alternate Key".
//
// Implementation outline:
//
//   1. RETRIEVE by alternate key (`sprk_uea_natural_key`):
//      • Hit  → row exists; Added/Updated → UPDATE (overwrite role +
//                                            sprk_lastsyncedon).
//                                          Removed → DELETE.
//      • Miss (FaultException `Cannot find the record`) → row absent;
//                                            Added/Updated → CREATE.
//                                            Removed → no-op (delete of
//                                                      absent row is
//                                                      acceptable per
//                                                      idempotency).
//
//   2. The Dataverse `UpsertRequest` message would be the textbook fit
//      but the existing `IDataverseService` abstraction does not surface
//      it. We compose the same behavior from `RetrieveByAlternateKeyAsync`
//      + `UpdateAsync`/`CreateAsync`/`DeleteAsync` — these are already
//      exposed by `IGenericEntityService` (sub-interface of
//      `IDataverseService`) and exercised by sibling code paths. The
//      retrieve-then-mutate sequence is naturally idempotent against
//      Service Bus at-least-once redelivery: if the row already matches
//      the desired state, the redundant write is a no-op semantically.
//
//   3. The composite-key probe uses Text(36) lowercase canonical-form
//      strings for `sprk_personid` + `sprk_entityrecordid` (per the data-
//      model doc — Dataverse rejects custom `UniqueIdentifier` columns
//      via Web API; both GUID-bearing columns are stored as Text(36)).
//      `Guid.ToString("D")` produces the canonical 36-char hyphenated
//      lowercase form.
//
//   4. The `personIdType` enum is mapped to its OptionSet integer (see
//      `sprk_userentityassociation_personidtype` in the data-model doc —
//      values 1..4 for User/Contact/Team/Organization match the
//      `PersonIdentityType` enum's pinned underlying values).
//
//   5. Idempotency invariant: calling `HandleAsync` twice with the same
//      event produces the same final state (the second call's RETRIEVE
//      hits, UPDATE re-applies role/timestamp). Per FR-2P2.4 this is
//      contract-binding; the unit test
//      `HandleAsync_DuplicateDelivery_IsIdempotent` exercises it.
//
//   6. Service registered as Scoped — `IDataverseService` is Scoped per
//      DataverseModule (ADR-010), so this handler MUST match. The host
//      (`MembershipJunctionUpdaterHost`) resolves it via
//      `IServiceScopeFactory.CreateScope()` per message (Singleton-with-
//      Scoped pattern — same pattern as `ServiceBusJobProcessor`).
//
// ADR-010 compliance: a concrete class behind an interface (per
// IMembershipJunctionUpdater rationale). The Null-Object pattern lives
// at the HOST level (ADR-032) — this handler is NEVER null-objected.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.4 +
//            AC-1P2.5; docs/data-model/sprk_userentityassociation.md;
//            src/server/shared/Spaarke.Dataverse/IGenericEntityService.cs.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Events;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Real implementation of <see cref="IMembershipJunctionUpdater"/>.
/// Upserts (Added/Updated) and deletes (Removed) junction rows on the
/// <c>sprk_userentityassociation</c> entity, keyed idempotently on the
/// 5-tuple natural key (spec FR-2P2.4).
/// </summary>
public sealed class MembershipJunctionUpdater : IMembershipJunctionUpdater
{
    /// <summary>
    /// Dataverse logical name of the junction table (see
    /// <c>docs/data-model/sprk_userentityassociation.md</c>).
    /// </summary>
    public const string JunctionEntityLogicalName = "sprk_userentityassociation";

    /// <summary>
    /// Alternate-key attribute names — declaration order matches the
    /// <c>sprk_uea_natural_key</c> EntityKey defined in the schema script
    /// (<c>scripts/Create-UserEntityAssociation.ps1</c>).
    /// </summary>
    private const string AttrPersonId = "sprk_personid";
    private const string AttrPersonIdType = "sprk_personidtype";
    private const string AttrEntityLogicalName = "sprk_entitylogicalname";
    private const string AttrEntityRecordId = "sprk_entityrecordid";
    private const string AttrSourceField = "sprk_sourcefield";
    private const string AttrRole = "sprk_role";
    private const string AttrLastSyncedOn = "sprk_lastsyncedon";
    private const string AttrPrimaryId = "sprk_userentityassociationid";

    private readonly IDataverseService _dataverse;
    private readonly TimeProvider _clock;
    private readonly IMembershipCacheInvalidator _cacheInvalidator;
    private readonly ILogger<MembershipJunctionUpdater> _logger;

    public MembershipJunctionUpdater(
        IDataverseService dataverse,
        TimeProvider clock,
        IMembershipCacheInvalidator cacheInvalidator,
        ILogger<MembershipJunctionUpdater> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _cacheInvalidator = cacheInvalidator ?? throw new ArgumentNullException(nameof(cacheInvalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(MembershipChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ct.ThrowIfCancellationRequested();

        // Build the composite alternate-key probe. The 5-tuple ordering
        // matches the schema-script declaration of `sprk_uea_natural_key`.
        var personIdString = evt.PersonId.ToString("D");
        var entityRecordIdString = evt.EntityRecordId.ToString("D");
        var personIdTypeValue = (int)evt.PersonIdType;

        var keyAttributes = new KeyAttributeCollection
        {
            { AttrPersonId, personIdString },
            { AttrPersonIdType, new OptionSetValue(personIdTypeValue) },
            { AttrEntityLogicalName, evt.EntityLogicalName },
            { AttrEntityRecordId, entityRecordIdString },
            { AttrSourceField, evt.SourceField },
        };

        // Probe for existing row. RetrieveByAlternateKeyAsync throws
        // when the row does not exist; we treat that as a "miss" and
        // branch by mutation type accordingly.
        Entity? existing = null;
        try
        {
            existing = await _dataverse.RetrieveByAlternateKeyAsync(
                JunctionEntityLogicalName,
                keyAttributes,
                new[] { AttrPrimaryId },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Honor NFR-07 drain — propagate cancellation as-is.
            throw;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Miss path — DataverseServiceClientImpl wraps the
            // SDK's "Cannot find the record" FaultException as
            // InvalidOperationException with a "not found" substring.
            existing = null;
        }
        catch (Exception ex)
        {
            // Catch the SDK's FaultException without taking a direct
            // dependency on Microsoft.Xrm.Sdk message types here.
            // FaultException's full name contains "FaultException";
            // its Detail/Message commonly contains "Cannot find the
            // record" or "does not exist".
            var t = ex.GetType().FullName ?? string.Empty;
            var m = ex.Message ?? string.Empty;
            if (t.Contains("FaultException", StringComparison.Ordinal) &&
                (m.Contains("Cannot find", StringComparison.OrdinalIgnoreCase) ||
                 m.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                 m.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            {
                existing = null;
            }
            else
            {
                throw;
            }
        }

        switch (evt.MutationType)
        {
            case MembershipMutationType.Added:
            case MembershipMutationType.Updated:
                {
                    var nowUtc = _clock.GetUtcNow().UtcDateTime;
                    if (existing is null)
                    {
                        // Create — full row including the alternate-key
                        // attributes.
                        var row = new Entity(JunctionEntityLogicalName)
                        {
                            [AttrPersonId] = personIdString,
                            [AttrPersonIdType] = new OptionSetValue(personIdTypeValue),
                            [AttrEntityLogicalName] = evt.EntityLogicalName,
                            [AttrEntityRecordId] = entityRecordIdString,
                            [AttrSourceField] = evt.SourceField,
                            [AttrRole] = evt.Role,
                            [AttrLastSyncedOn] = nowUtc,
                        };

                        var newId = await _dataverse
                            .CreateAsync(row, ct)
                            .ConfigureAwait(false);

                        _logger.LogInformation(
                            "Created junction row {Id} for {PersonId}/{EntityLogicalName}/{EntityRecordId}/{SourceField} (correlationId={CorrelationId})",
                            newId, evt.PersonId, evt.EntityLogicalName, evt.EntityRecordId, evt.SourceField, evt.CorrelationId);
                    }
                    else
                    {
                        // Update — overwrite role + last-synced-on only.
                        // The 5-tuple key attributes are immutable.
                        var fields = new Dictionary<string, object>
                        {
                            { AttrRole, evt.Role },
                            { AttrLastSyncedOn, nowUtc },
                        };

                        await _dataverse
                            .UpdateAsync(JunctionEntityLogicalName, existing.Id, fields, ct)
                            .ConfigureAwait(false);

                        _logger.LogInformation(
                            "Updated junction row {Id} (role={Role}, mutationType={MutationType}) for {PersonId}/{EntityLogicalName}/{EntityRecordId}/{SourceField} (correlationId={CorrelationId})",
                            existing.Id, evt.Role, evt.MutationType, evt.PersonId, evt.EntityLogicalName, evt.EntityRecordId, evt.SourceField, evt.CorrelationId);
                    }
                    break;
                }

            case MembershipMutationType.Removed:
                {
                    if (existing is null)
                    {
                        // Idempotent delete — absent row is acceptable.
                        _logger.LogInformation(
                            "Removed-event for absent junction row {PersonId}/{EntityLogicalName}/{EntityRecordId}/{SourceField} — no-op (idempotent) (correlationId={CorrelationId})",
                            evt.PersonId, evt.EntityLogicalName, evt.EntityRecordId, evt.SourceField, evt.CorrelationId);
                    }
                    else
                    {
                        await _dataverse
                            .DeleteAsync(JunctionEntityLogicalName, existing.Id, ct)
                            .ConfigureAwait(false);

                        _logger.LogInformation(
                            "Deleted junction row {Id} for {PersonId}/{EntityLogicalName}/{EntityRecordId}/{SourceField} (correlationId={CorrelationId})",
                            existing.Id, evt.PersonId, evt.EntityLogicalName, evt.EntityRecordId, evt.SourceField, evt.CorrelationId);
                    }
                    break;
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown MembershipMutationType: {evt.MutationType}");
        }

        // R3 Part 1 Phase 2 — Task 086 (FR-2P2.8 + AC-1P2.7).
        // After ANY successful junction-row write (Create / Update / Delete),
        // publish a cache-invalidation message so peer BFF instances evict
        // any cached membership results for (PersonId, EntityLogicalName).
        // Fire-and-forget: the invalidator's resilience contract guarantees
        // PublishInvalidationAsync never throws (Redis failures → log +
        // continue; the 5-min cache TTL is the backstop).
        //
        // Reuse path: task 085's MembershipReconciliationJob invokes
        // HandleAsync directly (no Service Bus topic involved); recon-driven
        // junction writes therefore fire the same invalidation through this
        // shared code path. No separate wiring needed on the recon side.
        await _cacheInvalidator
            .PublishInvalidationAsync(
                personId: evt.PersonId,
                entityLogicalName: evt.EntityLogicalName,
                correlationId: evt.CorrelationId,
                ct: ct)
            .ConfigureAwait(false);
    }
}
