using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// The document-pointer check — unified-access-control-r2 task 166 r1 (owner round 21 item 1, part b), made the
// INTERIM check of owner round 23 item 1 in task 166 r2.
//
// Same type as RecordContainerResolver.cs, split into its own file by reason-to-change (CLAUDE.md §11.5): this half
// answers "may the BFF follow THIS row's pointer as the application?" — an identity question (who created the item,
// who created the row) plus a tenancy question (is the container in the document owner's customer subtree) — while
// the main file answers where a record's content is placed. No new type, no new registration: the call sites and the
// DI registration (Program.cs) are unchanged.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed partial class RecordContainerResolver
{
    /// <summary>The code every app-only download refusal of an unverifiable document pointer carries.</summary>
    public const string DocumentStorageUnverifiedCode = "document_storage_unverified";

    /// <summary>An email attachment is a child document of the email document; its file lives where the email's does.</summary>
    private const string ParentDocumentColumn = "sprk_parentdocument";

    /// <summary>The system column naming the identity that CREATED the row.</summary>
    private const string CreatedByColumn = "createdby";

    /// <summary>
    /// The server-stamped "Created By (Person)" lookup to <c>systemuser</c> (tasks 133 / 146 c1-r1): the person an
    /// APP-created row was made for. Owner round 23: the row's creator is <c>createdby</c> when that is a person, else
    /// this column. Spelled here because task 146's shared constant (<c>Spaarke.Dataverse.RecordCreatorPersonColumn</c>)
    /// is not on this branch — switch to it at integration. Read in its OWN query: on an environment where the child
    /// schema has not been applied the column does not exist, and that must make only THIS answer unverifiable (owner
    /// round 17: a missing creator column is unverifiable), not every pointer check.
    /// </summary>
    internal const string CreatedByPersonColumn = "sprk_createdbyperson";

    /// <summary><c>systemuser.applicationid</c>: set for an application user (an Entra app's Dataverse identity).</summary>
    private const string ApplicationIdColumn = "applicationid";

    /// <summary><c>businessunit.parentbusinessunitid</c> — null on the root business unit only.</summary>
    private const string ParentBusinessUnitColumn = "parentbusinessunitid";

    /// <summary>
    /// How many business units one hierarchy read returns. Business-unit counts are small (tens); one read is cheaper and
    /// race-free compared with walking parent by parent (the <see cref="SpeAdminTenantScope"/> precedent). A hierarchy
    /// that does not fit is unverifiable and refuses.
    /// </summary>
    private const int BusinessUnitHierarchyLimit = 5000;

    /// <summary>
    /// The configuration keys that carry an Entra application (client) id the BFF authenticates AS: its API app
    /// registration, the user-assigned managed identity it uses for app-only Graph and Dataverse, and Dataverse's own
    /// client id. Together they are "the BFF identity" — the <c>createdBy.application.id</c> of an item the BFF uploaded
    /// app-only, and the <c>applicationid</c> of the Dataverse application user that creates rows app-only.
    /// </summary>
    internal static readonly string[] BffApplicationIdKeys =
    [
        "AzureAd:ClientId", "API_APP_ID", "Graph:ManagedIdentity:ClientId", "ManagedIdentity:ClientId", "Dataverse:ClientId",
    ];

    /// <summary>The distinct, non-empty GUIDs under <see cref="BffApplicationIdKeys"/>. Empty without configuration.</summary>
    internal static IReadOnlySet<Guid> BffApplicationIdsFrom(Microsoft.Extensions.Configuration.IConfiguration? configuration)
    {
        var ids = new HashSet<Guid>();
        if (configuration is null)
        {
            return ids;
        }

        foreach (var key in BffApplicationIdKeys)
        {
            if (Guid.TryParse(configuration[key], out var id) && id != Guid.Empty)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Throws <see cref="SdapProblemException"/> (<see cref="DocumentStorageUnverifiedCode"/>, 409) unless
    /// <see cref="IsDocumentPointerContainerAllowedAsync(Guid, string?, string?, CancellationToken)"/> accepts the
    /// pointer. Call it BEFORE every app-only read that follows a <c>sprk_document</c> row's <c>sprk_graphdriveid</c> /
    /// <c>sprk_graphitemid</c>, with exactly the drive and item that read will use.
    /// </summary>
    public async Task EnsureDocumentPointerContainerAsync(
        Guid documentId, string? pointerDriveId, string? pointerItemId, CancellationToken ct = default)
    {
        if (await IsDocumentPointerContainerAllowedAsync(documentId, pointerDriveId, pointerItemId, ct).ConfigureAwait(false))
        {
            return;
        }

        throw new SdapProblemException(
            code: DocumentStorageUnverifiedCode,
            title: "Document storage could not be verified",
            detail: "This document's file could not be verified as the one its record may use, so it is not served. "
                    + "An administrator must repair the document's storage location.",
            statusCode: 409);
    }

    /// <summary>
    /// May the BFF follow this document row's SharePoint Embedded pointer AS THE APPLICATION?
    /// </summary>
    /// <remarks>
    /// <para><b>The threat.</b> The BFF downloads a document's bytes as the managed identity from the drive and item the
    /// row's <c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c> name. Until the pointer columns are field-secured
    /// (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>, after the owed pointer-attach path and legacy migration) any
    /// Write holder can re-point a row — and rows forged before that lock stay forged.</para>
    /// <para><b>The interim rule — owner round 23 item 1 (task 166 r2).</b> Both halves must hold, and anything that
    /// cannot be decided refuses:</para>
    /// <list type="number">
    /// <item><b>The ITEM.</b> The drive item's <c>createdBy</c> (read app-only from Graph — the item must exist in THAT
    /// drive) is the row's creator: <c>createdby</c> when that is a person, else <see cref="CreatedByPersonColumn"/>
    /// (compared by Entra object id); or, for a row the BFF itself created, the BFF identity
    /// (<see cref="BffApplicationIdKeys"/>) for an item it uploaded app-only. Before r2 the item was not checked at all.</item>
    /// <item><b>The CONTAINER.</b> (a) A SECURE record's own container is honoured only for a document that hangs off
    /// that very record (one of its <c>DocumentLinkFields</c>, a related record the resolver resolves to that container,
    /// or its parent document, one level). (b) The communication archive container only on the ARCHIVE PATH: the row is
    /// linked to communication <c>C</c> (<c>sprk_relatedcommunication</c>, which every archive writer stamps) and the
    /// item is one the archive wrote for <c>C</c> (every archive upload is named <c>{C:N}_…</c>). (c) Otherwise a
    /// business unit's container, and only one in the DOCUMENT OWNER's customer subtree: the owner's top-level business
    /// unit (the ancestor directly under the root, or the root itself for a root-owned row) and everything beneath it.
    /// Under Model 1 — several customers as business units of one environment (owner round 20) — another customer's
    /// container is therefore refused. Before r2 any business unit's container, or the archive, was accepted.</item>
    /// </list>
    /// <para><b>Residual exposure until the strict check (recorded by owner round 23):</b> a pointer to ANOTHER
    /// legitimately uploaded item of the same creator, inside the owner's own customer subtree — for a BFF-created row
    /// "the same creator" is the BFF identity. The strict comparison (the row's pointer must equal the container
    /// derived for its record) replaces this rule once the pointer-attach path and the legacy migration land (owed;
    /// task note §19).</para>
    /// </remarks>
    public async Task<bool> IsDocumentPointerContainerAllowedAsync(
        Guid documentId, string? pointerDriveId, string? pointerItemId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(pointerDriveId) || string.IsNullOrWhiteSpace(pointerItemId))
        {
            return false;
        }

        var drive = pointerDriveId.Trim();
        var item = pointerItemId.Trim();
        try
        {
            var row = await _entityService.RetrieveAsync(
                "sprk_document", documentId,
                [CreatedByColumn, OwningBusinessUnitColumn, CrossPathLink.LinkedCommunicationAttribute], ct).ConfigureAwait(false);
            if (row is null)
            {
                return Refuse(documentId, "the document row could not be read");
            }

            if (_speFiles is null)
            {
                return Refuse(documentId, "no SharePoint Embedded reader is available to verify the item");
            }

            var creator = await _speFiles.GetItemCreatorAsync(drive, item, ct).ConfigureAwait(false);
            if (creator is null)
            {
                return Refuse(documentId, "the item is not in the drive the row names");
            }

            // (1) The CONTAINER.
            var secureOwner = await ResolveOwningRecordAsync(drive, ct).ConfigureAwait(false);
            if (secureOwner is not null)
            {
                if (!await DocumentHangsOffAsync(documentId, secureOwner, drive, depth: 0, ct).ConfigureAwait(false))
                {
                    return Refuse(documentId,
                        $"the pointer names the OWN container of secure {secureOwner.EntityLogicalName} {secureOwner.RecordId}, "
                        + "but the document does not belong to that record");
                }
            }
            else
            {
                // The archive may ALSO be a business unit's container (on dev, Communication:ArchiveContainerId is the
                // "Spaarke Demo" unit's), so either rule may admit the pointer: the archive path for the archive's own
                // records, the owner's customer subtree for everything else.
                var onTheArchivePath = !string.IsNullOrWhiteSpace(_archiveContainerId)
                                       && IsSameContainer(_archiveContainerId, drive)
                                       && IsArchivePathItem(row, creator);
                if (!onTheArchivePath && !await IsBusinessUnitContainerInOwnersSubtreeAsync(row, drive, ct).ConfigureAwait(false))
                {
                    return Refuse(documentId,
                        "the pointer names neither the archive's own record of this item nor a container in the document "
                        + "owner's customer subtree (another customer's, or none)");
                }
            }

            // (2) The ITEM.
            if (!await ItemWasCreatedByTheRowsCreatorAsync(documentId, row, creator, ct).ConfigureAwait(false))
            {
                return Refuse(documentId, "the item was not created by the document's creator");
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-POINTER] REFUSED: document {DocumentId}'s pointer could not be verified (fail closed).", documentId);
            return false;
        }
    }

    /// <summary>
    /// <see cref="IsDocumentPointerContainerAllowedAsync(Guid, string?, string?, CancellationToken)"/> for callers
    /// holding the document id as text (<see cref="DocumentEntity.Id"/>, job payloads). An id that is not a GUID names no
    /// <c>sprk_document</c> row, so its pointer cannot be verified — refused.
    /// </summary>
    public Task<bool> IsDocumentPointerContainerAllowedAsync(
        string? documentId, string? pointerDriveId, string? pointerItemId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            _logger.LogWarning(
                "[DOCUMENT-POINTER] REFUSED: '{DocumentId}' is not a document id, so the pointer it carries cannot be "
                + "verified (fail closed).", documentId);
            return Task.FromResult(false);
        }

        return IsDocumentPointerContainerAllowedAsync(id, pointerDriveId, pointerItemId, ct);
    }

    private bool Refuse(Guid documentId, string reason)
    {
        _logger.LogWarning(
            "[DOCUMENT-POINTER] REFUSED: document {DocumentId} — {Reason}. Not served app-only.", documentId, reason);
        return false;
    }

    /// <summary>
    /// The ITEM half of owner round 23 item 1: was the drive item created by the row's creator?
    /// </summary>
    /// <remarks>
    /// The row's creator is <c>createdby</c> when that is a person (no <c>applicationid</c>), else
    /// <see cref="CreatedByPersonColumn"/>; a person is compared by Entra object id (<c>azureactivedirectoryobjectid</c>
    /// against Graph's <c>createdBy.user.id</c>). An item uploaded app-only (no user id) is accepted only for a row the
    /// BFF itself created, and only when its <c>createdBy.application.id</c> is the BFF identity. Every unreadable or
    /// missing piece is a refusal.
    /// </remarks>
    private async Task<bool> ItemWasCreatedByTheRowsCreatorAsync(
        Guid documentId, Entity row, SpeItemCreator creator, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(CreatedByColumn) is not { Id: var createdById } || createdById == Guid.Empty)
        {
            return false;
        }

        var createdBy = await _entityService.RetrieveAsync(
            SystemUserEntity, createdById, [AzureAdObjectIdColumn, ApplicationIdColumn], ct).ConfigureAwait(false);
        if (createdBy is null)
        {
            return false;
        }

        var createdByApplication = createdBy.GetAttributeValue<Guid>(ApplicationIdColumn);
        var rowCreatedByPerson = createdByApplication == Guid.Empty;
        var rowCreatedByTheBff = !rowCreatedByPerson && _bffApplicationIds.Contains(createdByApplication);

        var personObjectId = rowCreatedByPerson
            ? NonEmpty(createdBy.GetAttributeValue<Guid>(AzureAdObjectIdColumn))
            : await ReadCreatedByPersonObjectIdAsync(documentId, ct).ConfigureAwait(false);

        if (Guid.TryParse(creator.UserObjectId, out var itemUser) && itemUser != Guid.Empty)
        {
            // Uploaded BY A PERSON: it must be the row's person.
            return personObjectId is { } person && person == itemUser;
        }

        // Uploaded app-only: only the BFF's own rows, and only by the BFF identity.
        return rowCreatedByTheBff
               && Guid.TryParse(creator.ApplicationId, out var itemApplication)
               && _bffApplicationIds.Contains(itemApplication);
    }

    /// <summary>
    /// The Entra object id of the person in <see cref="CreatedByPersonColumn"/>, or <see langword="null"/> — no person, a
    /// person that is an application user, or an unreadable column (including one this environment does not have yet).
    /// </summary>
    private async Task<Guid?> ReadCreatedByPersonObjectIdAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var row = await _entityService.RetrieveAsync("sprk_document", documentId, [CreatedByPersonColumn], ct).ConfigureAwait(false);
            if (row?.GetAttributeValue<EntityReference>(CreatedByPersonColumn) is not { Id: var personId } || personId == Guid.Empty)
            {
                return null;
            }

            var person = await _entityService.RetrieveAsync(
                SystemUserEntity, personId, [AzureAdObjectIdColumn, ApplicationIdColumn], ct).ConfigureAwait(false);
            if (person is null || person.GetAttributeValue<Guid>(ApplicationIdColumn) != Guid.Empty)
            {
                return null;
            }

            return NonEmpty(person.GetAttributeValue<Guid>(AzureAdObjectIdColumn));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Owner round 17: a missing (or unreadable) creator column is unverifiable — the caller refuses.
            _logger.LogInformation(ex,
                "[DOCUMENT-POINTER] '{Column}' of document {DocumentId} could not be read; its person is unverifiable.",
                CreatedByPersonColumn, documentId);
            return null;
        }
    }

    private static Guid? NonEmpty(Guid id) => id == Guid.Empty ? null : id;

    /// <summary>
    /// The archive path: the row is linked to communication <c>C</c> and the item is one the archive wrote FOR <c>C</c>
    /// — every archive writer (<c>CommunicationService</c>, <c>IncomingCommunicationProcessor</c>,
    /// <c>MessageAttachmentMaterializer</c>) names its upload <c>{C:N}_…</c> and stamps <c>sprk_relatedcommunication</c>.
    /// </summary>
    private static bool IsArchivePathItem(Entity row, SpeItemCreator creator)
    {
        if (row.GetAttributeValue<EntityReference>(CrossPathLink.LinkedCommunicationAttribute) is not { Id: var communicationId }
            || communicationId == Guid.Empty)
        {
            return false;
        }

        return creator.Name?.StartsWith($"{communicationId:N}_", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Does a business unit IN THE DOCUMENT OWNER'S CUSTOMER SUBTREE stamp <paramref name="drive"/> as its container?
    /// </summary>
    private async Task<bool> IsBusinessUnitContainerInOwnersSubtreeAsync(Entity row, string drive, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn) is not { Id: var ownerBusinessUnit }
            || ownerBusinessUnit == Guid.Empty)
        {
            return false;
        }

        var claimants = await BusinessUnitsClaimingAsync(drive, ct).ConfigureAwait(false);
        if (claimants.Count == 0)
        {
            return false;
        }

        var hierarchy = await LoadBusinessUnitHierarchyAsync(ct).ConfigureAwait(false);
        var subtree = CustomerSubtree(ownerBusinessUnit, hierarchy);
        return subtree is not null && claimants.Any(subtree.Contains);
    }

    /// <summary>The business units whose <c>sprk_containerid</c> is <paramref name="drive"/>.</summary>
    private async Task<IReadOnlyList<Guid>> BusinessUnitsClaimingAsync(string drive, CancellationToken ct)
    {
        var query = new QueryExpression(BusinessUnitEntity)
        {
            ColumnSet = new ColumnSet(ContainerColumn),
            TopCount = ClaimantProbeLimit,
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions = { new ConditionExpression(ContainerColumn, ConditionOperator.Like, $"%{EscapeForLike(drive)}%") }
            }
        };

        var results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return results?.Entities?
            .Where(e => e.Id != Guid.Empty && IsSameContainer(e.GetAttributeValue<string>(ContainerColumn), drive))
            .Select(e => e.Id)
            .ToList() ?? [];
    }

    /// <summary>Every business unit, child → parent (the root's parent is null). One read.</summary>
    private async Task<IReadOnlyDictionary<Guid, Guid?>> LoadBusinessUnitHierarchyAsync(CancellationToken ct)
    {
        var query = new QueryExpression(BusinessUnitEntity)
        {
            ColumnSet = new ColumnSet(ParentBusinessUnitColumn),
            TopCount = BusinessUnitHierarchyLimit,
        };

        var results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var units = results?.Entities ?? throw new InvalidOperationException("The business-unit hierarchy could not be read.");
        if (units.Count >= BusinessUnitHierarchyLimit)
        {
            throw new InvalidOperationException("The business-unit hierarchy is larger than one read; it cannot be verified.");
        }

        var map = new Dictionary<Guid, Guid?>();
        foreach (var unit in units)
        {
            var parent = unit.GetAttributeValue<EntityReference>(ParentBusinessUnitColumn)?.Id;
            map[unit.Id] = parent is { } p && p != Guid.Empty ? p : null;
        }

        return map;
    }

    /// <summary>
    /// The CUSTOMER subtree of <paramref name="ownerBusinessUnit"/>: its top-level business unit — the ancestor directly
    /// under the root — and every unit beneath it. A root-owned row's subtree is the root's (the operator level, the
    /// <see cref="SpeAdminTenantScope"/> precedent: an operator above the customer units reaches them). Null — refuse —
    /// when the unit is unknown, its chain is broken, or the hierarchy has a cycle.
    /// </summary>
    internal static IReadOnlyCollection<Guid>? CustomerSubtree(Guid ownerBusinessUnit, IReadOnlyDictionary<Guid, Guid?> hierarchy)
    {
        if (!hierarchy.ContainsKey(ownerBusinessUnit))
        {
            return null;
        }

        var customer = ownerBusinessUnit;
        var visited = new HashSet<Guid> { ownerBusinessUnit };
        while (hierarchy[customer] is { } parent)
        {
            if (!hierarchy.TryGetValue(parent, out var grandparent) || !visited.Add(parent))
            {
                return null;
            }

            if (grandparent is null)
            {
                break; // `parent` is the root: `customer` is the top-level unit.
            }

            customer = parent;
        }

        return SpeAdminTenantScope.CollectSelfAndDescendants(customer, hierarchy);
    }

    private async Task<bool> DocumentHangsOffAsync(
        Guid documentId, OwningSecureRecord owner, string drive, int depth, CancellationToken ct)
    {
        // The document's record links are the ONE canonical vocabulary (DocumentLinkFields — every sprk_document
        // record-link lookup, verified against live metadata). A link whose target this resolver cannot resolve to a
        // secure container simply does not match; it never widens what is honoured.
        var columns = DocumentLinkFields.LogicalNames.Append(ParentDocumentColumn).ToArray();
        var row = await _entityService.RetrieveAsync("sprk_document", documentId, columns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        foreach (var (column, target) in DocumentLinkFields.All.Select(f => (f.LogicalName, f.TargetEntityLogicalName)))
        {
            if (row.GetAttributeValue<EntityReference>(column) is not { Id: var linkedId } || linkedId == Guid.Empty)
            {
                continue;
            }

            if (string.Equals(target, owner.EntityLogicalName, StringComparison.Ordinal) && linkedId == owner.RecordId)
            {
                return true;
            }

            try
            {
                var decision = await ResolveForRecordAsync(target, linkedId, ct).ConfigureAwait(false);
                if (decision.Outcome == ContainerDecisionOutcome.ResolvedSecure && IsSameContainer(decision.ContainerId, drive))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One unresolvable link is not a match; the others may still be.
                _logger.LogInformation(ex,
                    "[DOCUMENT-POINTER] Link {Column} -> {Target} {LinkedId} of document {DocumentId} could not be resolved.",
                    column, target, linkedId, documentId);
            }
        }

        if (depth == 0 && row.GetAttributeValue<EntityReference>(ParentDocumentColumn) is { Id: var parentId }
            && parentId != Guid.Empty && parentId != documentId)
        {
            return await DocumentHangsOffAsync(parentId, owner, drive, depth: 1, ct).ConfigureAwait(false);
        }

        return false;
    }
}
