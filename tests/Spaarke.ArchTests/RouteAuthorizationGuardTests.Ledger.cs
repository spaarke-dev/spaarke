namespace Spaarke.ArchTests;

/// <summary>
/// THE DATA behind <see cref="RouteAuthorizationGuardTests"/>: the census classification, the credit lists,
/// the handler decisions, the admin pin, the waivers and the sweep ledger.
///
/// <para><b>Why this is its own file (task 167).</b> Tasks 159-166 fix the sweep's 82 findings in parallel, and
/// every one of them ends by deleting its Pending waivers and recording its resolution. Keeping the data apart
/// from the rules means those edits conflict here, on lists, rather than in the rule code; the main session
/// reconciles one data file at integration.</para>
///
/// <para>Every list carries its own maintenance comment. The binding rule for all of them: an entry is a line a
/// reviewer can evaluate in two years — a written reason, an owner where the entry is work, and a file:line
/// where the entry asserts something about code.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // =============================================================================================
    // AGGREGATORS — files that bind a route group of their own into another file's Map method
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: a cross-file group binding from a file NOT listed here is unparseable (an unknown aggregator is
    // a route surface nobody has classified). Add a file only after reading the group it binds and its chain.
    // The two pure aggregators register no route themselves, so they are not census files.
    // =============================================================================================

    private sealed record Aggregator(string RelativePath, string Reason);

    private static readonly IReadOnlyList<Aggregator> Aggregators = new[]
    {
        new Aggregator("Api/SpeAdminEndpoints.cs",
            "/api/spe + RequireAuthorization + AddSpeAdminAuthorizationFilter + AddSpeAdminTenantScopeFilter "
            + "(SpeAdminEndpoints.cs:27-35), bound into 19 Api/SpeAdmin/* files in BOTH call forms: group.MapXEndpoints() "
            + "and XEndpoints.MapXEndpoints(group)."),
        new Aggregator("Api/ComposeEndpoints.cs",
            "/api/compose + RequireAuthorization (ComposeEndpoints.cs:30-32), bound into the eight Compose*Endpoints files. "
            + "MapComposeSyncEndpoints also receives the ROOT builder `routes`, so its webhook is a root registration."),
        new Aggregator("Api/Ontology/OntologyAdminEndpoints.cs",
            "/api/v1/ontology/admin + RequireRateLimiting(dataverse-query) + RequireAuthorization (OntologyAdminEndpoints.cs:38-41), bound "
            + "into Api/Ontology/OntologyAdminRulesEndpoints.cs by group.MapOntologyAdminRulesEndpoints() (ontology platform R1 task 100) "
            + "and into Api/Ontology/OntologyAdminAuthoringEndpoints.cs by OntologyAdminAuthoringEndpoints.MapOntologyAdminAuthoringEndpoints"
            + "(group) (task 101), and calls app.MapOntologyAdminClassificationEndpoints() for Api/Ontology/OntologyAdminClassificationEndpoints.cs "
            + "(task 103), which declares its own group under the same URL prefix; it also registers GET /capabilities itself (task 104)."),
        new Aggregator("Api/ExternalAccess/ExternalAccessEndpoints.cs",
            "externalGroup /api/v1/external + RequireAuthorization(ExternalCollaboration) + CallerPrincipal (:54-57) and "
            + "adminGroup /api/v1/external-access + RequireAuthorization + AddDelegationRuleFilter (:123-126), bound into "
            + "the external data-plane and management files; it also registers three routes of its own."),
    };

    // =============================================================================================
    // GOVERNED FILES — the census set, every file classified
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: this set must EQUAL EndpointFiles() (TheEndpointFileCensusIsPinned). The Scope is checked
    // against how the scanner binds the file's routes, so it cannot drift from the code. The reason says what
    // the surface serves; the per-route decisions live in the lists below, never here.
    // =============================================================================================

    private enum Scope
    {
        /// <summary>Routes registered on the application root or on the file's own MapGroup.</summary>
        RouteLevelGate,

        /// <summary>Routes registered on a group an aggregator binds; prefix and chain are inherited.
        /// Added by task 091; Rule E forbids absolute paths on the bound group.</summary>
        GroupGated,

        /// <summary>Routes registered only in a method nothing calls — dead code, not live routes.</summary>
        NotMapped,
    }

    private sealed record GovernedFile(string RelativePath, Scope Scope, string Reason);

    private static readonly IReadOnlyList<GovernedFile> GovernedFiles = new[]
    {
        // ---- the document / file-byte surface (task 074's original RouteLevelGate set) ----
        new GovernedFile("Api/FileAccessEndpoints.cs", Scope.RouteLevelGate,
            "/api/documents/{documentId}/* — file bytes, URL minting and document identity; all eleven routes carry "
            + "AddDocumentAuthorizationFilter (share-link, gated by task 072, was RETIRED 2026-10-16, #1350; resolve-identity pairs DocumentUrlIdentityFilter "
            + "with it; GET /{documentId}/identity since word-add-in task 112; resolve-email-identity pairs "
            + "DocumentEmailIdentityFilter with it since word-add-in task 120)."),
        new GovernedFile("Api/DataverseDocumentsEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/documents/* — document rows, a byte download, the pointer attach (POST /{id}/file, task 166 f1) and the "
            + "two container-keyed listings (gated by task 078; GET /api/v1/documents?containerId= since task 166 carries "
            + "AddContainerDocumentAuthorizationFilter(queryParameter: \"containerId\") — its former Permanent 'collection read' "
            + "waiver was false, the caller names ONE container). PUT /{id} is KEPT (round 64): task 166 retired it, task 147 "
            + "r1 gave it a caller (the Compose re-file), so it stays behind 146's AppendTo check and refuses the "
            + "storage-pointer fields with 400 sdap.documents.pointer_field_refused — 166's condition for any body-bound "
            + "document update."),
        new GovernedFile("Api/DocumentOperationsEndpoints.cs", Scope.RouteLevelGate,
            "checkout / checkin / discard / delete / analyze on one document; all gated by task 022."),
        new GovernedFile("Api/DocumentsBulkEndpoints.cs", Scope.RouteLevelGate,
            "/api/documents/bulk-download — app-only lookup, so BulkDownloadAuthorizationFilter's per-document verdict is "
            + "the whole boundary (finding C1)."),
        new GovernedFile("Api/DocumentVersionEndpoints.cs", Scope.RouteLevelGate,
            "document-id-keyed version history and prior-version bytes, gated \"read\" by task 079."),
        new GovernedFile("Api/OBOEndpoints.cs", Scope.RouteLevelGate,
            "OBO uploads: two record-keyed routes (RecordRouteAccessAuthorizationFilter, task 076) and the record-less "
            + "/api/obo/me/files route (Permanent CreateWithNoPriorResource)."),
        new GovernedFile("Api/PermissionsEndpoints.cs", Scope.RouteLevelGate,
            "the caller's own rights on documents, computed by AuthorizationService in the handler (HandlerDecision)."),

        // ---- AI ----
        new GovernedFile("Api/Ai/SemanticSearchEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/search — document names, summaries and SPE ids (finding #1; gated by task 070)."),
        new GovernedFile("Api/Ai/RecordSearchEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/search/records — Dataverse record content over the search surface (task 077)."),
        new GovernedFile("Api/Ai/VisualizationEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/visualization/* — neighbour documents as graph nodes, authorized per row (word-add-in-r1 task 032)."),
        new GovernedFile("Api/Ai/AnalysisEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/analysis/* — create and execute (Read on every body document via IAiAuthorizationService; execute adds the "
            + "run filter: Write on every document for the document-profile branch or a playbook that can write, plus the "
            + "playbook-use decision), promote (G5: Create privilege on sprk_analysis, analysis.attach on the body document and "
            + "regarding record, playbook-use; the session owner and the session-derived document are checked in the handler) "
            + "and GET /{analysisId} (Read on EVERY populated anchor, uniform 404). Every new check is evaluated by "
            + "FinanceAuthorizationFilter (task 162; sweep S-02, S-22, S-52). /fork, /{analysisId}/save and "
            + "/{analysisId}/export were DELETED (owner round 10 item 1; S-01, S-50, S-51)."),
        new GovernedFile("Api/Ai/AnalysisChatContextEndpoints.cs", Scope.RouteLevelGate,
            "the chat context resolved for one analysis output (a latent IDOR; task-167 UNOWNED-NEW, assigned to 164)."),
        new GovernedFile("Api/Ai/CapabilityDiscoveryEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/ai/capabilities — the launchable-capability catalog."),
        new GovernedFile("Api/Ai/ChatAckEndpoints.cs", Scope.RouteLevelGate,
            "the chat UI-action acknowledgement on an owned session (SessionOwnershipFilter)."),
        new GovernedFile("Api/Ai/ChatDocumentEndpoints.cs", Scope.RouteLevelGate,
            "chat-session document upload, persist and content routes (SessionOwnershipFilter), plus the caller's own "
            + "event-rules opt-out."),
        new GovernedFile("Api/Ai/ChatEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/chat/* — sessions, messages, context switching, tabs, playbooks and context mappings (sweep S-23..S-25; "
            + "task 164)."),
        new GovernedFile("Api/Ai/ChatWordExportEndpoints.cs", Scope.RouteLevelGate,
            "Word export of chat content to the staging container over OBO."),
        new GovernedFile("Api/Ai/DailyBriefingEndpoints.cs", Scope.RouteLevelGate,
            "the daily briefing: summarise/narrate caller payloads; render/email the caller's own briefing."),
        new GovernedFile("Api/Ai/DispatchSessionEndpoint.cs", Scope.RouteLevelGate,
            "the chip-click dispatch on an owned session (sweep S-53; task 164)."),
        new GovernedFile("Api/Ai/FeedbackEndpoints.cs", Scope.RouteLevelGate,
            "per-response feedback submit and tenant-wide feedback aggregates."),
        new GovernedFile("Api/Ai/HandlerEndpoints.cs", Scope.RouteLevelGate,
            "the in-process tool-handler catalog."),
        new GovernedFile("Api/Ai/KnowledgeBaseEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/knowledge/indexes/health — the one route left: task 163 DELETED the document listing, delete, reindex and "
            + "test search (sweep S-03, S-26..S-28; owner round 10 item 1)."),
        // Api/Ai/AdminKnowledgeEndpoints.cs entry DELETED at the task-167 integration (2026-10-05): task 163 deleted the file
        // with all three of its routes (sweep S-20, S-21, S-49); their absence is pinned by the sweep ledger's ProofTest.
        new GovernedFile("Api/Ai/ModelEndpoints.cs", Scope.RouteLevelGate,
            "the static model-deployment catalog."),
        new GovernedFile("Api/Ai/NdaStandardEndpoints.cs", Scope.RouteLevelGate,
            "the fixed NDA-standard clause text B1..B16."),
        new GovernedFile("Api/Ai/NodeEndpoints.cs", Scope.RouteLevelGate,
            "playbook node CRUD, gated on playbook access/ownership; PUT /nodes/reorder was DELETED by task 164 (sweep S-54)."),
        new GovernedFile("Api/Ai/PlaybookEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/playbooks/* — playbook CRUD, sharing and lists; GET /by-id/{id} carries the playbook-use decision "
            + "(task 164, sweep S-55) and GET /by-name/{name} was DELETED."),
        new GovernedFile("Api/Ai/PlaybookRunEndpoints.cs", Scope.RouteLevelGate,
            "playbook execute (the run decision: playbook-use, Read/Write on every document and record parameter — task 164, "
            + "sweep S-29), validate, and the per-request run-status routes."),
        // Api/Ai/PromptLibraryEndpoints.cs and Api/Ai/RecordMatchEndpoints.cs entries DELETED at the task-167 integration
        // (2026-10-05): task 164 deleted both files (all six /api/ai/prompts routes and match-records / associate-record;
        // sweep S-31, S-32, S-56, S-57; owner round 10 item 1). Absence pinned by the sweep ledger's ProofTest.
        new GovernedFile("Api/Ai/RagEndpoints.cs", Scope.RouteLevelGate,
            "/api/ai/rag/* — search (a named parent through the targeted-record filter, every row trimmed as the caller), "
            + "index-file (Write on the named document, AppendTo on its parent), embedding, send-to-index, the RagApiKey "
            + "automation route, and the SystemAdmin index-maintenance routes (index, DELETE /{documentId}, bulk-index; task "
            + "163, sweep S-04..S-06, S-30). /index/batch and DELETE /source/{sourceDocumentId} were DELETED."),
        new GovernedFile("Api/Ai/ReviewMemoEndpoints.cs", Scope.RouteLevelGate,
            "review-memo assembly and download on an owned session (SessionOwnershipFilter)."),
        new GovernedFile("Api/Ai/ScopeEndpoints.cs", Scope.RouteLevelGate,
            "the org-wide skill / knowledge / tool / action / persona catalogs."),
        new GovernedFile("Api/Ai/StandaloneChatContextEndpoints.cs", Scope.RouteLevelGate,
            "standalone chat context built from an in-memory field catalog."),
        new GovernedFile("Api/Ai/SummarizeSessionEndpoint.cs", Scope.RouteLevelGate,
            "chat-summarize on an owned session (SessionOwnershipFilter)."),

        // ---- agent, admin, insights, memory, notifications ----
        new GovernedFile("Api/Agent/AgentEndpoints.cs", Scope.RouteLevelGate,
            "/api/agent/* — the M365 Copilot gateway (sweep S-18, S-19, S-78; task 164)."),
        new GovernedFile("Api/Admin/JobsEndpoints.cs", Scope.RouteLevelGate,
            "/api/admin/jobs/* — background-job inspection and control, SystemAdmin."),
        new GovernedFile("Api/Admin/MembershipAdminEndpoints.cs", Scope.RouteLevelGate,
            "/api/admin/membership/* — membership discovery audit and cache refresh, SystemAdmin."),
        new GovernedFile("Api/Admin/RecordMatchingAdminEndpoints.cs", Scope.RouteLevelGate,
            "/api/admin/record-matching/* — record-index sync behind sign-in only (sweep S-47, S-48, S-77; task 165)."),
        new GovernedFile("Api/Insights/InsightEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/insights/ask — the shared playbook-parameter policy, then Read (Write when a node that can write "
            + "reaches it) on the subject matter and each record parameter, as the caller (task 163 f1; sweep S-17)."),
        new GovernedFile("Api/Insights/InsightsAssistantEndpoint.cs", Scope.RouteLevelGate,
            "POST /api/insights/assistant/query — Read on the subject as the caller before any SSE frame (task 163 f1; "
            + "sweep S-40)."),
        new GovernedFile("Api/Insights/InsightsSearchEndpoint.cs", Scope.RouteLevelGate,
            "POST /api/insights/search — Read on the subject through AddFinanceAuthorizationFilter (task 163; sweep S-41)."),
        new GovernedFile("Api/Insights/PrecedentAdminEndpoints.cs", Scope.RouteLevelGate,
            "/api/insights/admin/precedents — SME precedent authoring behind the SPE admin role."),
        new GovernedFile("Api/Membership/MembershipEndpoints.cs", Scope.RouteLevelGate,
            "the caller's own record memberships."),
        new GovernedFile("Api/Memory/MemoryGovernanceEndpoints.cs", Scope.RouteLevelGate,
            "/api/memory/user/* (the caller's own memory). The record-memory read GET /api/memory/records/... was DELETED by "
            + "task 166 (sweep S-42)."),
        new GovernedFile("Api/Memory/PinnedMemoryEndpoints.cs", Scope.RouteLevelGate,
            "/api/memory/pins/* — the caller's pins; create/update take a caller-chosen matter, checked AppendTo as the caller "
            + "before the write, and update/delete answer another user's pin exactly as an unknown one (task 166; sweep S-43, "
            + "S-68; owner round 12 item 4)."),
        new GovernedFile("Api/Notifications/NotificationsEndpoints.cs", Scope.RouteLevelGate,
            "the caller's own notification negotiate, pending list and dismiss."),

        // ---- communications ----
        new GovernedFile("Api/CommunicationEndpoints.cs", Scope.RouteLevelGate,
            "/api/communications/* — send, archive, threads, proposals, the filing re-file (task 147), queue feed and the Graph "
            + "webhook. Every record-keyed route carries CommunicationRecordAuthorizationFilter after the identity "
            + "precondition (task 161; sweep S-07, S-33..S-35, S-58..S-62); GET /{id}/status was DELETED (S-79)."),
        new GovernedFile("Api/CommunicationTemplateEndpoints.cs", Scope.RouteLevelGate,
            "the email composer's template render: Read on the regarding record and an impersonated template read (task 161; "
            + "sweep S-08)."),
        new GovernedFile("Api/CommunicationDraftEndpoints.cs", Scope.RouteLevelGate,
            "the email composer's AI draft over caller-supplied text."),
        new GovernedFile("Api/AcsEventGridEndpoints.cs", Scope.RouteLevelGate,
            "the ACS Event Grid ingress — anonymous, with an OPTIONAL shared secret (task-167 UNOWNED-NEW, assigned to 161)."),

        // ---- Compose: the eight files the /api/compose aggregator binds ----
        // Reasons ported from the integration branch's guard (task 166's rewrites), in this structure's shape.
        new GovernedFile("Api/ComposeDocumentEndpoints.cs", Scope.GroupGated,
            "GET /documents/{documentSpeId} (load) and refresh-profile. Load reads the SPE bytes AS THE CALLER (OBO) under the "
            + "claim tenant and resumes only a session the caller owns (#863); since task 166 r1 its documentRecordId is "
            + "honoured only when that row's sprk_graphitemid IS the OBO-read item. refresh-profile (task 166 r1) is gated by "
            + "DocumentAuthorizationFilter(\"write\") on {documentId:guid}, takes the tenant from the CLAIM and stamps only "
            + "when the row's item is the posted item. POST /documents/{documentSpeId}/promote was DELETED by task 166 (S-63)."),
        new GovernedFile("Api/ComposeSaveEndpoints.cs", Scope.GroupGated,
            "POST /documents/{documentSpeId}/save and /documents/create-on-save — the write path. The SPE write is OBO; since "
            + "task 166 (amendment d) both take the tenant from the CLAIM and bind the body session ONLY when the caller owns "
            + "it (ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync) — otherwise the save runs unbound."),
        new GovernedFile("Api/ComposeAnnotationEndpoints.cs", Scope.GroupGated,
            "pull/reanchor annotations keyed by documentSpeId (the SPE read is OBO, so SPE decides; the tenant is the claim's, "
            + "task 166 r1), plus GET/POST /sessions/{sessionId}/annotations behind SessionOwnershipFilter; since task 166 "
            + "(S-80) the POST writes to the SAME claim tenant that filter authorized."),
        new GovernedFile("Api/ComposeCheckoutEndpoints.cs", Scope.GroupGated,
            "checkout/checkin STUBS and the lock-holder-only heartbeat."),
        new GovernedFile("Api/ComposeMountEndpoints.cs", Scope.GroupGated,
            "POST /upload — the retained bytes of a file uploaded into a chat session, authorized by the session it names: "
            + "since task 166 (S-64) the handler requires the BODY sessionId to be a session the caller owns BEFORE any cache "
            + "read — and POST /project, the stateless projection of caller-supplied bytes."),
        new GovernedFile("Api/ComposeTemplateEndpoints.cs", Scope.GroupGated,
            "POST /documents/{documentSpeId}/apply-template: a caller-named template read with an app token."),
        new GovernedFile("Api/ComposeSyncEndpoints.cs", Scope.GroupGated,
            "check-changes on the bound group — since task 166 (S-65) an OBO metadata read of THAT item in THAT container as "
            + "the caller BEFORE the app-only delta — and the anonymous HMAC-signed Graph webhook on the ROOT builder "
            + "`routes`, the reason Rule E reads only the bound group."),
        new GovernedFile("Api/ComposeActiveDocumentEndpoints.cs", Scope.GroupGated,
            "POST /active-document: the body session's owner is checked in the handler (#863); since task 166 r1 a STORED "
            + "document is recorded only when the caller can read its row (IDataverseUserClient, as the caller), and the SPE "
            + "pointer recorded is that row's, never the body's."),

        // ---- Dataverse proxy ----
        // Api/Dataverse/FetchEndpoints.cs and Api/Dataverse/RecordEndpoints.cs entries DELETED at the task-167 integration
        // (2026-10-05): task 160 deleted both files (POST /api/dataverse/fetch and GET /api/dataverse/record/...; sweep S-09,
        // S-10; owner round 10 item 1). Absence pinned by the sweep ledger's ProofTest.
        new GovernedFile("Api/Dataverse/MetadataEndpoints.cs", Scope.RouteLevelGate,
            "entity metadata, gated on entity Read privilege (EntitySource.FromRouteValue)."),
        new GovernedFile("Api/Dataverse/GridConfigurationEndpoints.cs", Scope.RouteLevelGate,
            "grid configurations for an entity, gated on entity Read privilege (EntitySource.FromRouteValue)."),
        new GovernedFile("Api/Dataverse/SavedQueryEndpoints.cs", Scope.RouteLevelGate,
            "system views: the list gated by the filter, the by-id read by the handler's privilege check."),

        // ---- events, field mappings, finance, misc ----
        // Text from task 147's ledger input (notes/task-147-client-child-writers.md §12), which REPLACES task 159's §5 text.
        new GovernedFile("Api/Events/EventEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/events/* — GET /{id}, POST /{id}/complete and PATCH /{id}/filing carry RecordRouteAccessAuthorizationFilter "
            + "on sprk_events({id}) (no Read → uniform 404; Read without the route's right → 403); POST / carries the same "
            + "filter with the Create privilege and AppendTo on the regarding record; GET / runs its query as the caller "
            + "(task 159, #1098). PATCH /{id}/filing (task 147, owner round 36) takes only the filing, its shape checked "
            + "before the filter, and re-files through OwnedChildWrite.RefileAsync (AppendTo on every new parent, F3). "
            + "PATCH /{id}/due-assignee (ontology platform R1 task 044, #29) writes ONLY sprk_duedate and/or the assignee contact "
            + "(statuscode Reassigned, reassigned-by): shape checked before the filter, the same write filter on sprk_events({id}) "
            + "(no Read → uniform 404; Read without Write → 403), then EventDueAssigneeWrite runs AS THE CALLER through "
            + "IDataverseUserClient (open work only). "
            + "PUT /{id}, DELETE /{id}, /{id}/cancel and /{id}/logs were deleted (round 10 item 1)."),
        new GovernedFile("Api/Signals/DecisionPlanEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/signals/{signalId}/decision-plan (ontology platform R1 task 036) -- a Signal's decision plan resolved against "
            + "the closed action catalog. The record-level decision is SignalCoreRecordAccess.AuthorizeAsync, asked AS THE CALLER "
            + "through IDataverseUserClient (the Signal row, then its core record of any type; a no-core Signal, in the Do lane only, "
            + "needs the caller's own read of BOTH the Signal and its subject row, D-35 as amended by D-129), and for a Do-lane Signal "
            + "the worklist's own \"mine\" read of its subject, also AS THE CALLER (D-141; not the caller's own work is the uniform 404): "
            + "no read is the uniform 404, an unresolved caller the single 403 (D-29), a fault reading the subject or answering \"mine\" a 503."),
        new GovernedFile("Api/Signals/DecisionCommitEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/signals/{signalId}/decision (ontology platform R1 task 043, D-17) -- records ONE decision on a Signal. "
            + "Before anything else (even the review-id lookup) every Signal in the review -- the primary and each Also-resolve -- "
            + "goes through SignalCoreRecordAccess.AuthorizeAsync AS THE CALLER through IDataverseUserClient (no read: the uniform "
            + "404; an unresolved caller: the single 403). That is the readability half only: the Do-lane \"mine\" half (D-141) runs "
            + "after the review-id ledger peek and before any lock, ledger answer or write (not mine: the uniform 404), is skipped only "
            + "when the same caller resends the same content, and runs again before a resume or a repair replay unless that review's own "
            + "reassign step landed. Then every action's preflight checks the caller's own rights before any "
            + "write (a missing right is 403). Core record and item subject are derived server-side from the Signal, never the body; "
            + "the Decision Record's owning team is the ownership resolver's answer for the core and every resolved Signal's subject (D-129)."),
        new GovernedFile("Api/Signals/SignalReadEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/v1/signals/worklist (ontology platform R1 task 038, D-15, FR-54) -- the worklist's only Signal reader. The "
            + "record-level decision is SignalReadService.ReadAsync, every row-visibility read AS THE CALLER through IDataverseUserClient: "
            + "the Signals (Dataverse trims by the Signal's BU and shares), then one batched read per core type of the catalog-named core "
            + "table (a row whose core record the caller cannot read, a half pair or a non-core type is dropped without trace), then for "
            + "the Do lane the subject filtered to the caller's own work (task 097 decision B, D-44, D-129; a no-core row is Do-only, "
            + "D-35). An unresolved caller is the single 403 (D-29); a fault is 503, never a shorter list."),
        new GovernedFile("Api/Ontology/OntologyAdminEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/ontology/admin (ontology platform R1 task 104, D-22, FR-56) -- GET /capabilities, the Ontology admin capability "
            + "probe: whether the CALLER holds prvCreatesprk_PolicyVersion, asked AS THE CALLER through IDataverseUserClient (WhoAmI, then "
            + "RetrieveUserPrivilegeByPrivilegeName on the id WhoAmI returned). It decides only whether the Console shows the tab; it is "
            + "never the security boundary. RequireAuthorization on the group; takes no id (Permanent CallerScopedOnly below). Tasks 100 "
            + "and 101 add their admin routes to this group, each authorized as the caller."),
        new GovernedFile("Api/Ontology/OntologyAdminClassificationEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/ontology/admin/classification (ontology platform R1 task 103, D-22, FR-59, D-49) -- the Classification area: GET /categories "
            + "(the triage categories with guidance, On/Off, the policy versions that read each, 30-day volume and stored recall), POST /categories "
            + "(a new category, always saved Off), PATCH /categories/{categoryId} (name and guidance; a rename is refused while any policy version "
            + "reads the category) and PUT /categories/{categoryId}/enabled (On/Off). The file declares its own group (RequireAuthorization) and "
            + "every read and write runs AS THE CALLER through IDataverseUserClient, so Dataverse enforces the Spaarke Ontology Administrator role "
            + "(a Console User's write is refused 403) and the audit names the human; an unresolved caller is the single 403. Nothing reads the "
            + "task 104 capability answer (OntologyAdminCallerBoundaryGuardTests)."),
        new GovernedFile("Api/Ontology/OntologyAdminRulesEndpoints.cs", Scope.GroupGated,
            "/api/v1/ontology/admin/rules/* (ontology platform R1 task 100, D-22, FR-57) -- the Rules area of the Ontology admin tab: the "
            + "rule list and rule page (reads), PUT /{policyId}/enabled (On/Off) and POST /{policyId}/retire. The group is bound by "
            + "OntologyAdminEndpoints (RequireAuthorization). Every Dataverse read and write is made AS THE CALLER through IDataverseUserClient "
            + "by OntologyAdminRulesService (Dataverse enforces the Spaarke Ontology Administrator role on each write and the audit names the "
            + "human); a rule whose sprk_domain is not empty or Ontology (Billing, Docketing) is the same 404 as a missing one, with no write. "
            + "No route reads the capability answer (rule A of OntologyAdminCallerBoundaryGuardTests). Retire's only service-identity work is "
            + "task 033's closure of the rule's Signals, called after the caller's own write succeeded."),
        new GovernedFile("Api/Ontology/OntologyAdminAuthoringEndpoints.cs", Scope.GroupGated,
            "/api/v1/ontology/admin/authoring/* (ontology platform R1 task 101, D-22 slice b, FR-58) -- the rule wizard's BFF: GET /catalog, "
            + "POST /dry-run, POST /policies and POST /policies/{policyId}/versions, on the group OntologyAdminEndpoints.cs passes in. Every "
            + "handler's only I/O is one OntologyAdminAuthoringService call whose every read the caller acts on and every write runs AS THE "
            + "CALLER through IDataverseUserClient (WhoAmI first: unresolved is the single 403); a policy id is read as the caller through the "
            + "Ontology domain allow-list before any write (absent, unreadable or non-Ontology: the uniform 404). The dry run evaluates as the "
            + "evaluator (#39) and returns only rows whose subject AND core record the caller's own read returns. Task 102a adds "
            + "POST /validate (compile only) to this file, gated exactly as the dry run. HandlerDecisions below."),
        new GovernedFile("Api/InquiryEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/inquiries/{serviceRequestId}/disposition (ontology platform R1 task 071, D-111) -- a person records how a "
            + "budget inquiry turned out. The route carries RecordRouteAccessAuthorizationFilter(\"write\", sprk_servicerequests, "
            + "serviceRequestId): no Read is the uniform 404, Read without Write a 403; every write then runs AS THE CALLER "
            + "(IDataverseUserClient, version-conditional), so Dataverse enforces Write a second time. The reply is read as the "
            + "caller and must be Incoming and filed against the inquiry named. GET /api/v1/inquiries/for-todo/{todoId} (task 154) "
            + "carries RecordRouteAccessAuthorizationFilter(\"read\", sprk_todos, todoId) and reads the To Do, the inquiry and its "
            + "replies AS THE CALLER: a refused read is the uniform 404, a fault 503."),
        new GovernedFile("Api/FieldMappings/FieldMappingEndpoints.cs", Scope.RouteLevelGate,
            "field-mapping configuration reads, type validation, and the push route: Read on the source as the caller, children "
            + "read and written impersonated (task 166; sweep S-67)."),
        new GovernedFile("Api/Finance/FinanceEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/* — each route declares the id it authorizes (AddFinanceAuthorizationFilter, task 130)."),
        new GovernedFile("Api/Finance/FinanceRollupEndpoints.cs", Scope.RouteLevelGate,
            "matter/project rollup recalculation, Read on the parent as the caller (task 130)."),
        new GovernedFile("Api/Finance/RateCardImportEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/ratecards/* (ebilling-r1 task 078, FR-29) -- rate card and timekeeper workbook template, import and publish. "
            + "Each route declares the Dataverse table privileges the caller must hold (AddFinanceAuthorizationFilter, D-76, fail closed), "
            + "plus a Read check on any matter, project or work assignment the card is scoped to; the eBilling kill switch runs after it."),
        new GovernedFile("Api/Finance/RuleSetEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/rulesets/* (ebilling-r1 task 076, FR-28) -- create a rule set from a source document, list / read it with its rules, "
            + "edit / accept / reject a rule, publish, assign, visibility, and (task 075, FR-27) POST .../{ruleSetId}/draft which queues the rule "
            + "drafting job (the count of endpoint files is unchanged at 137: a route in an existing file). Each route declares the billing-admin Dataverse table privileges the "
            + "caller must hold (BillingAdminAuthorization via AddFinanceAuthorizationFilter, D-76, fail closed; Write on sprk_ruleset is the marker "
            + "because the Ontology Administrator holds every privilege on the shared policy tables); reads of the proposals are admin-only too. "
            + "The eBilling kill switch runs after it."),
        new GovernedFile("Api/Finance/ProcedureEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/procedures/* (ebilling-r1 task 081, EB-D-148) -- Procedures and their versions. Step 0 on the group "
            + "(RequireInternalFinanceCaller, reads included: Procedures are internal-only). Each route then declares its basis through "
            + "AddFinanceAuthorizationFilter, as the caller, fail closed (BillingAdminAuthorization): POST / -- Create on sprk_procedure "
            + "(prvCreatesprk_procedure) plus Create and Write on sprk_ruleset (it creates the rule set too); GET /{procedureId} -- Read on the "
            + "Procedure (Record path, sprk_procedures); PATCH /{procedureId}, PATCH .../versions/{versionId}, POST .../versions/{versionId}/retire "
            + "and GET .../retire-preview -- Write on the Procedure AND the billing-admin marker (Write on sprk_ruleset); POST .../versions -- the "
            + "same plus Create on sprk_policyversion; POST .../versions/{versionId}/publish -- the same plus Write on sprk_policyversion and "
            + "sprk_policy; POST .../retire -- the same plus Write on sprk_policy. The eBilling kill switch runs after it."),
        new GovernedFile("Api/Finance/InvoiceAdjustmentEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/invoices/{invoiceId}/adjustment-rules, POST .../adjustments and DELETE .../adjustments/{lineId} (ebilling-r1 task 102, "
            + "FR-32) -- the invoice-level adjustments. Every route carries AddFinanceAuthorizationFilter on sprk_invoices(invoiceId) as the caller, fail "
            + "closed: the rule list with \"finance.read\" (Read), add and remove with \"ebilling.invoice.decide\" (Write, D-77); invoice access reaches "
            + "the caller through the core record (013, ADR-034). The eBilling kill switch runs after it. The body carries no amount and no total (016 "
            + "computes both), a removed line must be an Adjustment line of the route's own invoice (uniform 404), and the reviewer is the caller's "
            + "systemuserid from their own token, never the body."),
        new GovernedFile("Api/Finance/InvoiceContextEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/invoices/{invoiceId}/context (ebilling-r1 task 106, FR-33) -- the Invoice Review context panel. The route carries "
            + "AddFinanceAuthorizationFilter(\"finance.read\", sprk_invoices, invoiceId): Read on the invoice as the caller, fail closed; the eBilling "
            + "kill switch runs after it. The firm and core record the sections are scoped to are read from that invoice, never from the request."),
        new GovernedFile("Api/Finance/InvoiceCorrectionEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/finance/invoices/{invoiceId}/lines/{lineId}/corrections, POST .../header/corrections and GET .../corrections (ebilling-r1 task "
            + "101 part B1, FR-31, FR-22, D-64) -- reviewer corrections of extracted values and AI codes. Every route carries "
            + "AddFinanceAuthorizationFilter on sprk_invoices(invoiceId) as the caller, fail closed: the list with \"finance.read\" (Read), the two "
            + "corrections with \"ebilling.invoice.decide\" (Write, D-77); invoice access reaches the caller through the core record (013, ADR-034). "
            + "The eBilling kill switch runs after it. The line must belong to the route's invoice (uniform 404) and a code must be in the line's "
            + "recorded code set before anything is written; the body carries no outcome, role or reviewer (the reviewer is the caller's systemuserid "
            + "from their own token) and a correction never touches a line's status, approved amount or decision."),
        new GovernedFile("Api/Finance/InvoiceLineReviewEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/invoices/{invoiceId}/review and POST /api/finance/invoices/{invoiceId}/lines/{lineId}/decision, .../lines/decisions, "
            + ".../lines/accept-clean (ebilling-r1 task 100, FR-31) -- the Invoice Review read model and the line decisions. Every route carries "
            + "AddFinanceAuthorizationFilter on sprk_invoices(invoiceId) as the caller, fail closed: the read with \"finance.read\" (Read), the three "
            + "decisions with \"ebilling.invoice.decide\" (Write, D-77); invoice access reaches the caller through the core record (013, ADR-034). The "
            + "eBilling kill switch runs after it. Every line id is checked against the invoice's own lines (uniform 404) before a write; the decider "
            + "is the caller's systemuserid from their own token, never the body."),
        new GovernedFile("Api/Finance/InvoiceRelatedWorkEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/invoices/{invoiceId}/lines/{lineId}/related-work (ebilling-r1 task 107, FR-34) -- the Related work pane of one fee line. "
            + "The route carries AddFinanceAuthorizationFilter(\"finance.read\", sprk_invoices, invoiceId): Read on the invoice as the caller, fail closed; "
            + "the eBilling kill switch runs after it. The line must belong to the invoice (uniform 404); the core record, author and window are read from "
            + "the line, so no core, container or document id comes from the request, and every candidate document is put to the caller's own Read "
            + "(sprk_documents, \"read\") before it is searched or returned."),
        new GovernedFile("Api/Finance/InvoiceSubmissionEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/{matters|projects|workassignments}/{id}/invoice-submissions and /api/finance/invoice-submissions/{id} "
            + "(ebilling-r1 task 044, FR-17 internal channel). The submits run AddFinanceAuthorizationFilter as the caller: Read and "
            + "AppendTo on the route's core record (finance.submit_invoice) and AppendTo on the query firm (finance.attach_invoice); "
            + "the status read needs Read on the submission (uniform 404). The eBilling kill switch runs after each filter. The firm must also be "
            + "linked to the record (task 051, D-110: the record's assigned law firms only; 403 firm_not_linked, 503 when the link cannot be read) "
            + "-- decided in InvoiceSubmissionService.SubmitAsync, the one owner both channels call, not in this file. "
            + "Task 095 adds POST /api/finance/invoice-submissions/from-email (the Outlook add-in): the same Finance filter first "
            + "(finance.submit_invoice on the body's core record, finance.attach_invoice on its firm; a malformed body is a 400 before any "
            + "check), then the /office/save chain (OfficeAuth, EntityAccess entity.associate_document, rate limit, version-save "
            + "authorization), the kill switch and the idempotency filter bound to the submission key; firm_not_linked through "
            + "CheckFirmLinkAsync. And GET /api/finance/core-records/{entityType}/{entityId}/firms (finance.read on the core record, "
            + "uniform 404), which lists only the record's active linked firms."),
        new GovernedFile("Api/Finance/CodeSetMappingEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/code-set-mappings/* (ebilling-r1 task 048, FR-22) -- AI code-set proposals (GET) and the admin confirm that writes one "
            + "default code set (POST). Each route declares the Dataverse table privileges the caller must hold (AddFinanceAuthorizationFilter, "
            + "D-76, fail closed): Write on BOTH reference tables for the list, Write on the one table named in the path for the confirm; the "
            + "eBilling kill switch runs after it. Only the confirm route writes a mapping."),
        new GovernedFile("Api/Finance/SubmissionOverrideEndpoints.cs", Scope.RouteLevelGate,
            "/api/finance/submissions/{submissionId}/blocked, /override and /return (ebilling-r1 task 105, FR-19, FR-37, D-24, D-73, D-76) -- read, "
            + "override and return a blocked internal submission. Each route carries AddFinanceAuthorizationFilter as the caller on the submission "
            + "named in the path (Read for the read, uniform 404; Read and Write for the two decisions, ebilling.submission.decide); the eBilling kill "
            + "switch runs after it. The override additionally needs the caller's own Dataverse table privilege on sprk_ebillingoverride, asked of "
            + "Dataverse as the caller on EVERY call inside SubmissionOverrideService (EbillingOverridePrivilege, fail closed: 403 "
            + "ebilling.override.not_permitted); Return needs no privilege (D-73). The decider, the invoice, the findings and the decision id are "
            + "derived server-side, never taken from the request."),
        new GovernedFile("Api/Finance/InvoiceApprovalEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/invoices/{invoiceId}/approval and POST /api/finance/invoices/{invoiceId}/approve (ebilling-r1 task 103, FR-35, "
            + "FR-11, D-40, D-78) -- the approval state and the approve operation. Each route carries AddFinanceAuthorizationFilter as the caller on "
            + "the invoice named in the path (finance.read: Read; ebilling.invoice.decide: Write), fail closed; the eBilling kill switch runs after "
            + "it. Approve additionally resolves the caller's eligibility LIVE from the invoice's core record inside InvoiceApprovalService on every "
            + "call (403 ebilling.approver.not_eligible, nothing written); the stored eligible-approver set never grants. The decider and the "
            + "decision id are derived server-side, never taken from the request."),
        new GovernedFile("Api/Finance/InvoiceRejectReturnEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/finance/invoices/{invoiceId}/reject and POST /api/finance/invoices/{invoiceId}/return (ebilling-r1 task 104, FR-36, "
            + "FR-11, D-43, D-122) -- an eligible approver rejects an invoice (final) or returns it to the firm. Each route carries "
            + "AddFinanceAuthorizationFilter as the caller on the invoice named in the path (ebilling.invoice.decide: Write), fail closed; the "
            + "eBilling kill switch runs after it. Both additionally resolve the caller's eligibility LIVE from the invoice's core record inside "
            + "InvoiceRejectReturnService on every call (403 ebilling.approver.not_eligible, nothing written); the stored eligible-approver set "
            + "never grants. The decider and the decision id are derived server-side, never taken from the request."),
        new GovernedFile("Api/Finance/StoredFileEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/finance/files/{fileId}/content (ebilling-r1 task 090, EB-D-148, procedure-and-file-model 4.3) -- an internal caller downloads "
            + "a Stored File's bytes. AddStoredFileAccessFilter runs the download rule before the handler: step 0 (403 before any record read), "
            + "the app-only row read, the CALLER's own Read on the parent and, for a copy, on its source document (gate variant, uniform 404), "
            + "then StoredFileService.JudgeServableAsync: Servable / own path / scan verdict (404, 409 file_scan_pending, 503). The eBilling kill "
            + "switch runs after it."),
        new GovernedFile("Api/ScorecardCalculatorEndpoints.cs", Scope.RouteLevelGate,
            "matter/project grade recalculation, Read on the parent as the caller (task 130)."),
        new GovernedFile("Api/NavMapEndpoints.cs", Scope.RouteLevelGate,
            "Dataverse navigation metadata."),
        new GovernedFile("Api/RegistrationEndpoints.cs", Scope.RouteLevelGate,
            "the anonymous demo-request form and its admin approve/reject."),
        new GovernedFile("Api/Reporting/ReportingEndpoints.cs", Scope.RouteLevelGate,
            "/api/reporting/* — Power BI embed behind the module role; since task 166 r1 every report route reads (and "
            + "writes) its sprk_report row AS THE CALLER and binds the workspace from that row, never the request (sweep S-70, "
            + "S-71, S-81; amendment f). PUT /reports/{reportId} was replaced by PATCH."),
        new GovernedFile("Api/ResilienceEndpoints.cs", Scope.RouteLevelGate,
            "circuit-breaker diagnostics behind sign-in only (task-167 UNOWNED-NEW, assigned to 166)."),
        new GovernedFile("Api/Platform/KeylessProofEndpoints.cs", Scope.RouteLevelGate,
            "the stamp's keyless proof for provisioning's acceptance gate (customer-provisioning task 230b): one managed-identity "
            + "call per Azure service, behind an application role only the L2 Worker identity holds; no record is read or returned. "
            + "Task 260 (ISS-014) adds the stamp's read-only secure-record isolation census behind the same role: it reads the "
            + "security directory (business units, roles, users, teams) as the BFF and returns only the scheduled job's result "
            + "(status, verdict, findings) — no business record is read or returned, nothing is written."),
        new GovernedFile("Api/ExternalAccess/PersonAccessAdministrationEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/external-access/remove-person and /reinstate-person: person-level removal and reinstatement of external "
            + "access, behind AddExternalAccessAdministratorAuthorizationFilter (membership of the standing-grant administrators "
            + "field security profile, direct or through a team; the caller from WhoAmI over their own token). Every refusal, "
            + "fault or timeout is the same 403."),
        new GovernedFile("Api/UserEndpoints.cs", Scope.RouteLevelGate,
            "the caller's own profile and capabilities, read on behalf of the caller."),
        // Api/WorkAssignmentEndpoints.cs entry DELETED at the task-167 integration (2026-10-05): task 166 deleted the file
        // (POST /api/v1/work-assignments, sweep S-76; owner round 10 item 1). Absence pinned by the sweep ledger's ProofTest.
        new GovernedFile("Api/ConfigEndpoints.cs", Scope.RouteLevelGate,
            "anonymous public bootstrap configuration."),

        // ---- child records: added by task 147 r1 (owner round 28 item 1) ----
        new GovernedFile("Api/ChildRecordEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/child-records/{table} and PATCH /api/v1/child-records/{table}/{id} — the browser's child-record "
            + "creates and re-files (task 147 r1). The decision is in the handler, AS THE CALLER (IDataverseUserClient), because "
            + "the records to authorize are whatever the Web API payload binds: the table's Create/Append privilege, AppendTo "
            + "on EVERY bound record (an unappendable and a missing record answer the same 404), no field-secured or "
            + "server-owned column; a re-file also needs the row readable (uniform 404) and F3 to leave a secure record."),

        // ---- Office add-in ----
        new GovernedFile("Api/Office/OfficeEndpoints.cs", Scope.RouteLevelGate,
            "/api/office/* — save (EntityAccessFilter + OfficeVersionSave), jobs (JobOwnershipFilter), todo and quickcreate "
            + "(source-access filters), the impersonated entity search, reference data and an anonymous health check. "
            + "QuickCreateSourceAccessFilter gates the SOURCE read and passes through when no source is named; since task 166 "
            + "(S-69) the SAME filter also requires the caller's live CREATE privilege for the target table "
            + "(prvCreatesprk_Matter / prvCreatesprk_Project / prvCreatesprk_Invoice, CallerRecordAccessProbe."
            + "CallerHoldsPrivilegeAsync); the create stays app-only and team-owned (owner G5). POST /todo's "
            + "TodoSourceAccessFilter likewise requires prvCreatesprk_Todo since task 166 (amendment c)."),
        new GovernedFile("Api/Office/CommunicationsEndpoints.cs", Scope.RouteLevelGate,
            "/api/office/communications/* — every read runs through IDataverseUserClient as the caller (task 127)."),

        // ---- workspace ----
        new GovernedFile("Api/Workspace/WorkspaceEndpoints.cs", Scope.RouteLevelGate,
            "the caller's portfolio, health and briefing, and client-supplied scoring."),
        new GovernedFile("Api/Workspace/WorkspaceAiEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/workspace/ai/summary — Read on the named record through AddFinanceAuthorizationFilter (task 163; sweep "
            + "S-46)."),
        new GovernedFile("Api/Workspace/WorkspaceFileEndpoints.cs", Scope.RouteLevelGate,
            "text extraction and summary of uploaded files."),
        new GovernedFile("Api/Workspace/WorkspaceLayoutEndpoints.cs", Scope.RouteLevelGate,
            "layout CRUD scoped by owner in WorkspaceLayoutService, plus static section/template catalogs."),
        new GovernedFile("Api/Workspace/WorkspaceMatterEndpoints.cs", Scope.RouteLevelGate,
            "matter pre-fill and AI summary over caller-supplied files and fields."),
        new GovernedFile("Api/Workspace/WorkspaceProjectEndpoints.cs", Scope.RouteLevelGate,
            "project pre-fill over caller-supplied files."),
        // Api/Workspace/WorkspaceStateEndpoints.cs entry DELETED at the task-167 integration (2026-10-05): task 166 deleted
        // the file (GET /api/workspace/state, sweep S-82; owner round 10 item 1). Absence pinned by the sweep ledger's ProofTest.

        // ---- external access: the aggregator file itself, and what it binds ----
        new GovernedFile("Api/ExternalAccess/ExternalAccessEndpoints.cs", Scope.RouteLevelGate,
            "the aggregator's own routes: /me, /me/entitlements and the inline close-project (sweep S-39; task 166)."),
        new GovernedFile("Api/ExternalAccess/ExternalProjectDataEndpoints.cs", Scope.GroupGated,
            "the external data plane: every handler checks the caller principal's rights on the route record "
            + "(HoldsReadOnProject / RightsForRoot) before any read — the Wave-3 reference implementation."),
        new GovernedFile("Api/ExternalAccess/ExternalModuleDataEndpoints.cs", Scope.GroupGated,
            "scoped module reads (Tier2ScopeFilterInjector, IsRecordAccessible) and schema/view passthrough, under the nested "
            + "/api/dataverse group of /api/v1/external."),
        new GovernedFile("Api/ExternalAccess/ExternalMessageEndpoints.cs", Scope.GroupGated,
            "GET /api/v1/external/{root}/{id}/messages (spaarke-SPA-external-access-platform-r3 task 033, FR-04) on the "
            + "COLLABORATION group: the record is gated against the caller principal's accessible set through the module "
            + "registry (IsRecordAccessible; ScopeRows for invoices and the requester-scoped service requests) before the "
            + "app-only message read; an absent record and a foreign one are the same 403. POST (task 034, FR-11) runs the "
            + "same gate and also requires Create on the record (GetRecordRightsAsync) before the app-only thread post."),
        new GovernedFile("Api/ExternalAccess/ExternalIntakeEndpoints.cs", Scope.GroupGated,
            "POST /api/v1/external/servicerequests (spaarke-SPA-external-access-platform-r3 task 035, FR-05/FR-06) on the "
            + "COLLABORATION group: 403 unless the caller principal is entitled to the legal-front-door module "
            + "(ModuleEntitlementResolver.IsEntitledAsync — the /me/entitlements set, partner switch included) and has a "
            + "contact; the one row it writes app-only names that contact as sprk_requestedby, so it lands only in the "
            + "caller's own Service Requests set. Takes no record id."),
        new GovernedFile("Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs", Scope.GroupGated,
            "POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions, GET /api/v1/external/invoice-submissions"
            + "/{id} and GET /api/v1/external/invoice-submissions (ebilling-r1 task 044, FR-17 SPA channel, NFR-10) on the COLLABORATION "
            + "group. Each route runs AddExternalInvoiceSubmissionAuthorizationFilter: a submit needs Read and Create on the core "
            + "record (the principal's post-veto rights) and a firm among the caller contact's conferring memberships; a read needs the "
            + "submission's firm to be the caller's and Read on its core record (uniform 404); the list is the caller's firms on the "
            + "caller's readable records. The eBilling kill switch runs after the filter. Task 051 (D-106) adds GET "
            + "/api/v1/external/invoice-submissions/firm-records under the same filter (the firm must be one of the caller's ACTIVE "
            + "memberships; it lists only ids from the caller's own readable sets that the firm is linked to, at most 200 per kind from at "
            + "most 500 examined, with partial and truncated flags; a systemuser is refused), a systemuser refusal on a submit before any "
            + "write, and the firm-to-record link a submit requires. The filter's link check is an early copy: the owner is "
            + "InvoiceSubmissionService.SubmitAsync (D-110), so the internal route enforces the same rule with the same codes."),
        new GovernedFile("Api/ExternalAccess/ExternalRegistrationEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/external/registration (spaarke-SPA-external-access-platform-r3 task 047, ADR-028 A9) on its OWN "
            + "group: RequireAuthorization(ExternalCollaboration) WITHOUT CallerPrincipalAuthorizationFilter (the caller is "
            + "not yet resolved). The handler admits a WorkforceHome identity only (:114) and registers only the caller "
            + "itself, keyed by its own token tid/oid/email; it takes no record id."),
        new GovernedFile("Api/ExternalAccess/ExternalWorkforceLinkEndpoints.cs", Scope.RouteLevelGate,
            "POST /api/v1/external/workforce/link (spaarke-SPA-external-access-platform-r3 task 048, ADR-028 A9) on its OWN "
            + "group: RequireAuthorization(ExternalCollaboration) WITHOUT CallerPrincipalAuthorizationFilter (a licensed "
            + "user's home identity is not yet linked). The handler admits a WorkforceHome identity only (:107); the body's "
            + "Spaarke-tenant guest token is validated with the default scheme's own parameters plus the A9 conditions (:133), "
            + "and the link is written once onto THAT guest's enabled systemuser, keyed by the bearer's own tid/oid (:147). "
            + "It takes no record or systemuser id."),
        new GovernedFile("Api/ExternalAccess/ExternalNotificationFeedEndpoints.cs", Scope.GroupGated,
            "GET /api/v1/external/notifications and POST /api/v1/external/notifications/seen "
            + "(spaarke-SPA-external-access-platform-r3 task 037, FR-08) on the COLLABORATION group. Neither takes an id: the "
            + "feed is the caller principal's own (grants to its contact, each re-checked with CallerPrincipal.RightsOn; "
            + "messages only on its accessible set and own service requests) and the marker is keyed by its own id."),
        new GovernedFile("Api/ExternalAccess/GrantExternalAccessEndpoint.cs", Scope.GroupGated,
            "POST /grant under the DelegationRuleFilter admin group."),
        new GovernedFile("Api/ExternalAccess/RevokeExternalAccessEndpoint.cs", Scope.GroupGated,
            "POST /revoke under the DelegationRuleFilter admin group (a sweep-refuted route)."),
        new GovernedFile("Api/ExternalAccess/SetRecordShareExpiryEndpoint.cs", Scope.GroupGated,
            "POST /set-record-share-expiry under the DelegationRuleFilter admin group (task 098)."),
        new GovernedFile("Api/ExternalAccess/RecordAccessGateEndpoint.cs", Scope.GroupGated,
            "GET /can-manage-access — its whole purpose is to be gated by DelegationRuleFilter (task 118)."),
        new GovernedFile("Api/ExternalAccess/RecordGrantsEndpoint.cs", Scope.GroupGated,
            "GET /record-grants under the DelegationRuleFilter admin group (task 099a, #1010(a)): the record's active grant rows "
            + "for Manage Access's Current Access, behind the /can-manage-access rule (Write and Share on the record, as the "
            + "caller); the row read is app-only, so the filter is the whole authorization."),
        new GovernedFile("Api/ExternalAccess/InternalShareEndpoints.cs", Scope.GroupGated,
            "internal POA share/unshare/list under the DelegationRuleFilter admin group (task 063)."),
        new GovernedFile("Api/ExternalAccess/InviteExternalUserEndpoint.cs", Scope.GroupGated,
            "POST /invite under the DelegationRuleFilter admin group."),
        new GovernedFile("Api/ExternalAccess/InviteAndGrantExternalUserEndpoint.cs", Scope.GroupGated,
            "POST /invite-and-grant under the DelegationRuleFilter admin group."),
        new GovernedFile("Api/ExternalAccess/ProvisionProjectEndpoint.cs", Scope.GroupGated,
            "POST /provision-project under the DelegationRuleFilter admin group."),
        new GovernedFile("Api/ExternalAccess/UnsecureProjectEndpoint.cs", Scope.GroupGated,
            "POST /unsecure-project under the DelegationRuleFilter admin group (task 061)."),
        new GovernedFile("Api/ExternalAccess/NoAccessEnforceEndpoint.cs", Scope.GroupGated,
            "POST /no-access/enforce under the DelegationRuleFilter admin group (task 143): removes the direct POA shares one No "
            + "Access entry walls off; the filter's NoAccessEnforceRequest target is the sprk_noaccessentries row (Write on "
            + "the ENTRY; an absent entry and an unwritable one are the same 403)."),
        new GovernedFile("Api/ExternalAccess/RecordNoAccessEndpoint.cs", Scope.RouteLevelGate,
            "GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{recordId}/no-access (task 064, owner round 59 "
            + "item 3): each route carries RecordRouteAccessAuthorizationFilter on its fixed entity set with the 'read' key "
            + "(no Read, an absent record and any probe fault are the uniform 404); the entries are added only when the rights "
            + "that same probe returned include Write (owner O2). Not on the admin group: DelegationRuleFilter demands Write."),
        new GovernedFile("Api/Records/RecordFamilyEndpoints.cs", Scope.RouteLevelGate,
            "GET /api/v1/records/{table}/{id}/family (field-mapping-server-write-path-r1 task 072, FR-31): a record's Filing family, "
            + "read AS THE CALLER by RecordFamilyReader. The gate is in the HANDLER (HandlerDecisions): the route supplies the "
            + "table, which RecordRouteAccessAuthorizationFilter's fixed-entity-set form cannot take, and its declared-target form "
            + "denies with a 403, the wrong shape. The handler asks the caller's Read on the anchor through "
            + "CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync (an unknown id and a denied one take the same work, "
            + "#2137); no Read, an unknown record, a non-family table and any probe fault are the uniform 404."),
        new GovernedFile("Api/ExternalAccess/AssignedAccessSyncEndpoint.cs", Scope.GroupGated,
            "POST /assigned-access/sync, GET /assigned-access and POST /assigned-access/dismiss under the DelegationRuleFilter "
            + "admin group (task 142): the Assigned-To rule for one root record — Write on the RECORD; an unknown id and an "
            + "unwritable one are the same 403."),
        new GovernedFile("Api/ExternalAccess/ContactGrantEndpoints.cs", Scope.GroupGated,
            "/api/v1/external/contact-grants (POST grant, GET list, POST revoke) on the COLLABORATION group (task 140, #1063): "
            + "a Collaborate/Full Access contact grants colleagues of its own organization at or below its own level, lists "
            + "and revokes what it issued. Each route carries AddContactGrantorAuthorizationFilter; a systemuser is refused "
            + "(Manage Access is theirs)."),
        new GovernedFile("Api/ExternalAccess/ProjectClosureEndpoint.cs", Scope.NotMapped,
            "MapProjectClosureEndpoint has ZERO callers; the live close-project is mapped inline in ExternalAccessEndpoints.cs. "
            + "Its Handle method is still used. TheDeadClosureRegistrationIsNotMapped fails if it gains a caller."),

        // ---- SPE admin: the 19 files the /api/spe aggregator binds ----
        new GovernedFile("Api/SpeAdmin/AuditLogEndpoints.cs", Scope.GroupGated, "the SPE admin audit log."),
        new GovernedFile("Api/SpeAdmin/BulkOperationEndpoints.cs", Scope.GroupGated,
            "SPE bulk delete/permissions with configId in the BODY (sweep S-44, S-72; task 165)."),
        new GovernedFile("Api/SpeAdmin/BusinessUnitEndpoints.cs", Scope.GroupGated, "the business-unit picker for SPE admin."),
        new GovernedFile("Api/SpeAdmin/ConfigEndpoints.cs", Scope.GroupGated,
            "container-type config CRUD, route param 'id' bypassing the tenant scope (sweep S-45, S-73..S-75; task 165)."),
        new GovernedFile("Api/SpeAdmin/ConsumingTenantEndpoints.cs", Scope.GroupGated, "consuming-tenant registrations."),
        new GovernedFile("Api/SpeAdmin/ContainerColumnEndpoints.cs", Scope.GroupGated, "container column definitions."),
        new GovernedFile("Api/SpeAdmin/ContainerCustomPropertyEndpoints.cs", Scope.GroupGated, "container custom properties."),
        new GovernedFile("Api/SpeAdmin/ContainerEndpoints.cs", Scope.GroupGated, "container lifecycle (static binding form)."),
        new GovernedFile("Api/SpeAdmin/ContainerItemEndpoints.cs", Scope.GroupGated,
            "container item bytes, sharing, delete and upload — moved onto the group by task 091."),
        new GovernedFile("Api/SpeAdmin/ContainerPermissionEndpoints.cs", Scope.GroupGated, "container permission CRUD."),
        new GovernedFile("Api/SpeAdmin/ContainerTypeEndpoints.cs", Scope.GroupGated, "container types and registration."),
        new GovernedFile("Api/SpeAdmin/ContainerTypePermissionEndpoints.cs", Scope.GroupGated, "container-type permissions and owners."),
        new GovernedFile("Api/SpeAdmin/ContainerTypeSettingsEndpoints.cs", Scope.GroupGated, "container-type settings."),
        new GovernedFile("Api/SpeAdmin/DashboardEndpoints.cs", Scope.GroupGated, "the cross-config SPE dashboard (nested group)."),
        new GovernedFile("Api/SpeAdmin/EnvironmentEndpoints.cs", Scope.GroupGated, "SPE environment records."),
        new GovernedFile("Api/SpeAdmin/RecycleBinEndpoints.cs", Scope.GroupGated, "container and item recycle bins."),
        new GovernedFile("Api/SpeAdmin/SearchContainersEndpoints.cs", Scope.GroupGated, "container search across a config's containers."),
        new GovernedFile("Api/SpeAdmin/SearchItemsEndpoints.cs", Scope.GroupGated, "drive-item search inside a config's containers."),
        new GovernedFile("Api/SpeAdmin/SecurityEndpoints.cs", Scope.GroupGated, "SPE security alerts and score (nested group)."),

        // ---- outside Api/** ----
        new GovernedFile("Endpoints/Onboarding/ConsentCallbackEndpoint.cs", Scope.RouteLevelGate,
            "the anonymous, HMAC-verified admin-consent callback; path from a const."),
        new GovernedFile("Infrastructure/DI/EndpointMappingExtensions.cs", Scope.RouteLevelGate,
            "the health probes (/healthz via MapHealthChecks, /ping, /status, the Dataverse probes). The anonymous "
            + "GET /healthz/dataverse/doc/{id} the sweep never traced was DELETED by task 166 (amendment a)."),
    };

    // Census: the total routing surface. A new endpoint file must be classified before the build goes green.
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
    //
    // 117 -> 116 (2026-09-07, unified-access-control-r2 task 083). A DOWNWARD move, the census's fifth
    // firing and the second in the delete direction:
    //
    //   083  -1  Api/DocumentsEndpoints.cs DELETED. Task 090 had already removed six of its eight
    //            routes (unsatisfiable "canmanagecontainers" on collection endpoints); 083 removed the
    //            last two — PUT /api/drives/{driveId}/upload and DELETE /api/drives/{driveId}/items/
    //            {itemId} — so the file had no Map{Verb} call left and was deleted outright.
    //
    // Unlike the 110 -> 117 firing above, this one moves a GOVERNED file OUT of the set, so the same
    // discipline applies in reverse: its GovernedFiles entry (Scope.RouteLevelGate) was DELETED in the
    // same edit, and so were its two Pending/"UNOWNED" waivers. Bumping the count alone would have left
    // Rule A scanning a path that no longer exists — which ScanFile treats as unparseable and therefore
    // FAILS on, so in this direction the census and the governed set cannot silently disagree. That is
    // the opposite of the ComposeEndpoints case, where the count could have gone green while the guarded
    // surface escaped; worth recording that the two directions have different failure modes.
    //   061  +1  Api/ExternalAccess/UnsecureProjectEndpoint.cs ADDED — POST /unsecure-project, the
    //            reverse of secure-project provisioning (design §5.1 "the designation is reversible").
    //            Classified per the maintenance procedure: it serves NEITHER document metadata nor file
    //            bytes. It mutates a Dataverse sprk_project's ownership, POA shares and sprk_issecure
    //            flag, so there is no GovernedFiles entry to add — the count alone moves.
    //
    //            Its authorization is NOT weaker for being outside GovernedFiles: the route sits in the
    //            external-access admin group, so it inherits AddDelegationRuleFilter() — the FR-07
    //            Write-on-the-target check evaluated as the CALLER over OBO — exactly as
    //            /provision-project does. DelegationRuleFilter's target map gained the matching
    //            UnsecureProjectRequest case in the same change; without it the filter would resolve no
    //            target and deny every call, which is fail-closed but reads as a bug.
    //
    // 117 -> 118 (2026-09-11, unified-access-control-r2 task 098):
    //
    //   098  +1  Api/ExternalAccess/SetRecordShareExpiryEndpoint.cs ADDED — POST /set-record-share-expiry,
    //            the Manage Access toolbar Expiration (spec FR-33): one expiry written to every active
    //            sprk_externalrecordaccess row of a record in one transaction. Classified per the maintenance
    //            procedure: it serves NEITHER document metadata nor file bytes — it mutates the expiry of
    //            share rows — so there is no GovernedFiles entry to add; the count alone moves.
    //
    //            Same shape as 061: the route sits in the external-access admin group, so it inherits
    //            AddDelegationRuleFilter() (Write on the record, evaluated as the CALLER over OBO), and
    //            DelegationRuleFilter's target map gained the matching SetRecordShareExpiryRequest case in
    //            the same change. Caught by this census at review time, before CI — it is doing its job.
    //
    // 118 -> 119 (2026-09-15, unified-access-control-r2 task 063):
    //
    //   063  +1  Api/ExternalAccess/InternalShareEndpoints.cs ADDED — POST /share-user, POST /unshare-user and
    //            GET /user-shares, the server half of the Manage Access "+ User" picker (spec FR-29): internal
    //            system-user POA shares on a project, matter or work assignment. Classified per the maintenance
    //            procedure: it serves NEITHER document metadata nor file bytes — it writes and lists POA shares on
    //            root records — so there is no GovernedFiles entry to add; the count alone moves.
    //
    //            Same shape as 061 and 098: the routes sit in the external-access admin group and inherit
    //            AddDelegationRuleFilter() (Write on the record, evaluated as the CALLER over OBO), and
    //            DelegationRuleFilter's target map gained one case per request type in the same change —
    //            ShareRecordWithUserRequest, UnshareRecordWithUserRequest and the GET's RecordUserSharesQuery.
    // 119 -> 120 (2026-09-21, unified-access-control-r2 task 118):
    //
    //   118  +1  Api/ExternalAccess/RecordAccessGateEndpoint.cs ADDED — GET /can-manage-access, which answers
    //            the delegation question for ONE record so the Manage Access affordance can gate on the rule
    //            the server enforces instead of on a table-level Create privilege that asked a different
    //            question with the opposite fail direction (spec FR-07 / owner decision D-1 option C).
    //            Classified per the maintenance procedure: it serves NEITHER document metadata nor file bytes —
    //            it returns one boolean about a root record — so there is no GovernedFiles entry to add; the
    //            count alone moves. Same shape as 061, 098 and 063.
    //
    //            Worth stating because this file is unlike its neighbours: it is the first route whose ENTIRE
    //            PURPOSE is to be gated. It carries no rights logic, and its 200 means only "AddDelegationRuleFilter()
    //            let me through" — so for this one file, being inside the group is not merely how it is protected,
    //            it is how it is CORRECT. DelegationRuleFilter's target map gained the matching
    //            RecordAccessGateQuery case in the same change; without it the route would default-deny and the
    //            affordance would vanish for every user. Both directions are pinned by
    //            tests/integration/auth/UnifiedAccessControl/RecordAccessGateTests.cs.
    //
    // 120 -> 121 (2026-10-02, unified-access-control-r2 task 143):
    //
    //   143  +1  Api/ExternalAccess/NoAccessEnforceEndpoint.cs ADDED — POST /no-access/enforce, which enforces one
    //            No Access entry now: it removes the direct POA shares the entry walls off on secure records (owner
    //            Q4). Classified per the maintenance procedure: it serves NEITHER document metadata nor file bytes —
    //            it removes POA shares on root records and returns a report — so there is no GovernedFiles entry to
    //            add; the count alone moves. Same shape as 061, 098, 063 and 118: the route sits in the
    //            external-access admin group and inherits AddDelegationRuleFilter(). DelegationRuleFilter's target map
    //            gained the matching NoAccessEnforceRequest case in the same change — its target is the
    //            sprk_noaccessentries row, so the caller needs Write on the ENTRY; an absent entry and an unwritable one
    //            are the same 403. Pinned through the real pipeline by
    //            tests/integration/auth/UnifiedAccessControl/NoAccessEnforceEndpointTests.cs.
    //
    // 121 -> 122 (2026-10-03, unified-access-control-r2 task 142):
    //
    //   142  +1  Api/ExternalAccess/AssignedAccessSyncEndpoint.cs ADDED — POST /assigned-access/sync (applies the
    //            Assigned-To rule to one root record: grants/shares for its "Assigned *" subjects, after re-applying
    //            its No Access entries), GET /assigned-access (that record's Assigned-To ledger: suggestions,
    //            provenance, residual read-time terms) and POST /assigned-access/dismiss (declines one suggestion).
    //            Classified per the maintenance procedure: they serve NEITHER document metadata nor file bytes — they
    //            write and report grant/share state on root records, like /user-shares (063) — so there is no
    //            GovernedFiles entry to add; the count alone moves. Same shape as 063, 118 and 143: the routes sit in
    //            the external-access admin group and inherit AddDelegationRuleFilter(); DelegationRuleFilter's target
    //            map gained the AssignedAccessSyncRequest, AssignedAccessListQuery and AssignedAccessDismissRequest
    //            cases in the same change (Write on the RECORD; an unknown id and an unwritable one are the same 403).
    //            Pinned through the real pipeline by
    //            tests/integration/auth/UnifiedAccessControl/AssignedAccessSyncEndpointTests.cs.
    //
    // 122 -> 120 (2026-10-04, sweep integration of unified-access-control-r2 task 160, GitHub #1099; written as
    // 120 -> 118 on the task branch, before 142 and 143 landed here). A DOWNWARD move, the census's third firing in the
    // delete direction:
    //
    //   160  -2  Api/Dataverse/FetchEndpoints.cs and Api/Dataverse/RecordEndpoints.cs DELETED — the
    //            internal POST /api/dataverse/fetch and GET /api/dataverse/record/{entityLogicalName}/
    //            {id:guid}. Route sweep findings #9 and #10 (both critical): each ran the caller's query
    //            APP-ONLY behind DataverseAuthorizationFilter, which checks a table privilege at any depth
    //            and never a record. Owner round 10 item 1: no caller in the repo, in no published API
    //            description, so deleted rather than fixed. Neither file was in GovernedFiles and neither
    //            had a waiver, so the count alone moves. Deny proof:
    //            Sprk.Bff.Api.IntegrationTests.Api.Dataverse.DataverseProxyRoutesRemovedTests.
    //
    // 120 -> 119 (2026-10-04, sweep integration of unified-access-control-r2 task 163; written as 120 -> 119 on the task
    // branch). A DOWNWARD move:
    //
    //   163  -1  Api/Ai/AdminKnowledgeEndpoints.cs DELETED with all three of its routes (POST
    //            /api/admin/knowledge/index-references, POST and DELETE /index-reference/{knowledgeSourceId})
    //            under owner round 10 item 1 — no caller in the repo and in no published API description.
    //            The file was NOT in GovernedFiles, so there is no entry to remove. Api/Ai/KnowledgeBaseEndpoints.cs
    //            lost four routes in the same task but keeps GET /indexes/health, so it is still counted.
    //
    // 119 -> 117 (2026-10-04, sweep integration of unified-access-control-r2 task 164, owner round 10 item 1; written as
    // 120 -> 118 on task 164's own branch, and 119 -> 117 on task 163's fix round that merged it). A DOWNWARD move:
    //
    //   164  -1  Api/Ai/PromptLibraryEndpoints.cs DELETED — all six /api/ai/prompts routes retired (no caller in
    //            the repo, not in any published API description; sweep findings #56 and #57). Never governed.
    //        -1  Api/Ai/RecordMatchEndpoints.cs DELETED — POST /api/ai/document-intelligence/match-records and
    //            /associate-record retired on the same rule (sweep #31, #32). Never governed.
    //         0  GET /api/ai/playbooks/by-name/{name} and PUT /api/ai/playbooks/{id:guid}/nodes/reorder were
    //            retired too, but their files (PlaybookEndpoints.cs, NodeEndpoints.cs) still map other routes, so
    //            the census cannot see them. Absence is pinned by
    //            tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs.
    //
    // Reconcile at integration: sibling sweep tasks (159-169) move this count too; the merged value is the
    // master count after every retired and added file, recounted, not a sum of deltas.
    //
    // 117 -> 118 (2026-10-04, unified-access-control-r2 task 147 r1c, owner round 28 item 1 + round 36; written as
    // 122 -> 123 on the task branch, before 160, 163 and 164 landed here):
    //
    //   147  +1  Api/ChildRecordEndpoints.cs ADDED — the browser's child-record writes through the BFF (G5 create,
    //            re-file through the shared core). It writes Dataverse content keyed by caller-chosen ids, so it IS
    //            governed: classified HandlerAuthorized above, with the reason. The re-file routes the events and
    //            communications families gained (PATCH /api/v1/events/{id}/filing — round 36, after task 159 deleted
    //            the general PUT — and PATCH /api/communications/{id}/filing) live in existing files and call the same
    //            handler, so the census cannot see them.
    //
    // 118 -> 119 (2026-10-05, sweep integration of unified-access-control-r2 task 140; written as 122 -> 123 on the task
    // branch, before 160, 163, 164 and 147 landed here):
    //
    //   140  +1  Api/ExternalAccess/ContactGrantEndpoints.cs ADDED — POST/GET /api/v1/external/contact-grants and
    //            POST /api/v1/external/contact-grants/revoke: a Collaborate/Full Access CONTACT grants colleagues of its
    //            own organization, lists and revokes what it issued. Unlike 143/142 these are on the COLLABORATION group
    //            (a contact can authenticate nowhere else) and they WRITE access, so the file IS governed:
    //            RouteLevelGate, every route carrying AddContactGrantorAuthorizationFilter. Pinned by
    //            tests/integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.cs (handlers + host routes).
    //
    // 119 -> 117 (2026-10-05, sweep integration of unified-access-control-r2 task 166, route-authorization sweep; written as
    // 120 -> 118 on the task branch, before 160, 163, 164, 147 and 140 landed here). A DOWNWARD move — two route files
    // DELETED, each because its only route had no caller in the repository and is in no published API description
    // (owner round 10 item 1):
    //
    //   166  -1  Api/WorkAssignmentEndpoints.cs — POST /api/v1/work-assignments (S-76): app-only create with
    //            ownerid = a caller-chosen user, no Create/AppendTo check, app-authored notification to anyone.
    //   166  -1  Api/Workspace/WorkspaceStateEndpoints.cs — GET /api/workspace/state (S-82): read any chat session's
    //            workspace tabs with no session-owner check.
    //
    // Neither file was in GovernedFiles, so this is a pure count move. Other routes task 166 deleted sat in files
    // that keep routes and so do not move the census: POST /api/compose/documents/{documentSpeId}/promote
    // (ComposeDocumentEndpoints.cs), GET /api/memory/records/{entityLogicalName}/{id:guid} (MemoryGovernanceEndpoints.cs)
    // and GET /healthz/dataverse/doc/{id} (Infrastructure/DI/EndpointMappingExtensions.cs). Rule A and NoWaiverIsStale
    // are what see those; DeadRouteRetirementTests asserts they stay gone. PUT /api/v1/documents/{id}
    // (DataverseDocumentsEndpoints.cs), which the task branch also retired, is KEPT at this integration: task 147 r1 gave
    // it a caller (the Compose document association), so it stays behind 146's AppendTo check and now refuses the
    // storage-pointer fields — the condition task 166 set for any body-bound document update.
    //
    // 117 -> 117 (2026-10-05, integration of unified-access-control-r2 task 167 into batch 4; written as 120 -> 120 on the task
    // branch, before 143, 142, 160, 163, 164, 147, 140 and 166 landed here). The COUNT did not move; its meaning did.
    // GovernedFiles grew from 25 entries to ALL 117 — the census set and the governed set are now asserted EQUAL, so this
    // number is a sanity check on a set that is pinned file by file. The history lines above say "no GovernedFiles entry to
    // add" for 143, 142 and 063-style files and "HandlerAuthorized" for 147: both describe the guard of THEIR day; in this
    // structure every census file has an entry (the four added since the task-167 branch point — NoAccessEnforceEndpoint,
    // AssignedAccessSyncEndpoint, ContactGrantEndpoints, ChildRecordEndpoints — included) and the seven files 160, 163, 164
    // and 166 deleted have none. Two vocabulary changes on the task branch, both verified to leave the count unchanged:
    // EndpointFiles() now selects by the scanner's own registration vocabulary (Map{Verb}, MapMethods, MapHealthChecks — a
    // file registering only MapMethods would have been invisible), and it reads code with comments AND string literals
    // blanked (a log message naming ".MapGet(" no longer counts).
    //
    // 117 -> 116 (2026-10-06, customer-provisioning-orchestration-r1 task 227f). A DOWNWARD move:
    //
    //   227f  -1  Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs DELETED with ITenantContainerResolver — GET
    //             /api/diagnostics/tenant-container-resolver (the I4 diagnostic task 081 scoped) had no consumer left: the
    //             L2 probe that called it was unregistered and H13's I4 reads the deployed app settings since T227c.
    //             SpeContainerOwnershipGuard is the BFF's one definition of this stamp's containers (T227d). Its
    //             GovernedFiles entry and its P:OperatorGateInHandler waiver went with it — a route that no longer exists
    //             needs no gate.
    //
    // 116 -> 117 (2026-10-06, customer-provisioning-orchestration-r1 task 230b). An UPWARD move:
    //
    //   230b  +1  Api/Platform/KeylessProofEndpoints.cs — POST /api/platform/keyless-proof, the stamp's keyless proof
    //             for provisioning's acceptance gate (H13). GovernedFiles entry, AdminMechanism
    //             (AddKeylessProofAuthorizationFilter), AdminOnlyRoutes group and ClaimOnlyFilters entry added with it.
    //
    // 117 -> 118 (2026-10-08, unified-access-control-r2 task 064, owner round 59 item 3; merged with the 227f/230b
    // moves above — they net to zero, so the merged count is 118):
    //
    //   064  +1  Api/ExternalAccess/RecordNoAccessEndpoint.cs ADDED — GET /api/v1/records/{type}/{recordId}/no-access for
    //            project, matter and work assignment: whether the record is Secure and under No Access (every caller with
    //            Read; task 153's banner) and, for a caller who also holds Write, the covering entries (task 067). Governed
    //            RouteLevelGate: each of its three routes carries RecordRouteAccessAuthorizationFilter on a constant entity
    //            set. Pinned through the real pipeline by tests/integration/auth/UnifiedAccessControl/RecordNoAccessEndpointTests.cs.
    //
    // 118 -> 119 (2026-10-07, spaarke-ontology-platform-r1 task 036; renumbered at a master merge 2026-10-08). Net +1: Api/Signals/DecisionPlanEndpoints.cs ADDED --
    // GET /api/v1/signals/{signalId:guid}/decision-plan. Classified by GovernedFiles (RouteLevelGate) and HandlerDecisions
    // below. Ontology task 036 owns only these three ledger edits (this count, one GovernedFiles entry, one
    // HandlerDecision); other ontology routes (038, 043, 044, 046 ...) add their own and the main session reconciles.
    //
    // 119 -> 120 (2026-10-09, spaarke-ontology-platform-r1 task 071). Net +1: Api/InquiryEndpoints.cs ADDED -- POST
    // /api/v1/inquiries/{serviceRequestId:guid}/disposition. Classified by GovernedFiles (RouteLevelGate); its record-level
    // decision is the already-credited fixed-entity-set form of RecordRouteAccessAuthorizationFilter. The main session
    // reconciles the count with the other ontology routes at integration.
    //
    // 120 -> 121 (2026-10-11, spaarke-ontology-platform-r1 task 043). Net +1: Api/Signals/DecisionCommitEndpoints.cs ADDED --
    // POST /api/v1/signals/{signalId:guid}/decision. Classified by GovernedFiles (RouteLevelGate) and HandlerDecisions below.
    // The main session reconciles the count with the other ontology routes at integration.
    //
    // The three moves below landed on master in parallel with the three ontology moves above, each side counting from 118.
    // Their files are disjoint, so at the 2026-10-14 master -> ontology merge the count is 118 + 3 + 3 = 124.
    //
    // 118 -> 119 (2026-10-10, spaarke-SPA-external-access-platform-r3 task 033, FR-04):
    //
    //   033  +1  Api/ExternalAccess/ExternalMessageEndpoints.cs ADDED — GET /api/v1/external/{root}/{id}/messages, the
    //            message thread regarding a project / matter / work assignment / invoice / own service request, on the
    //            COLLABORATION group. Governed GroupGated with a HandlerDecision (IsRecordAccessible through
    //            GetMessagesCoreAsync -> IsRootRecordReadableAsync). Pinned by
    //            tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageEndpointsTests.cs.
    //
    // 119 -> 120 (2026-10-11, spaarke-SPA-external-access-platform-r3 task 035, FR-05/FR-06):
    //
    //   035  +1  Api/ExternalAccess/ExternalIntakeEndpoints.cs ADDED — POST /api/v1/external/servicerequests, the Legal
    //            Front Door intake submit, on the COLLABORATION group. Governed GroupGated with a HandlerDecision (the
    //            legal-front-door module entitlement, ModuleEntitlementResolver.IsEntitledAsync, before the app-only create).
    //            Pinned by tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs.
    //
    // 120 -> 121 (2026-10-12, spaarke-SPA-external-access-platform-r3 task 037, FR-08):
    //
    //   037  +1  Api/ExternalAccess/ExternalNotificationFeedEndpoints.cs ADDED — GET /api/v1/external/notifications (the
    //            derived in-portal feed) and POST /api/v1/external/notifications/seen (the caller's last-seen marker), on the
    //            COLLABORATION group. Governed GroupGated; both routes take no id and are Permanent CallerScopedOnly (the
    //            /me precedent). Pinned by tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalNotificationFeedTests.cs.
    //
    // 121 + 121 -> 124 (2026-10-14, merge of master into docs/ontology-platform-design): the union of the two
    // 118 -> 121 sequences above (ontology 036/071/043 + external-access-r3 033/035/037). No route added or removed here.
    //
    // 124 -> 125 (2026-10-14, spaarke-ontology-platform-r1 task 038). Net +1: Api/Signals/SignalReadEndpoints.cs ADDED --
    // GET /api/v1/signals/worklist. Classified by GovernedFiles (RouteLevelGate) and HandlerDecisions below.
    //
    // 125 -> 126 (2026-10-15, spaarke-SPA-external-access-platform-r3 task 047, ADR-028 A9):
    //
    //   047  +1  Api/ExternalAccess/ExternalRegistrationEndpoints.cs ADDED — POST /api/v1/external/registration, a
    //            WorkforceHome caller's one-time registration, on its OWN /api/v1/external group (ExternalCollaboration,
    //            no CallerPrincipalAuthorizationFilter). Governed RouteLevelGate; Permanent CallerScopedOnly (it registers
    //            only the caller, from its own token). Pinned by
    //            tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs and by
    //            AuthenticationSchemeCensusTests (the route pin and the only-caller rule).
    //
    // 126 -> 127 (2026-10-16, spaarke-SPA-external-access-platform-r3 task 048, ADR-028 A9):
    //
    //   048  +1  Api/ExternalAccess/ExternalWorkforceLinkEndpoints.cs ADDED — POST /api/v1/external/workforce/link, a
    //            licensed user's stored home link (dual-token proof), on its OWN /api/v1/external group
    //            (ExternalCollaboration, no CallerPrincipalAuthorizationFilter). Governed RouteLevelGate; Permanent
    //            CallerScopedOnly (it links only the caller's own two identities, from its own two tokens). Pinned by
    //            tests/integration/auth/ExternalAccess/ExternalWorkforceLinkEndpointsTests.cs and by
    //            AuthenticationSchemeCensusTests (the route pin and the only-caller rule).
    //
    // 125 -> 126 (2026-10-16, spaarke-ontology-platform-r1 task 104). Net +1: Api/Ontology/OntologyAdminEndpoints.cs ADDED --
    // GET /api/v1/ontology/admin/capabilities. Classified by GovernedFiles (RouteLevelGate) and a Permanent CallerScopedOnly waiver.
    // The main session reconciles the count with the other ontology routes at integration.
    //
    // 126 + 127 -> 128 (2026-10-16, merge of master into work/spaarke-SPA-external-access-platform-r3): the union of the
    // two 125 -> sequences above (external-access-r3 047/048 + ontology 104). No route added or removed here.
    //
    // 126 -> 127 (2026-10-16, spaarke-ontology-platform-r1 task 100). Net +1: Api/Ontology/OntologyAdminRulesEndpoints.cs ADDED --
    // GET /api/v1/ontology/admin/rules, GET /rules/{policyId}, PUT /rules/{policyId}/enabled, POST /rules/{policyId}/retire. Bound by the
    // new Aggregator OntologyAdminEndpoints.cs (GroupGated). Classified by GovernedFiles and four HandlerDecisions below.
    // The main session reconciles the count with the other ontology routes (tasks 101, 103) at integration.
    //
    // 127 -> 128 (2026-10-16, spaarke-ontology-platform-r1 task 101). Net +1: Api/Ontology/OntologyAdminAuthoringEndpoints.cs ADDED --
    // the four rule-authoring routes on the Ontology admin group (GroupGated; OntologyAdminEndpoints.cs becomes a pinned Aggregator).
    // Classified by GovernedFiles and four HandlerDecisions (IDataverseUserClient).
    //
    // 128 -> 129 (2026-10-16, spaarke-ontology-platform-r1 task 103). Net +1: Api/Ontology/OntologyAdminClassificationEndpoints.cs ADDED --
    // GET/POST /api/v1/ontology/admin/classification/categories, PATCH /categories/{categoryId:guid} and PUT /categories/{categoryId:guid}/enabled.
    // Classified by GovernedFiles (RouteLevelGate, its own group) and one HandlerDecision per route below.
    //
    // Integration (2026-10-16): the 126 of task 104 + 100 + 101 + 103 = 129 (one file each; verified by the census pin test).
    //
    // 128 + 129 -> 131 (2026-10-16, merge of master e702c3401 into work/spaarke-SPA-external-access-platform-r3): master's 129
    // (ontology 104/100/101/103 on 125) plus external-access-r3's two (047 registration, 048 workforce link). No route added or removed here.
    //
    // 129 -> 130 (2026-10-16, unified-access-control-r2 task 099a, after ontology #2044 merged, #1010(a)):
    //
    //   099a +1  Api/ExternalAccess/RecordGrantsEndpoint.cs ADDED — GET /record-grants, the record's active
    //            sprk_externalrecordaccess rows for Manage Access's Current Access (the PCF read them as the user, so a
    //            Write-holder in a child business unit saw none of the root-owned rows). Governed GroupGated: it sits in
    //            the external-access admin group and inherits AddDelegationRuleFilter(); DelegationRuleFilter's target map
    //            gained the matching RecordGrantsQuery case in the same change (the /can-manage-access rule — Write and
    //            Share on the RECORD; an unknown id and an unmanageable one are the same 403). Pinned through the real
    //            pipeline by tests/integration/auth/UnifiedAccessControl/RecordGrantsEndpointTests.cs.
    //
    // 125 -> 126 (2026-10-16, ebilling-r1 task 078, FR-29). Net +1: Api/Finance/RateCardImportEndpoints.cs ADDED -- GET
    // /api/finance/ratecards/import-template, POST /api/finance/ratecards/import and POST /api/finance/ratecards/{rateCardId}/publish.
    // Classified by GovernedFiles (RouteLevelGate: each route's AddFinanceAuthorizationFilter requires the caller's own Dataverse
    // table privilege, D-76); the kill-switch filter .RequireEbillingEnabled is classified in NotAuthorizationForms.
    //
    // 126 -> 127 (2026-10-16, ebilling-r1 task 106, FR-33). Net +1: Api/Finance/InvoiceContextEndpoints.cs ADDED -- GET
    // /api/finance/invoices/{invoiceId}/context. Classified by GovernedFiles (RouteLevelGate: AddFinanceAuthorizationFilter, finance.read on the
    // route's invoice); the kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms.
    // 127 -> 129 (2026-10-16, ebilling-r1 task 044, FR-17). Net +2: Api/Finance/InvoiceSubmissionEndpoints.cs ADDED (RouteLevelGate:
    // AddFinanceAuthorizationFilter as the caller on the core record, the firm and the submission) and
    // Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs ADDED (GroupGated: AddExternalInvoiceSubmissionAuthorizationFilter,
    // credited below). The kill-switch filter .RequireEbillingEnabled is classified in NotAuthorizationForms.
    // 129 -> 130 (2026-10-16, ebilling-r1 task 048, FR-22). Net +1: Api/Finance/CodeSetMappingEndpoints.cs ADDED -- GET
    // /api/finance/code-set-mappings/proposals and POST /api/finance/code-set-mappings/{table}/{id}/confirm. Classified by GovernedFiles
    // (RouteLevelGate: each route's AddFinanceAuthorizationFilter requires the caller's own Dataverse table privilege, D-76); the
    // kill-switch filter and the errorCode-stamping filter are classified in NotAuthorizationForms.
    // 130 -> 131 (2026-10-16, ebilling-r1 task 107, FR-34). Net +1: Api/Finance/InvoiceRelatedWorkEndpoints.cs ADDED -- GET
    // /api/finance/invoices/{invoiceId}/lines/{lineId}/related-work. Classified by GovernedFiles (RouteLevelGate: AddFinanceAuthorizationFilter,
    // finance.read on the route's invoice); the kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms.
    //
    // Integration (2026-10-17, ebilling-r1 merge of origin/master 1df4ee12d): both sides counted from 125: master +5 (130) and
    // ebilling-r1 +6 (131). Combined 125 + 5 + 6 = 136.
    // 136 -> 137 (2026-10-17, ebilling-r1 task 076, FR-28). Net +1: Api/Finance/RuleSetEndpoints.cs ADDED -- POST/GET /api/finance/rulesets,
    // GET /api/finance/rulesets/{ruleSetId}, PATCH .../proposals/{policyId}, POST .../proposals/{policyId}/accept|reject, POST .../publish,
    // PUT .../assignment and PUT .../visibility. Classified by GovernedFiles (RouteLevelGate: AddFinanceAuthorizationFilter, the caller's own
    // billing-admin table privileges, D-76); the kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms.
    //
    // 131 + 130 -> 132 (2026-10-17, merge of master 1d435e695 into work/spaarke-SPA-external-access-platform-r3): master's 130
    // (129 + UAC 099a) plus external-access-r3's two (047 registration, 048 workforce link). No route added or removed here.
    // 137 + 132 -> 139 (2026-10-18, ebilling-r1 merge of origin/master 1473b34c6): both sides already shared master 1df4ee12d (130).
    // ebilling-r1 added 7 on it (137); master added 2 (external-access-r3 047 ExternalRegistrationEndpoints.cs, 048
    // ExternalWorkforceLinkEndpoints.cs) for 132. Union 130 + 7 + 2 = 139. No route added or removed here.
    // 139 -> 140 (2026-10-20, ebilling-r1 task 100, FR-31). Net +1: Api/Finance/InvoiceLineReviewEndpoints.cs ADDED -- GET
    // /api/finance/invoices/{invoiceId}/review, POST .../lines/{lineId}/decision, POST .../lines/decisions, POST .../lines/accept-clean.
    // Classified by GovernedFiles (RouteLevelGate: AddFinanceAuthorizationFilter, finance.read / ebilling.invoice.decide on the route's invoice);
    // the kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms. Parallel tasks that also add a file (050,
    // 105) each bump this by one: reconcile the sum at integration.
    // 140 -> 141 (2026-10-20, ebilling-r1 task 105, integrated after task 100, FR-19 / FR-37). Net +1: Api/Finance/SubmissionOverrideEndpoints.cs ADDED -- GET
    // /api/finance/submissions/{submissionId}/blocked, POST .../override and POST .../return. Classified by GovernedFiles (RouteLevelGate:
    // AddFinanceAuthorizationFilter as the caller on the submission; the override's privilege is asked inside the service on every call);
    // the kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms.
    // 141 -> 142 (2026-10-22, ebilling-r1 task 081, EB-D-148). Net +1: Api/Finance/ProcedureEndpoints.cs ADDED -- POST/GET/PATCH
    // /api/finance/procedures[/{procedureId}], POST .../versions, PATCH .../versions/{versionId}, POST .../versions/{versionId}/publish|retire,
    // GET .../retire-preview and POST .../retire. Classified by GovernedFiles (RouteLevelGate: step 0 on the group, then
    // AddFinanceAuthorizationFilter per route as the caller); the kill-switch filter is already classified in NotAuthorizationForms.
    // 142 -> 143 (2026-10-22, ebilling-r1 task 090, integrated after task 081, EB-D-148). Net +1: Api/Finance/StoredFileEndpoints.cs ADDED -- GET
    // /api/finance/files/{fileId}/content. Classified by GovernedFiles (RouteLevelGate: AddStoredFileAccessFilter, credited in CreditedForms:
    // step 0, then the caller's own Read on the parent and the source document via the gate variant); .RequireEbillingEnabled is already in
    // NotAuthorizationForms.
    //
    // 132 -> 133 (2026-10-27, spaarke-SPA-external-access-platform-r3, contact identity hardening slice C3). An UPWARD move:
    //
    //   C3  +1  Api/ExternalAccess/PersonAccessAdministrationEndpoints.cs ADDED — POST /api/v1/external-access/remove-person
    //           and /reinstate-person. GovernedFiles entry (RouteLevelGate), AdminMechanism
    //           (AddExternalAccessAdministratorAuthorizationFilter) and AdminOnlyRoutes group added with it. The filter
    //           consults CallerRecordAccessProbe (WhoAmI over the caller's token), so it passes Rule B as a decision service.
    //
    // 143 + 133 -> 144 (2026-10-28, ebilling-r1 merge of origin/master cae791a74): both sides already shared 132. ebilling-r1
    // added 11 on it (143); master added 1 (external-access-r3 C3 PersonAccessAdministrationEndpoints.cs) for 133.
    // Union 132 + 11 + 1 = 144. No route added or removed here.
    //
    // 144 -> 145 (2026-10-29, ebilling-r1 task 102, FR-32). Net +1: Api/Finance/InvoiceAdjustmentEndpoints.cs ADDED -- GET
    // /api/finance/invoices/{invoiceId}/adjustment-rules, POST .../adjustments and DELETE .../adjustments/{lineId}. Classified by GovernedFiles
    // (RouteLevelGate: step 0 on the group, then AddFinanceAuthorizationFilter as the caller on the route's invoice); .RequireEbillingEnabled is
    // already in NotAuthorizationForms. (Written on the branch as 143 -> 144; reconciled at the ebilling-r1 integration merge.)
    //
    // 145 -> 146 (2026-10-29, ebilling-r1 task 103, FR-35). Net +1: Api/Finance/InvoiceApprovalEndpoints.cs ADDED -- GET
    // /api/finance/invoices/{invoiceId}/approval and POST .../approve. Classified by GovernedFiles (RouteLevelGate: AddFinanceAuthorizationFilter,
    // finance.read / ebilling.invoice.decide on the route's invoice; approve's eligibility is asked inside the service on every call); the
    // kill-switch filter .RequireEbillingEnabled is already classified in NotAuthorizationForms. (Written on the branch as 143 -> 144;
    // reconciled at the ebilling-r1 integration merge: 132 shared + 11 ebilling-r1 + 1 master + 102 + 103 = 146.)
    //
    // 146 -> 147 (2026-10-29, ebilling-r1 merge of origin/master): master moved 133 -> 134 (field-mapping-server-write-path-r1 task 072,
    // FR-31), added on the shared 132 line, so the union is 146 + 1 = 147. Master's entry, verbatim:
    //
    // 133 -> 134 (2026-10-29, field-mapping-server-write-path-r1 task 072, FR-31). An UPWARD move:
    //
    //   072  +1  Api/Records/RecordFamilyEndpoints.cs ADDED — GET /api/v1/records/{table}/{id:guid}/family, a record's Filing
    //            family read as the caller by RecordFamilyReader (task 071). Governed RouteLevelGate (its own MapGroup, the
    //            /api/v1/records conventions of RecordNoAccessEndpoint, which keeps its one responsibility) with ONE
    //            HandlerDecision: the gate is the equalised Read probe on the anchor in the handler, not a route filter (the
    //            route supplies the table; the filter's fixed-entity-set form cannot take it and its declared-target form
    //            denies 403). The uniform 404 covers no Read, an unknown id, a non-family table and a probe fault. Pinned
    //            through a minimal TestServer host (this endpoint mapped alone, probe and Dataverse substituted) by
    //            tests/integration/contract/Api/Records/RecordFamilyEndpointsContractTests.cs.
    //
    // 147 -> 148 (2026-10-29, ebilling-r1 task 104, FR-36; written on the branch as 146 -> 147, reconciled at the merge into work). Net +1: Api/Finance/InvoiceRejectReturnEndpoints.cs ADDED -- POST
    // /api/finance/invoices/{invoiceId}/reject and POST .../return. Classified by GovernedFiles (RouteLevelGate: step 0 on the group, then
    // AddFinanceAuthorizationFilter as the caller on the route's invoice, ebilling.invoice.decide: Write; eligibility is asked inside the
    // service on every call); .RequireEbillingEnabled is already in NotAuthorizationForms.
    //
    // 148 -> 149 (2026-10-30 at the merge into work; written on the branch as 146 -> 147), ebilling-r1 task 101 part B1, FR-31). Net +1: Api/Finance/InvoiceCorrectionEndpoints.cs ADDED -- POST
    // /api/finance/invoices/{invoiceId}/lines/{lineId}/corrections, POST .../header/corrections and GET .../corrections. Classified by GovernedFiles
    // (RouteLevelGate: step 0 on the group, then AddFinanceAuthorizationFilter as the caller on the route's invoice, finance.read /
    // ebilling.invoice.decide); .RequireEbillingEnabled is already in NotAuthorizationForms. (Written on the branch as 146 -> 147; reconciled at
    // the ebilling-r1 integration merge.)
    private const int ExpectedEndpointFileCount = 149;
    
    // =============================================================================================
    // THE CREDITED ALLOW-LIST — the only attachment forms Rule A credits as a per-resource decision
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE (task 167). One entry per FORM, added only after reading the filter and finding that it
    // decides — a lookup of the caller's rights on the record the route names, failing closed. The reason cites
    // file:line. ONE LIST, TWO CONSUMERS: Rule A credits exactly these forms, and Rule B inspects exactly these
    // files (plus every *AuthorizationFilter.cs), so a credited-but-never-inspected filter cannot exist.
    // An entry no route attaches fails (EveryAttachmentFormIsClassifiedExactlyOnce).
    //
    // What this list REPLACED: task 074's FilterMarker credited any ".Add*AuthorizationFilter(" and any
    // ".AddEndpointFilter<*Authorization*|*Access*Filter>". Six filters passed that test and decided nothing
    // for the routes they were on; they are now in NonDecidingAttachments.
    // =============================================================================================

    private sealed record CreditedForm(string Form, string FilterFile, string Reason);

    private static readonly IReadOnlyList<CreditedForm> CreditedForms = new[]
    {
        new CreditedForm("AddDocumentAuthorizationFilter", "Api/Filters/DocumentAuthorizationFilter.cs",
            "Resolves the route document and asks AuthorizationService.AuthorizeAsync for the declared operation "
            + "(DocumentAuthorizationFilter.cs:100), denying 403 unless IsAllowed (:116-118)."),
        new CreditedForm("AddContainerDocumentAuthorizationFilter", "Api/Filters/ContainerDocumentAuthorizationFilter.cs",
            "Resolves container -> owning record and requires the caller's Read on it via "
            + "AuthorizationService.GetCallerRecordAccessAsync (ContainerDocumentAuthorizationFilter.cs:299); an unresolvable "
            + "container is refused (task 078)."),
        new CreditedForm("AddBulkDownloadAuthorizationFilter", "Api/Filters/BulkDownloadAuthorizationFilter.cs",
            "Authorizes EVERY requested document through AuthorizationService.AuthorizeAsync "
            + "(BulkDownloadAuthorizationFilter.cs:166) and publishes only the allowed set."),
        new CreditedForm("AddRecordSearchAuthorizationFilter", "Api/Filters/RecordSearchAuthorizationFilter.cs",
            "The filter/endpoint PAIR (task 077): the filter refuses unmappable types (RecordSearchAuthorizationFilter.cs:268) and "
            + "publishes the row obligation (:274); the endpoint authorizes every row and refuses without the obligation."),
        new CreditedForm("AddSemanticSearchAuthorizationFilter", "Api/Filters/SemanticSearchAuthorizationFilter.cs",
            "Checks the scoped record as the caller via GetCallerRecordAccessAsync (SemanticSearchAuthorizationFilter.cs:497) "
            + "and publishes the per-row trim obligation (task 070)."),
        new CreditedForm("AddVisualizationAuthorizationFilter", "Api/Filters/VisualizationAuthorizationFilter.cs",
            "Authorizes the SOURCE document via IAiAuthorizationService.AuthorizeAsync (VisualizationAuthorizationFilter.cs:228) "
            + "and publishes the row obligation the endpoint enforces per neighbour (word-add-in-r1 task 032)."),
        new CreditedForm("AddVisualizationContentAuthorizationFilter", "Api/Filters/VisualizationAuthorizationFilter.cs",
            "Uploaded content has no stored record, so the filter requires a resolvable caller and token and PUBLISHES the row "
            + "obligation (VisualizationAuthorizationFilter.cs:200-211); the endpoint authorizes every row it returns."),
        new CreditedForm("AddFinanceAuthorizationFilter", "Api/Filters/FinanceAuthorizationFilter.cs",
            "Each route declares the id it authorizes; the filter evaluates it as the caller via GetCallerRecordAccessAsync + "
            + "OperationAccessPolicy.HasRequiredRights (FinanceAuthorizationFilter.cs:409-412), uniform 404 on deny (task 130)."),
        new CreditedForm("RequireWriteOnTheRuleSetsProcedure", "Api/Finance/BillingAdminAuthorization.cs",
            "Reads the route rule set's Procedure (BillingAdminAuthorization.cs:172) and, when set, asks the caller's own Write on "
            + "sprk_procedures via CallerReadCheck.CheckAsync (:191); a read fault or an unanswered question is 403 system_failure (:178, :200) "
            + "(ebilling-r1 081, uac D; /draft added in re-check round 2)."),
        new CreditedForm("RequireReadOnTheRuleSetsProcedure", "Api/Finance/BillingAdminAuthorization.cs",
            "The sibling for the rule set READ: the same Procedure resolution (BillingAdminAuthorization.cs:172) and CallerReadCheck.CheckAsync "
            + "(:191) asking Read; denied is 403 insufficient_rights, an unanswered question or a read fault 403 system_failure (:178, :200) "
            + "(ebilling-r1 081, uac r2 I-P5)."),
        new CreditedForm("AddStoredFileAccessFilter", "Api/Finance/StoredFileEndpoints.cs",
            "StoredFileAccessFilter decides before the handler: step 0 via FinanceAuthorizationFilter.EvaluateStepZeroAsync "
            + "(StoredFileEndpoints.cs:241, 403), then RetrievePrincipalAccess as the caller over OBO via "
            + "CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync on the row's parent (:264) and, for a copy, its source "
            + "document (:280), uniform 404 on deny (:224); any unexpected fault refuses with 503 (:207) (ebilling-r1 task 090)."),
        new CreditedForm("AddRecordRouteAccessAuthorizationFilter", "Api/Filters/RecordRouteAccessAuthorizationFilter.cs",
            "RetrievePrincipalAccess on the route record over OBO via CallerRecordAccessProbe "
            + "(RecordRouteAccessAuthorizationFilter.cs:110), 403 on deny (:155); the probe throwing is a denial (task 076)."),
        new CreditedForm("AddEntityAccessFilter", "Api/Filters/EntityAccessFilter.cs",
            "OBO RetrievePrincipalAccess (AppendTo) on SaveRequest.TargetEntity via CallerRecordAccessProbe.GetCallerRightsAsync "
            + "(EntityAccessFilter.cs:276), 403 on deny (:291). Its pass-through on a null target is in the task-167 note."),
        new CreditedForm("AddJobOwnershipFilter", "Api/Filters/JobOwnershipFilter.cs",
            "OWNER-COMPARISON: loads the job and 403s unless its recorded creator is the caller (JobOwnershipFilter.cs:178, :220); "
            + "a blank creator is denied (task 067/120)."),
        new CreditedForm("AddTodoSourceAccessFilter", "Api/Filters/TodoSourceAccessFilter.cs",
            "Read on EVERY caller-supplied source id via CallerRecordAccessProbe.GetCallerRightsAsync "
            + "(TodoSourceAccessFilter.cs:182), one constant deny body (word-add-in-r1 task 064)."),
        new CreditedForm("AddQuickCreateSourceAccessFilter", "Api/Filters/QuickCreateSourceAccessFilter.cs",
            "Read on the caller-named SOURCE record via CallerRecordAccessProbe.GetCallerRightsAsync "
            + "(QuickCreateSourceAccessFilter.cs:116), 403 on deny (:159). Create privilege is NOT checked (sweep S-69)."),
        new CreditedForm("AddOfficeVersionSaveAuthorizationFilter", "Api/Filters/OfficeVersionSaveAuthorizationFilter.cs",
            "For a version save, binds the body's ExistingDocumentId and runs DocumentAuthorizationFilter (write) with "
            + "AuthorizationService (OfficeVersionSaveAuthorizationFilter.cs:87); refuses a route already binding an id (:76)."),
        new CreditedForm("AddPlaybookAccessAuthorizationFilter", "Api/Filters/PlaybookAuthorizationFilter.cs",
            "Loads the route playbook and allows owner OR public (PlaybookAuthorizationFilter.cs:125-136), 404 when absent "
            + "(:121), 403 otherwise (:156). The oid-vs-systemuserid owner defect is in the task-167 note."),
        new CreditedForm("AddPlaybookOwnerAuthorizationFilter", "Api/Filters/PlaybookAuthorizationFilter.cs",
            "Loads the route playbook and allows the OWNER only (PlaybookAuthorizationFilter.cs:125-132), 403 otherwise "
            + "(:156) — fail closed, and today likely denies every owner (the note)."),
        new CreditedForm("AddSessionOwnershipFilter", "Api/Filters/SessionOwnershipFilter.cs",
            "OWNER-COMPARISON added by task 167: loads (claim tid, route sessionId) via ChatSessionManager and answers 404 unless "
            + "OwnerOid equals the caller (SessionOwnershipFilter.cs:173-213); refuses a route with no {sessionId}."),
        new CreditedForm("AddDelegationRuleFilter", "Api/ExternalAccess/DelegationRuleFilter.cs",
            "Added by task 167: resolves the request's target record and requires the caller's Write on it over OBO via "
            + "CallerRecordAccessProbe.GetCallerRightsAsync (DelegationRuleFilter.cs:158); every exit denies 403."),
        new CreditedForm("AddAnalysisExecuteAuthorizationFilter", "Api/Filters/AnalysisAuthorizationFilter.cs",
            "DocumentAccess mode: 400 on zero ids, then IAiAuthorizationService.AuthorizeAsync over every body DocumentId "
            + "(AnalysisAuthorizationFilter.cs:121-174, :139), 403 on deny. (The handler WRITES — sweep S-52.)"),
        new CreditedForm("AddDataverseAuthorizationFilter(EntitySource.FromRouteValue)", "Services/Dataverse/DataverseAuthorizationFilter.cs",
            "Entity-level Read privilege on the ROUTE entity via IDataversePrivilegeChecker.HasReadPrivilegeAsync "
            + "(DataverseAuthorizationFilter.cs:128-148) — the correct decision for SCHEMA routes, which serve no record."),

        // ---- added at the task-167 integration (2026-10-05), from the fix tasks' ledger inputs; each filter read ----
        new CreditedForm("AddAnalysisRecordAuthorizationFilter", "Api/Filters/AnalysisAuthorizationFilter.cs",
            "Task 162 (was NonDeciding): AnalysisAccess mode resolves the analysis-read rule (ResolveAnalysisReadTargetsAsync) "
            + "and hands it to FinanceAuthorizationFilter (AnalysisAuthorizationFilter.cs:382-401, :860-879) — Read on EVERY "
            + "populated anchor, or the verified creator for a personal analysis; every failure is the uniform 404."),
        new CreditedForm("AddAnalysisCreateAuthorizationFilter", "Api/Filters/AnalysisAuthorizationFilter.cs",
            "Task 162 f1 (G5): the Create privilege on sprk_analysis, analysis.attach (Read + AppendTo) on the body document, "
            + "the playbook-use decision and Read on every scope row, as the caller through FinanceAuthorizationFilter "
            + "(AnalysisAuthorizationFilter.cs:711-758); a fault while declaring the checks denies."),
        new CreditedForm("AddAnalysisPromoteAuthorizationFilter", "Api/Filters/AnalysisAuthorizationFilter.cs",
            "Task 162 (G5): the Create privilege on sprk_analysis, analysis.attach on the body document and the regarding record, "
            + "and the playbook-use decision, through FinanceAuthorizationFilter (AnalysisAuthorizationFilter.cs:626-687); the "
            + "session-owner and session-document checks are in the handler."),
        new CreditedForm("AddAnalysisRunAuthorizationFilter", "Api/Filters/AnalysisAuthorizationFilter.cs",
            "Task 162, chained after the DocumentAccess Read check on /execute: Write on every document when the run can write "
            + "them (the document-profile branch or a side-effecting node), plus the playbook-use decision, through "
            + "FinanceAuthorizationFilter (AnalysisAuthorizationFilter.cs:779-843)."),
        new CreditedForm("AddPlaybookByIdAuthorizationFilter", "Api/Filters/PlaybookAuthorizationFilter.cs",
            "Task 164 (UniformById): the playbook-use decision on the route id — public, or the caller's own Dataverse Read on "
            + "the sprk_analysisplaybook row — with ONE uniform 404 for unknown, denied and faulting ids, before the handler's "
            + "cache is read (PlaybookAuthorizationFilter.cs:365-376)."),
        new CreditedForm("AddPlaybookRunAuthorizationFilter", "Api/Filters/PlaybookAuthorizationFilter.cs",
            "Task 164 (Run, on /execute and the agent run-playbook): the parameter policy's 400s, the playbook-use decision "
            + "(uniform 404), then AuthorizationService.AuthorizeAsync Read/Write on every document and every record parameter "
            + "as the caller (uniform 403) (PlaybookAuthorizationFilter.cs:398-470, :744)."),
        new CreditedForm("AddTargetedRecordAuthorizationFilter", "Api/Ai/RagEndpoints.cs",
            "Task 163: FinanceAuthorizationFilter on the record a rag/search or index-file request names (Read on a search "
            + "parent; Write on the document, AppendTo on the parent), uniform 404 (RagEndpoints.cs:775-805). It passes through "
            + "when no record is named (:792-795): the per-row trim and the caller's OBO download decide the rest."),
        new CreditedForm("AddCommunicationRecordAuthorizationFilter", "Api/Filters/CommunicationRecordAuthorizationFilter.cs",
            "Task 161: the request shape, then the caller's token and Dataverse user, then the route's own check — visibility "
            + "as the caller, AppendTo / Write via CallerRecordAccessProbe, table privileges, AuthorizationService on "
            + "attachments (CommunicationRecordAuthorizationFilter.cs:181-232, :735-778); an undeclared route and every fault "
            + "deny (:212, :829-846)."),
        new CreditedForm("AddContactGrantorAuthorizationFilter", "Api/ExternalAccess/ContactGrantorAuthorizationFilter.cs",
            "Task 140: a CONTACT principal holding Collaborate or Full Access on the named record, from the evaluator's "
            + "post-veto rights (principal.RightsOn, ContactGrantorAuthorizationFilter.cs:182, :272); a revoke needs a row the "
            + "caller issued (one 404, :253); an unmapped request type is denied (:146-151)."),
        new CreditedForm("AddExternalInvoiceSubmissionAuthorizationFilter", "Api/ExternalAccess/ExternalInvoiceSubmissionAuthorizationFilter.cs",
            "ebilling-r1 task 044: a submit needs Read and Create on the route's core record from the evaluator's post-veto rights "
            + "(principal.RightsOn, ExternalInvoiceSubmissionAuthorizationFilter.cs:153) and a firm among the caller contact's conferring "
            + "memberships (:180, :256; an unreadable read is 503, never 'no firm'); a read needs the submission's firm among them and "
            + "Read on its core record, else one uniform 404 (:206-225); a missing principal denies (:119)."),
        new CreditedForm("AddInsightsAskAuthorizationFilter", "Api/Insights/InsightEndpoints.cs",
            "Task 163 f1: the shared playbook-parameter policy, registered insights-ask playbooks only, then Read on the subject "
            + "matter (Write when a node that can write reaches it) and each record parameter, as the caller, through "
            + "FinanceAuthorizationFilter (InsightEndpoints.cs:148-244)."),
        new CreditedForm("AddInsightsAssistantAuthorizationFilter", "Api/Insights/InsightsAssistantEndpoint.cs",
            "Task 163 f1: Read on the subject as the caller through FinanceAuthorizationFilter (uniform 404 before any SSE "
            + "frame); the Write question is carried to the facade's run guard (InsightsAssistantEndpoint.cs:183-200)."),
    };

    // =============================================================================================
    // ADMIN MECHANISMS — exactly four forms count as an admin policy
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: a route credited ONLY by one of these must be in AdminOnlyRoutes. Not admin, deliberately:
    // AddReportingAuthorizationFilter (a module role, not an operator gate) and
    // RequireAuthorization(AuthPolicies.ExternalCollaboration) (authentication-scheme selection). The BFF overrides no
    // DefaultPolicy, so a bare RequireAuthorization() only means "signed in"; the FallbackPolicy (owner round 14 item 2)
    // also means only "signed in", for an endpoint that declares nothing — and the BUILD still refuses such a route
    // (NoRouteIsAnonymousByOmission, task 167 r2/f1).
    //
    // ⚠️ The SystemAdmin assertion ALSO passes a token whose scope claim merely CONTAINS "admin"
    // (Infrastructure/DI/AuthorizationModule.cs:371-373). Task 165 fixes that policy (its amendment); this guard
    // credits the policy as specified and records the weakness in the task-167 note (escalation trigger 6).
    // =============================================================================================

    private sealed record AdminMechanism(string Form, string Reason);

    private static readonly IReadOnlyList<AdminMechanism> AdminMechanisms = new[]
    {
        new AdminMechanism("RequireAuthorization(\"SystemAdmin\")",
            "The SystemAdmin policy (AuthorizationModule.cs:362-377): an admin role or scope on the token. Its scope-substring "
            + "branch is task 165's to fix."),
        new AdminMechanism("RequireAuthorization(AuthPolicies.RagApiKey)",
            "The RAG service-automation API key (AuthorizationModule.cs:328-332) — a machine credential, not a user."),
        new AdminMechanism("AddSpeAdminAuthorizationFilter",
            "The Admin or SystemAdmin app role in the 'roles' claim (SpeAdminAuthorizationFilter.cs), including when inherited "
            + "from the /api/spe aggregator."),
        new AdminMechanism("AddRegistrationAuthorizationFilter",
            "The demo-registration approver role (RegistrationAuthorizationFilter.cs) on approve/reject."),
        new AdminMechanism("AddKeylessProofAuthorizationFilter",
            "The Provisioning.KeylessProof APPLICATION role on an app-only token (KeylessProofAuthorizationFilter.cs IsAdmitted: "
            + "no scp, idtyp absent or 'app') — a machine credential H3 assigns only to the L2 Worker identity "
            + "(allowedMemberTypes Application, so no user can hold it). Customer-provisioning task 230b."),
        new AdminMechanism("AddExternalAccessAdministratorAuthorizationFilter",
            "Membership of the standing-grant administrators field security profile, direct or through a team "
            + "(ExternalAccessAdministratorAuthority): the caller's systemuserid from WhoAmI over their own token, the profile "
            + "resolved by its pinned name to exactly one id, one app-credential membership query; any fault, timeout, empty "
            + "WhoAmI, or missing or ambiguous profile is the same 403. No cache. External-access-r3 slice C3."),
    };

    // =============================================================================================
    // NON-DECIDING ATTACHMENTS — filters that pass the request through without a record decision
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: each entry is EVIDENCE, pinned by NonDecidingAttachmentStillPassesThrough against the real
    // source. When a fix task makes the filter decide, the pin fails with the remedy: delete the entry, add the
    // form to CreditedForms if it now decides, re-run Rule A. OwningTask is the fix task whose POML modifies the
    // filter, or "-" for a filter that is an identity or role precondition BY DESIGN.
    // =============================================================================================

    private sealed record NonDecidingAttachment(string Form, string FilterFile, string OwningTask, string Reason, string Evidence);

    private static readonly IReadOnlyList<NonDecidingAttachment> NonDecidingAttachments = new[]
    {
        // At the task-167 integration (2026-10-05): task 164 r1 made AddAiAuthorizationFilter a filter FACTORY that runs a
        // chat-context evaluation (documents, host record, playbook — as the caller) when the HANDLER declares one of five chat
        // request bodies (AiAuthorizationFilter.cs:35-40, :375-521), and the old path otherwise. The guard credits by FORM, and
        // the same form still passes through on every other route, so the form stays here and earns no credit: the five
        // chat-context routes (S-18, S-23..S-25, S-53) are resolved in the ledger by declaration where the route is otherwise
        // credited (SessionOwnershipFilter) and stay Pending 164 where it is not (S-18, S-24).
        new NonDecidingAttachment("AddAiAuthorizationFilter", "Api/Filters/AiAuthorizationFilter.cs", "164",
            "Outside the five chat-context request bodies (task 164 r1) it decides only for DocumentAnalysisRequest / Guid "
            + "arguments and returns next otherwise; no handler binds DocumentAnalysisRequest, and the one Guid binder passes an "
            + "ANALYSIS id it authorizes as a document.",
            "if (documentIds.Count == 0) return await next(context) (AiAuthorizationFilter.cs:84) and zero handlers bind DocumentAnalysisRequest"),
        new NonDecidingAttachment("AddEndpointFilter<AiAuthorizationFilter>", "Api/Filters/AiAuthorizationFilter.cs", "164",
            "The same filter attached by type (the knowledge and admin-knowledge groups): the same pass-through for every "
            + "argument it does not extract.",
            "if (documentIds.Count == 0) return await next(context) (AiAuthorizationFilter.cs:84) and zero handlers bind DocumentAnalysisRequest"),
        // Task 171: it DECIDES (DocumentAuthorizationFilter on the row) when the route's {documentSpeId} has a sprk_document, and
        // passes through for a row-less item (Compose Path B, reported to the owner as escalation trigger 2) — so by FORM it
        // earns no credit, and each route keeps its own declaration (HandlerDecision / Pending) for the row-less case.
        new NonDecidingAttachment("AddComposeDocumentAuthorizationFilter", "Api/Filters/ComposeDocumentAuthorizationFilter.cs", "171",
            "Authorizes the item's sprk_document row (read / write) when one exists; for a row-less item it returns next "
            + "unmarked, and the Compose byte calls keep the caller's OBO identity (SPE decides).",
            "if (row is null) { return await next(context); } (ComposeDocumentAuthorizationFilter.cs)"),
        new NonDecidingAttachment("AddEndpointFilter<CommunicationAuthorizationFilter>", "Api/Filters/CommunicationAuthorizationFilter.cs", "161",
            "Checks IsAuthenticated and an oid, then next — nothing else. The sweep disproved its old claim that "
            + "ICommunicationAccessFilter scopes send, archive, suggest, create-task, threads, affinity, proposals and verify.",
            "the whole file (43 lines) references no decision service"),
        new NonDecidingAttachment("AddTenantAuthorizationFilter", "Api/Filters/TenantAuthorizationFilter.cs", "163",
            "Compares the tid claim with EVERY tenant id the request names (task 163: each batch item, not just the first) and "
            + "passes through when none is named; never consults a record (sweep S-04..S-06, S-30). On /rag/index and DELETE "
            + "/rag/{documentId} the decision is the SystemAdmin policy beside it (AdminOnlyRoutes).",
            "if (requestedTenantIds.Count == 0) … return await next(context) (TenantAuthorizationFilter.cs:79-84)"),
        new NonDecidingAttachment("AddEndpointFilter<WorkspaceAuthorizationFilter>", "Api/Filters/WorkspaceAuthorizationFilter.cs", "-",
            "Resolves the caller's oid into HttpContext.Items and denies 401 without one — an identity precondition by design, "
            + "not a record decision.",
            "the file references no decision service"),
        new NonDecidingAttachment("AddAgentAuthorizationFilter", "Api/Agent/AgentAuthorizationFilter.cs", "164",
            "Asserts an oid and a tid on the agent token, nothing else (with a TODO for audience/app-role validation).",
            "the file references no decision service"),
        new NonDecidingAttachment("AddCallerPrincipalAuthorizationFilter", "Api/Filters/CallerPrincipalAuthorizationFilter.cs", "-",
            "Resolves the caller principal (contact-plane identity) as the PRECONDITION of the per-record checks the handlers "
            + "make — by design, never a decision itself.",
            "the file references no decision service"),
        // Task 186 (review round 1 F1): the workforce privilege ceiling on the /api/v1/external group. It only NARROWS — a
        // workforce contact whose Workforce role lacks the route's declared table privileges is refused before the handler
        // — and passes every other caller straight through. It never decides on a record, so it earns no Rule A credit; each
        // route keeps the credit it had. Its coverage of the group is WorkforceCeilingCoverageTests.
        new NonDecidingAttachment("AddWorkforceCeilingFilter", "Api/ExternalAccess/WorkforceCeilingEndpointFilter.cs", "-",
            "A role-privilege ceiling by design (table x operation, never a record): a caller it does not apply to (a systemuser, "
            + "a CIAM partner) goes straight to next, and a workforce contact the role allows goes to next too.",
            "|| !WorkforceRoleCeilingGate.Applies(principal)) { ... return await next(context) (WorkforceCeilingEndpointFilter.cs, InvokeAsync)"),
        new NonDecidingAttachment("AddReportingAuthorizationFilter", "Api/Reporting/ReportingAuthorizationFilter.cs", "-",
            "A module gate plus the sprk_ReportingAccess role claim — a module role by design, not per-resource and not an "
            + "operator surface (sweep S-70, S-71, S-81).",
            "decides from role claims (IsInRole) and references no decision service"),
        new NonDecidingAttachment("AddSpeAdminTenantScopeFilter", "Api/Filters/SpeAdminTenantScopeFilter.cs", "165",
            "Since task 165 it decides per config (uniform 404; 503 when unverifiable — fail CLOSED), per container (its "
            + "business-unit binding; an unbound container reaches nobody) and per container type, and confines the "
            + "tenant-/type-wide routes to a root admin of a Spaarke-operated environment. It still passes through a request "
            + "that names NO configId, which the handler then refuses with its own 400; so it is not a per-resource credit "
            + "on its own, and the /api/spe routes are credited as an operator surface (AdminOnlyRoutes).",
            "if (present.Count == 0) { return await next(context); } (SpeAdminTenantScopeFilter.cs, InvokeAsync)"),
    };

    // =============================================================================================
    // NOT AUTHORIZATION — authorization-SHAPED calls that are not authorization
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: each was read and decides nothing about who may act on what. Crediting any of them would mark
    // an ungated route as gated (task 120's control AuthFilterRecognition_NegativeControl_BaselineAuthnIsNotAGate).
    // An entry no route uses is deleted (AddTenantEnvironmentRoutingFilter was named at authoring but no route
    // attaches it, so it is not listed).
    // =============================================================================================

    private sealed record NotAuthorizationForm(string Form, string Reason);

    private static readonly IReadOnlyList<NotAuthorizationForm> NotAuthorizationForms = new[]
    {
        new NotAuthorizationForm("RequireAuthorization()", "Authentication only — 'are you anyone?'. The default policy and the FallbackPolicy both mean an authenticated user."),
        new NotAuthorizationForm("RequireAuthorization(AuthPolicies.ExternalCollaboration)", "Selects the CIAM + workforce authentication schemes; no role, no record."),
        new NotAuthorizationForm("RequireRateLimiting", "Throughput protection (a mandatory control for anonymous routes, never an authorization decision)."),
        new NotAuthorizationForm("RequireWebhookSignature", "HMAC authenticity of a webhook body (WebhookSignatureFilter.cs) — the control an AnonymousByDesign waiver names."),
        new NotAuthorizationForm("AddOfficeAuthFilter", "IsAuthenticated + stash the oid (OfficeAuthFilter.cs:76-135); authentication only."),
        new NotAuthorizationForm("AddOfficeRateLimitFilter", "Office throughput limiter, explicitly fail-OPEN."),
        new NotAuthorizationForm("RequireEbillingEnabled",
            "ebilling-r1 task 078: the eBilling kill switch (EbillingEnabledFilter.cs) answers 503 ebilling.disabled when the feature flag is off. "
            + "It is chained AFTER the route's AddFinanceAuthorizationFilter and decides nothing about who may act on what."),
        new NotAuthorizationForm("AddEndpointFilter<CodeSetForbiddenCodeFilter>",
            "ebilling-r1 task 048: stamps the stable errorCode ebilling.codeset.forbidden (ADR-019) on a 403 that the route's "
            + "AddFinanceAuthorizationFilter produced (CodeSetMappingEndpoints.cs). It is chained BEFORE that filter, only reads its result, "
            + "and can never turn a denial into anything else; it decides nothing."),
        new NotAuthorizationForm("AddIdempotencyFilter", "Replays a cached response for a repeated client key."),
        new NotAuthorizationForm("AddEndpointFilter<DocumentUrlIdentityFilter>", "Resolves a document URL to an id; the route's decision is its AddDocumentAuthorizationFilter."),
        new NotAuthorizationForm("AddEndpointFilter<DocumentEmailIdentityFilter>", "Word add-in task 120: resolves an email's message id to its saved .eml document id; the route's decision is its AddDocumentAuthorizationFilter."),
        new NotAuthorizationForm("AddEndpointFilter(lambda)", "The one inline filter (OfficeEndpoints.cs:1521) validates Guid.Empty. An inline lambda is NEVER credited."),
        new NotAuthorizationForm("WithMetadata(new RequestSizeLimitAttribute)", "A request body size limit on the Compose save/mount routes."),
        new NotAuthorizationForm("AddEndpointFilter(ValidateCreateEventRequestAsync)",
            "Task 159: POST /api/v1/events request SHAPE only (EventEndpoints.cs:201-214, no I/O), run BEFORE the route's "
            + "RecordRouteAccessAuthorizationFilter so a 400 never becomes a 403; it decides nothing about any record."),
        new NotAuthorizationForm("RequireSpeAdminPlatformOperator",
            "Task 165 (owner round 49 item 1): endpoint METADATA, not a filter. It marks the tenant-wide and type-wide SPE "
            + "admin routes so that SpeAdminTenantScopeFilter, which decides, confines them to a root admin of a Spaarke-"
            + "operated environment (the platform-operator check that runs first in its InvokeAsync). It narrows an "
            + "AdminOnlyRoutes surface further and decides nothing itself."),
        new NotAuthorizationForm("RequireWorkforceCeiling",
            "Task 186 (review round 1 F1): endpoint METADATA, not a filter — the route's table x operation set for the "
            + "workforce privilege ceiling, read by the group's AddWorkforceCeilingFilter (NonDecidingAttachments). It narrows "
            + "what a workforce contact may do and decides nothing about any record."),
        new NotAuthorizationForm("RequireInternalFinanceCaller",
            "ebilling-r1 task 099 (procedure-and-file-model section 5.3): endpoint METADATA on every /api/finance route group, not a "
            + "filter. It switches on step 0 inside the route's own AddFinanceAuthorizationFilter (CreditedForms), which refuses a "
            + "flagged-external, row-less or unreadable caller before its record checks. It only narrows that filter and decides "
            + "nothing itself; FinanceStepZeroCoverageGuardTests pins it on every /api/finance route."),
        new NotAuthorizationForm("AddEndpointFilter(ValidateFilingRequestAsync)",
            "Task 147 r1c: PATCH /api/v1/events/{id}/filing request SHAPE only (EventEndpoints.cs:172-177, "
            + "ChildRecordEndpoints.FilingShapeProblem, no I/O), before the route's RecordRouteAccessAuthorizationFilter(\"write\")."),
        new NotAuthorizationForm("AddEndpointFilter(ValidateDueAssigneeRequestAsync)",
            "Ontology platform R1 task 044: PATCH /api/v1/events/{id}/due-assignee request SHAPE only (EventDueAssigneeWrite.ShapeProblem, "
            + "no I/O): at least a due date or an assignee. It runs before the route's RecordRouteAccessAuthorizationFilter(\"write\") "
            + "and decides nothing about any record."),
    };

    /// <summary>
    /// Named policies backed by <c>ResourceAccessRequirement</c> (AuthorizationModule.cs): a genuine resource decision,
    /// credited as ResourcePolicy and pinned in <see cref="PolicyOnlyRoutes"/>.
    /// </summary>
    private static readonly IReadOnlySet<string> ResourcePolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        "canpreviewfiles", "candownloadfiles", "canuploadfiles", "canreplacefiles",
        "canreadmetadata", "canupdatemetadata", "canlistchildren",
        "candeletefiles", "canmovefiles", "cancopyfiles", "cancreatefolders",
        "cansharefiles", "canmanagefilepermissions",
        "canviewversions", "canrestoreversions",
        // "canwritefiles" REMOVED 2026-09-07 (task 083) — the policy no longer exists.
    };

    /// <summary>
    /// Routes whose only credit is a ResourceAccessRequirement policy. EMPTY since task 083 (2026-09-07) and kept
    /// pinned: the next such route fails as `added`, forcing the resource-key-vs-resource-domain question that no
    /// structural rule can ask (finding #4).
    /// </summary>
    private static readonly IReadOnlySet<string> PolicyOnlyRoutes = new HashSet<string>(StringComparer.Ordinal);

    // =============================================================================================
    // THE EXPLICITLY ANONYMOUS SURFACE — pinned (task 167 f1, owner round 14 item 2)
    // ---------------------------------------------------------------------------------------------
    // Since owner round 14 the BFF sets an authorization FallbackPolicy (an authenticated user), so a route is
    // reachable WITHOUT SIGNING IN only when it says so with .AllowAnonymous() on its route or group chain. These
    // are all of them. TheExplicitlyAnonymousSurfaceIsPinned fails on an added AND on a removed route, so the public
    // surface can change only in a diff a reviewer sees. Each route ALSO needs an AnonymousByDesign waiver naming its
    // mandatory control, or a Pending waiver (Rule A) — this list does not replace that; it makes the set explicit.
    //
    // MAINTENANCE: adding a public route = add it here AND give it its waiver. Removing or gating one = delete its line
    // here in the same diff (task 166 deleted the document probe GET /healthz/dataverse/doc/{id}, so its line went at
    // the task-167 integration; the ACS ingress is Pending 161).
    //
    // EACH ROUTE'S COMPENSATING CONTROL IS PINNED HERE, AND ENFORCED (main-session round 43 item 1, task 167 f2-v1).
    // Owner round 12 item 1 keeps the strict AnonymousByDesign rule — an anonymous route's compensating control is
    // MANDATORY — and until f2-v1 only the three liveness probes had theirs read from code; for the other thirteen it was
    // waiver TEXT, so removing `.RequireRateLimiting("anonymous")` from /healthz/dataverse kept the build green. Each line
    // below lists EVERY control the route carries, of three kinds:
    //   - RateLimitControl("policy")  — exactly ONE .RequireRateLimiting("policy") on the effective chain, a plain literal;
    //   - WebhookSignatureControl()   — .RequireWebhookSignature(...) on the chain (WebhookSignatureFilter, fail closed);
    //   - DevelopmentOnlyControl()    — the registration sits in the then-branch of `if (<env>.IsDevelopment())`, <env> an
    //                                   IWebHostEnvironment / IHostEnvironment parameter or an `.Environment` property.
    // EveryExplicitlyAnonymousRouteCarriesItsPinnedControl fails when a control is removed, swapped (a looser policy is a
    // different policy), added unpinned, or applied in a form the guard cannot read (an attribute, metadata, a wrapper —
    // RateLimitPoliciesAreAppliedOnlyOnAScannedChain refuses those outright), and when the route's waiver text does not
    // name exactly these controls. A route with no control at all fails.
    // =============================================================================================

    private enum AnonymousControlKind
    {
        RateLimit,
        WebhookSignature,
        DevelopmentOnly,
    }

    /// <summary>One compensating control of an explicitly anonymous route. <see cref="Evidence"/> is the text its waiver
    /// must name.</summary>
    private sealed record AnonymousControl(AnonymousControlKind Kind, string? Policy)
    {
        public string Evidence => Kind switch
        {
            AnonymousControlKind.RateLimit => $"RequireRateLimiting(\"{Policy}\")",
            AnonymousControlKind.WebhookSignature => "RequireWebhookSignature",
            _ => "IsDevelopment()",
        };
    }

    private static AnonymousControl RateLimitControl(string policy) => new(AnonymousControlKind.RateLimit, policy);

    private static AnonymousControl WebhookSignatureControl() => new(AnonymousControlKind.WebhookSignature, null);

    private static AnonymousControl DevelopmentOnlyControl() => new(AnonymousControlKind.DevelopmentOnly, null);

    private sealed record AnonymousRoute(string Route, string Purpose, IReadOnlyList<AnonymousControl> Controls);

    private static AnonymousRoute PublicRoute(string route, string purpose, params AnonymousControl[] controls) => new(route, purpose, controls);

    private static readonly IReadOnlyList<AnonymousRoute> ExplicitlyAnonymousRoutes = new[]
    {
        PublicRoute("GET /healthz", "liveness probe", RateLimitControl("health-probe")),
        PublicRoute("GET /healthz/catalog", "catalog probe", RateLimitControl("health-probe")),
        PublicRoute("GET /ping", "warm-up probe", RateLimitControl("health-probe")),
        PublicRoute("GET /healthz/dataverse", "Dataverse probe", RateLimitControl("anonymous")),
        PublicRoute("GET /healthz/dataverse/crud", "Dataverse CRUD probe", RateLimitControl("anonymous")),
        PublicRoute("GET /status", "service metadata", RateLimitControl("anonymous")),
        PublicRoute("GET /api/config/client", "MSAL bootstrap config", RateLimitControl("anonymous")),
        PublicRoute("GET /api/config", "public runtime config", RateLimitControl("anonymous")),
        PublicRoute("GET /api/office/health", "Office add-in connectivity", RateLimitControl("anonymous")),
        PublicRoute("POST /api/office/save-debug", "development-only diagnostic", DevelopmentOnlyControl()),
        PublicRoute("POST /api/registration/demo-request", "public demo form", RateLimitControl("anonymous")),
        PublicRoute("POST /api/onboarding/consent-callback", "admin-consent redirect target (HMAC-SHA256 over the body in the handler)",
            RateLimitControl("anonymous")),
        PublicRoute("POST /api/compose/webhooks/spe-doc-changed", "Graph change-notification webhook",
            WebhookSignatureControl(), RateLimitControl("webhook-graph")),
        PublicRoute("POST /api/communications/incoming-webhook", "Graph mail-notification webhook",
            WebhookSignatureControl(), RateLimitControl("webhook-graph")),
        PublicRoute("POST /api/communications/acs/eventgrid", "Pending 161 (the shared secret is optional)",
            RateLimitControl("webhook-graph")),
    };

    // =============================================================================================
    // THE LIVENESS-PROBE RATE-LIMIT POLICY — pinned (owner round 14 item 1)
    // ---------------------------------------------------------------------------------------------
    // "health-probe" (RateLimitingModule.cs: 120/min per client IP, sliding window, no queue) exists so the App Service
    // health check, the slot-swap warm-up ping and the 5-second deploy pollers never see a 429. It is LOOSER than the
    // shared "anonymous" policy (10/min), so it must not spread: TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes
    // fails when any other route carries it (added) or when a probe loses it (removed), and when a route carrying it is
    // not anonymous. Round 12 item 1 still holds — the probes ARE rate limited; this is which limit.
    // MAINTENANCE: a new liveness probe a platform polls joins this set in the same diff that maps it, with its
    // AnonymousByDesign waiver naming RequireRateLimiting("health-probe"). Any other anonymous route uses "anonymous".
    // This set equals the ExplicitlyAnonymousRoutes lines whose control is RateLimitControl("health-probe")
    // (EveryExplicitlyAnonymousRouteCarriesItsPinnedControl asserts it), and the policy can no longer reach a route by an
    // [EnableRateLimiting] attribute or metadata (RateLimitPoliciesAreAppliedOnlyOnAScannedChain, task 167 f2-v1).
    // =============================================================================================

    private static readonly IReadOnlySet<string> HealthProbeRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        "GET /healthz", "GET /healthz/catalog", "GET /ping",
    };

    // =============================================================================================
    // ADMIN-ONLY ROUTES — pinned, grouped by file, one reason per operator surface
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE: TheSetOfAdminOnlyRoutesIsPinned fails on "added" (a new admin-only route: confirm its audience
    // is operators) and on "removed" (a route left, or gained a per-resource decision). The SPE sweep routes
    // S-44, S-45, S-72..S-75 are here AND carry InsufficientDecision waivers: admin credit is real, but the
    // tenant scope does not reach them. Credited, not flagged, yet worth attention (the task-167 note): the SPE
    // environment and dashboard routes (no BU scoping), the bulk status route (no starter check), and register
    // (an app token sent to a caller-chosen host) — task 165's amendment owns them.
    //
    // Each group DECLARES the one admin mechanism its routes carry; EveryAdminOnlyRouteCarriesItsGroupsMechanism fails
    // on a route added to a group without it (task 167 f2). A SWEEP entry resolves through this set only when its group's
    // mechanism is an admin POLICY — SystemAdminPolicy or SpeAdminPolicy (owner round 9 item 3; main-session round 34
    // item 5, which lifted the old "/api/spe/ only" limit). A fix task resolving a sweep route by admin gating adds the
    // route to its file's SystemAdmin/SPE group, deletes its waiver, and sets ResolvedBy + ProofTest.
    // =============================================================================================

    /// <summary>One operator surface: its file, the ONE admin mechanism every route in it carries (one of
    /// <see cref="AdminMechanisms"/>; pinned by <c>EveryAdminOnlyRouteCarriesItsGroupsMechanism</c>), why it is an
    /// operator surface, and its routes. A file whose routes use two mechanisms has one group per mechanism.</summary>
    private sealed record AdminOnlyGroup(string File, string Mechanism, string Reason, IReadOnlyList<string> Routes);

    private const string SystemAdminPolicy = "RequireAuthorization(\"SystemAdmin\")";
    private const string SpeAdminPolicy = "AddSpeAdminAuthorizationFilter";
    private const string RagApiKeyCredential = "RequireAuthorization(AuthPolicies.RagApiKey)";
    private const string RegistrationApproverRole = "AddRegistrationAuthorizationFilter";
    private const string KeylessProofRole = "AddKeylessProofAuthorizationFilter";
    private const string ExternalAccessAdministrator = "AddExternalAccessAdministratorAuthorizationFilter";

    private static readonly IReadOnlyList<AdminOnlyGroup> AdminOnlyRoutes = new[]
    {
        new AdminOnlyGroup("Api/Admin/JobsEndpoints.cs", SystemAdminPolicy,
            "Background-job inspection, history, trigger and enable/disable: the scheduler is an operator control "
            + "plane, behind SystemAdmin by owner decision Q6 (R3 task 020).",
            new[]
            {
                "GET /api/admin/jobs",
                "GET /api/admin/jobs/{jobId}/status",
                "POST /api/admin/jobs/{jobId}/trigger",
                "GET /api/admin/jobs/{jobId}/history",
                "POST /api/admin/jobs/{jobId}/enable",
                "POST /api/admin/jobs/{jobId}/disable",
            }),
        new AdminOnlyGroup("Api/Admin/MembershipAdminEndpoints.cs", SystemAdminPolicy,
            "Membership-field discovery audit and metadata-cache refresh: tenant-wide configuration diagnostics "
            + "for operators, SystemAdmin by owner decision Q6 (R3 task 036).",
            new[]
            {
                "GET /api/admin/membership/discovered/{entityType}",
                "POST /api/admin/membership/refresh-metadata",
            }),
        new AdminOnlyGroup("Api/Platform/KeylessProofEndpoints.cs", KeylessProofRole,
            "PROVISIONING AUTOMATION behind the Provisioning.KeylessProof application role (no user acts): L2's acceptance gate "
            + "(H13) asks the stamp's BFF to prove one managed-identity call per Azure service. Status and timing only — no "
            + "record, data or secret is returned (task 230b). Task 260 (ISS-014): H13 also asks it to run the read-only "
            + "secure-record isolation census (the scheduled job's code and result) and refuses Ready unless it is isolated.",
            new[]
            {
                "POST /api/platform/keyless-proof",
                "POST /api/platform/secure-record-isolation-census",
            }),
        new AdminOnlyGroup("Api/ExternalAccess/PersonAccessAdministrationEndpoints.cs", ExternalAccessAdministrator,
            "PERSON-LEVEL external-access administration: remove a person (marker, revoke every grant, evict caches, clear the "
            + "invite state) and reinstate one (clear the marker only). The person is not a record the route names, so the "
            + "authority is the standing-grant administrators profile, not a per-record right (slice C3).",
            new[]
            {
                "POST /api/v1/external-access/remove-person",
                "POST /api/v1/external-access/reinstate-person",
            }),
        new AdminOnlyGroup("Api/Ai/RagEndpoints.cs", RagApiKeyCredential,
            "enqueue-indexing is SERVICE AUTOMATION behind the RagApiKey machine credential (no user acts). The key "
            + "holder controls the tenant partition (task-167 note).",
            new[]
            {
                "POST /api/ai/rag/enqueue-indexing",
            }),
        new AdminOnlyGroup("Api/Ai/RagEndpoints.cs", SystemAdminPolicy,
            "bulk-index and its status are SystemAdmin index maintenance: re-indexing the tenant's whole corpus is an "
            + "operator action, never a user's. Task 163 (sweep S-06, S-30; notes §15.7 item 4) put POST /api/ai/rag/index and "
            + "DELETE /api/ai/rag/{documentId} behind the same policy: raw chunk writes and deletes over the caller's OWN "
            + "token-tenant partition (the tenant filter beside them refuses another tenant) are index maintenance too.",
            new[]
            {
                "POST /api/ai/rag/admin/bulk-index",
                "GET /api/ai/rag/admin/bulk-index/{jobId}/status",
                "POST /api/ai/rag/index",
                "DELETE /api/ai/rag/{documentId}",
            }),
        new AdminOnlyGroup("Api/Admin/RecordMatchingAdminEndpoints.cs", SystemAdminPolicy,
            "Record-matching index maintenance is an OPERATOR surface: a full or incremental app-only re-read of every "
            + "matter, project and invoice into the shared records index, and that index's counts. Task 165 (sweep S-47, "
            + "S-48, S-77) put the group behind the SystemAdmin policy, whose scope-substring branch it also removed.",
            new[]
            {
                "POST /api/admin/record-matching/sync",
                "POST /api/admin/record-matching/sync-incremental",
                "GET /api/admin/record-matching/status",
            }),
        new AdminOnlyGroup("Api/Insights/PrecedentAdminEndpoints.cs", SpeAdminPolicy,
            "SME authoring and confirmation of Insights precedents (D-P3 phase 1): curation of tenant-wide "
            + "reference content behind the Admin/SystemAdmin app role.",
            new[]
            {
                "POST /api/insights/admin/precedents",
                "POST /api/insights/admin/precedents/{id:guid}/confirm",
            }),
        new AdminOnlyGroup("Api/RegistrationEndpoints.cs", RegistrationApproverRole,
            "Approving or rejecting a demo-access request provisions or refuses an environment: an operator "
            + "decision behind the registration approver role.",
            new[]
            {
                "POST /api/registration/requests/{id:guid}/approve",
                "POST /api/registration/requests/{id:guid}/reject",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/AuditLogEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/audit",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/BulkOperationEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators. configId arrives in the BODY, so the tenant scope never fires (S-44, S-72, task "
            + "165).",
            new[]
            {
                "POST /api/spe/bulk/delete",
                "POST /api/spe/bulk/permissions",
                "GET /api/spe/bulk/{operationId}/status",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/BusinessUnitEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/businessunits",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ConfigEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators. Task 165 renamed the route parameter to 'configId', so the tenant scope reads it and "
            + "answers the uniform 404 for a config outside the caller's units (S-45, S-73..S-75).",
            new[]
            {
                "GET /api/spe/configs",
                "GET /api/spe/configs/{configId:guid}",
                "POST /api/spe/configs",
                "PUT /api/spe/configs/{configId:guid}",
                "DELETE /api/spe/configs/{configId:guid}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ConsumingTenantEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containertypes/{typeId}/consumers",
                "POST /api/spe/containertypes/{typeId}/consumers",
                "PUT /api/spe/containertypes/{typeId}/consumers/{appId}",
                "DELETE /api/spe/containertypes/{typeId}/consumers/{appId}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerColumnEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containers/{containerId}/columns",
                "POST /api/spe/containers/{containerId}/columns",
                "PATCH /api/spe/containers/{containerId}/columns/{columnId}",
                "DELETE /api/spe/containers/{containerId}/columns/{columnId}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerCustomPropertyEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containers/{containerId}/customproperties",
                "PUT /api/spe/containers/{containerId}/customproperties",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containers",
                "GET /api/spe/containers/{containerId}",
                "POST /api/spe/containers",
                "PATCH /api/spe/containers/{containerId}",
                "POST /api/spe/containers/{containerId}/activate",
                "POST /api/spe/containers/{containerId}/lock",
                "POST /api/spe/containers/{containerId}/unlock",
                "POST /api/spe/containers/{containerId}/archive",
                "POST /api/spe/containers/{containerId}/unarchive",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerItemEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containers/{containerId}/items",
                "GET /api/spe/containers/{containerId}/items/{itemId}/versions",
                "GET /api/spe/containers/{containerId}/items/{itemId}/thumbnails",
                "POST /api/spe/containers/{containerId}/items/{itemId}/share",
                "GET /api/spe/containers/{containerId}/items/{itemId}/content",
                "GET /api/spe/containers/{containerId}/items/{itemId}/preview",
                "DELETE /api/spe/containers/{containerId}/items/{itemId}",
                "POST /api/spe/containers/{containerId}/folders",
                "POST /api/spe/containers/{containerId}/items/upload",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerPermissionEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containers/{containerId}/permissions",
                "POST /api/spe/containers/{containerId}/permissions",
                "PATCH /api/spe/containers/{containerId}/permissions/{permissionId}",
                "DELETE /api/spe/containers/{containerId}/permissions/{permissionId}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerTypeEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators. /register sends an app token to a caller-chosen host (task-167 note; task 165 "
            + "amendment).",
            new[]
            {
                "GET /api/spe/containertypes",
                "GET /api/spe/containertypes/{typeId}",
                "POST /api/spe/containertypes",
                "POST /api/spe/containertypes/{typeId}/register",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerTypePermissionEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/containertypes/{typeId}/permissions",
                "GET /api/spe/containertypes/{typeId}/owners",
                "POST /api/spe/containertypes/{typeId}/owners",
                "DELETE /api/spe/containertypes/{typeId}/owners/{permissionId}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/ContainerTypeSettingsEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "PUT /api/spe/containertypes/{typeId}/settings",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/DashboardEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators. Aggregates across ALL configs with no BU filter (task-167 note; task 165 "
            + "amendment).",
            new[]
            {
                "GET /api/spe/dashboard/metrics",
                "POST /api/spe/dashboard/refresh",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/EnvironmentEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators. No business-unit scoping at all (task-167 note; task 165 amendment).",
            new[]
            {
                "GET /api/spe/environments",
                "GET /api/spe/environments/{id:guid}",
                "POST /api/spe/environments",
                "PUT /api/spe/environments/{id:guid}",
                "DELETE /api/spe/environments/{id:guid}",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/RecycleBinEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/recyclebin",
                "POST /api/spe/recyclebin/{containerId}/restore",
                "DELETE /api/spe/recyclebin/{containerId}",
                "GET /api/spe/containers/{containerId}/recyclebin/items",
                "POST /api/spe/containers/{containerId}/recyclebin/items/restore",
                "POST /api/spe/containers/{containerId}/recyclebin/items/delete",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/SearchContainersEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "POST /api/spe/search/containers",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/SearchItemsEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "POST /api/spe/search/items",
            }),
        new AdminOnlyGroup("Api/SpeAdmin/SecurityEndpoints.cs", SpeAdminPolicy,
            "SPE administration via the /api/spe aggregator (Admin/SystemAdmin app role + tenant scope): "
            + "containers and container types are platform resources with no Dataverse row, administered by tenant "
            + "SPE administrators.",
            new[]
            {
                "GET /api/spe/security/alerts",
                "GET /api/spe/security/score",
            }),
    };

    // =============================================================================================
    // HANDLER DECISIONS — routes whose decision lives in the handler, verified in source
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE. A declaration names the route key, the handler (a method in the endpoint file, "Type.Method",
    // or "inline" for a lambda), the decision seam, up to two "ConcreteType.Method" hops the handler calls, and a
    // reason citing file:line. EveryHandlerDecisionIsVerified locates the handler, follows each hop (each must be
    // called by the body before it; an interface is refused; every overload must reach the next step), and
    // requires the seam in the last BODY — the code between the braces (or after the lambda arrow), never the
    // signature. The seam counts when it occurs there as a whole identifier outside comments, or when the body
    // USES a parameter of that method, or a field/property declared at the top level of its type, whose declared
    // type is the seam (a qualified name such as Spaarke.Core.Auth.AuthorizationService counts). An unused DI
    // parameter of the seam type earns nothing (task 167 r1). A body includes the same-type helpers it calls
    // directly, one level deep (owner round 12 item 3). This is a PRESENCE check, the same limit Rule B has: it
    // proves the seam is reached, not that it is applied to the right id. The behavioural deny tests of tasks
    // 159-166 are the proof.
    //
    // A HandlerDecision is allowed only when the decision covers EVERY caller-chosen identifier the handler acts
    // on; partial coverage is a finding (a Pending waiver), not a credit. A fix task's "the query is the gate"
    // shape (a list trimmed by an impersonated or delegated query) is recorded here, never as a Permanent waiver.
    // =============================================================================================

    private sealed record HandlerDecision(string Route, string Handler, string Seam, IReadOnlyList<string> Hops, string Reason);

    private static readonly IReadOnlyList<HandlerDecision> HandlerDecisions = new[]
    {
        new HandlerDecision("POST /api/ai/rag/send-to-index", "SendToIndex", "AuthorizationService", Array.Empty<string>(),
            "Every body DocumentId is authorized as the caller "
            + "(AuthorizationService.GetCallerRecordAccessAsync, RagEndpoints.cs:765) before any read, and the "
            + "body TenantId must equal the token's (:712)."),
        new HandlerDecision("GET /api/communications/{id:guid}/attachments/text", "GetCommunicationAttachmentTextAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationAttachmentTextService.GetAttachmentTextAsync" },
            "Attachments are read AS the caller (MSCRMCallerID) for the caller-chosen communication id "
            + "(CommunicationAttachmentTextService.cs:150); the SPE download follows only for rows that query "
            + "returned."),
        new HandlerDecision("GET /api/communications/threads/{threadId:guid}/messages", "GetThreadMessagesAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationThreadReadService.ReadThreadAsync" },
            "The thread's messages are queried AS the caller (CommunicationThreadReadService.cs:223, _query "
            + ":165), then trimmed by ICommunicationAccessFilter."),
        new HandlerDecision("GET /api/communications/threads/{threadId:guid}/unread-count", "GetThreadUnreadCountAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationThreadReadService.GetUnreadCountAsync" },
            "The unread count is computed over a query run AS the caller "
            + "(CommunicationThreadReadService.cs:278)."),
        new HandlerDecision("GET /api/communications/threads", "ListThreadsAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationThreadReadService.ListThreadsAsync" },
            "The thread list is a query run AS the caller (CommunicationThreadReadService.cs:747): the query is "
            + "the gate."),
        new HandlerDecision("GET /api/communications/by-regarding/{entityType}/{id:guid}", "GetCommunicationsByRegardingAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationThreadReadService.ReadByRegardingAsync" },
            "Threads and messages for the caller-chosen record are queried AS the caller "
            + "(CommunicationThreadReadService.cs:330)."),
        new HandlerDecision("GET /api/communications", "QueryCommunicationsAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationThreadReadService.QueryCommunicationsAsync" },
            "Every filter (thread, regarding, participant) narrows a query run AS the caller "
            + "(CommunicationThreadReadService.cs:908)."),
        new HandlerDecision("GET /api/communications/queue-feed", "GetQueueFeedAsync", "IImpersonatedCommunicationQuery", new[] { "CommunicationQueueFeedService.GetQueueFeedAsync" },
            "The feed's communications are queried AS the caller (CommunicationQueueFeedService.cs:151); the "
            + "app-only proposal read (:233) is keyed only by those visible ids."),
        // Task 171: the read goes through ComposeSpeAccess.DownloadForComposeAsync — APP-ONLY exactly when
        // ComposeDocumentAuthorizationFilter authorized the item's sprk_document row (read) and verified its pointer;
        // otherwise (no row — Compose Path B) ON BEHALF OF the caller, where SPE decides both caller-chosen ids.
        new HandlerDecision("POST /api/compose/document/{documentSpeId}/pull-annotations", "PullAnnotations", "DownloadForComposeAsync", Array.Empty<string>(),
            "Reads the drive item through DownloadForComposeAsync (ComposeAnnotationEndpoints.cs:149): app-only only for "
            + "the row the route filter authorized; otherwise ON BEHALF OF the caller, so SPE decides both caller-chosen ids. "
            + "Nothing else is read."),
        new HandlerDecision("POST /api/compose/document/{documentSpeId}/reanchor-annotations", "ReanchorAnnotations", "DownloadForComposeAsync", Array.Empty<string>(),
            "Reads the drive item through DownloadForComposeAsync (ComposeAnnotationEndpoints.cs:248) — the row decision or, "
            + "with no row, the caller's own SPE read — then caches anchors under that item id."),
        new HandlerDecision("GET /api/dataverse/savedquery/{savedQueryId:guid}", "GetSavedQueryByIdAsync", "IDataversePrivilegeChecker", Array.Empty<string>(),
            "Loads only `savedquery` (system views, SavedQueryService.cs:100-104) and refuses 403 unless the "
            + "caller holds Read on the view's entity (HasReadPrivilegeAsync, SavedQueryEndpoints.cs:122) — the "
            + "correct decision for a schema read."),
        new HandlerDecision("GET /api/v1/external/projects", "GetProjects", "GetAccessibleProjectIds", Array.Empty<string>(),
            "Lists only the caller principal's Read-filtered project set "
            + "(callerContext.GetAccessibleProjectIds(), ExternalProjectDataEndpoints.cs:514)."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}", "GetProjectById", "HoldsReadOnProject", Array.Empty<string>(),
            "403 unless the caller holds Read on the route project (HoldsReadOnProject, "
            + "ExternalProjectDataEndpoints.cs:531)."),
        // Task 031 (external-access-platform-r3, FR-24): the project document routes now run the handlers generalized
        // over the document parent; the project's rights still come from RightsForRoot.
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/documents", "GetDocuments", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentsForParent: 403 unless RightsForDocumentParentAsync gives Read on the route "
            + "project (RightsForRoot, ExternalProjectDataEndpoints.cs:560) before the document list."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/content", "DownloadDocumentContent", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to DownloadDocumentForParent: Read on the project (ExternalProjectDataEndpoints.cs:606) AND the "
            + "document must belong to it through the project lookup (:624, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the app-only stream."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/todos", "GetTodos", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListTodosForRoot, which requires Read from RightsForRoot on the route project "
            + "(ExternalProjectDataEndpoints.cs:688-689)."),
        new HandlerDecision("POST /api/v1/external/projects/{id:guid}/todos", "CreateTodo", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForRootWriteAsync" },
            "Delegates to CreateTodoForRoot, which requires Create from RightsForRoot before any write — through "
            + "RightsForRootWriteAsync, which (task 036, FR-20) also confirms it against Dataverse for a Dataverse-decided "
            + "caller (ExternalProjectDataEndpoints.cs:729-730, :930)."),
        new HandlerDecision("GET /api/v1/external/matters/{id:guid}/todos", "GetMatterTodos", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListTodosForRoot: Read from RightsForRoot on the route matter "
            + "(ExternalProjectDataEndpoints.cs:688-689)."),
        new HandlerDecision("POST /api/v1/external/matters/{id:guid}/todos", "CreateMatterTodo", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForRootWriteAsync" },
            "Delegates to CreateTodoForRoot: Create from RightsForRoot on the route matter, through "
            + "RightsForRootWriteAsync (ExternalProjectDataEndpoints.cs:729-730, :930)."),
        new HandlerDecision("GET /api/v1/external/workassignments/{id:guid}/todos", "GetWorkAssignmentTodos", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListTodosForRoot: Read from RightsForRoot on the route work assignment "
            + "(ExternalProjectDataEndpoints.cs:688-689)."),
        new HandlerDecision("POST /api/v1/external/workassignments/{id:guid}/todos", "CreateWorkAssignmentTodo", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForRootWriteAsync" },
            "Delegates to CreateTodoForRoot: Create from RightsForRoot on the route work assignment, through "
            + "RightsForRootWriteAsync (ExternalProjectDataEndpoints.cs:729-730, :930)."),
        new HandlerDecision("POST /api/v1/external/projects/{id:guid}/documents", "UploadDocument", "AccessRights", Array.Empty<string>(),
            "Delegates to UploadDocumentForParent: requires Create on the route project from the caller principal's "
            + "rights before the upload (rights.HasFlag(AccessRights.Create), ExternalProjectDataEndpoints.cs:1269)."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/versions", "GetDocumentVersions", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentVersionsForParent: Read on the route project (ExternalProjectDataEndpoints.cs:1435) "
            + "and the document's project scoping (:1445, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the version read."),
        // Task 031 (FR-24): documents on matters and work assignments — the same four handlers, rights from RightsForRoot.
        new HandlerDecision("GET /api/v1/external/matters/{id:guid}/documents", "GetMatterDocuments", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentsForParent: Read on the route matter from RightsForRoot "
            + "(ExternalProjectDataEndpoints.cs:560) before the document list."),
        new HandlerDecision("GET /api/v1/external/matters/{id:guid}/documents/{documentId:guid}/content", "DownloadMatterDocumentContent", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to DownloadDocumentForParent: Read on the matter (ExternalProjectDataEndpoints.cs:606) AND the "
            + "document must belong to it through the matter lookup (:624, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the app-only stream."),
        new HandlerDecision("POST /api/v1/external/matters/{id:guid}/documents", "UploadMatterDocument", "AccessRights", Array.Empty<string>(),
            "Delegates to UploadDocumentForParent: Create on the route matter (rights.HasFlag(AccessRights.Create), "
            + "ExternalProjectDataEndpoints.cs:1269) before any SPE write."),
        new HandlerDecision("GET /api/v1/external/matters/{id:guid}/documents/{documentId:guid}/versions", "GetMatterDocumentVersions", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentVersionsForParent: Read on the matter (ExternalProjectDataEndpoints.cs:1435) and "
            + "the document's matter scoping (:1445, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the version read."),
        new HandlerDecision("GET /api/v1/external/workassignments/{id:guid}/documents", "GetWorkAssignmentDocuments", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentsForParent: Read on the route work assignment from RightsForRoot "
            + "(ExternalProjectDataEndpoints.cs:560) before the document list."),
        new HandlerDecision("GET /api/v1/external/workassignments/{id:guid}/documents/{documentId:guid}/content", "DownloadWorkAssignmentDocumentContent", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to DownloadDocumentForParent: Read on the work assignment (ExternalProjectDataEndpoints.cs:606) "
            + "AND the document's work-assignment scoping (:624, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the app-only stream."),
        new HandlerDecision("POST /api/v1/external/workassignments/{id:guid}/documents", "UploadWorkAssignmentDocument", "AccessRights", Array.Empty<string>(),
            "Delegates to UploadDocumentForParent: Create on the route work assignment (rights.HasFlag(AccessRights.Create), "
            + "ExternalProjectDataEndpoints.cs:1269) before any SPE write."),
        new HandlerDecision("GET /api/v1/external/workassignments/{id:guid}/documents/{documentId:guid}/versions", "GetWorkAssignmentDocumentVersions", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentVersionsForParent: Read on the work assignment (ExternalProjectDataEndpoints.cs:1435) "
            + "and the document's work-assignment scoping (:1445, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the version read."),
        // Task 031 (FR-24): a service request is not a root — its rights are the submitter scope, the Service Requests
        // module's accessible set (sprk_requestedby = the caller's contact), decided by IsRecordAccessible.
        new HandlerDecision("GET /api/v1/external/servicerequests/{id:guid}/documents", "GetServiceRequestDocuments", "IsRecordAccessible", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentsForParent: Read only when the request's requester is in the Service Requests "
            + "module's accessible set (module.IsRecordAccessible, ExternalProjectDataEndpoints.cs:989)."),
        new HandlerDecision("GET /api/v1/external/servicerequests/{id:guid}/documents/{documentId:guid}/content", "DownloadServiceRequestDocumentContent", "IsRecordAccessible", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to DownloadDocumentForParent: the requester check (ExternalProjectDataEndpoints.cs:989) AND the "
            + "document's sprk_relatedservicerequest scoping (:624, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the app-only stream."),
        new HandlerDecision("POST /api/v1/external/servicerequests/{id:guid}/documents", "UploadServiceRequestDocument", "IsRecordAccessible", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to UploadDocumentForParent: Read and Create only for the requester (module.IsRecordAccessible, "
            + "ExternalProjectDataEndpoints.cs:989) before any SPE write."),
        new HandlerDecision("GET /api/v1/external/servicerequests/{id:guid}/documents/{documentId:guid}/versions", "GetServiceRequestDocumentVersions", "IsRecordAccessible", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentVersionsForParent: the requester check (ExternalProjectDataEndpoints.cs:989) and the "
            + "document's service-request scoping (:1445, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the version read."),
        // Task 031 (FR-24, owner decision 2026-10-11 option A): an invoice is not a root and is not securable — its rights
        // are INHERITED, the union of the caller's RightsForRoot on the invoice's sprk_matter and sprk_project
        // (ExternalProjectDataEndpoints.RightsForInvoiceAsync), the rule ExternalMessageEndpoints.RightsThroughParents
        // already applies to an invoice post.
        new HandlerDecision("GET /api/v1/external/invoices/{id:guid}/documents", "GetInvoiceDocuments", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentsForParent: Read on the route invoice (ExternalProjectDataEndpoints.cs:560), the union "
            + "of RightsForRoot on its matter and project (RightsForInvoiceAsync, :925), before the document list."),
        new HandlerDecision("GET /api/v1/external/invoices/{id:guid}/documents/{documentId:guid}/content", "DownloadInvoiceDocumentContent", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to DownloadDocumentForParent: the inherited Read on the invoice (ExternalProjectDataEndpoints.cs:606, RightsForInvoiceAsync :925) "
            + "AND the document's sprk_invoice scoping (:624, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the app-only stream."),
        new HandlerDecision("POST /api/v1/external/invoices/{id:guid}/documents", "UploadInvoiceDocument", "AccessRights", Array.Empty<string>(),
            "Delegates to UploadDocumentForParent: Create on the route invoice, inherited from its matter or project "
            + "(rights.HasFlag(AccessRights.Create), ExternalProjectDataEndpoints.cs:1269) before any SPE write."),
        new HandlerDecision("GET /api/v1/external/invoices/{id:guid}/documents/{documentId:guid}/versions", "GetInvoiceDocumentVersions", "RightsForRoot", new[] { "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Delegates to ListDocumentVersionsForParent: the inherited Read on the invoice (ExternalProjectDataEndpoints.cs:1435, RightsForInvoiceAsync :925) "
            + "and the document's invoice scoping (:1445, IsReadableUnder: also Read on every SECURE linked root, by effective status (ExternalMultiRootRule / ExternalDataService.GetNonSecureRootsAsync; unknown counts as secure), one uniform 403) before the version read."),
        // Task 039 (external-access-platform-r3, FR-03): the project event read is generalized over the root kind, as the
        // to-do read is; the matter and work-assignment routes are two more adapters on the same handler.
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/events", "GetEvents", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListEventsForRoot: 403 unless RightsForRoot gives Read on the route project "
            + "(ExternalProjectDataEndpoints.cs:1524) before any Dataverse read."),
        new HandlerDecision("GET /api/v1/external/matters/{id:guid}/events", "GetMatterEvents", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListEventsForRoot: 403 unless RightsForRoot gives Read on the route matter "
            + "(ExternalProjectDataEndpoints.cs:1524) before any Dataverse read; an event also filed to a SECURE linked root the "
            + "caller cannot read is withheld, by effective status (ExternalDataService.ReadableEventsAsync, ExternalMultiRootRule; unknown counts as secure)."),
        new HandlerDecision("GET /api/v1/external/workassignments/{id:guid}/events", "GetWorkAssignmentEvents", "RightsForRoot", Array.Empty<string>(),
            "Delegates to ListEventsForRoot: 403 unless RightsForRoot gives Read on the route work assignment "
            + "(ExternalProjectDataEndpoints.cs:1524) before any Dataverse read; an event also filed to a SECURE linked root the "
            + "caller cannot read is withheld, by effective status (ExternalDataService.ReadableEventsAsync, ExternalMultiRootRule; unknown counts as secure)."),
        new HandlerDecision("POST /api/v1/external/projects/{id:guid}/events", "CreateEvent", "AccessRights", Array.Empty<string>(),
            "Requires Create on the route project (RightsForRootWriteAsync, ExternalProjectDataEndpoints.cs:1895-1897; "
            + "task 036 confirms it against Dataverse for a Dataverse-decided caller) before the write."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/contacts", "GetContacts", "HoldsReadOnProject", Array.Empty<string>(),
            "Read on the route project (ExternalProjectDataEndpoints.cs:1612)."),
        new HandlerDecision("GET /api/v1/external/projects/{id:guid}/organizations", "GetOrganizations", "HoldsReadOnProject", Array.Empty<string>(),
            "Read on the route project (ExternalProjectDataEndpoints.cs:1628)."),
        new HandlerDecision("PATCH /api/v1/external/todos/{id:guid}", "UpdateTodo", "RightsForRoot", Array.Empty<string>(),
            "Write on the to-do's ROOT from RightsForRoot before the patch "
            + "(ExternalProjectDataEndpoints.cs:1684-1690)."),
        // Task 032 (external-access-platform-r3, FR-26): delete-own. ONE check (AuthorizeDeleteOwnAsync) for all three:
        // the row's task-030 creator stamp must be the caller (ExternalDataService.IsCreatedByCaller,
        // ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync) AND the caller must hold Read on EVERY parent this plane
        // authorizes (the lookups ExternalDataService models: a document's project / matter / work assignment / service
        // request; a to-do's or event's regarding project / matter / work assignment; other lookups on the row, such as
        // sprk_invoice or sprk_parentdocument, are not consulted), through RightsForDocumentParentAsync (RightsForRoot for
        // a project / matter / work assignment; the requester scope for a service request). Absent / not-own / unstamped
        // / no-Read: one 403. A delete is a write, so it keeps the every-parent bar; the owner's option-B rule (2026-10-12:
        // Read on every SECURE linked root, by effective status) governs the READ paths and does not touch this one.
        new HandlerDecision("DELETE /api/v1/external/documents/{id:guid}", "DeleteOwnDocument", "RightsForRoot",
            new[] { "ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync", "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Creator stamp + Read on every modelled parent (AuthorizeDeleteOwnAsync, ExternalProjectDataEndpoints.cs:1749-1796) before "
            + "DocumentCheckoutService.DeleteAsync deletes the verified-pointer SPE file, then the row "
            + "(ExternalProjectDataEndpoints.cs:1845, DeleteOwnDocument)."),
        new HandlerDecision("DELETE /api/v1/external/todos/{id:guid}", "DeleteOwnTodo", "RightsForRoot",
            new[] { "ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync", "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Creator stamp + Read on every modelled root (AuthorizeDeleteOwnAsync, ExternalProjectDataEndpoints.cs:1749-1796) before "
            + "the app-only row delete (ExternalProjectDataEndpoints.cs:1907, DeleteAuthorizedRowAsync)."),
        new HandlerDecision("DELETE /api/v1/external/events/{id:guid}", "DeleteOwnEvent", "RightsForRoot",
            new[] { "ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync", "ExternalProjectDataEndpoints.RightsForDocumentParentAsync" },
            "Creator stamp + Read on every modelled root (AuthorizeDeleteOwnAsync, ExternalProjectDataEndpoints.cs:1749-1796) before "
            + "the app-only row delete (ExternalProjectDataEndpoints.cs:1922, DeleteAuthorizedRowAsync)."),
        // Owner decision 2026-10-11 (task 032 follow-up, FR-26), option A: the REQUESTER deletes a service request that has no
        // related row. Requester only through the same requester scope as the service-request document routes
        // (RightsForDocumentParentAsync -> the Service Requests module's IsRecordAccessible: sprk_requestedby = the caller's
        // contact; the partner switch is honoured); owner decision 2026-10-12: a closed (statecode 1) request is a 409
        // service_request_closed, the state read in the SAME row read as the requester and used only after the requester
        // gate; then every 1:N relationship is probed app-only (HasRelatedRecordsAsync) and any related row is a 409
        // has_related_records. Absent / not-requester: the same 403 as the other delete routes.
        new HandlerDecision("DELETE /api/v1/external/servicerequests/{id:guid}", "DeleteOwnServiceRequest", "IsRecordAccessible",
            new[] { "ExternalProjectDataEndpoints.RightsForServiceRequestAsync" },
            "Requester only (RightsForServiceRequestAsync -> module.IsRecordAccessible, ExternalProjectDataEndpoints.cs:989; the same "
            + "row read returns statecode), then open only (a closed request is 409 service_request_closed, DeleteOwnServiceRequest), "
            + "then no related row in any of the 8 relationships (ExternalDataService.HasRelatedRecordsAsync, "
            + "ExternalProjectDataEndpoints.cs:2002) before the app-only row delete (DeleteAuthorizedRowAsync, :2018)."),
        new HandlerDecision("POST /api/v1/external/api/dataverse/fetch", "ExecuteScopedFetchAsync", "Tier2ScopeFilterInjector", Array.Empty<string>(),
            "The caller's FetchXML is rewritten to their accessible-record set before it runs "
            + "(Tier2ScopeFilterInjector.Inject, ExternalModuleDataEndpoints.cs:277), then ScopeRows trims the "
            + "result (:300)."),
        new HandlerDecision("GET /api/v1/external/api/dataverse/record/{entityLogicalName}/{id:guid}", "GetScopedRecordAsync", "IsRecordAccessible", Array.Empty<string>(),
            "Refused unless the record is in the caller principal's accessible set (module.IsRecordAccessible, "
            + "ExternalModuleDataEndpoints.cs:405)."),
        new HandlerDecision("GET /api/v1/external/{root}/{id:guid}/messages", "GetMessagesAsync", "IsRecordAccessible",
            new[] { "ExternalMessageEndpoints.GetMessagesCoreAsync", "ExternalMessageEndpoints.IsRootRecordReadableAsync" },
            "403 unless the route record is in the caller principal's accessible set, decided by the registered module "
            + "(module.IsRecordAccessible for project / matter / work assignment; the module's ScopeRows over the record's "
            + "own scope lookups for an invoice or the caller's own service request), before the app-only message read "
            + "(ExternalMessageEndpoints.cs:339 the gate call, :506 EvaluateRecordGateAsync, :563 ScopeRows); an unsupported root is a 404 with no read."),
        new HandlerDecision("POST /api/v1/external/{root}/{id:guid}/messages", "PostMessageAsync", "IsRecordAccessible",
            new[] { "ExternalMessageEndpoints.PostMessageCoreAsync", "ExternalMessageEndpoints.GetRecordRightsAsync" },
            "Task 034 (FR-11): the SAME record gate as the GET (module.IsRecordAccessible / ScopeRows, through "
            + "EvaluateRecordGateAsync, ExternalMessageEndpoints.cs:506, :563) — outside the set, absent or no grants is the "
            + "same 403 — and then Create on the record (the caller's rights on the root, on an invoice's parent root, or "
            + "the requester on their own service request; :211, :223) before CommunicationService.PostToRecordThreadAsync "
            + "writes app-only (:233). The body is validated and an unattributable sender refused before any read (:191, :200)."),
        new HandlerDecision("POST /api/v1/external/servicerequests", "SubmitServiceRequestAsync", "IsEntitledAsync", Array.Empty<string>(),
            "Task 035 (FR-05/FR-06): 403 unless the caller principal is entitled to legal-front-door "
            + "(ModuleEntitlementResolver.IsEntitledAsync, ExternalIntakeEndpoints.cs:161 — a CIAM partner only with the partner "
            + "switch on), may submit the requested type (ModuleEntitlementResolver.IsIntakeTypeAvailable, :176 — Invention "
            + "for a CIAM partner only with PartnerInventionIntakeEnabled on; owner 2026-10-12) and has a contact (:194, :205), "
            + "before the one app-only create, whose sprk_requestedby is that contact (:292) and whose owner is the default team "
            + "of that contact's business unit, resolved first and refused when it cannot be (:232, :261). The requester's "
            + "department is read app-only from that contact (:270) and stored as a system key, never taken from the body. "
            + "The route names no existing record."),
        new HandlerDecision("GET /api/office/communications/by-message-id/{internetMessageId}", "FindByMessageIdAsync", "IDataverseUserClient", Array.Empty<string>(),
            "Reads through IDataverseUserClient under the CALLER's Dataverse security (task 127, "
            + "CommunicationsEndpoints.cs:145, :179); an unreadable communication 404s like a missing one."),
        new HandlerDecision("GET /api/office/communications/by-message-id/{internetMessageId}/suggestions", "GetSuggestionsByMessageIdAsync", "IDataverseUserClient", Array.Empty<string>(),
            "The communication lookup AND the candidate names run under the caller's security "
            + "(CommunicationsEndpoints.cs:349, :380). Residual on derived flags is task 161's (note)."),
        new HandlerDecision("GET /api/office/communications/{commId:guid}/linked-todos", "GetLinkedTodosAsync", "IDataverseUserClient", Array.Empty<string>(),
            "The sprk_todo query runs under the caller's security (CommunicationsEndpoints.cs:553); an "
            + "unreadable communication yields an empty list."),
        new HandlerDecision("GET /api/office/search/entities", "SearchEntitiesAsync", "IImpersonatedCommunicationQuery", new[] { "OfficeService.SearchEntitiesAsync", "OfficeSearchService.SearchEntitiesAsync" },
            "Each per-type query runs AS the caller (MSCRMCallerID, OfficeSearchService.cs:362) — the query is "
            + "the gate, with no app-only fallback (word-add-in-r1 task 062)."),
        new HandlerDecision("GET /api/documents/{documentId}/permissions", "GetDocumentPermissionsAsync", "AuthorizationService", Array.Empty<string>(),
            "The caller's rights on the route document come from AuthorizationService.GetCallerAccessAsync "
            + "(PermissionsEndpoints.cs:90)."),
        new HandlerDecision("POST /api/documents/permissions/batch", "GetBatchPermissionsAsync", "AuthorizationService", Array.Empty<string>(),
            "Each requested document's rights come from AuthorizationService.GetCallerAccessAsync as the caller "
            + "(PermissionsEndpoints.cs:177)."),
        new HandlerDecision("GET /api/workspace/layouts", "GetLayouts", "WorkspaceLayoutService", Array.Empty<string>(),
            "System layouts plus layouts filtered by the caller's ownerid "
            + "(WorkspaceLayoutService.GetLayoutsAsync, WorkspaceLayoutEndpoints.cs:158)."),
        new HandlerDecision("GET /api/workspace/layouts/default", "GetDefaultLayout", "WorkspaceLayoutService", Array.Empty<string>(),
            "The caller's own default, or a system layout (WorkspaceLayoutService.GetDefaultLayoutAsync, "
            + "WorkspaceLayoutEndpoints.cs:197)."),
        new HandlerDecision("GET /api/workspace/layouts/{id:guid}", "GetLayoutById", "WorkspaceLayoutService", Array.Empty<string>(),
            "GetLayoutByIdAsync denies unless the row's ownerid is the caller's systemuserid "
            + "(WorkspaceLayoutService.cs:197-212)."),
        new HandlerDecision("POST /api/workspace/layouts", "CreateLayout", "WorkspaceLayoutService", Array.Empty<string>(),
            "Creates a layout owned by the caller (WorkspaceLayoutService.CreateLayoutAsync, "
            + "WorkspaceLayoutEndpoints.cs:297)."),
        new HandlerDecision("PUT /api/workspace/layouts/{id:guid}", "UpdateLayout", "WorkspaceLayoutService", Array.Empty<string>(),
            "UpdateLayoutAsync answers Forbidden unless the caller owns the layout "
            + "(WorkspaceLayoutEndpoints.cs:367-371)."),
        new HandlerDecision("DELETE /api/workspace/layouts/{id:guid}", "DeleteLayout", "WorkspaceLayoutService", Array.Empty<string>(),
            "DeleteLayoutAsync is owner-scoped like the read (WorkspaceLayoutEndpoints.cs:474)."),
        new HandlerDecision("GET /api/me/capabilities", "inline", "ForUserAsync", new[] { "SpeFileStore.GetUserCapabilitiesAsync", "UserOperations.GetUserCapabilitiesAsync" },
            "The container's drive is read with the OBO Graph client (UserOperations.cs:62-68), so SPE decides "
            + "the only caller-chosen id and the answer is the caller's own capability."),

        // ---- added at the task-167 integration (2026-10-05), from the fix tasks' ledger inputs ("handler decision") ----
        new HandlerDecision("GET /api/v1/events", "GetEventsAsync", "DataverseImpersonation",
            new[] { "DataverseWebApiService.QueryEventsAsCallerAsync", "DataverseWebApiService.QueryEventsCoreAsync" },
            "Task 159 (#1098; sweep S-12): the list runs AS the caller — QueryEventsAsCallerAsync refuses an empty caller "
            + "(DataverseWebApiService.cs:343-350) and sends MSCRMCallerID through DataverseImpersonation.ApplyAsSystemUser "
            + "(:205-207); an unresolvable caller is 403 before any query (EventEndpoints.cs:341-498). The query is the gate."),
        new HandlerDecision("GET /api/v1/signals/{signalId:guid}/decision-plan", "GetDecisionPlanAsync", "IDataverseUserClient",
            new[] { "SignalCoreRecordAccess.AuthorizeAsync" },
            "Ontology task 036: the handler's first act is SignalCoreRecordAccess.AuthorizeAsync (DecisionPlanEndpoints.cs:169), which reads "
            + "the Signal (SignalCoreRecordAccess.cs:164) and then its core record (:216), or for a no-core Do Signal its subject row "
            + "(:281; a thrown or faulted read is Unavailable, :286/:291), through IDataverseUserClient AS THE CALLER; for a Do-lane Signal "
            + "it then runs ConfirmDoLaneMineAsync (:314, called at :230/:296), the worklist's own \"mine\" read of the subject AS THE CALLER "
            + "(:342-343; D-141): not the caller's own work is NotFound, a fault Unavailable. "
            + "Anything but Allowed returns before the plan is read (uniform 404 at DecisionPlanEndpoints.cs:180-182, caller_unresolved 403 at :170-172, "
            + "a subject-read or \"mine\" fault 503 at :175-177)."),
        new HandlerDecision("POST /api/v1/signals/{signalId:guid}/decision", "CommitDecisionAsync", "IDataverseUserClient",
            new[] { "DecisionCommitService.CommitAsync", "SignalCoreRecordAccess.AuthorizeAsync" },
            "Ontology task 043: the handler calls DecisionCommitService.CommitAsync (DecisionCommitEndpoints.cs:56), whose first I/O is "
            + "SignalCoreRecordAccess.AuthorizeAsync for every Signal in the review (DecisionCommitService.cs:461, readability only: enforceDoLaneMine false) -- the Signal and "
            + "its core record (or, no-core, its subject row) read AS THE CALLER through IDataverseUserClient; anything but Allowed returns before the review-id "
            + "ledger, any app-only read or any write (uniform 404 at :466-467, caller_unresolved 403 at :462-463, 503 at :464-465). The Do-lane \"mine\" "
            + "half (D-141, ConfirmDoLaneMineAsync :568, the same three answers) runs right after the ledger peek (:481-482), before any "
            + "lock, ledger answer or write; it is skipped only when the same caller resends the same content (the ledger key carries the "
            + "caller and the stored hash the content), runs under the lock when that entry is gone (:621-622), and runs again before a "
            + "resume (:645) or a repair replay (:759) unless that review's own reassign step landed (:638). Before any "
            + "write or close it also asks, as the caller through CallerRecordAccessProbe, Write on each Signal's subject and "
            + "AppendTo on the core record (FR-51; :964, AppendTo :1472, Write :1495), also before a stored-record repeat closes anything and again "
            + "before a replay repairs a close (:803), and every executor's own preflight. The Decision Record's owning team is the "
            + "ownership resolver's answer for EVERY parent the record sits under (the core, if any, and the subject of each Signal it "
            + "resolves, secure-if-any; D-129, uac-r2 F1), taken as returned and asked only AFTER that check (:1069), so the answer never shows "
            + "whether a row exists (a refusal is 422, a resolver fault 503: :1071-1085)."),
        new HandlerDecision("GET /api/v1/signals/{signalId:guid}/suppression", "GetSuppressionAsync", "IDataverseUserClient",
            new[] { "SignalCoreRecordAccess.AuthorizeAsync" },
            "Ontology task 034: the handler's first act is SignalCoreRecordAccess.AuthorizeAsync (DecisionPlanEndpoints.cs:75), "
            + "the same decision as decision-plan: the Signal, then its core record of any type, read AS THE CALLER through IDataverseUserClient "
            + "(a no-core Do Signal needs the caller's own read of the Signal and its subject, D-35 as amended by D-129), and for a Do-lane Signal the same \"mine\" read (D-141). Anything but Allowed returns the uniform 404 (:86-88) / caller_unresolved 403 (:76-78) / subject-or-\"mine\"-fault 503 (:81-83) before the count is "
            + "read. The count is the caller's: the grain's Signal ids are read through the caller's own IDataverseUserClient and the app-only rows are "
            + "trimmed to that set before anything is counted; Decision-Record facts are read app-only for those rows only. For a Do item the count is the "
            + "item's person's own (D-145a): the handler passes decision.CallerContactId (DecisionPlanEndpoints.cs:98, set from the \"mine\" read at "
            + "SignalCoreRecordAccess.cs:381/:386), and a \"mine\" caller who is not that person (owner, creator, assigner) gets a 200 empty status "
            + "that counts toward nothing (SignalSuppressionService.cs:918-922), not a 404. Only the counts and the "
            + "suppressed-until date are returned. A fault after Allowed is a 503 problem with the trace id, never the 404 (that is for not-yours). Lives in the already-governed DecisionPlanEndpoints.cs, so the file count is unchanged."),
        new HandlerDecision("GET /api/v1/ontology/admin/authoring/catalog", "GetCatalogAsync", "IDataverseUserClient",
            new[] { "OntologyAdminAuthoringService.GetCatalogAsync" },
            "Ontology task 101: the handler's only I/O is OntologyAdminAuthoringService.GetCatalogAsync (OntologyAdminAuthoringEndpoints.cs:68), "
            + "which resolves the caller with WhoAmI AS THE CALLER (OntologyAdminAuthoringService.cs:189, :1362; unresolved: the single 403) "
            + "and reads the triage categories AS THE CALLER (:253). The rest of the catalog is the compiler's static lists; no id is taken. "
            + "Not privilege-gated (fix round 1, F1-1): it evaluates nothing and shows only static lists and the caller's own read."),
        new HandlerDecision("POST /api/v1/ontology/admin/authoring/dry-run", "DryRunAsync", "IDataverseUserClient",
            new[] { "OntologyAdminAuthoringService.DryRunAsync" },
            "Ontology task 101 (#39): the handler's only I/O is OntologyAdminAuthoringService.DryRunAsync (OntologyAdminAuthoringEndpoints.cs:72). "
            + "WhoAmI as the caller first (:294); then, because a dry run writes nothing and so Dataverse checks no role on it, the caller's "
            + "prvCreatesprk_PolicyVersion is asked FRESH, AS THE CALLER, on every request (:301, :1387; RetrieveUserPrivilegeByPrivilegeName "
            + "bound to the WhoAmI id; not held: 403 ontology.admin.forbidden with nothing read or evaluated; fault: 503; never the capability "
            + "probe's answer). A body policyId is read AS THE CALLER through the Ontology domain allow-list (:323, :1427; absent, unreadable or "
            + "non-Ontology: the uniform 404) before anything is evaluated. The draft is evaluated as the EVALUATOR (writes nothing, :358), then "
            + "every hit's subject and core record are re-read AS THE CALLER (:476, :493) and a row is returned only when both come back; the "
            + "policy's Signals are read AS THE CALLER (:544). Nothing the caller cannot read is returned or counted."),
        new HandlerDecision("POST /api/v1/ontology/admin/authoring/validate", "ValidateAsync", "IDataverseUserClient",
            new[] { "OntologyAdminAuthoringService.ValidateAsync" },
            "Ontology task 102a: the handler's only I/O is OntologyAdminAuthoringService.ValidateAsync (OntologyAdminAuthoringEndpoints.cs:86). "
            + "Gated exactly as the dry run: WhoAmI as the caller first (OntologyAdminAuthoringService.cs:720; unresolved: the single 403), "
            + "then the caller's prvCreatesprk_PolicyVersion asked FRESH, AS THE CALLER (:726; not held: 403 ontology.admin.forbidden, "
            + "nothing in the request is read). A body policyId is read AS THE CALLER through the Ontology domain allow-list (:741; absent, "
            + "unreadable or non-Ontology: the uniform 404). The draft is then compiled only, through the same ValidateDraft save and "
            + "publish use (:766, PolicyVersionValidator.ValidateForAuthoring, including the authorable-column allow-list): nothing is "
            + "written, nothing is evaluated, and no dry-run proof is recorded. Lives in the already-governed "
            + "OntologyAdminAuthoringEndpoints.cs, so the file count is unchanged."),
        new HandlerDecision("POST /api/v1/ontology/admin/authoring/policies", "SavePolicyAsync", "IDataverseUserClient",
            new[] { "OntologyAdminAuthoringService.SavePolicyAsync" },
            "Ontology task 101 (FR-10): the handler's only I/O is OntologyAdminAuthoringService.SavePolicyAsync (OntologyAdminAuthoringEndpoints.cs:77). "
            + "WhoAmI as the caller first (:661); then validation and the dry-run proof, then the policy (Off, Ontology domain) and its first "
            + "version are created AS THE CALLER in one deep insert (:771) and the pointer set AS THE CALLER (:789): Dataverse enforces the "
            + "Spaarke Ontology Administrator role (refused: 403, nothing written). Takes no id."),
        new HandlerDecision("POST /api/v1/ontology/admin/authoring/policies/{policyId:guid}/versions", "PublishVersionAsync", "IDataverseUserClient",
            new[] { "OntologyAdminAuthoringService.PublishVersionAsync" },
            "Ontology task 101 (D-13, D-14): the handler's only I/O is OntologyAdminAuthoringService.PublishVersionAsync "
            + "(OntologyAdminAuthoringEndpoints.cs:85). WhoAmI as the caller first (:831); the route's policyId is read AS THE CALLER through "
            + "the Ontology domain allow-list (:849, :1427) before ANY write (absent, unreadable or non-Ontology: the uniform 404, zero writes). "
            + "The version create (:902), the old version's sprk_inforceto stamp (:987, the only PATCH of a version, :967-968) and the pointer "
            + "move (:1008) all run AS THE CALLER, row-version conditional; only then is the job's single-policy pass run as the evaluator (:929)."),
        new HandlerDecision("GET /api/v1/signals/worklist", "GetWorklistAsync", "IDataverseUserClient",
            new[] { "SignalReadService.ReadAsync" },
            "Ontology task 038: the handler's only I/O is SignalReadService.ReadAsync (SignalReadEndpoints.cs:60). It resolves the caller "
            + "with WhoAmI AS THE CALLER (SignalReadService.cs:310, ResolveMineCallerAsync; WhoAmI at :1035; unresolved: the single 403 at SignalReadEndpoints.cs:64), runs the "
            + "worklist FetchXML as the caller (:333), keeps a row only when the caller's own batched read of its catalog-named core "
            + "table returns its core id (:900; the table must be in CoreAncestorResolver.CoreRecordEntities), and keeps a Do-lane row "
            + "only when the caller's own read of its subject, filtered to the caller's work, returns it (:954-955). Nothing app-only is "
            + "returned unless the caller's read admitted its row (the app-only secure-family placement only WITHHOLDS evidence detail, "
            + "D-123; the rule short name, work type and severity are read app-only from sprk_policy by id for the rows kept, D-185 task 163, "
            + "so the worklist needs no caller Read on sprk_policy: configuration, not matter data); a fault is 503 (SignalReadEndpoints.cs:73), "
            + "never a shorter list."),
        new HandlerDecision("GET /api/v1/ontology/admin/classification/categories", "ListCategoriesAsync", "IDataverseUserClient",
            new[] { "OntologyAdminClassificationService.ListAsync" },
            "Ontology task 103: the handler's only I/O is OntologyAdminClassificationService.ListAsync (OntologyAdminClassificationEndpoints.cs:111), "
            + "every read of which (the categories :75, the policies and their versions :84, the 30-day aggregate :85) goes through the caller's own "
            + "IDataverseUserClient, so Dataverse trims to what the caller may read. An unresolved caller is the single 403 "
            + "(OntologyAdminClassificationEndpoints.cs:163); a fault reading the categories is 503."),
        new HandlerDecision("POST /api/v1/ontology/admin/classification/categories", "CreateCategoryAsync", "IDataverseUserClient",
            new[] { "OntologyAdminClassificationService.CreateAsync" },
            "Ontology task 103: CreateAsync (OntologyAdminClassificationEndpoints.cs:122) reads the names and then POSTs the row AS THE CALLER "
            + "through IDataverseUserClient (OntologyAdminClassificationService.cs:190), so Dataverse enforces prvCreatesprk_TriageCategory "
            + "(a Console User gets 403 not_permitted). The row is written with sprk_enabled = false (:183); the request has no enabled member."),
        new HandlerDecision("PATCH /api/v1/ontology/admin/classification/categories/{categoryId:guid}", "UpdateCategoryAsync", "IDataverseUserClient",
            new[] { "OntologyAdminClassificationService.UpdateAsync" },
            "Ontology task 103: UpdateAsync (OntologyAdminClassificationEndpoints.cs:135) reads the row (OntologyAdminClassificationService.cs:792; "
            + "a row the caller cannot read is the uniform 404), then, for a rename only (:250), refuses it while the routing configuration is keyed by "
            + "the name (:342, in process through IOptionsMonitor, no app identity), unless the caller's own read covers every policy version "
            + "(:351; WhoAmI :451, then every RolePrivilege row for prvReadsprk_Policy and prvReadsprk_PolicyVersion :513, and, only when a Deep row "
            + "decides, the root business unit :563, all AS THE CALLER; a privilege is covered only by a row that is Global, or Deep on the ROOT "
            + "business unit, with no RecordFilterId: a Deep grant on a child unit (a team role), a conditional grant or a row with no business unit "
            + "gives 409 rename_unverifiable and the reference read is never made, and a root-unit read that is not exactly one row is 503), "
            + "while any policy version reads the category by id or name, or when that cannot be checked (503). It then PATCHes AS THE CALLER "
            + "through IDataverseUserClient, version-conditional (:808), so Dataverse enforces prvWritesprk_TriageCategory."),
        new HandlerDecision("PUT /api/v1/ontology/admin/classification/categories/{categoryId:guid}/enabled", "SetCategoryEnabledAsync", "IDataverseUserClient",
            new[] { "OntologyAdminClassificationService.SetEnabledAsync" },
            "Ontology task 103: SetEnabledAsync (OntologyAdminClassificationEndpoints.cs:147) reads the row (OntologyAdminClassificationService.cs:792), "
            + "re-checks the name among the active categories when turning On, then PATCHes sprk_enabled AS THE CALLER through IDataverseUserClient, "
            + "version-conditional (:808, from :320), so Dataverse enforces prvWritesprk_TriageCategory."),
        new HandlerDecision("GET /api/v1/ontology/admin/rules", "ListRulesAsync", "IDataverseUserClient",
            new[] { "OntologyAdminRulesService.ListAsync" },
            "Ontology task 100: the handler's only I/O is OntologyAdminRulesService.ListAsync (OntologyAdminRulesEndpoints.cs:83). It lists the "
            + "policies, their version rows and the user-visible Signal numbers through IDataverseUserClient AS THE CALLER (GetRowsAsync, "
            + "OntologyAdminRulesService.cs:441), each policy query carrying the Ontology-only domain condition (:142, :152); a caller no "
            + "user context names is the single 403 (Classify, :487), a fault 503, never a shorter list (:153-162). Takes no id."),
        new HandlerDecision("GET /api/v1/ontology/admin/rules/{policyId:guid}", "GetRuleAsync", "IDataverseUserClient",
            new[] { "OntologyAdminRulesService.GetAsync" },
            "Ontology task 100: the handler's only I/O is OntologyAdminRulesService.GetAsync (OntologyAdminRulesEndpoints.cs:89), whose first act "
            + "is ReadPolicyAsync (OntologyAdminRulesService.cs:202): the policy by id through IDataverseUserClient AS THE CALLER with the "
            + "Ontology-only domain condition (:476). A rule the caller cannot read, that does not exist or is a Billing/Docketing rule is the "
            + "uniform 404 before the versions, names or numbers are read (:203-206)."),
        new HandlerDecision("PUT /api/v1/ontology/admin/rules/{policyId:guid}/enabled", "SetRuleEnabledAsync", "IDataverseUserClient",
            new[] { "OntologyAdminRulesService.SetEnabledAsync" },
            "Ontology task 100 (D-22): the handler's only I/O is OntologyAdminRulesService.SetEnabledAsync (OntologyAdminRulesEndpoints.cs:96). "
            + "It reads the policy by id AS THE CALLER with the Ontology-only domain condition (OntologyAdminRulesService.cs:281, :476; a missing, "
            + "invisible or Billing rule is the uniform 404 with no write), refuses an inactive rule (:287-291), then PATCHes sprk_enabled through "
            + "IDataverseUserClient (WriteAsync, :294, :410-412) so Dataverse enforces the Ontology Administrator role: a refusal is 403 (:425-427). "
            + "The PATCH is issued even when the value is unchanged, so Dataverse decides every call."),
        new HandlerDecision("POST /api/v1/ontology/admin/rules/{policyId:guid}/retire", "RetireRuleAsync", "IDataverseUserClient",
            new[] { "OntologyAdminRulesService.RetireAsync" },
            "Ontology task 100 (D-22, FR-16): the handler's only I/O is OntologyAdminRulesService.RetireAsync (OntologyAdminRulesEndpoints.cs:103). "
            + "A missing reason is refused before any I/O (:311-322). It reads the policy by id AS THE CALLER with the Ontology-only domain "
            + "condition (OntologyAdminRulesService.cs:324, :476; uniform 404, no write), PATCHes statecode/statuscode/sprk_retiredreason through "
            + "IDataverseUserClient conditional on the row version it read (WriteAsync, :350, :410-411; a concurrent change is a 412, returned as a 409 "
            + "with no closure, :427; a Console User is refused by Dataverse, 403), and ONLY THEN calls task 033's PolicyRetired closure "
            + "(CloseOpenSignalsAsync, :361): a refused write returns before the closure (:351-358)."),
        new HandlerDecision("POST /api/v1/child-records/{table}", "CreateAsync", "IDataverseUserClient", Array.Empty<string>(),
            "Task 147 r1 (G5): every check is asked AS THE CALLER through IDataverseUserClient — WhoAmI first (a caller it "
            + "cannot name is the single 403, ontology task 046; ChildRecordEndpoints.cs:229), the payload mapped with the "
            + "caller's client (:243), then OwnedChildWrite.CreateAsync checks the table privilege and AppendTo on EVERY bound "
            + "record before the app-only create (:258) — for a work assignment (task 046, D-113) with the secure-create plan."),
        new HandlerDecision("PATCH /api/v1/child-records/{table}/{id:guid}", "RefileAsync", "IDataverseUserClient", Array.Empty<string>(),
            "Task 147 r1: the re-file is the caller's own PATCH through IDataverseUserClient (ChildRecordEndpoints.cs:432-433 → "
            + "UpdateAsync, :448): an unreadable row is the uniform 404, AppendTo on every new parent and F3 on a move out of a "
            + "secure record are asked as the caller (OwnedChildWrite.RefileAsync, :988)."),
        new HandlerDecision("POST /api/memory/pins", "CreatePinAsync", "CallerRecordAccessProbe", Array.Empty<string>(),
            "Task 166 (sweep S-43): the matter a pin names is checked AppendTo (memory.pin_matter) AS THE CALLER through "
            + "CallerRecordAccessProbe before the repository write (PinnedMemoryEndpoints.cs:319-325, :725-760); unknown and "
            + "forbidden matters are the same 403; a pin with no matter is private to the caller."),
        new HandlerDecision("PUT /api/memory/pins/{pinId}", "UpdatePinAsync", "CallerRecordAccessProbe", Array.Empty<string>(),
            "Task 166 (sweep S-68): another user's pin is answered exactly as an unknown one (PinnedMemoryEndpoints.cs:432, "
            + ":443), then the NEW matter is checked AppendTo as the caller through CallerRecordAccessProbe (:448)."),
        new HandlerDecision("POST /api/v1/field-mappings/push", "PushFieldMappingsAsync", "CallerRecordAccessProbe", Array.Empty<string>(),
            "Task 166 (sweep S-67): Read on the SOURCE record and the caller's systemuserid through CallerRecordAccessProbe "
            + "(FieldMappingEndpoints.cs:474, :776), then the source and the children are read and written impersonated as "
            + "the caller (:530, :710); an unauthorized source is the uniform 404."),
        new HandlerDecision("GET /api/v1/records/{table}/{id:guid}/family", "GetFamilyAsync", "CallerRecordAccessProbe", Array.Empty<string>(),
            "Task 072 (FR-31): the caller's systemuserid (WhoAmI on their own token, RecordFamilyEndpoints.cs:212) and Read on the "
            + "ANCHOR through CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync on the reader's own entity set for the "
            + "route table (:139, :152, :164) before anything is read; no Read, an unknown id, a non-family table and a probe fault "
            + "are the uniform 404. The walk then reads every member AS THE CALLER (RecordFamilyReader over "
            + "IImpersonatedCommunicationQuery, :173): a record the caller cannot read is never listed."),
        new HandlerDecision("POST /api/compose/active-document", "RegisterActiveDocument", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1 (was UNOWNED-NEW): the body session must be the caller's (ResolveOwnedSessionAsync, "
            + "ComposeActiveDocumentEndpoints.cs:402), and a STORED document is recorded only when the caller can read its row "
            + "through IDataverseUserClient (:268-272) — the pointer recorded is that row's, never the body's."),
        new HandlerDecision("GET /api/reporting/embed-token", "GetEmbedToken", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1 (sweep S-71): the sprk_report row is read AS THE CALLER (ReadCatalogRowAsync over IDataverseUserClient, "
            + "ReportingEndpoints.cs:234); an unreadable row is the uniform 404 before Power BI is asked, the workspace comes "
            + "from the row and must be allowed (f1), and the RLS identity is server-computed."),
        new HandlerDecision("GET /api/reporting/reports", "GetReports", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1: the catalog list is a query run AS THE CALLER (dataverseUser.GetAsync, ReportingEndpoints.cs:296) — "
            + "the query is the gate — and rows of a workspace this deployment does not allow are omitted (f1)."),
        new HandlerDecision("GET /api/reporting/reports/{reportId:guid}", "GetReport", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1 (sweep S-81): the row is read AS THE CALLER (ReadActionableRowAsync, ReportingEndpoints.cs:354); an "
            + "unreadable or not-allowed-workspace row is the uniform 404."),
        new HandlerDecision("POST /api/reporting/reports", "CreateReport", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1/r2: prvCreatesprk_Report AS THE CALLER before any read or clone (CallerRecordAccessProbe), the SOURCE "
            + "row read as the caller (ReportingEndpoints.cs:415) and the new row created as the caller; an unreadable source "
            + "is the uniform 404."),
        new HandlerDecision("PATCH /api/reporting/reports/{reportId:guid}", "UpdateReport", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1 (replaces PUT): the row is read and written AS THE CALLER (ReadActionableRowAsync, "
            + "ReportingEndpoints.cs:559); an unreadable row is the uniform 404 and nothing is written."),
        new HandlerDecision("DELETE /api/reporting/reports/{reportId:guid}", "DeleteReport", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1/r2: the row is read (ReportingEndpoints.cs:628) and DELETED as the caller first; the Power BI report "
            + "goes only for an unreferenced custom row, so a caller without Delete on the row deletes nothing."),
        new HandlerDecision("POST /api/reporting/export", "ExportReport", "IDataverseUserClient", Array.Empty<string>(),
            "Task 166 r1/r2 (sweep S-70): the row is read AS THE CALLER (ReadCatalogRowAsync, ReportingEndpoints.cs:720); an "
            + "unreadable row is the uniform 404 before Power BI is asked, and the export carries the server-computed "
            + "business-unit RLS identity (503 without it)."),
    };

    /// <summary>
    /// Caller-context seams: mechanisms that make a read or write happen AS the caller (or against the caller's
    /// computed rights), so the platform — not the BFF's own identity — decides. Each entry names its type's file and
    /// why it decides. A general service type is never admitted (task 120 refused IOfficeService for that reason).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string TypeFile, string Reason)> CallerContextSeams =
        new Dictionary<string, (string TypeFile, string Reason)>(StringComparer.Ordinal)
        {
            ["IDataverseUserClient"] = ("Infrastructure/Dataverse/IDataverseUserClient.cs",
                "A Dataverse Web API client that runs under the CALLER's security context (OBO), so row security decides."),
            ["DownloadForComposeAsync"] = ("Services/Compose/ComposeSpeAccess.cs",
                "Task 171: Compose's ONE byte identity rule — app-only only for the item ComposeDocumentAuthorizationFilter "
                + "authorized (its sprk_document row + a verified pointer); otherwise the caller's own OBO read, where SPE decides."),
            ["IImpersonatedCommunicationQuery"] = ("Services/Communication/IImpersonatedCommunicationQuery.cs",
                "App-only query WITH MSCRMCallerID = the caller's systemuserid: Dataverse applies the caller's row security."),
            ["DataverseImpersonation"] = ("../../shared/Spaarke.Dataverse/DataverseImpersonation.cs",
                "The single SDK impersonation entry point (task 160) — a ServiceClient call made as the caller."),
            ["HoldsReadOnProject"] = ("Api/ExternalAccess/ExternalProjectDataEndpoints.cs",
                "Read on the project from the caller principal's computed rights (RightsForRoot), 403 before any read."),
            ["RightsForRoot"] = ("Api/ExternalAccess/ExternalProjectDataEndpoints.cs",
                "The caller principal's rights on a project / matter / work-assignment root; None for an unknown root."),
            ["GetAccessibleProjectIds"] = ("Infrastructure/ExternalAccess/CallerPrincipalResolver.cs",
                "The caller principal's Read-filtered project set (ReadableProjects) — a list trimmed to the caller's rights."),
            ["IsRecordAccessible"] = ("Infrastructure/ExternalAccess/ExternalModuleRegistry.cs",
                "Membership of a record in the caller principal's accessible set for a registered module."),
            ["IsEntitledAsync"] = ("Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs",
                "The caller principal's Tier-1 module entitlement (r3 task 035): the same set /me/entitlements reports, from "
                + "the server-resolved principal kind and the bound partner switch; false fails the route closed. A MODULE-level "
                + "decision only: it decides nothing about a record, so it may credit only a route that names none (no '{' in "
                + "the route; EveryHandlerDecisionIsVerified enforces it)."),
            ["Tier2ScopeFilterInjector"] = ("Api/ExternalAccess/Tier2ScopeFilterInjector.cs",
                "Rewrites the caller's FetchXML to their accessible-record set before it runs — the query is the gate."),
            ["DownloadFileAsUserAsync"] = ("Infrastructure/Graph/SpeFileStore.cs",
                "The OBO SPE download: SPE evaluates the caller's own permission on the (driveId, itemId) the caller named."),
            ["ForUserAsync"] = ("Infrastructure/Graph/IGraphClientFactory.cs",
                "The OBO Graph client: every SPE call made with it is evaluated against the caller's own permissions."),
        };

    // =============================================================================================
    // RULE B VOCABULARY
    // =============================================================================================

    /// <summary>
    /// The types that constitute "an authorization decision". Matched as WHOLE identifiers in code (task 167) — a
    /// filter referencing one of them is consulting a real decision path.
    /// </summary>
    private static readonly string[] DecisionServices =
    {
        "AuthorizationService",          // Spaarke.Core.Auth — the canonical evaluator
        "IAiAuthorizationService",       // task 167: its own entry, no longer a substring accident. AnalysisAuthorizationFilter's
                                         // DocumentAccess path (:139) and VisualizationAuthorizationFilter (:228) call AuthorizeAsync on it.
        "IAccessDataSource",             // the Dataverse rights resolver behind it
        "AccessRights",                  // the rights enum — a filter comparing rights is deciding
        "IAccessibleRecordSetService",   // the accessible-record-set gate (external / contact plane)
        "RetrievePrincipalAccess",       // the impersonated Dataverse call
        "IDataverseRecordShareService",  // explicit grants + revokes
        "ICommunicationAccessFilter",    // the communication-scoped decision seam
        "IDataversePrivilegeChecker",    // DataverseAuthorizationFilter: entity-level Read privilege (schema routes)
        "WorkspaceLayoutService",        // owner-scoped layout lookups, deny on mismatch
        "CallerRecordAccessProbe",       // OBO RetrievePrincipalAccess, fails closed
    };

    /// <summary>
    /// Filters that legitimately reach a decision without an authorization service — claims-only, signature-only, or
    /// OWNER-COMPARISON. Each carries a reason. Being here satisfies Rule B; it does NOT earn Rule A credit — that is
    /// decided by CreditedForms alone.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ClaimOnlyFilters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TenantAuthorizationFilter"] =
                "Decides tenant boundary from the 'tid' claim against a tenant id in the request. Rule A does NOT credit it: it "
                + "passes through when the request names no tenant, and it never consults a record (NonDecidingAttachments).",
            ["CallerPrincipalAuthorizationFilter"] =
                "Resolves and validates the caller principal as a PRECONDITION for the per-record checks the handlers make. It "
                + "establishes who is asking; the record decision is a separate downstream step by design.",
            ["SpeAdminTenantScopeFilter"] =
                "Scopes SPE-admin operations to the caller's business units. Tenant scoping, not per-record authorization; it "
                + "passes through without a configId and fails open on lookup errors (NonDecidingAttachments, task 165).",
            // Agent and Communication reasons ported from the integration branch's guard (tasks 164 and 161), task-167 shape.
            ["AgentAuthorizationFilter"] =
                "M365 Copilot gateway (/api/agent/*). Asserts a resolvable oid AND tid on the inbound agent token and denies "
                + "without either — an IDENTITY PRECONDITION only, so Rule A does not credit it. The agent routes DO reach "
                + "document and playbook data (sweep S-18, S-19): the per-record decisions are other filters in the same chain "
                + "(POST /run-playbook: PlaybookAuthorizationFilter run mode — playbook-use, Read/Write on the document and the "
                + "record parameters; POST /message: AiAuthorizationFilter's chat-context evaluation; task 164), and the status "
                + "route's run-owner comparison is in its handler.",
            ["CommunicationAuthorizationFilter"] =
                "IDENTITY PRECONDITION only (authenticated + a resolvable oid) for the /api/communications routes; it decides "
                + "nothing about any record. The 2026-10-02 route sweep found the old reason ('per-record scoping is "
                + "ICommunicationAccessFilter's job') false for twelve routes (S-07, S-33..S-35, S-58..S-62). Since task 161 the "
                + "per-record decision is CommunicationRecordAuthorizationFilter, attached AFTER this filter in each such route's "
                + "chain and credited in its own right; routes carrying ONLY this filter must scope their reads in the handler.",
            ["WorkspaceAuthorizationFilter"] =
                "Resolves the caller's oid, denies 401 when absent, and stashes it in HttpContext.Items. A claim-resolution "
                + "precondition by construction; it earns no Rule A credit.",
            ["RegistrationAuthorizationFilter"] =
                "Demo-request approval surface. Decides from identity + role claims on the token — one of the four admin "
                + "mechanisms; the endpoints serve no document or matter content.",
            ["SpeAdminAuthorizationFilter"] =
                "SPE administrative surface. Decides from admin role claims, because its resources are containers and container "
                + "types, which have no RetrievePrincipalAccess answer — one of the four admin mechanisms.",
            ["JobOwnershipFilter"] =
                "OWNER-COMPARISON. Loads the job via IOfficeService and 403s unless its CreatedBy equals the caller's object id; "
                + "a blank creator is denied (task 067). IOfficeService is a general service, so it is not in DecisionServices.",
            ["SessionOwnershipFilter"] =
                "OWNER-COMPARISON (task 167). Loads the session through ChatSessionManager for (claim tid, route sessionId) and "
                + "answers 404 unless OwnerOid equals the caller (SessionOwnershipFilter.cs:173-213). ChatSessionManager is a "
                + "general service, so it is not in DecisionServices.",
            // PlaybookAuthorizationFilter's OWNER-COMPARISON entry was DELETED at the task-167 integration (2026-10-05): since
            // task 164 every mode asks the caller's own Dataverse Read through AuthorizationService (PlaybookAuthorizationFilter.cs
            // GetCallerRecordAccessAsync / AuthorizeAsync), so the filter passes Rule B on its own and is inspected like any other.
            ["KeylessProofAuthorizationFilter"] =
                "Provisioning acceptance surface (task 230b). Decides from the token's claims — the Provisioning.KeylessProof "
                + "application role on an app-only token (no scp; idtyp absent or 'app') — one of the admin mechanisms. The "
                + "route reads no record; it returns per-service status and timing only.",
            ["ReportingAuthorizationFilter"] =
                "Power BI embed surface. Decides from role claims checked against configuration — a module role, not a record "
                + "decision; it earns no Rule A credit (NonDecidingAttachments).",
        };

    /// <summary>Filters that consult no decision service and SHOULD — debt with an owner. EMPTY, and kept empty.</summary>
    private static readonly IReadOnlyDictionary<string, string> KnownDecorativeFilters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // =============================================================================================
    // THE WAIVER LIST
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. A waiver is per-ROUTE, in code, carries a written reason, and is enumerable here. A path pattern or a
    //      naming convention is NOT a waiver; it is a hole with better manners.
    //
    //   2. Choose the kind honestly:
    //        Pending   — the route SHOULD be decided and is not yet. It names its owning task (one of 159-166 or
    //                    "UNOWNED-NEW") and its Gap:
    //                      NoDecision           — the route has no credit at all;
    //                      InsufficientDecision — the route IS credited, but the credit does not cover the finding.
    //                                             It records ObservedMechanisms, the route's exact authorization
    //                                             fingerprint; a change to that fingerprint makes it stale.
    //        Permanent — the route is genuinely safe without a per-resource decision. It names a PermanentBasis from
    //                    the CLOSED set, and its reason cites the handler file:line that proves the basis:
    //                      AnonymousByDesign         — .AllowAnonymous() DECLARED on the route or group chain, with a
    //                                                  MANDATORY compensating control named at file:line (a rate
    //                                                  limit, an HMAC, OAuth state, an IsDevelopment-only mapping). An
    //                                                  optional control does not count, and neither does a harmless
    //                                                  response on its own (owner round 12 item 1 kept this strict rule
    //                                                  and rate-limited /healthz, /healthz/catalog and /ping instead).
    //                                                  A route anonymous by OMISSION (no RequireAuthorization, no
    //                                                  AllowAnonymous) cannot be waived at all: NoRouteIsAnonymousByOmission
    //                                                  fails it whatever its waiver says (task 167 r2), and the runtime
    //                                                  FallbackPolicy answers it 401 (owner round 14 item 2). The three
    //                                                  liveness probes name RequireRateLimiting("health-probe"), the
    //                                                  dedicated per-IP probe policy (owner round 14 item 1).
    //                      CallerScopedOnly          — reads/writes only rows keyed by a server-derived caller identity
    //                                                  (oid, systemuserid, contact id), no caller-chosen id of another
    //                                                  principal's record.
    //                      ReferenceData             — configuration, catalog or schema content, not customer records;
    //                                                  "takes no record id" means SELECTS NOTHING BY a record id (owner
    //                                                  round 12 item 2): a catalog key, a static model-deployment id, an
    //                                                  NDA clauseRef, a context-mapping cache key or a system saved-query
    //                                                  lookup is not a record id.
    //                      CreateWithNoPriorResource — the request names NO existing record, container, drive, owner,
    //                                                  parent or regarding id.
    //                      CallerSuppliedContentOnly — processes only the request's own bytes or text; reads no stored
    //                                                  record, index or file by any id.
    //                      OperatorGateInHandler     — (no route uses it since task 227f deleted the I4 diagnostic; kept as
    //                                                  the basis a future operator-only route would declare) the handler
    //                                                  denies every caller outside an operator allow-list or
    //                                                  app-only classification before any data access.
    //                      OwnerComparison           — (OWNER-COMPARISON, owner round 12 item 4) the handler loads the
    //                                                  record a caller-chosen id names and refuses unless its recorded
    //                                                  owner (or lock holder) IS the server-derived caller, before any
    //                                                  of it reaches the response or any write; AND an unknown id and a
    //                                                  not-yours id get the SAME answer (owner round 9 fix pattern). A
    //                                                  route whose owner check answers 404-vs-403 does not qualify: it
    //                                                  is Pending until the answers are made uniform.
    //                    No basis fits a genuinely safe route? STOP and propose a new one (task 167 trigger 2) — never
    //                    widen a basis.
    //
    //   3. A waiver whose route has since been credited, changed or deleted is STALE and fails. Delete it — that
    //      deletion is how the work list visibly shrinks. A Permanent waiver on a credited route is REDUNDANT and
    //      fails the same way.
    //
    //   4. Never add a Permanent waiver to make a build go green. Converting Permanent to Pending is allowed;
    //      Pending to Permanent is forbidden (the sweep ledger enforces it for sweep routes, review elsewhere).
    //      The one sanctioned exception is the diff that LANDS THE FIX which makes a basis true — the fix is the work
    //      in between, and the Permanent reason cites the fixed line (owner round 12 item 4: task 166 makes
    //      DELETE /api/memory/pins/{pinId} answer one uniform 404, then replaces its Pending waiver with
    //      OwnerComparison).
    //
    //   5. A sweep route (SweepFindings) carries exactly the Pending waiver its ledger entry names. A fix task
    //      resolves it ONLY by credit: make the route pass Rule A, delete the waiver, set ResolvedBy and ProofTest.
    //
    // STATE AT TASK 167 r1 (2026-10-03, after owner round 12): 90 sweep Pending waivers (159-166), 21 UNOWNED-NEW
    // findings for the main session to assign (listed in notes/task-167-every-route-authorization-guard.md), 14
    // Pending routes owned by a fix task's amendment or by owner round 12 (161: 4, 166: 10), and 89 Permanent
    // waivers each with a verified basis. The suite is green BECAUSE of the Pending entries — that is the honest
    // state, not a passing grade. Task 167 r2 changed no waiver: it added NoRouteIsAnonymousByOmission, which no
    // waiver can satisfy. Task 167 f1 (owner round 14) changed no waiver's kind, basis or owner: the three probe
    // waivers now name RequireRateLimiting("health-probe"), and the line citations moved with the source.
    //
    // STATE AT THE TASK-167 INTEGRATION (2026-10-05, batch 4, after 140, 142, 143, 147, 158-164 and 166 landed; 165 had
    // not): 14 sweep Pending waivers left (165: 9; 164: 3; 166: 2 — the ledger header above the sweep block says why each
    // is still owed), 0 UNOWNED-NEW (all 21 assigned or found resolved), 18 non-sweep Pending (161: 1; 163: 1; 164: 8;
    // 166: 8), and 90 Permanent (the task-167 89 plus DELETE /api/memory/pins/{pinId}, converted by owner round 12 item 4).
    // =============================================================================================

    private enum WaiverKind
    {
        Pending,
        Permanent,
    }

    private enum PermanentBasis
    {
        None,
        AnonymousByDesign,
        CallerScopedOnly,
        ReferenceData,
        CreateWithNoPriorResource,
        CallerSuppliedContentOnly,
        OperatorGateInHandler,

        /// <summary>OWNER-COMPARISON (owner round 12 item 4): a caller-chosen id is loaded and refused unless its
        /// owner is the caller, with one uniform answer for unknown and not-yours.</summary>
        OwnerComparison,
    }

    private enum Gap
    {
        None,
        NoDecision,
        InsufficientDecision,
    }

    private sealed record Waiver(string Route, WaiverKind Kind, string OwningTask, string Reason)
    {
        public PermanentBasis Basis { get; init; }

        public Gap Gap { get; init; }

        public string? ObservedMechanisms { get; init; }
    }

    private static Waiver Pending(string route, string owningTask, Gap gap, string reason, string? observed = null)
        => new(route, WaiverKind.Pending, owningTask, reason) { Gap = gap, ObservedMechanisms = observed };

    private static Waiver Permanent(string route, PermanentBasis basis, string owningTask, string reason)
        => new(route, WaiverKind.Permanent, owningTask, reason) { Basis = basis };

    /// <summary>The number of UNOWNED-NEW waivers. The task-167 note §3 lists the original 21; every one was assigned an
    /// owning task (or found resolved) at the task-167 integration — see the "ASSIGNED at the integration" block in
    /// <see cref="Waivers"/>. A NEW UNOWNED-NEW finding raises this and is listed in the note in the same diff.</summary>
    private const int ExpectedUnownedNewCount = 0;

    private static readonly IReadOnlyList<Waiver> Waivers = new[]
    {
        // ---------- task 072: WAIVER REMOVED 2026-08-26, route is gated ----------
        //
        // POST /api/documents/{documentId}/share-link now carries
        // .AddDocumentAuthorizationFilter("share"). Deleted rather than left behind per maintenance rule
        // 3 above — a Pending waiver whose route has become gated is STALE and fails
        // NoWaiverIsStaleAndEveryWaiverIsWellFormed (task 074 called it NoWaiverIsStale).
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
        //
        // 2026-10-16: the ROUTE IS GONE (unified-access-control-r2, #1350; #1752 link half). Graph refuses createLink in
        // SPE containers, so it answered 502 for every document; no client called it; a working link would have bypassed
        // Secure and No Access. ShareLinkOptions (Documents:ShareLinks) and the createLink facade members went with it.

        // ---------- task 073: THREE WAIVERS REMOVED 2026-08-27, the routes are GONE ----------
        //
        // PUT /api/containers/{containerId}/files/{*path}, POST /api/containers/{containerId}/upload and
        // PUT /api/upload-session/chunk were RETIRED by task 073, which deleted Api/UploadEndpoints.cs
        // outright rather than gating it. Deleted here rather than left behind per maintenance rule 3 — and
        // note that the stale rule of that day (NoWaiverIsStale) could NOT have caught these on its own: it
        // fired when a waived route became GATED, not when it was DELETED. That gap is now closed — any waiver
        // on an absent route fails NoWaiverIsStaleAndEveryWaiverIsWellFormed — which makes 073 the last task
        // that could leave dead waivers silently.
        //
        // What 073 actually closed, for the record: the routes carried RequireAuthorization("canwritefiles")
        // -> ResourceAccessRequirement -> ResourceAccessHandler — a real, fail-closed mechanism resolving
        // DOCUMENT rights from a CONTAINER id, because ExtractResourceId treats
        // containerId/driveId/documentId/id interchangeably. Wrong resource domain, not a missing
        // mechanism, which is precisely why no structural rule here could see it. Retirement removes the
        // defect and the shape together. Regression guard:
        // tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs.

        // ---------- task 083: TWO WAIVERS REMOVED 2026-09-07, the routes are GONE ----------
        //
        // PUT /api/drives/{driveId}/upload and DELETE /api/drives/{driveId}/items/{itemId} were RETIRED
        // by task 083, which deleted Api/DocumentsEndpoints.cs outright rather than gating it — the same
        // disposition, for the same reason, as 073 on the container-keyed twin above. These two carried
        // WaiverKind.Pending / "UNOWNED"; they were the last UNOWNED waivers in this list.
        //
        // DELETED, NOT CONVERTED TO PERMANENT. That distinction is the whole point of the Pending kind:
        // this file's own rule (maintenance rule 3) forbids promoting a Pending waiver to Permanent to
        // make a build green, because doing so converts "someone must fix this" into "this is fine" with
        // no fix in between — inverting the forcing function. The route being gone is the only clean way
        // a Pending waiver is allowed to leave.
        //
        // What 083 actually closed: both took an SPE drive id off the ROUTE and wrote (or DESTROYED)
        // app-only as the managed identity, so SPE applied no caller-side check, behind
        // RequireAuthorization("canwritefiles") -> ResourceAccessRequirement("upload_file") ->
        // ResourceAccessHandler, which resolves DOCUMENT rights from a DRIVE id. Wrong resource domain,
        // not a missing mechanism — invisible to every structural rule in this file, which is why they
        // needed a waiver rather than tripping a check. The now-orphaned canwritefiles + canreadfiles
        // policies were deleted with them, and "canwritefiles" was removed from ResourcePolicies below.
        //
        // ⚠️ The second waiver's own instruction was DISCHARGED, not dropped: it said the owning task
        // "must FIRST resolve whether the web resource is deployed." Deployment of
        // src/dataverse/webresources/spaarke_documents/ is genuinely not determinable from the repo
        // (manual portal deploy, per its README). That question turned out not to gate the decision:
        // that file's getAuthToken returns null and its apiCall sends only credentials:'include', while
        // the BFF's schemes are JwtBearer + ApiKey + Ciam with no cookie scheme — so a deployed copy
        // 401s on every call. Deployed or not, it cannot reach these routes. Recorded because the
        // waiver asked, and "could not determine" is a worse answer than "determined it does not matter".
        //
        // Regression guard: tests/integration/regression/DriveKeyedWriteRouteRetirementTests.cs.

        // ---------- PENDING — the OBO upload trio, re-pointed from 071 to 073/075/076 ----------
        //
        // Task 071 DELETED four routes from this group (children / PATCH / content / DELETE) — all had
        // zero callers and gated document-id-keyed equivalents already ship — so their waivers are gone
        // rather than updated. A waiver for a route that no longer exists is worse than noise: it reads
        // as unfinished work and would be carried forward forever. (When 071 ran, the stale rule did not
        // catch this case — it fired only when a waived route became GATED. It has since been extended:
        // NoWaiverIsStaleAndEveryWaiverIsWellFormed fails any waiver whose route no longer exists.)
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
        // ⚠️ CORRECTED 2026-08-28 when 076 landed. This block previously PREDICTED that all three
        // entries would be gone — "the first is CONVERTED to a record-keyed contract and gated". Two of
        // three came true (the chunked pair was deleted; see the 076 block below). The prediction about
        // the FIRST one did not, and the prediction is corrected here rather than left to read as an
        // unmet promise.
        //
        // 076 DID build the gated record-keyed replacement —
        //   PUT  /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}
        //   POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session
        // both carrying .AddRecordRouteAccessAuthorizationFilter(...).
        //
        // 🔴 THE PENDING WAIVER FOR "PUT /api/obo/containers/{id}/files/{*path}" WAS DELETED HERE
        // 2026-09-03 — because THE ROUTE WAS DELETED, which is the only way a Pending waiver is
        // allowed to leave this list. It was NOT converted to Permanent: it was a work item, and the
        // work item was completed.
        //
        // It survived this long because three client paths uploaded bytes before any owning record
        // existed (EmailComposer local attachments, the Analysis wizard's standalone document, and
        // DocumentUploadWizard "skip associate"). PUT /api/obo/me/files/{*path} gave all three a
        // callable upload that names no container, DocumentUploadWizard — the last client on the old
        // route — cut over, and the route went with it. The absent-route rule in
        // `NoWaiverIsStaleAndEveryWaiverIsWellFormed` is what would have caught this entry being left behind.

        // ---------- task 076: the record-LESS upload route. PERMANENT, and honestly so. ----------
        //
        // Added 2026-09-03 with PUT /api/obo/me/files/{*path}. This is Permanent under maintenance
        // rule 2's SECOND clause — "a create with no pre-existing resource" — not rule 4's forbidden
        // build-go-green move. The distinction matters, so state it plainly: there is no resource to
        // authorize because the record does not exist yet; that is the whole reason the route exists.
        // It is the same reasoning the Permanent waiver on POST /api/v1/documents already carries.
        //
        // What makes it safe is NOT a filter, and pretending otherwise would be the dishonest version:
        //   - The route has NO container parameter, so a caller cannot name where bytes go. That is
        //     the property finding #2 was about, and it holds here by construction.
        //   - The container is derived SERVER-side from the acting user's business unit
        //     (RecordContainerResolver.ResolveForActingUserAsync), so exposure is bounded to a
        //     container the caller is entitled to anyway.
        //   - A caller who cannot be resolved to a Dataverse principal gets a typed 403
        //     (acting_user_not_resolvable) rather than a shared-container fallback.
        //   - Secure content can never arrive here: secure records resolve through the RECORD-keyed
        //     route and fail closed. Acting-user BU is admissible ONLY where no record exists — for a
        //     secure record it is provably the WRONG container, since users sit in the Operations
        //     subtree while secure records are owned in `Secure Record`.
        //
        // If someone later adds a record id to this route "for convenience", this waiver is wrong and
        // the route needs the record-keyed filter instead. That is the only way it becomes stale.

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

        // ---------- task 078: ONE WAIVER REMOVED 2026-08-28, the route is now GATED ----------
        //
        // GET /api/v1/containers/{containerId}/documents — the sixth miss, and the one this rule found
        // itself on its first run — now carries .AddContainerDocumentAuthorizationFilter()
        // (DataverseDocumentsEndpoints.cs:605). The filter resolves container -> OWNING RECORD through
        // task 075's RecordContainerResolver.ResolveOwningRecordAsync (the one such mapping, reverse
        // direction) and requires the caller's Read on that record via
        // AuthorizationService.GetCallerRecordAccessAsync, evaluated OBO as the caller. A container that
        // resolves to no owning record is REFUSED (ADR-003), not listed.
        //
        // Deleted rather than converted to Permanent: the waiver stopped being TRUE, which is the forcing
        // function working. Converting it would have inverted the mechanism — see task 074's constraint.
        //
        // Note the earlier entry's own suggestion — that the control here is result trimming (Wave 3) —
        // was right about the SHARED-container case and wrong that it made a per-resource gate impossible.
        // The route names one resource, so it has an authorization subject; the shared case is precisely
        // what the filter refuses, because trimming does not exist yet.
        //
        // 078 perturbation-proved the gate is what keeps Rule A green here, not a waiver: removing
        // .AddContainerDocumentAuthorizationFilter() makes Rule A FAIL naming this route again.

        // ---------- task 167: the two task-074 PERMANENT waivers on /api/v1/documents were NOT TRUE as written ----------
        //
        // "POST /api/v1/documents — CREATE, there is no pre-existing resource": the body REQUIRES a ContainerId (an existing
        // container) and writes it with no caller-rights check, so the strict CreateWithNoPriorResource test fails. Now a
        // Pending UNOWNED-NEW waiver (below). "GET /api/v1/documents — COLLECTION READ, result trimming": the sweep showed it
        // is a caller-chosen-container read, the ungated twin of the route task 078 gated — now Pending, owned by 166
        // (S-66). Converting Permanent to Pending is the allowed direction.


        // ---------- task 120 (GitHub #1015): the Office surface, first time it is inside the guard ----------
        //
        // WHY THIS BLOCK EXISTS. Api/Office/* was never in GovernedFiles, so the whole surface was
        // classified "serves neither document nor Dataverse content" by omission — and FilterMarker could
        // not have recognised its gates even if it had been, because the Office filters are named without
        // the word "Authorization". Task 120 opened that blind spot on 2026-09-29 with NINE Pending waivers
        // and ONE Permanent: not nine new holes, one blind spot. (This header said TEN at first — a miscount
        // in the very file whose job is to prevent drift.)
        //
        // STATE AFTER THE 2026-09-30 MASTER MERGE — two projects closed the same holes independently, and
        // the attribution below says which fix SURVIVED, not merely which project found it:
        //   • /communications ×3 (#1020) — unified-access-control-r2 task 127. Permanent: query-is-the-gate.
        //   • /search/entities (#1021)   — spaarkeai-word-add-in-r1 task 062 (impersonated read). This
        //                                  branch's task 126 (OBO) fixed it too and was SUPERSEDED at merge.
        //   • POST /todo (#1022)         — word-add-in-r1 task 064, TodoSourceAccessFilter: no waiver THEN, it
        //                                  is a real endpoint filter (credited today in CreditedForms; task 074
        //                                  called that list ExplicitlyCreditedFilterTypeNames). (This branch's task
        //                                  128 gated only the regarding id; superseded.) ⚠️ Since owner round 12
        //                                  item 9 it carries a Pending InsufficientDecision waiver owned by 166:
        //                                  the filter gates the source read, not the Create privilege.
        //   • POST /quickcreate          — word-add-in-r1 QuickCreateSourceAccessFilter: NO waiver for the
        //                                  same reason. Its old Permanent "CREATE, nothing to authorize"
        //                                  waiver went stale the moment the source-record read was gated.
        //   • /search/matter-types       — NEW from master (task 038); reference data, Permanent.
        //   ⇒ FOUR Pending (#1023 ×2, #1024 ×2 — latent stubs; word-add-in-r1 task 058 deletes those routes
        //     and must delete these waivers with them) and FIVE Permanent.
        //
        // The outcome to prefer is the /todo and /quickcreate one: a real filter Rule A can SEE. A waiver —
        // even a Permanent one — is a note explaining why the mechanical check cannot see a control.
        // Maintenance rule 4 still binds: never convert "someone must fix this" into "this is fine" to make
        // a build green.

        // ✅ RESOLVED 2026-09-29 by task 127. Permanent rather than deleted: the SHAPE of the control
        // is what a future reader needs before "simplifying" it back. There is still no endpoint
        // filter, and correctly so — the record is not known until the query resolves it, so the
        // DELEGATED QUERY ITSELF is the authorization boundary. Denial returns the ordinary 404, never
        // a 403: answering 403 would confirm the record exists, trading an IDOR for an existence
        // oracle (the separation task 022 removed from bulk download).

        // ✅ RESOLVED 2026-09-29 by task 127 — and this one needed TWO fixes, not one. Trimming only
        // the communication lookup would have left the worse leak intact behind a route that looked
        // fixed: candidate DISPLAY NAMES were resolved app-only, so even an entitled caller received
        // the names of candidate records they had no right to see.

        // ✅ RESOLVED 2026-09-29 by task 127.

        // ✅ RESOLVED by spaarkeai-word-add-in-r1 task 062 — arrived with the 2026-09-30 master merge. This
        // branch's task 126 fixed the same hole with a different mechanism (OBO via IDataverseUserClient) and
        // was SUPERSEDED at merge: ADR-028 Amendment A5 sanctions app-only IMPERSONATED read for "what may
        // this workforce user see" sets, and this project's own task 036 was headed the same way.
        //
        // Kept Permanent, NOT deleted, because the shape of the control is what a future reader needs
        // before "simplifying" it: there is no endpoint filter and correctly so — there is no target record
        // to authorize before the query runs, so the QUERY IS the boundary. TotalCount/HasMore derive from
        // the trimmed set (a count over untrimmed matches is the same disclosure, restated).
        //
        // What was wrong, for the record: the search ran app-only with no user predicate, so any
        // authenticated Office caller could enumerate every matter, project, invoice, account and contact
        // in the tenant from a 2-character substring plus paging.

        // NEW from master (spaarkeai-word-add-in-r1 task 038). First measured by this census at the
        // 2026-09-30 merge — master never governed Api/Office/*, so it never had to pass Rule A there.

        // The four Pending waivers for the Office STUB routes (document search and recent items, #1023; share
        // links and share attach, #1024) were DELETED 2026-09-30 WITH THE ROUTES by spaarkeai-word-add-in-r1
        // task 058. All four served fabricated data behind the authentication filter only, and no client called
        // them, so the routes were removed rather than gated.

        // ⇒ TASK 167: the three /api/office/communications routes and GET /api/office/search/entities are now VERIFIED
        //   HandlerDecision declarations (IDataverseUserClient; IImpersonatedCommunicationQuery through two hops) and
        //   their Permanent waivers are deleted — a credit Rule A can SEE replaces a note explaining why it could not.
        //   /search/matter-types stays Permanent ReferenceData, and PUT /api/obo/me/files stays Permanent
        //   CreateWithNoPriorResource; both are re-stated below with their file:line.
        //
        // POST /api/office/quickcreate/{entityType} — waiver DELETED at the 2026-09-30 merge. It read
        // "CREATE. There is no pre-existing resource to authorize", which stopped being true when
        // spaarkeai-word-add-in-r1 added QuickCreateSourceAccessFilter: the route now reads a caller-named
        // SOURCE record to copy fields from, and gates that read. The filter is credited (CreditedForms today;
        // ExplicitlyCreditedFilterTypeNames then), so Rule A sees the gate directly. The old waiver's two caveats
        // were real and moved to the OfficeEndpoints GovernedFile entry rather than being lost.
        // (The stale rule of that day would NOT have caught this: it inspected PENDING waivers only, so a
        // Permanent waiver could outlive its premise silently. Since task 167 a Permanent waiver on a credited
        // route fails as REDUNDANT in NoWaiverIsStaleAndEveryWaiverIsWellFormed — this exact case now fails.)

        // =========================================================================================
        // TASK 167 (2026-10-03): THE SWEEP — 90 Pending waivers, one per ledger route, owned by its fix task.
        // Reasons quote the sweep's "what any signed-in caller can do" column.
        //
        // AT THE TASK-167 INTEGRATION (2026-10-05, batch 4) the sibling fix tasks 159-164 and 166 had landed. Each sweep
        // waiver whose route is now credited, deleted or (InsufficientDecision) fixed inside its credited filter was
        // DELETED here and its SweepFindings entry given ResolvedBy + ProofTest (the fix task's ledger input; the retired
        // routes per notes/task-167-every-route-authorization-guard.md §18.2). What is left below is owed:
        //   - 159, 160, 161, 162, 163: nothing left.
        //   - 164: S-24 and S-18 are FIXED in code (task 164 r1: AiAuthorizationFilter's chat-context evaluation), but the
        //     guard credits by attachment FORM and that form still passes through on every other route
        //     (NonDecidingAttachments), so a NoDecision entry cannot resolve by declaration; S-78 is fixed by an owner
        //     comparison in the handler, which is no seam the guard credits. All three stay Pending 164 until the guard
        //     can see them (a distinct attachment form for the chat-context mode, or an owner-comparison credit for a
        //     sweep entry — the main session's decision).
        //   - 165: not merged at this integration; all nine stay Pending 165.
        //   - 166: S-64 (session owner checked in the handler — an owner comparison) and S-65 (an OBO metadata read,
        //     GetFileMetadataAsUserAsync, not in CallerContextSeams) are FIXED in code but invisible to the guard for the
        //     same reasons; they stay Pending 166.
        // =========================================================================================

        // ---------- sweep fix task 164 ----------

        // ---------- round 65 item 2: five Pending waivers DELETED (2026-10-05) ----------
        // S-24, S-18, S-78 (task 164) and S-64, S-65 (task 166) were FIXED IN CODE in shapes the scanner cannot credit by form
        // (a filter mode for some request types of a shared attachment, two owner comparisons, an OBO metadata read). Each is
        // resolved by the ONE generic ProvenByTest credit: a real-app test asserting the route's refusal AND success.

        // ---------- sweep fix task 165: all nine waivers DELETED at its integration (batch-4, 2026-10-05) ----------
        // S-44, S-72 (bulk), S-45, S-73..S-75 (configs) and S-47, S-48, S-77 (record matching) are resolved by credit:
        // the /api/spe routes are an operator surface (AdminOnlyRoutes, the SPE admin mechanism) on which task 165's
        // SpeAdminTenantScopeFilter now confines every configId to the caller's units (uniform 404; 503 fail-closed), and the
        // record-matching group requires the SystemAdmin policy. Task 165 also re-keyed the config routes {id:guid} ->
        // {configId:guid} (its note section 1), so those three waivers' keys no longer exist. ResolvedBy + ProofTest below.

        // ---------- sweep fix task 166 ----------

        // =========================================================================================
        // TASK 167: EVERY OTHER ROUTE THE EXTENDED GUARD FLAGS — each decided by reading its handler.
        // Pending: a real gap (owner = the fix task whose amendment names it, else UNOWNED-NEW; the [severity] is
        // the proposed one). Permanent: a basis from the closed set, proven at the cited file:line.
        // =========================================================================================

        // ---------- P:AnonymousByDesign ----------
        // Each reason names EXACTLY the controls ExplicitlyAnonymousRoutes pins for the route, and the guard reads each of
        // them from code (EveryExplicitlyAnonymousRouteCarriesItsPinnedControl, main-session round 43 item 1).
        Permanent("GET /healthz", PermanentBasis.AnonymousByDesign, "167",
            "App Service liveness probe, anonymous by platform contract. Mandatory control: "
            + "RequireRateLimiting(\"health-probe\") at EndpointMappingExtensions.cs:72 (owner round 12 item 1; the "
            + "dedicated per-IP probe policy, owner round 14 item 1, RateLimitingModule.cs); the HealthCheckOptions "
            + "(:67-70) have no ResponseWriter, so the body is the aggregate status word only — no record, no id, no "
            + "side effect."),
        Permanent("GET /healthz/catalog", PermanentBasis.AnonymousByDesign, "167",
            "FR-P0-04 catalog-reconciliation probe. Mandatory control: RequireRateLimiting(\"health-probe\") at "
            + "EndpointMappingExtensions.cs:85 (owner rounds 12 item 1 + 14 item 1); the HealthCheckOptions (:80-83) "
            + "have no ResponseWriter, so the body is the aggregate status word only; takes no id and writes nothing; "
            + "each check is memoized for 30 s and time-bounded (MemoizedHealthCheck.cs, round 34 item 7, round 43 item 2)."),
        Permanent("GET /healthz/dataverse", PermanentBasis.AnonymousByDesign, "167",
            "Dataverse connectivity probe. Mandatory control: RequireRateLimiting(\"anonymous\") at "
            + "EndpointMappingExtensions.cs:92; the handler (TestDataverseConnectionAsync, :502-519) takes no id "
            + "and returns a fixed status message, never ex.Message (task 023)."),
        Permanent("GET /healthz/dataverse/crud", PermanentBasis.AnonymousByDesign, "167",
            "Dataverse CRUD probe. Mandatory control: RequireRateLimiting(\"anonymous\") at "
            + "EndpointMappingExtensions.cs:95; the handler (TestDataverseCrudOperationsAsync, :521-538) takes no "
            + "id and returns a fixed healthy/failed message with no record content."),
        Permanent("GET /ping", PermanentBasis.AnonymousByDesign, "167",
            "Warm-up probe. Mandatory control: RequireRateLimiting(\"health-probe\") at "
            + "EndpointMappingExtensions.cs:133 (owner rounds 12 item 1 + 14 item 1); the handler (:131) answers the constant "
            + "text \"pong\" — no input is read, nothing is looked up, nothing is written."),
        Permanent("GET /status", PermanentBasis.AnonymousByDesign, "167",
            "Service metadata probe. Mandatory control: RequireRateLimiting(\"anonymous\") at "
            + "EndpointMappingExtensions.cs:147; the body (:137-145) is the constant service name, version and "
            + "server time — no id, no record."),
        Permanent("GET /api/config/client", PermanentBasis.AnonymousByDesign, "167",
            "MSAL bootstrap config for a page with no token yet. Mandatory control: "
            + "RequireRateLimiting(\"anonymous\") at ConfigEndpoints.cs:62; the handler (:105-144) returns only the "
            + "public client id, authority, scope and BFF URL from configuration — no id is taken."),
        Permanent("GET /api/config", PermanentBasis.AnonymousByDesign, "167",
            "FR-36 public runtime config bundle. Mandatory control: RequireRateLimiting(\"anonymous\") at "
            + "ConfigEndpoints.cs:85; the handler (:157-193) serialises PublicConfigOptions only (bffUrl, "
            + "msalClientId, tenantId, featureFlags)."),
        Permanent("GET /api/office/health", PermanentBasis.AnonymousByDesign, "167",
            "Office add-in connectivity check. Mandatory control: RequireRateLimiting(\"anonymous\") at "
            + "OfficeEndpoints.cs:80; the handler (GetHealthAsync, :88-105) returns a constant status object and "
            + "reads nothing."),
        Permanent("POST /api/office/save-debug", PermanentBasis.AnonymousByDesign, "167",
            "Development-only diagnostic. Mandatory control: the route is mapped ONLY inside `if "
            + "(env.IsDevelopment())` at OfficeEndpoints.cs:117, so no deployed environment registers it; it logs "
            + "the body length and echoes nothing."),
        Permanent("POST /api/registration/demo-request", PermanentBasis.AnonymousByDesign, "167",
            "Public demo-request form: the submitter has no account by definition. Mandatory control: "
            + "RequireRateLimiting(\"anonymous\") at RegistrationEndpoints.cs:24; the handler (:69+) validates "
            + "input and creates a NEW request row — it names no existing record. (Its 409 on a duplicate e-mail "
            + "is noted in the task note.)"),
        Permanent("POST /api/onboarding/consent-callback", PermanentBasis.AnonymousByDesign, "167",
            "External admin-consent redirect target (no token exists yet). Mandatory control: "
            + "RequireRateLimiting(\"anonymous\") at ConsentCallbackEndpoint.cs:72. Authenticity: HMAC-SHA256 over "
            + "the raw body in the handler, ConsentCallbackEndpoint.cs:119-150 — a missing header, a missing key or a "
            + "mismatch is refused before any work."),
        Permanent("POST /api/compose/webhooks/spe-doc-changed", PermanentBasis.AnonymousByDesign, "167",
            "Graph change-notification webhook (Graph sends no OAuth token). Mandatory control: "
            + "RequireWebhookSignature at ComposeSyncEndpoints.cs:44, which fails closed with no key or header "
            + "(WebhookSignatureFilter.cs:93-117), and RequireRateLimiting(\"webhook-graph\") at :48 (defense in depth); "
            + "the validationToken handshake (ComposeSyncEndpoints.cs:104-110) only echoes the token."),
        Permanent("POST /api/communications/incoming-webhook", PermanentBasis.AnonymousByDesign, "167",
            "Graph mail-notification webhook. Mandatory control: RequireWebhookSignature at "
            + "CommunicationEndpoints.cs:513, which fails closed with no key or header "
            + "(WebhookSignatureFilter.cs:93-117), and RequireRateLimiting(\"webhook-graph\") at :517 (defense in depth); "
            + "the validationToken handshake (CommunicationEndpoints.cs:1291-1301) only echoes the token."),

        // ---------- the task-167 UNOWNED-NEW findings — ASSIGNED at the integration (2026-10-05) ----------
        // The 21 of notes/task-167-every-route-authorization-guard.md §3, each given to the task whose surface it is
        // (communications 161, search/RAG 163, AI/chat/playbook 164, compose/documents/remaining routes 166). Five were
        // already RESOLVED by a sibling and their waivers deleted: proposals /apply and /undo and the task /undo (161,
        // CommunicationRecordAuthorizationFilter), POST /api/compose/active-document (166 r1, HandlerDecision) and
        // refresh-profile (166 r1 re-keyed it to {documentId:guid} behind DocumentAuthorizationFilter("write")). None is
        // UNOWNED-NEW any more (ExpectedUnownedNewCount = 0).
        Pending("POST /api/communications/acs/eventgrid", "161", Gap.NoDecision,
            "[medium] ANONYMOUS, and no MANDATORY authenticity control. The ?sig= shared secret is enforced "
            + "only when configured (AcsEventGridIngressService.cs:68-69); the mandatory topic allow-list "
            + "(:121-141) checks a topic string the caller writes in the body. Anyone who knows the ACS topic id "
            + "can enqueue forged chat events. Its only control until then: RequireRateLimiting(\"webhook-graph\") at "
            + "AcsEventGridEndpoints.cs:32 (600/min per IP)."),
        Pending("GET /api/ai/chat/context-mappings/analysis/{analysisId}", "164", Gap.NoDecision,
            "[medium] Refuted by ACCIDENT, not by a decision: the app-only Retrieve asks for attributes that do "
            + "not exist (AnalysisChatContextResolver.cs:267-277). AiAuthorizationFilter does not decide here. "
            + "Fix the attributes and it reads any analysis's chat context."),
        Pending("GET /api/ai/chat/sessions/by-analysis/{analysisId:guid}", "164", Gap.NoDecision,
            "[medium] The handler returns the most recent session bound to ANY analysis with no owner check "
            + "(ChatEndpoints.cs:2278-2303). Safe today only because AiAuthorizationFilter authorizes the "
            + "analysis id as a DOCUMENT id and so always denies."),
        Pending("DELETE /api/ai/chat/context-mappings/cache", "164", Gap.NoDecision,
            "[low] Any signed-in user evicts EVERY cached context mapping, not tenant-scoped "
            + "(EvictAllCachedMappingsAsync, ChatEndpoints.cs:1946). An operator action with no admin policy."),
        Pending("POST /api/ai/chat/export/word", "164", Gap.NoDecision,
            "[low] Loads the body SessionId in the tenant with no owner check (ChatWordExportEndpoints.cs:117): "
            + "a session-existence oracle. The container comes from configuration (:249-267) and the upload is "
            + "OBO."),
        Pending("POST /api/ai/feedback", "164", Gap.NoDecision,
            "[low] Writes feedback against ANY body SessionId with no owner check "
            + "(FeedbackEndpoints.cs:96-110); it feeds the per-playbook aggregates other users read."),
        Pending("GET /api/ai/feedback/playbook/{id}", "164", Gap.NoDecision,
            "[low] Tenant-wide feedback aggregate for any playbook id, to any signed-in user "
            + "(FeedbackEndpoints.cs:142). Usage analytics with no admin policy."),
        Pending("GET /api/ai/feedback/capability/{id}", "164", Gap.NoDecision,
            "[low] Tenant-wide feedback aggregate for any capability id, to any signed-in user "
            + "(FeedbackEndpoints.cs:55). Usage analytics with no admin policy."),
        Pending("GET /api/ai/knowledge/indexes/health", "163", Gap.NoDecision,
            "[low] Index names and document counts of the tenant knowledge and discovery indexes "
            + "(KnowledgeBaseEndpoints.cs:131-140): the same operator-surface disclosure as sweep S-77, behind "
            + "sign-in only."),
        Pending("POST /api/ai/playbooks", "164", Gap.NoDecision,
            "[low] Creates a playbook whose body names EXISTING action/skill/knowledge/tool rows "
            + "(SavePlaybookRequest, PlaybookDto.cs:8; PlaybookEndpoints.cs:213) with no check that the caller "
            + "may use them."),
        Pending("GET /api/compose/documents/{documentSpeId}", "166", Gap.NoDecision,
            "[low] The SPE read is OBO, but the documentRecordId and matterId query values are not tied to it "
            + "and drive app-only reads and a profile dispatch (ComposeDocumentEndpoints.cs:61-137). Task 166 r1 bound "
            + "documentRecordId to the OBO-read item's row; the client-supplied matterId still becomes the session's "
            + "host context unauthorized (ComposeService.cs:1059, :1449), so the route stays Pending 166."),
        Pending("POST /api/compose/documents/{documentSpeId}/apply-template", "166", Gap.NoDecision,
            "[low] Resolves a caller-named template with an APP token (ComposeTemplateEndpoints.cs:95-106). "
            + "Safe only if every template is org-shared."),
        Pending("POST /api/v1/documents", "166", Gap.NoDecision,
            "[medium] The body's required ContainerId (Models.cs CreateDocumentRequest) is written with no "
            + "caller-rights check; only the owner team is server-derived "
            + "(DataverseDocumentsEndpoints.cs:571-597). The old Permanent 'CREATE' waiver was not true as "
            + "written."),
        Pending("GET /api/resilience/circuits", "166", Gap.NoDecision,
            "[low] Operator diagnostics: every downstream circuit-breaker state to any signed-in user "
            + "(ResilienceEndpoints.cs:48-59). No admin policy."),
        Pending("GET /api/resilience/circuits/{serviceName}", "166", Gap.NoDecision,
            "[low] Operator diagnostics: one downstream circuit-breaker state to any signed-in user "
            + "(ResilienceEndpoints.cs:30). No admin policy."),
        Pending("GET /api/resilience/health", "166", Gap.NoDecision,
            "[low] Operator diagnostics: per-service resilience health to any signed-in user "
            + "(ResilienceEndpoints.cs:72-84). No admin policy."),

        // ---------- N:166 ----------
        Pending("POST /api/compose/documents/{documentSpeId}/save", "166", Gap.NoDecision,
            "[medium] The SPE write is OBO, but the body TenantId and SessionId flow into the first-save rebind "
            + "with no owner check, and DocumentRecordId is read app-only (ComposeSaveEndpoints.cs:26, :81-123). "
            + "Task 166 amendment (d) LANDED the tenant-from-claim and owned-session-only rebind "
            + "(ComposeSaveEndpoints.cs:322, ResolveOwnedSessionAsync) — an owner comparison the guard cannot credit."),
        Pending("POST /api/compose/documents/create-on-save", "166", Gap.NoDecision,
            "[medium] Same ExecuteSaveAsync path as /save (ComposeSaveEndpoints.cs:159-240): body TenantId + "
            + "SessionId rebind with no owner check, and a body SourceDocumentRecordId. Task 166 amendment (d), "
            + "'any other Compose session route with the same flaw' — landed as for /save; an owner comparison the "
            + "guard cannot credit."),

        // Owner round 12 items 9 (revoke, todo, close-project) and 4 (DELETE pin) — RESOLVED by task 166 and recorded at the
        // task-167 integration: revoke and close-project derive the container server-side, POST /todo's filter requires
        // prvCreatesprk_Todo, so their waivers are deleted (close-project is sweep S-39, resolved in the ledger); the DELETE
        // pin's 404-vs-403 split is gone, so its Pending waiver became the Permanent OwnerComparison below in the diff that
        // landed the fix (maintenance rule 4's one sanctioned exception, named by owner round 12 item 4).

        // ---------- P:ReferenceData ----------
        Permanent("GET /api/ai/capabilities", PermanentBasis.ReferenceData, "167",
            "Catalog of text-projectable Binding rows (CapabilityDiscoveryEndpoints.cs:105-113); the optional "
            + "`surface` query value filters the catalog and selects no record."),
        Permanent("GET /api/ai/chat/context-mappings", PermanentBasis.ReferenceData, "167",
            "Chat context-mapping configuration for an entity TYPE (ChatContextMappingService.ResolveAsync, "
            + "ChatEndpoints.cs:1914). `entityType` is a table name, not a record id."),
        Permanent("GET /api/ai/tools/handlers", PermanentBasis.ReferenceData, "167",
            "In-process catalog of registered IAiToolHandler classes (HandlerEndpoints.cs:82-87): class, tool "
            + "name and description. No Dataverse, no id."),
        Permanent("GET /api/ai/handlers", PermanentBasis.ReferenceData, "167",
            "Tool-handler registry catalog (IToolHandlerRegistry.GetAllHandlerInfo, HandlerEndpoints.cs:142), "
            + "response-cached. Code metadata, no customer content, no id."),
        Permanent("GET /api/ai/handlers/{handlerId}", PermanentBasis.ReferenceData, "167",
            "One entry of the in-process tool-handler registry (HandlerEndpoints.cs:183). {handlerId} is a "
            + "catalog key in the registry, not a record id."),
        Permanent("GET /api/ai/model-deployments", PermanentBasis.ReferenceData, "167",
            "Static in-memory model-deployment catalog (StubModelDeployments, ModelEndpoints.cs:12, listed at "
            + ":122). No Dataverse read, no id."),
        Permanent("GET /api/ai/model-deployments/{id:guid}", PermanentBasis.ReferenceData, "167",
            "One entry of the static StubModelDeployments array (ModelEndpoints.cs:196). The {id} is a key into "
            + "a compiled-in catalog, not a customer record id."),
        Permanent("GET /api/ai/nda-standard/clauses/{clauseRef}", PermanentBasis.ReferenceData, "167",
            "The fixed NDA-standard clause text B1..B16 from NdaStandardClauseProvider "
            + "(NdaStandardEndpoints.cs:58). {clauseRef} is a catalog key, not a record id."),
        Permanent("GET /api/ai/nda-standard/clauses", PermanentBasis.ReferenceData, "167",
            "All NDA-standard clauses from NdaStandardClauseProvider.AllClauses (NdaStandardEndpoints.cs:66). "
            + "Compiled reference text; no id."),
        Permanent("GET /api/ai/scopes/skills", PermanentBasis.ReferenceData, "167",
            "Org-wide skill catalog (IScopeResolverService.ListSkillsAsync, ScopeEndpoints.cs:114). "
            + "Configuration rows, paged; takes no record id."),
        Permanent("GET /api/ai/scopes/knowledge", PermanentBasis.ReferenceData, "167",
            "Org-wide knowledge-source catalog (ListKnowledgeAsync, ScopeEndpoints.cs:154). Lists configuration "
            + "rows; reads no indexed content and takes no id."),
        Permanent("GET /api/ai/scopes/tools", PermanentBasis.ReferenceData, "167",
            "Org-wide tool catalog (ListToolsAsync, ScopeEndpoints.cs:194). Configuration rows; takes no id."),
        Permanent("GET /api/ai/scopes/actions", PermanentBasis.ReferenceData, "167",
            "Org-wide action catalog (ListActionsAsync, ScopeEndpoints.cs:234). Configuration rows; takes no "
            + "id."),
        Permanent("GET /api/ai/scopes/personas", PermanentBasis.ReferenceData, "167",
            "Org-wide persona catalog (ListPersonasAsync, ScopeEndpoints.cs:280). Configuration rows; takes no "
            + "id."),
        Permanent("GET /api/ai/chat/context-mappings/standalone", PermanentBasis.ReferenceData, "167",
            "Built from an in-memory field catalog (StandaloneChatContextProvider.BuildFromCatalog, "
            + "StandaloneChatContextProvider.cs:226). The entityId query value is only echoed into the cache key; "
            + "NOTHING is read by it (owner round 12 item 2: selects nothing by a record id)."),
        Permanent("GET /api/v1/field-mappings/profiles", PermanentBasis.ReferenceData, "167",
            "Field-mapping profile configuration (QueryFieldMappingProfilesAsync, "
            + "FieldMappingEndpoints.cs:153). Configuration rows; takes no record id."),
        Permanent("GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}", PermanentBasis.ReferenceData, "167",
            "Field-mapping profile + rules for an entity-TYPE pair (FieldMappingEndpoints.cs:284). The route "
            + "values are table names, not record ids."),
        Permanent("GET /api/navmap/{entityLogicalName}/entityset", PermanentBasis.ReferenceData, "167",
            "Dataverse METADATA: an entity's EntitySetName (NavMapEndpoints.cs:116). Schema content; the route "
            + "value is a table name."),
        Permanent("GET /api/navmap/{childEntity}/{relationship}/lookup", PermanentBasis.ReferenceData, "167",
            "Dataverse METADATA: lookup navigation names for a relationship (NavMapEndpoints.cs:197). Schema "
            + "content; table and relationship names, no record id."),
        Permanent("GET /api/navmap/{parentEntity}/{relationship}/collection", PermanentBasis.ReferenceData, "167",
            "Dataverse METADATA: collection navigation property name (NavMapEndpoints.cs:317). Schema content; "
            + "no record id."),
        Permanent("GET /api/v1/external/api/dataverse/metadata/{entityLogicalName}", PermanentBasis.ReferenceData, "167",
            "Schema of a REGISTERED module entity, app-only passthrough (ExternalModuleDataEndpoints.cs:464); "
            + "no record data. (The sweep notes registered-entity schema reaches every collaboration caller — "
            + "recorded in the note.)"),
        Permanent("GET /api/v1/external/api/dataverse/savedquery/{savedQueryId:guid}", PermanentBasis.ReferenceData, "167",
            "A system view definition, refused when no registered module uses that view id (ExternalModuleDataEndpoints.cs:515) "
            + "or the loaded view targets another entity (:533). View FetchXML/LayoutXML, no record data; the saved-query "
            + "id selects a view, not a record (owner round 12 item 2)."),
        Permanent("GET /api/v1/external/api/dataverse/savedqueries/{entityLogicalName}", PermanentBasis.ReferenceData, "167",
            "System view list for a REGISTERED module entity only, fail-closed otherwise "
            + "(ExternalModuleDataEndpoints.cs:587, :592). View definitions, no record data."),
        // Master merge 2026-10-05 (spaarkeai-word-add-in-r1 task 100): /search/matter-types was GENERALIZED into ONE
        // parameterized route (CLAUDE.md §11); {list} = matter-types is the same URL. Ported from master's old-format waiver.
        Permanent("GET /api/office/search/{list}", PermanentBasis.ReferenceData, "167",
            "REFERENCE-DATA READ: the active rows of one create-form reference list — matter types, practice areas, project "
            + "types (OfficeEndpoints.cs:1067, :1280). {list} is a list NAME looked up in the CLOSED "
            + "OfficeSearchService.ReferenceLists table (OfficeSearchService.cs:473, :484; anything else is 404), not a "
            + "record id. Small, organization-owned lookup lists, not customer record content. If it ever takes a record "
            + "id, lets the caller name a table, or returns customer rows, re-classify."),
        Permanent("GET /api/office/quickcreate/defaults", PermanentBasis.CallerScopedOnly, "167",
            "CALLER'S OWN IDENTITY (master, spaarkeai-word-add-in-r1 task 100): returns the caller's own linked contact as "
            + "the Assigned To prefill, resolved from the caller's own systemuserid (OfficeEndpoints.cs:1391, :1433), never "
            + "a client-supplied value. Takes no id. The contact the pane then POSTS is gated by QuickCreateSourceAccessFilter "
            + "(Read on assignedToContactId). If it ever takes an id or returns another person's data, re-classify."),
        Permanent("GET /api/workspace/sections", PermanentBasis.ReferenceData, "167",
            "Static section catalog AvailableSections (WorkspaceLayoutEndpoints.cs:541-544). Compiled "
            + "configuration, no id."),
        Permanent("GET /api/workspace/templates", PermanentBasis.ReferenceData, "167",
            "Static template catalog AvailableTemplates (WorkspaceLayoutEndpoints.cs:550-553). Compiled "
            + "configuration, no id."),

        // ---------- P:CallerScopedOnly ----------
        Permanent("GET /api/ai/chat/event-rules/opt-out", PermanentBasis.CallerScopedOnly, "167",
            "Reads the caller's own opt-out flag keyed by the tid + oid claims "
            + "(ChatDocumentEndpoints.cs:1557-1565). Takes no id."),
        Permanent("PUT /api/ai/chat/event-rules/opt-out", PermanentBasis.CallerScopedOnly, "167",
            "Writes the caller's own opt-out flag keyed by the tid + oid claims "
            + "(ChatDocumentEndpoints.cs:1581-1589). Takes no id."),
        Permanent("GET /api/ai/chat/sessions", PermanentBasis.CallerScopedOnly, "167",
            "Lists only sessions whose owner is the caller: ListRecentSessionsAsync(tenantId, ownerOid) with "
            + "ownerOid from the token (ChatEndpoints.cs:2122-2135, issue #863). Takes no id."),
        Permanent("POST /api/ai/daily-briefing/render", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own briefing: the systemuserid is server-derived (DailyBriefingEndpoints.cs:115, "
            + ":131) and the collector reads every row AS the caller via IImpersonatedCommunicationQuery "
            + "(DailyBriefingCollector.cs:718). Takes no id."),
        Permanent("POST /api/ai/daily-briefing/email", PermanentBasis.CallerScopedOnly, "167",
            "Sends the caller's own briefing (server-derived systemuserid, DailyBriefingEndpoints.cs:191; rows "
            + "read as the caller, DailyBriefingCollector.cs:718). A colleague recipient must be an active "
            + "internal user (:220); no record id is taken."),
        Permanent("GET /api/ai/playbooks/runs/{runId:guid}", PermanentBasis.CallerScopedOnly, "167",
            "The run store is per REQUEST: PlaybookOrchestrationService is Scoped "
            + "(AnalysisServicesModule.cs:1302) and _activeRuns is an instance field "
            + "(PlaybookOrchestrationService.cs:61), so no other caller's run is reachable. (Functional defect "
            + "noted.)"),
        Permanent("GET /api/ai/playbooks/runs/{runId:guid}/stream", PermanentBasis.CallerScopedOnly, "167",
            "Same per-request run store as GET .../runs/{runId}: Scoped registration "
            + "(AnalysisServicesModule.cs:1302) + instance field _activeRuns "
            + "(PlaybookOrchestrationService.cs:61). Another caller's run cannot be reached."),
        Permanent("POST /api/ai/playbooks/runs/{runId:guid}/cancel", PermanentBasis.CallerScopedOnly, "167",
            "Cancels only a run in the current request's scoped store (AnalysisServicesModule.cs:1302; "
            + "PlaybookOrchestrationService.cs:61). Another caller's run cannot be reached, so cannot be "
            + "cancelled."),
        Permanent("GET /api/ai/playbooks/runs/{runId:guid}/detail", PermanentBasis.CallerScopedOnly, "167",
            "Same per-request run store (AnalysisServicesModule.cs:1302; PlaybookOrchestrationService.cs:61). "
            + "Another caller's run is unreachable by construction."),
        Permanent("POST /api/communications/threads/direct", PermanentBasis.CallerScopedOnly, "167",
            "Finds the caller's own Direct thread with one colleague, or creates one OWNED by the caller and "
            + "shares Read to that colleague (DirectThreadAccessService.cs:62-100). The caller is always a party; "
            + "no other principal's record is read."),
        Permanent("GET /api/v1/external/me", PermanentBasis.CallerScopedOnly, "167",
            "The resolved caller principal's own access context: ExternalUserContextEndpoint.HandleAsync (the mapped handler) returns "
            + "Handle's answer (ExternalUserContextEndpoint.cs:45, :74 — caller.ReadableProjects) plus the caller's own firms "
            + "(ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync: the caller contact's memberships only, id and name). "
            + "Takes no id."),
        Permanent("GET /api/v1/external/me/entitlements", PermanentBasis.CallerScopedOnly, "167",
            "The caller principal's own module entitlements (MeEntitlementsEndpoint.cs:32). Takes no id."),
        Permanent("GET /api/v1/external/notifications", PermanentBasis.CallerScopedOnly, "r3-037",
            "The caller principal's own feed: grants naming its own contact, each kept only with Read on its root "
            + "(CallerPrincipal.RightsOn, ExternalNotificationFeedEndpoints.cs:277), and messages read only for its accessible "
            + "set (:288) and its own service requests (the module's ScopeRows, :313), under the external read rules (:328). "
            + "Takes no id."),
        Permanent("POST /api/v1/external/registration", PermanentBasis.CallerScopedOnly, "r3-047",
            "Registers ONLY the caller: a WorkforceHome identity (ExternalRegistrationEndpoints.cs:114), keyed by its own "
            + "token tid/oid and email claim (:172), through ContactIdentityBinder.RegisterHomeContactAsync (:184). The "
            + "closed body carries names only; it takes no record, owner or contact id."),
        Permanent("POST /api/v1/external/workforce/link", PermanentBasis.CallerScopedOnly, "r3-048",
            "Links ONLY the caller's own two identities: a WorkforceHome bearer (ExternalWorkforceLinkEndpoints.cs:107), keyed "
            + "by its own token tid/oid (:121), and the same person's Spaarke-tenant guest token from the closed body, "
            + "validated with the default scheme's parameters (:133, SpaarkeGuestTokenValidator). The write targets that "
            + "guest token's own enabled systemuser, once, never re-pointed (ContactIdentityBinder.LinkHomeSystemUserAsync, "
            + ":147). It takes no record, systemuser or contact id."),
        Permanent("POST /api/v1/external/notifications/seen", PermanentBasis.CallerScopedOnly, "r3-037",
            "Writes the caller principal's own last-seen marker, keyed by its own systemuser or contact id "
            + "(ExternalNotificationFeedEndpoints.cs:192, :218). Takes no id and no body."),
        Permanent("GET /api/users/me/memberships/{entityType}", PermanentBasis.CallerScopedOnly, "167",
            "Memberships of the caller's own systemuserid, resolved from the oid claim "
            + "(MembershipEndpoints.cs:192-209). {entityType} is a table name; no caller-chosen principal or "
            + "record."),
        Permanent("GET /api/memory/user", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own memory items: subject from ResolveCallerUserSubjectAsync(User) "
            + "(MemoryGovernanceEndpoints.cs:150, :156). Takes no id."),
        Permanent("POST /api/memory/user/seed", PermanentBasis.CallerScopedOnly, "167",
            "Seeds the caller's own memory subject (ResolveCallerUserSubjectAsync, "
            + "MemoryGovernanceEndpoints.cs:204). Takes no other principal's id."),
        Permanent("DELETE /api/memory/user/{itemId}", PermanentBasis.CallerScopedOnly, "167",
            "Deletes an item under the caller's own subject key (DeleteItemAsync(subjectKey, itemId), "
            + "MemoryGovernanceEndpoints.cs:270, :279); an item of another subject is not addressable."),
        Permanent("DELETE /api/memory/user", PermanentBasis.CallerScopedOnly, "167",
            "Erases the caller's own memory subject (MemoryGovernanceEndpoints.cs:304, :316). Takes no id."),
        Permanent("GET /api/memory/pins", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own pins: GetByUserAsync(tenantId, userId) from the token "
            + "(PinnedMemoryEndpoints.cs:210, :217)."),
        Permanent("POST /api/notifications/negotiate", PermanentBasis.CallerScopedOnly, "167",
            "Issues a SignalR connection scoped to the caller's own oid (NotificationsEndpoints.cs:81, :94). "
            + "Takes no id."),
        Permanent("GET /api/notifications/pending", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own pending outbox rows, keyed by the server-derived systemuserid "
            + "(NotificationsEndpoints.cs:158, :169)."),
        Permanent("POST /api/notifications/{outboxRowId:guid}/dismiss", PermanentBasis.CallerScopedOnly, "167",
            "Dismisses only a row in the caller's own pending set: 404 unless the id is in "
            + "GetPendingAsync(systemUserId) (NotificationsEndpoints.cs:218-224)."),
        Permanent("GET /api/me", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own Graph profile, read on behalf of the caller (SpeFileStore.GetUserInfoAsync(ctx), "
            + "UserEndpoints.cs:84). Takes no id."),
        Permanent("GET /api/reporting/status", PermanentBasis.CallerScopedOnly, "167",
            "Constant module status plus the caller's OWN privilege level from their role claims "
            + "(ReportingEndpoints.cs:141-148). Reads no report, takes no id."),
        Permanent("GET /api/v1/ontology/admin/capabilities", PermanentBasis.CallerScopedOnly, "ontology-104",
            "The caller's OWN privilege set: WhoAmI() as the caller (OntologyAdminCapabilityProbe.cs:103), then "
            + "RetrieveUserPrivilegeByPrivilegeName on the systemuserid WhoAmI returned, also as the caller (:118, :137-138). Takes no id; "
            + "returns one boolean. An unresolved caller is the single 403 (OntologyAdminEndpoints.cs:71-80). Display only (D-22): every admin write "
            + "authorizes itself as the caller."),
        Permanent("GET /api/workspace/portfolio", PermanentBasis.CallerScopedOnly, "167",
            "Portfolio summary for the caller's own oid (WorkspaceEndpoints.cs:121, :140). Takes no id."),
        Permanent("GET /api/workspace/health", PermanentBasis.CallerScopedOnly, "167",
            "Health metrics for the caller's own oid (WorkspaceEndpoints.cs:189, :203). Takes no id."),
        Permanent("GET /api/workspace/briefing", PermanentBasis.CallerScopedOnly, "167",
            "Briefing for the caller's own oid (WorkspaceEndpoints.cs:242, :261). Takes no id."),

        // The five playbook LISTS — CallerScopedOnly by owner round 12 item 6 (task 167 had them ReferenceData). Each
        // takes no id and lists the caller's own definitions and/or the shared ones. The user-list filter compares
        // _ownerid_value (a systemuserid) with the Entra OID (PlaybookService.cs:318), so the caller's own list is
        // likely always empty — fail closed, a functional defect whose fix owner round 12 item 6 assigns to task 164
        // (with PlaybookAuthorizationFilter.cs:125 if it has the same mismatch).
        Permanent("GET /api/ai/playbooks", PermanentBasis.CallerScopedOnly, "167",
            "The caller's own playbook definitions: ListUserPlaybooksAsync filters _ownerid_value by the "
            + "server-derived caller id (PlaybookEndpoints.cs:495; PlaybookService.cs:318). Takes no id. Owner "
            + "round 12 item 6; the oid-vs-systemuserid filter fix is task 164's."),
        Permanent("GET /api/ai/playbooks/public", PermanentBasis.CallerScopedOnly, "167",
            "Public playbook definitions only: sprk_ispublic eq true (PlaybookEndpoints.cs:558; "
            + "PlaybookService.cs:344). Takes no id; nothing private of another principal is read. Owner round 12 "
            + "item 6."),
        Permanent("GET /api/ai/playbooks/templates", PermanentBasis.CallerScopedOnly, "167",
            "Returns an empty list today: ListTemplatesAsync is a stub because sprk_istemplate does not exist "
            + "(PlaybookEndpoints.cs:791; PlaybookService.cs:752-770). Takes no id. Owner round 12 item 6; if it "
            + "ever reads rows, re-classify."),
        Permanent("GET /api/ai/chat/playbooks", PermanentBasis.CallerScopedOnly, "167",
            "The picker list: the caller's own playbooks (ListUserPlaybooksAsync with the server-derived caller "
            + "id, ChatEndpoints.cs:1815-1824) plus public ones. Takes no id. Owner round 12 item 6; the filter fix "
            + "is task 164's."),
        Permanent("GET /api/agent/playbooks", PermanentBasis.CallerScopedOnly, "167",
            "The agent's list: the caller's own playbooks (ListUserPlaybooksAsync with the server-derived caller "
            + "id, AgentEndpoints.cs:306-311) plus the public catalog. Takes no id. Owner round 12 item 6; the "
            + "filter fix is task 164's."),

        // ---------- P:OwnerComparison (owner round 12 item 4) ----------
        Permanent("DELETE /api/memory/pins/{pinId}", PermanentBasis.OwnerComparison, "166",
            "Task 166 (owner round 12 item 4): loads the caller-chosen pin under the claim tenant "
            + "(PinnedMemoryEndpoints.cs:542) and deletes it ONLY when its UserId is the caller's (:551-556, :558); an unknown "
            + "pin (:545) and another user's pin (:555) are the SAME PinNotFound 404 (:778-783) — no existence oracle."),
        Permanent("POST /api/compose/document/{documentId:guid}/heartbeat", PermanentBasis.OwnerComparison, "167",
            "Loads the caller-chosen document and writes a heartbeat ONLY when the caller holds its checkout lock "
            + "(CheckedOutById == the caller's server-derived systemuserid, DocumentCheckoutService.cs:489); "
            + "missing, not checked out and not-yours all collapse to ONE 404 (ComposeCheckoutEndpoints.cs:104-109) "
            + "— no existence oracle. Was CallerScopedOnly in task 167; re-classified by owner round 12 item 4."),

        // ---------- P:CallerSuppliedContentOnly ----------
        Permanent("POST /api/ai/daily-briefing/summarize", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Builds the prompt from the request's categories and priority items only "
            + "(DailyBriefingEndpoints.cs:466-468); reads no stored record."),
        Permanent("POST /api/ai/daily-briefing/narrate", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Narrates the caller's PRE-COLLECTED payload: NarrateAsync passes systemUserId: null so nothing is "
            + "collected (DailyBriefingCompositeService.cs:163-176)."),
        Permanent("POST /api/ai/rag/embedding", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Embeds the request text only (GetEmbeddingAsync(request.Text), RagEndpoints.cs:468); reads no "
            + "index document."),
        Permanent("POST /api/communications/draft", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Drafts from the request's intent, instruction, body and subject only "
            + "(CommunicationDraftEndpoints.cs:72-81)."),
        Permanent("POST /api/compose/project", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Stateless projection of the DOCX bytes in the request (ProjectForMount(body.Content), "
            + "ComposeMountEndpoints.cs:309); persists nothing."),
        Permanent("POST /api/compose/documents/{documentId:guid}/checkout", PermanentBasis.CallerSuppliedContentOnly, "167",
            "STUB: returns a fixed problem response pointing clients at /api/documents/{id}/checkout and reads "
            + "nothing (ComposeCheckoutEndpoints.cs:49-62). A candidate for owner round 10 item 1."),
        Permanent("POST /api/compose/documents/{documentId:guid}/checkin", PermanentBasis.CallerSuppliedContentOnly, "167",
            "STUB: returns a fixed problem response and reads nothing (ComposeCheckoutEndpoints.cs:64-77). A "
            + "candidate for owner round 10 item 1."),
        Permanent("POST /api/v1/field-mappings/validate", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Validates two field TYPE names from the request (TypeCompatibilityValidator.Validate, "
            + "FieldMappingEndpoints.cs:106); reads nothing."),
        Permanent("POST /api/workspace/calculate-scores", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Scores the inputs the client supplies per item (WorkspaceEndpoints.cs:349-351); reads no event by "
            + "id."),
        Permanent("POST /api/workspace/events/{id:guid}/scores", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Computes priority and effort from the request inputs (WorkspaceEndpoints.cs:454-455); the route id "
            + "is only compared with the body's EventId (:432) — nothing is read by it."),
        Permanent("POST /api/workspace/files/extract-text", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Extracts text from the uploaded files in the request (ExtractTextFromFilesAsync, "
            + "WorkspaceFileEndpoints.cs:125)."),
        Permanent("POST /api/workspace/files/summarize", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Summarises the uploaded files in the request (WorkspaceFileEndpoints.cs:156-198); reads no stored "
            + "file by id."),
        Permanent("POST /api/workspace/matters/pre-fill", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Analyses the uploaded files in memory (MatterPreFillService.cs:308); writes nothing to storage (task "
            + "227f retired the staging upload). Reads no record by id."),
        Permanent("POST /api/workspace/matters/ai-summary", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Summarises the matter fields the client sends (prompt from the request, "
            + "WorkspaceMatterEndpoints.cs:235); loads no matter by id."),
        Permanent("POST /api/workspace/projects/pre-fill", PermanentBasis.CallerSuppliedContentOnly, "167",
            "Analyses the uploaded files (AnalyzeFilesAsync(files), WorkspaceProjectEndpoints.cs:100) in memory, "
            + "like the matter pre-fill; writes nothing to storage and reads no record by id."),

        // ---------- P:CreateWithNoPriorResource ----------
        Permanent("PUT /api/obo/me/files/{*path}", PermanentBasis.CreateWithNoPriorResource, "167",
            "Record-LESS upload (task 076): the route takes NO container parameter; the container is derived "
            + "server-side from the acting user's business unit (ResolveForActingUserAsync, OBOEndpoints.cs:274), "
            + "and an unresolvable caller gets a typed 403."),

        // ---------- P:OperatorGateInHandler ----------
        // (GET /api/diagnostics/tenant-container-resolver was its only entry; the route was REMOVED by
        // customer-provisioning-orchestration-r1 task 227f — a route that no longer exists needs no gate.)

        // ---------- N:161 ----------
        // The four thread/message write routes (rename, pin, DELETE thread, DELETE message) task 161's amendment owned were
        // RESOLVED by 161 (CommunicationRecordAuthorizationFilter: Write on the thread / message) and their waivers deleted at
        // the task-167 integration (2026-10-05).
    };

    // =============================================================================================
    // THE SWEEP LEDGER — the 82 findings (90 route keys) of the 2026-10-02 route authorization sweep
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE. A closed set: do not add, drop or re-own an entry (owner round 9; task 167 trigger 4). An entry
    // resolves ONLY by credit. The fix task, in one diff:
    //   1. makes the route pass Rule A by a credited filter or a HandlerDecision — or by an AdminOnlyRoutes entry in a
    //      group gated by RequireAuthorization("SystemAdmin") or the SPE admin policy (main-session round 34 item 5);
    //   2. deletes the route's Pending waiver;
    //   3. sets ResolvedBy = its task id and ProofTest = "{repo-relative test file}::{deny test method}".
    // A route DELETED under owner round 10 item 1 (no caller, not published — tasks 159, 160, 164) keeps its entry: the
    // fix task deletes the waiver and sets ResolvedBy + a ProofTest that pins the route's ABSENCE (its verb + a path the
    // template matches, asserted 404/405 signed in or absent from the endpoint table). Any other absent key still fails
    // (main-session round 34 item 4; RetiredEntryViolations).
    // Every ProofTest — a deny test or an absence pin — must RUN (task 167 f2-v1): a plain xUnit [Fact] or [Theory] with no
    // Skip on any attribute, public, outside any #if region, with no Skip call and no `return` in its own body, in a file
    // one of ProofTestProjects compiles (RouteAuthorizationGuardTests.cs "A PROOF TEST MUST RUN").
    // For an InsufficientDecision entry the guard cannot SEE a fix made inside an already-credited filter or
    // handler; that resolution is by declaration (ResolvedBy + ProofTest), reviewed at code review.
    // =============================================================================================

    private enum Severity
    {
        Critical,
        High,
        Medium,
        Low,
    }

    private sealed record SweepFinding(string SweepId, Severity Severity, string Route, string OwningTask, Gap Gap)
    {
        public string? ResolvedBy { get; init; }

        public string? ProofTest { get; init; }

        /// <summary>
        /// Main-session round 65 item 2: the ONE generic credit for a fix the scanner cannot see. When true, a present
        /// route's entry resolves only if its ProofTest drives the real app and asserts the route's refusal AND success
        /// (RouteAuthorizationGuardTests.ProvenByTest.cs), and Rule A then counts the route as declared.
        /// </summary>
        public bool ProvenByTest { get; init; }
    }

    private static readonly IReadOnlyList<SweepFinding> SweepFindings = new[]
    {
        new SweepFinding("S-11", Severity.Critical, "POST /api/v1/events/{id:guid}/cancel", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::DeletedRoutes_AreNotMapped_AndReachNothing" },
        new SweepFinding("S-12", Severity.Critical, "GET /api/v1/events", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::List_UnresolvedCaller_Is403CallerUnresolved_AndNoEventQueryRuns" },
        new SweepFinding("S-13", Severity.Critical, "GET /api/v1/events/{id:guid}", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::IdRoute_CallerWithoutRead_GetsTheUniform404_AndNoEventServiceIsCalled" },
        new SweepFinding("S-14", Severity.Critical, "DELETE /api/v1/events/{id:guid}", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::DeletedRoutes_AreNotMapped_AndReachNothing" },
        new SweepFinding("S-15", Severity.Critical, "PUT /api/v1/events/{id:guid}", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::DeletedRoutes_AreNotMapped_AndReachNothing" },
        new SweepFinding("S-16", Severity.Critical, "POST /api/v1/events/{id:guid}/complete", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::WriteRoute_CallerWithReadOnly_Gets403InsufficientRights_AndNothingIsWritten" },
        new SweepFinding("S-37", Severity.High, "GET /api/v1/events/{id:guid}/logs", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::DeletedRoutes_AreNotMapped_AndReachNothing" },
        new SweepFinding("S-38", Severity.High, "POST /api/v1/events", "159", Gap.NoDecision)
            { ResolvedBy = "159", ProofTest = "tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs::Create_WithThePrivilegeButNoAppendToOnTheRegarding_Is403_AndNoEventOrLogRowIsWritten" },
        new SweepFinding("S-09", Severity.Critical, "POST /api/dataverse/fetch", "160", Gap.NoDecision)
            { ResolvedBy = "160", ProofTest = "tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/DataverseProxyRoutesRemovedTests.cs::PostFetch_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity" },
        new SweepFinding("S-10", Severity.Critical, "GET /api/dataverse/record/{entityLogicalName}/{id:guid}", "160", Gap.NoDecision)
            { ResolvedBy = "160", ProofTest = "tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/DataverseProxyRoutesRemovedTests.cs::GetRecord_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity" },
        new SweepFinding("S-07", Severity.Critical, "POST /api/communications/send", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::Send_AnAttachmentTheCallerCannotRead_Is403_AndUnknownOrUnparseableIdsGetTheSameAnswer" },
        new SweepFinding("S-07", Severity.Critical, "POST /api/communications/send-bulk", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::SendBulk_AnUnreadableAttachment_Is403ForTheWholeRequest_AndAnUnknownOneGetsTheSameAnswer" },
        new SweepFinding("S-08", Severity.Critical, "POST /api/communications/template/render", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::TemplateRender_ARegardingRecordTheCallerCannotRead_Is403WithNoTraceOfIt_AndAnUnknownOneGetsTheSameAnswer" },
        new SweepFinding("S-33", Severity.High, "POST /api/communications/{id:guid}/archive", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::Archive_InvisibleOrInternalOnlyCommunication_GetsTheHandlersOwnNotFound_AndNothingIsArchived" },
        new SweepFinding("S-34", Severity.High, "POST /api/communications/{id:guid}/suggest-associations", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::SuggestAssociations_InvisibleCommunication_GetsTheHandlersOwnNotFound_AndTheEngineNeverRuns" },
        new SweepFinding("S-35", Severity.High, "POST /api/communications/{communicationId:guid}/create-task", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated" },
        new SweepFinding("S-58", Severity.Medium, "POST /api/communications/threads", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::CreateRecordThread_WithoutAppendTo_Is403_AndANonExistentRecordGetsTheSameAnswer" },
        new SweepFinding("S-59", Severity.Medium, "POST /api/communications/{id:guid}/confirm-affinity", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten" },
        new SweepFinding("S-60", Severity.Medium, "POST /api/communications/proposals/{reviewLogId:guid}/dismiss", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten" },
        new SweepFinding("S-61", Severity.Medium, "POST /api/communications/proposals/{reviewLogId:guid}/create-task/apply", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated" },
        new SweepFinding("S-62", Severity.Medium, "POST /api/communications/accounts/{id:guid}/verify", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::VerifyAccount_WithoutWrite_GetsTheHandlersOwnNotFound_AndNoGraphOrWrite" },
        new SweepFinding("S-79", Severity.Low, "GET /api/communications/{id:guid}/status", "161", Gap.NoDecision)
            { ResolvedBy = "161", ProofTest = "tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs::Status_TheDeletedRoute_AnswersNobody_AndReadsNothing" },
        new SweepFinding("S-01", Severity.Critical, "POST /api/ai/analysis/{analysisId:guid}/export", "162", Gap.NoDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/regression/AnalysisRouteRetirementTests.cs::RetiredRoute_IsAbsentFromTheEndpointTable" },
        new SweepFinding("S-02", Severity.Critical, "GET /api/ai/analysis/{analysisId:guid}", "162", Gap.NoDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/contract/Api/Ai/AnalysisEndpointsAuthorizationContractTests.cs::Get_ReadableDocumentUnreadableMatter_IsUniform404" },
        new SweepFinding("S-22", Severity.High, "POST /api/ai/analysis/promote", "162", Gap.NoDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/contract/Api/Ai/AnalysisEndpointsAuthorizationContractTests.cs::Promote_BodyDocumentWithoutAttachRights_Is403" },
        new SweepFinding("S-50", Severity.Medium, "POST /api/ai/analysis/{analysisId:guid}/save", "162", Gap.NoDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/regression/AnalysisRouteRetirementTests.cs::RetiredRoute_IsAbsentFromTheEndpointTable" },
        new SweepFinding("S-51", Severity.Medium, "POST /api/ai/analysis/fork", "162", Gap.NoDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/regression/AnalysisRouteRetirementTests.cs::RetiredRoute_IsAbsentFromTheEndpointTable" },
        new SweepFinding("S-52", Severity.Medium, "POST /api/ai/analysis/execute", "162", Gap.InsufficientDecision)
            { ResolvedBy = "162", ProofTest = "tests/integration/contract/Api/Ai/AnalysisEndpointsAuthorizationContractTests.cs::Execute_ProfileBranchWithoutWrite_Is403BeforeAnyWrite" },
        new SweepFinding("S-03", Severity.Critical, "POST /api/ai/knowledge/test-search", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-26", Severity.High, "GET /api/ai/knowledge/indexes/{indexName}/documents", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-27", Severity.High, "DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-28", Severity.High, "POST /api/ai/knowledge/indexes/reindex/{documentId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-04", Severity.Critical, "POST /api/ai/rag/index-file", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::IndexFile_NamedDocumentTheCallerCannotWrite_IsTheUniform404_NothingIndexedOrStamped" },
        new SweepFinding("S-05", Severity.Critical, "POST /api/ai/rag/search", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::Search_CallerWhoCanReadNoneOfTheRows_Gets200EmptyAndZeroCount" },
        new SweepFinding("S-06", Severity.Critical, "POST /api/ai/rag/index", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::Index_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsIndexed" },
        new SweepFinding("S-06", Severity.Critical, "POST /api/ai/rag/index/batch", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-30", Severity.High, "DELETE /api/ai/rag/{documentId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::DeleteChunk_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsDeleted" },
        new SweepFinding("S-30", Severity.High, "DELETE /api/ai/rag/source/{sourceDocumentId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-20", Severity.High, "POST /api/admin/knowledge/index-reference/{knowledgeSourceId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-21", Severity.High, "DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId}", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-49", Severity.Medium, "POST /api/admin/knowledge/index-references", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-17", Severity.Critical, "POST /api/insights/ask", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Ask_PersistingPlaybook_AReaderWithoutWrite_GetsTheSameUniform404_AndNothingRuns" },
        new SweepFinding("S-40", Severity.High, "POST /api/insights/assistant/query", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Assistant_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_NoFacade_NoSseFrame" },
        new SweepFinding("S-41", Severity.High, "POST /api/insights/search", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Search_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_AndSearchNeverRuns" },
        new SweepFinding("S-46", Severity.High, "POST /api/workspace/ai/summary", "163", Gap.NoDecision)
            { ResolvedBy = "163", ProofTest = "tests/integration/contract/Api/Workspace/WorkspaceAiSummaryAuthorizationContractTests.cs::UnreadableAndAbsentRecords_AreTheIdenticalUniform404_AndNothingIsRead" },
        new SweepFinding("S-24", Severity.High, "POST /api/ai/chat/sessions", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProvenByTest = true, ProofTest = "tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs::ProvenByTest_ChatSessionCreate_UnreadableDocumentIs403_ReadableDocumentIs201" },
        new SweepFinding("S-23", Severity.High, "PATCH /api/ai/chat/sessions/{sessionId}/context", "164", Gap.InsufficientDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs::SwitchContext_AnyIdTheCallerCannotRead_IsTheUniform403_AndTheSessionIsUnchanged" },
        new SweepFinding("S-25", Severity.High, "POST /api/ai/chat/sessions/{sessionId}/messages", "164", Gap.InsufficientDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs::Messages_APerTurnDocumentTheCallerCannotRead_IsTheUniform403ProblemDetails_NotAStream_AndNoTurnRuns" },
        new SweepFinding("S-53", Severity.Medium, "POST /api/ai/chat/sessions/{sessionId}/dispatch", "164", Gap.InsufficientDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs::Dispatch_AStoredContextTheCallerCannotRead_IsTheUniform403_AndTheOrchestratorNeverRuns" },
        new SweepFinding("S-18", Severity.High, "POST /api/agent/message", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProvenByTest = true, ProofTest = "tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs::ProvenByTest_AgentMessage_UnreadableBodyDocumentIs403_ReadableOneRunsTheTurnAnd200" },
        new SweepFinding("S-19", Severity.High, "POST /api/agent/run-playbook", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs::AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns" },
        new SweepFinding("S-78", Severity.Low, "GET /api/agent/playbooks/status/{jobId:guid}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProvenByTest = true, ProofTest = "tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs::ProvenByTest_AgentPlaybookStatus_AnotherUsersRunIs404_TheCallersOwnRunIs200" },
        new SweepFinding("S-29", Severity.High, "POST /api/ai/playbooks/{id:guid}/execute", "164", Gap.InsufficientDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs::Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns" },
        new SweepFinding("S-54", Severity.Medium, "PUT /api/ai/playbooks/{id:guid}/nodes/reorder", "164", Gap.InsufficientDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-55", Severity.Medium, "GET /api/ai/playbooks/by-id/{id}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs::ById_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_ThatNeverEchoesTheId_AndTheCacheIsNeverRead" },
        new SweepFinding("S-55", Severity.Medium, "GET /api/ai/playbooks/by-name/{name}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-56", Severity.Medium, "GET /api/ai/prompts", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-56", Severity.Medium, "POST /api/ai/prompts", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-57", Severity.Medium, "GET /api/ai/prompts/{id}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-57", Severity.Medium, "PUT /api/ai/prompts/{id}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-57", Severity.Medium, "DELETE /api/ai/prompts/{id}", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-57", Severity.Medium, "POST /api/ai/prompts/{id}/render", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-31", Severity.High, "POST /api/ai/document-intelligence/match-records", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-32", Severity.High, "POST /api/ai/document-intelligence/associate-record", "164", Gap.NoDecision)
            { ResolvedBy = "164", ProofTest = "tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable" },
        new SweepFinding("S-44", Severity.High, "POST /api/spe/bulk/delete", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued" },
        new SweepFinding("S-72", Severity.Medium, "POST /api/spe/bulk/permissions", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued" },
        // S-45, S-73, S-75: the sweep keyed these {id:guid}; task 165 renamed the parameter to {configId:guid} (the
        // tenant scope never read "id"). Reconciled at the 165 integration under main-session round 65: the same verb
        // and path, re-keyed to the live route (the task 167 trigger 4 escalation, answered).
        new SweepFinding("S-45", Severity.High, "PUT /api/spe/configs/{configId:guid}", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Put_OutOfScopeConfig_IsTheUniform404_AndNothingIsWritten" },
        new SweepFinding("S-73", Severity.Medium, "GET /api/spe/configs/{configId:guid}", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Get_OutOfScopeConfig_AnswersTheSame404AsAnUnknownConfig_AndReadsNothing" },
        new SweepFinding("S-74", Severity.Medium, "POST /api/spe/configs", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Post_IntoAnUnreachableBusinessUnit_Is403_AndNothingIsCreated" },
        new SweepFinding("S-75", Severity.Medium, "DELETE /api/spe/configs/{configId:guid}", "165", Gap.InsufficientDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/SpeAdmin/SpeAdminConfigAndBulkTenantScopeTests.cs::Delete_OutOfScopeAndUnknownConfigs_AreTheUniform404_AndNothingIsDeleted" },
        new SweepFinding("S-47", Severity.Medium, "POST /api/admin/record-matching/sync", "165", Gap.NoDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/Admin/RecordMatchingAdminPolicyTests.cs::ACallerWithAUserRoleOnly_IsForbidden_AndTheSyncServiceIsNeverCalled" },
        new SweepFinding("S-48", Severity.Medium, "POST /api/admin/record-matching/sync-incremental", "165", Gap.NoDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/Admin/RecordMatchingAdminPolicyTests.cs::ACallerWithAUserRoleOnly_IsForbidden_AndTheSyncServiceIsNeverCalled" },
        new SweepFinding("S-77", Severity.Low, "GET /api/admin/record-matching/status", "165", Gap.NoDecision)
            { ResolvedBy = "165", ProofTest = "tests/integration/auth/Admin/RecordMatchingAdminPolicyTests.cs::ACallerWithAUserRoleOnly_IsForbidden_AndTheSyncServiceIsNeverCalled" },
        new SweepFinding("S-36", Severity.High, "PUT /api/v1/documents/{id}", "166", Gap.InsufficientDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/data-mutation/CoreAncestorStamping/DocumentRefileRestampRouteTests.cs::Put_NamingAStoragePointerField_IsRefusedAndWritesNothing" },
        new SweepFinding("S-66", Severity.Medium, "GET /api/v1/documents", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Documents/DocumentPointerAndContainerListAuthorizationContractTests.cs::List_ACallerWithNoReadOnTheOwningRecord_Is403_AndTheListingNeverRuns" },
        new SweepFinding("S-39", Severity.High, "POST /api/v1/external-access/close-project", "166", Gap.InsufficientDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs::CloseProject_AClientSuppliedContainerIdIsIgnored_TheDerivedContainerIsTheOnlyOneTouched" },
        new SweepFinding("S-42", Severity.High, "GET /api/memory/records/{entityLogicalName}/{id:guid}", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs::RetiredRoute_WhenRequested_Returns404NotRouted" },
        new SweepFinding("S-43", Severity.High, "POST /api/memory/pins", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Memory/MemoryRecordAndPinAuthorizationContractTests.cs::CreatePin_WithoutAppendToOnTheMatter_Is403_AndWritesNothing" },
        new SweepFinding("S-68", Severity.Medium, "PUT /api/memory/pins/{pinId}", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Memory/MemoryRecordAndPinAuthorizationContractTests.cs::UpdatePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNoMatterIsAsked" },
        new SweepFinding("S-63", Severity.Medium, "POST /api/compose/documents/{documentSpeId}/promote", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs::RetiredRoute_WhenRequested_Returns404NotRouted" },
        new SweepFinding("S-64", Severity.Medium, "POST /api/compose/upload", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProvenByTest = true, ProofTest = "tests/integration/contract/Api/Compose/ComposeSessionAndContainerAuthorizationContractTests.cs::ProvenByTest_Upload_AnotherUsersSessionIs404_TheOwnersSessionIs200" },
        new SweepFinding("S-65", Severity.Medium, "POST /api/compose/document/{documentSpeId}/check-changes", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProvenByTest = true, ProofTest = "tests/integration/contract/Api/Ai/ComposeWordShuttlePollEndpointContractTests.cs::ProvenByTest_CheckChanges_AnItemTheCallerCannotSeeIs404_AVisibleItemIs200" },
        new SweepFinding("S-80", Severity.Low, "POST /api/compose/sessions/{sessionId}/annotations", "166", Gap.InsufficientDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Compose/ComposeSessionAndContainerAuthorizationContractTests.cs::SaveAnnotations_AForeignBodyTenant_IsIgnored_TheWriteLandsOnTheAuthorizedSession" },
        new SweepFinding("S-67", Severity.Medium, "POST /api/v1/field-mappings/push", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/FieldMappings/FieldMappingPushAuthorizationContractTests.cs::Push_WhenTheSourceCannotBeAuthorizedAsTheCaller_IsTheUniform404_AndNothingIsReadOrWritten" },
        new SweepFinding("S-69", Severity.Medium, "POST /api/office/quickcreate/{entityType}", "166", Gap.InsufficientDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs::Post_QuickCreate_WhenTheCallerLacksTheTablesCreatePrivilege_Returns403_AndCreatesNothing" },
        new SweepFinding("S-70", Severity.Medium, "POST /api/reporting/export", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Reporting/ReportingCatalogBindingContractTests.cs::Export_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked" },
        new SweepFinding("S-71", Severity.Medium, "GET /api/reporting/embed-token", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Reporting/ReportingCatalogBindingContractTests.cs::EmbedToken_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked" },
        new SweepFinding("S-81", Severity.Low, "GET /api/reporting/reports/{reportId:guid}", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/contract/Api/Reporting/ReportingCatalogBindingContractTests.cs::GetReport_ForARowTheCallerCannotBindTo_IsTheUniform404" },
        new SweepFinding("S-76", Severity.Medium, "POST /api/v1/work-assignments", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs::RetiredRoute_WhenRequested_Returns404NotRouted" },
        new SweepFinding("S-82", Severity.Low, "GET /api/workspace/state", "166", Gap.NoDecision)
            { ResolvedBy = "166", ProofTest = "tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs::RetiredRoute_WhenRequested_Returns404NotRouted" },
    };
}
