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
/// carries a real, fail-closed <c>ResourceAccessRequirement</c> policy whose handler authorizes a CONTAINER
/// id against DOCUMENT rights — a wrong-resource-domain defect that no structural rule detects without
/// hard-coding that one mismatch. It is owned behaviourally by task 073 and pinned here in
/// <see cref="PolicyOnlyRoutes"/> so its mechanism cannot change silently. See
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
            "drive-keyed version history and prior-version BYTES. Same shape as the OBO routes."),

        new GovernedFile("Api/DocumentsEndpoints.cs", Scope.RouteLevelGate,
            "drive-keyed upload and item delete."),

        new GovernedFile("Api/UploadEndpoints.cs", Scope.RouteLevelGate,
            "container-keyed file writes — finding #4. App-only managed identity, so no container ACL is "
            + "needed by a caller."),

        new GovernedFile("Api/OBOEndpoints.cs", Scope.RouteLevelGate,
            "Container/drive-keyed SPE writes — finding #2. Task 071 deleted the four read/mutate routes "
            + "here (zero callers, gated id-keyed equivalents ship); the surviving upload trio is waived "
            + "to 073/075/076 because content-CREATION has no document to authorize against yet. "
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

        new GovernedFile("Api/ComposeEndpoints.cs", Scope.HandlerAuthorized,
            "19 routes keyed by documentSpeId / documentId / sessionId. Group-level RequireAuthorization "
            + "plus in-handler checks. Listed rather than gated because converting Compose to route-level "
            + "filters is a design change owned by the Compose ADR (ADR-049), not by this guard."),
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
        // ---------- PENDING — task 072 ----------
        new Waiver("POST /api/documents/{documentId}/share-link", WaiverKind.Pending, "072",
            "Mints a scope=anonymous, NON-EXPIRING SPE sharing link with no per-document filter. Escalates "
            + "\"container member\" to \"anyone with the URL\". Task 072 adds the filter, bounds the expiry "
            + "and drops scope=anonymous."),

        // ---------- PENDING — task 073 (container/drive-keyed writes) ----------
        new Waiver("PUT /api/containers/{containerId}/files/{*path}", WaiverKind.Pending, "073",
            "Finding #4. Carries RequireAuthorization(\"canwritefiles\") -> ResourceAccessRequirement -> "
            + "ResourceAccessHandler, which is real and fail-closed BUT resolves DOCUMENT rights from a "
            + "CONTAINER id (ExtractResourceId treats containerId/driveId/documentId/id interchangeably). "
            + "Wrong resource domain, not a missing mechanism — needs a behavioural denial test, task 073."),

        new Waiver("POST /api/containers/{containerId}/upload", WaiverKind.Pending, "073",
            "Same file, same container key, same wrong-domain policy as the route above."),

        new Waiver("PUT /api/upload-session/chunk", WaiverKind.Pending, "073",
            "Chunk writes carry NO resource key at all, so no per-resource mechanism can apply to the route "
            + "as written. Correctness requires the upload session itself to be bound to an already "
            + "authorized container at creation time."),

        new Waiver("PUT /api/drives/{driveId}/upload", WaiverKind.Pending, "073",
            "Drive-keyed write with the canwritefiles policy only — same wrong-domain shape."),

        new Waiver("DELETE /api/drives/{driveId}/items/{itemId}", WaiverKind.Pending, "073",
            "Drive-keyed DESTROY with the canwritefiles policy only. A destroy path is the worst case for a "
            + "wrong-domain check."),

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
        // tasks 075/076 build and 073 consumes. ADR-008 §6.5 Path A, bounded.
        new Waiver("PUT /api/obo/containers/{id}/files/{*path}", WaiverKind.Pending, "073/075/076",
            "Finding #2 — writes into a caller-named container. Authorization subject is the OWNING RECORD, "
            + "not a document that does not exist yet; 11 live call sites via EntityCreationService.ts:493. "
            + "Latent: OBO means SPE denies without a container ACL and no user holds one."),

        new Waiver("POST /api/obo/drives/{driveId}/upload-session", WaiverKind.Pending, "073/075/076",
            "Finding #2 — opens an upload session against an arbitrary drive id. Unreachable today: its "
            + "client first calls GET /api/obo/containers/{id}/drive, which is mapped NOWHERE."),

        new Waiver("PUT /api/obo/upload-session/chunk", WaiverKind.Pending, "073/075/076",
            "Finding #2 — carries NO resource key at all, so no per-route mechanism can apply to it as "
            + "written. Its authorization has to come from the session it belongs to. Unreachable today "
            + "for the same reason as the session route above."),

        // ---------- PENDING — task 079, found by task 071's inventory ----------
        new Waiver("GET /api/obo/drives/{driveId}/items/{itemId}/versions", WaiverKind.Pending, "079",
            "Finding #2's shape in DocumentVersionEndpoints.cs — version history for an arbitrary item. "
            + "Unlike the deleted four this has a LIVE caller (versionHistory.ts:81) and reads EXISTING "
            + "content, so it must be GATED, not deleted. Filed as task 079."),

        new Waiver("GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content",
            WaiverKind.Pending, "079",
            "Finding #2's shape — PRIOR-VERSION BYTES for an arbitrary item. Version content is exactly as "
            + "disclosing as current content and is easy to forget. Gate, do not delete — task 079."),

        // ---------- PENDING — found by this rule, no owner yet ----------
        new Waiver("GET /api/v1/containers/{containerId}/documents", WaiverKind.Pending, "UNOWNED",
            "FOUND BY THIS RULE ON ITS FIRST RUN and present in no Wave 1 task. Lists the documents of an "
            + "arbitrary container id behind RequireAuthorization() alone — no filter, no resource policy. "
            + "This is the sixth miss on a surface that has been recounted four times. Suggest folding into "
            + "task 073, which already owns the container-keyed surface."),

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
        "PUT /api/containers/{containerId}/files/{*path}",
        "POST /api/containers/{containerId}/upload",
        "PUT /api/upload-session/chunk",
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
    // green. 108 files under Api/** plus Infrastructure/DI/EndpointMappingExtensions.cs (health probes and
    // the module aggregator). See TheEndpointFileCensusIsPinned for the maintenance procedure.
    private const int ExpectedEndpointFileCount = 109;

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
        var gated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in GovernedFiles.Where(f => f.Scope == Scope.RouteLevelGate))
        {
            foreach (var route in ScanFile(file.RelativePath))
            {
                if (!route.Unparseable && route.Mechanism == AuthMechanism.Filter)
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
            if (!DecisionServices.Any(s => code.Contains(s, StringComparison.Ordinal)))
            {
                violations.Add(
                    $"{SourceScan.Relative(file)}: references none of the authorization decision services");
            }
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
    private static readonly IReadOnlyDictionary<string, string> KnownDecorativeFilters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecordSearchAuthorizationFilter"] =
                "PENDING — owner UNASSIGNED. FOUND BY RULE B, in no finding list. This is finding #1's TWIN, "
                + "on the same /api/ai/search group: it reads the 'tid' claim, extracts the request, writes "
                + "one LogInformation, and returns `await next(context)`. There is no authorization decision "
                + "anywhere in it, and its only dependency is ILogger — the exact shape "
                + "SemanticSearchAuthorizationFilter had when a non-admin denied Read on all 442 documents "
                + "read a matter's full document list through POST /api/ai/search. It gates "
                + "POST /api/ai/search/records, so Rule A currently classifies that route as GATED. Needs "
                + "the same treatment task 070 is giving its sibling — constrain to the caller's accessible "
                + "record set. Delete this entry when it does.",
        };

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

        // Census: 7 → 3, because task 071 DELETED four of them (children / PATCH / content / DELETE —
        // all had zero callers, and gated document-id-keyed equivalents already ship). The 3 survivors
        // are the upload trio, which task 071 escalated rather than gated: they CREATE content, so no
        // sprk_document exists yet to authorize against, and attaching the document filter would deny
        // 100% of uploads across 9 wizards. They are waived to 073/075/076 below.
        //
        // Update this number when routes are added or removed here — that is the ratchet working, not a
        // nuisance. Note the retroactive-validation test above is unaffected by the deletions: it feeds
        // the scanner INLINE source text, so it still proves the rule catches finding #2's shape even
        // though that route no longer exists in the file.
        Assert.True(
            ScanFile("Api/OBOEndpoints.cs").Count == 3,
            "Expected 3 registrations in OBOEndpoints.cs after task 071 retired the four drive-keyed "
            + "read/mutate routes. A drop here would make the surviving upload trio invisible.");
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
    /// <c>.RequireAuthorization("canwritefiles")</c> in <c>UploadEndpoints.cs</c> visible at all — it comes
    /// after a 90-line lambda body.
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
}
