using System.Collections.Concurrent;
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
// INTERIM check of owner round 23 item 1 in task 166 r2, and given its STRICT successor (the derived-container rule
// round 21 decided) behind a flag in task 166 f1.
//
// Same type as RecordContainerResolver.cs, split into its own file by reason-to-change (CLAUDE.md §11.5): this half
// answers "may the BFF follow THIS row's pointer as the application?" and "where does THIS document's file belong?"
// — an identity question (who created the item, who created the row) plus a tenancy / placement question — while the
// main file answers where a record's content is placed. No new type registration: the call sites and the DI
// registration (Program.cs) are unchanged.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed partial class RecordContainerResolver
{
    /// <summary>The code every app-only download refusal of an unverifiable document pointer carries.</summary>
    public const string DocumentStorageUnverifiedCode = "document_storage_unverified";

    /// <summary>
    /// The configuration flag that switches the check from the round-23 INTERIM rule to the STRICT derived-container rule
    /// (owner round 21 item 1 (b); task 166 f1). Default <see langword="false"/> = the interim rule. The main session sets
    /// it to <c>true</c> (App Service setting <c>DocumentPointer__StrictDerivedContainer</c>) only after the legacy
    /// migration's <c>-Verify</c> has passed — that gate proves the flip refuses no document the interim rule serves.
    /// </summary>
    public const string StrictDerivedContainerKey = "DocumentPointer:StrictDerivedContainer";

    /// <summary>
    /// The configuration flag that ends the transition of owner round 72 item 1 (task 171, adversarial finding 4): once
    /// <c>scripts/Invoke-DocumentItemIdBoundBackfill.ps1 -Verify</c> has passed, the main session sets it to <c>true</c>
    /// (App Service setting <c>DocumentPointer__ItemIdBoundBackfillComplete</c>) and a row whose field-secured item-id copy
    /// is EMPTY is refused. Default <see langword="false"/>: an empty copy falls back to the rule in force (interim or
    /// strict) exactly as before — a row not yet backfilled is not newly refused. A copy that DIFFERS from the item is
    /// refused in both states.
    /// </summary>
    public const string ItemIdBoundBackfillCompleteKey = "DocumentPointer:ItemIdBoundBackfillComplete";

    /// <summary>
    /// <c>EmailProcessing:DefaultContainerId</c> — the Office save path's last-resort container for content with no
    /// record when the acting user's business unit has none (<c>OfficeService</c>). The derived container of an UNFILED
    /// document whose owner's business unit stamps no container is this one, exactly as that save chose it.
    /// </summary>
    internal const string UnfiledDefaultContainerKey = "EmailProcessing:DefaultContainerId";

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

    /// <summary>The communication a document belongs to (the archive path's link; every archive writer stamps it).</summary>
    private const string CommunicationEntity = "sprk_communication";

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
    /// <remarks>
    /// An environment whose app-only Graph or Dataverse identity is NOT under one of these keys (for example a
    /// system-assigned managed identity with no client-id setting) cannot recognise the rows and items the BFF made, so
    /// under the interim rule every BFF-created document is refused (fail closed). The deployment guide names the keys
    /// (task 166 f1, verifier item 12).
    /// </remarks>
    internal static readonly string[] BffApplicationIdKeys =
    [
        "AzureAd:ClientId", "API_APP_ID", "Graph:ManagedIdentity:ClientId", "ManagedIdentity:ClientId", "Dataverse:ClientId",
    ];

    /// <summary>The STRICT rule is in force (see <see cref="StrictDerivedContainerKey"/>).</summary>
    private readonly bool _strictDerivedContainer;

    /// <summary>An EMPTY item-id copy refuses (see <see cref="ItemIdBoundBackfillCompleteKey"/>).</summary>
    private readonly bool _itemIdBoundBackfillComplete;

    /// <summary><see cref="UnfiledDefaultContainerKey"/>, when configured.</summary>
    private readonly string? _unfiledDefaultContainerId;

    // Per-SCOPE memo of the two directory reads every interim check repeats (task 166 f1, verifier item 13): the
    // resolver is registered Scoped, so this lives for one request (a bulk download of N documents reads the hierarchy
    // once, not N times) or one job run. Only SUCCESSFUL answers are kept — a faulted read is dropped, so a later call in
    // the same scope asks again; nothing crosses requests, so a change to the directory is seen by the next request.
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<Guid>>> _claimantsMemo = new(StringComparer.Ordinal);
    private Task<IReadOnlyDictionary<Guid, Guid?>>? _hierarchyMemo;

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

    /// <summary><see cref="StrictDerivedContainerKey"/> as a boolean; absent, blank or unparseable = the interim rule.</summary>
    internal static bool StrictDerivedContainerFrom(Microsoft.Extensions.Configuration.IConfiguration? configuration)
        => bool.TryParse(configuration?[StrictDerivedContainerKey], out var strict) && strict;

    /// <summary><see cref="ItemIdBoundBackfillCompleteKey"/> as a boolean; absent, blank or unparseable = the transition.</summary>
    internal static bool ItemIdBoundBackfillCompleteFrom(Microsoft.Extensions.Configuration.IConfiguration? configuration)
        => bool.TryParse(configuration?[ItemIdBoundBackfillCompleteKey], out var complete) && complete;

    /// <summary>
    /// Owner round 72 item 1 (task 171, adversarial finding 4): why the row's item id may NOT be followed because its
    /// field-secured copy (<see cref="Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn"/>) disagrees with the
    /// item about to be read, or <see langword="null"/> when the copy agrees (or is empty during the transition).
    /// </summary>
    /// <remarks>
    /// <c>sprk_graphitemid</c> cannot be field-secured (it is in an alternate key), so any Write holder can re-point it;
    /// the copy can only be written by the BFF. A copy naming a DIFFERENT item means the pointer was changed outside the
    /// BFF — refused under both rules. An EMPTY copy is a row not yet backfilled (or written outside the BFF): refused once
    /// <see cref="ItemIdBoundBackfillCompleteKey"/> is set, otherwise left to the rule in force.
    /// <para>The ONE definition: the pointer check and <c>DocumentContainerRelocator</c> (which must never move — and so
    /// re-bind — a forged pointer) both call it. <paramref name="row"/> must carry the copy column.</para>
    /// </remarks>
    internal string? ItemBindingRefusal(Entity row, string item)
        => Spaarke.Dataverse.DocumentPointerBinding.Compare(
                row.GetAttributeValue<string>(Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn), item) switch
        {
            Spaarke.Dataverse.DocumentPointerBinding.BindingState.Mismatch =>
                "the item id differs from its field-secured copy (sprk_graphitemidbound) — the pointer was changed outside the BFF",
            Spaarke.Dataverse.DocumentPointerBinding.BindingState.Unbound when _itemIdBoundBackfillComplete =>
                "the item id has no field-secured copy (sprk_graphitemidbound is empty) and the backfill is complete",
            _ => null,
        };

    /// <summary>Which rule <see cref="IsDocumentPointerContainerAllowedAsync(Guid, string?, string?, CancellationToken)"/> applies.</summary>
    internal bool StrictDerivedContainerMode => _strictDerivedContainer;

    /// <summary>Is <paramref name="drive"/> the same container id as <paramref name="container"/> (trimmed, ordinal)?</summary>
    internal static bool IsSameContainerId(string? container, string? drive)
        => !string.IsNullOrWhiteSpace(drive) && IsSameContainer(container, drive.Trim());

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
    /// (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>) any Write holder can re-point a row — and rows forged before
    /// that lock stay forged.</para>
    /// <para><b>Two rules, one flag</b> (<see cref="StrictDerivedContainerKey"/>):</para>
    /// <list type="bullet">
    /// <item><b>STRICT</b> (round 21 item 1 (b), task 166 f1): the pointer's container must be the container DERIVED for
    /// the document's own record (<see cref="DeriveDocumentContainersAsync"/>) and the item must be in it. In force once
    /// the client no longer writes pointers (the pointer-attach route), the pointer columns are locked, and the legacy
    /// migration has moved every misplaced file — the main session flips the flag after the migration's <c>-Verify</c>.
    /// Its residual: a row forged BEFORE the lock that points at another item of the SAME derived container (round 21:
    /// "the check does not stop the write"; the lock does).</item>
    /// <item><b>INTERIM</b> (round 23 item 1, the default): <see cref="IsAllowedUnderInterimRuleAsync"/>.</item>
    /// </list>
    /// </remarks>
    public Task<bool> IsDocumentPointerContainerAllowedAsync(
        Guid documentId, string? pointerDriveId, string? pointerItemId, CancellationToken ct = default)
        => _strictDerivedContainer
            ? IsAllowedUnderStrictRuleAsync(documentId, pointerDriveId, pointerItemId, ct)
            : IsAllowedUnderInterimRuleAsync(documentId, pointerDriveId, pointerItemId, ct);

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

    /// <summary>
    /// The INTERIM rule — owner round 23 item 1 (task 166 r2), with round 25 item 6's root rule (task 166 f1). Both
    /// halves must hold, and anything that cannot be decided refuses:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>The ITEM.</b> The drive item's <c>createdBy</c> (read app-only from Graph — the item must exist in THAT
    /// drive) is the row's creator: <c>createdby</c> when that is a person, else <see cref="CreatedByPersonColumn"/>
    /// (compared by Entra object id); or, for a row the BFF itself created, the BFF identity
    /// (<see cref="BffApplicationIdKeys"/>) for an item it uploaded app-only.</item>
    /// <item><b>The CONTAINER.</b> (a) A SECURE record's own container is honoured only for a document that hangs off
    /// that very record (one of its <c>DocumentLinkFields</c>, a related record the resolver resolves to that container,
    /// or its parent document, one level). (b) The communication archive container only on the ARCHIVE PATH: the row is
    /// linked to communication <c>C</c> (<c>sprk_relatedcommunication</c>, which every archive writer stamps) and the
    /// item is one the archive wrote for <c>C</c> (every archive upload is named <c>{C:N}_…</c>). (c) Otherwise a
    /// business unit's container, and only one in the DOCUMENT OWNER's customer subtree: the owner's top-level business
    /// unit (the ancestor directly under the root) and everything beneath it — and for a ROOT-owned row, only a
    /// container the root unit itself stamps (round 25 item 6: no cross-customer re-pointing under Model 1).</item>
    /// </list>
    /// <para><b>Residual exposure until the strict check (recorded by owner round 23):</b> a pointer to ANOTHER
    /// legitimately uploaded item of the same creator, inside the owner's own customer subtree — for a BFF-created row
    /// "the same creator" is the BFF identity.</para>
    /// <para><b>A file the BFF placed (owner round 37 item 3, task 166 f1-v1).</b> When those two halves refuse an item
    /// that the BFF IDENTITY uploaded app-only — a file <c>DocumentContainerRelocator</c> copied into the document's
    /// derived container (the legacy migration and every Make Secure move), whose uploader can never be the row's
    /// person — the interim rule ALSO serves it, but only when the pointer passes the STRICT derived-container test
    /// (<see cref="StrictRefusalAsync"/>: the drive is the container derived for this document and the item is in it).
    /// That disjunct admits nothing the strict rule refuses, so the interim rule is never weaker than the strict rule; its
    /// residual is the strict rule's (a pre-lock forged pointer to another BFF-placed item of the SAME derived container).
    /// Without it every relocated file was refused for app-only download from the migration's <c>-Apply</c> until the
    /// strict flip, and every Make Secure move while the interim rule is in force.</para>
    /// </remarks>
    internal async Task<bool> IsAllowedUnderInterimRuleAsync(
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
                [CreatedByColumn, OwningBusinessUnitColumn, CrossPathLink.LinkedCommunicationAttribute,
                 Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn], ct).ConfigureAwait(false);
            if (row is null)
            {
                return Refuse(documentId, "the document row could not be read");
            }

            // Round 72 item 1: before anything else, the item must be the one the BFF bound to this row.
            if (ItemBindingRefusal(row, item) is { } bindingRefusal)
            {
                return Refuse(documentId, bindingRefusal);
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

            var refusal = await InterimRefusalAsync(documentId, row, drive, creator, ct).ConfigureAwait(false);
            if (refusal is null)
            {
                return true;
            }

            // Round 37 item 3: a file the BFF identity placed is served when — and only when — the strict rule would
            // serve it. Anything the strict test refuses (another container, an undecidable document) stays refused.
            if (IsUploadedByTheBffIdentity(creator))
            {
                var strictRefusal = await StrictRefusalAsync(documentId, drive, item, creator, ct).ConfigureAwait(false);
                if (strictRefusal is null)
                {
                    _logger.LogInformation(
                        "[DOCUMENT-POINTER] served under the interim rule: document {DocumentId}'s item {Item} was placed by the "
                        + "BFF identity in the document's derived container {Drive} (owner round 37 item 3).",
                        documentId, item, drive);
                    return true;
                }

                refusal = $"{refusal}; and, uploaded by the BFF identity, it fails the derived-container test ({strictRefusal})";
            }

            return Refuse(documentId, refusal);
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
    /// The STRICT rule — owner round 21 item 1 (b), task 166 f1: the pointer's drive is a container DERIVED for the
    /// document (<see cref="DeriveDocumentContainersAsync"/>) and the item exists in that drive. Anything undecidable —
    /// an underivable container, an unreadable row, no item reader, a Graph fault — refuses.
    /// </summary>
    internal async Task<bool> IsAllowedUnderStrictRuleAsync(
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
            // Round 72 item 1: the item must be the one the BFF bound to this row (the strict derivation does not look at
            // the item id, so without this a pre- or post-lock re-point inside the derived container would be served).
            var row = await _entityService.RetrieveAsync(
                "sprk_document", documentId, [Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn], ct).ConfigureAwait(false);
            if (row is null)
            {
                return Refuse(documentId, "the document row could not be read");
            }

            if (ItemBindingRefusal(row, item) is { } bindingRefusal)
            {
                return Refuse(documentId, bindingRefusal);
            }

            var refusal = await StrictRefusalAsync(documentId, drive, item, knownItem: null, ct).ConfigureAwait(false);
            return refusal is null || Refuse(documentId, refusal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-POINTER] REFUSED (strict): document {DocumentId}'s pointer could not be verified (fail closed).",
                documentId);
            return false;
        }
    }

    private bool Refuse(Guid documentId, string reason)
    {
        _logger.LogWarning(
            "[DOCUMENT-POINTER] REFUSED: document {DocumentId} — {Reason}. Not served app-only.", documentId, reason);
        return false;
    }

    /// <summary>
    /// The round-23 interim rule's two halves for an item that EXISTS in <paramref name="drive"/>: why it refuses, or
    /// <see langword="null"/> when both halves hold. Faults propagate (the caller refuses).
    /// </summary>
    private async Task<string?> InterimRefusalAsync(
        Guid documentId, Entity row, string drive, SpeItemCreator creator, CancellationToken ct)
    {
        // (1) The CONTAINER.
        var secureOwner = await ResolveOwningRecordAsync(drive, ct).ConfigureAwait(false);
        if (secureOwner is not null)
        {
            if (!await DocumentHangsOffAsync(documentId, secureOwner, drive, depth: 0, ct).ConfigureAwait(false))
            {
                return $"the pointer names the OWN container of secure {secureOwner.EntityLogicalName} {secureOwner.RecordId}, "
                       + "but the document does not belong to that record";
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
                return "the pointer names neither the archive's own record of this item nor a container in the document "
                       + "owner's customer subtree (another customer's, a child unit's for a root-owned row, or none)";
            }
        }

        // (2) The ITEM.
        return await ItemWasCreatedByTheRowsCreatorAsync(documentId, row, creator, ct).ConfigureAwait(false)
            ? null
            : "the item was not created by the document's creator";
    }

    /// <summary>
    /// The STRICT derived-container test: why it refuses, or <see langword="null"/> when the pointer's drive is a
    /// container derived for the document and the item exists in it. <paramref name="knownItem"/> is the item's Graph
    /// facts when the caller already read them from that drive (no second read). Faults propagate (the caller refuses).
    /// </summary>
    private async Task<string?> StrictRefusalAsync(
        Guid documentId, string drive, string item, SpeItemCreator? knownItem, CancellationToken ct)
    {
        if (_speFiles is null)
        {
            return "no SharePoint Embedded reader is available to verify the item";
        }

        var derivation = await DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (!derivation.Decided)
        {
            return $"its container cannot be derived ({derivation.Reason})";
        }

        if (!derivation.Allows(drive))
        {
            return $"the pointer names a container that is not the one derived for the document ({derivation.Reason})";
        }

        if ((knownItem ?? await _speFiles.GetItemCreatorAsync(drive, item, ct).ConfigureAwait(false)) is null)
        {
            return "the item is not in the drive the row names";
        }

        return null;
    }

    /// <summary>
    /// Did the BFF IDENTITY upload this item app-only? Graph reports no user and an application id under
    /// <see cref="BffApplicationIdKeys"/>. Owner round 37 item 3: such an item is one the BFF placed — a relocation copy —
    /// and the interim rule serves it when the strict derived-container test passes.
    /// </summary>
    internal bool IsUploadedByTheBffIdentity(SpeItemCreator item)
        => (!Guid.TryParse(item.UserObjectId, out var user) || user == Guid.Empty)
           && Guid.TryParse(item.ApplicationId, out var application)
           && _bffApplicationIds.Contains(application);

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // THE DERIVED CONTAINER (task 166 f1) — where a document's file BELONGS, by the same answers that place content.
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The container(s) a document's file BELONGS in, derived server-side from the document's own record links — the
    /// strict rule's reference, the pointer-attach route's check, and the legacy migration's / Make Secure's target.
    /// </summary>
    /// <remarks>
    /// <para>Derived from the SAME answers that place content (this type's forward resolution), so a file the record-keyed
    /// upload routes, the Office save, Compose and the communication archive stored is in a container the derivation
    /// names:</para>
    /// <list type="number">
    /// <item>Every record link on the row (<see cref="DocumentLinkFields"/>) whose target can OWN content — a project,
    /// matter or work assignment, or a record that can hang off one (<c>ChildAncestorLinks</c>: event, invoice, to-do,
    /// analysis, agreement, budget, report card, service request) — is resolved with <see cref="ResolveForRecordAsync(string, Guid, CancellationToken)"/>;
    /// <c>sprk_relatedcommunication</c> with the communication pipeline's own answer (the communication's secure root,
    /// else <c>Communication:ArchiveContainerId</c>). A PARTY link (contact, organization) or the OOB <c>email</c> activity
    /// owns no content and is not consulted.</item>
    /// <item>No such link: the PARENT document's derivation (one level — an email attachment lives where its email
    /// does).</item>
    /// <item>Still nothing (an unfiled document): the container of the document's OWNING business unit — where the
    /// record-less upload routes put the bytes — else <see cref="UnfiledDefaultContainerKey"/>.</item>
    /// </list>
    /// <para><b>A secure answer dominates.</b> If any link resolves to a SECURE container, that container is the ONLY
    /// allowed one (two different secure containers are ambiguous and undecidable). Otherwise every non-secure answer is
    /// allowed (a document filed to two non-secure records may live in either one's container) and the first, in
    /// <see cref="DocumentLinkFields"/> order, is the primary — the migration's target.</para>
    /// <para><b>Fail closed.</b> An unreadable row, a link whose resolution throws (an ambiguous or unreadable ancestor,
    /// a secure record with no container) or no derivable container is NOT decided; the strict rule refuses and the
    /// migration reports it.</para>
    /// </remarks>
    public async Task<DocumentContainerDerivation> DeriveDocumentContainersAsync(Guid documentId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty)
        {
            return DocumentContainerDerivation.Undecided("an empty document id names no document");
        }

        try
        {
            return await DeriveCoreAsync(documentId, depth: 0, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[DOCUMENT-CONTAINER] The container of document {DocumentId} could not be derived.", documentId);
            return DocumentContainerDerivation.Undecided($"the document or one of its records could not be read ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// The container(s) a COMMUNICATION's content belongs in — the communication pipeline's own answer (its secure root's
    /// own container, else <c>Communication:ArchiveContainerId</c>), exactly the answer <see cref="DeriveDocumentContainersAsync"/>
    /// gives a document linked to that communication (<c>sprk_relatedcommunication</c>).
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 166, owner round 45 item 3: a <c>sprk_communicationattachment</c> row that names a
    /// moved file but is not linked to the moved document is classified by the subtree of the record its communication is
    /// regarding — inside the moved file's container (re-keyed to the copy), outside it (the source stays for it), or
    /// undecidable (pending). Fail closed: an unreadable communication or an ambiguous / refused resolution is NOT decided.
    /// </remarks>
    internal async Task<DocumentContainerDerivation> DeriveCommunicationContainersAsync(Guid communicationId, CancellationToken ct = default)
    {
        if (communicationId == Guid.Empty)
        {
            return DocumentContainerDerivation.Undecided("an empty communication id names no communication");
        }

        try
        {
            var decision = await ResolveForRecordWithFixedFallbackAsync(CommunicationEntity, communicationId, _archiveContainerId, ct)
                .ConfigureAwait(false);
            return decision.Outcome switch
            {
                ContainerDecisionOutcome.ResolvedSecure when !string.IsNullOrWhiteSpace(decision.ContainerId)
                    => new DocumentContainerDerivation(true, [decision.ContainerId!.Trim()], decision.ContainerId!.Trim(), IsSecure: true,
                        "the secure container of the communication's record"),
                ContainerDecisionOutcome.ResolvedFallback when !string.IsNullOrWhiteSpace(decision.ContainerId)
                    => new DocumentContainerDerivation(true, [decision.ContainerId!.Trim()], decision.ContainerId!.Trim(), IsSecure: false,
                        "the communication's container (its record is not secure)"),
                _ => DocumentContainerDerivation.Undecided($"the communication resolved to {decision.Outcome}"),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[DOCUMENT-CONTAINER] The container of communication {CommunicationId} could not be derived.", communicationId);
            return DocumentContainerDerivation.Undecided($"the communication or one of its records could not be read ({ex.GetType().Name})");
        }
    }

    private async Task<DocumentContainerDerivation> DeriveCoreAsync(Guid documentId, int depth, CancellationToken ct)
    {
        var columns = DocumentLinkFields.LogicalNames
            .Append(ParentDocumentColumn)
            .Append(OwningBusinessUnitColumn)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var row = await _entityService.RetrieveAsync("sprk_document", documentId, columns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return DocumentContainerDerivation.Undecided("the document row could not be read");
        }

        var secure = new List<string>();
        var plain = new List<string>();
        var consulted = new List<string>();

        foreach (var link in DocumentLinkFields.All)
        {
            if (row.GetAttributeValue<EntityReference>(link.LogicalName) is not { Id: var linkedId } || linkedId == Guid.Empty)
            {
                continue;
            }

            ContainerDecision decision;
            if (string.Equals(link.TargetEntityLogicalName, CommunicationEntity, StringComparison.Ordinal))
            {
                decision = await ResolveForRecordWithFixedFallbackAsync(CommunicationEntity, linkedId, _archiveContainerId, ct)
                    .ConfigureAwait(false);
            }
            else if (ChildAncestorLinks.KindOf(link.TargetEntityLogicalName) is ChildAncestorLinks.RecordKind.Root
                     or ChildAncestorLinks.RecordKind.Intermediate)
            {
                decision = await ResolveForRecordAsync(link.TargetEntityLogicalName, linkedId, ct).ConfigureAwait(false);
            }
            else
            {
                // A party, or the OOB email activity: not an owner of content.
                continue;
            }

            consulted.Add(link.LogicalName);
            switch (decision.Outcome)
            {
                case ContainerDecisionOutcome.ResolvedSecure when !string.IsNullOrWhiteSpace(decision.ContainerId):
                    secure.Add(decision.ContainerId!.Trim());
                    break;
                case ContainerDecisionOutcome.ResolvedFallback when !string.IsNullOrWhiteSpace(decision.ContainerId):
                    plain.Add(decision.ContainerId!.Trim());
                    break;
                case ContainerDecisionOutcome.Unresolved:
                    break;
                default:
                    return DocumentContainerDerivation.Undecided(
                        $"{link.LogicalName} -> {link.TargetEntityLogicalName} resolved to {decision.Outcome}");
            }
        }

        if (secure.Count == 0 && plain.Count == 0 && consulted.Count == 0 && depth == 0
            && row.GetAttributeValue<EntityReference>(ParentDocumentColumn) is { Id: var parentId }
            && parentId != Guid.Empty && parentId != documentId)
        {
            var viaParent = await DeriveCoreAsync(parentId, depth: 1, ct).ConfigureAwait(false);
            return viaParent.Decided
                ? viaParent with { Reason = $"the parent document's: {viaParent.Reason}" }
                : DocumentContainerDerivation.Undecided($"the parent document's container: {viaParent.Reason}");
        }

        var distinctSecure = secure.Distinct(StringComparer.Ordinal).ToList();
        if (distinctSecure.Count > 1)
        {
            return DocumentContainerDerivation.Undecided("its records resolve to two different secure containers (ambiguous)");
        }

        if (distinctSecure.Count == 1)
        {
            return new DocumentContainerDerivation(
                true, distinctSecure, distinctSecure[0], IsSecure: true,
                $"the secure container of its record ({string.Join(", ", consulted)})");
        }

        var distinctPlain = plain.Distinct(StringComparer.Ordinal).ToList();
        if (distinctPlain.Count > 0)
        {
            return new DocumentContainerDerivation(
                true, distinctPlain, distinctPlain[0], IsSecure: false,
                $"the container(s) of its record(s) ({string.Join(", ", consulted)})");
        }

        if (consulted.Count > 0)
        {
            return DocumentContainerDerivation.Undecided(
                $"its records ({string.Join(", ", consulted)}) resolve to no container (their business units stamp none)");
        }

        // Unfiled: the owning business unit's container — where the record-less upload routes store the bytes.
        var ownerContainer = await ResolveOwningBusinessUnitContainerAsync(row, "sprk_document", documentId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(ownerContainer))
        {
            return new DocumentContainerDerivation(
                true, [ownerContainer.Trim()], ownerContainer.Trim(), IsSecure: false,
                "the owning business unit's container (unfiled document)");
        }

        if (!string.IsNullOrWhiteSpace(_unfiledDefaultContainerId))
        {
            return new DocumentContainerDerivation(
                true, [_unfiledDefaultContainerId.Trim()], _unfiledDefaultContainerId.Trim(), IsSecure: false,
                $"{UnfiledDefaultContainerKey} (unfiled document; its business unit stamps no container)");
        }

        return DocumentContainerDerivation.Undecided("it is unfiled and its business unit stamps no container");
    }

    /// <summary>
    /// The relocation source check of the legacy migration (task 166 f1, owner round 26 item 3): may the BFF move this
    /// row's file into its derived container? Only a file that is VERIFIABLY the row's own: the item exists, was
    /// uploaded by the row's creator (the round-23 ITEM half), and sits in a container of this environment — a business
    /// unit's, the archive, or the own container of a secure record the document hangs off. A forged pointer to another
    /// person's item is therefore never copied into the document's container (it is reported for an administrator).
    /// </summary>
    internal async Task<bool> IsRelocationSourceVerifiedAsync(
        Guid documentId, string? pointerDriveId, string? pointerItemId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(pointerDriveId) || string.IsNullOrWhiteSpace(pointerItemId)
            || _speFiles is null)
        {
            return false;
        }

        var drive = pointerDriveId.Trim();
        try
        {
            var row = await _entityService.RetrieveAsync(
                "sprk_document", documentId, [CreatedByColumn, OwningBusinessUnitColumn], ct).ConfigureAwait(false);
            var creator = row is null ? null : await _speFiles.GetItemCreatorAsync(drive, pointerItemId.Trim(), ct).ConfigureAwait(false);
            if (row is null || creator is null
                || !await ItemWasCreatedByTheRowsCreatorAsync(documentId, row, creator, ct).ConfigureAwait(false))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_archiveContainerId) && IsSameContainer(_archiveContainerId, drive))
            {
                return true;
            }

            if ((await BusinessUnitsClaimingAsync(drive, ct).ConfigureAwait(false)).Count > 0)
            {
                return true;
            }

            var secureOwner = await ResolveOwningRecordAsync(drive, ct).ConfigureAwait(false);
            return secureOwner is not null
                   && await DocumentHangsOffAsync(documentId, secureOwner, drive, depth: 0, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[DOCUMENT-CONTAINER] The file of document {DocumentId} could not be verified for relocation.", documentId);
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The ITEM half (round 23)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

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
        if (await ReadRowCreatorAsync(documentId, row, ct).ConfigureAwait(false) is not { } rowCreator)
        {
            return false;
        }

        if (Guid.TryParse(creator.UserObjectId, out var itemUser) && itemUser != Guid.Empty)
        {
            // Uploaded BY A PERSON: it must be the row's person.
            return rowCreator.PersonObjectId is { } person && person == itemUser;
        }

        // Uploaded app-only: only the BFF's own rows, and only by the BFF identity.
        return rowCreator.CreatedByTheBff
               && Guid.TryParse(creator.ApplicationId, out var itemApplication)
               && _bffApplicationIds.Contains(itemApplication);
    }

    /// <summary>
    /// The Entra object id of the PERSON who created this document row — <c>createdby</c> when that is a person, else the
    /// person recorded in <see cref="CreatedByPersonColumn"/> — or <see langword="null"/> when there is none or it cannot
    /// be read. <paramref name="row"/> must carry <c>createdby</c>.
    /// </summary>
    /// <remarks>
    /// The ONE definition of "the row's creator" (owner round 23 item 1): the ITEM half of the pointer check and the
    /// pointer attach (<c>DocumentContainerRelocator.AttachFileAsync</c>, task 166 f1) both ask it, so a row whose file
    /// the check would accept is a row whose creator may attach that file, and no other.
    /// </remarks>
    internal async Task<Guid?> ReadDocumentCreatorObjectIdAsync(Guid documentId, Entity row, CancellationToken ct = default)
        => (await ReadRowCreatorAsync(documentId, row, ct).ConfigureAwait(false))?.PersonObjectId;

    /// <summary>
    /// Who created the row: the person (see <see cref="ReadDocumentCreatorObjectIdAsync"/>), and whether the row's
    /// <c>createdby</c> is the BFF identity. <see langword="null"/> = no readable <c>createdby</c>.
    /// </summary>
    private async Task<RowCreator?> ReadRowCreatorAsync(Guid documentId, Entity row, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(CreatedByColumn) is not { Id: var createdById } || createdById == Guid.Empty)
        {
            return null;
        }

        var createdBy = await _entityService.RetrieveAsync(
            SystemUserEntity, createdById, [AzureAdObjectIdColumn, ApplicationIdColumn], ct).ConfigureAwait(false);
        if (createdBy is null)
        {
            return null;
        }

        var createdByApplication = createdBy.GetAttributeValue<Guid>(ApplicationIdColumn);
        var rowCreatedByPerson = createdByApplication == Guid.Empty;
        var personObjectId = rowCreatedByPerson
            ? NonEmpty(createdBy.GetAttributeValue<Guid>(AzureAdObjectIdColumn))
            : await ReadCreatedByPersonObjectIdAsync(documentId, ct).ConfigureAwait(false);

        return new RowCreator(personObjectId, !rowCreatedByPerson && _bffApplicationIds.Contains(createdByApplication));
    }

    private readonly record struct RowCreator(Guid? PersonObjectId, bool CreatedByTheBff);

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

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The CONTAINER half of the interim rule: the document owner's customer subtree
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

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

    /// <summary>The business units whose <c>sprk_containerid</c> is <paramref name="drive"/> (memoized per scope).</summary>
    private async Task<IReadOnlyList<Guid>> BusinessUnitsClaimingAsync(string drive, CancellationToken ct)
    {
        var read = _claimantsMemo.GetOrAdd(drive, d => QueryBusinessUnitsClaimingAsync(d, ct));
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch
        {
            // Never remember a failure: the next question in this scope asks Dataverse again.
            _claimantsMemo.TryRemove(new KeyValuePair<string, Task<IReadOnlyList<Guid>>>(drive, read));
            throw;
        }
    }

    private async Task<IReadOnlyList<Guid>> QueryBusinessUnitsClaimingAsync(string drive, CancellationToken ct)
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

    /// <summary>Every business unit, child → parent (the root's parent is null). One read per scope.</summary>
    private async Task<IReadOnlyDictionary<Guid, Guid?>> LoadBusinessUnitHierarchyAsync(CancellationToken ct)
    {
        var read = _hierarchyMemo ??= QueryBusinessUnitHierarchyAsync(ct);
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch
        {
            // Never remember a failure (see BusinessUnitsClaimingAsync).
            _ = Interlocked.CompareExchange(ref _hierarchyMemo, null, read);
            throw;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, Guid?>> QueryBusinessUnitHierarchyAsync(CancellationToken ct)
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
    /// under the root — and every unit beneath it. For a ROOT-owned row it is the root unit ALONE (owner round 25 item 6,
    /// task 166 f1): under Model 1 the customers are the units beneath the root, so the root's own documents may use only
    /// the container the root itself stamps, never a customer's. Null — refuse — when the unit is unknown, its chain is
    /// broken, or the hierarchy has a cycle.
    /// </summary>
    internal static IReadOnlyCollection<Guid>? CustomerSubtree(Guid ownerBusinessUnit, IReadOnlyDictionary<Guid, Guid?> hierarchy)
    {
        if (!hierarchy.TryGetValue(ownerBusinessUnit, out var ownerParent))
        {
            return null;
        }

        if (ownerParent is null)
        {
            // The owner IS the root: only the root's own container (round 25 item 6). Before f1 this returned the whole
            // environment ("the operator level"), which let a Write holder on a root-owned row re-point it at any
            // customer's container.
            return [ownerBusinessUnit];
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

    /// <summary>
    /// Is <paramref name="businessUnit"/> the unit <paramref name="subtreeRoot"/> or beneath it? One hierarchy read per
    /// scope (shared with the pointer check). Unknown unit, broken chain, cycle or a read fault → <see langword="false"/>
    /// (fail closed). Used by the reporting module's allowed-workspace check for a workspace bound to one customer
    /// (task 166 f1, owner round 25 item 6).
    /// </summary>
    internal async Task<bool> IsBusinessUnitInSubtreeAsync(Guid businessUnit, Guid subtreeRoot, CancellationToken ct = default)
    {
        if (businessUnit == Guid.Empty || subtreeRoot == Guid.Empty)
        {
            return false;
        }

        try
        {
            var hierarchy = await LoadBusinessUnitHierarchyAsync(ct).ConfigureAwait(false);
            var visited = new HashSet<Guid>();
            Guid? current = businessUnit;
            while (current is { } unit)
            {
                if (unit == subtreeRoot)
                {
                    return true;
                }

                if (!visited.Add(unit) || !hierarchy.TryGetValue(unit, out var parent))
                {
                    return false;
                }

                current = parent;
            }

            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[BUSINESS-UNIT] Whether {Unit} is beneath {Root} could not be read; answering no (fail closed).",
                businessUnit, subtreeRoot);
            return false;
        }
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

/// <summary>
/// Where a document's file BELONGS (<see cref="RecordContainerResolver.DeriveDocumentContainersAsync"/>; task 166 f1).
/// </summary>
/// <param name="Decided">A container could be derived. When false the strict rule refuses and the migration reports it.</param>
/// <param name="AllowedContainers">Every container the file may live in: exactly one when <paramref name="IsSecure"/>.</param>
/// <param name="PrimaryContainer">The one a misplaced file is moved into.</param>
/// <param name="IsSecure">The derivation is a secure record's own container.</param>
/// <param name="Reason">Why — for logs and the migration report.</param>
public sealed record DocumentContainerDerivation(
    bool Decided,
    IReadOnlyList<string> AllowedContainers,
    string? PrimaryContainer,
    bool IsSecure,
    string Reason)
{
    /// <summary>Not decided, with the reason.</summary>
    public static DocumentContainerDerivation Undecided(string reason) => new(false, [], null, false, reason);

    /// <summary>Is <paramref name="drive"/> one of the allowed containers? Never true when not decided.</summary>
    public bool Allows(string? drive)
        => Decided && AllowedContainers.Any(c => RecordContainerResolver.IsSameContainerId(c, drive));
}
