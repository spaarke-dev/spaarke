using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Maps a <c>sprk_communication</c>'s resolved polymorphic regarding (ADR-024 family, single source of
/// truth <see cref="RegardingFieldMap"/>) into the RAG index grounding key
/// (<see cref="ParentEntityContext"/>). FR-D1 / FR-06: without this, communication documents index with
/// <c>ParentEntity = null</c>, so the matter never becomes the index parent scope and matter-scoped RAG
/// queries return zero of that matter's correspondence.
/// </summary>
/// <remarks>
/// <para><b>Primary-only grounding.</b> The FIRST regarding set in <see cref="RegardingFieldMap.All"/>
/// priority order is the communication's <i>primary</i> parent. We ground to that primary only when its
/// type is representable in <see cref="ParentEntityContext"/>; otherwise we degrade to null. We
/// deliberately do NOT fall through to a lower-priority representable regarding — grounding a document to
/// a non-primary parent would misfile it into the wrong parent's RAG scope.</para>
/// <para><b>Representable types.</b> <see cref="ParentEntityContext.EntityTypes"/> supports
/// matter / project / invoice / account / contact only. Other regarding targets (service request, work
/// assignment, event, budget, report card, analysis, organization) degrade to null grounding — the
/// current behavior — rather than fabricating an unsupported scheme (that expansion would be a change to
/// the AI-owned <see cref="ParentEntityContext"/> contract, out of scope for Communication code per
/// ADR-013 / §11). Tracked as a follow-up.</para>
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
    /// Build the grounding key from an already-retrieved <c>sprk_communication</c> entity. Returns the
    /// primary regarding as a <see cref="ParentEntityContext"/> when representable; null otherwise.
    /// </summary>
    public static ParentEntityContext? FromCommunication(Entity? communication)
    {
        if (communication is null)
        {
            return null;
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
                    ? $"Unknown {entityType}"
                    : reference.Name;
                return new ParentEntityContext(entityType, reference.Id.ToString(), name);
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Retrieve the communication's regarding and map it to a grounding key. Best-effort / non-fatal
    /// (NFR-04): returns null on any failure, and the caller keeps the existing null-degradation
    /// (the indexing handler then runs its own resolver chain).
    /// </summary>
    public static async Task<ParentEntityContext?> ResolveAsync(
        IGenericEntityService entityService,
        Guid communicationId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var communication = await entityService.RetrieveAsync(
                "sprk_communication", communicationId, RegardingColumns, ct);
            return FromCommunication(communication);
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
}
