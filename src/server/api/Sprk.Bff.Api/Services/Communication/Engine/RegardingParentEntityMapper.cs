using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Maps a <c>sprk_communication</c>'s resolved polymorphic regarding (ADR-024 family, single source of
/// truth <see cref="RegardingFieldMap"/>) into the RAG index grounding key
/// (<see cref="ParentEntityContext"/>). FR-D1 / FR-06: without this, communication documents index with
/// <c>ParentEntity = null</c>, so the matter never becomes the index parent scope and matter-scoped RAG
/// queries return zero of that matter's correspondence.
/// </summary>
/// <remarks>
/// <para><b>Core records decide</b> (unified-access-control-r2 task 177, verifier round; the shared rule in
/// <see cref="DocumentIndexParentResolver"/>). Search authorizes an indexed row by its parent, so the parent is the record
/// whose access GOVERNS the communication. Its core regardings (work assignment, project, matter — the
/// <c>sprk_regarding{core}</c> stamps) are the candidates: one is the parent; two or more go through the rule (the one secure
/// record, else the most specific; two different secure roots or an unreadable secure state: no parent). Before this, the
/// FIRST regarding in <see cref="RegardingFieldMap.All"/> order won, so a communication filed to a matter AND to a secure
/// project or work assignment was indexed under the matter, and a work assignment was not representable at all.</para>
/// <para><b>Primary-only grounding for the rest.</b> With no core regarding, the FIRST regarding set in
/// <see cref="RegardingFieldMap.All"/> order is the primary parent. We ground to it only when its type is representable in
/// <see cref="ParentEntityContext"/> (invoice, service request, account, contact); otherwise we degrade to null and do NOT
/// fall through to a lower-priority regarding — that would misfile it into the wrong parent's RAG scope.</para>
/// <para><b>Best-effort / non-fatal (NFR-04).</b> Any failure resolving the regarding degrades to null
/// grounding and never fails the capture or send path.</para>
/// </remarks>
public static class RegardingParentEntityMapper
{
    /// <summary>
    /// Dataverse regarding-target logical name → <see cref="ParentEntityContext"/> short scheme type.
    /// Only the subset representable in <see cref="ParentEntityContext.EntityTypes"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RepresentableTypeMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = ParentEntityContext.EntityTypes.Matter,
            ["sprk_project"] = ParentEntityContext.EntityTypes.Project,
            ["sprk_servicerequest"] = ParentEntityContext.EntityTypes.ServiceRequest,
            ["sprk_invoice"] = ParentEntityContext.EntityTypes.Invoice,
            ["account"] = ParentEntityContext.EntityTypes.Account,
            ["contact"] = ParentEntityContext.EntityTypes.Contact,
        };

    /// <summary>
    /// The column set to retrieve on <c>sprk_communication</c> — every regarding lookup, so the primary
    /// (highest-priority set) can be identified before checking representability.
    /// </summary>
    public static readonly string[] RegardingColumns = RegardingFieldMap.AllRegardingFields.ToArray();

    /// <summary>
    /// Build the grounding key from an already-retrieved <c>sprk_communication</c> entity, with no further read: its one core
    /// regarding; <c>null</c> when it has two or more (only <see cref="ResolveAsync"/> can apply the rule, which reads their
    /// secure state); otherwise the primary non-core regarding when representable.
    /// </summary>
    public static ParentEntityContext? FromCommunication(Entity? communication)
    {
        if (communication is null)
        {
            return null;
        }

        var cores = DocumentIndexParentResolver.CoreRegardingsOf(communication, UnknownName);
        if (cores.Count > 0)
        {
            return cores.Count == 1 ? cores[0].Parent : null;
        }

        foreach (var (entityLogicalName, regardingField) in RegardingFieldMap.All)
        {
            var reference = communication.GetAttributeValue<EntityReference>(regardingField);
            if (reference is null || reference.Id == Guid.Empty)
            {
                continue;
            }

            // Primary regarding found. Ground only if representable; otherwise degrade to null (do NOT
            // fall through to a lower-priority regarding — that would misfile into a non-primary scope).
            if (RepresentableTypeMap.TryGetValue(entityLogicalName, out var entityType))
            {
                var name = string.IsNullOrWhiteSpace(reference.Name)
                    ? UnknownName(entityType)
                    : reference.Name;
                return new ParentEntityContext(entityType, reference.Id.ToString(), name);
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Retrieve the communication's regarding and map it to a grounding key, applying the shared rule when it names two or
    /// more core records (<paramref name="parents"/>; <c>null</c> only in a partial composition, which then fails closed for
    /// such a communication). Best-effort / non-fatal (NFR-04): returns null on any failure, and the caller keeps the existing
    /// null-degradation (the indexing handler then runs its own resolver chain).
    /// </summary>
    public static async Task<ParentEntityContext?> ResolveAsync(
        IGenericEntityService entityService,
        DocumentIndexParentResolver? parents,
        Guid communicationId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var communication = await entityService.RetrieveAsync(
                "sprk_communication", communicationId, RegardingColumns, ct);
            if (communication is null)
            {
                return null;
            }

            var cores = DocumentIndexParentResolver.CoreRegardingsOf(communication, UnknownName);
            if (cores.Count < 2)
            {
                return FromCommunication(communication);
            }

            if (parents is null)
            {
                // Registered unconditionally (AnalysisServicesModule); absent only in a partial composition. Without the
                // rule the governing record is unknown: no parent, never the first one (fail closed).
                logger.LogWarning(
                    "RAG grounding: communication {CommunicationId} names {Count} core records and the index-parent rule is not available; indexing with ParentEntity=null.",
                    communicationId, cores.Count);
                return null;
            }

            return await parents.ChooseGoverningAsync(cores, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "RAG grounding: failed to resolve regarding for communication {CommunicationId}; indexing with ParentEntity=null (best-effort, NFR-04).",
                communicationId);
            return null;
        }
    }

    private static string UnknownName(string entityType) => $"Unknown {entityType}";
}
