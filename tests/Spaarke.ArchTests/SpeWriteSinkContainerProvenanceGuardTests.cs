using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 083 §6 — the forcing function for SPE write-sink container provenance:
/// a BFF call site that writes, replaces or deletes content INSIDE a SharePoint Embedded container fails
/// the build unless its container's ORIGIN is declared, classified, and — when the client names it —
/// owned by a task.
///
/// <para><b>Why this exists, and why it is shaped as an inversion.</b> The sibling guard
/// <see cref="RouteAuthorizationGuardTests"/> governs a HAND-MAINTAINED census of twelve endpoint files. It
/// has found four real holes, which is a good record — but it can only find holes in files somebody already
/// thought to list. On 2026-08-28 a manual sweep found TWO LIVE instances of the container-selection defect
/// class in files that census does not contain: the Office save path
/// (<c>Services/Office/OfficeStorageUploader.cs</c>, reached from <c>Api/Office/OfficeEndpoints.cs</c>) and
/// the SPE-admin item surface (<c>Api/SpeAdmin/ContainerItemEndpoints.cs</c>). <b>The census's
/// incompleteness was the defect.</b> So this rule is inverted: it scans EVERY <c>.cs</c> file under the BFF
/// and requires that every sink site it finds be declared. An undeclared site fails the build, naming file,
/// line and sink. Incompleteness is no longer silent — it is a red build.</para>
///
/// <para><b>The defect class.</b> The client names a container or drive; the server writes bytes into it. SPE
/// permissions are <b>container-level</b>: access to a container confers access to everything in it, and
/// there is no per-file grant or deny to narrow that — <i>"you can't break inheritance on arbitrary files or
/// folders"</i>. So a secure record's content written into a shared container is readable by every member of
/// that container, and no later per-item permission narrows it. That is why the container decision is an
/// authorization decision and not a routing detail, and why the guard cares about the container's ORIGIN
/// rather than about whether some authorization mechanism is present.</para>
///
/// <para>🔴 <b>Corrected 2026-09-02 (owner).</b> This paragraph used to say such content "cannot be
/// retracted" and that "there is no repair". That overstates it and misdescribes the model: because
/// permissions attach to the CONTAINER, removing the file from the container DOES end the access. The real
/// hazard is exposure for as long as the file sits there, plus the fact that nobody finds out — a
/// misrouted write is silent. So the remedy exists but depends on noticing, which is exactly what this
/// guard supplies. Do not restore the "irreversible" framing; it invites the wrong fix (hunting for a
/// per-file ACL that does not exist) instead of the right one (move or delete the item, then fix the
/// provenance).</para>
///
/// <para>See
/// <c>Infrastructure/Dataverse/SecureContainerDecision.cs</c> for the rule itself (pure, pinned jointly with a
/// TypeScript half against <c>tests/fixtures/secure-container-decision-table.json</c>).</para>
///
/// <para><b>The settled model</b> (owner-decided 2026-08-27; not re-opened here):
/// <code>
/// secure record      -> its OWN sprk_containerid, or FAIL CLOSED (never a fallback)
/// everything else    -> the RECORD's owningbusinessunit -> businessunit.sprk_containerid
/// server-side ingest -> Communication:ArchiveContainerId (no owning record exists)
/// </code>
/// The container follows the RECORD, not the acting user and not the request.</para>
///
/// <para><b>What this rule does NOT claim.</b> It is a provenance census, not a proof of correctness. It can
/// tell you that a site's container comes from configuration; it cannot tell you the configured container is
/// the right one. It can tell you a site's container comes from a Dataverse row; it cannot tell you the caller
/// was authorized against that row. Those need behavioural tests
/// (<c>tests/integration/auth/UnifiedAccessControl/**</c>). What it CAN do — and what the manual sweep could
/// not — is guarantee that no such site exists which nobody has looked at.</para>
///
/// <para><b>Relationship to <see cref="RouteAuthorizationGuardTests"/>.</b> Complementary, not overlapping.
/// That guard asks "does this ROUTE reach a per-resource authorization decision?" and is keyed on routes.
/// This one asks "where did the CONTAINER come from?" and is keyed on sink call sites — which is why it sees
/// service-layer and background-worker sinks that no route census reaches (rows 20-25 of the inventory below
/// are in <c>Services/**</c> and <c>Workers/**</c>). Deliberately a separate file: nothing here edits that
/// one.</para>
/// </summary>
public class SpeWriteSinkContainerProvenanceGuardTests
{
    // =================================================================================================
    // THE SCOPE BOUNDARY — what counts as a sink
    // -------------------------------------------------------------------------------------------------
    // Written down deliberately, because a rule whose scope nobody can state will be waived.
    //
    // IN SCOPE — operations that CREATE, REPLACE or DELETE an item (bytes or folder) INSIDE an SPE
    //            container/drive. That is the set of operations whose blast radius is governed by the
    //            container's CONTAINER-LEVEL permission model (no per-file grant or deny narrows it).
    //
    // OUT OF SCOPE, and each for a stated reason:
    //   · container LIFECYCLE (CreateContainerAsync, PermanentDeleteContainerForConfigAsync) — creating or
    //     destroying the container itself does not place content into a container somebody else can read.
    //   · PERMISSIONS / membership (Containers[id].Permissions[...] in SpeContainerMembershipService,
    //     DemoExpirationService, DemoProvisioningService) — a genuine additive-permission surface, but a
    //     DIFFERENT one, governed by task 020 and ADR-034. Mixing them would produce one list nobody reads.
    //   · list-COLUMN schema (DeleteColumnAsync) and change-notification SUBSCRIPTIONS
    //     (DeleteSubscriptionAsync) — no bytes, no container content.
    //   · READS (DownloadContentAsUserAsync, ListContainerItems*, preview/thumbnail/version reads) — a read
    //     cannot create an unretractable disclosure. Reads are RouteAuthorizationGuardTests' subject.
    //
    // The vocabulary itself is pinned — see TheSpeWriteSinkVocabularyOfThePlumbingLayerIsPinned. That matters
    // because the sink-NAME list is itself a hand-maintained census, and it was ALREADY incomplete: the task
    // brief's own list of "known sink names" omitted UploadFileToContainerForConfigAsync, which is the live
    // upload sink behind POST /api/spe/containers/{id}/items/upload. Inverting the file census while leaving
    // the name census hand-maintained would just move the blind spot.
    // =================================================================================================

    // =================================================================================================
    // THE PLUMBING-LAYER EXCLUSION — decided, not assumed
    // -------------------------------------------------------------------------------------------------
    // DECISION: Infrastructure/Graph/** is EXCLUDED from the site scan.
    //
    // WHY. Every real sink routes through that layer (ADR-007: SpeFileStore is the sole SPE access path), so
    // including it would add ~20 sites that have no container decision of their own — SpeFileStore.cs:75 is
    // literally `=> _uploadManager.UploadSmallAsync(driveId, path, content, ct)`. Those entries would
    // dominate the list, every one of them would carry the same non-answer ("the caller chose"), and a list
    // in which most entries say nothing is a list nobody re-reads. The blast radius of that is precisely the
    // failure this guard exists to prevent.
    //
    // WHY THE EXCLUSION CANNOT HIDE A REAL CALLER-NAMED WRITE — this is the part that makes the decision
    // safe rather than convenient, and it is ENFORCED, not asserted in prose. See
    // ThePlumbingLayerExclusionIsStillJustified, which requires BOTH:
    //
    //   (1) NO ORIGINATION. No file in Infrastructure/Graph/** may contain a container/drive ORIGIN
    //       expression. Two tiers, because "reads configuration" and "reads a CONTAINER from configuration"
    //       are not the same claim:
    //         tier 1, unconditional — IOptions< / _options. / _speOptions., the three well-known container
    //                  config keys, sprk_containerid / sprk_graphdriveid column reads, and any HTTP
    //                  RouteValues / FromRoute / FromQuery / FromBody / Request.Query read.
    //         tier 2, conditional  — a generic configuration read fires only when its KEY names a container
    //                  or a drive, and fires as UNVERIFIABLE when the key is not a literal.
    //       The split was forced by this guard's own first run: a bare GetValue< in tier 1 fired on
    //       SpeAdmin:GraphClientCacheTtlMinutes and SpeAdmin:SearchRegion, which are a cache TTL and a Graph
    //       search region. Those are the sanctioned shape, so the CLAIM was narrowed to what it can support
    //       rather than the exclusion widened to make the build green — and that real false positive is now
    //       a permanent positive control (see Origination_PositiveControl_...).
    //       Verified empty as of 2026-08-28 across all five files including the 6k-line
    //       SpeAdminGraphService.cs. Every drive/container id in that layer arrives as a method parameter,
    //       which is what "plumbing" means operationally. The moment that stops being true the exclusion
    //       stops applying and this test fails.
    //
    //   (2) NO BYPASS. `graphClient.Drives[...]` appears NOWHERE outside Infrastructure/Graph/**. If some
    //       future service reaches around the facade straight to Graph, it is not covered by the excluded
    //       layer and the bypass rule fails. (This is also stronger than the mutation-only check the task
    //       asked for: any direct drive access outside the facade fails, read or write, because a facade
    //       bypass is itself the interesting event.)
    //
    // Together those two make the exclusion a THEOREM about the excluded code rather than a hope. Note the
    // exclusion is by DIRECTORY, not by file list: a new file added to Infrastructure/Graph/** is excluded
    // from the site scan but immediately subject to both guards above, so it cannot arrive un-inspected.
    // =================================================================================================

    private static readonly string BffRoot =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    /// <summary>The excluded plumbing layer, relative to <see cref="BffRoot"/>. See the decision above.</summary>
    private const string PlumbingLayer = "Infrastructure/Graph";

    // =================================================================================================
    // THE PROVENANCE CLASSIFICATIONS
    // =================================================================================================

    private enum Provenance
    {
        /// <summary>
        /// The container/drive comes from a Dataverse row the caller was authorized against — the sanctioned
        /// shape. This is what the settled model prescribes for anything with an owning record.
        /// </summary>
        ServerDerivedRecord,

        /// <summary>
        /// The container comes from configuration. Legitimate ONLY for server-side ingest or staging with no
        /// owning record; a config container for content that DOES have an owning record is the task-075
        /// defect (secure attachments landing in the shared archive).
        /// </summary>
        ServerDerivedConfig,

        /// <summary>
        /// The container comes from the ACTING USER's business unit, read server-side from Dataverse.
        /// Added 2026-09-03 by task 076, implementing the owner's 2026-08-28 resolution order for content
        /// that has no owning record at the moment the bytes move.
        ///
        /// <para><b>Why this is not <see cref="ServerDerivedRecord"/>.</b> That category means "a Dataverse
        /// row the caller was authorized against" — the row that OWNS the content. Here no such row exists
        /// yet; the container is derived from the caller's own user → business-unit chain. Filing these
        /// under ServerDerivedRecord would blur the exact distinction this guard exists to draw, and would
        /// hide the fact that the destination was chosen without an owning record to justify it.</para>
        ///
        /// <para><b>Why it is not <see cref="ClientSupplied"/> either.</b> The client cannot name the
        /// container — the route carries no container parameter. The correct VALUE being the user's BU
        /// container is a different claim from the CLIENT being allowed to send one, and that distinction
        /// is the whole basis of the owner's answer.</para>
        ///
        /// <para><b>Why it cannot become a loophole.</b> It is admissible ONLY where no record exists.
        /// Secure content can never arrive this way: secure records resolve through the record-keyed route
        /// and fail closed, and for a secure record the acting user's BU is provably the WRONG container —
        /// users sit in the Operations subtree while secure records are owned in <c>Secure Projects</c>.
        /// A site claiming this provenance from a route that also accepts a record id is misclassified.</para>
        ///
        /// <para>🔴 <b>Known residual, accepted and separately filed.</b> Content placed in a BU container
        /// this way and LATER associated to a secure record is already in the shared container, and SPE
        /// permissions are additive-only, so nothing retracts it. See
        /// <c>notes/finding-secure-transition-container-migration.md</c> — its own project by owner
        /// direction 2026-08-31. It is strictly smaller than the behaviour it replaces, where the CLIENT
        /// named the container.</para>
        /// </summary>
        ServerDerivedActingUser,

        /// <summary>
        /// The container/drive comes from client route, query or body. FAILS unless the entry carries an
        /// owning task — the analogue of RouteAuthorizationGuardTests' UNOWNED rule. These are a work list
        /// that shrinks to zero, never exemptions.
        /// </summary>
        ClientSupplied,

        /// <summary>
        /// The container comes from the client, and that is the CORRECT design because the route is an
        /// administrative surface whose function is to operate on a container the admin names. Added
        /// 2026-08-30 by task 091, by owner decision.
        ///
        /// <para><b>Why this is a category and not an exemption.</b> Our auth structure already treats
        /// SPE Admin as a distinct authorization plane rather than an unclassified special case: it has
        /// two independently-documented layers (<c>SpeAdminAuthorizationFilter</c> — the Spaarke admin
        /// app role; <c>SpeAdminTenantScopeFilter</c> — <c>configId</c> ownership), and its own deny-code
        /// namespace (<c>spe.admin.*</c>) separate from the record plane's <c>sdap.access.*</c>. There is
        /// no owning record to derive a container from, and record-less containers legitimately exist
        /// (task 078 confirmed every shared BU / archive container). Classifying these
        /// <see cref="ClientSupplied"/> would assert an unfixed record-plane defect about a correctly
        /// designed admin-plane decision — and would leave a work list that can never reach zero, which
        /// is how a work list stops being read.</para>
        ///
        /// <para><b>Why it cannot become a loophole.</b> It is shaped like ADR-028's enumerated
        /// credential exceptions (E-1/E-3) rather than like a blanket waiver: the membership is
        /// enumerated, the COUNT is pinned by
        /// <see cref="TheAdministrativeRoleScopedSetIsPinnedAndActuallyGroupGated"/>, and that same rule
        /// mechanically verifies the file's routes are group-relative — i.e. that they genuinely cannot
        /// resolve without the group that carries both filters. A site claiming this provenance from a
        /// file that self-registers absolute paths fails the build. Task 091 is the proof this matters:
        /// these three routes carried this exact shape while registered on the ROOT app, gated by
        /// nothing.</para>
        /// </summary>
        AdministrativeRoleScoped,

        /// <summary>
        /// No production callers. Must name the evidence. Expected to be DELETED, not classified forever —
        /// task 083's own constraint is "DELETE rather than convert where a path is dead".
        /// </summary>
        Dead,
    }

    /// <summary>
    /// One declared sink site.
    ///
    /// <para><b>Keyed on (File, Sink, Ordinal) and deliberately NOT on line number.</b> A line-keyed census
    /// fires on every unrelated edit above the sink, and a guard that cries wolf on formatting changes gets
    /// disabled. The ordinal is the 1-based index of that sink name's CALL sites within that file, in file
    /// order — stable under edits above, and it changes exactly when a sink is added, removed or reordered,
    /// which is when a human should look. Live line numbers are printed in every failure message.</para>
    /// </summary>
    private sealed record SinkSite(
        string File,
        string Sink,
        int Ordinal,
        Provenance Provenance,
        string OwningTask,
        string Origin,
        string Reason,
        bool DirectStampRead = false);

    // =================================================================================================
    // THE ALLOW-LIST
    // -------------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read this before adding, editing or deleting an entry.
    //
    //  1. YOU ARE HERE BECAUSE A SINK SITE IS UNDECLARED. Do not silence the guard by narrowing its scope
    //     (there is no per-file opt-out, on purpose) and do not delete an existing entry to make a count
    //     line up. Trace the site's container ORIGIN — read backwards from the sink to wherever the
    //     container/drive id is first produced — and add an entry.
    //
    //  2. CLASSIFY HONESTLY. The classification is about where the value CAME FROM, not about how safe it
    //     feels:
    //        ServerDerivedRecord  — read off a Dataverse row (ideally via RecordContainerResolver /
    //                               ResolveContainerForContentAsync). The sanctioned shape.
    //        ServerDerivedConfig  — read from IOptions / IConfiguration. Legitimate ONLY when there is no
    //                               owning record (server ingest, staging).
    //        ClientSupplied       — route / query / body, at ANY depth. A parameter threaded three call
    //                               frames down from a request DTO is still client-supplied; that is exactly
    //                               how the Office path (row 20) stayed invisible to a route census.
    //        Dead                 — zero production callers, WITH the evidence named.
    //
    //  3. ClientSupplied MUST name an owning task. "UNOWNED" is permitted only for a site this guard found
    //     that has not been assigned yet — and it is a defect report, not a waiver. Never re-classify a
    //     ClientSupplied site as something else to make a build green; that inverts the forcing function.
    //
    //  4. EVERY entry carries a substantive Reason (>= 80 chars) containing an ADR citation (ADR-nnn). An
    //     unexplained exemption is indistinguishable from an oversight six months later.
    //
    //  5. WHEN A SITE IS FIXED OR DELETED, DELETE ITS ENTRY. A listed site that no longer exists fails
    //     NoDeclaredSinkSiteIsStale — the list cannot outlive its cause. If a site was MOVED rather than
    //     deleted, add its new home before deleting the old entry, because this rule cannot tell those two
    //     cases apart.
    //
    // STATE AT THE TIME OF WRITING (2026-08-28). The suite is GREEN, and it is green only BECAUSE eleven
    // ClientSupplied entries carry owning tasks. That is the honest state of the codebase, not a passing
    // grade. Several of them (rows 20/24/25 Office, rows 6/7/8 SpeAdmin) are NEW — found by writing this
    // guard, absent from every prior sweep and from task 083's §2 table. (This paragraph said "nine" twice
    // on the day it was written, when there were twelve entries; see the COUNT CORRECTED note below.)
    //
    // DISCOVERED INVENTORY, refreshed 2026-08-28 by the folder-removal change. Lines are INFORMATIONAL
    // (see the keying note on SinkSite) and were verified by hand against the tree on that date:
    //
    //   Api/Ai/ChatDocumentEndpoints.cs:1160                      UploadSmallAsUserAsync              config
    //   Api/Ai/ChatWordExportEndpoints.cs:154                     UploadSmallAsUserAsync              config
    //   Api/DocumentsEndpoints.cs:65                              UploadSmallAsync                    CLIENT
    //   Api/DocumentsEndpoints.cs:122                             DeleteFileAsync                     CLIENT
    //   Api/OBOEndpoints.cs:72                                    UploadSmallAsUserAsync              CLIENT
    //     ^ ROUTE DELETED 2026-09-03 (task 076). Kept in this historical census because the census
    //       records what the ORIGINAL scan found; the live set is the AllowList below.
    //   Api/SpeAdmin/ContainerItemEndpoints.cs:633                CreateFolderForConfigAsync          CLIENT  <-- NEW
    //   Api/SpeAdmin/ContainerItemEndpoints.cs:924                DeleteDriveItemForConfigAsync       CLIENT  <-- NEW
    //   Api/SpeAdmin/ContainerItemEndpoints.cs:1067               UploadFileToContainerForConfigAsync CLIENT  <-- NEW
    //   Services/Ai/WorkingDocumentService.cs:172                 UploadSmallAsync                    record*
    //   Services/Communication/CommunicationService.cs:2066       UploadSmallAsync                    config
    //   Services/Communication/IncomingCommunicationProcessor.cs:921   UploadSmallAsync               record
    //   Services/Communication/IncomingCommunicationProcessor.cs:1093  UploadSmallAsync               record
    //   Services/Communication/MessageAttachmentMaterializer.cs        UploadSmallAsync               record
    //          (file was DELETED 2026-08-28 and RESTORED 2026-08-29 — see the entry in AllowList for why
    //           the "Dead" classification it was deleted under did not hold)
    //   Services/Compose/ComposeService.cs:442                    ReplaceFileContentAsUserAsync       CLIENT
    //   Services/Compose/ComposeService.cs:1448                   ReplaceFileContentAsUserAsync       record
    //   Services/Compose/ComposeService.cs:1484                   UploadSmallAsUserAsync              CLIENT
    //   Services/Compose/ComposeService.cs:1515                   ReplaceFileContentAsUserAsync       CLIENT
    //   Services/DocumentCheckoutService.cs:787                   DeleteFileAsync                     record
    //   Services/Email/EmailAttachmentProcessor.cs:232            UploadSmallAsync                    DEAD
    //   Services/Office/OfficeStorageUploader.cs:54               UploadSmallAsync                    CLIENT  <-- NEW
    //   Services/Office/OfficeStorageUploader.cs:86               DeleteFileAsync                     record
    //   Services/Workspace/MatterPreFillService.cs:334            UploadSmallAsUserAsync              config
    //   Services/Workspace/ProjectPreFillService.cs:304           UploadSmallAsUserAsync              config
    //   (Workers/Office/UploadFinalizationWorker.cs:646           — SINK DELETED 2026-08-28, was UNREACHABLE)
    //   Workers/Office/UploadFinalizationWorker.cs:1146           UploadSmallAsync                    CLIENT  <-- now ordinal 1
    //                                                                        (* reads the stamped column directly)
    //
    // CORRECTIONS TO THE 2026-08-28 MANUAL SWEEP, recorded here because the same errors will otherwise be
    // re-derived from the sweep note:
    //   · Api/SpeAdmin/ContainerItemEndpoints.cs — the sweep gave ":160->994" for the upload and ":127->888"
    //     for the delete. The ROUTE registrations at 160/127 are right; the SINKS are at 1067 and 924, and
    //     the upload sink's NAME is UploadFileToContainerForConfigAsync (the sweep named the private
    //     SpeAdminGraphService.UploadSmallFileAsync three frames further down).
    //   · The sweep MISSED Api/SpeAdmin/ContainerItemEndpoints.cs:633 CreateFolderForConfigAsync entirely —
    //     a third client-named write on the same surface (folder create, same {id} + configId provenance).
    //   · Api/Ai/ChatWordExportEndpoints.cs was reported as having ZERO callers. Its route IS mapped
    //     (Infrastructure/DI/EndpointMappingExtensions.cs:268), so the sink is reachable over HTTP; only its
    //     CLIENT usage is unattested, which source cannot decide either way. Classified live-config.
    //   · The chunked-upload facade methods (CreateUploadSessionAsUserAsync / UploadChunkAsUserAsync) are
    //     dead as the sweep said, but the comment at Infrastructure/Graph/UploadSessionManager.cs:103-104
    //     still asserts they are "LIVE via OBOEndpoints.cs:119/172" — stale since task 076 deleted those
    //     routes. Not fixed here (this task is guard-only); reported.
    // =================================================================================================

    private static readonly IReadOnlyList<SinkSite> AllowList = new[]
    {
        // ---------------------------------------------------------------------------------------------
        // ClientSupplied — the work list. TWO sites, all owned. Both are in Api/DocumentsEndpoints.cs.
        //
        // COUNT CORRECTED 2026-08-28, TWICE ON 2026-09-01, AND AGAIN AT THE 2026-09-03 MERGE. The header
        // said "nine" on the day the guard was written when the block held TWELVE, then "eleven" while the
        // block actually held EIGHT, then "seven" when a machine count said SIX — the prose count is
        // enforced by nothing (Rule A pins AllowList.Count against the DISCOVERED set, not against a
        // number in a comment), so it drifts EVERY time, including in the same edit that corrects it.
        //
        // ⚠️ THE 2026-09-03 MERGE IS THE SHARPEST INSTANCE YET, and it is why this paragraph is kept.
        // TWO branches each shrank this list, and each wrote its own count in this same header — so the
        // merge conflicted on the NUMBER while both bodies merged cleanly. Neither number was right
        // afterward:
        //   · compose-r8 wrote THREE  — true on its branch: #858 converted the Compose create-on-save
        //     mint, and the drive-provenance fix converted the three ComposeSaveStorageCoordinator
        //     replace sinks, all to ServerDerivedRecord.
        //   · unified-access-control-r2 wrote SIX — true on ITS branch: task 076 deleted the route behind
        //     `Api/OBOEndpoints.cs :: UploadSmallAsUserAsync #1`, so that sink left the list. There is no
        //     longer a ClientSupplied upload sink in OBOEndpoints.cs at all.
        // Both reductions are real and they compose. The merged answer is TWO, and it was obtained by
        // running the grep below — NOT by picking a side, and NOT by subtracting one prose number from
        // the other.
        //
        // Recount with `grep -c "^            Provenance.ClientSupplied,"` rather than by reading — that
        // is a machine count, and the five wrong numbers above are the argument for taking one.
        // ---------------------------------------------------------------------------------------------
        new SinkSite("Api/DocumentsEndpoints.cs", "UploadSmallAsync", 1,
            Provenance.ClientSupplied, "083 (row 4)",
            "route parameter {driveId}",
            "PUT /api/drives/{driveId}/upload writes app-only (MI) into whatever drive the caller names, "
            + "gated by the 'canwritefiles' POLICY only — and ResourceAccessHandler.ExtractResourceId "
            + "accepts driveId/containerId/documentId interchangeably, so the policy authorizes a DRIVE id "
            + "against DOCUMENT rights (ADR-003 authorization seams; ADR-008 requires the decision be an "
            + "endpoint filter on the right resource domain). App-only means no container ACL constrains it, "
            + "so this is a live hole rather than a latent bypass. Task 083 does this row FIRST."),

        new SinkSite("Api/DocumentsEndpoints.cs", "DeleteFileAsync", 1,
            Provenance.ClientSupplied, "083 (row 5)",
            "route parameters {driveId} + {itemId}",
            "DELETE /api/drives/{driveId}/items/{itemId} DESTROYS a caller-named drive item app-only (MI) "
            + "behind the same wrong-resource-domain 'canwritefiles' policy (ADR-003; ADR-008). A destroy is "
            + "the worst case for a wrong-resource-domain decision because, unlike a misplaced write, there "
            + "is not even a record left to audit afterwards."),

        // 🔴 DELETED 2026-09-03: the ClientSupplied entry for
        // `Api/OBOEndpoints.cs :: UploadSmallAsUserAsync #1`, which described
        // `PUT /api/obo/containers/{id}/files/{*path}` — a route that took its container straight
        // from a route parameter. The ROUTE was deleted (task 076, client cutover complete), so the
        // declaration went with it. It never became permanent, which was the standing requirement.
        //
        // ⚠️ ORDINALS IN THIS FILE SHIFTED AS A RESULT. Ordinals are assigned per (file, sink) in
        // FILE order, so removing the FIRST `UploadSmallAsUserAsync` call site in OBOEndpoints.cs
        // renumbered the two below it: #2 -> #1 and #3 -> #2. Deleting this entry without
        // renumbering those fails Rule A in both directions at once — two undeclared sites AND two
        // stale declarations, in one run.

        // ── The three SPE-Admin item writes ───────────────────────────────────────────────────────
        //
        // AdministrativeRoleScoped by OWNER DECISION, 2026-08-30 (task 091). The open question this
        // block previously carried — keep them ClientSupplied forever, or give the admin plane its own
        // provenance — was resolved in favour of the latter, on the grounds of consistency with the
        // auth structure we already have: SPE Admin is a distinct authorization plane with two named
        // layers and its own deny-code namespace, not an unclassified special case. See the enum doc.
        //
        // What that decision does NOT do: it does not weaken anything. Task 091 first moved all nine of
        // this file's routes onto the /api/spe group, so they inherit SpeAdminAuthorizationFilter
        // (admin app role) + SpeAdminTenantScopeFilter (configId ownership). The provenance is only
        // sanctioned BECAUSE the gate is now real, and
        // TheAdministrativeRoleScopedSetIsPinnedAndActuallyGroupGated re-proves the group-registration
        // mechanically on every build rather than trusting this comment.
        new SinkSite("Api/SpeAdmin/ContainerItemEndpoints.cs", "CreateFolderForConfigAsync", 1,
            Provenance.AdministrativeRoleScoped, "091 (gated + owner-classified)",
            "route parameter {id} (container) + query parameter configId",
            "POST /api/spe/containers/{id}/folders creates a folder in a caller-named container using the "
            + "container-type config's app-only credentials. FOUND BY THIS GUARD: absent from task 083's "
            + "§2 table AND from the 2026-08-28 manual sweep, which caught this file's upload and delete "
            + "but not its folder create. Lower blast radius than its siblings (a folder holds no bytes "
            + "yet) but identical provenance, and a folder is where the next write lands. Since task 091 "
            + "the route requires the Spaarke admin app role and a configId inside the caller's tenant "
            + "scope; there is still no per-RECORD decision, which is inherent to administering a "
            + "container rather than a document (ADR-003; ADR-008)."),

        new SinkSite("Api/SpeAdmin/ContainerItemEndpoints.cs", "DeleteDriveItemForConfigAsync", 1,
            Provenance.AdministrativeRoleScoped, "091 (gated + owner-classified)",
            "route parameter {id} (container) + query parameter configId",
            "DELETE /api/spe/containers/{id}/items/{itemId} destroys a caller-named item in a caller-named "
            + "container, app-only through the container-type config credentials, with no per-record "
            + "decision (ADR-003; ADR-008). Gated by task 091. ⚠️ ONE CLAIM HERE WAS TRUE WHEN WRITTEN AND "
            + "IS NOW FALSE — it read 'this whole file sits outside RouteAuthorizationGuardTests' "
            + "twelve-file census, which is why three live client-named writes lived here unremarked'. "
            + "Task 091 added the file to that census as Scope.GroupGated. The observation was the "
            + "load-bearing one: the census could only find holes in files someone had already listed, "
            + "and it governed twelve. Note also that the census undercounted this file's exposure — the "
            + "sweep found three WRITE sinks here, but the file has NINE routes, and the six read routes "
            + "(including file download and sharing-link minting) were invisible to a write-sink scan."),

        new SinkSite("Api/SpeAdmin/ContainerItemEndpoints.cs", "UploadFileToContainerForConfigAsync", 1,
            Provenance.AdministrativeRoleScoped, "091 (gated + owner-classified)",
            "route parameter {id} (container) + query parameter configId",
            "POST /api/spe/containers/{id}/items/upload writes bytes into a caller-named container app-only "
            + "(ADR-003; ADR-008; ADR-007 for the Graph path). Gated by task 091. Note the sink NAME: the "
            + "manual sweep and the task brief both named SpeAdminGraphService.UploadSmallFileAsync, which "
            + "is the private implementation three frames down; the actual decision site calls "
            + "UploadFileToContainerForConfigAsync, a sink name absent from every list this project has "
            + "kept. That omission is why the vocabulary is now pinned too."),

        // ── The APPLY-TEMPLATE replace MOVED, 2026-09-01 (issue #776) ──────────────────────────────
        //
        // It was Services/Compose/ComposeService.cs :: ReplaceFileContentAsUserAsync #1. #776 gave
        // apply-template an If-Match precondition asserting the version its merge was computed from, and
        // routed the write through ComposeSaveStorageCoordinator.ReplaceWithPreconditionAsync rather than
        // adding a second precondition idiom (root §11). No NEW sink line appeared: apply-template became
        // an additional CALLER of the coordinator's two existing sinks, so the coordinator's declared
        // ordinals are unchanged in count and only their caller list grows (noted on both entries below).
        //
        // ⚠️ SAME CASE RULE A WARNS ABOUT — "a rename looks identical to a deletion from here." The guard
        // reported 27 sites found against 28 declared with NOTHING undeclared, which is the signature of a
        // site that left without a replacement appearing. Deleting the entry on that evidence alone would
        // have been wrong twice over: it would have read as "fixed" when nothing about the provenance
        // changed, and it would have dropped an unresolved #858-family container decision out of the
        // census during a refactor whose subject was concurrency, not provenance.
        //
        // ✅ RESOLVED 2026-09-01 (drive provenance). The note that stood here read: "Provenance UNCHANGED
        // and still unfixed: apply-template's driveId is a ROUTE parameter … When that task lands it must
        // convert apply-template too, not just the save replace branch." It landed, and it did convert
        // both. ApplyTemplateAsync's route parameter is renamed `requestedDriveId` and is now a CLAIM: the
        // method resolves the drive recorded on the owning sprk_document row
        // (ComposeRecordResolution.TryResolveRecordedDriveIdAsync) and uses that for the metadata read, the
        // download AND the preconditioned write. Reading from one drive while writing to another was the
        // sharpest form of the divergence, and apply-template is the only read-merge-write on this list.
        //
        // The concern is therefore discharged on the coordinator entries below rather than deferred to them.

        // ── CONVERTED by issue #858 (2026-09-01) — was ClientSupplied ("SaveComposeDocumentRequest."
        // ── "ContainerId (client body)"), the entry this guard carried since the census was built. ──
        new SinkSite("Services/Compose/ComposeService.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "ResolveCreateOnSaveContainerAsync: session-bound matter (ownership-checked, then authorized "
            + "via CallerRecordAccessProbe + OperationAccessPolicy entity.associate_document) -> "
            + "RecordContainerResolver.ResolveForRecordAsync; no matter -> ResolveForActingUserAsync "
            + "(systemuser.azureactivedirectoryobjectid -> businessunit.sprk_containerid)",
            "Compose create-on-save Fork B mints the drive item in a SERVER-derived container (issue #858): "
            + "SaveComposeDocumentRequest.ContainerId is DELETED along with the wire field and the "
            + "'containerId is required' 400 guard, so the request cannot name a container at all "
            + "(ADR-049; ADR-003). The record identity the container derives from comes from SERVER-side "
            + "session state and is authorized before use — #858's originally-proposed fix (thread "
            + "(entity, recordId) onto the request) was rejected as relocating the defect, since a "
            + "caller-named matter is the same primitive one hop earlier. Unresolvable container -> honest "
            + "container-step failure (never a write); denial / unsupported host / unattributable caller "
            + "-> typed 403/409. The acting-user leg is record-PLANE derivation (the caller's own "
            + "systemuser row), filed ServerDerivedRecord like OBOEndpoints ordinal 1 rather than "
            + "ServerDerivedConfig, because nothing about it is configuration."),

        // ── The ordinary Compose save replace MOVED, 2026-08-30 ────────────────────────────────────
        //
        // It was Services/Compose/ComposeService.cs :: ReplaceFileContentAsUserAsync #3. PR #806 merged
        // to master and extracted it into ComposeSaveStorageCoordinator, where FR-S02's If-Match
        // precondition split it into TWO sinks: a blind PUT when there is no resolved version to assert
        // against, and a preconditioned PUT when there is.
        //
        // ⚠️ THIS IS THE CASE RULE A'S OWN MESSAGE WARNS ABOUT — "a rename looks identical to a
        // deletion from here". The guard reported the old entry as stale AND the two new sites as
        // undeclared, in the same run; had it only done the former, the unresolved #858 container
        // decision would have quietly left the census during a refactor nobody thought of as touching
        // container provenance. Both new sites are declared BEFORE the old entry was deleted, which is
        // the order the maintenance procedure requires.
        //
        // ── CONVERTED 2026-09-01 (drive provenance) — all three were ClientSupplied, owned by #858 ──
        //
        // The origin these entries recorded — "driveId parameter <- ComposeService.cs (save replace
        // branch) <- request.DriveId (client body)" — is no longer true. `SaveAsync` now resolves the
        // drive RECORDED on the owning sprk_document row ONCE, at the top of the method, and folds it back
        // onto the request (`request = request with { DriveId = … }`), so every consumer on the replace
        // path — baseline re-fetch, pre-write metadata read + PDF guard, stale-base re-anchor download,
        // and all three sinks below — addresses the record's drive. Same for apply-template, the trio's
        // other caller (see the note above it).
        //
        // ⚠️ READ THIS BEFORE TREATING IT AS AN ACCESS-CONTROL FIX, because the surrounding entries in this
        // list ARE access-control findings and the shapes look identical. Compose's writes are OBO: SPE
        // authorizes them as the acting user, so a caller could never reach a drive their own token did not
        // already permit — the old entries said as much ("the OBO leg is what limits blast radius here").
        // What was actually broken is PROVENANCE: the row said the document lived at drive X while the
        // bytes went to drive Y, so the record and the storage could disagree and nothing noticed. That is
        // an audit-trail defect, and it is the reason this converts to ServerDerivedRecord rather than
        // being deleted as a non-finding.
        //
        // THE FALLBACK IS DECLARED, NOT HIDDEN. When the row carries no `sprk_graphdriveid` the caller's
        // value is still used, logged at Debug. Legacy rows predating the full-SPE-pointer stamp exist —
        // PromoteIfEphemeralAsync documents that such a row makes downstream readers 409 "No file is
        // attached" — so a hard fail-closed would break saves on real documents to close a hole OBO
        // already closes. An attacker cannot make a row's drive id DISAPPEAR, so the fallback covers legacy
        // data, not an attack path. A divergence between the two values logs at Warning: that divergence is
        // the signal the fix exists to produce.
        new SinkSite("Services/Compose/ComposeSaveStorageCoordinator.cs", "ReplaceFileContentAsUserAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "driveId parameter <- ComposeService.SaveAsync/ApplyTemplateAsync <- ResolveAuthoritativeDriveIdAsync "
            + "<- ComposeRecordResolution.TryResolveRecordedDriveIdAsync (sprk_graphdriveid on the sprk_document "
            + "row keyed by sprk_graphitemid); caller-supplied value only when the row records no drive",
            "The blind-PUT branch of the ordinary Compose save: no resolved version to precondition on "
            + "(a drive-less path), so it replaces content with no If-Match (ADR-049; ADR-003). The DRIVE, "
            + "however, is now the one the authorized record itself records, so a missing precondition is a "
            + "concurrency exposure and not also a provenance one. Was ComposeService #3 until PR #806 "
            + "extracted it, and ClientSupplied under #858 until the drive-provenance fix converted it."),

        new SinkSite("Services/Compose/ComposeSaveStorageCoordinator.cs", "ReplaceFileContentAsUserAsync", 2,
            Provenance.ServerDerivedRecord, "",
            "driveId parameter <- ComposeService.SaveAsync/ApplyTemplateAsync <- ResolveAuthoritativeDriveIdAsync "
            + "<- ComposeRecordResolution.TryResolveRecordedDriveIdAsync (sprk_graphdriveid on the sprk_document "
            + "row keyed by sprk_graphitemid); caller-supplied value only when the row records no drive",
            "The preconditioned branch of the same save (FR-S02 If-Match, added by PR #806). Its old entry "
            + "made a point worth keeping now that it reads the other way round: an ETag check protects "
            + "against a lost UPDATE and says nothing about WHERE the update lands, so the precondition was "
            + "never evidence about drive provenance (ADR-049; ADR-003). The two concerns are now covered by "
            + "two different mechanisms — If-Match for the version, the record for the drive — rather than "
            + "one of them being mistaken for both."),

        new SinkSite("Services/Compose/ComposeSaveStorageCoordinator.cs", "ReplaceFileContentAsUserAsync", 3,
            Provenance.ServerDerivedRecord, "",
            "driveId parameter <- ComposeService.SaveAsync/ApplyTemplateAsync <- ResolveAuthoritativeDriveIdAsync "
            + "<- ComposeRecordResolution.TryResolveRecordedDriveIdAsync (sprk_graphdriveid on the sprk_document "
            + "row keyed by sprk_graphitemid); caller-supplied value only when the row records no drive",
            "The single rebase RETRY inside catch(EtagPreconditionFailedException): re-reads the live "
            + "version and re-issues the replace against the same drive the record names (ADR-049; ADR-003). "
            + "Declared separately because it is a distinct write that a reader skimming the method will "
            + "miss — it sits inside a catch block, three sites deep, and I missed it myself on first read, "
            + "declaring only ordinals 1 and 2 until Rule A named the third. That is the argument for "
            + "keying this list per CALL SITE rather than per method in one line, and it is why the "
            + "provenance conversion had to reach all three ordinals rather than the two obvious ones."),

        new SinkSite("Services/Office/OfficeStorageUploader.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "085 (CLOSED 2026-08-30)",
            "containerId parameter <- OfficeService.ResolveContainerAsync <- RecordContainerResolver "
            + "keyed on SaveRequest.TargetEntity (the record AddEntityAccessFilter authorized), "
            + "falling back to EmailProcessing:DefaultContainerId only when no target entity is named",
            "CLOSED BY TASK 085. SaveRequest.ContainerId was DELETED from the contract and the container is "
            + "now derived from the SAME record the caller was authorized against, through task 076's "
            + "RecordContainerResolver — so the authorization key and the write destination are one value "
            + "and no code path can let them disagree. A secure record's own container wins, and a secure "
            + "record with no container of its own is REFUSED rather than falling back to the shared "
            + "default (ADR-003 fail-closed). The historical finding is preserved because it is the reason "
            + "this guard is keyed on sinks rather than routes: the client value crossed THREE frames "
            + "(Api/Office/OfficeEndpoints -> OfficeService -> OfficeStorageUploader) before reaching the "
            + "sink, so no route-level census could see it. And the sharpest detail — "
            + "POST /api/office/save DID carry .AddEntityAccessFilter(), authorizing the caller against "
            + "SaveRequest.TargetEntity while the container came from SaveRequest.ContainerId, a DIFFERENT "
            + "client-supplied field on the same body. Authorized against one thing, writing into another: "
            + "option (B), which task 083 rejected. That is why an authorization filter is not evidence "
            + "about container provenance — the two guards ask different questions.\n\n"
            + "ORIGINAL FINDING TEXT (retained for the record): The Office add-in save path writes "
            + "app-only (MI) into a container the client names in the save "
            + "request body (ADR-003; ADR-007). THE HEADLINE FINDING of the 2026-08-28 sweep, and the reason "
            + "this guard is keyed on sinks rather than routes: the client-supplied value crosses THREE "
            + "frames (Api/Office/OfficeEndpoints -> OfficeService -> OfficeStorageUploader) before reaching "
            + "the sink, so no route-level census could see it and no reader of any single file would "
            + "notice. App-only, so no container ACL constrains it. SHARPEST DETAIL, worth reading twice: "
            + "POST /api/office/save DOES carry .AddEntityAccessFilter(), which authorizes the caller "
            + "against SaveRequest.TargetEntity — but the container comes from SaveRequest.ContainerId, a "
            + "DIFFERENT client-supplied field on the same body. Authorized against one thing, writing into "
            + "another. That is exactly the option (B) task 083 rejected ('a route accepting BOTH a record "
            + "and a container is a failed implementation'), and it is why presence of an authorization "
            + "filter is not evidence about container provenance — the two guards ask different questions."),

        // ⚠️ ORDINAL SHIFT, 2026-08-28 — READ BEFORE EDITING. This file used to declare TWO UploadSmallAsync
        // sites: ordinal 1 was the worker's own private UploadToSpeAsync (the "traditional upload flow"),
        // ordinal 2 was the email-attachment upload. The traditional-flow branch was DELETED as unreachable
        // (OfficeJobQueue is the payload's sole producer and always sets TempFileLocation = "spe://…", so the
        // branch guarding it could never be false), which took its sink with it. The attachment site is
        // therefore now ordinal 1 — the entry below is the SURVIVING LIVE site, not the old ordinal 1.
        //
        // This is exactly the case the MAINTENANCE PROCEDURE's rule 5 warns about: a deletion and a move are
        // indistinguishable from here, so the reclassification is written down rather than left to be
        // re-derived. The prior ordinal-2 entry's substance is preserved below.
        new SinkSite("Workers/Office/UploadFinalizationWorker.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "085 (CLOSED 2026-08-30)",
            "the drive resolved from payload.ContainerId — which task 085 now seeds with the "
            + "SERVER-derived container (RecordContainerResolver keyed on the authorized record), "
            + "not with SaveRequest.ContainerId — reused for attachments",
            "CLOSED BY TASK 085, at the payload rather than at this site. The container is derived BEFORE "
            + "the ProcessingJob payload is serialized, so what this worker reads back is the "
            + "server-derived value; previously the payload carried the client's own choice, which meant a "
            + "client-supplied container OUTLIVED the request inside a Dataverse row. That laundering "
            + "through a queue message is the shape most likely to be mistaken for server-derived by a "
            + "reader who starts at the worker — which is precisely why the fix had to reach the payload "
            + "and not just the synchronous upload. As of 2026-08-28 the attachment lands FLAT in the "
            + "container root with the parent document id folded into the filename; that change removed "
            + "implicit folder creation and did NOT change provenance — this one did.\n\n"
            + "ORIGINAL FINDING TEXT (retained for the record): Email attachments are uploaded into the "
            + "SAME drive that the client-named container resolved to "
            + "(ADR-003; ADR-036 background jobs). This is the async half of the Office save path: the "
            + "client-supplied container is laundered through a queue message, which is the shape most "
            + "likely to be mistaken for server-derived by a reader who starts at the worker. A fix that "
            + "threads (entity, recordId) through the job payload must reach this site AND the synchronous "
            + "OfficeStorageUploader one. As of 2026-08-28 the attachment now lands FLAT in the container "
            + "root with the parent document id folded into the filename — that change removed the implicit "
            + "folder creation, and it did NOT change the container provenance, which is why this entry "
            + "stays ClientSupplied and owned."),

        // ---------------------------------------------------------------------------------------------
        // ServerDerivedRecord — the sanctioned shape. These are the sites the guard must NOT push away.
        // ---------------------------------------------------------------------------------------------
        new SinkSite("Api/ExternalAccess/ExternalProjectDataEndpoints.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "decision.ContainerId from RecordContainerResolver.ResolveForRecordAsync(\"sprk_project\", id)",
            "The EXTERNAL upload route (R15). Highest-stakes sink in this list: the caller is an external "
            + "CIAM contact, not a Dataverse principal, so nothing downstream would stop a client-named "
            + "container. The request carries the file and NOTHING about storage — the container is resolved "
            + "from the same project id the participation gate authorized, so the authorization key and the "
            + "storage target cannot disagree (#858). Unresolved and FailClosed both return 422 rather than "
            + "falling back to a shared container, and the upload passes ConflictBehavior.Fail so an "
            + "external participant can never overwrite an existing document with a same-named file "
            + "(ADR-003 fail-closed; ADR-007)."),

        new SinkSite("Services/Communication/IncomingCommunicationProcessor.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "ResolveContainerForContentAsync(scope, communicationId, \"attachment processing\", ct)",
            "THE reference implementation for inbound ingest, and the shape every ClientSupplied row above "
            + "should converge on: task 075 replaced a direct _options.ArchiveContainerId read with a "
            + "resolver that routes through SecureContainerDecision, so a secure matter's attachments go to "
            + "that matter's own container or nowhere at all (ADR-003 fail-closed; ADR-045 communication "
            + "architecture). A blank resolution RETURNS rather than falling back."),

        new SinkSite("Services/Communication/IncomingCommunicationProcessor.cs", "UploadSmallAsync", 2,
            Provenance.ServerDerivedRecord, "",
            "ResolveContainerForContentAsync(scope, communicationId, \".eml archival\", ct)",
            "The .eml archival leg of the same processor, resolved the same way. A .eml is the FULL message "
            + "body, so for a secure matter it is at least as disclosing as any attachment — it was "
            + "previously archived to the shared container unconditionally, which is the defect task 075 "
            + "closed (ADR-003; ADR-045)."),

        // ORDINAL CHANGED 2 -> 1 on 2026-09-01 (#776), and this is the entry that proves the ordinal
        // keying earns its keep. Nothing about this SITE changed — same call, same method, same
        // Dataverse-derived drive. It renumbered only because #776 removed the apply-template sink that
        // used to occupy #1 EARLIER IN THE SAME FILE. The guard caught it as a matched pair (this entry
        // stale + the dedup site undeclared) in one run, which is what distinguished a renumber from a
        // deletion; had it reported only the stale half, the honest move would have looked like a fix.
        new SinkSite("Services/Compose/ComposeService.cs", "ReplaceFileContentAsUserAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "match.DriveId / match.SpeId from the Dataverse transient-key lookup",
            "The create-on-save dedup path replaces content in the drive item recorded on the row the "
            + "transient key resolved to, so the drive comes from Dataverse and not from the request "
            + "(ADR-049; ADR-003). Was ordinal #2 until #776 removed the apply-template sink above it. "
            + "Pinned individually so a future edit cannot quietly swap which ordinal is which — the "
            + "renumber this entry just went through is exactly that risk materialising benignly."),

        new SinkSite("Services/DocumentCheckoutService.cs", "DeleteFileAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "document.DriveId / document.ItemId off the authorized sprk_document row",
            "The sanctioned delete path: the SPE pointer is read off the document row the caller was already "
            + "authorized against, so the authorization key and the storage target are the same value "
            + "(ADR-003; ADR-007). Contrast Api/DocumentsEndpoints.cs DeleteFileAsync, which takes both from "
            + "the route — same sink, opposite provenance."),

        new SinkSite("Services/Office/OfficeStorageUploader.cs", "DeleteFileAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "the (driveId, itemId) THIS request just uploaded, returned by the upload call",
            "Gate-after-write cleanup of a byte-identical duplicate (email-communication-intelligence-r2 "
            + "FR-C3): deletes only the transient blob this same request created, never the canonical item, "
            + "and is best-effort/non-fatal (ADR-003; ADR-007). Server-derived even though it sits two lines "
            + "from a ClientSupplied sink in the same file, which is the clearest argument in this list for "
            + "per-site rather than per-file classification."),

        // ── ADDED 2026-08-28, and NOT by the change that brought me here. ────────────────────────────
        // These two sites were UNDECLARED on work/unified-access-control-r2, so Rule A was already RED
        // before the folder-removal change touched anything: task 076 added the record-keyed upload pair
        // (PUT /api/obo/records/{entity}/{recordId:guid}/files/{*path} and its upload-session sibling)
        // without adding declarations. Verified pre-existing by stashing the folder-removal work and
        // re-running the guard on a clean tree: same two failures, same 1-failed/8-passed.
        //
        // Declared rather than left red for two reasons. (1) Rule A's non-vacuity half —
        // discovered.Count == AllowList.Count — sits BEHIND the undeclared-sites assertion, so while
        // these were red the count check never executed and NO change to this allow-list could be
        // verified. A guard whose second half is unreachable is not a guard. (2) A red baseline makes a
        // newly-broken declaration indistinguishable from the old break in CI.
        //
        // Both are the SANCTIONED shape, traced not assumed — hence ServerDerivedRecord and no owning
        // task. This is a declaration gap being closed, not a hole being waived.
        new SinkSite("Api/OBOEndpoints.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "RecordContainerResolver.ResolveForRecordAsync(entityLogicalName, recordId) -> decision.ContainerId",
            "The task-076 record-keyed upload route: the container is derived from the RECORD named in the "
            + "route, and the code comment at the resolution site is explicit that no fallback may be "
            + "passed because \"the caller regains the ability to choose\" if one is (ADR-003 fail-closed; "
            + "ADR-007 SpeFileStore). An Unresolved decision returns 409 rather than defaulting, and a "
            + "secure record with no container of its own throws secure_record_container_missing. Note "
            + "what is and is not client-supplied here: {*path} IS the caller's, but this guard classifies "
            + "the CONTAINER's origin, and the container is the record's. This was ordinal 2 until "
            + "2026-09-03, when the container-KEYED route that held ordinal 1 was deleted; there is no "
            + "longer any ClientSupplied upload sink in this file."),

        new SinkSite("Api/OBOEndpoints.cs", "UploadSmallAsUserAsync", 2,
            Provenance.ServerDerivedActingUser, "",
            "RecordContainerResolver.ResolveForActingUserAsync(callerOid) -> decision.ContainerId",
            "The task-076 record-LESS upload route (PUT /api/obo/me/files/{*path}), for content with no "
            + "owning record at the moment the bytes move: EmailComposer local attachments, the Analysis "
            + "wizard's standalone document, and DocumentUploadWizard 'skip associate'. The route carries "
            + "NO container parameter, so the caller cannot name a destination — the container is derived "
            + "server-side from the acting user's business unit, per the owner's 2026-08-28 resolution "
            + "order (ADR-003 fail-closed; ADR-007 SpeFileStore). A caller who cannot be resolved to a "
            + "Dataverse principal gets a typed 403 (acting_user_not_resolvable), and a business unit with "
            + "no sprk_containerid returns 409 — neither falls back to a shared container. Secure content "
            + "never reaches this sink: secure records go through ordinal 1 (the record-keyed route) and "
            + "fail closed there. This was ordinal 3 until 2026-09-03; providing this route is what made "
            + "the container-KEYED legacy route deletable, and it was deleted the same day."),

        new SinkSite("Api/OBOEndpoints.cs", "CreateUploadSessionAsUserAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "RecordContainerResolver.ResolveForRecordAsync(entityLogicalName, recordId) -> decision.ContainerId",
            "The >=4 MiB sibling of the record-keyed upload route, resolving the container identically — "
            + "the source calls it \"identical resolution to the small route, deliberately: one contract, "
            + "two sizes\" (ADR-003; ADR-007). Listed separately because it is a distinct sink NAME whose "
            + "blast radius differs: opening the upload session is the act that reserves the destination "
            + "item, and unlike the simple PUT this path DOES carry an @microsoft.graph.conflictBehavior "
            + "(caller-selectable fail/replace/rename), so a 'replace' here is an explicit caller choice "
            + "rather than the silent overwrite the path-keyed PUT performs."),

        new SinkSite("Services/Ai/WorkingDocumentService.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "the matter's stamped sprk_containerid, read directly from the sprk_matter row",
            "Analysis working documents go to the matter's own container — server-derived, so not a "
            + "client-named write (ADR-003; ADR-013 AI boundary; ADR-007). FLAGGED, and pinned separately by "
            + "TheSetOfSitesReadingAStampedContainerColumnDirectlyIsPinned: it reads sprk_containerid off "
            + "the row instead of going through RecordContainerResolver, and SecureContainerDecision's own "
            + "documentation states that stale stamps demonstrably exist because the creation wizard's "
            + "business-unit cascade writes that column. For a SECURE matter the stamp is the right answer; "
            + "for a non-secure one a stale stamp silently redirects content. Not a hole, but the one "
            + "server-derived site whose correctness depends on data hygiene rather than on a resolver.",
            DirectStampRead: true),

        // ---------------------------------------------------------------------------------------------
        // ServerDerivedConfig — legitimate only where there is no owning record.
        // ---------------------------------------------------------------------------------------------
        new SinkSite("Services/Communication/CommunicationService.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedConfig, "",
            "_options.ArchiveContainerId (Communication:ArchiveContainerId)",
            "The sanctioned server-ingest case named explicitly by the settled model: outbound .eml archival "
            + "has no owning record at the moment of the write, so Communication:ArchiveContainerId is the "
            + "correct source and a missing value THROWS rather than defaulting (ADR-003; ADR-045). This is "
            + "the one config read the model blesses; the others in this section are staging, not archive."),

        new SinkSite("Services/Workspace/MatterPreFillService.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedConfig, "",
            "_speOptions.StagingContainerId (SharePointEmbedded:StagingContainerId)",
            "AI pre-fill uploads a candidate document to the STAGING container for text extraction before "
            + "any matter exists — there is no owning record yet, which is the condition that makes a config "
            + "container legitimate (ADR-003; ADR-007). Runs under OBO, so the acting user must already hold "
            + "the staging container. The content's permanent home is decided later, by the resolver."),

        new SinkSite("Services/Workspace/ProjectPreFillService.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedConfig, "",
            "_speOptions.StagingContainerId (SharePointEmbedded:StagingContainerId)",
            "The project twin of the matter pre-fill site, identical provenance and identical reasoning "
            + "(ADR-003; ADR-007). Listed separately rather than folded in because the two services diverge "
            + "in every other respect and a shared entry would hide a future divergence here."),

        new SinkSite("Api/Ai/ChatWordExportEndpoints.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedConfig, "",
            "SharePointEmbedded:StagingContainerId, falling back to EmailProcessing:DefaultContainerId",
            "Chat Word export writes the generated DOCX to the staging container under OBO (ADR-003; "
            + "ADR-013). No owning record exists — the artifact is minted from a chat message. The route IS "
            + "mapped (Infrastructure/DI/EndpointMappingExtensions.cs), contrary to the sweep's "
            + "'ZERO callers' note, so treat the sink as reachable; source analysis cannot attest client "
            + "usage either way."),

        new SinkSite("Api/Ai/ChatDocumentEndpoints.cs", "UploadSmallAsUserAsync", 1,
            Provenance.ServerDerivedConfig, "",
            "ResolveContainerId(session, configuration) -> SharePointEmbedded:StagingContainerId, "
            + "falling back to EmailProcessing:DefaultContainerId",
            "Chat document persistence, same config keys and same reasoning as the Word export sibling "
            + "(ADR-003; ADR-013). Worth one caution for whoever revisits this: the fallback to "
            + "EmailProcessing:DefaultContainerId means a deployment that configures only the email default "
            + "will route chat artifacts into the email container. Server-derived either way, so not this "
            + "guard's business, but it is the kind of config coupling that produces a surprise."),

        // ⚠️ DELETED 2026-08-28, RESTORED 2026-08-29 — the entry below replaces the deletion note that
        // stood here. It is filed under ServerDerivedRecord, NOT Dead, and not ClientSupplied. Both
        // reclassifications are deliberate:
        //
        //   · not Dead — the 2026-08-28 deletion of the whole class rested on a "zero production callers"
        //     search that was PRODUCTION-ONLY. The type also had a DI registration, five unit tests
        //     (including the CHAT-ATTACHMENT-POLICY gate), THIS allow-list citation, and live consumers in
        //     two sibling worktrees. A caller count of zero in one tree is not evidence of deadness, and
        //     this list's Dead classification means "provably unreachable", which was never established.
        //
        //   · not ClientSupplied — the deletion note said the DriveId override was "a caller-named drive
        //     waiting for its first caller". That was true of the PRE-076 shape and had ALREADY been fixed
        //     when the note was written: task 076 demoted request.DriveId to the fallback ARGUMENT of
        //     CommunicationContainerResolver.ResolveContainerAsync, so a secure regarding's own container
        //     beats it and a secure regarding with no container fails closed. The class was restored in
        //     that hardened form only. If a future edit ever reverts to
        //     `request.DriveId ?? ArchiveContainerId` at the sink, this entry becomes ClientSupplied and
        //     needs an owning task — that is the edit this declaration exists to make visible.
        new SinkSite("Services/Communication/MessageAttachmentMaterializer.cs", "UploadSmallAsync", 1,
            Provenance.ServerDerivedRecord, "",
            "CommunicationContainerResolver.ResolveContainerAsync(request.CommunicationId, "
            + "request.DriveId ?? _options.ArchiveContainerId)",
            "The messaging-channel twin of the IncomingCommunicationProcessor rows above, and resolved the "
            + "same way (ADR-003 fail-closed; ADR-045 communication architecture). Note where the "
            + "caller-supplied value sits: request.DriveId is passed IN as the non-secure default — INV-7's "
            + "tier-3 fallback — never consulted ahead of the record. A null resolver REFUSES rather than "
            + "using the shared archive container, which is the CLAUDE.md §10 F.1 asymmetric-registration "
            + "trap being avoided rather than fallen into. Its upload also lands FLAT with the "
            + "communication id folded into a SANITIZED filename, so it can no longer mint folders."),

        // ---------------------------------------------------------------------------------------------
        // Dead — expected to be deleted, not classified forever.
        // ---------------------------------------------------------------------------------------------
        new SinkSite("Services/Email/EmailAttachmentProcessor.cs", "UploadSmallAsync", 1,
            Provenance.Dead, "083 (delete-rather-than-convert)",
            "request.DriveId on ProcessAttachmentsRequest",
            "EVIDENCE OF DEADNESS: IEmailAttachmentProcessor IS resolved in production, but only "
            + "ShouldFilterAttachment is ever called (IncomingCommunicationProcessor.cs:901). "
            + "ProcessAttachmentsAsync — the sole path to this sink, via the private "
            + "ProcessSingleAttachmentAsync — has zero production callers; the inbound pipeline does its own "
            + "upload through the task-075 resolver instead. A partially-live service with a dead write path "
            + "is the hardest deadness to see, which is why the evidence is written down (ADR-003; "
            + "ADR-045)."),
    };

    // =================================================================================================
    // THE SINK VOCABULARY — the second inversion
    // -------------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE. You are here because Infrastructure/Graph/** declares a write-shaped method
    // that is not classified below. Decide which it is and add it:
    //
    //   ContentWrite      — it creates, replaces or deletes an item INSIDE a container/drive. Adding it here
    //                       arms the site scan, which will then very likely fail with new undeclared sites.
    //                       That cascade is the mechanism working.
    //   NotAContentWrite  — it mutates something else (the container itself, a permission, a column, a
    //                       subscription). Say WHAT it mutates; "not relevant" is not a reason.
    //
    // Why this list exists at all: the sink-NAME census is as hand-maintained as the file census this guard
    // inverts, and it was already wrong. Task 083's brief listed eight "known sink names" and omitted
    // UploadFileToContainerForConfigAsync — the live upload sink behind POST
    // /api/spe/containers/{id}/items/upload. Pinning declarations means the next new primitive fails the
    // build instead of being silently un-scanned. The probe is scoped to Infrastructure/Graph/** because
    // ADR-007 makes that the only layer allowed to touch Graph, and ThePlumbingLayerExclusionIsStillJustified
    // enforces it.
    // =================================================================================================

    private enum SinkKind
    {
        ContentWrite,
        NotAContentWrite,
    }

    private sealed record SinkName(string Name, SinkKind Kind, string Reason);

    /// <summary>
    /// Names probed for. Deliberately shape-based rather than a literal list, so a NEW primitive is caught by
    /// the pattern and forced into <see cref="SinkVocabulary"/>.
    /// </summary>
    private static readonly Regex WriteShapedDeclarationName = new(
        @"^(?:Upload\w*Async|Replace\w*Async|Delete\w*Async|Create\w*UploadSession\w*Async|CreateFolder\w*Async)$",
        RegexOptions.Compiled);

    private static readonly IReadOnlyList<SinkName> SinkVocabulary = new[]
    {
        new SinkName("UploadSmallAsync", SinkKind.ContentWrite,
            "App-only (MI) small-file upload — the most-used content write in the codebase (ADR-007). "
            + "Creates a drive item at a caller-chosen path inside the drive it is handed."),

        new SinkName("UploadSmallAsUserAsync", SinkKind.ContentWrite,
            "The OBO twin of UploadSmallAsync (ADR-007). Creates a drive item under the acting user's "
            + "identity, so the user's own container ACL applies — a constraint, not an authorization "
            + "decision about the RECORD."),

        new SinkName("UploadSmallFileAsync", SinkKind.ContentWrite,
            "SpeAdminGraphService's private small-file leg, selected by UploadFileToContainerAsync for "
            + "payloads under the chunking threshold (ADR-007). Reachable only through that public method."),

        new SinkName("UploadLargeFileAsync", SinkKind.ContentWrite,
            "SpeAdminGraphService's private chunked leg, the >4MB counterpart of UploadSmallFileAsync "
            + "(ADR-007). Opens a Graph upload session and writes the item in ranges."),

        new SinkName("UploadFileToContainerAsync", SinkKind.ContentWrite,
            "SpeAdminGraphService's public upload entry point; picks small-vs-large internally (ADR-007). "
            + "Absent from every prior sink list this project kept, which is what this vocabulary pin fixes."),

        new SinkName("UploadFileToContainerForConfigAsync", SinkKind.ContentWrite,
            "The container-type-config-scoped wrapper around UploadFileToContainerAsync and the ACTUAL sink "
            + "name at the SPE-admin upload decision site (ADR-007). The name the manual sweep and the task "
            + "brief both missed."),

        new SinkName("UploadChunkAsUserAsync", SinkKind.ContentWrite,
            "OBO chunk PUT against a Graph upload-session URL (ADR-007). Currently unreachable from any "
            + "caller since task 076 deleted the chunked route pair; kept in the vocabulary so a new caller "
            + "would be caught rather than silently un-scanned."),

        new SinkName("ReplaceFileContentAsUserAsync", SinkKind.ContentWrite,
            "OBO in-place content replacement, minting a new SPE version of an EXISTING item (ADR-007; "
            + "ADR-049 makes this Compose's canonical persist idiom). A replace is a write into the "
            + "container the item already lives in."),

        new SinkName("DeleteFileAsync", SinkKind.ContentWrite,
            "App-only (MI) drive-item delete (ADR-007). A destroy, so the wrong-resource-domain failure mode "
            + "leaves nothing behind to audit."),

        new SinkName("DeleteItemAsUserAsync", SinkKind.ContentWrite,
            "The OBO twin of DeleteFileAsync (ADR-007). Zero callers since tasks 071/076 retired the OBO "
            + "drive-keyed routes; retained in the vocabulary for the same reason as UploadChunkAsUserAsync."),

        new SinkName("DeleteDriveItemAsync", SinkKind.ContentWrite,
            "SpeAdminGraphService's drive-item delete, reached through DeleteDriveItemForConfigAsync "
            + "(ADR-007). The Graph call itself is graphClient.Drives[driveId].Items[itemId].DeleteAsync."),

        new SinkName("DeleteDriveItemForConfigAsync", SinkKind.ContentWrite,
            "The container-type-config-scoped delete used by the SPE-admin item surface (ADR-007). One of "
            + "the three client-named writes in ContainerItemEndpoints.cs."),

        new SinkName("CreateUploadSessionAsUserAsync", SinkKind.ContentWrite,
            "Opens an OBO Graph upload session against a drive path, which is the act that reserves the "
            + "destination item (ADR-007). Dead alongside UploadChunkAsUserAsync since task 076."),

        new SinkName("CreateFolderAsync", SinkKind.ContentWrite,
            "Creates a folder item inside a container via Children.PostAsync (ADR-007). No bytes, but it is "
            + "an item created in the container, and it is where the next write lands."),

        new SinkName("CreateFolderForConfigAsync", SinkKind.ContentWrite,
            "The container-type-config-scoped folder create used by the SPE-admin item surface (ADR-007). "
            + "The third client-named write in ContainerItemEndpoints.cs, missed entirely by the manual sweep."),

        // ---- NotAContentWrite: mutations of something OTHER than container content ----
        new SinkName("DeleteColumnAsync", SinkKind.NotAContentWrite,
            "Deletes a SharePoint list COLUMN from the container's document-library schema — metadata "
            + "shape, not an item, and it places no content anywhere (ADR-007). Governed by the SPE-admin "
            + "config surface, not by container provenance."),

        new SinkName("DeleteColumnForConfigAsync", SinkKind.NotAContentWrite,
            "The container-type-config-scoped wrapper around DeleteColumnAsync; same reasoning — column "
            + "schema, no item, no bytes (ADR-007)."),

        new SinkName("DeleteSubscriptionAsync", SinkKind.NotAContentWrite,
            "Deletes a Graph CHANGE-NOTIFICATION subscription (graphClient.Subscriptions[id]) — a webhook "
            + "registration, not container content (ADR-007; ADR-045 owns the notification lifecycle)."),
    };

    private static readonly IReadOnlySet<string> ContentWriteSinks =
        SinkVocabulary.Where(v => v.Kind == SinkKind.ContentWrite)
                      .Select(v => v.Name)
                      .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The pseudo-sink standing for a direct Graph drive-item mutation that bypasses the SpeFileStore facade
    /// (ADR-007's rule). Routed through the same allow-list mechanism so a bypass is a declarable site rather
    /// than a special case, and so the negative control can seed one.
    /// </summary>
    private const string DirectGraphMutation = "graphClient.Drives[].Items[] mutation";

    // =================================================================================================
    // RULE A — every discovered sink site is declared (THE INVERSION)
    // =================================================================================================

    [Fact(DisplayName = "Task 083 Rule A: every SPE write-sink site in the BFF carries a declared container provenance")]
    public void EverySpeWriteSinkSiteCarriesADeclaredContainerProvenance()
    {
        var discovered = ScanTree();
        var violations = Evaluate(discovered, AllowList);

        Assert.True(
            violations.Count == 0,
            "SPE write-sink site(s) in the BFF have NO declared container provenance. This is the whole "
            + "point of this guard: a sink nobody has classified is a container decision nobody has "
            + "reviewed, and SPE permissions are CONTAINER-level — content written into a shared container "
            + "is readable by every member of that container, with no per-file deny to narrow it, until "
            + "somebody notices and moves or deletes the item. A misrouted write is silent; this guard is "
            + "the noticing.\n\n"
            + "REMEDY — trace the container/drive id BACKWARDS from the sink to where it is first produced, "
            + "then add an entry to AllowList in this file. Read the MAINTENANCE PROCEDURE above it first; "
            + "in particular, a value threaded three call frames down from a request DTO is still "
            + "ClientSupplied (that is exactly how the Office save path stayed invisible).\n\n"
            + "Do NOT make this pass by removing the file from the scan. There is no per-file opt-out, on "
            + "purpose — narrowing scope to make a hole disappear is the failure this guard exists to "
            + "prevent.\n\n"
            + "Undeclared / unclassifiable sites:\n  " + string.Join("\n  ", violations));

        // Non-vacuity. Every assertion here would hold if the scanner silently found nothing, so the
        // discovered set is pinned against the declared set in BOTH directions (the stale half lives in
        // NoDeclaredSinkSiteIsStale). A scanner regression shows up as a wall of stale entries, not a
        // green run.
        Assert.True(
            discovered.Count(d => !d.Unclassifiable) == AllowList.Count,
            $"The scanner found {discovered.Count(d => !d.Unclassifiable)} sink call sites but "
            + $"{AllowList.Count} are declared. Rule A passed, so nothing is UNDECLARED — which means the "
            + "counts diverged through a duplicate or stale entry. Check NoDeclaredSinkSiteIsStale's output.");
    }

    [Fact(DisplayName = "Task 083 Rule A: no declared sink site is stale — a fixed or deleted site's entry must be deleted")]
    public void NoDeclaredSinkSiteIsStale()
    {
        // What makes the list SELF-LIQUIDATING. When task 083 converts the Office path or #858 lands the
        // Compose fix, the corresponding entries stop being true; leaving them behind would quietly
        // pre-authorize whatever appears at that ordinal next, and it would inflate the apparent size of
        // the remaining work list. RouteAuthorizationGuardTests learned this the hard way: three separate
        // tasks (071, 073, 079) each left dead waivers that only hand inspection caught.
        var present = ScanTree()
            .Where(d => !d.Unclassifiable)
            .Select(d => Key(d.File, d.Sink, d.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var stale = AllowList
            .Where(a => !present.Contains(Key(a.File, a.Sink, a.Ordinal)))
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal} ({a.Provenance}"
                         + (string.IsNullOrWhiteSpace(a.OwningTask) ? ")" : $", owner: {a.OwningTask})"))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These declared sink sites NO LONGER EXIST. Either the site was fixed/deleted — in which case "
            + "DELETE the entry, and that deletion is how the work list visibly shrinks toward zero — or the "
            + "sink MOVED, in which case declare its new home BEFORE deleting the old entry, because this "
            + "rule cannot tell those two cases apart.\n\n"
            + "If a whole file moved, note that entries are keyed on (file, sink, ordinal): a rename looks "
            + "identical to a deletion from here.\n\n  " + string.Join("\n  ", stale));

        Assert.True(
            AllowList.Select(a => Key(a.File, a.Sink, a.Ordinal)).Distinct(StringComparer.Ordinal).Count()
                == AllowList.Count,
            "Duplicate allow-list keys — two entries for one sink site means one of them is unreviewed.");
    }

    // =================================================================================================
    // RULE B — the provenance classification has teeth
    // =================================================================================================

    [Fact(DisplayName = "Task 091 Rule F: the AdministrativeRoleScoped set is pinned, and every member is genuinely group-gated")]
    public void TheAdministrativeRoleScopedSetIsPinnedAndActuallyGroupGated()
    {
        // MAINTENANCE PROCEDURE. This provenance says "the client names the container and that is
        // correct, because the caller is an authenticated ADMIN bounded by tenant scope". It is the one
        // category in this guard that does NOT shrink to zero, so it is the one a future change is most
        // tempted to hide in. Two mechanisms stop that:
        //
        //   1. The membership is PINNED below. A new member cannot join silently; adding one is a
        //      deliberate edit that a reviewer sees.
        //   2. The claim is VERIFIED, not trusted. A route can only inherit the admin-role and
        //      tenant-scope filters if it registers on the group that carries them — and a route that
        //      spells its own absolute "/api/..." path does not need the group to resolve. That is
        //      precisely the shape these three routes had while registered on the ROOT app, gated by
        //      nothing at all, until task 091. So: any file claiming this provenance must declare only
        //      group-relative routes.
        //
        // What this rule cannot see: whether the group those routes register on actually carries the
        // filters. RouteAuthorizationGuardTests.GroupGatedFilesRegisterNoAbsolutePaths covers the same
        // invariant from the route side, the RouteGroupBuilder parameter type enforces it at compile
        // time, and SpeAdminContainerItemRouteGateTests proves it at runtime with real requests. This
        // rule is the provenance-side half of that set, not the whole proof.
        var expected = new[]
        {
            ("Api/SpeAdmin/ContainerItemEndpoints.cs", "CreateFolderForConfigAsync"),
            ("Api/SpeAdmin/ContainerItemEndpoints.cs", "DeleteDriveItemForConfigAsync"),
            ("Api/SpeAdmin/ContainerItemEndpoints.cs", "UploadFileToContainerForConfigAsync"),
        };

        var actual = AllowList
            .Where(s => s.Provenance == Provenance.AdministrativeRoleScoped)
            .Select(s => (s.File, s.Sink))
            .OrderBy(x => x.File, StringComparer.Ordinal)
            .ThenBy(x => x.Sink, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            actual.SequenceEqual(expected.OrderBy(x => x.Item1, StringComparer.Ordinal)
                                         .ThenBy(x => x.Item2, StringComparer.Ordinal)),
            "The AdministrativeRoleScoped set changed. This provenance means \"client-named container, "
            + "and that is correct because the caller is a role-gated, tenant-scoped admin\" — it is the "
            + "only category here that never shrinks to zero, so joining it must be deliberate.\n\n"
            + "Before adding a member, confirm ALL of: (a) the route is genuinely administrative — its "
            + "function is to operate on a container the admin names, not to store a record's content; "
            + "(b) there is no owning record it could derive the container from instead; (c) it "
            + "registers on a group carrying BOTH SpeAdminAuthorizationFilter and "
            + "SpeAdminTenantScopeFilter. If (c) is not true you have found a live hole, not a new "
            + "category — that was exactly task 091's finding.\n\n"
            + $"Expected:\n  {string.Join("\n  ", expected.Select(e => $"{e.Item1} :: {e.Item2}"))}\n\n"
            + $"Found:\n  {string.Join("\n  ", actual.Select(a => $"{a.File} :: {a.Sink}"))}");

        // The verification half: every claiming file must declare only group-relative routes, so its
        // routes genuinely cannot resolve without the group that carries the two filters.
        var selfRegistering = new List<string>();

        foreach (var file in actual.Select(a => a.File).Distinct(StringComparer.Ordinal))
        {
            var source = File.ReadAllText(
                Path.Combine(BffRoot, file.Replace('/', Path.DirectorySeparatorChar)));

            foreach (Match match in Regex.Matches(
                         source, @"\.Map(?:Get|Post|Put|Patch|Delete)\s*\(\s*""(?<route>[^""]+)"""))
            {
                var route = match.Groups["route"].Value;
                if (route.StartsWith("/api/", StringComparison.Ordinal))
                {
                    selfRegistering.Add($"{file}: \"{route}\"");
                }
            }
        }

        Assert.True(
            selfRegistering.Count == 0,
            "These files claim AdministrativeRoleScoped provenance but declare ABSOLUTE route paths, so "
            + "their routes resolve without the group — which means they may inherit neither the "
            + "admin-role filter nor the tenant-scope filter, and the provenance claim is unfounded. "
            + "This is the pre-task-091 shape of ContainerItemEndpoints exactly.\n\n  "
            + string.Join("\n  ", selfRegistering));

        Assert.True(
            actual.Length > 0,
            "No AdministrativeRoleScoped site is declared, so this rule asserts nothing. If the last one "
            + "was removed, delete the rule and the enum member rather than leaving both to pass "
            + "vacuously.");
    }

    [Fact(DisplayName = "Task 083 Rule B: every ClientSupplied site names an owning task, every Dead site names its evidence, every entry carries a reasoned ADR citation")]
    public void EveryDeclaredSiteCarriesAnOwnerWhereRequiredAndAReasonWithAnAdrCitation()
    {
        // The part of this mechanism that decays. An unexplained exemption is indistinguishable from an
        // oversight six months later, and a ClientSupplied site with no owner is how a temporary hole
        // becomes permanent. Enforced rather than trusted — the same bar RouteAuthorizationGuardTests
        // holds its waivers to.
        var ownerless = AllowList
            .Where(a => a.Provenance is Provenance.ClientSupplied or Provenance.Dead
                        && string.IsNullOrWhiteSpace(a.OwningTask))
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal} ({a.Provenance})")
            .ToList();

        Assert.True(
            ownerless.Count == 0,
            "Every ClientSupplied site MUST name the task that will remove it, and every Dead site MUST name "
            + "who deletes it, so this list reads as a work list that shrinks to zero rather than a set of "
            + "exemptions. Use \"UNOWNED\" only for a site this guard found that has not been assigned "
            + "yet — and treat that as a defect report, not a waiver.\n\n  "
            + string.Join("\n  ", ownerless));

        var thin = AllowList
            .Where(a => string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Trim().Length < 80)
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal}")
            .ToList();

        Assert.True(
            thin.Count == 0,
            "Every entry must carry a substantive written reason — a paragraph a reviewer two years from now "
            + "can evaluate. \"Legacy\" is not a reason; \"server-side, fine\" is not a reason. Entries "
            + "missing one:\n  " + string.Join("\n  ", thin));

        var uncited = AllowList
            .Where(a => !Regex.IsMatch(a.Reason, @"ADR-\d{3}"))
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal}")
            .ToList();

        Assert.True(
            uncited.Count == 0,
            "Every entry must cite the ADR its reasoning rests on (ADR-nnn) — tests/CLAUDE.md's authoring "
            + "rule for this KEEP path. ADR-003 (authorization seams / fail-closed) applies to essentially "
            + "every entry; add ADR-007 for the SPE access path, ADR-008 where a route filter is the missing "
            + "mechanism, ADR-045 for communication, ADR-049 for Compose. Entries missing one:\n  "
            + string.Join("\n  ", uncited));

        var noOrigin = AllowList
            .Where(a => string.IsNullOrWhiteSpace(a.Origin) || a.Origin.Trim().Length < 10)
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal}")
            .ToList();

        Assert.True(
            noOrigin.Count == 0,
            "Every entry must state WHERE the container/drive id comes from, concretely enough to check: the "
            + "route parameter, the config key, the DTO property, the resolver call. The classification is a "
            + "conclusion; the origin is the evidence for it.\n  " + string.Join("\n  ", noOrigin));

        // Server-derived is a CLAIM, and the cheapest way it goes wrong is a copy-pasted entry that
        // inherits a sibling's justification. Requiring the origin string to look like what it claims
        // catches that without pretending to verify the code.
        var configWithoutConfigOrigin = AllowList
            .Where(a => a.Provenance == Provenance.ServerDerivedConfig
                        && !Regex.IsMatch(a.Origin, @"_options\.|_speOptions\.|Configuration|ContainerId\b|:[A-Za-z]+ContainerId"))
            .Select(a => $"{a.File} :: {a.Sink} #{a.Ordinal} — origin does not read as configuration: {a.Origin}")
            .ToList();

        Assert.True(
            configWithoutConfigOrigin.Count == 0,
            "A ServerDerivedConfig entry whose stated origin does not mention an options object or a config "
            + "key is either misclassified or carrying an inherited reason. Say which key it reads.\n  "
            + string.Join("\n  ", configWithoutConfigOrigin));
    }

    // =================================================================================================
    // RULE C — the sink vocabulary is pinned (the second inversion)
    // =================================================================================================

    [Fact(DisplayName = "Task 083 Rule C: the SPE write-sink vocabulary of the Graph plumbing layer is pinned, so a NEW primitive must be classified")]
    public void TheSpeWriteSinkVocabularyOfThePlumbingLayerIsPinned()
    {
        var declared = SinkVocabulary.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in PlumbingLayerFiles())
        {
            var text = Decomment(File.ReadAllText(file));

            foreach (Match m in MethodDeclaration.Matches(text))
            {
                var name = m.Groups["name"].Value;
                if (WriteShapedDeclarationName.IsMatch(name))
                {
                    found.Add(name);
                }
            }
        }

        var unclassified = found.Except(declared, StringComparer.Ordinal).ToList();

        Assert.True(
            unclassified.Count == 0,
            "Infrastructure/Graph/** declares write-shaped method(s) that the sink vocabulary does not "
            + "classify. A sink name nobody has classified is a sink the site scan does not look for — the "
            + "same blind spot as an unlisted FILE, one level down. This is not hypothetical: task 083's own "
            + "brief listed eight sink names and omitted UploadFileToContainerForConfigAsync, the live "
            + "upload sink behind POST /api/spe/containers/{id}/items/upload.\n\n"
            + "REMEDY — add each to SinkVocabulary as ContentWrite (creates/replaces/deletes an item inside "
            + "a container) or NotAContentWrite (say WHAT it mutates instead). Expect a ContentWrite "
            + "addition to cascade into new Rule A failures; that cascade is the mechanism working.\n\n  "
            + string.Join("\n  ", unclassified));

        var vanished = declared.Except(found, StringComparer.Ordinal).ToList();

        Assert.True(
            vanished.Count == 0,
            "Vocabulary entries name methods no longer declared in Infrastructure/Graph/**. Good if a dead "
            + "primitive was deleted — remove the entry too. Bad if it was RENAMED or MOVED out of the "
            + "plumbing layer, because then the site scan has stopped looking for a live sink. Check which "
            + "before deleting:\n  " + string.Join("\n  ", vanished));

        var thin = SinkVocabulary
            .Where(v => string.IsNullOrWhiteSpace(v.Reason) || v.Reason.Trim().Length < 80
                        || !Regex.IsMatch(v.Reason, @"ADR-\d{3}"))
            .Select(v => v.Name)
            .ToList();

        Assert.True(
            thin.Count == 0,
            "Every vocabulary entry needs a substantive reason with an ADR citation, same bar as the "
            + "allow-list. A NotAContentWrite entry with a thin reason is how a real sink gets excluded from "
            + "the scan with nobody noticing:\n  " + string.Join("\n  ", thin));
    }

    // =================================================================================================
    // RULE D — the plumbing-layer exclusion is still justified (the guard ON the exclusion)
    // =================================================================================================

    [Fact(DisplayName = "Task 083 Rule D: the Graph plumbing layer originates no container id and nothing bypasses it, so excluding it hides nothing")]
    public void ThePlumbingLayerExclusionIsStillJustified()
    {
        // Read the PLUMBING-LAYER EXCLUSION decision at the top of this file first. The exclusion is only
        // sound while both halves below hold; this test is what turns "we decided to exclude it" into a
        // checkable claim.

        // (1) NO ORIGINATION. Every drive/container id in the excluded layer must arrive as a method
        //     parameter. Verified by the ABSENCE of every construct that could produce one locally.
        var originations = new List<string>();

        foreach (var file in PlumbingLayerFiles())
        {
            originations.AddRange(OriginationsIn(Relative(file), File.ReadAllText(file)));
        }

        Assert.True(
            originations.Count == 0,
            "The excluded Graph plumbing layer now ORIGINATES a container/drive id — it reads configuration, "
            + "a Dataverse container column, or an HTTP request value. The exclusion in this file rests on "
            + "the opposite being true (every id arrives as a method parameter), so it no longer applies and "
            + "the layer could now hide a real container decision.\n\n"
            + "REMEDY — pick one: (a) move the origination OUT of the plumbing layer to the caller that owns "
            + "the decision, which is where ADR-007 and ADR-003 both put it; or (b) narrow PlumbingLayer to "
            + "exclude only the files that remain pure and declare the new site in AllowList. Do NOT widen "
            + "this pattern to make the build green.\n\n  " + string.Join("\n  ", originations));

        // (2) NO BYPASS. Nothing outside the excluded layer may touch graphClient.Drives[...] directly.
        //     Deliberately stricter than a mutation-only check: a facade bypass is itself the interesting
        //     event, and this is what stops the exclusion from being a hole for a service that reaches
        //     around SpeFileStore to Graph.
        var bypasses = new List<string>();

        foreach (var file in ScannedFiles())
        {
            var text = Decomment(File.ReadAllText(file));

            foreach (Match m in GraphDrivesAccess.Matches(text))
            {
                bypasses.Add($"{Relative(file)}:{LineOf(text, m.Index)}");
            }
        }

        Assert.True(
            bypasses.Count == 0,
            "File(s) outside Infrastructure/Graph/** access graphClient.Drives[...] directly, bypassing the "
            + "SpeFileStore facade that ADR-007 makes the sole SPE access path. Two problems at once: the "
            + "ADR-007 violation, and the fact that this guard's plumbing-layer exclusion assumes every "
            + "content write routes through the facade.\n\n"
            + "REMEDY — route the call through SpeFileStore. If a genuinely new Graph capability is needed, "
            + "add it to the facade rather than reaching around it.\n\n  "
            + string.Join("\n  ", bypasses));

        // And the excluded layer must actually exist — a rename would otherwise silently turn the whole
        // exclusion into a no-op while every assertion above passed vacuously.
        Assert.True(
            PlumbingLayerFiles().Any(),
            $"No files found under {PlumbingLayer}. If the plumbing layer moved, update PlumbingLayer — "
            + "otherwise the exclusion excludes nothing and Rule A is about to report ~20 pass-through "
            + "sites, while these two guards silently verify an empty set.");
    }

    [Fact(DisplayName = "Task 083 Rule D: the set of sites reading a stamped sprk_containerid column directly is pinned")]
    public void TheSetOfSitesReadingAStampedContainerColumnDirectlyIsPinned()
    {
        // ServerDerivedRecord is the sanctioned classification, but it contains one category this guard
        // cannot verify: a site that reads sprk_containerid straight off a row instead of going through
        // RecordContainerResolver. SecureContainerDecision's own documentation states that stale stamps
        // demonstrably exist, because the creation wizard's business-unit cascade writes that column. For a
        // SECURE record the stamp is the right answer; for a non-secure one a stale stamp silently
        // redirects content — and "silently" is the operative word, since the write succeeds.
        //
        // Pinning the set means a NEW site cannot quietly join the one server-derived category whose
        // correctness depends on data hygiene rather than on a resolver. Same reasoning as
        // RouteAuthorizationGuardTests' PolicyOnlyRoutes pin: fence off what you cannot check.
        var actual = AllowList
            .Where(a => a.DirectStampRead)
            .Select(a => Key(a.File, a.Sink, a.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            Key("Services/Ai/WorkingDocumentService.cs", "UploadSmallAsync", 1),
        };

        var added = actual.Except(expected, StringComparer.Ordinal).ToList();
        var removed = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(
            added.Count == 0,
            "New site(s) marked DirectStampRead. Before pinning them here, prefer the resolver: "
            + "RecordContainerResolver applies SecureContainerDecision (secure -> own container or FAIL "
            + "CLOSED; otherwise the record's owningbusinessunit container) and is the reason a stale stamp "
            + "cannot misroute content on that path. Add to this set only if reading the column directly is "
            + "genuinely necessary, and say why in the entry's Reason.\n\n  " + string.Join("\n  ", added));

        Assert.True(
            removed.Count == 0,
            "Site(s) left the DirectStampRead set — good if they moved onto RecordContainerResolver (delete "
            + "them from this set AND drop the flag on the entry), bad if the entry was edited without "
            + "review:\n\n  " + string.Join("\n  ", removed));
    }

    // =================================================================================================
    // CONTROLS — mandatory for this KEEP path per tests/CLAUDE.md
    // =================================================================================================

    [Fact(DisplayName = "Task 083 negative control: the detector fires on an undeclared sink site, including the two the manual sweep missed")]
    public void Detector_NegativeControl_FiresOnAnUndeclaredSinkSite()
    {
        // A detector nobody has seen fail is a detector nobody knows works. Each case below is fed to the
        // scanner as literal source text — nothing touches disk, so these keep proving the rule after the
        // real code changes.

        // Case 1 — the shape this guard was built for: a sink in a file no census contains.
        var rogue = Evaluate(
            ScanText("Api/Fake/RogueEndpoints.cs", """
                app.MapPost("/api/fake/{containerId}/upload", async (
                    string containerId, HttpRequest req, SpeFileStore speFileStore, CancellationToken ct) =>
                {
                    var driveId = await speFileStore.ResolveDriveIdAsync(containerId, ct);
                    var item = await speFileStore.UploadSmallAsync(driveId, "f.docx", req.Body, ct);
                    return TypedResults.Ok(item);
                }).RequireAuthorization();
                """),
            AllowList);

        Assert.Single(rogue);
        Assert.Contains("Api/Fake/RogueEndpoints.cs", rogue[0]);
        Assert.Contains("UploadSmallAsync", rogue[0]);
        Assert.Contains(":5", rogue[0]);   // the failure message must name the LINE, not just the file

        // Case 2 — the Office save path's actual shape (row 9, LIVE, missed by every prior sweep): the
        // client-supplied container is a plain method parameter three frames from the request, in a
        // SERVICE file, and the sink is reached via a local variable. No route census can see this.
        var officeShape = Evaluate(
            ScanText("Services/Fake/FakeStorageUploader.cs", """
                public async Task<bool> UploadToSpeAsync(
                    string containerId, string fileName, Stream content, CancellationToken ct)
                {
                    var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct);
                    var result = await _speFileStore.UploadSmallAsync(driveId, fileName, content, ct);
                    return result is not null;
                }
                """),
            AllowList);

        Assert.Single(officeShape);
        Assert.Contains("Services/Fake/FakeStorageUploader.cs", officeShape[0]);

        // Case 3 — the SPE-admin item surface's shape (row 10, LIVE, also outside the route census), and
        // specifically the sink NAME that every prior list omitted. A detector keyed only on the brief's
        // eight "known" names would pass this file.
        var speAdminShape = Evaluate(
            ScanText("Api/Fake/FakeContainerItemEndpoints.cs", """
                var config = await graphService.ResolveConfigAsync(configId, ct);
                var uploaded = await graphService.UploadFileToContainerForConfigAsync(
                    config, id, fileName, stream, size, folderId, ct);
                """),
            AllowList);

        Assert.Single(speAdminShape);
        Assert.Contains("UploadFileToContainerForConfigAsync", speAdminShape[0]);

        // Case 4 — a direct Graph drive-item mutation that reaches around the SpeFileStore facade. Routed
        // through the same allow-list mechanism, so a bypass is a declarable site rather than a silent one.
        var directGraph = Evaluate(
            ScanText("Services/Fake/FakeBypass.cs", """
                var client = await _factory.CreateAppOnlyClientAsync(ct);
                await client.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: ct);
                """),
            AllowList);

        Assert.Single(directGraph);
        Assert.Contains(DirectGraphMutation, directGraph[0]);

        // Case 5 — FAIL-CLOSED. A sink occurrence the scanner cannot classify as call-or-declaration is
        // reported as a violation, never skipped. An unreadable site is exactly the blind spot this guard
        // exists to remove, so silence there would defeat the whole design.
        var unreadable = ScanText("Services/Fake/FakeIndirect.cs", """
            var sink = handlers[UploadSmallAsync(driveId, path, content, ct)];
            """);

        Assert.Contains(unreadable, s => s.Unclassifiable);
        Assert.Contains("could not classify", string.Join("\n", Evaluate(unreadable, AllowList)));
    }

    [Fact(DisplayName = "Task 083 positive control: the detector does not fire on the sanctioned ServerDerivedRecord shape, on prose, or on declarations")]
    public void Detector_PositiveControl_DoesNotFireOnTheSanctionedShape()
    {
        // A guard that flags the code it protects gets deleted rather than obeyed — this caught two real
        // defects in task 060. Four ways this rule could wrongly push code AWAY from the sanctioned shape:

        // (1) The sanctioned ServerDerivedRecord shape, declared, must be accepted — and must NOT be
        //     dragged into Rule B's owning-task requirement, which is the ClientSupplied rule only.
        var sanctionedAllowList = new[]
        {
            new SinkSite("Services/Fake/FakeIngest.cs", "UploadSmallAsync", 1,
                Provenance.ServerDerivedRecord, "",
                "ResolveContainerForContentAsync(scope, communicationId, ...)",
                "Control fixture mirroring the IncomingCommunicationProcessor reference implementation: the "
                + "container is resolved from the RECORD through SecureContainerDecision, so a secure record "
                + "goes to its own container or nowhere (ADR-003 fail-closed; ADR-045)."),
        };

        var sanctioned = Evaluate(
            ScanText("Services/Fake/FakeIngest.cs", """
                var driveId = await ResolveContainerForContentAsync(scope, communicationId, "attachments", ct);
                if (string.IsNullOrWhiteSpace(driveId)) { return; }
                var fileHandle = await speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);
                """),
            sanctionedAllowList);

        Assert.Empty(sanctioned);

        Assert.Empty(sanctionedAllowList
            .Where(a => a.Provenance == Provenance.ClientSupplied && string.IsNullOrWhiteSpace(a.OwningTask)));

        // (2) PROSE must not count as a sink. Nearly every file in this codebase discusses these methods in
        //     doc comments — ContentDedupDetector.cs and IDocumentProfileAi.cs both carry
        //     <see cref="SpeFileStore.UploadSmallAsync"/>, and ComposeEndpoints.cs names
        //     ReplaceFileContentAsUserAsync in a comment right above unrelated code. A comment-blind scan
        //     would demand allow-list entries for documentation, and the list would be abandoned within a
        //     week. This is the same failure mode that made share-link's gate look present to a
        //     line-scoped scan in task 074.
        var proseOnly = ScanText("Services/Fake/FakeProse.cs", """
            /// <summary>Persists via <see cref="SpeFileStore.UploadSmallAsync"/> (ADR-007).</summary>
            // Future: switch this to ReplaceFileContentAsUserAsync(ctx, driveId, itemId, s, ct) once #806 lands.
            /* Historically this called DeleteFileAsync(driveId, itemId, ct) directly. */
            public void NotASink() { }
            """);

        Assert.Empty(proseOnly);

        // (3) A DECLARATION is not a call site. The facade and its implementations declare every sink in the
        //     vocabulary; if declarations counted, ISpeFileOperations.cs alone would demand six entries that
        //     make no container decision at all.
        var declarations = ScanText("Infrastructure/Fake/IFakeSpeOps.cs", """
            Task<FileHandleDto?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct);
            public virtual Task<bool> DeleteFileAsync(string driveId, string itemId, CancellationToken ct = default)
                => _driveItemOps.DeleteFileAsync(driveId, itemId, ct);
            """);

        // The declarations contribute nothing; only the delegating CALL on the second line is a site.
        Assert.Single(declarations);
        Assert.All(declarations, d => Assert.False(d.Unclassifiable));
        Assert.Equal("DeleteFileAsync", declarations[0].Sink);

        // (4) THE REAL SANCTIONED SITES IN PRODUCTION, which is the control that actually matters: scanning
        //     the two reference-implementation files against the real allow-list must produce nothing. Task
        //     060's lesson was that a synthetic positive control can pass while the guard still flags the
        //     production shape it was meant to protect.
        var referenceImplementations = new[]
        {
            "Services/Communication/IncomingCommunicationProcessor.cs",
            "Services/DocumentCheckoutService.cs",
        };

        var sanctionedInProduction = Evaluate(
            referenceImplementations.SelectMany(f => ScanText(f, ReadBff(f))),
            AllowList);

        Assert.True(
            sanctionedInProduction.Count == 0,
            "The guard flagged a REFERENCE IMPLEMENTATION of the sanctioned shape. Fix the guard, not the "
            + "reference implementation — a rule that pushes code away from the pattern it exists to require "
            + "gets deleted rather than obeyed.\n\n  "
            + string.Join("\n  ", sanctionedInProduction));

        // ...and they must not be scanning empty, or (4) proves nothing.
        Assert.True(
            referenceImplementations.SelectMany(f => ScanText(f, ReadBff(f))).Count() >= 3,
            "The reference-implementation files yielded fewer than the 3 known sink sites (two in "
            + "IncomingCommunicationProcessor, one in DocumentCheckoutService). A scanner that finds nothing "
            + "makes every positive control vacuous.");
    }

    [Fact(DisplayName = "Task 083 Rule D controls: the origination detector fires on a container config key and on an unverifiable key, and does NOT fire on the plumbing layer's legitimate config reads")]
    public void Origination_Controls_FireOnContainerOriginationOnlyAndNotOnLegitimateConfigReads()
    {
        // NEGATIVE — the four shapes that would make the plumbing-layer exclusion unsound. If any of these
        // appeared in Infrastructure/Graph/**, the layer would be choosing a container rather than plumbing
        // one, and Rule A's whole-tree scan would be skipping a real decision site.
        Assert.NotEmpty(OriginationsIn("Infrastructure/Graph/Fake.cs",
            "var driveId = _options.ArchiveContainerId;"));

        Assert.NotEmpty(OriginationsIn("Infrastructure/Graph/Fake.cs",
            "var driveId = configuration[\"SharePointEmbedded:StagingContainerId\"];"));

        Assert.NotEmpty(OriginationsIn("Infrastructure/Graph/Fake.cs",
            "var driveId = row.GetAttributeValue<string>(\"sprk_containerid\");"));

        Assert.NotEmpty(OriginationsIn("Infrastructure/Graph/Fake.cs",
            "var driveId = ctx.Request.Query[\"containerId\"].ToString();"));

        // FAIL-CLOSED — a config read whose key is computed cannot be cleared, so it is reported. A guard
        // that assumed a non-literal key was innocent would be trivially defeatable by naming the key in a
        // constant.
        var unverifiable = OriginationsIn("Infrastructure/Graph/Fake.cs",
            "var driveId = configuration.GetValue<string>(ContainerKeyName);");
        Assert.NotEmpty(unverifiable);
        Assert.Contains("NON-LITERAL", unverifiable[0]);

        // POSITIVE — the two REAL config reads in SpeAdminGraphService's constructor, verbatim in shape.
        // These are what the tier-1/tier-2 split exists for: they fired on the guard's first run, and
        // pinning them here is what stops a future edit from "simplifying" the pattern back into a
        // false-positive machine. A cache TTL and a Graph search region are not container ids.
        Assert.Empty(OriginationsIn("Infrastructure/Graph/SpeAdminGraphService.cs",
            "var ttlMinutes = configuration.GetValue<int>(\"SpeAdmin:GraphClientCacheTtlMinutes\", defaultValue: 30);"));

        Assert.Empty(OriginationsIn("Infrastructure/Graph/SpeAdminGraphService.cs",
            "var region = configuration.GetValue<string>(\"SpeAdmin:SearchRegion\");"));

        // POSITIVE — a container id arriving as a METHOD PARAMETER is the definition of plumbing and must
        // never be reported. If it were, the exclusion could not hold for any file in the layer and the
        // guard would be unusable.
        Assert.Empty(OriginationsIn("Infrastructure/Graph/SpeFileStore.cs", """
            public virtual Task<FileHandleDto?> UploadSmallAsync(
                string driveId, string path, Stream content, CancellationToken ct = default)
                => _uploadManager.UploadSmallAsync(driveId, path, content, ct);
            """));

        // POSITIVE — prose. Every one of these files discusses container ids in doc comments; a
        // comment-blind origination detector would condemn the layer on its own documentation.
        Assert.Empty(OriginationsIn("Infrastructure/Graph/Fake.cs",
            "/// Resolves the container's sprk_containerid; callers pass _options.ArchiveContainerId."));
    }

    // =================================================================================================
    // MACHINERY
    // -------------------------------------------------------------------------------------------------
    // Crude by design. This is arch-fitness scanning, not compilation: it strips comments and matches
    // patterns; it does not resolve types or follow data flow. That is adequate BECAUSE every rule above is
    // paired with a negative control proving the detector fires and a positive control proving it does not
    // fire on the sanctioned shape.
    //
    // The Decomment / SkipStringLiteral / StatementFrom helpers are duplicated from
    // RouteAuthorizationGuardTests rather than extracted to SourceScan. Deliberate: that file was being
    // edited concurrently by two other agents when this guard was written, and a shared-helper refactor
    // across three concurrent editors trades a real merge conflict for a stylistic win. If both guards
    // settle, hoisting these four helpers into SourceScan is the right follow-up.
    // =================================================================================================

    private sealed record DiscoveredSite(string File, string Sink, int Ordinal, int Line, bool Unclassifiable);

    private enum SiteKind
    {
        Call,
        Declaration,
        Unclassifiable,
    }

    /// <summary>A C# method declaration's name — used by Rule C, which pins the sink vocabulary.</summary>
    private static readonly Regex MethodDeclaration = new(
        @"(?:public|private|internal|protected)[^;{}()]*?\b(?<name>\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Constructs that can ONLY be container/drive origination — Rule D tier 1. An options object or an
    /// HTTP route/query/body read inside the plumbing layer is origination-shaped by definition: that layer
    /// receives an <c>HttpContext</c> for OBO token exchange and nothing else, so it has no business reading
    /// request values at all.
    /// </summary>
    private static readonly Regex ContainerOriginationUnconditional = new(
        @"IOptions\s*<|_options\s*\.|_speOptions\s*\."
        + @"|StagingContainerId|ArchiveContainerId|DefaultContainerId"
        + @"|sprk_containerid|sprk_graphdriveid"
        + @"|RouteValues|FromRoute|FromQuery|FromBody|Request\s*\.\s*Query",
        RegexOptions.Compiled);

    /// <summary>
    /// A generic configuration read — Rule D tier 2, and CONDITIONAL, because the plumbing layer legitimately
    /// reads configuration that has nothing to do with container identity.
    ///
    /// <para>This distinction was forced by the guard's own first run, which is the best evidence it works:
    /// the tier-1 pattern originally included a bare <c>GetValue&lt;</c> and fired on
    /// <c>SpeAdmin:GraphClientCacheTtlMinutes</c> and <c>SpeAdmin:SearchRegion</c> in
    /// <c>SpeAdminGraphService</c>'s constructor. A cache TTL and a Graph search region are not container
    /// ids. Widening the exclusion to make that pass would have been the wrong fix; narrowing the CLAIM to
    /// what it can actually support is the right one.</para>
    ///
    /// <para>So: a config read is origination when its key names a container or a drive, AND a config read
    /// whose key is not a literal is reported as UNVERIFIABLE rather than assumed innocent — fail-closed,
    /// because a key computed at runtime is precisely where a container key would hide.</para>
    /// </summary>
    private static readonly Regex ConfigurationRead = new(
        @"(?:[Cc]onfiguration\s*\[|GetValue\s*<[^<>]*>\s*\()\s*(?:""(?<key>[^""]*)"")?",
        RegexOptions.Compiled);

    private static readonly Regex ContainerShapedConfigKey =
        new(@"Container|Drive", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Direct Graph drive access — <c>graphClient.Drives[...]</c>.</summary>
    private static readonly Regex GraphDrivesAccess = new(@"\.\s*Drives\s*\[", RegexOptions.Compiled);

    /// <summary>A mutating Graph verb, used to tell a direct drive WRITE from a direct drive READ.</summary>
    private static readonly Regex GraphMutatingVerb =
        new(@"\.\s*(DeleteAsync|PutAsync|PostAsync|PatchAsync)\s*\(", RegexOptions.Compiled);

    /// <summary>Type tokens that mark a match as a method DECLARATION rather than a call.</summary>
    private static readonly IReadOnlySet<string> DeclarationTypeTokens =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "void", "Task", "ValueTask", "bool", "string", "int", "long", "object", "Stream", "DriveItem",
        };

    /// <summary>
    /// Every container/drive ORIGINATION expression in one file's source text — Rule D's detector, taken as
    /// text so the controls can feed it literal source rather than the real tree.
    /// </summary>
    private static IReadOnlyList<string> OriginationsIn(string relativeFile, string rawText)
    {
        var text = Decomment(rawText);
        var found = new List<string>();

        foreach (Match m in ContainerOriginationUnconditional.Matches(text))
        {
            found.Add($"{relativeFile}:{LineOf(text, m.Index)}: {m.Value.Trim()}");
        }

        foreach (Match m in ConfigurationRead.Matches(text))
        {
            var line = LineOf(text, m.Index);

            if (!m.Groups["key"].Success)
            {
                found.Add(
                    $"{relativeFile}:{line}: configuration read with a NON-LITERAL key — cannot verify it "
                    + "does not yield a container/drive id, so it is reported rather than assumed innocent");
                continue;
            }

            var key = m.Groups["key"].Value;
            if (ContainerShapedConfigKey.IsMatch(key))
            {
                found.Add($"{relativeFile}:{line}: configuration key \"{key}\" names a container/drive");
            }
        }

        return found;
    }

    private static string Key(string file, string sink, int ordinal) => $"{file}|{sink}|{ordinal}";

    private static string Relative(string absolute)
        => absolute
            .Replace(BffRoot + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string ReadBff(string relativeFile)
        => File.ReadAllText(Path.Combine(BffRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Every <c>.cs</c> file under the BFF, minus build output and minus the excluded plumbing layer.
    /// WHOLE-TREE by construction — there is no per-file opt-out, because the incompleteness of a
    /// hand-maintained file list is the defect this guard exists to remove.
    /// </summary>
    private static IEnumerable<string> ScannedFiles()
        => Directory
            .EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !Relative(f).StartsWith(PlumbingLayer + "/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static IEnumerable<string> PlumbingLayerFiles()
        => Directory
            .EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Relative(f).StartsWith(PlumbingLayer + "/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static List<DiscoveredSite> ScanTree()
        => ScannedFiles()
            .SelectMany(f => ScanText(Relative(f), File.ReadAllText(f)))
            .ToList();

    /// <summary>
    /// Every SPE write-sink CALL site in one file's source text.
    ///
    /// <para>Comment-stripped FIRST — prose naming a sink must not count as a sink. That is not a
    /// hypothetical nicety: <c>Services/Documents/ContentDedupDetector.cs</c> and
    /// <c>Services/Ai/PublicContracts/IDocumentProfileAi.cs</c> both carry
    /// <c>&lt;see cref="SpeFileStore.UploadSmallAsync"/&gt;</c>, and <c>Api/ComposeEndpoints.cs</c> names
    /// <c>ReplaceFileContentAsUserAsync</c> in a comment above unrelated code. A comment-blind scan would
    /// demand allow-list entries for documentation and the list would be abandoned.</para>
    ///
    /// <para>Declarations are excluded (a facade method's signature makes no container decision) and an
    /// occurrence that cannot be classified either way is returned as <c>Unclassifiable</c>, which
    /// <see cref="Evaluate"/> reports as a VIOLATION. Fail-closed, for the same reason
    /// <see cref="RouteAuthorizationGuardTests"/> fails on an unparseable registration: an unreadable site
    /// is precisely the blind spot being removed.</para>
    /// </summary>
    private static List<DiscoveredSite> ScanText(string relativeFile, string rawText)
    {
        var text = Decomment(rawText);
        var results = new List<DiscoveredSite>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sink in ContentWriteSinks.OrderBy(s => s, StringComparer.Ordinal))
        {
            foreach (Match m in Regex.Matches(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(sink)}\s*\("))
            {
                var kind = ClassifyMatch(text, m.Index);
                if (kind == SiteKind.Declaration)
                {
                    continue;
                }

                var line = LineOf(text, m.Index);

                if (kind == SiteKind.Unclassifiable)
                {
                    results.Add(new DiscoveredSite(relativeFile, sink, 0, line, Unclassifiable: true));
                    continue;
                }

                results.Add(new DiscoveredSite(relativeFile, sink, 0, line, Unclassifiable: false));
            }
        }

        // Direct Graph drive-item mutation, routed through the same mechanism so a facade bypass is a
        // declarable site rather than a special case. A drive READ is not a sink, so the statement the
        // access begins must actually reach a mutating verb.
        foreach (Match m in GraphDrivesAccess.Matches(text))
        {
            var statement = StatementFrom(text, m.Index);
            if (GraphMutatingVerb.IsMatch(statement))
            {
                results.Add(new DiscoveredSite(
                    relativeFile, DirectGraphMutation, 0, LineOf(text, m.Index), Unclassifiable: false));
            }
        }

        // Ordinals are assigned per (file, sink) in FILE order, not in the order the sink names were
        // iterated — otherwise the same site would change ordinal when the vocabulary grows.
        return results
            .OrderBy(r => r.Line)
            .Select(r =>
            {
                if (r.Unclassifiable)
                {
                    return r;
                }

                ordinals.TryGetValue(r.Sink, out var n);
                ordinals[r.Sink] = n + 1;
                return r with { Ordinal = n + 1 };
            })
            .ToList();
    }

    /// <summary>
    /// The violations for a set of discovered sites against an allow-list: every site must be declared, and
    /// an unclassifiable occurrence is a violation in its own right.
    ///
    /// <para>Taking the sites as an argument rather than scanning the tree internally is what makes both
    /// controls possible — they feed literal source text through the same code path the real rule uses,
    /// which is the only way a control proves anything about the real rule.</para>
    /// </summary>
    private static IReadOnlyList<string> Evaluate(
        IEnumerable<DiscoveredSite> sites,
        IReadOnlyList<SinkSite> allowList)
    {
        var declared = allowList
            .Select(a => Key(a.File, a.Sink, a.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<string>();

        foreach (var site in sites)
        {
            if (site.Unclassifiable)
            {
                violations.Add(
                    $"{site.File}:{site.Line}: could not classify the occurrence of {site.Sink} as a call or "
                    + "a declaration. Reported rather than skipped — an unreadable sink site is the blind "
                    + "spot this guard removes. Reformat the call so it reads as one, or teach "
                    + "ClassifyMatch about the shape.");
                continue;
            }

            if (declared.Contains(Key(site.File, site.Sink, site.Ordinal)))
            {
                continue;
            }

            violations.Add(
                $"{site.File}:{site.Line}: {site.Sink} (occurrence #{site.Ordinal} of that sink in this "
                + "file) writes into an SPE container with NO declared container provenance.");
        }

        return violations;
    }

    /// <summary>
    /// Whether a sink-name occurrence is a CALL, a method DECLARATION, or unreadable.
    ///
    /// <para>Decided by the token immediately before the name, which is enough for the shapes this codebase
    /// actually uses: <c>.Sink(</c> and <c>await Sink(</c> are calls; <c>Task&lt;T&gt; Sink(</c> and
    /// <c>Task Sink(</c> are declarations. <c>=&gt; Sink(</c> is special-cased because it also ends in
    /// <c>&gt;</c>. Anything else returns <see cref="SiteKind.Unclassifiable"/> rather than guessing.</para>
    /// </summary>
    private static SiteKind ClassifyMatch(string text, int nameStart)
    {
        var i = nameStart - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        if (i < 0)
        {
            return SiteKind.Unclassifiable;
        }

        if (text[i] == '.')
        {
            return SiteKind.Call;
        }

        if (text[i] == '>')
        {
            // `=> Sink(` is a call; `Task<T> Sink(` is a declaration.
            return i > 0 && text[i - 1] == '=' ? SiteKind.Call : SiteKind.Declaration;
        }

        var end = i + 1;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var word = text[start..end];

        if (word is "await" or "return")
        {
            return SiteKind.Call;
        }

        return DeclarationTypeTokens.Contains(word) ? SiteKind.Declaration : SiteKind.Unclassifiable;
    }

    /// <summary>
    /// The statement beginning at <paramref name="start"/>: text up to the first <c>;</c> outside every
    /// parenthesis and brace opened after that point, so a fluent Graph chain broken across many lines stays
    /// intact.
    /// </summary>
    private static string StatementFrom(string text, int start)
    {
        var parens = 0;
        var braces = 0;
        var i = start;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"')
            {
                i = SkipStringLiteral(text, i);
                continue;
            }

            switch (c)
            {
                case '(': parens++; break;
                case ')': parens--; break;
                case '{': braces++; break;
                case '}': braces--; break;
                case ';' when parens <= 0 && braces <= 0:
                    return text[start..(i + 1)];
            }

            i++;
        }

        return text[start..];
    }

    /// <summary>Index just past a string literal (handles verbatim, raw, interpolated and escaped forms).</summary>
    private static int SkipStringLiteral(string text, int quoteIndex)
    {
        if (quoteIndex + 2 < text.Length && text[quoteIndex + 1] == '"' && text[quoteIndex + 2] == '"')
        {
            var close = text.IndexOf("\"\"\"", quoteIndex + 3, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 3;
        }

        var verbatim = quoteIndex > 0 && text[quoteIndex - 1] == '@';
        var i = quoteIndex + 1;

        while (i < text.Length)
        {
            if (text[i] == '\\' && !verbatim)
            {
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>
    /// The text with line and block comments replaced by spaces, LINE STRUCTURE AND OFFSETS PRESERVED so a
    /// match index still maps to a line number. String literals are left intact — blanking a <c>//</c> inside
    /// a URL would corrupt the statement.
    /// </summary>
    private static string Decomment(string text)
    {
        var sb = new StringBuilder(text);
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                i = SkipStringLiteral(text, i);
                continue;
            }

            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    sb[i] = ' ';
                    i++;
                }

                continue;
            }

            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? text.Length : close + 2;
                for (var j = i; j < stop; j++)
                {
                    if (sb[j] != '\n')
                    {
                        sb[j] = ' ';
                    }
                }

                i = stop;
                continue;
            }

            i++;
        }

        return sb.ToString();
    }

    private static int LineOf(string text, int index)
        => text.Take(index).Count(c => c == '\n') + 1;
}
