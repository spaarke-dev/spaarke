using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Generic entity CRUD, metadata, and query operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IGenericEntityService
{
    Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default);
    Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default);

    /// <summary>
    /// Atomically creates-or-updates a record keyed by its ALTERNATE KEY (Dataverse
    /// <c>UpsertRequest</c>) in a single server-side round-trip. The <paramref name="entity"/> MUST
    /// carry a <see cref="Entity.KeyAttributes"/> alternate key (or a primary <see cref="Entity.Id"/>);
    /// Dataverse resolves the target atomically, so two concurrent first-writes for the same key yield
    /// exactly ONE row (the second UPDATES the first's row) — there is no read-then-create TOCTOU window.
    /// </summary>
    /// <remarks>
    /// Added 2026-08-16 (spaarkeai-compose-r7 task 013, FR-07d) to close the compose promote
    /// duplicate-<c>sprk_document</c> race: <c>ComposeService.PromoteIfEphemeralAsync</c> replaces its
    /// read-then-<see cref="CreateAsync"/>-then-catch sequence with this atomic upsert on the
    /// <c>sprk_graphitemid_uk</c> alternate key. Implemented by <c>DataverseServiceClientImpl</c> via
    /// <c>Microsoft.Xrm.Sdk.Messages.UpsertRequest</c>; the Web-API impl throws
    /// <see cref="NotImplementedException"/> (existing pattern — the BFF runtime always resolves the
    /// ServiceClient impl).
    /// </remarks>
    /// <returns>The resolved record id and whether Dataverse CREATED it (<c>false</c> = the row already
    /// existed and was updated).</returns>
    Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default);
    /// <summary>
    /// Updates the provided fields on an existing record. A <c>fields</c> value of C# <c>null</c> SKIPS that key
    /// (only the fields you supply are written — the default convenience). A value of <see cref="DBNull.Value"/>
    /// explicitly CLEARS that column to null (e.g. severing a lookup) — the only way to null a field through this
    /// generic seam (added for FR-C3 graduate-on-divergence clearing <c>sprk_canonicaldocument</c>).
    /// </summary>
    Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default);

    /// <summary>
    /// <see cref="UpdateAsync"/>, applied only while the row's version is still <paramref name="rowVersion"/> (Dataverse
    /// optimistic concurrency: <c>UpdateRequest.ConcurrencyBehavior = IfRowVersionMatches</c>). The field rules are
    /// <see cref="UpdateAsync"/>'s exactly: a C# <c>null</c> value SKIPS that key, <see cref="DBNull.Value"/> CLEARS the
    /// column, and an <see cref="EntityReference"/> sets a lookup — so a write built for <see cref="UpdateAsync"/> can be made
    /// conditional unchanged.
    /// </summary>
    /// <remarks>
    /// #2143 (unified-access-control-r2): the re-file's owner assignment and its compensating put-back
    /// (<c>RecordOwnershipResolver.ReparentAsync</c>) must not overwrite a concurrent re-file. Component Justification
    /// (CLAUDE.md §11): (1) Existing — <see cref="IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync"/> is the
    /// Web API conditional update; <c>OntologyWriterDataverseClient.UpdateIfRowVersionMatchesAsync</c> (Sprk.Bff.Api,
    /// Services/Signals) is an SDK conditional update. (2) Extension — the first takes Web API payloads, so it cannot express
    /// the SDK lookup sets and <see cref="DBNull"/> clears the resolver writes without each table's navigation-property names,
    /// which the resolver does not have. The second authenticates as a DIFFERENT identity — the Signal writer's own managed
    /// identity, deliberately unable to route through this service's credential — which holds no Write on the child tables a
    /// re-file re-owns; re-owning through it would fail, and granting that identity Write there would break its isolation. (3) Cost of doing nothing — an unconditional owner write after a re-file can leave a row filed under a
    /// secure record but owned in an ordinary business unit (fail-open, #2143), and the put-back can overwrite a newer filing.
    /// </remarks>
    /// <param name="rowVersion">The row's <c>versionnumber</c> (or <see cref="Entity.RowVersion"/>) as read.</param>
    /// <exception cref="System.Data.DBConcurrencyException">The row changed since <paramref name="rowVersion"/> was read
    /// (Dataverse <c>ConcurrencyVersionMismatch</c>, <c>0x80060882</c>); nothing was written. The same exception
    /// <see cref="IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync"/> throws for HTTP 412, so callers handle
    /// one type.</exception>
    Task UpdateIfRowVersionMatchesAsync(
        string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct = default);

    /// <summary>
    /// Updates N records of ONE table in one request, <b>all-or-nothing</b>: every update is applied, or
    /// none is. Never a partial set.
    /// </summary>
    /// <remarks>
    /// <para><b>Limits.</b> Keep each call to at most <b>1,000</b> updates — <c>ExecuteMultiple</c>'s
    /// documented batch size and the same ceiling this method always had; the transaction documentation
    /// states no figure of its own. Splitting a larger set into several calls gives per-call atomicity only —
    /// decide whether that is acceptable before doing it.</para>
    /// <para><b>Fields.</b> Exactly <see cref="UpdateAsync"/>'s convention: a C# <c>null</c> value SKIPS that key,
    /// and <see cref="DBNull.Value"/> explicitly CLEARS that column to null, inside the same transaction (e.g.
    /// severing a lookup as part of the all-or-nothing change — unified-access-control-r2 task 140, round 34 item 3:
    /// <c>set-record-share-expiry</c> takes a contact-issued grant over in the same write). It was rejected before
    /// 2026-10-04 because the builder put the <see cref="DBNull"/> itself into the entity, which cannot be
    /// serialized; it is now mapped to null, as <see cref="UpdateAsync"/> maps it.</para>
    /// <para><b>Failure.</b> Throws <see cref="InvalidOperationException"/> whose message states only what
    /// is known: when Dataverse identifies the request that faulted, the message names its index and record
    /// and states that NO update was applied; otherwise it states that the updates were applied all together
    /// or not at all. Cancellation surfaces as <see cref="OperationCanceledException"/>; argument errors throw
    /// before any I/O.</para>
    /// <para><b>Corrected 2026-09-10</b> (unified-access-control-r2 task 096, ISS-005 / #970). This method
    /// used to send an <c>ExecuteMultipleRequest</c> with <c>ContinueOnError = false</c> under the comment
    /// "Stop on first error for transactional behavior". <c>ExecuteMultiple</c> is not transactional: it
    /// stopped at the first fault with every earlier update already committed, and its error message
    /// implied nothing had happened.</para>
    /// </remarks>
    Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default);

    /// <summary>
    /// <see cref="BulkUpdateAsync"/> where every update is applied only while its row is still at <c>rowVersion</c> (the row's
    /// <c>versionnumber</c> as the caller read it; Dataverse optimistic concurrency, <c>ConcurrencyBehavior.IfRowVersionMatches</c>).
    /// All-or-nothing, as <see cref="BulkUpdateAsync"/>: one row changed since it was read rolls the whole transaction back.
    /// </summary>
    /// <remarks>
    /// <para><b>Failure.</b> A row version mismatch (<c>ConcurrencyVersionMismatch</c>, 0x80060882) throws
    /// <see cref="System.Data.DBConcurrencyException"/>: NO update was applied. Any other failure throws as
    /// <see cref="BulkUpdateAsync"/> does. Cancellation surfaces as <see cref="OperationCanceledException"/>.</para>
    /// <para>Added for ebilling-r1 task 076 (review pass 2): the rule set publish writes as the application identity and must not
    /// overwrite a version or policy row another admin changed after the publish read it. Same seam and identity as
    /// <see cref="BulkUpdateAsync"/>; the existing conditional writes are single-row and run on other identities
    /// (<c>IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync</c>, the Ontology writer's own connection).</para>
    /// <para>The default body throws: only the ServiceClient implementation (<c>DataverseServiceClientImpl</c>) sends it, so a
    /// substitute that does not model row versions fails loudly instead of writing unconditionally.</para>
    /// </remarks>
    Task BulkUpdateIfRowVersionsMatchAsync(
        string entityLogicalName, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "BulkUpdateIfRowVersionsMatchAsync is implemented by DataverseServiceClientImpl only; a substitute must model row versions.");

    /// <summary>
    /// Sends <paramref name="requests"/> (creates, updates, deletes, in any mix of tables) as ONE <c>ExecuteTransactionRequest</c>,
    /// <b>all-or-nothing</b>: Dataverse rolls the whole transaction back when any request faults. Requests run in list order.
    /// </summary>
    /// <remarks>
    /// <para><b>Failure.</b> Throws <see cref="DataverseTransactionException"/> (an <see cref="InvalidOperationException"/>) whose
    /// <see cref="DataverseTransactionException.FailedRequestIndex"/> is the zero-based index of the request Dataverse says faulted,
    /// and whose <see cref="DataverseTransactionException.OutcomeUnknown"/> is true when Dataverse returned no fault (a timeout can
    /// land after the commit). Cancellation surfaces as <see cref="OperationCanceledException"/>. A null, empty or nested
    /// (<c>ExecuteMultiple</c> / <c>ExecuteTransaction</c>) list throws <see cref="ArgumentException"/> before any I/O.</para>
    /// <para><b>Limits.</b> No responses are returned. Dataverse documents no request count for a transaction; keep a call to the same
    /// 1,000-request bound <see cref="BulkUpdateAsync"/> uses.</para>
    /// <para>Added for ebilling-r1 task 049 (G1): the submission pipeline persists an invoice, its lines, its approval portion and the
    /// document link across several tables in one unit, and <see cref="BulkUpdateAsync"/> covers single-table updates only.</para>
    /// <para>The default body throws, as <see cref="BulkUpdateIfRowVersionsMatchAsync"/>'s does: only the ServiceClient
    /// implementation (<c>DataverseServiceClientImpl</c>) sends it, so a substitute that does not model transactions fails loudly
    /// instead of writing the rows one by one.</para>
    /// </remarks>
    Task ExecuteTransactionAsync(IReadOnlyList<OrganizationRequest> requests, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "ExecuteTransactionAsync is implemented by DataverseServiceClientImpl only; a substitute must model the transaction.");

    Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default);
    Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default);
    Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default);
    Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default);
    Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default);

    /// <summary>
    /// Executes a FetchXML query and returns the matching <see cref="EntityCollection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added 2026-06-10 for multi-container-multi-index-r1 Phase G (task 102) to support the
    /// lookup-first <see cref="Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver"/> chain and the
    /// new <see cref="Sprk.Bff.Api.Services.Ai.DataverseAllowedIndexesProvider"/> allow-list lookup.
    /// Both consumers need FetchXml <c>link-entity</c> support (outer-join cascade across the
    /// doc → parent → BU chain projecting <c>idx.sprk_searchindexname</c>), which
    /// <see cref="QueryExpression"/> cannot express ergonomically.
    /// </para>
    /// <para>
    /// Implementations delegate to the SDK <c>ServiceClient.RetrieveMultipleAsync(FetchExpression, ct)</c>
    /// overload. The Web API implementation throws <c>NotImplementedException</c> (existing pattern —
    /// the BFF runtime always resolves <see cref="DataverseServiceClientImpl"/>).
    /// </para>
    /// </remarks>
    /// <param name="fetch">A SDK <see cref="FetchExpression"/> wrapping a FetchXml payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default);

    Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default);

    /// <summary>
    /// The <c>systemuserid</c> of the identity this connection authenticates as (Dataverse <c>WhoAmI</c>): the id Dataverse stamps in the
    /// <c>createdby</c> of every row this service creates without impersonation. A caller that later has to recognise its own rows (the
    /// rule drafting job's retry compensation, ebilling-r1 task 075) matches <c>createdby</c> against it.
    /// </summary>
    /// <remarks>
    /// A fault, or an empty id, is thrown and never cached, so the caller fails closed. The default body throws: only the ServiceClient
    /// implementation (<c>DataverseServiceClientImpl</c>) can answer, so a substitute that does not model identities fails loudly instead
    /// of letting a caller treat every row as foreign (or as its own).
    /// </remarks>
    Task<Guid> GetCurrentIdentityIdAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("GetCurrentIdentityIdAsync is implemented by DataverseServiceClientImpl only; a substitute must model identities.");

    /// <summary>
    /// Associates a primary entity with one or more related entities via a many-to-many
    /// relationship. Each <paramref name="relatedEntities"/> entry must reference an
    /// existing row of the related entity type used in the relationship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generic N:N pattern — used by the Insights Engine D-P3 admin endpoint (task 012)
    /// to attach supporting <c>sprk_matter</c> rows to a new <c>sprk_precedent</c> via
    /// the <c>sprk_precedent_matter</c> relationship. Existing analysis scope wiring uses
    /// the domain-specific <see cref="IAnalysisDataverseService.AssociateScopesAsync"/>;
    /// this method exposes the same primitive without baking the entity types into the
    /// interface.
    /// </para>
    /// <para>
    /// Already-existing associations should be tolerated by implementations (Dataverse
    /// surfaces these as <c>HTTP 400</c> / <c>InvalidOperationException</c>; callers
    /// generally want idempotent semantics).
    /// </para>
    /// </remarks>
    /// <param name="entityLogicalName">Logical name of the primary entity (e.g. <c>sprk_precedent</c>).</param>
    /// <param name="entityId">Row id of the primary entity.</param>
    /// <param name="relationshipName">Schema name of the relationship (e.g. <c>sprk_precedent_matter</c>).</param>
    /// <param name="relatedEntities">References to the related rows to attach.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AssociateAsync(
        string entityLogicalName,
        Guid entityId,
        string relationshipName,
        IEnumerable<EntityReference> relatedEntities,
        CancellationToken ct = default);
}
