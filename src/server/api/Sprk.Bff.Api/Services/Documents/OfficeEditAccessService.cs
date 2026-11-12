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
/// role, or an item permission of ours that still stands), "writer-jit" (granted now), "item-writer-jit" (a flagged-external
/// caller granted a write permission on this ONE file now — task 191g),
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
/// <para><b>A flagged-external caller never gets a container role</b> (uac-r2 task 191g, owner Q2 = b, design §3.12). A
/// caller whose stored <c>sprk_isexternal</c> is true is routed to <see cref="PrepareFlaggedAsync"/>: with Write on the
/// secure record, the record not Restricted (effective through filing) and the document externally visible
/// (<see cref="ExternalVisibility"/>, through 191c's <see cref="ExternalVisibilityDecider"/>), the caller is granted a WRITE
/// permission on that ONE file (<see cref="SpeContainerMembershipService.GrantMarkedItemWriterAsync"/>), read back and
/// recorded; otherwise view only. The item grant is off unless <see cref="FlaggedOfficeEditEnabledConfigKey"/> is true;
/// off, a flagged caller gets view only (never a container role either way). A caller whose flag cannot be read gets no
/// grant at all (fail closed).</para>
/// <para><b>Why a class of its own</b> (CLAUDE.md §11): the decision combines the container classification
/// (<see cref="RecordContainerResolver"/>), the caller's Dataverse rights (<see cref="CallerRecordAccessProbe"/>) and
/// the grant primitive (<see cref="SpeContainerMembershipService"/>), and two routes need exactly the same decision.
/// Without it a secure document cannot be edited in Office at all.</para>
/// </remarks>
public class OfficeEditAccessService
{
    /// <summary>
    /// Task 191g: the item-level Office edit grant for flagged-external callers, and the sync's removal of flagged users'
    /// container roles on secure containers. Absent, empty or unparseable = off. Off, a flagged caller gets view only.
    /// Turning it on is design §3.19 step 8 (its own owner go).
    /// </summary>
    internal const string FlaggedOfficeEditEnabledConfigKey = "ExternalAccess:FlaggedOfficeEdit:Enabled";

    /// <summary>Whether <see cref="FlaggedOfficeEditEnabledConfigKey"/> parses to <c>true</c> in <paramref name="configuration"/>.</summary>
    internal static bool FlaggedOfficeEditEnabled(IConfiguration configuration)
        => bool.TryParse(configuration[FlaggedOfficeEditEnabledConfigKey]?.Trim(), out var on) && on;

    private readonly RecordContainerResolver _containerResolver;
    private readonly CallerRecordAccessProbe _probe;
    private readonly SpeContainerMembershipService _membership;
    private readonly IGenericEntityService _entities;
    private readonly Sprk.Bff.Api.Services.Access.ExternalVisibilityDecider _visibility;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OfficeEditAccessService> _logger;

    internal OfficeEditAccessService(
        RecordContainerResolver containerResolver,
        CallerRecordAccessProbe probe,
        SpeContainerMembershipService membership,
        IGenericEntityService entities,
        Sprk.Bff.Api.Services.Access.ExternalVisibilityDecider visibility,
        IConfiguration configuration,
        ILogger<OfficeEditAccessService> logger)
    {
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
    /// <param name="itemId">The document's drive item (<c>sprk_graphitemid</c>, already pointer-checked by the route): the one
    /// file a flagged-external caller may be granted (task 191g). Not used for any other caller.</param>
    public virtual async Task<OfficeEditAccess> PrepareAsync(
        Guid documentId, string driveId, HttpContext httpContext, CancellationToken ct = default, bool describeSharedContainer = true,
        string? itemId = null)
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
        Microsoft.Xrm.Sdk.Entity? user;
        try
        {
            user = systemUserId is { } id
                ? await _entities.RetrieveAsync("systemuser", id, ["domainname", "azureactivedirectoryobjectid", "sprk_isexternal"], ct).ConfigureAwait(false)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Task 191g: the caller's sprk_isexternal cannot be read, so whether a container role is allowed is unknown —
            // no grant of any kind (fail closed), and an honest 503 like the unresolved-user branch below (never links that
            // look usable and that Office then refuses).
            _logger.LogWarning(ex,
                "[OFFICE-EDIT] Document {DocumentId}: the caller's Dataverse user could not be read; no edit access is granted.",
                documentId);
            throw Unavailable(documentId, "the caller's Dataverse user could not be read");
        }

        var upn = user?.GetAttributeValue<string>("domainname");
        var objectId = user?.GetAttributeValue<Guid?>("azureactivedirectoryobjectid");
        if (user is null || systemUserId is null || string.IsNullOrWhiteSpace(upn))
        {
            throw Unavailable(documentId, "the caller's Dataverse user (and its sign-in name) could not be resolved");
        }

        // Task 191g (owner Q2 = b): a flagged-external caller NEVER reaches the container grant below.
        if (user.GetAttributeValue<bool?>("sprk_isexternal") == true)
        {
            return await PrepareFlaggedAsync(documentId, driveId, itemId, secureOwner, systemUserId.Value, objectId, ct)
                .ConfigureAwait(false);
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
    /// Task 191g (owner Q2 = b, design §3.12): Office edit for a flagged-external caller who already holds Write on the secure
    /// record (asked as the caller, above). Never a container role. An item write permission on <paramref name="itemId"/> only
    /// when the setting is on, the record is not Restricted (effective through filing; unreadable counts as Restricted) and
    /// <see cref="ExternalVisibility"/> answers Visible for the document (Hidden and Undecided are not visible). Otherwise view
    /// only. A grant that cannot be made, read back or recorded is 503 <c>edit_access_unavailable</c>.
    /// </summary>
    private async Task<OfficeEditAccess> PrepareFlaggedAsync(
        Guid documentId, string driveId, string? itemId, OwningSecureRecord secureOwner, Guid systemUserId, Guid? objectId,
        CancellationToken ct)
    {
        // Round 67 / Q4 = a: a Restricted record admits no flagged user (a record whose flag cannot be read counts as Restricted).
        if (await IsRestrictedOrUnreadableAsync(secureOwner, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "[OFFICE-EDIT] Document {DocumentId}: a flagged-external caller on Restricted (or unreadable) {Entity} {RecordId}; no grant.",
                documentId, secureOwner.EntityLogicalName, secureOwner.RecordId);
            return new OfficeEditAccess(false, "none");
        }

        if (!FlaggedOfficeEditEnabled(_configuration))
        {
            _logger.LogInformation(
                "[OFFICE-EDIT] Document {DocumentId}: a flagged-external caller; the item-level grant is off ({Setting}), view only.",
                documentId, FlaggedOfficeEditEnabledConfigKey);
            return new OfficeEditAccess(false, "none");
        }

        ExternalVisibilityDecision decision;
        bool faulted;
        try
        {
            var result = await _visibility([("sprk_document", documentId)], ct).ConfigureAwait(false);
            decision = result.For("sprk_document", documentId);
            faulted = result.Faulted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[OFFICE-EDIT] Document {DocumentId}: its external visibility could not be decided; no item grant (view only).", documentId);
            return new OfficeEditAccess(false, "none");
        }

        // Only a decision that did not fault counts as Visible (a faulted one makes every row ReadFailed; trust nothing else in it).
        if (faulted || !decision.IsVisible)
        {
            _logger.LogInformation(
                "[OFFICE-EDIT] Document {DocumentId}: not externally visible ({Verdict}, {Reason}, faulted={Faulted}); no item grant (view only).",
                documentId, decision.Verdict, decision.Reason, faulted);
            return new OfficeEditAccess(false, "none");
        }

        if (string.IsNullOrWhiteSpace(itemId) || objectId is not { } oid || oid == Guid.Empty)
        {
            throw Unavailable(documentId, "the document's file or the caller's sign-in identity is unknown, so no item grant can be made");
        }

        var outcome = await _membership.GrantMarkedItemWriterAsync(driveId, itemId, documentId, systemUserId, oid, ct)
            .ConfigureAwait(false);
        return outcome switch
        {
            SpeContainerMembershipService.ItemGrantOutcome.Granted => new OfficeEditAccess(true, "item-writer-jit"),
            SpeContainerMembershipService.ItemGrantOutcome.Reused => new OfficeEditAccess(true, "member"),
            SpeContainerMembershipService.ItemGrantOutcome.HeldByOther => new OfficeEditAccess(true, "member"),
            SpeContainerMembershipService.ItemGrantOutcome.HeldReadOnly => new OfficeEditAccess(false, "member-read-only"),
            _ => throw Unavailable(documentId, "the item edit grant could not be made"),
        };
    }

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
