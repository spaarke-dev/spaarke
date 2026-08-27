using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 075 — the ONE record-aware SharePoint Embedded container mapping, in both
/// directions.
///
/// <para><b>Forward</b> (<see cref="ResolveForRecordAsync"/>): <i>which container does this record's content
/// belong in?</i> A secure record resolves to its own <c>sprk_containerid</c> or fails; everything else
/// resolves to the caller's existing default (the business-unit cascade on the client per INV-7,
/// <c>Communication:ArchiveContainerId</c> for server-side ingest).</para>
///
/// <para><b>Reverse</b> (<see cref="ResolveOwningRecordAsync"/>): <i>which record owns this container?</i>
/// This is the authorization subject for the container-keyed write and read routes — tasks 073
/// (<c>PUT /api/containers/{containerId}/files/{*path}</c>) and 078
/// (<c>GET /api/v1/containers/{containerId}/documents</c>). Both directions come from this one component so
/// there is exactly one mapping in the codebase.</para>
///
/// <para><b>Why this exists at all.</b> Provisioning creates a per-project container and stamps its id on
/// the project row (task 021), and until this component landed <b>nothing read it</b>. Uploads resolved from
/// the acting user's business unit or a single global archive container, so a secure project's documents went
/// into a shared container. SharePoint Embedded permissions are additive-only — inheritance cannot be broken
/// on an individual file — so no later per-item permission can retract that. The per-project container is the
/// only available isolation mechanism, and this component is what makes the stamp mean something.</para>
/// </summary>
public interface IRecordContainerResolver
{
    /// <summary>
    /// Decide which container the given record's content belongs in.
    /// </summary>
    /// <param name="entityLogicalName">The record's entity logical name (e.g. <c>sprk_project</c>).</param>
    /// <param name="recordId">The record id.</param>
    /// <param name="nonSecureFallbackContainerId">
    /// The container to use when the record is NOT secure — the caller's existing behaviour. Pass the
    /// business-unit-resolved container on client-driven paths, or <c>Communication:ArchiveContainerId</c> on
    /// ingest. May be null/blank, in which case a non-secure record resolves to
    /// <see cref="ContainerDecisionOutcome.Unresolved"/> and the caller keeps its existing skip behaviour.
    /// </param>
    /// <returns>
    /// A resolution whose <see cref="ContainerDecision.ContainerId"/> is non-null unless the outcome is
    /// <see cref="ContainerDecisionOutcome.Unresolved"/> — which is reachable ONLY for a non-secure record.
    /// </returns>
    /// <exception cref="SdapProblemException">
    /// <para><c>secure_record_container_missing</c> (409) — the record IS secure and has no container of its
    /// own. This is a REFUSAL, not a fallback: substituting the shared container here is the isolation
    /// failure this component exists to prevent, and it would be invisible because the upload would
    /// succeed.</para>
    /// <para><c>container_record_not_found</c> (404) — the record does not exist, so its securability cannot
    /// be established.</para>
    /// </exception>
    /// <remarks>
    /// Any failure to DETERMINE securability (metadata unavailable, record read failed) propagates rather
    /// than defaulting to "not secure". An unknown answer read as "not secure" is the same isolation failure
    /// with an extra step.
    /// </remarks>
    Task<ContainerDecision> ResolveForRecordAsync(
        string entityLogicalName,
        Guid recordId,
        string? nonSecureFallbackContainerId,
        CancellationToken ct = default);

    /// <summary>
    /// The reverse mapping: find the secure record that owns <paramref name="containerId"/>.
    /// </summary>
    /// <returns>
    /// The owning secure record, or <c>null</c> when no secure record claims this container — i.e. it is a
    /// shared business-unit or archive container. <c>null</c> is an ANSWER, not an error: callers decide what
    /// a non-record-owned container means for them (task 073 authorizes the write; task 078 the read).
    /// </returns>
    /// <exception cref="SdapProblemException">
    /// <c>container_ownership_ambiguous</c> (409) — more than one secure record claims this container, or a
    /// secure record shares it with a non-secure record. Either is co-mingling: the condition this wave
    /// exists to prevent. It is refused rather than resolved, because picking one claimant would authorize
    /// against the wrong record.
    /// </exception>
    Task<OwningSecureRecord?> ResolveOwningRecordAsync(string containerId, CancellationToken ct = default);
}

/// <summary>
/// The secure record that owns a container, as returned by
/// <see cref="IRecordContainerResolver.ResolveOwningRecordAsync"/>.
/// </summary>
/// <param name="EntityLogicalName">The owning record's entity logical name.</param>
/// <param name="RecordId">The owning record's id — the authorization subject for tasks 073 / 078.</param>
public sealed record OwningSecureRecord(string EntityLogicalName, Guid RecordId);
