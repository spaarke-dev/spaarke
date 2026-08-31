using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 074 — the forcing function for BFF route authorization: a route that
/// serves document metadata or file bytes and carries no per-resource authorization decision fails the
/// build unless it carries an explicit, reasoned, enumerable waiver.
///
/// <para><b>Why this exists.</b> Enforcement on this surface has been by human enumeration and the count
/// has been wrong every single time it was taken: the secure-project workflow review estimated ~15 document
/// routes, task 022's sweep found 22, then <c>POST /api/ai/search</c> and <c>POST
/// /api/documents/{id}/share-link</c> were found AFTER that sweep, then five more in
/// <c>Api/OBOEndpoints.cs</c>. On its first run this rule found a sixth nobody had listed —
/// <c>GET /api/v1/containers/{containerId}/documents</c>. The next miss was scheduled, not hypothetical.</para>
///
/// <para><b>Why source analysis and not endpoint reflection.</b> The task's preferred mechanism was to
/// construct the app, enumerate <c>EndpointDataSource</c>, and read each endpoint's metadata. That mechanism
/// is <i>structurally incapable</i> of answering this question, and the reason is a platform fact rather
/// than an implementation difficulty: <c>AddEndpointFilter</c> appends to an internal filter-factory list
/// that is compiled into the endpoint's <c>RequestDelegate</c>. It adds NOTHING to
/// <c>EndpointBuilder.Metadata</c>, and there is no <c>IEndpointFilterMetadata</c>. Reflection therefore
/// yields only <c>IAuthorizeData</c> (from <c>RequireAuthorization</c>) and <c>IAllowAnonymous</c> — which
/// is exactly the authenticated-vs-anonymous distinction that produced every one of the findings.
/// <c>share-link</c> sits on a group carrying <c>.RequireAuthorization()</c> and was still a hole.</para>
///
/// <para>There is a second, independent disqualifier: <c>/api/ai/search</c> is registered inside a compound
/// config gate (<c>EndpointMappingExtensions</c> — <c>DocumentIntelligence:Enabled &amp;&amp;
/// Analysis:Enabled</c>). An app built in a test with default configuration would not register it at all, so
/// the route of the first finding would be ABSENT from a reflected census and the rule would silently pass.
/// A rule whose coverage depends on test configuration is the failure mode this task exists to remove.</para>
///
/// <para><b>How the brittleness of source analysis is answered.</b> The task's objection to static analysis
/// — that it misses helper-wrapped registrations and so reproduces the original failure — is answered
/// structurally, not by promise. (1) <b>Parsing is fail-closed</b>: the scanner must account for 100% of the
/// <c>Map{Verb}</c> call sites it finds in a governed file, and a call site it cannot parse is reported as a
/// VIOLATION, never skipped — see <see cref="EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver"/>.
/// (2) <b>A file census pins the routing surface</b>, so a new endpoint file fails the build until it is
/// classified — see <see cref="TheEndpointFileCensusIsPinned"/>. A blind spot becomes loud instead of
/// invisible.</para>
///
/// <para><b>What this rule does NOT catch, stated plainly.</b> Presence of a mechanism is not presence of a
/// decision. Of the four historical misses, only two were structural absences. <c>/api/ai/search</c> had a
/// filter attached from the start that returned allow from every branch — caught here by
/// <see cref="NoAuthorizationFilterIsDecorative"/>. <c>PUT /api/containers/{containerId}/files/{*path}</c>
/// carried a real, fail-closed <c>ResourceAccessRequirement</c> policy whose handler authorized a CONTAINER
/// id against DOCUMENT rights — a wrong-resource-domain defect that no structural rule detects without
/// hard-coding that one mismatch.
///
/// <para>That route is GONE as of 2026-08-27: task 073 RETIRED it by deleting
/// <c>Api/UploadEndpoints.cs</c> outright, so it is no longer pinned in
/// <see cref="PolicyOnlyRoutes"/> — retirement removed the defect and the shape together. Its
/// wrong-resource-domain shape survives only as an INLINE fixture inside
/// <see cref="RetroactivelyDetectsAllFourHistoricalMisses"/>, which feeds the scanner literal source text
/// and never reads the file. The shape is NOT extinct in the codebase: the drive-keyed
/// <c>PUT /api/drives/{driveId}/upload</c> and <c>DELETE /api/drives/{driveId}/items/{itemId}</c> in
/// <c>DocumentsEndpoints.cs</c> are the same policy-only pattern, still live and still waived, and
/// <c>PUT /api/obo/containers/{id}/files/{*path}</c> is the same shape protected only by running under OBO
/// rather than app-only. See
/// <c>projects/unified-access-control-r2/notes/task-074-route-authorization-forcing-function.md</c> §5.</para>
/// </summary>
public class RouteAuthorizationGuardTests
{
    // =============================================================================================
    // THE SCOPE BOUNDARY
    // ---------------------------------------------------------------------------------------------
    // Written down deliberately, because the task's own warning is that "a rule whose scope nobody can
    // state will be waived".
    //
    //   RouteLevelGate    — the document-metadata / file-byte surface, where a per-route authorization
    //                       filter (or a resource policy) IS the established convention. Rule A applies.
    //
    //   HandlerAuthorized — the route authorizes INSIDE the handler rather than in the fluent chain.
    //                       Rule A deliberately does NOT apply: a structural rule cannot distinguish
    //                       "authorizes in the handler" from "does not authorize", and flagging these
    //                       would flag the reference implementation. ExternalProjectDataEndpoints is
    //                       named in the build plan as THE correct contact-plane pattern (it checks
    //                       project access AND doc-in-project before any SPE read). A guard that flags
    //                       the code it protects gets deleted rather than obeyed. These files are still
    //                       covered by Rule B and by the census; verifying them needs behavioural tests.
    //
    // Everything else under Api/** is NotGoverned — it serves no document or Dataverse content. That is
    // not a waiver: a not-governed file is outside this rule's subject matter, whereas a waiver is an
    // in-scope route deliberately exempted. The census below is what stops that distinction from rotting.
    // =============================================================================================

    private enum Scope
    {
        RouteLevelGate,
        HandlerAuthorized,
    }

    private sealed record GovernedFile(string RelativePath, Scope Scope, string Reason);

    private static readonly IReadOnlyList<GovernedFile> GovernedFiles = new[]
    {
        // ---- Rule A applies: the per-route gate is the convention here ----
        new GovernedFile("Api/FileAccessEndpoints.cs", Scope.RouteLevelGate,
            "/api/documents/{documentId}/* — file bytes (content, download, eml-render) and URL minting "
            + "(preview-url, view-url, office, open-links, share-link). Eight of nine routes already carry "
            + "AddDocumentAuthorizationFilter(\"read\"); the ninth is finding #3."),

        new GovernedFile("Api/DataverseDocumentsEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/documents/* — document rows plus a byte download, and a container-keyed document "
            + "listing. The download route is the sibling task 001 pinned against /api/documents/download."),

        new GovernedFile("Api/DocumentOperationsEndpoints.cs", Scope.RouteLevelGate,
            "checkout / checkin / discard / delete / analyze on a single document. All six gated by task 022."),

        new GovernedFile("Api/DocumentsBulkEndpoints.cs", Scope.RouteLevelGate,
            "/api/documents/bulk-download — finding C1. Its lookup is app-only, so the per-document verdict "
            + "from BulkDownloadAuthorizationFilter is the entire boundary."),

        new GovernedFile("Api/DocumentVersionEndpoints.cs", Scope.RouteLevelGate,
            "document-id-keyed version history and prior-version BYTES, both gated \"read\" by task 079. "
            + "The drive-keyed pair was DELETED; the SPE pointer is now read off the authorized row."),

        new GovernedFile("Api/DocumentsEndpoints.cs", Scope.RouteLevelGate,
            "drive-keyed upload and item delete."),

        // Api/UploadEndpoints.cs entry DELETED 2026-08-27 — task 073 deleted the file (218 lines, zero
        // additions), retiring all three app-only container-keyed write routes. Left in place it would
        // throw FileNotFoundException from ScanFile's unguarded File.ReadAllText and account for 4 of the
        // 5 guard failures. The wrong-resource-domain SHAPE it demonstrated is not lost: the retroactive
        // -validation test keeps it as an INLINE fixture (see the ScanText("Api/UploadEndpoints.cs", …)
        // call in RetroactivelyDetectsAllFourHistoricalMisses), which feeds the scanner literal source
        // text and never touches disk.

        new GovernedFile("Api/OBOEndpoints.cs", Scope.RouteLevelGate,
            "Container/drive-keyed SPE writes — finding #2. Task 071 deleted the four read/mutate routes "
            + "here (zero callers, gated id-keyed equivalents ship); the surviving upload trio is waived "
            + "to 075/076 because content-CREATION has no document to authorize against yet. "
            + "AddDocumentAuthorizationFilter still appears ZERO times in this file."),

        new GovernedFile("Api/Ai/SemanticSearchEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/search — document names, AI summaries, TL;DRs, driveId, speFileId. Finding #1. Never "
            + "touches SPE, so container ACLs are irrelevant to it."),

        new GovernedFile("Api/Ai/RecordSearchEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/search/records — Dataverse record content over the same AI search surface."),

        // ---- Rule A does NOT apply: authorization lives in the handler ----
        new GovernedFile("Api/ExternalAccess/ExternalProjectDataEndpoints.cs", Scope.HandlerAuthorized,
            "THE reference implementation per the Wave-3 build plan: each handler checks project access AND "
            + "doc-in-project before any SPE read, then streams app-only. The decision is in the handler by "
            + "design, because a contact is not a security principal and the access must be COMPUTED."),

        new GovernedFile("Api/ExternalAccess/ExternalModuleDataEndpoints.cs", Scope.HandlerAuthorized,
            "Scoped Dataverse reads for the contact plane. Authorization is the Tier2ScopeFilterInjector "
            + "rewriting the caller's FetchXML against their accessible-record set — a query-shaping "
            + "mechanism with no route-level equivalent."),

        // ---- Compose: ONE governed file became EIGHT (compose-r8 task 070) ----
        // Api/ComposeEndpoints.cs was split by reason-to-change. It is the same route surface, keyed the
        // same way (documentSpeId / documentId / sessionId), authorized the same way (group-level
        // RequireAuthorization + in-handler checks), and it stays HandlerAuthorized for the same reason:
        // converting Compose to route-level filters is a design change owned by ADR-049, not by this guard.
        //
        // Enumerated one-per-file rather than by prefix ON PURPOSE. A `StartsWith("Api/Compose")` rule
        // would have absorbed the split silently — and would absorb the NEXT Compose file silently too,
        // which is the census's whole subject. Eight entries cost eight lines and make a ninth file fail
        // loudly until someone classifies it.
        new GovernedFile("Api/ComposeDocumentEndpoints.cs", Scope.HandlerAuthorized,
            "GET /documents/{documentSpeId} (load), promote, refresh-profile — the document read/lifecycle "
            + "cluster. Serves SPE document bytes and metadata; the handler resolves the caller and the "
            + "document together."),

        new GovernedFile("Api/ComposeSaveEndpoints.cs", Scope.HandlerAuthorized,
            "POST /documents/{documentSpeId}/save and /documents/create-on-save — the write path. "
            + "create-on-save has no pre-existing document to authorize against, which is the same "
            + "content-CREATION shape waived on OBOEndpoints rather than a gap unique to Compose."),

        new GovernedFile("Api/ComposeAnnotationEndpoints.cs", Scope.HandlerAuthorized,
            "pull/reanchor annotations keyed by documentSpeId, plus GET/POST /sessions/{sessionId}/"
            + "annotations. The two session-scoped routes DO carry a route-level gate as of #863 "
            + "(.AddSessionOwnershipFilter()); the file stays HandlerAuthorized because the "
            + "documentSpeId-keyed pair does not."),

        new GovernedFile("Api/ComposeCheckoutEndpoints.cs", Scope.HandlerAuthorized,
            "checkout / checkin / heartbeat keyed by documentId. Mutates lock state on a document row, so "
            + "the decision is per-document and lives with the lock logic."),

        new GovernedFile("Api/ComposeMountEndpoints.cs", Scope.HandlerAuthorized,
            "POST /upload and POST /project. /upload is content CREATION (no prior document). /project is "
            + "the stateless projection endpoint — it renders bytes the caller supplies and persists "
            + "nothing, so there is no stored resource to authorize against."),

        new GovernedFile("Api/ComposeTemplateEndpoints.cs", Scope.HandlerAuthorized,
            "POST /documents/{documentSpeId}/apply-template — writes template content into an existing "
            + "document; authorized against that document in the handler."),

        new GovernedFile("Api/ComposeSyncEndpoints.cs", Scope.HandlerAuthorized,
            "POST /document/{documentSpeId}/check-changes (authenticated etag poll) AND the ONE anonymous "
            + "route in the Compose surface: POST /api/compose/webhooks/spe-doc-changed, registered on "
            + "`routes` not `group`, .AllowAnonymous() because Graph's own contract sends the validation "
            + "handshake and notifications unauthenticated. It is not ungoverned: HMAC-SHA256 over the raw "
            + "body via RequireWebhookSignature + constant-time clientState in the handler, both mandatory "
            + "with no DEVELOPMENT_MODE bypass. Flagged here so a reader auditing 'which Compose routes are "
            + "anonymous' gets the answer from this list rather than from a grep."),

        new GovernedFile("Api/ComposeActiveDocumentEndpoints.cs", Scope.HandlerAuthorized,
            "POST /active-document. Takes sessionId in the BODY, so no route-level filter can see it — the "
            + "handler checks the parent session's owner directly (#863). Also enumerated in "
            + "SessionOwnershipGuardTests.BodyScopedSessionRoutes; the two lists answer different questions "
            + "and both need the entry."),
    };

    // =============================================================================================
    // THE WAIVER LIST
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. A waiver is per-ROUTE, in code, carries a written reason, and is enumerable here. A path
    //      pattern or a naming convention is NOT a waiver; it is a hole with better manners.
    //
    //   2. Choose the kind honestly:
    //        Pending   — the route SHOULD be gated and is not yet. MUST name the owning task id. These
    //                    are a work list that shrinks to zero, not exemptions.
    //        Permanent — the route is genuinely not a per-resource decision (a create with no
    //                    pre-existing resource; a collection read whose correctness is result trimming).
    //
    //   3. A Pending waiver whose route has since become gated is STALE and fails NoWaiverIsStale. Delete
    //      it — do not leave it behind "just in case". The list cannot outlive its cause.
    //
    //   4. Never add a Permanent waiver to make a build go green. If the honest answer is "this needs a
    //      gate and nobody has written it", that is Pending with an owner, even if the owner is "unowned".
    //
    // STATE AT THE TIME OF WRITING: Wave 1 is only partially landed. Tasks 072 and 073 are not being
    // executed, and 070/071 were in flight in other sessions. The suite is GREEN only BECAUSE of the
    // Pending entries below. That is the honest state, not a passing grade.
    // =============================================================================================

    private enum WaiverKind
    {
        Pending,
        Permanent,
    }

    private sealed record Waiver(string Route, WaiverKind Kind, string OwningTask, string Reason);

    private static readonly IReadOnlyList<Waiver> Waivers = new[]
    {
        // ---------- task 072: WAIVER REMOVED 2026-08-26, route is gated ----------
        //
        // POST /api/documents/{documentId}/share-link now carries
        // .AddDocumentAuthorizationFilter("share"). Deleted rather than left behind per maintenance rule
        // 3 above — a Pending waiver whose route has become gated is STALE and fails NoWaiverIsStale.
        //
        // What 072 actually closed, for the record: the missing per-document gate (the route's authority
        // was container-scoped OBO access), the permanent lifetime (expiration: null → bounded by
        // Documents:ShareLinks, [Range]-capped so it cannot be configured back to effectively-permanent),
        // and anonymous-as-the-silent-default (now an explicit per-call request, capped harder, logged at
        // Warning with the caller's oid).
        //
        // What 072 did NOT close, deliberately: anonymous links still EXIST, because the shipped email
        // composer needs external recipients to be able to open them (email-communication-solution-r5 R2
        // item 12) and an organization-scoped link cannot do that. That residual is bounded, gated on
        // Share, and recorded in notes/task-072-gate-share-link.md — not silently accepted.

        // ---------- task 073: THREE WAIVERS REMOVED 2026-08-27, the routes are GONE ----------
        //
        // PUT /api/containers/{containerId}/files/{*path}, POST /api/containers/{containerId}/upload and
        // PUT /api/upload-session/chunk were RETIRED by task 073, which deleted Api/UploadEndpoints.cs
        // outright rather than gating it. Deleted here rather than left behind per maintenance rule 3 — and
        // note that NoWaiverIsStale could NOT have caught these on its own: it fires when a waived route
        // becomes GATED, not when it is DELETED. That gap is now closed (see NoWaiverIsStale below), which
        // makes 073 the last task that could leave dead waivers silently.
        //
        // What 073 actually closed, for the record: the routes carried RequireAuthorization("canwritefiles")
        // -> ResourceAccessRequirement -> ResourceAccessHandler — a real, fail-closed mechanism resolving
        // DOCUMENT rights from a CONTAINER id, because ExtractResourceId treats
        // containerId/driveId/documentId/id interchangeably. Wrong resource domain, not a missing
        // mechanism, which is precisely why no structural rule here could see it. Retirement removes the
        // defect and the shape together. Regression guard:
        // tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs.

        // ---------- PENDING — still live, still ungated, in DocumentsEndpoints.cs ----------
        // Re-pointed off "073" 2026-08-27: these two are NOT in the file 073 deleted, and 073's scope did
        // not extend to them. Leaving them owned by a completed task would read as done; deleting them
        // would silently un-waive two live holes — one of them a DESTROY.
        new Waiver("PUT /api/drives/{driveId}/upload", WaiverKind.Pending, "UNOWNED",
            "Drive-keyed write with the canwritefiles policy only — the same wrong-domain shape 073 retired "
            + "on the container-keyed twin, surviving here because it lives in DocumentsEndpoints.cs, "
            + "outside 073's scope. Needs an owner."),

        new Waiver("DELETE /api/drives/{driveId}/items/{itemId}", WaiverKind.Pending, "UNOWNED",
            "Drive-keyed DESTROY with the canwritefiles policy only. A destroy path is the worst case for a "
            + "wrong-domain check, and this is the route the merge plan flagged as REACHABLE via "
            + "src/dataverse/webresources/spaarke_documents/DocumentOperations.js:578, which reads "
            + "driveId/itemId off form attributes and so depends on no deleted route. Needs an owner, and "
            + "that task must FIRST resolve whether the web resource is deployed."),

        // ---------- PENDING — the OBO upload trio, re-pointed from 071 to 073/075/076 ----------
        //
        // Task 071 DELETED four routes from this group (children / PATCH / content / DELETE) — all had
        // zero callers and gated document-id-keyed equivalents already ship — so their waivers are gone
        // rather than updated. A waiver for a route that no longer exists is worse than noise: it reads
        // as unfinished work and would be carried forward forever. (NoWaiverIsStale does not catch this
        // case — it fires when a waived route becomes GATED, not when it is DELETED. Worth extending.)
        //
        // These three survived, and NOT because 071 ran out of time. They CREATE content: the wizard
        // ordering is uploadFilesToSpe THEN createDocumentRecords, so no sprk_document exists at
        // authorization time. Attaching DocumentAuthorizationFilter makes ExtractResourceId hand back a
        // CONTAINER id, which is not an sprk_documents GUID, so RetrievePrincipalAccess returns None and
        // 100% of uploads deny — across 9 Create*Wizard surfaces plus EmailComposer and
        // DocumentUploadWizard. Their authorization object is the owning RECORD, which is exactly what
        // tasks 075/076 build. Root CLAUDE.md §6.5 path A, bounded.
        //
        // Owner re-pointed "073/075/076" -> "075/076" on 2026-08-27: 073 has landed and did NOT gate these.
        // It retired the app-only container-keyed twin instead, so the OBO trio's remaining dependency is
        // 075's resolver + 076's contract change, not 073.
        //
        // 🔎 CITATION FIXED 2026-08-27: this block previously cited "ADR-008 §6.5". ADR-008 has no §6.5 —
        // §6.5 is root CLAUDE.md's ADR Conflict Resolution Protocol. Pre-existing error, corrected here.
        //
        // ⚠️ 076's rewrite to option (C) (2026-08-27) changes the DISPOSITION of all three: the first is
        // CONVERTED to a record-keyed contract and gated; the other two are DELETED, because their client
        // (Spaarke.SdapClient UploadOperation.ts:98) first calls GET /api/obo/containers/{id}/drive, which
        // is mapped NOWHERE — the chunked path is dead by 404. So all three entries below should be gone
        // when 076 lands, and none should become Permanent.
        new Waiver("PUT /api/obo/containers/{id}/files/{*path}", WaiverKind.Pending, "075/076",
            "Finding #2 — writes into a caller-named container. Authorization subject is the OWNING RECORD, "
            + "not a document that does not exist yet; 11 live call sites via EntityCreationService.ts:493. "
            + "Latent: OBO means SPE denies without a container ACL and no user holds one."),

        // ---------- task 076: TWO WAIVERS REMOVED 2026-08-27, the routes were DELETED ----------
        //
        // POST /api/obo/drives/{driveId}/upload-session and PUT /api/obo/upload-session/chunk are gone.
        // Task 076 DELETED the chunked OBO pair rather than converting it, because the path was dead:
        // its only client (Spaarke.SdapClient UploadOperation.createUploadSession) began with
        // GET /api/obo/containers/{id}/drive, which is mapped NOWHERE, so it threw on the 404 before
        // reaching either route. The chunk route was deader still — even that client PUT straight to
        // Graph's session.uploadUrl and never called it.
        //
        // Deleted rather than left behind per maintenance rule 3, and NOT converted to Permanent:
        // the routes ceased to exist, which is the forcing function working, not the rule relaxing.
        // The third member of the trio (PUT /api/obo/containers/{id}/files/{*path}) is still below —
        // it is the LIVE upload route and 076 converts rather than deletes it.

        // ---------- task 079: TWO WAIVERS REMOVED 2026-08-27, the routes were RE-KEYED and gated ----------
        //
        // GET /api/obo/drives/{driveId}/items/{itemId}/versions and .../versions/{versionId}/content are
        // gone. Task 079 re-keyed both onto the document row —
        // GET /api/documents/{documentId}/versions and .../versions/{versionId}/content, each carrying
        // .AddDocumentAuthorizationFilter("read") (DocumentVersionEndpoints.cs:133, :182) — so the SPE
        // pointer is now read off the row the caller was authorized against, rather than supplied by the
        // caller. Deleted rather than left behind per maintenance rule 3.
        //
        // 079 perturbation-proved the gates are what keeps Rule A green here, not a waiver: removing them
        // makes Rule A FAIL naming the two NEW route keys, which the old drive-keyed waivers do not cover.

        // ---------- PENDING — found by this rule; now owned by task 078 ----------
        new Waiver("GET /api/v1/containers/{containerId}/documents", WaiverKind.Pending, "078",
            "FOUND BY THIS RULE ON ITS FIRST RUN and present in no Wave 1 task. Lists the documents of an "
            + "arbitrary container id behind RequireAuthorization() alone — no filter, no resource policy. "
            + "This is the sixth miss on a surface that has been recounted four times. Owner re-pointed "
            + "UNOWNED -> 078 on 2026-08-27: the entry suggested folding it into 073, and 073 has now "
            + "landed having correctly NOT done so — this is a collection READ whose control is result "
            + "trimming against the caller's accessible-record set, which is a different mechanism from the "
            + "per-resource write gate 073 owned. Task 078 owns it and already depends on 075's "
            + "container->record mapping."),

        // ---------- PERMANENT ----------
        new Waiver("POST /api/v1/documents", WaiverKind.Permanent, "-",
            "CREATE. There is no pre-existing resource to authorize, so a per-resource filter has nothing "
            + "to check. The meaningful control is validation of the PARENT record reference in the handler, "
            + "which is a different mechanism and a different task."),

        new Waiver("GET /api/v1/documents", WaiverKind.Permanent, "-",
            "COLLECTION READ. Correctness here is RESULT TRIMMING, not a per-record gate — a per-resource "
            + "filter has no single resource to check. Trimming the collection to the caller's accessible "
            + "record set is Wave 3's subject (AccessibleRecordSetService), not a waiver-able gate."),
    };

    /// <summary>
    /// Governed routes whose only per-resource mechanism is a <c>ResourceAccessRequirement</c>-backed named
    /// policy. Pinned so a new route cannot quietly join this set: the mechanism is real and fail-closed,
    /// but its correctness depends on the route's resource key matching the mechanism's resource domain,
    /// and finding #4 is what happens when it does not.
    /// </summary>
    private static readonly IReadOnlySet<string> PolicyOnlyRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        // The three container/chunk routes were REMOVED from this set 2026-08-27 with task 073's deletion
        // of Api/UploadEndpoints.cs. This is a SEPARATE list from Waivers — which is why
        // "PUT /api/upload-session/chunk" used to appear twice in this file, and why removing it from one
        // list is not enough. TheSetOfPolicyOnlyRoutesIsPinned compares against what the scanner actually
        // finds, so a stale entry here fails as `removed`, not silently.
        "PUT /api/drives/{driveId}/upload",
        "DELETE /api/drives/{driveId}/items/{itemId}",
    };

    /// <summary>
    /// Named authorization policies backed by <c>ResourceAccessRequirement</c> (see
    /// <c>Infrastructure/DI/AuthorizationModule.cs</c>). <c>RequireAuthorization("&lt;one of these&gt;")</c>
    /// is a genuine resource decision — <c>ResourceAccessHandler</c> calls
    /// <c>AuthorizationService.AuthorizeAsync</c> and fails closed on every error path — as distinct from a
    /// bare <c>RequireAuthorization()</c>, which only asks "are you anyone?".
    /// </summary>
    private static readonly IReadOnlySet<string> ResourcePolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        "canpreviewfiles", "candownloadfiles", "canuploadfiles", "canreplacefiles",
        "canreadmetadata", "canupdatemetadata", "canlistchildren",
        "candeletefiles", "canmovefiles", "cancopyfiles", "cancreatefolders",
        "cansharefiles", "canmanagefilepermissions",
        "canviewversions", "canrestoreversions",
        "canwritefiles",
    };

    // Census: the total routing surface. A new endpoint file must be classified before the build goes
    // green. See TheEndpointFileCensusIsPinned for the maintenance procedure.
    //
    // 109 -> 111 (2026-08-26, unified-access-control-r2 task 080, on merging 285 commits of master).
    // The ratchet fired for the second time in two days and was RIGHT both times. Master added two
    // route-registering files, and classifying them is what the count exists to force:
    //
    //   Endpoints/Onboarding/ConsentCallbackEndpoint.cs
    //     MapPost + AllowAnonymous(). Legitimately anonymous — it is an external OAuth consent
    //     redirect target, so the caller cannot hold a token yet. Not a document/Dataverse route.
    //     NOT added to GovernedFiles.
    //
    //   Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs
    //     MapGet + RequireAuthorization() and nothing else. Classifying it surfaced a CROSS-TENANT
    //     READ, filed as task 081 and FIXED there (15b5dc6a3, hardened by 1a77288b0): it TOOK tenantId
    //     from the QUERY STRING and TREATED the caller's JWT `tid` claim as a mere fallback, so an
    //     authenticated caller in tenant A COULD resolve tenant B's SPE container id by passing
    //     ?tenantId={B}. The 400-vs-200 split on "tenant not served by this stamp" WAS also a
    //     tenant-enumeration oracle. Now gated on a positively-classified app-only caller AND an
    //     operator allow-list, denying before any resolver call; the `tid` fallback is gone. Its own
    //     doc comment claimed "parity with all other BFF endpoints" for auth and it carried a
    //     Placement Justification, so it passed review with the defect in it.
    //     NOT added to GovernedFiles — this guard's governed scope is per-DOCUMENT/record
    //     authorization, and a tenant-scoping defect is a different class. Task 081 owns the fix;
    //     forcing it in here would blur what Rule A means.
    //
    // Both files are outside the governed set, so this is a pure count bump — which is exactly the
    // outcome the census is designed to make deliberate rather than silent.
    //
    // 111 -> 110 (2026-08-27, unified-access-control-r2, on merging tranche 1 = tasks 073 + 079).
    // A DOWNWARD move, the census's third firing and the first in the delete direction:
    //
    //   073  -1  Api/UploadEndpoints.cs DELETED (218 lines, zero additions) — all three app-only
    //            container-keyed write routes retired rather than gated.
    //   079   0  the two version routes were RE-KEYED WITHIN DocumentVersionEndpoints.cs
    //            (drive-keyed -> document-id-keyed, both gated "read"). No file added or removed, so the
    //            census cannot see this task at all — which is worth stating, because "the census did not
    //            move" is NOT evidence a task changed nothing. Rule A is what sees 079.
    //
    // Deliberately verified before bumping, rather than after: master's merge (15385bbdf) also DELETED
    // Api/Filters/WorkspaceLayoutAuthorizationFilter.cs, and that does NOT move the count — the file has
    // no Map* call, so EndpointFiles() never counted it. Two deletions, one census delta.
    //
    // 110 -> 117 (2026-08-28, spaarkeai-compose-r8, on merging master into the R8 branch). Net +7 from a
    // single decomposition, and the census's fourth firing:
    //
    //   070  -1  Api/ComposeEndpoints.cs — NOT deleted. It still exists and still owns
    //            MapGroup("/api/compose") + RequireAuthorization(); it simply no longer registers any
    //            route DIRECTLY, so EndpointFiles() (which selects on .Map{Verb}() ) stopped counting it.
    //        +8  Api/Compose{Document,Save,Annotation,Checkout,Mount,Template,Sync,ActiveDocument}
    //            Endpoints.cs ADDED — the split by reason-to-change (CLAUDE.md §11.5).
    //
    // That -1 is worth stating precisely, because "deleted" and "demoted to an aggregator" leave the same
    // footprint in the count and very different ones in reality: the group-level RequireAuthorization()
    // every one of the eight inherits is still declared in ComposeEndpoints.cs. Its GovernedFiles entry was
    // removed rather than kept because a file with zero Map{Verb} call sites passes
    // ScannerAccountsForEveryRegistrationInTheGovernedFiles vacuously (expected 0, actual 0) — an entry
    // that asserts nothing, on a list whose value is that every entry means something.
    //
    // The arithmetic is the least interesting part of this firing. ComposeEndpoints.cs was a GOVERNED file
    // (HandlerAuthorized). Bumping 110 -> 117 and stopping would have left the count green while every
    // Compose route sat outside GovernedFiles — the guard passing BECAUSE the surface it guards had been
    // reorganized out from under it. So the split is reflected as eight GovernedFiles entries above, one
    // per file, not as a prefix rule.
    //
    // Same discipline as the 111 -> 110 note above: verified, not inferred. The eight successors are
    // exactly the delta (110 - 1 + 8 = 117 reconciles with no residue); ComposeEndpoints.cs was read to
    // confirm it survives as the aggregator rather than assumed gone from the count alone; and the
    // anonymous webhook inside ComposeSyncEndpoints.cs was read before being classified rather than
    // assumed authenticated because its siblings are.
    private const int ExpectedEndpointFileCount = 117;

    // =============================================================================================
    // RULE A — every governed route carries a per-resource decision, or a named waiver
    // =============================================================================================

    [Fact(DisplayName = "Task 074 Rule A: every governed route carries per-resource authorization or a named waiver")]
    public void EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver()
    {
        var waived = Waivers.Select(w => w.Route).ToHashSet(StringComparer.Ordinal);
        var ungated = new List<string>();
        var unparseable = new List<string>();

        foreach (var file in GovernedFiles.Where(f => f.Scope == Scope.RouteLevelGate))
        {
            foreach (var route in ScanFile(file.RelativePath))
            {
                if (route.Unparseable)
                {
                    // FAIL-CLOSED. A registration the scanner cannot read is the helper-wrapped blind spot
                    // the task warned about. It is never skipped.
                    unparseable.Add($"{file.RelativePath}:{route.Line}: could not resolve the registration chain");
                    continue;
                }

                if (route.Mechanism != AuthMechanism.None || waived.Contains(route.Key))
                {
                    continue;
                }

                ungated.Add($"{route.Key}\n      at {file.RelativePath}:{route.Line}");
            }
        }

        Assert.True(
            unparseable.Count == 0,
            "The route scanner could not resolve one or more registration chains. This is treated as a "
            + "FAILURE rather than a skip, on purpose: an unreadable registration is exactly the "
            + "helper-wrapped blind spot that would otherwise let an ungated route through silently.\n\n"
            + "Fix the scanner (or reformat the registration so a fluent chain is readable) — do not narrow "
            + "the scope.\n\n" + string.Join("\n", unparseable));

        Assert.True(
            ungated.Count == 0,
            "These BFF routes serve document metadata or file bytes and carry NO per-resource authorization "
            + "decision — only RequireAuthorization() (\"are you anyone?\"), AllowAnonymous, or rate "
            + "limiting.\n\n"
            + "REMEDY — pick one, in this order of preference:\n"
            + "  1. Add a per-resource filter to the registration chain:\n"
            + "         .AddDocumentAuthorizationFilter(\"read\" | \"write\" | \"delete\")\n"
            + "     That routes through AuthorizationService -> RetrievePrincipalAccess and fails closed.\n"
            + "  2. If the route should not exist under broker-only, DELETE it. That was task 071's\n"
            + "     preferred outcome for the OBO drive-keyed routes.\n"
            + "  3. If it genuinely needs no per-resource decision (a create with no pre-existing\n"
            + "     resource; a collection read whose control is result trimming), add a Waiver entry in\n"
            + "     this file with a written reason — Permanent, or Pending WITH the owning task id.\n\n"
            + "Do NOT make this pass by removing the file from GovernedFiles. Narrowing the scope to make a\n"
            + "hole disappear is the failure this guard exists to prevent.\n\n"
            + "Ungated routes:\n    " + string.Join("\n    ", ungated));
    }

    [Fact(DisplayName = "Task 074 Rule A: every waiver carries a reason, and every Pending waiver names its owning task")]
    public void EveryWaiverCarriesAReasonAndPendingWaiversNameTheirOwningTask()
    {
        // The waiver list is the part of this mechanism that decays. An unexplained exemption is
        // indistinguishable from an oversight six months later, and a Pending waiver with no owner is how a
        // temporary hole becomes permanent. Enforced rather than trusted.
        var unexplained = Waivers
            .Where(w => string.IsNullOrWhiteSpace(w.Reason) || w.Reason.Trim().Length < 60)
            .Select(w => w.Route)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every waiver must carry a substantive written reason — a sentence a reviewer two years from "
            + "now can evaluate. \"Legacy\" is not a reason; \"needed for now\" is not a reason. Entries "
            + "missing one:\n  " + string.Join("\n  ", unexplained));

        var ownerless = Waivers
            .Where(w => w.Kind == WaiverKind.Pending && string.IsNullOrWhiteSpace(w.OwningTask))
            .Select(w => w.Route)
            .ToList();

        Assert.True(
            ownerless.Count == 0,
            "Every PENDING waiver must name the task that will remove it, so the list reads as a work list "
            + "that shrinks to zero rather than a set of exemptions. Use \"UNOWNED\" only when the route was "
            + "found by this rule and has not been assigned yet. Ownerless:\n  "
            + string.Join("\n  ", ownerless));

        Assert.True(
            Waivers.Select(w => w.Route).Distinct(StringComparer.Ordinal).Count() == Waivers.Count,
            "Duplicate waiver routes — two entries for one route means one of them is unreviewed.");

        // Rule B's two exemption lists decay the same way and are held to the same bar.
        var thinFilterReasons = ClaimOnlyFilters.Concat(KnownDecorativeFilters)
            .Where(e => string.IsNullOrWhiteSpace(e.Value) || e.Value.Trim().Length < 60)
            .Select(e => e.Key)
            .ToList();

        Assert.True(
            thinFilterReasons.Count == 0,
            "Every ClaimOnlyFilters / KnownDecorativeFilters entry must carry a substantive written reason. "
            + "An unexplained exemption on Rule B is how a decorative filter gets in — which is the defect "
            + "Rule B exists to catch. Entries missing one:\n  " + string.Join("\n  ", thinFilterReasons));

        Assert.True(
            ClaimOnlyFilters.Keys.Intersect(KnownDecorativeFilters.Keys, StringComparer.Ordinal).ToList() is { Count: 0 },
            "A filter cannot be both legitimately claim-only (Permanent) and known-decorative debt "
            + "(Pending). Pick one — the distinction is the whole point of splitting the lists.");
    }

    [Fact(DisplayName = "Task 074 Rule A: no waiver is stale — a gated route's waiver must be deleted")]
    public void NoWaiverIsStale()
    {
        // This is what makes the waiver list SELF-LIQUIDATING. When task 071/072/073 lands a gate, the
        // corresponding waiver stops being true, and leaving it behind would quietly re-widen the rule for
        // that route forever. So a waiver on a now-gated route FAILS, and the remedy is to delete it.
        //
        // TWO ways a waiver goes stale. The rule originally caught only the first.
        //
        //   (1) GATED  — the route still exists and now carries a filter. Caught since task 074.
        //   (2) ABSENT — the route no longer exists at all, because a task DELETED or RE-KEYED it.
        //
        // Case (2) was added 2026-08-27, and it was added because THREE separate tasks each left dead
        // waivers this rule structurally could not see:
        //   · 071 deleted four OBO read/mutate routes  (spotted by hand; its §6a asked for this rule)
        //   · 073 deleted Api/UploadEndpoints.cs       (spotted by hand; three dead waivers)
        //   · 079 re-keyed two version routes onto the document row (spotted by hand; two dead waivers)
        // Three for three by hand is not a process — the same miss found by inspection each time is the
        // definition of a check that should be mechanical. A waiver for a route that does not exist is
        // worse than noise: it reads as unfinished work and gets carried forward forever, and it inflates
        // the apparent size of the remaining work list.
        var gated = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);

        // `present` deliberately scans BOTH scopes. Rule A only applies to RouteLevelGate, but a waiver
        // naming a route in a HandlerAuthorized file is still a waiver for a route that EXISTS, and
        // reporting it as absent would be a false accusation of deadness.
        foreach (var file in GovernedFiles)
        {
            foreach (var route in ScanFile(file.RelativePath))
            {
                if (route.Unparseable || string.IsNullOrEmpty(route.Key))
                {
                    continue;
                }

                present.Add(route.Key);

                if (file.Scope == Scope.RouteLevelGate && route.Mechanism == AuthMechanism.Filter)
                {
                    gated.Add(route.Key);
                }
            }
        }

        var stale = Waivers
            .Where(w => w.Kind == WaiverKind.Pending && gated.Contains(w.Route))
            .Select(w => $"{w.Route} (owner: task {w.OwningTask})")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These routes now carry a per-resource authorization filter, so their PENDING waivers are "
            + "stale. DELETE the waiver entries from this file — that deletion is how the Wave 1 work list "
            + "visibly shrinks toward zero. Leaving them behind silently re-widens the rule for those "
            + "routes.\n\n  " + string.Join("\n  ", stale));

        // Applies to Permanent as well as Pending: a Permanent waiver can outlive its route too, and it is
        // the likelier of the two to go unnoticed, precisely because nobody is waiting for it to shrink.
        var absent = Waivers
            .Where(w => !present.Contains(w.Route))
            .Select(w => $"{w.Route} ({w.Kind}, owner: task {w.OwningTask})")
            .ToList();

        Assert.True(
            absent.Count == 0,
            "These waivers name routes that NO LONGER EXIST in any governed file. A task deleted or "
            + "re-keyed the route and left its waiver behind. DELETE the entries — and if the route was "
            + "RE-KEYED rather than deleted, check whether the NEW key needs a gate or a waiver of its own "
            + "before you delete, because this rule cannot tell those two cases apart.\n\n"
            + "Note: this fires only for routes absent from the GOVERNED file set. If a route moved to a "
            + "file that is not in GovernedFiles, that is a census/classification problem — fix the "
            + "classification, do not delete the waiver to make this green.\n\n  "
            + string.Join("\n  ", absent));
    }

    [Fact(DisplayName = "Task 074 Rule A: the set of policy-only governed routes is pinned")]
    public void TheSetOfPolicyOnlyRoutesIsPinned()
    {
        // A ResourceAccessRequirement policy IS a real resource decision, so Rule A accepts it. But finding
        // #4 is a route whose policy authorizes a CONTAINER id against DOCUMENT rights, which no structural
        // rule can see. Pinning the set means a NEW route cannot quietly join the one category whose
        // correctness this guard cannot verify.
        var actual = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in GovernedFiles.Where(f => f.Scope == Scope.RouteLevelGate))
        {
            foreach (var route in ScanFile(file.RelativePath))
            {
                if (!route.Unparseable && route.Mechanism == AuthMechanism.ResourcePolicy)
                {
                    actual.Add(route.Key);
                }
            }
        }

        var added = actual.Except(PolicyOnlyRoutes, StringComparer.Ordinal).ToList();
        var removed = PolicyOnlyRoutes.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(
            added.Count == 0,
            "New governed route(s) rely on a named resource POLICY as their only per-resource mechanism. "
            + "That category is pinned because its correctness depends on the route's resource key matching "
            + "the policy handler's resource domain — ResourceAccessHandler.ExtractResourceId accepts "
            + "containerId / driveId / documentId / id INTERCHANGEABLY and resolves document rights from all "
            + "of them. Confirm the key matches the domain, then add the route to PolicyOnlyRoutes.\n\n  "
            + string.Join("\n  ", added));

        Assert.True(
            removed.Count == 0,
            "Route(s) left the policy-only set — good if they gained a filter (delete them from "
            + "PolicyOnlyRoutes AND delete their waiver), bad if the route was renamed without review:\n\n  "
            + string.Join("\n  ", removed));
    }

    // =============================================================================================
    // RULE B — an authorization filter must actually decide something
    // =============================================================================================

    [Fact(DisplayName = "Task 074 Rule B: no *AuthorizationFilter is decorative — each must consult an authorization decision service")]
    public void NoAuthorizationFilterIsDecorative()
    {
        // THIS IS THE RULE THAT CATCHES FINDING #1, and it exists because that finding was NOT a missing
        // filter. SemanticSearchAuthorizationFilter was attached to /api/ai/search from the start
        // (commit fbe0fcdb9). Its only constructor argument was ILogger, it referenced no authorization or
        // access service, and every branch of ValidateScopeAuthorization returned
        // `new AuthorizationResult(true, null)` — including `case SearchScope.All`. It produced an AUDIT
        // LOG, not a decision, while looking exactly like a gate at the call site.
        //
        // The generalisation is precise and checkable: a type named *AuthorizationFilter that consults no
        // authorization decision service decides nothing.
        var violations = new List<string>();

        foreach (var file in AuthorizationFilterFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (ClaimOnlyFilters.ContainsKey(name) || KnownDecorativeFilters.ContainsKey(name))
            {
                continue;
            }

            var code = Decomment(File.ReadAllText(file));
            if (DecisionServices.Any(s => code.Contains(s, StringComparison.Ordinal)))
            {
                continue;
            }

            // THE PAIR MAY DECIDE (extended 2026-08-26 by task 077).
            //
            // A filter runs BEFORE the handler, so it cannot authorize rows that do not exist yet. Where
            // the subject of authorization IS the result set — record search, and document search under
            // scope=all — the only honest split is: the filter authorizes the request and publishes the
            // obligation, and the ENDPOINT authorizes the rows. Rule B's original per-file question
            // ("does this filter consult a decision service?") cannot express that, and the first version
            // of this rule therefore had to park RecordSearchAuthorizationFilter in
            // KnownDecorativeFilters even after it was fixed.
            //
            // Two ways to satisfy the rule were considered. Making the filter wrap next() and rewrite the
            // handler's result WOULD satisfy the per-file form — and would fail OPEN the moment the
            // handler's result shape changed, because the filter's pattern-match would silently stop
            // matching. Enforcing in the endpoint fails CLOSED: the handler refuses outright when the
            // published obligation is absent. Rule B must not push a design toward the fail-open option,
            // so the rule widened instead of the code narrowing.
            //
            // The invariant enforced here is therefore "the filter/endpoint PAIR decides": a filter with
            // no decision service of its own is acceptable only if EVERY endpoint file that attaches it
            // consults one. A decorative filter attached to a decorative endpoint still fails, which is
            // the shape that mattered.
            var attachingEndpoints = EndpointFilesAttaching(name);
            if (attachingEndpoints.Count > 0
                && attachingEndpoints.All(endpointFile =>
                    DecisionServices.Any(s =>
                        Decomment(File.ReadAllText(endpointFile)).Contains(s, StringComparison.Ordinal))))
            {
                continue;
            }

            violations.Add(
                $"{SourceScan.Relative(file)}: references none of the authorization decision services"
                + (attachingEndpoints.Count == 0
                    ? " (and no endpoint file attaching it was found)"
                    : ", and neither do all of the endpoint files attaching it: "
                      + string.Join(", ", attachingEndpoints.Select(SourceScan.Relative))));
        }

        Assert.True(
            violations.Count == 0,
            "These types are named *AuthorizationFilter but consult NO authorization decision service. A "
            + "filter that consults nothing decides nothing — it produces an audit log while reading as a "
            + "gate at the call site. That is precisely what SemanticSearchAuthorizationFilter was on "
            + "/api/ai/search: every scope, including scope=all, returned allow.\n\n"
            + "REMEDY: consult one of " + string.Join(" / ", DecisionServices) + " and return a 403 on "
            + "deny; or, if the filter legitimately decides from claims or a signature alone, add it to "
            + "ClaimOnlyFilters WITH a written reason.\n\n"
            + string.Join("\n", violations));
    }

    /// <summary>
    /// Filters that legitimately reach a decision without an authorization service — claims-only or
    /// signature-only. Each carries a reason, for the same purpose as the waiver reasons: an unexplained
    /// exemption here is how a decorative filter gets in.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ClaimOnlyFilters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TenantAuthorizationFilter"] =
                "Decides tenant boundary from the 'tid' claim alone. The tenant is IN the token; there is "
                + "no record to look up, and a Dataverse round trip could not make the answer more true.",
            ["CallerPrincipalAuthorizationFilter"] =
                "Resolves and validates the caller principal (the contact-plane identity) as a PRECONDITION "
                + "for the per-record checks that run after it. It establishes who is asking; the record "
                + "decision is a separate downstream step by design.",
            ["SpeAdminTenantScopeFilter"] =
                "Scopes SPE-admin operations to the caller's consuming tenant from claims. Tenant scoping, "
                + "not per-record authorization — the record decision is SpeAdminAuthorizationFilter's job.",
            ["AgentAuthorizationFilter"] =
                "M365 Copilot gateway (/api/agent/*). Asserts a resolvable oid AND tid on the inbound agent "
                + "token and denies without either. Identity precondition for a gateway, not a document "
                + "route — it serves no document metadata or bytes, so it is outside Rule A's subject too.",
            ["CommunicationAuthorizationFilter"] =
                "Gates SENDING a communication, not reading a record. Its own summary is explicit that "
                + "Phase 1 permits any authenticated user with a valid oid, so it does not misrepresent "
                + "itself as a per-resource gate. Per-record scoping is ICommunicationAccessFilter's job, "
                + "which is a separate seam already in DecisionServices.",
            ["WorkspaceAuthorizationFilter"] =
                "Resolves the caller's oid, denies 401 when absent, and stashes it in HttpContext.Items so "
                + "handlers do not repeat claim extraction. A claim-resolution precondition by construction "
                + "— naming it *AuthorizationFilter overstates it, but it decides the one thing it claims.",
            ["RegistrationAuthorizationFilter"] =
                "Demo-request approval surface (/api/registration/*). Decides from identity + role claims on "
                + "the token; there is no Dataverse record to authorize against at submission time, and the "
                + "endpoints serve no document or matter content.",
            ["SpeAdminAuthorizationFilter"] =
                "SPE administrative surface. Decides from admin role claims rather than per-record rights, "
                + "because the resources are CONTAINERS and container types, which are not Dataverse rows "
                + "and have no RetrievePrincipalAccess answer.",
            ["ReportingAuthorizationFilter"] =
                "Power BI embed surface. Decides from role claims checked against a configured privilege "
                + "list (IConfiguration), not from record rights — a report is not a Dataverse row. "
                + "Workstream D is deferred (ADR-028 DEF-001), so this surface is not live.",
        };

    /// <summary>
    /// Filters that consult no decision service and SHOULD — recorded as debt with an owner rather than
    /// excused. Same Pending semantics as the route waivers: this list is a work list that shrinks to zero.
    ///
    /// <para><b>Rule B found this one itself.</b> It is not from any finding list.</para>
    /// </summary>
    /// <remarks>
    /// EMPTY, as of 2026-08-26 (task 077) — and the entry it used to hold is worth remembering.
    ///
    /// <c>RecordSearchAuthorizationFilter</c> sat here from the day Rule B first ran, with the note
    /// "FOUND BY RULE B, in no finding list … Delete this entry when it does." That is the whole argument
    /// for Rule B in one line: the filter WAS attached to <c>POST /api/ai/search/records</c>, so Rule A
    /// classified the route as GATED, and four separate hand enumerations of this surface agreed. Only
    /// the question "does the filter consult a decision service?" saw through it.
    ///
    /// Task 077 made that filter authorize (caller identity, OBO token, and that every requested record
    /// type is one whose access can be evaluated) and moved row authorization into the endpoint, which
    /// now refuses outright when the published obligation is absent. Rule B was widened in the same
    /// change to accept a deciding filter/endpoint PAIR, so this entry was deleted rather than reworded —
    /// the distinction the task file insisted on.
    ///
    /// Keep this dictionary EMPTY. An entry here is a route serving content behind a filter that decides
    /// nothing; it should be a work item with an owner, not a permanent exemption.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownDecorativeFilters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The types that constitute "an authorization decision" in this codebase. A filter referencing any of
    /// them is consulting a real decision path; one referencing none of them is not.
    /// </summary>
    private static readonly string[] DecisionServices =
    {
        "AuthorizationService",          // Spaarke.Core.Auth — the canonical evaluator
        "IAccessDataSource",             // the Dataverse rights resolver behind it
        "AccessRights",                  // the rights enum — a filter comparing rights is deciding
        "IAccessibleRecordSetService",   // the accessible-record-set gate (external / contact plane)
        "RetrievePrincipalAccess",       // the impersonated Dataverse call
        "IDataverseAccessGrantService",  // explicit grants
        "ICommunicationAccessFilter",    // the communication-scoped decision seam

        // Added after running Rule B against the real 30-filter surface. Both of these ARE genuine
        // decisions and would otherwise have been false positives — the outcome the task's zero-false-
        // positive constraint demands be reached by widening the definition, not by waiving real gates:
        "IDataversePrivilegeChecker",    // DataverseAuthorizationFilter: HasReadPrivilegeAsync / GetReadableEntitiesAsync
        "WorkspaceLayoutService",        // WorkspaceLayoutAuthorizationFilter: GetLayoutByIdAsync(layoutId, userId) — ownership-scoped lookup, denies on mismatch
    };

    // =============================================================================================
    // CENSUS — the anti-drift ratchet (the task's mechanism (c))
    // =============================================================================================

    [Fact(DisplayName = "Task 074 census: the set of BFF endpoint files is pinned, so a new route surface must be classified")]
    public void TheEndpointFileCensusIsPinned()
    {
        // MAINTENANCE PROCEDURE. This count going up means a NEW file registers HTTP routes. Before
        // changing the number, decide which scope the file belongs to:
        //
        //   - serves document metadata / file bytes, gated per route  -> add to GovernedFiles as
        //                                                                Scope.RouteLevelGate
        //   - serves that content but authorizes in the handler       -> add as Scope.HandlerAuthorized
        //                                                                WITH the reason
        //   - serves neither                                          -> nothing to add; just bump the count
        //
        // This is the belt-and-braces half of the mechanism, and it is aimed at the one thing a written
        // scope boundary cannot defend itself against: a new route group appearing outside it. Cheap, and
        // hard to fool.
        var files = EndpointFiles().ToList();

        Assert.True(
            files.Count == ExpectedEndpointFileCount,
            $"The number of BFF files registering HTTP routes changed: expected "
            + $"{ExpectedEndpointFileCount}, found {files.Count}.\n\n"
            + "If a file was ADDED, classify it before bumping the count — see the maintenance procedure in "
            + "this test. A new document/Dataverse-content route surface that is not added to GovernedFiles "
            + "is outside the guard, which is exactly how the sixth miss "
            + "(GET /api/v1/containers/{containerId}/documents) stayed invisible through four recounts.\n\n"
            + "If a file was REMOVED, bump the count down and delete any waivers that pointed into it.");

        // And the governed files must actually exist — a renamed file would otherwise silently drop out of
        // scope while every assertion above still passed vacuously.
        var missing = GovernedFiles
            .Where(f => !File.Exists(Path.Combine(BffRoot, f.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
            .Select(f => f.RelativePath)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Governed file(s) not found on disk. If a file was renamed, update GovernedFiles rather than "
            + "deleting the entry — the routes still exist and the invariant still holds:\n  "
            + string.Join("\n  ", missing));
    }

    // =============================================================================================
    // CONTROLS — the retroactive proof, per tests/CLAUDE.md's authoring rules for this KEEP path
    // =============================================================================================

    [Fact(DisplayName = "Task 074 negative control: the detector fires on each historical miss, reintroduced as source")]
    public void Detector_NegativeControl_FiresOnEachHistoricalMiss()
    {
        // RETROACTIVE VALIDATION. A rule not shown to catch the known misses is unvalidated. Each case
        // below is the registration as it actually stood when the miss was live — reintroduced as source so
        // the proof runs on every build instead of living in a note.

        // Miss #3 — POST /api/documents/{documentId}/share-link. The one route in FileAccessEndpoints.cs
        // with no .AddDocumentAuthorizationFilter, on a group whose RequireAuthorization() asked only "are
        // you anyone?".
        var shareLink = ScanText("Api/FileAccessEndpoints.cs", new[]
        {
            "        var docs = app.MapGroup(\"/api/documents\").RequireAuthorization();",
            "        docs.MapPost(\"/{documentId}/share-link\", CreateShareLink)",
            "            .WithName(\"CreateDocumentShareLink\")",
            "            .Produces<ShareLinkResponse>(StatusCodes.Status200OK);",
        });
        Assert.Single(shareLink);
        Assert.Equal(AuthMechanism.None, shareLink[0].Mechanism);
        Assert.Equal("POST /api/documents/{documentId}/share-link", shareLink[0].Key);

        // Miss #2 — an OBOEndpoints drive-keyed route. Byte read on an arbitrary drive item, carrying only
        // rate limiting and RequireAuthorization().
        var obo = ScanText("Api/OBOEndpoints.cs", new[]
        {
            "        app.MapGet(\"/api/obo/drives/{driveId}/items/{itemId}/content\", async (",
            "            string driveId, string itemId, SpeFileStore store) =>",
            "        {",
            "            var stream = await store.DownloadContentAsUserAsync(driveId, itemId);",
            "            return TypedResults.Stream(stream);",
            "        }).RequireRateLimiting(\"graph-read\").RequireAuthorization();",
        });
        Assert.Single(obo);
        Assert.Equal(AuthMechanism.None, obo[0].Mechanism);
        Assert.Equal("GET /api/obo/drives/{driveId}/items/{itemId}/content", obo[0].Key);

        // Miss #4's SHAPE — the container upload. The detector must classify it ResourcePolicy, NOT None
        // and NOT Filter: the policy is real and fail-closed, and the defect is that it authorizes a
        // container id against document rights. Asserting the honest classification is the point — a
        // detector that called this "gated" would be lying, and one that called it "ungated" would be
        // wrong about the mechanism.
        var upload = ScanText("Api/UploadEndpoints.cs", new[]
        {
            "        app.MapPut(\"/api/containers/{containerId}/files/{*path}\", async (",
            "            string containerId, string path, HttpRequest req) =>",
            "        {",
            "            return TypedResults.Ok();",
            "        })",
            "        .RequireAuthorization(\"canwritefiles\");",
        });
        Assert.Single(upload);
        Assert.Equal(AuthMechanism.ResourcePolicy, upload[0].Mechanism);
        Assert.Equal("PUT /api/containers/{containerId}/files/{*path}", upload[0].Key);

        // The sixth miss this rule found itself — container-keyed document listing behind
        // RequireAuthorization() alone.
        var containerDocs = ScanText("Api/DataverseDocumentsEndpoints.cs", new[]
        {
            "        app.MapGet(\"/api/v1/containers/{containerId}/documents\", async (",
            "            string containerId, IDocumentDataverseService svc) =>",
            "        {",
            "            return TypedResults.Ok(await svc.ListAsync(containerId));",
            "        })",
            "        .RequireAuthorization();",
        });
        Assert.Single(containerDocs);
        Assert.Equal(AuthMechanism.None, containerDocs[0].Mechanism);

        // Miss #1 — /api/ai/search. NOT detectable by Rule A: the route DID carry a filter. Asserting that
        // here, rather than omitting the case, keeps the boundary of Rule A honest and stops a future
        // reader assuming this rule covered all four.
        var search = ScanText("Api/Ai/SemanticSearchEndpoints.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/ai/search\").RequireAuthorization();",
            "        group.MapPost(\"/\", Search)",
            "            .AddSemanticSearchAuthorizationFilter()",
            "            .Produces<SemanticSearchResponse>(StatusCodes.Status200OK);",
        });
        Assert.Single(search);
        Assert.Equal(AuthMechanism.Filter, search[0].Mechanism);
    }

    [Fact(DisplayName = "Task 074 negative control: Rule B fires on the decorative filter that made /api/ai/search a hole")]
    public void RuleB_NegativeControl_FiresOnADecorativeFilter()
    {
        // The filter as it actually stood: one ILogger dependency, allow from every branch, no
        // authorization service anywhere. This is miss #1's retroactive proof.
        const string decorative = """
            public class SemanticSearchAuthorizationFilter : IEndpointFilter
            {
                private readonly ILogger<SemanticSearchAuthorizationFilter>? _logger;
                public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                {
                    var userTenantId = context.HttpContext.User.FindFirst("tid")?.Value;
                    if (string.IsNullOrEmpty(userTenantId)) return Results.Problem(statusCode: 401);
                    return await next(context);
                }
                private AuthorizationResult ValidateScopeAuthorization(SemanticSearchRequest r, string t)
                {
                    switch (r.Scope)
                    {
                        case SearchScope.All: return new AuthorizationResult(true, null);
                        default: return new AuthorizationResult(true, null);
                    }
                }
            }
            """;

        Assert.False(
            DecisionServices.Any(s => Decomment(decorative).Contains(s, StringComparison.Ordinal)),
            "Rule B must flag a filter that consults no authorization decision service — this is the "
            + "/api/ai/search filter verbatim in shape.");

        // Positive half: the SANCTIONED shape must NOT be flagged. A guard that flags DocumentAuthorization
        // Filter would be pushing code away from the very gate it exists to require.
        const string sanctioned = """
            public class DocumentAuthorizationFilter : IEndpointFilter
            {
                private readonly AuthorizationService _authorizationService;
                public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
                {
                    var result = await _authorizationService.AuthorizeAsync(authContext);
                    if (!result.IsAllowed) return Results.Problem(statusCode: 403);
                    return await next(c);
                }
            }
            """;

        Assert.True(
            DecisionServices.Any(s => Decomment(sanctioned).Contains(s, StringComparison.Ordinal)),
            "Rule B must NOT flag DocumentAuthorizationFilter — it consults AuthorizationService.");

        // And the detector must not be satisfied by PROSE. Every filter in this codebase discusses
        // authorization in its doc comment; a detector that read its own documentation as evidence of a
        // decision would pass on a filter that does nothing — the exact defect being hunted.
        const string prosePassingAsEvidence = """
            /// <summary>Validates access via AuthorizationService and AccessRights.</summary>
            // Future: consult IAccessDataSource / RetrievePrincipalAccess for per-document rights.
            public class FutureAuthorizationFilter : IEndpointFilter
            {
                private readonly ILogger _logger;
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate n) => n(c);
            }
            """;

        Assert.False(
            DecisionServices.Any(s => Decomment(prosePassingAsEvidence).Contains(s, StringComparison.Ordinal)),
            "Rule B must not accept a doc comment or a \"Future:\" note as evidence of a decision. The real "
            + "SemanticSearchAuthorizationFilter carried exactly such notes ('Future enhancements: "
            + "Document-level authorization') while deciding nothing.");
    }

    [Fact(DisplayName = "Task 074 positive control: the detector does not fire on the gated routes it protects")]
    public void Detector_PositiveControl_DoesNotFireOnGatedRoutes()
    {
        // A guard that flags the sanctioned shape gets deleted rather than obeyed. These are the two
        // gated forms actually in the codebase, plus the group-level-filter form, plus an explicitly
        // anonymous route.
        var gated = ScanText("Api/FileAccessEndpoints.cs", new[]
        {
            "        var docs = app.MapGroup(\"/api/documents\").RequireAuthorization();",
            "        docs.MapGet(\"/{documentId}/download\", GetDownload)",
            "            .AddDocumentAuthorizationFilter(\"read\")",
            "            .WithName(\"GetDocumentDownload\");",
            "        docs.MapPost(\"/bulk\", Bulk)",
            "            .AddEndpointFilter<BulkDownloadAuthorizationFilter>()",
            "            .WithName(\"Bulk\");",
        });
        Assert.Equal(2, gated.Count);
        Assert.All(gated, r => Assert.Equal(AuthMechanism.Filter, r.Mechanism));

        // Group-level filter must be inherited by its routes — DocumentOperationsEndpoints and the
        // external-access group both rely on this, and a rule that ignored group-level chains would
        // report a wall of false positives on correct code.
        var groupFiltered = ScanText("Api/Fake/GroupFiltered.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/thing\")",
            "            .RequireAuthorization()",
            "            .AddEndpointFilter<EntityAccessFilter>();",
            "        group.MapGet(\"/{id}\", Get);",
        });
        Assert.Single(groupFiltered);
        Assert.Equal(AuthMechanism.Filter, groupFiltered[0].Mechanism);

        // An explicitly anonymous route is Anonymous, not None — /ping and /healthz must never be reported
        // as ungated document routes.
        var anon = ScanText("Api/Fake/Health.cs", new[]
        {
            "        app.MapGet(\"/ping\", () => Results.Text(\"pong\")).AllowAnonymous();",
        });
        Assert.Single(anon);
        Assert.Equal(AuthMechanism.Anonymous, anon[0].Mechanism);

        // A comment that MENTIONS the filter must not be mistaken for the filter. FileAccessEndpoints.cs
        // has three such comments (lines 139, 169) explaining the gate; a line-scoped scan that read them
        // as evidence would mark share-link gated and the whole guard would be vacuous.
        var commentOnly = ScanText("Api/Fake/Commented.cs", new[]
        {
            "        // This route had NO per-document filter, so AddDocumentAuthorizationFilter(\"read\")",
            "        // was added by task 002. See the note above.",
            "        docs.MapPost(\"/{documentId}/share-link\", CreateShareLink)",
            "            .WithName(\"CreateDocumentShareLink\");",
        });
        Assert.Single(commentOnly);
        Assert.Equal(AuthMechanism.None, commentOnly[0].Mechanism);
    }

    [Fact(DisplayName = "Task 074: the scanner reads the real governed files and finds every registration in them")]
    public void ScannerAccountsForEveryRegistrationInTheGovernedFiles()
    {
        // Guards against the vacuous pass: every assertion above would hold if the scanner silently found
        // nothing. This pins the scanner against a mechanical count of Map{Verb} call sites taken
        // independently by regex, so a parser regression shows up as a mismatch rather than as a green run.
        var mismatches = new List<string>();

        foreach (var file in GovernedFiles)
        {
            var path = Path.Combine(BffRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var code = Decomment(File.ReadAllText(path));
            var expected = Regex.Matches(code, @"\.Map(Get|Post|Put|Patch|Delete)\s*\(").Count;
            var actual = ScanFile(file.RelativePath).Count;

            if (expected != actual)
            {
                mismatches.Add($"{file.RelativePath}: regex counted {expected} registrations, scanner returned {actual}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            "The scanner did not account for every Map{Verb} call site in a governed file. This is the "
            + "fail-closed completeness check: a registration the scanner drops is a route whose "
            + "authorization nobody is checking.\n\n" + string.Join("\n", mismatches));

        // Census: 7 → 3 → 1.
        //
        // 7 → 3: task 071 DELETED four (children / PATCH / content / DELETE — all had zero callers, and
        // gated document-id-keyed equivalents already ship). The 3 survivors were the upload trio, which
        // 071 escalated rather than gated: they CREATE content, so no sprk_document exists yet to
        // authorize against, and attaching the document filter would deny 100% of uploads across 9
        // wizards.
        //
        // 3 → 1: task 076 DELETED the chunked pair (POST .../upload-session, PUT .../upload-session/chunk)
        // on 2026-08-27. Not converted — DEAD. Their only client began with
        // GET /api/obo/containers/{id}/drive, a route mapped NOWHERE, so it threw on that 404 before
        // reaching either one; and the chunk route had no client at all, because even that client PUT
        // straight to Graph's session.uploadUrl. Both waivers were deleted with them.
        //
        // The 1 survivor is PUT /api/obo/containers/{id}/files/{*path} — the LIVE upload route, which
        // 076 CONVERTS to the record-keyed contract rather than deleting. Its waiver goes when the
        // conversion lands, and it must not become Permanent.
        //
        // Update this number when routes are added or removed here — that is the ratchet working, not a
        // nuisance. Note the retroactive-validation test above is unaffected by the deletions: it feeds
        // the scanner INLINE source text, so it still proves the rule catches finding #2's shape even
        // though that route no longer exists in the file.
        Assert.True(
            ScanFile("Api/OBOEndpoints.cs").Count == 1,
            "Expected 1 registration in OBOEndpoints.cs after task 076 deleted the dead chunked pair, "
            + "leaving only the live upload route. A drop here would make that route invisible.");
    }

    // =============================================================================================
    // MACHINERY
    // =============================================================================================

    private enum AuthMechanism
    {
        /// <summary>Only RequireAuthorization() / rate limiting — "are you anyone?". A hole on a document route.</summary>
        None,

        /// <summary>A per-resource endpoint filter, at route or group level.</summary>
        Filter,

        /// <summary>A ResourceAccessRequirement-backed named policy. Real, but see PolicyOnlyRoutes.</summary>
        ResourcePolicy,

        /// <summary>Explicitly public.</summary>
        Anonymous,
    }

    private sealed record RouteRegistration(string Key, int Line, AuthMechanism Mechanism, bool Unparseable);

    private static readonly string BffRoot =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    private static readonly Regex MapCall = new(@"\.Map(Get|Post|Put|Patch|Delete)\s*\(", RegexOptions.Compiled);

    // `var docs = app.MapGroup("/api/documents")` — receiver variable -> path prefix.
    private static readonly Regex GroupDecl =
        new(@"var\s+(\w+)\s*=\s*\w+\s*\.MapGroup\s*\(\s*""([^""]*)""", RegexOptions.Compiled);

    // A per-resource filter, in either of the two forms this codebase uses.
    private static readonly Regex FilterMarker = new(
        @"\.Add\w*AuthorizationFilter\s*[<(]"
        + @"|\.AddEndpointFilter\s*<\s*\w*(?:Authorization|Access)\w*Filter\s*>",
        RegexOptions.Compiled);

    private static readonly Regex NamedPolicy =
        new(@"\.RequireAuthorization\s*\(\s*""([^""]+)""", RegexOptions.Compiled);

    private static List<RouteRegistration> ScanFile(string relativePath)
    {
        var path = Path.Combine(BffRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Scan(relativePath, File.ReadAllText(path));
    }

    private static List<RouteRegistration> ScanText(string relativePath, IEnumerable<string> lines)
        => Scan(relativePath, string.Join("\n", lines));

    /// <summary>
    /// Extracts every route registration with its resolved path and authorization mechanism.
    ///
    /// <para><b>Statement-scoped, not line-scoped, and comment-stripped first.</b> Both matter and both
    /// were established by running this against the real files rather than by reading them: registration
    /// chains span many lines (a Produces ladder runs to a dozen), inline handler lambdas put braces and
    /// semicolons INSIDE the statement, and <c>FileAccessEndpoints.cs</c> contains prose comments naming
    /// <c>AddDocumentAuthorizationFilter</c> right above an ungated route. A line-scoped or
    /// comment-blind scan gets <c>share-link</c> wrong in one direction or the other.</para>
    /// </summary>
    private static List<RouteRegistration> Scan(string relativePath, string rawText)
    {
        var text = Decomment(rawText);
        var results = new List<RouteRegistration>();

        // Group prefixes, and the group-level chain (a group may itself carry the filter).
        var groupPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupMechanism = new Dictionary<string, AuthMechanism>(StringComparer.Ordinal);

        foreach (Match g in GroupDecl.Matches(text))
        {
            var variable = g.Groups[1].Value;
            groupPrefix[variable] = g.Groups[2].Value;
            groupMechanism[variable] = MechanismOf(StatementFrom(text, g.Index));
        }

        foreach (Match m in MapCall.Matches(text))
        {
            var verb = m.Groups[1].Value.ToUpperInvariant();
            var line = LineOf(text, m.Index);

            // The receiver is the identifier immediately before the '.'.
            var receiver = ReceiverBefore(text, m.Index);
            var statement = StatementFrom(text, m.Index);

            // The route path is the first string literal inside the Map call's parentheses.
            var pathMatch = Regex.Match(statement, @"^\s*\.Map\w+\s*\(\s*""([^""]*)""");
            if (!pathMatch.Success)
            {
                // FAIL-CLOSED: a registration whose path cannot be read is reported, never skipped.
                results.Add(new RouteRegistration($"{verb} <unresolved> ({relativePath}:{line})", line,
                    AuthMechanism.None, Unparseable: true));
                continue;
            }

            var prefix = groupPrefix.TryGetValue(receiver, out var p) ? p : string.Empty;
            var relative = pathMatch.Groups[1].Value;
            var full = Combine(prefix, relative);

            var mechanism = MechanismOf(statement);
            if (mechanism == AuthMechanism.None
                && groupMechanism.TryGetValue(receiver, out var gm)
                && gm != AuthMechanism.None)
            {
                mechanism = gm;   // group-level filters are inherited by their routes
            }

            results.Add(new RouteRegistration($"{verb} {full}", line, mechanism, Unparseable: false));
        }

        return results;
    }

    private static AuthMechanism MechanismOf(string statement)
    {
        if (FilterMarker.IsMatch(statement))
        {
            return AuthMechanism.Filter;
        }

        var policy = NamedPolicy.Match(statement);
        if (policy.Success && ResourcePolicies.Contains(policy.Groups[1].Value))
        {
            return AuthMechanism.ResourcePolicy;
        }

        if (statement.Contains(".AllowAnonymous(", StringComparison.Ordinal))
        {
            return AuthMechanism.Anonymous;
        }

        return AuthMechanism.None;
    }

    /// <summary>
    /// The registration statement beginning at <paramref name="start"/>: text up to the first <c>;</c> that
    /// sits outside every parenthesis and brace opened after that point. Inline handler lambdas therefore
    /// stay INSIDE the statement instead of truncating it, which is what makes the trailing
    /// <c>.RequireAuthorization("canwritefiles")</c> on <c>PUT /api/drives/{driveId}/upload</c> in
    /// <c>DocumentsEndpoints.cs</c> visible at all — it comes after a long lambda body. (This example named
    /// <c>UploadEndpoints.cs</c> until 2026-08-27, when task 073 deleted that file; the surviving
    /// drive-keyed route is the same shape.)
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

    /// <summary>Index just past a string literal (handles verbatim, interpolated and escaped forms).</summary>
    private static int SkipStringLiteral(string text, int quoteIndex)
    {
        // Raw string literal ("""...""") — used by the controls in this file, and cheap to handle.
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
                    i += 2;   // "" escape inside a verbatim string
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>
    /// The identifier immediately preceding the <c>.</c> at <paramref name="dotIndex"/> — the receiver of
    /// the Map call, which is how a route is tied back to its <c>MapGroup</c> prefix.
    /// </summary>
    private static string ReceiverBefore(string text, int dotIndex)
    {
        var end = dotIndex;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return text[start..end];
    }

    private static string Combine(string prefix, string relative)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return relative;
        }

        if (relative is "" or "/")
        {
            return prefix;
        }

        return prefix.TrimEnd('/') + "/" + relative.TrimStart('/');
    }

    /// <summary>
    /// The text with comments replaced by spaces, LINE STRUCTURE AND OFFSETS PRESERVED so a match index
    /// still maps to a line number. String literals are left intact — blanking a <c>//</c> inside a URL in
    /// a <c>WithDescription</c> would corrupt the statement.
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

    /// <summary>Every BFF file that registers HTTP routes — the census subject.</summary>
    private static IEnumerable<string> EndpointFiles()
        => Directory
            .EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => MapCall.IsMatch(Decomment(File.ReadAllText(f))))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Every <c>*AuthorizationFilter.cs</c> under the BFF — Rule B's subject.</summary>
    private static IEnumerable<string> AuthorizationFilterFiles()
        => Directory
            .EnumerateFiles(BffRoot, "*AuthorizationFilter.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// Every non-filter source file that ATTACHES the named filter via its
    /// <c>Add{FilterName}()</c> extension. Used by Rule B to ask whether the filter/endpoint pair
    /// reaches a decision when the filter alone does not.
    /// </summary>
    /// <remarks>
    /// Matches on the generated extension-method name rather than the type name, because the type name
    /// also appears in <c>using</c>s, logger generics and doc comments — a match on those would let a
    /// filter be "attached" by any file that merely mentions it. <c>*AuthorizationFilter.cs</c> files are
    /// excluded so a filter's own extension class does not count as its attaching endpoint.
    ///
    /// Returning EMPTY is a violation, not a pass: a filter nothing attaches is either dead code or
    /// evidence that the attachment convention changed, and both deserve a look.
    /// </remarks>
    private static IReadOnlyList<string> EndpointFilesAttaching(string filterTypeName)
    {
        var attachCall = $"Add{filterTypeName}(";

        return Directory
            .EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.EndsWith("AuthorizationFilter.cs", StringComparison.Ordinal))
            .Where(f => Decomment(File.ReadAllText(f)).Contains(attachCall, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}
