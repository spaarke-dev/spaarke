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
/// <param name="CanEdit">The caller holds (or was just granted) an editing role, or is a standing writer of this
/// business-unit container (an enabled internal person whose own business unit maps to it).</param>
/// <param name="Role">"standing" (a standing writer of this business-unit container), "member" (already held an editing
/// role), "writer-jit" (granted now),
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
    /// <param name="describeSharedContainer">When <see langword="false"/> (the caller only needs the grant side — e.g.
    /// <c>/open-links</c>, which returns no edit verdict), a SHARED container is answered <c>not-evaluated</c> without
    /// reading anything: nothing is ever granted there, so the reads would buy nothing.</param>
    public virtual async Task<OfficeEditAccess> PrepareAsync(
        Guid documentId, string driveId, HttpContext httpContext, CancellationToken ct = default, bool describeSharedContainer = true)
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

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        if (secureOwner is null)
        {
            // A business-unit / environment container: nothing is granted here (round 70 — the sync keeps its
            // standing writers). What is REPORTED must be true for this caller, though (adversarial finding 9).
            return describeSharedContainer
                ? await StandingAccessAsync(documentId, driveId, token, ct).ConfigureAwait(false)
                : new OfficeEditAccess(false, "not-evaluated");
        }
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

    /// <summary>
    /// What a caller can do in Office on a SHARED (business-unit / environment) container — reported, never granted
    /// (task 171, adversarial finding 9). <c>standing</c> only for a caller the sync keeps as a standing writer HERE: an
    /// enabled internal person (<see cref="Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync.IsStandingEligible"/>)
    /// whose own business unit's container is this one. Anyone else is answered from the container's actual roles
    /// (one Graph read): an editing role → <c>member</c>; a reader → <c>member-read-only</c>; none → <c>none</c>. A fact
    /// that cannot be read → <c>unknown</c> (never "can edit").
    /// </summary>
    private async Task<OfficeEditAccess> StandingAccessAsync(Guid documentId, string driveId, string? token, CancellationToken ct)
    {
        try
        {
            var systemUserId = await _probe.GetCallerSystemUserIdAsync(token, ct).ConfigureAwait(false);
            if (systemUserId is not { } id)
            {
                return new OfficeEditAccess(false, "unknown");
            }

            var user = await _entities.RetrieveAsync("systemuser", id, StandingColumns, ct).ConfigureAwait(false);
            if (user is null)
            {
                return new OfficeEditAccess(false, "unknown");
            }

            if (Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync.IsStandingEligible(user)
                && user.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("businessunitid") is { Id: var unitId })
            {
                var unit = await _entities.RetrieveAsync("businessunit", unitId, ["sprk_containerid"], ct).ConfigureAwait(false);
                if (string.Equals(unit?.GetAttributeValue<string>("sprk_containerid")?.Trim(), driveId, StringComparison.Ordinal))
                {
                    return new OfficeEditAccess(true, "standing");
                }
            }

            // Not a standing writer of THIS container (another unit's record, an external user, a hand-granted member):
            // the container's own roles say what Office will allow.
            var access = await _membership.ReadAccessAsync(driveId, ct).ConfigureAwait(false);
            var held = access?.Roles.FirstOrDefault(r => r.IsFor(
                user.GetAttributeValue<string>("domainname"), user.GetAttributeValue<Guid?>("azureactivedirectoryobjectid")));
            return held is null
                ? new OfficeEditAccess(false, "none")
                : held.CanEdit ? new OfficeEditAccess(true, "member") : new OfficeEditAccess(false, "member-read-only");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[OFFICE-EDIT] Document {DocumentId}: the caller's standing access could not be determined; reported as unknown.",
                documentId);
            return new OfficeEditAccess(false, "unknown");
        }
    }

    private static readonly string[] StandingColumns =
    [
        "domainname", "azureactivedirectoryobjectid", "isdisabled", "accessmode", "applicationid", "sprk_isexternal",
        "businessunitid",
    ];

    private async Task<bool> IsRestrictedOrUnreadableAsync(OwningSecureRecord record, CancellationToken ct)
    {
        try
        {
            var row = await _entities.RetrieveAsync(record.EntityLogicalName, record.RecordId, ["sprk_accesspermission"], ct)
                .ConfigureAwait(false);
            if (row is null
                || row.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sprk_accesspermission")?.Value
                    == ExternalParticipationService.AccessPermissionRestricted)
                return true;

            // #1478 (task 175): Restricted THROUGH a parent counts (task 174's effective rule); a chain that cannot be read
            // counts as Restricted (fail closed: no grant for an external caller).
            return await Sprk.Bff.Api.Infrastructure.ExternalAccess.EffectiveRootFlags.RestrictedThroughFilingAsync(
                _entities, _logger, record.EntityLogicalName, record.RecordId, ct).ConfigureAwait(false) ?? true;
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
