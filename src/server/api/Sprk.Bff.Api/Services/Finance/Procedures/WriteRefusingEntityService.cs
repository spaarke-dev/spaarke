using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 084d-1 (ontology-r1 review F1): the <see cref="IGenericEntityService"/> a dry run plans through. Reads pass to the
/// inner service; every write member throws <see cref="InvalidOperationException"/> before anything is sent, so a planning step cannot write,
/// by construction rather than by a test double.
/// </summary>
/// <remarks>
/// <para><b>Write members</b> (each throws): <see cref="CreateAsync"/>, <see cref="UpsertAsync"/>, <see cref="UpdateAsync"/>,
/// <see cref="UpdateIfRowVersionMatchesAsync"/>, <see cref="BulkUpdateAsync"/>, <see cref="BulkUpdateIfRowVersionsMatchAsync"/>,
/// <see cref="ExecuteTransactionAsync"/>, <see cref="DeleteAsync"/> and <see cref="AssociateAsync"/>. They are explicit interface members of
/// THIS type, so a subclass (<see cref="ProcedureRowOverlay"/>) inherits the refusal and cannot override it. Every interface member is
/// implemented here, including the ones with a default body, so no call falls through to a default.</para>
/// <para><b>Enforced by:</b> <c>ProcedureDryRunPlanningContractTests</c> (every interface member is classified as a read or a write, and each
/// write throws on this type and on the overlay) and the ArchTest <c>DryRunNoWriteGuardTests</c> (the IL of the dry-run types references no
/// write member of the inner service, with must-fire and must-not-fire controls).</para>
/// <para>§11: <i>Existing</i> — no read-only <see cref="IGenericEntityService"/> exists; test doubles refuse writes, production code has no
/// such seam. <i>Extension</i> — the existing implementations write by design, so a refusing decorator is the only way to give planning a
/// service it cannot write through. <i>Cost of doing nothing</i> — a dry run's no-write would rest on reading the planning code, and the
/// change path's stage 1 and 2 sends (which happen while it is still planning) would be one refactor away from running in a preview.</para>
/// </remarks>
internal class WriteRefusingEntityService : IGenericEntityService
{
    private readonly IGenericEntityService _inner;

    public WriteRefusingEntityService(IGenericEntityService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (inner is WriteRefusingEntityService)
        {
            // One layer of refusal is enough; a stack of them is a planning bug (a plan built on another plan's service).
            throw new ArgumentException("The inner service already refuses writes; wrap the application service once.", nameof(inner));
        }
    }

    /// <summary>The service reads go to. Never written through.</summary>
    protected IGenericEntityService Inner => _inner;

    private static InvalidOperationException Refused(string member) =>
        new($"A dry run never writes: {member} was called on the planning service. Nothing was sent.");

    // ── Reads: passed through (the overlay overrides the ones it shapes) ───────────────────────────────────────────────

    public virtual Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
        _inner.RetrieveAsync(entityLogicalName, id, columns, ct);

    public virtual Task<Entity> RetrieveByAlternateKeyAsync(
        string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) =>
        _inner.RetrieveByAlternateKeyAsync(entityLogicalName, alternateKeyValues, columns, ct);

    public virtual Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default) =>
        _inner.RetrieveMultipleAsync(query, ct);

    public virtual Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) =>
        _inner.RetrieveMultipleAsync(fetch, ct);

    public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) =>
        _inner.GetEntitySetNameAsync(entityLogicalName, ct);

    public Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) =>
        _inner.GetLookupNavigationAsync(childEntityLogicalName, relationshipSchemaName, ct);

    public Task<IReadOnlyDictionary<string, ColumnMetadata>> GetColumnMetadataAsync(string entityLogicalName, CancellationToken ct = default) =>
        _inner.GetColumnMetadataAsync(entityLogicalName, ct);

    public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) =>
        _inner.GetCollectionNavigationAsync(parentEntityLogicalName, relationshipSchemaName, ct);

    public Task<Guid> GetCurrentIdentityIdAsync(CancellationToken ct = default) => _inner.GetCurrentIdentityIdAsync(ct);

    // ── Writes: refused ─────────────────────────────────────────────────────────────────────────────────────────────────

    Task<Guid> IGenericEntityService.CreateAsync(Entity entity, CancellationToken ct) => throw Refused(nameof(IGenericEntityService.CreateAsync));

    Task<(Guid Id, bool Created)> IGenericEntityService.UpsertAsync(Entity entity, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.UpsertAsync));

    Task IGenericEntityService.UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.UpdateAsync));

    Task IGenericEntityService.UpdateIfRowVersionMatchesAsync(
        string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync));

    Task IGenericEntityService.BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.BulkUpdateAsync));

    Task IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync(
        string entityLogicalName, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync));

    Task IGenericEntityService.ExecuteTransactionAsync(IReadOnlyList<OrganizationRequest> requests, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.ExecuteTransactionAsync));

    Task IGenericEntityService.DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.DeleteAsync));

    Task IGenericEntityService.AssociateAsync(
        string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct) =>
        throw Refused(nameof(IGenericEntityService.AssociateAsync));
}
