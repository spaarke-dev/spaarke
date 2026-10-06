using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// What an Office edit-open may expect: whether the caller can edit the file in Office, and why.
/// </summary>
/// <param name="CanEdit">The caller holds (or was just granted) a writer role, or the container is a business-unit
/// container whose internal users are standing writers.</param>
/// <param name="Role">"standing" (business-unit container), "member" (already held a role), "writer-jit" (granted now),
/// "member-read-only" (already holds a role that cannot edit — e.g. a hand-granted reader), "none" (secure container, no
/// Write on its record, or an external user on a Restricted record), or "unknown" (the container could not be
/// classified).</param>
public sealed record OfficeEditAccess(bool CanEdit, string Role);

/// <summary>
/// Just-in-time Office edit access on a per-record SECURE container — unified-access-control-r2 task 171, owner rounds
/// 69 (2) and 70. A secure container has no standing members by design, so "Edit in Word/Excel" (web or desktop —
/// even a desktop open needs the user's own role) works only after the BFF grants the caller a WRITER role on it.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> Called by <c>GET /api/documents/{id}/office</c> and <c>/open-links</c> AFTER their per-document
/// Read gate and pointer check, and BEFORE the URL is returned. When the document's container is a secure record's own
/// container, the caller's rights on THAT record are asked of Dataverse AS THE CALLER
/// (<see cref="CallerRecordAccessProbe"/>, the same question <c>RecordRouteAccessAuthorizationFilter</c> asks). Write →
/// the caller is granted a writer role through <see cref="SpeContainerMembershipService.GrantMarkedWriterAsync"/>
/// (idempotent: a caller who already holds any role is left as they are). No Write → no grant, ever: the links are the
/// same pointers, SharePoint refuses them, and the in-app preview (broker) is the caller's view path.</para>
/// <para><b>Not on a business-unit container.</b> Round 70: internal users there are STANDING writers, kept by
/// <c>SpeContainerMembershipSyncJob</c>; nothing is granted on an edit-open.</para>
/// <para><b>Removal.</b> <c>SpeContainerMembershipSyncJob</c> removes the grant once Dataverse positively answers that
/// the holder no longer has Write on the record (or the holder is disabled).</para>
/// <para><b>Why a class of its own</b> (CLAUDE.md §11): the decision combines the container classification
/// (<see cref="RecordContainerResolver"/>), the caller's Dataverse rights (<see cref="CallerRecordAccessProbe"/>) and
/// the grant primitive (<see cref="SpeContainerMembershipService"/>), and two routes need exactly the same decision.
/// Without it a secure document cannot be edited in Office at all.</para>
/// </remarks>
public class OfficeEditAccessService
{
    private readonly RecordContainerResolver _containerResolver;
    private readonly CallerRecordAccessProbe _probe;
    private readonly SpeContainerMembershipService _membership;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<OfficeEditAccessService> _logger;

    public OfficeEditAccessService(
        RecordContainerResolver containerResolver,
        CallerRecordAccessProbe probe,
        SpeContainerMembershipService membership,
        IGenericEntityService entities,
        ILogger<OfficeEditAccessService> logger)
    {
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures the caller can edit <paramref name="documentId"/>'s file in Office when it lives in a SECURE container
    /// and the caller holds Write on that container's record. Throws <see cref="SdapProblemException"/> (503
    /// <c>edit_access_unavailable</c>) when a Write holder's grant could not be made — never a URL that cannot work
    /// for a caller entitled to it, and never a grant that could not be recorded.
    /// </summary>
    public virtual async Task<OfficeEditAccess> PrepareAsync(
        Guid documentId, string driveId, HttpContext httpContext, CancellationToken ct = default)
    {
        OwningSecureRecord? secureOwner;
        try
        {
            secureOwner = await ResolveSecureOwnerAsync(driveId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[OFFICE-EDIT] Document {DocumentId}: could not tell whether its container is secure; no edit access is granted.",
                documentId);
            return new OfficeEditAccess(false, "unknown");
        }

        if (secureOwner is null)
        {
            // A business-unit / environment container: internal users are standing writers (round 70).
            return new OfficeEditAccess(true, "standing");
        }

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (!EntityAccessFilter.TryResolveEntitySet(secureOwner.EntityLogicalName, out var entitySet))
        {
            _logger.LogWarning(
                "[OFFICE-EDIT] Document {DocumentId}: secure owner type {Entity} has no entity set; no edit access is granted.",
                documentId, secureOwner.EntityLogicalName);
            return new OfficeEditAccess(false, "none");
        }

        var rights = await _probe.GetCallerRightsAsync(token, entitySet, secureOwner.RecordId, ct).ConfigureAwait(false);
        if (!rights.HasFlag(AccessRights.Write))
        {
            _logger.LogInformation(
                "[OFFICE-EDIT] Document {DocumentId}: the caller has no Write on secure {Entity} {RecordId}; no grant (view only).",
                documentId, secureOwner.EntityLogicalName, secureOwner.RecordId);
            return new OfficeEditAccess(false, "none");
        }

        var systemUserId = await _probe.GetCallerSystemUserIdAsync(token, ct).ConfigureAwait(false);
        var user = systemUserId is { } id
            ? await _entities.RetrieveAsync("systemuser", id, ["domainname", "azureactivedirectoryobjectid", "sprk_isexternal"], ct).ConfigureAwait(false)
            : null;
        var upn = user?.GetAttributeValue<string>("domainname");
        var objectId = user?.GetAttributeValue<Guid?>("azureactivedirectoryobjectid");
        if (user is null || systemUserId is null || string.IsNullOrWhiteSpace(upn))
        {
            throw Unavailable(documentId, "the caller's Dataverse user (and its sign-in name) could not be resolved");
        }

        // Round 67: a Restricted record admits no external user. SpeContainerMembershipSync removes exactly such a grant,
        // so it is never made here (a record whose flag cannot be read gets no grant either — fail closed).
        if (user.GetAttributeValue<bool?>("sprk_isexternal") == true
            && await IsRestrictedOrUnreadableAsync(secureOwner, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "[OFFICE-EDIT] Document {DocumentId}: an external caller on Restricted (or unreadable) {Entity} {RecordId}; no grant.",
                documentId, secureOwner.EntityLogicalName, secureOwner.RecordId);
            return new OfficeEditAccess(false, "none");
        }

        var access = await _membership.ReadAccessAsync(driveId, ct).ConfigureAwait(false)
                     ?? throw Unavailable(documentId, "the container could not be read");

        var held = access.Roles.FirstOrDefault(r => r.IsFor(upn, objectId));
        if (held is not null)
        {
            // Already a member (an earlier grant of ours, or a role someone else gave): reuse it. A role is never changed
            // here, so a read-only role (a hand-granted reader) stays read-only and the answer says so.
            return held.CanEdit
                ? new OfficeEditAccess(true, "member")
                : new OfficeEditAccess(false, "member-read-only");
        }

        var outcome = await _membership.GrantMarkedWriterAsync(
            driveId, SpeContainerMembershipService.JitWriterMarkerPrefix, systemUserId.Value, upn, ct).ConfigureAwait(false);

        return outcome switch
        {
            SpeContainerMembershipService.MarkedGrantOutcome.Granted => new OfficeEditAccess(true, "writer-jit"),
            SpeContainerMembershipService.MarkedGrantOutcome.AlreadyHeld => new OfficeEditAccess(true, "member"),
            _ => throw Unavailable(documentId, "the edit grant could not be made"),
        };
    }

    /// <summary>
    /// The secure record whose OWN container <paramref name="driveId"/> is, or <see langword="null"/> for a shared
    /// (business-unit / environment) container. <c>virtual</c> so a test can name the classification at this seam
    /// (the resolver is sealed, ADR-010); production asks <see cref="RecordContainerResolver.ResolveOwningRecordAsync"/>.
    /// </summary>
    protected virtual Task<OwningSecureRecord?> ResolveSecureOwnerAsync(string driveId, CancellationToken ct)
        => _containerResolver.ResolveOwningRecordAsync(driveId, ct);

    private async Task<bool> IsRestrictedOrUnreadableAsync(OwningSecureRecord record, CancellationToken ct)
    {
        try
        {
            var row = await _entities.RetrieveAsync(record.EntityLogicalName, record.RecordId, ["sprk_accesspermission"], ct)
                .ConfigureAwait(false);
            return row is null
                   || row.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sprk_accesspermission")?.Value
                       == ExternalParticipationService.AccessPermissionRestricted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[OFFICE-EDIT] {Entity} {RecordId}'s access permission could not be read.",
                record.EntityLogicalName, record.RecordId);
            return true;
        }
    }

    private SdapProblemException Unavailable(Guid documentId, string reason)
    {
        _logger.LogError("[OFFICE-EDIT] Document {DocumentId}: {Reason}; edit access is not available.", documentId, reason);
        return new SdapProblemException(
            code: "edit_access_unavailable",
            title: "Edit access could not be prepared",
            detail: "You can edit this document, but access for Office could not be prepared just now. Try again shortly.",
            statusCode: 503);
    }
}
