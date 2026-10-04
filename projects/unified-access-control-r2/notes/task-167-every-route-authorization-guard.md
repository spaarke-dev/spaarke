# Task 167 — the every-route authorization guard

> **Owner mandate**: owner round 9 item 3 (`notes/session27-owner-decisions-and-research.md`) — "every BFF route must
> declare how it is authorized: a record-level check, an admin policy, or an explicit, reasoned waiver ... A new route
> with only a sign-in check fails the build."
> **Evidence**: `notes/route-authorization-sweep-2026-10-02.md` (82 findings, 90 route keys).
> **Branch**: `task/uac-r2-167` from `work/unified-access-control-r2` @ `6b243f092`; verifier round r1 on
> `task/uac-r2-167-r1` (§15); verifier round r2 on `task/uac-r2-167-r2` (§16); fix round f1 on `task/uac-r2-167-f1`
> (§17 — owner round 14 items 1-2 and the r2 verifier's residuals; **nothing left open**). **`src/` changes, each by
> owner decision**: round r1 (owner round 12 item 1) rate-limited `GET /healthz`, `GET /healthz/catalog` and `GET /ping`;
> round f1 (owner round 14) moved those three to a dedicated `"health-probe"` policy (`RateLimitingModule.cs`) and set
> the authorization FallbackPolicy (`AuthorizationModule.ApplyFallbackPolicy`), plus comment-only corrections in three
> endpoint files that said "no FallbackPolicy exists" (§17.6).

## 0. ⚠️ Read first — a HIGH finding the sweep never traced (escalation trigger 7, sent to the main session)

**`GET /healthz/dataverse/doc/{id}` is an ANONYMOUS read of any `sprk_document` by id.**
`Infrastructure/DI/EndpointMappingExtensions.cs:81-113`: `.AllowAnonymous()` + `RequireRateLimiting("anonymous")`
only, mapped unconditionally; `IDocumentDataverseService.GetDocumentAsync(id)` is app-only (`GraphModule.cs:95` forwards
it to the `IDataverseService` singleton). It returns name, file name, `isEmailArchive`, `parentDocumentId`, `matterId`,
`projectId`, `invoiceId` and `emailConversationIndex` for a caller-chosen id, and 200-FOUND vs 200-NOT_FOUND is an
existence oracle. Proposed severity **high**.
- **Owner**: task **166** — its amendment (a) already names this route ("remove or gate it, and move the smoke check that
  uses it to /healthz/dataverse"). The waiver is therefore `Pending("166", NoDecision)`, not `UNOWNED-NEW` (a deliberate
  deviation from 167's AC text, which predates 166's amendment; recorded in §9 and **accepted by owner round 12 item
  8**).
- **Consumer a fix breaks**: `.claude/skills/bff-deploy/SKILL.md` §9c (post-deploy "MI → Dataverse" smoke check), plus
  `projects/dotnet-10-upgrade-r1/notes/slot-swap-runbook.md` and `051-operator-runbook.md`. The skill edit is
  main-session-only (`.claude/**`); 166 records its exact text.
- Reported to the main session by `SendMessage` on confirmation (2026-10-03), before the rest of the task. (The harness
  forbids this task from editing `current-task.md`, so trigger 7's "write it in current-task.md" is satisfied by the
  message plus this section.)

No `UNOWNED-NEW` finding is rated critical or high; the highest is medium (§3).

## 1. What changed (tests only)

| File | Role |
|---|---|
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` | The RULES and CONTROLS (rewritten): Rule A over every route, the waiver/ledger/admin/policy pins, HandlerDecision verification, the attachment-form census, the pass-through pins, Rule B with whole-identifier tokens, census, Rule E, and every negative/positive control. |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Scanner.cs` (new) | The PARSER: a C# lexer (comments + literals), type/method parsing, call-site binding through method parameters and aggregators, the fluent-chain parser, form normalization. |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (new) | The DATA: Aggregators, GovernedFiles (120), the census history, CreditedForms (20), AdminMechanisms (4), NonDecidingAttachments (12), NotAuthorizationForms (10), AdminOnlyRoutes (83 routes in 24 groups), HandlerDecisions (43), CallerContextSeams (10), DecisionServices, ClaimOnlyFilters, the Waivers (212) and the SweepFindings ledger (90). |

**Split choice (recommended in the POML, taken):** the class is `partial` across three files with three different
reasons to change — rules (rare), parser (when the codebase invents a registration shape), data (every fix task). Tasks
159-166 resolve their entries by editing `.Ledger.cs` only, so their merge conflicts land on lists, not on rule code.
(The POML listed two files; the parser split is a third, same reasoning — CLAUDE.md §11.5 cohesion, not line count.)

## 2. Scanner output (authoritative; the POML's Python approximation was guidance)

- **Census**: 120 files (`EndpointFiles()` now selects by the scanner's own vocabulary — `Map{Verb}`, `MapMethods`,
  `MapHealthChecks` — over code with comments AND literals blanked). Count unchanged at 120; GovernedFiles grew from 25
  to all 120 and the two sets are asserted **equal**. 3 files outside `Api/**`.
- **Registrations**: 431 total → **430 live** + **1 unbound** (`ProjectClosureEndpoint.MapProjectClosureEndpoint`, zero
  callers → `NotMapped`). Live by verb: POST 190, GET 187 (incl. 2 MapHealthChecks), DELETE 26, PUT 19, PATCH 8 (5
  MapPatch + 3 MapMethods `["PATCH"]`). **118** live routes are aggregator-bound (Compose, SPE admin, external).
- **Zero** unparseable registrations, zero scanner problems, **zero duplicate keys** (the close-project dead
  registration no longer collides: it is unbound).
- **Credit** (live routes): PerResource **108** · HandlerDecision **43** · AdminOnly **83** · Anonymous **16** · None
  **180**. Flagged (None + Anonymous) = **196** = 74 sweep NoDecision + 122 other.
- **Waivers** (after round r1 + owner round 12, §15): 214 = **125 Pending** (90 sweep: 74 NoDecision + 16
  InsufficientDecision; **14** owned by a fix task's amendment or by owner round 12 — 161: 4, 166: 10, two of them
  InsufficientDecision on credited routes; **21 UNOWNED-NEW**) + **89 Permanent** (CallerScopedOnly 31 · ReferenceData
  26 · CallerSuppliedContentOnly 15 · AnonymousByDesign 14 · OwnerComparison 1 · CreateWithNoPriorResource 1 ·
  OperatorGateInHandler 1). (At the first commit: 212 = 122 Pending + 90 Permanent.)
- The sweep ledger's 90 keys are all present on the branch with the expected N/I credit state (escalation trigger 4 did
  not fire); no 159-166 commit had landed on this branch (§8).

## 3. UNOWNED-NEW findings (21) — for the main session to assign

Each is a `Pending("UNOWNED-NEW", NoDecision)` waiver; the code pins the count (`ExpectedUnownedNewCount = 21`).
Columns: route, registration, proposed severity, owner, evidence (handler file:line), named consumer a fix would break.

| # | Route | Registered | Sev | Owner | What a caller can do / evidence | Consumer |
|---|---|---|---|---|---|---|
| 1 | `GET /api/ai/chat/context-mappings/analysis/{analysisId}` | `Api/Ai/AnalysisChatContextEndpoints.cs:37` | medium | UNOWNED-NEW | Refuted by ACCIDENT, not by a decision: the app-only Retrieve asks for attributes that do not exist (AnalysisChatContextResolver.cs:267-277). AiAuthorizationFilter does not decide here. Fix the attributes and it reads any analysis's chat context. | none found |
| 2 | `GET /api/ai/chat/sessions/by-analysis/{analysisId:guid}` | `Api/Ai/ChatEndpoints.cs:201` | medium | UNOWNED-NEW | The handler returns the most recent session bound to ANY analysis with no owner check (ChatEndpoints.cs:2278-2303). Safe today only because AiAuthorizationFilter authorizes the analysis id as a DOCUMENT id and so always denies. | none found |
| 3 | `POST /api/communications/acs/eventgrid` (ANONYMOUS) | `Api/AcsEventGridEndpoints.cs:30` | medium | UNOWNED-NEW | ANONYMOUS, and no MANDATORY authenticity control. The ?sig= shared secret is enforced only when configured (AcsEventGridIngressService.cs:68-69); the mandatory topic allow-list (:121-141) checks a topic string the caller writes in the body. Anyone who knows the ACS topic id can enqueue forged chat events. | none found |
| 4 | `POST /api/communications/proposals/{reviewLogId:guid}/apply` | `Api/CommunicationEndpoints.cs:292` | medium | UNOWNED-NEW | The target write is impersonated, but the caller-chosen review-log row is loaded app-only with a 404 existence oracle and the audit rows are written app-only (CommunicationProposalApplyService.cs:166-200, :293). Partial coverage; siblings S-60/S-61 are task 161's. | none found |
| 5 | `POST /api/communications/proposals/{reviewLogId:guid}/undo` | `Api/CommunicationEndpoints.cs:361` | medium | UNOWNED-NEW | Same shape as /apply: the review-log row is loaded app-only by caller-chosen id (CommunicationProposalApplyService.cs:425-445), then the revert is impersonated. Partial coverage. | none found |
| 6 | `POST /api/compose/documents/{documentRecordId:guid}/refresh-profile` | `Api/ComposeDocumentEndpoints.cs:49` | medium | UNOWNED-NEW | A caller-chosen documentRecordId and body TenantId: the download is OBO, but the seven profile fields are then written app-only (ComposeDocumentEndpoints.cs:301-332). SPE read rights buy a Dataverse write. | none found |
| 7 | `POST /api/v1/documents` | `Api/DataverseDocumentsEndpoints.cs:32` | medium | UNOWNED-NEW | The body's required ContainerId (Models.cs CreateDocumentRequest) is written with no caller-rights check; only the owner team is server-derived (DataverseDocumentsEndpoints.cs:571-597). The old Permanent 'CREATE' waiver was not true as written. | none found |
| 8 | `DELETE /api/ai/chat/context-mappings/cache` | `Api/Ai/ChatEndpoints.cs:338` | low | UNOWNED-NEW | Any signed-in user evicts EVERY cached context mapping, not tenant-scoped (EvictAllCachedMappingsAsync, ChatEndpoints.cs:1946). An operator action with no admin policy. | none found |
| 9 | `GET /api/ai/feedback/capability/{id}` | `Api/Ai/FeedbackEndpoints.cs:55` | low | UNOWNED-NEW | Tenant-wide feedback aggregate for any capability id, to any signed-in user (FeedbackEndpoints.cs:55). Usage analytics with no admin policy. | none found |
| 10 | `GET /api/ai/feedback/playbook/{id}` | `Api/Ai/FeedbackEndpoints.cs:43` | low | UNOWNED-NEW | Tenant-wide feedback aggregate for any playbook id, to any signed-in user (FeedbackEndpoints.cs:142). Usage analytics with no admin policy. | none found |
| 11 | `GET /api/ai/knowledge/indexes/health` | `Api/Ai/KnowledgeBaseEndpoints.cs:32` | low | UNOWNED-NEW | Index names and document counts of the tenant knowledge and discovery indexes (KnowledgeBaseEndpoints.cs:131-140): the same operator-surface disclosure as sweep S-77, behind sign-in only. | none found |
| 12 | `GET /api/compose/documents/{documentSpeId}` | `Api/ComposeDocumentEndpoints.cs:25` | low | UNOWNED-NEW | The SPE read is OBO, but the documentRecordId and matterId query values are not tied to it and drive app-only reads and a profile dispatch (ComposeDocumentEndpoints.cs:61-137). | none found |
| 13 | `GET /api/resilience/circuits` | `Api/ResilienceEndpoints.cs:23` | low | UNOWNED-NEW | Operator diagnostics: every downstream circuit-breaker state to any signed-in user (ResilienceEndpoints.cs:48-59). No admin policy. | none found |
| 14 | `GET /api/resilience/circuits/{serviceName}` | `Api/ResilienceEndpoints.cs:30` | low | UNOWNED-NEW | Operator diagnostics: one downstream circuit-breaker state to any signed-in user (ResilienceEndpoints.cs:30). No admin policy. | none found |
| 15 | `GET /api/resilience/health` | `Api/ResilienceEndpoints.cs:38` | low | UNOWNED-NEW | Operator diagnostics: per-service resilience health to any signed-in user (ResilienceEndpoints.cs:72-84). No admin policy. | none found |
| 16 | `POST /api/ai/chat/export/word` | `Api/Ai/ChatWordExportEndpoints.cs:41` | low | UNOWNED-NEW | Loads the body SessionId in the tenant with no owner check (ChatWordExportEndpoints.cs:117): a session-existence oracle. The container comes from configuration (:249-267) and the upload is OBO. | none found |
| 17 | `POST /api/ai/feedback` | `Api/Ai/FeedbackEndpoints.cs:31` | low | UNOWNED-NEW | Writes feedback against ANY body SessionId with no owner check (FeedbackEndpoints.cs:96-110); it feeds the per-playbook aggregates other users read. | none found |
| 18 | `POST /api/ai/playbooks` | `Api/Ai/PlaybookEndpoints.cs:25` | low | UNOWNED-NEW | Creates a playbook whose body names EXISTING action/skill/knowledge/tool rows (SavePlaybookRequest, PlaybookDto.cs:8; PlaybookEndpoints.cs:213) with no check that the caller may use them. | none found |
| 19 | `POST /api/communications/{communicationId:guid}/tasks/{taskId:guid}/undo` | `Api/CommunicationEndpoints.cs:375` | low | UNOWNED-NEW | The task update is impersonated (CommunicationCreateTaskApplyService.cs:591-601), but the caller-chosen communicationId is unchecked and the audit row is written app-only (:632). | none found |
| 20 | `POST /api/compose/active-document` | `Api/ComposeActiveDocumentEndpoints.cs:31` | low | UNOWNED-NEW | The session owner IS checked (ComposeActiveDocumentEndpoints.cs:102-113), but a body DocumentId is recorded as the session's active document with no read check; downstream consumers may read it app-only. | none found |
| 21 | `POST /api/compose/documents/{documentSpeId}/apply-template` | `Api/ComposeTemplateEndpoints.cs:35` | low | UNOWNED-NEW | Resolves a caller-named template with an APP token (ComposeTemplateEndpoints.cs:95-106). Safe only if every template is org-shared. | none found |

## 4. Non-sweep routes owned by a fix task's amendment or by owner round 12 (14)

Pending, owned by the task whose `<amendments>` names the route (owner round 12 item 8 keeps them there) or to which
owner round 12 assigns the fix — more accurate than `UNOWNED-NEW`, and inside the closed owner set. These are NOT
sweep ledger entries; when the owning task fixes one, its waiver goes stale (credited, or its fingerprint changes) or
absent (deleted) and must be deleted. Rows 1-11 are NoDecision from the first commit; rows 12-14 were added in round
r1 (§15).

| # | Route | Registered | Sev | Owner | Evidence | Consumer |
|---|---|---|---|---|---|---|
| 1 | `GET /healthz/dataverse/doc/{id}` (ANONYMOUS) | `Infrastructure/DI/EndpointMappingExtensions.cs:81` | high | 166 | ANONYMOUS read of any sprk_document by id (EndpointMappingExtensions.cs:81-113): name, file name, parent, matter, project and invoice ids, app-only via IDocumentDataverseService. Rate limit only. Task 166 amendment (a) owns it; consumer .claude/skills/bff-deploy/SKILL.md §9c must move to GET /healthz/dataverse first. | .claude/skills/bff-deploy/SKILL.md §9c smoke check; projects/dotnet-10-upgrade-r1/notes/slot-swap-runbook.md; 051-operator-runbook.md |
| 2 | `DELETE /api/communications/threads/{threadId:guid}` | `Api/CommunicationEndpoints.cs:192` | medium | 161 | Impersonated READ check then an app-only deactivate (CommunicationEndpoints.cs:192). Task 161 amendment: require Write on the thread. | none found |
| 3 | `DELETE /api/communications/{id:guid}` | `Api/CommunicationEndpoints.cs:204` | medium | 161 | Impersonated READ check then an app-only deactivate via IThreadResolver (CommunicationEndpoints.cs:204; ThreadResolver.cs:396). Task 161 amendment: require Write on the message. | none found |
| 4 | `PATCH /api/communications/threads/{threadId:guid}/pin` | `Api/CommunicationEndpoints.cs:178` | medium | 161 | Impersonated READ check then an app-only WRITE of the pin (CommunicationEndpoints.cs:178). Task 161 amendment: require Write on the thread. | none found |
| 5 | `POST /api/communications/threads/{threadId:guid}/rename` | `Api/CommunicationEndpoints.cs:165` | medium | 161 | Impersonated READ check (CanCallerSeeThreadAsync) then an app-only WRITE (CommunicationEndpoints.cs:165): Read is not Write. Task 161 amendment: require Write on the thread. | none found |
| 6 | `DELETE /api/reporting/reports/{reportId:guid}` | `Api/Reporting/ReportingEndpoints.cs:108` | medium | 166 | Author/Admin role only, then deletes a report in ANY caller-chosen workspace via the service principal (ReportingEndpoints.cs:108). Task 166 amendment (f). | none found |
| 7 | `POST /api/compose/documents/create-on-save` | `Api/ComposeSaveEndpoints.cs:51` | medium | 166 | Same ExecuteSaveAsync path as /save (ComposeSaveEndpoints.cs:159-240): body TenantId + SessionId rebind with no owner check, and a body SourceDocumentRecordId. Task 166 amendment (d), 'any other Compose session route with the same flaw'. | none found |
| 8 | `POST /api/compose/documents/{documentSpeId}/save` | `Api/ComposeSaveEndpoints.cs:26` | medium | 166 | The SPE write is OBO, but the body TenantId and SessionId flow into the first-save rebind with no owner check, and DocumentRecordId is read app-only (ComposeSaveEndpoints.cs:26, :81-123). Task 166 amendment (d). | none found |
| 9 | `POST /api/reporting/reports` | `Api/Reporting/ReportingEndpoints.cs:87` | medium | 166 | Author/Admin role only, then creates a report in ANY caller-chosen workspaceId via the service principal (ReportingEndpoints.cs:87). Task 166 amendment (f). | none found |
| 10 | `PUT /api/reporting/reports/{reportId:guid}` | `Api/Reporting/ReportingEndpoints.cs:97` | medium | 166 | Author/Admin role only, then updates a report in ANY caller-chosen workspace via the service principal (ReportingEndpoints.cs:97). Task 166 amendment (f). | none found |
| 11 | `GET /api/reporting/reports` | `Api/Reporting/ReportingEndpoints.cs:66` | low | 166 | Lists reports in ANY caller-chosen workspaceId with the service principal (ReportingEndpoints.cs:228-251); only the Reporting role is checked. Task 166 amendment (f) and owner round 10 item 1 own the reporting module. | none found |
| 12 | `DELETE /api/memory/pins/{pinId}` | `Api/Memory/PinnedMemoryEndpoints.cs:184` | low | 166 (NoDecision) | Loads ANY pin by caller-chosen id (`:499`) and answers 404 "Pin not found" (`:503`) for an unknown id but 403 "Caller does not own this pin" (`:515`) for another user's — a cross-user pin-existence oracle; the delete itself (`:521`) is owner-gated. Owner round 12 item 4: 166 makes both answers one 404, then replaces this waiver with Permanent **OwnerComparison** in the same diff. Was Permanent CallerScopedOnly at the first commit (verifier finding 6). | none found |
| 13 | `POST /api/v1/external-access/revoke` | `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs` (bound by the external-access aggregator) | medium | 166 (InsufficientDecision, observed `AddDelegationRuleFilter`) | DelegationRuleFilter decides Write on the project; the handler then acts on a client-supplied `ContainerId` (`RevokeExternalAccessEndpoint.cs:350-357`). 166 amendment (b); owner round 12 item 9. | none found |
| 14 | `POST /api/office/todo` | `Api/Office/OfficeEndpoints.cs:1346` | medium | 166 (InsufficientDecision, observed `AddTodoSourceAccessFilter`) | TodoSourceAccessFilter gates the SOURCE read; the to-do row is created with no Create-privilege check (`CreateTodoAsync`, `OfficeEndpoints.cs:1368`, `:1409`). 166 amendment (c); owner round 12 item 9. | Office add-in to-do pane |

The third credited route owner round 12 item 9 names — `POST /api/v1/external-access/close-project`'s
`RemoveAllExternalMembersAsync` stripping INTERNAL users (166 amendment (e)) — is already sweep S-39 (Pending
InsufficientDecision, 166); a route carries one waiver, so S-39's reason now also names amendment (e).

## 5. Permanent waivers (89 after round r1; 90 at the first commit) — each basis verified by reading the handler; the reason cites file:line

- **AnonymousByDesign** (14): `GET /healthz`, `GET /healthz/catalog`, `GET /healthz/dataverse`, `GET /healthz/dataverse/crud`, `GET /ping`, `GET /status`, `GET /api/config/client`, `GET /api/config`, `GET /api/office/health`, `POST /api/office/save-debug`, `POST /api/registration/demo-request`, `POST /api/onboarding/consent-callback`, `POST /api/compose/webhooks/spe-doc-changed`, `POST /api/communications/incoming-webhook`. Every one now names a MANDATORY control (rate limit, HMAC, IsDevelopment-only mapping); `/healthz`, `/healthz/catalog` and `/ping` gained their rate limit in round r1 (owner round 12 item 1).
- **ReferenceData** (26): `GET /api/ai/capabilities`, `GET /api/ai/chat/context-mappings`, `GET /api/ai/tools/handlers`, `GET /api/ai/handlers`, `GET /api/ai/handlers/{handlerId}`, `GET /api/ai/model-deployments`, `GET /api/ai/model-deployments/{id:guid}`, `GET /api/ai/nda-standard/clauses/{clauseRef}`, `GET /api/ai/nda-standard/clauses`, `GET /api/ai/scopes/skills`, `GET /api/ai/scopes/knowledge`, `GET /api/ai/scopes/tools`, `GET /api/ai/scopes/actions`, `GET /api/ai/scopes/personas`, `GET /api/ai/chat/context-mappings/standalone`, `GET /api/v1/field-mappings/profiles`, `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}`, `GET /api/navmap/{entityLogicalName}/entityset`, `GET /api/navmap/{childEntity}/{relationship}/lookup`, `GET /api/navmap/{parentEntity}/{relationship}/collection`, `GET /api/v1/external/api/dataverse/metadata/{entityLogicalName}`, `GET /api/v1/external/api/dataverse/savedquery/{savedQueryId:guid}`, `GET /api/v1/external/api/dataverse/savedqueries/{entityLogicalName}`, `GET /api/office/search/matter-types`, `GET /api/workspace/sections`, `GET /api/workspace/templates`
- **CallerScopedOnly** (31): `GET /api/ai/chat/event-rules/opt-out`, `PUT /api/ai/chat/event-rules/opt-out`, `GET /api/ai/chat/sessions`, `POST /api/ai/daily-briefing/render`, `POST /api/ai/daily-briefing/email`, `GET /api/ai/playbooks/runs/{runId:guid}`, `GET /api/ai/playbooks/runs/{runId:guid}/stream`, `POST /api/ai/playbooks/runs/{runId:guid}/cancel`, `GET /api/ai/playbooks/runs/{runId:guid}/detail`, `POST /api/communications/threads/direct`, `GET /api/v1/external/me`, `GET /api/v1/external/me/entitlements`, `GET /api/users/me/memberships/{entityType}`, `GET /api/memory/user`, `POST /api/memory/user/seed`, `DELETE /api/memory/user/{itemId}`, `DELETE /api/memory/user`, `GET /api/memory/pins`, `POST /api/notifications/negotiate`, `GET /api/notifications/pending`, `POST /api/notifications/{outboxRowId:guid}/dismiss`, `GET /api/me`, `GET /api/reporting/status`, `GET /api/workspace/portfolio`, `GET /api/workspace/health`, `GET /api/workspace/briefing`, and (owner round 12 item 6, moved from ReferenceData) `GET /api/ai/playbooks`, `GET /api/ai/playbooks/public`, `GET /api/ai/playbooks/templates`, `GET /api/ai/chat/playbooks`, `GET /api/agent/playbooks`
- **OwnerComparison** (1, new basis — owner round 12 item 4): `POST /api/compose/document/{documentId:guid}/heartbeat` (was CallerScopedOnly). `DELETE /api/memory/pins/{pinId}` (was CallerScopedOnly) does not meet it yet and is Pending 166 (§4 row 12).
- **CallerSuppliedContentOnly** (15): `POST /api/ai/daily-briefing/summarize`, `POST /api/ai/daily-briefing/narrate`, `POST /api/ai/rag/embedding`, `POST /api/communications/draft`, `POST /api/compose/project`, `POST /api/compose/documents/{documentId:guid}/checkout`, `POST /api/compose/documents/{documentId:guid}/checkin`, `POST /api/v1/field-mappings/validate`, `POST /api/workspace/calculate-scores`, `POST /api/workspace/events/{id:guid}/scores`, `POST /api/workspace/files/extract-text`, `POST /api/workspace/files/summarize`, `POST /api/workspace/matters/pre-fill`, `POST /api/workspace/matters/ai-summary`, `POST /api/workspace/projects/pre-fill`
- **CreateWithNoPriorResource** (1): `PUT /api/obo/me/files/{*path}`
- **OperatorGateInHandler** (1): `GET /api/diagnostics/tenant-container-resolver`

Changes to the task-074 Permanent waivers: `POST /api/v1/documents` ("CREATE") → **UNOWNED-NEW** (the body requires an
existing `ContainerId`, written with no caller check — strict CreateWithNoPriorResource fails); `GET /api/v1/documents`
→ **Pending 166 (S-66)**; the three `/api/office/communications` routes and `GET /api/office/search/entities` →
**verified HandlerDecisions** (waivers deleted); `PUT /api/obo/me/files/{*path}` stays CreateWithNoPriorResource;
`GET /api/office/search/matter-types` stays ReferenceData. No Pending waiver was converted to Permanent.

## 6. HandlerDecisions (43) — verified in source by `EveryHandlerDecisionIsVerified`

| Route | Handler | Seam | Hops |
|---|---|---|---|
| `POST /api/ai/rag/send-to-index` | SendToIndex | AuthorizationService | — |
| `GET /api/communications/{id:guid}/attachments/text` | GetCommunicationAttachmentTextAsync | IImpersonatedCommunicationQuery | CommunicationAttachmentTextService.GetAttachmentTextAsync |
| `GET /api/communications/threads/{threadId:guid}/messages` | GetThreadMessagesAsync | IImpersonatedCommunicationQuery | CommunicationThreadReadService.ReadThreadAsync |
| `GET /api/communications/threads/{threadId:guid}/unread-count` | GetThreadUnreadCountAsync | IImpersonatedCommunicationQuery | CommunicationThreadReadService.GetUnreadCountAsync |
| `GET /api/communications/threads` | ListThreadsAsync | IImpersonatedCommunicationQuery | CommunicationThreadReadService.ListThreadsAsync |
| `GET /api/communications/by-regarding/{entityType}/{id:guid}` | GetCommunicationsByRegardingAsync | IImpersonatedCommunicationQuery | CommunicationThreadReadService.ReadByRegardingAsync |
| `GET /api/communications` | QueryCommunicationsAsync | IImpersonatedCommunicationQuery | CommunicationThreadReadService.QueryCommunicationsAsync |
| `GET /api/communications/queue-feed` | GetQueueFeedAsync | IImpersonatedCommunicationQuery | CommunicationQueueFeedService.GetQueueFeedAsync |
| `POST /api/compose/document/{documentSpeId}/pull-annotations` | PullAnnotations | DownloadFileAsUserAsync | — |
| `POST /api/compose/document/{documentSpeId}/reanchor-annotations` | ReanchorAnnotations | DownloadFileAsUserAsync | — |
| `GET /api/dataverse/savedquery/{savedQueryId:guid}` | GetSavedQueryByIdAsync | IDataversePrivilegeChecker | — |
| `GET /api/v1/external/projects` | GetProjects | GetAccessibleProjectIds | — |
| `GET /api/v1/external/projects/{id:guid}` | GetProjectById | HoldsReadOnProject | — |
| `GET /api/v1/external/projects/{id:guid}/documents` | GetDocuments | HoldsReadOnProject | — |
| `GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/content` | DownloadDocumentContent | HoldsReadOnProject | — |
| `GET /api/v1/external/projects/{id:guid}/todos` | GetTodos | RightsForRoot | — |
| `POST /api/v1/external/projects/{id:guid}/todos` | CreateTodo | RightsForRoot | — |
| `GET /api/v1/external/matters/{id:guid}/todos` | GetMatterTodos | RightsForRoot | — |
| `POST /api/v1/external/matters/{id:guid}/todos` | CreateMatterTodo | RightsForRoot | — |
| `GET /api/v1/external/workassignments/{id:guid}/todos` | GetWorkAssignmentTodos | RightsForRoot | — |
| `POST /api/v1/external/workassignments/{id:guid}/todos` | CreateWorkAssignmentTodo | RightsForRoot | — |
| `POST /api/v1/external/projects/{id:guid}/documents` | UploadDocument | AccessRights | — |
| `GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/versions` | GetDocumentVersions | HoldsReadOnProject | — |
| `GET /api/v1/external/projects/{id:guid}/events` | GetEvents | HoldsReadOnProject | — |
| `POST /api/v1/external/projects/{id:guid}/events` | CreateEvent | AccessRights | — |
| `GET /api/v1/external/projects/{id:guid}/contacts` | GetContacts | HoldsReadOnProject | — |
| `GET /api/v1/external/projects/{id:guid}/organizations` | GetOrganizations | HoldsReadOnProject | — |
| `PATCH /api/v1/external/todos/{id:guid}` | UpdateTodo | RightsForRoot | — |
| `POST /api/v1/external/api/dataverse/fetch` | ExecuteScopedFetchAsync | Tier2ScopeFilterInjector | — |
| `GET /api/v1/external/api/dataverse/record/{entityLogicalName}/{id:guid}` | GetScopedRecordAsync | IsRecordAccessible | — |
| `GET /api/office/communications/by-message-id/{internetMessageId}` | FindByMessageIdAsync | IDataverseUserClient | — |
| `GET /api/office/communications/by-message-id/{internetMessageId}/suggestions` | GetSuggestionsByMessageIdAsync | IDataverseUserClient | — |
| `GET /api/office/communications/{commId:guid}/linked-todos` | GetLinkedTodosAsync | IDataverseUserClient | — |
| `GET /api/office/search/entities` | SearchEntitiesAsync | IImpersonatedCommunicationQuery | OfficeService.SearchEntitiesAsync → OfficeSearchService.SearchEntitiesAsync |
| `GET /api/documents/{documentId}/permissions` | GetDocumentPermissionsAsync | AuthorizationService | — |
| `POST /api/documents/permissions/batch` | GetBatchPermissionsAsync | AuthorizationService | — |
| `GET /api/workspace/layouts` | GetLayouts | WorkspaceLayoutService | — |
| `GET /api/workspace/layouts/default` | GetDefaultLayout | WorkspaceLayoutService | — |
| `GET /api/workspace/layouts/{id:guid}` | GetLayoutById | WorkspaceLayoutService | — |
| `POST /api/workspace/layouts` | CreateLayout | WorkspaceLayoutService | — |
| `PUT /api/workspace/layouts/{id:guid}` | UpdateLayout | WorkspaceLayoutService | — |
| `DELETE /api/workspace/layouts/{id:guid}` | DeleteLayout | WorkspaceLayoutService | — |
| `GET /api/me/capabilities` | inline | ForUserAsync | SpeFileStore.GetUserCapabilitiesAsync → UserOperations.GetUserCapabilitiesAsync |

## 7. Credit lists, pins and Rule B

- **CreditedForms (20)**: exactly the POML's allow-list, each read and cited. `.AddSessionOwnershipFilter()` and
  `.AddDelegationRuleFilter()` are newly credited; `.AddDataverseAuthorizationFilter(...)` is credited ONLY for
  `EntitySource.FromRouteValue` (the form key includes the first argument). An entry no route attaches fails.
- **NonDecidingAttachments (12)** with owners: AnalysisRecord 162 · AiAuthorizationFilter (both forms) 164 ·
  CommunicationAuthorizationFilter 161 · Dataverse record + FetchXML modes 160 · TenantAuthorizationFilter 163 ·
  AgentAuthorizationFilter 164 · SpeAdminTenantScopeFilter 165 · Workspace / CallerPrincipal / Reporting "-". Each is
  pinned by a `[Theory]` case reading the real filter source.
- **NotAuthorizationForms (10)**: `.AddTenantEnvironmentRoutingFilter` was named in the POML but **no route attaches
  it**, so it is not listed (an unused entry fails); `.WithMetadata(new RequestSizeLimitAttribute)` was added (the
  scanner treats `.WithMetadata` as authorization-shaped because it can carry an authorize attribute).
- **Authorization-shaped** is deliberately WIDER than the POML's four shapes: any `.Add*Filter*`, `.AddEndpointFilter`,
  `.Require*`, `.WithMetadata`, or a name containing "Authoriz"/"Anonymous" must be classified.
- **AdminOnlyRoutes**: 83 routes in 24 file groups (SPE admin 68 incl. the six sweep I keys; Jobs 6; MembershipAdmin 2;
  RAG 3; Precedent admin 2; Registration 2).
- **CallerContextSeams (10)**: IDataverseUserClient, IImpersonatedCommunicationQuery, DataverseImpersonation,
  HoldsReadOnProject, RightsForRoot, GetAccessibleProjectIds, IsRecordAccessible, Tier2ScopeFilterInjector,
  DownloadFileAsUserAsync (OBO SPE download), ForUserAsync (OBO Graph client). No general service type admitted
  (escalation trigger 5 did not fire).
- **Rule B verdict changes** (whole-identifier tokens over code with comments AND literals blanked; old = substring over
  comment-stripped text):

| Filter | Before | After |
|---|---|---|
| `PlaybookAuthorizationFilter.cs` | PASS — only because "AccessRights" is a substring of `PlaybookAccessRights` | would FAIL → added to ClaimOnlyFilters as **OWNER-COMPARISON** (owner/public/shared, `:125-147`), the JobOwnershipFilter pattern |
| `AiAuthorizationFilter.cs` | PASS via the substring "AuthorizationService" ⊂ `IAiAuthorizationService` | PASS via the new `IAiAuthorizationService` token (Rule A no longer credits it — NonDeciding) |
| `AnalysisAuthorizationFilter.cs` | PASS via the same substring | PASS via `IAiAuthorizationService` (its DocumentAccess path, `:139`) |
| `VisualizationAuthorizationFilter.cs` | PASS via the same substring | PASS via `IAiAuthorizationService` (`:228`) |
| `SessionOwnershipFilter.cs` | not inspected | inspected; OWNER-COMPARISON ClaimOnly entry |
| `DelegationRuleFilter.cs` | not inspected | inspected; PASS (`CallerRecordAccessProbe`, `AccessRights`) |

  ClaimOnly reasons corrected for `CommunicationAuthorizationFilter` (no per-record scoping elsewhere — sweep) and
  `AgentAuthorizationFilter` (the gateway DOES serve document data — S-18, S-19). `KnownDecorativeFilters` stays empty.

## 8. Landed-fix reconciliation and conflict check

- **Landed fixes**: none. `git log` on this branch has no 159-166 commit; the local branches `task/uac-r2-159`, `-160`,
  `-161` exist with **no commits** ahead of `work/unified-access-control-r2`. Every ledger entry is `ResolvedBy = null`.
- **/conflict-check** (2026-10-03): no open PR touches `RouteAuthorizationGuardTests.cs`; master has not changed it since
  the base. Soft warn: tasks 159-166 run in parallel on the same hot file by owner decision (round 9 item 2); per 167's
  amendment they record "Route authorization ledger input" in their notes and the main session folds it into the
  ledger at integration. The data split (§1) keeps those edits on `.Ledger.cs`.

## 9. Interpretations and decisions — ANSWERED by owner round 12 (2026-10-03)

The first commit recorded these readings for confirmation instead of firing escalation trigger 2 for items 1-2 and
the CallerScopedOnly cases (verifier finding 5: that should have been a stop). Owner round 12 answered every one; the
code now follows the answers (§15).

1. **AnonymousByDesign "mandatory compensating control"** — the first commit accepted "a response fixed in source" for
   `GET /healthz`, `GET /healthz/catalog` and `GET /ping`. **Owner round 12 item 1: the strict rule is kept; rate-limit
   the three.** Done (`EndpointMappingExtensions.cs:68`, `:76`, `:124`); the widening is removed from the waiver
   procedure text.
2. **ReferenceData "takes no record id" = "selects nothing by a record id"** (catalog keys, the static model-deployment
   id, NDA `{clauseRef}`, the context-mapping cache key, the saved-query lookup). **Owner round 12 item 2: accepted.**
   The waiver procedure text now says so.
3. **HandlerDecision body = the method + same-type helpers it calls directly (one level).** **Owner round 12 item 3:
   accepted.** (Round r1 also stops the SIGNATURE counting as body — §15 item 1.)
4. **Daily briefing render/email are CallerScopedOnly.** **Owner round 12 item 5: as recorded.**
5. **Owners from amendments** (`GET /healthz/dataverse/doc/{id}` → 166 (a); thread/message writes → 161; Compose save +
   create-on-save → 166 (d); reporting → 166 (f)). **Owner round 12 item 8: they stay owned by 161/166** — the deviation
   from the AC's literal "UNOWNED-NEW" is accepted.
6. **Playbook lists** (`/api/ai/playbooks`, `/public`, `/templates`, `/api/ai/chat/playbooks`, `/api/agent/playbooks`)
   were ReferenceData. **Owner round 12 item 6: CallerScopedOnly**, AND the oid-vs-systemuserid mismatch in the
   user-list `_ownerid_value` filter (`PlaybookService.cs:318`; and `PlaybookAuthorizationFilter.cs:125` if the same) is a
   code fix owned by **task 164**. Re-classified; the 164 assignment is for the main session to add to 164's POML.
7. **Direct thread** (`POST /api/communications/threads/direct`) is CallerScopedOnly. **Owner round 12 item 7: as
   recorded.**
7a. **Owner comparisons on a caller-chosen id** (heartbeat, DELETE pin — classified CallerScopedOnly at the first
   commit). **Owner round 12 item 4: a new OWNER-COMPARISON basis**, and DELETE pin must answer one uniform 404 (code
   fix owned by task 166). Heartbeat → Permanent `OwnerComparison`; DELETE pin → Pending 166 (§4 row 12).
   `POST /api/notifications/{outboxRowId:guid}/dismiss` and `DELETE /api/memory/user/{itemId}` stay CallerScopedOnly:
   neither ever resolves the caller-chosen id outside the caller's own keyed set (`GetPendingAsync(systemUserId)` then a
   membership test, `NotificationsEndpoints.cs:218-224`; a store partitioned by the caller's subject key,
   `MemoryGovernanceEndpoints.cs:278-279`), so there is no other principal's record to compare against.
8. **The SystemAdmin policy** passes any token whose scope CONTAINS "admin" (`AuthorizationModule.cs:371-373`;
   escalation trigger 6 — recorded, credit kept as specified). Task 165's amendment fixes the policy.

## 10. Not flagged, but worth the main session's attention (credited routes — the guard cannot see these)

- **Admin-credited (task 165 amendment)**: `/api/spe/environments*` (5) and `/api/spe/dashboard/*` (2) have no BU
  scoping; `GET /api/spe/bulk/{operationId}/status` has no starter check; `SpeAdminTenantScope.CanAccessConfigAsync`
  fails OPEN (`Services/SpeAdmin/SpeAdminTenantScope.cs:99-122`); `POST /api/spe/containertypes/{typeId}/register`
  sends an app token to a caller-supplied host; the SystemAdmin "admin" substring.
- **`PlaybookAuthorizationFilter.cs:125`** compares `playbook.OwnerId` (a systemuserid) with the Entra oid — probably
  denies every owner (fail closed). Same mismatch in `PlaybookService.ListUserPlaybooksAsync` (`_ownerid_value eq {oid}`,
  `PlaybookService.cs:318`): the user's own list is likely always empty.
- **`POST /api/ai/chat/sessions/{sessionId}/documents/from-document`** answers 404/422 before its in-handler check (an
  existence/type oracle); **`.../gates/{gateId}/resolve`** re-checks no target at approve time.
- **`EntityAccessFilter`** passes through when `SaveRequest.TargetEntity` is null (`EntityAccessFilter.cs:234-244`).
- ~~**`POST /api/office/todo`**, **`POST /api/v1/external-access/revoke`**, **close-project** internal-user strip~~ —
  now TRACKED by the guard (owner round 12 item 9): Pending InsufficientDecision waivers owned by 166 (§4 rows 13-14;
  S-39's reason for close-project). Office **suggestions** derived flags over the untrimmed set (161 amendment) remain
  untracked here.
- **`POST /api/{matters|projects}/{id}/recalculate-grades`** writes six fields on a Read decision; **`POST
  /api/ai/analysis/create`** creates an analysis on Read; **unsecure-project / provision-project / invite** (sweep
  noteworthy, external-membership slice).
- **`POST /api/ai/rag/enqueue-indexing`** (RagApiKey): the key holder chooses the tenant partition.
- **`GET /api/communications/queue-feed`** (HandlerDecision): the app-only proposal read is keyed by visible
  communications, but returns target record ids/fields of proposals — residual worth a look with 161.
- **Functional defects found while reading**: the four playbook-run routes can never find a run started by an EARLIER
  request (Scoped `PlaybookOrchestrationService`, instance `_activeRuns`) — and **sweep S-78** (`GET
  /api/agent/playbooks/status/{jobId:guid}`) reads the same per-request store, so the same lifetime argument would
  refute it; the ledger is closed, so S-78 stays Pending for 164 to resolve or re-assess. `ListTemplatesAsync` is a stub;
  Compose checkout/checkin are stubs (round 10 item 1 candidates); `POST /api/registration/demo-request` answers 409 on
  a duplicate e-mail (an e-mail existence oracle, low).
- **Doc drift outside this task's scope** (main session): `.github/workflows/ci-tier1-blocking.yml:307-318` still names
  `EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` (now `EveryRouteDeclaresHowItIsAuthorized`) — no
  functional impact, the job runs the full suite with no filter since 2026-09-10; `ContainerDocumentAuthorizationFilter.cs:24-26`
  says its method name is load-bearing for "FilterMarker" (retired — credit is by `CreditedForms` now); route
  descriptions in `CommunicationEndpoints.cs` say "Auth-scoped via the endpoint filter (NFR-07)".
- **`.claude/` edits needed by this task**: none. (166 records the bff-deploy §9c edit.)

## 11. Seeding proofs — every new rule shown to fire on the REAL data file, then restored

Method: one violation at a time applied to `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`, the
named rule run, the failure captured, the file restored byte-for-byte and touched. Rules whose seed would need a `src/`
edit (scanner forms, duplicate keys, Rule E's exception, the decorative forms, the pass-through replacement) are proved
by the inline-fixture controls listed after the table, which run on every build.

| # | Rule | Seeded | Result | Failure text (abridged) |
|---|---|---|---|---|
| 1 | census set | remove the GovernedFiles entry for Api/ResilienceEndpoints.cs | FAILED 1 test(s): `TheEndpointFileCensusIsPinned` | `Api/ResilienceEndpoints.cs: registers routes but is NOT in GovernedFiles — classify it (RouteLevelGate, GroupGated or NotMapped) with a reason` |
| 2 | aggregator binding | remove Api/ComposeEndpoints.cs from Aggregators | FAILED 1 test(s): `EveryRouteDeclaresHowItIsAuthorized` | `Api/ComposeActiveDocumentEndpoints.cs:31: Api/ComposeEndpoints.cs:47 binds a route group of its own into Api/ComposeActiveDocumentEndpoints.cs, but it is not a pinned aggregator (see Aggregators) — an unknown aggregator is a ro...` |
| 3 | every-route Rule A | delete the Pending waiver of GET /api/v1/events/{id:guid} (S-13) | FAILED 1 test(s): `EveryRouteDeclaresHowItIsAuthorized` | `GET /api/v1/events/{id:guid} — at Api/Events/EventEndpoints.cs:48 — only signed-in (or nothing): [RequireAuthorization(), RequireRateLimiting]` |
| 4 | anonymous route needs AnonymousByDesign or Pending | delete the AnonymousByDesign waiver of GET /ping | FAILED 1 test(s): `EveryRouteDeclaresHowItIsAuthorized` | `GET /ping — at Infrastructure/DI/EndpointMappingExtensions.cs:115 — ANONYMOUS with no AnonymousByDesign or Pending waiver` |
| 5 | attachment-form census | remove AddIdempotencyFilter from NotAuthorizationForms | FAILED 1 test(s): `EveryAttachmentFormIsClassifiedExactlyOnce` | `Api/Office/OfficeEndpoints.cs:189: .AddIdempotencyFilter (on POST /api/office/save)` |
| 6 | non-credited forms (a pass-through credited) | add AddTenantAuthorizationFilter to CreditedForms | FAILED 2 test(s): `EveryAttachmentFormIsClassifiedExactlyOnce`, `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `AddTenantAuthorizationFilter: listed in CreditedForms AND NonDecidingAttachments — a form belongs to exactly one list` |
| 7 | pass-through pins | point the AddTenantAuthorizationFilter entry at DocumentAuthorizationFilter.cs (a filter that decides) | FAILED 1 test(s): `NonDecidingAttachmentStillPassesThrough` | `The pass-through recorded for .AddTenantAuthorizationFilter (Api/Filters/DocumentAuthorizationFilter.cs) is no longer in the source: if (string.IsNullOrEmpty(requestedTenantId)) return await next(context) (TenantAuthorizationFi...` |
| 8 | whole-identifier tokens | remove IAiAuthorizationService from DecisionServices | FAILED 1 test(s): `NoAuthorizationFilterIsDecorative` | `src\server\api\Sprk.Bff.Api\Api\Filters\AiAuthorizationFilter.cs: references none of the decision services, and neither do all of the endpoint files attaching it: Api/Ai/AdminKnowledgeEndpoints.cs, Api/Ai/AnalysisChatContextEnd...` |
| 9 | admin pin (added) | remove GET /api/admin/jobs from AdminOnlyRoutes | FAILED 1 test(s): `TheSetOfAdminOnlyRoutesIsPinned` | `GET /api/admin/jobs` |
| 10 | HandlerDecision | declare the permissions route with the seam IDataverseUserClient (not the one its handler reaches) | FAILED 1 test(s): `EveryHandlerDecisionIsVerified` | `GET /api/documents/{documentId}/permissions: the seam 'IDataverseUserClient' is not reached in 1 of 1 final body` |
| 11 | stale: NoDecision on a credited route | set S-52 POST /api/ai/analysis/execute to Gap.NoDecision | FAILED 1 test(s): `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `POST /api/ai/analysis/execute (Pending NoDecision, owner 162): STALE — the route is credited (PerResource: AddAnalysisExecuteAuthorizationFilter). If a fix landed, DELETE this waiver (and, for a sweep route, set ResolvedBy + Pr...` |
| 12 | stale: InsufficientDecision fingerprint | change S-73's recorded ObservedMechanisms | FAILED 1 test(s): `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `GET /api/spe/configs/{id:guid} (Pending InsufficientDecision, owner 165): STALE — the route's authorization fingerprint changed from 'AddSpeAdminAuthorizationFilter' to 'AddSpeAdminAuthorizationFilter + AddSpeAdminTenantScopeFi...` |
| 13 | stale: Permanent on a credited route (redundant) | re-label the GET /api/me Permanent waiver onto the credited POST /api/office/save | FAILED 1 test(s): `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `POST /api/office/save (Permanent CallerScopedOnly): REDUNDANT — the route is credited (PerResource). A Permanent waiver on a credited route is a note nobody needs; delete it.` |
| 14 | stale: AnonymousByDesign on a non-anonymous route | change GET /api/memory/pins to AnonymousByDesign | FAILED 1 test(s): `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `GET /api/memory/pins (Permanent AnonymousByDesign): AnonymousByDesign on a route that is not anonymous (None)` |
| 15 | stale: absent route | re-key the GET /api/workspace/templates waiver to a route that does not exist | FAILED 1 test(s): `NoWaiverIsStaleAndEveryWaiverIsWellFormed` | `GET /api/workspace/templatesGONE (Permanent ReferenceData): STALE — the route no longer exists. Delete the waiver; if the route was RE-KEYED, the new key needs its own decision.` |
| 16 | closed basis set (Pending -> Permanent forbidden on a sweep route) | make the S-11 waiver Permanent | FAILED 1 test(s): `TheSweepLedgerIsPinnedAndResolvesOnlyByCredit` | `S-11 POST /api/v1/events/{id:guid}/cancel (owner 159): a sweep route may NEVER carry a Permanent waiver — the sweep proved it unsafe. Resolve it by credit.` |
| 17 | sweep ledger: ResolvedBy without credit | set ResolvedBy + a missing ProofTest on S-12 GET /api/v1/events | FAILED 1 test(s): `TheSweepLedgerIsPinnedAndResolvesOnlyByCredit` | `S-12 GET /api/v1/events (owner 159): ResolvedBy is set, so the route carries no waiver of any kind — delete it` |
| 18 | sweep ledger: re-owning | re-own S-07 POST /api/communications/send's waiver to 166 | FAILED 1 test(s): `TheSweepLedgerIsPinnedAndResolvesOnlyByCredit` | `S-07 POST /api/communications/send (owner 161): an unresolved sweep entry carries exactly one Pending waiver with owner 161 and Gap NoDecision; found owner 166 gap NoDecision` |

Inline-fixture controls (run on every build, cannot go stale): `Scanner_NegativeControl_UnreadableShapesAreUnparseable`
(const path, MapMethods form, unknown aggregator, assigned builder) · `DuplicateKeysAndRuleE_NegativeControl`
(ExternalModuleData without its aggregator collides with `Api/Dataverse/FetchEndpoints.cs`; absolute path on the bound
group fails; the root-builder webhook does not) · `Credit_NegativeControl_DecorativeFormsAreNotCredited` (13
pass-through forms earn no credit; 6 deciding forms do; an unknown `.AddFooAuthorizationFilter()` fails the census at
file:line 2) · `NonDecidingPins_NegativeControl_FailWhenTheFilterDecides` (each of the 12 pins fails on a fixture whose
pass-through is replaced by a decision) · `DecisionTokens_MatchWholeIdentifiersOnly` · `HandlerDecision_NegativeControl_*`
(seam only in a comment, missing handler, unlisted seam, interface hop, one-of-two overloads, three hops) ·
`WaiverRules_NegativeControl_EachShapeFails` (every stale rule + the rate-limiter positive) ·
`SweepLedger_NegativeControl_EachShapeFails` (Permanent on POST /api/v1/events, the old GET /api/v1/documents waiver
restored, S-10 waiver deleted, send re-owned to 166, ResolvedBy with a missing proof method) ·
`SweepWaivers_NegativeControl_RemovalFailsAndChainChangeIsStale` (all 74 N removals fail Rule A naming the route; all 16
I fingerprint changes go stale) · `RuleA_NegativeControl_FiresOnARemovedFilterAndOnSignedInOnly` ·
`Admin_NegativeControl_PinFiresAndOnlyFourMechanismsCount` · `Census_CountsAFileThatRegistersOnlyMapMethodsOrMapHealthChecks`.

## 12. Test runs (project hard gate)

All runs are against the worktree at the task's final file state (after `dotnet format whitespace` on the three guard files).

| Suite | Result | Notes |
|---|---|---|
| Affected: `RouteAuthorizationGuardTests` (filter) | **47 / 47 passed** | 35 Facts + 1 Theory over the 12 NonDecidingAttachments; re-run after formatting |
| `tests/Spaarke.ArchTests` (NetArchTest, full) | **378 / 378 passed**, 0 skipped, 1 m 34 s | re-run on the final state just before commit |
| `Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** | |
| `Spe.Integration.Tests` | **403 passed, 25 skipped, 0 failed** | the 25 skips are the suite's own live-environment skips |
| `Sprk.Bff.Api.Tests` (BFF unit, full) run 1 | 14,157 passed, **55 failed**, 54 skipped of 14,266, 43 m 22 s | console log kept one failure name: `InsightEndpointsContractTests.PostAsk_EndpointRegistered`, `TaskCanceledException` after 2 m 20 s |
| `Sprk.Bff.Api.Tests` (BFF unit, full) run 2, TRX | 14,009 passed, **203 failed**, 54 skipped of 14,266, 1 h 8 m | see the classification below |

**Unit-suite failures are machine contention, not this task.** This is shown three ways:

1. **Every failure is a cancellation.** Of the 203 run-2 failures:
   - 201 are `TaskCanceledException: The operation was canceled`. Each took 2 m 00 s to 14 m 29 s, i.e. it hit the test client's 2-minute timeout.
   - 1 is `IOException: The client aborted the request`, at 6 m 18 s (`OfficeFilingAccessParityContractTests`).
   - 1 is a Compose fidelity-gate case (`alternate-content-duplicate-paraid.docx`) whose recorded failure reason is "unhandled exception during round trip: TaskCanceledException".

   No failure is an assertion on behaviour. Throughout, the machine was running other agents' builds and test hosts: 249-254 `dotnet`/`testhost` processes at 87-88 % CPU. A larger failure count in a longer run (55 failures in 43 min, 203 in 68 min) is the contention signature.
2. **Isolated re-run:** the 73 classes holding all 203 failures.
   - Result: **880 passed, 4 failed, 2 skipped of 886** (27 m 11 s).
   - The 4 failures were again `TaskCanceledException` at 2 m 55 s to 3 m 18 s. They include `SearchItems_WithoutAuthentication_Returns401`, a test that does no work beyond rejecting the call.
   - The 4: `SearchItemsTests.SearchItems_WithToken_WhitespaceQuery_Returns400`, `SearchItemsTests.SearchItems_WithoutAuthentication_Returns401`, `DocumentProfileContractTests.GetDocument_WithNonCompletedStatus_Returns200WithStatusCodeAndNoProfileText(100000006)`, and `OfficeVersionSaveRevertTests.EmailSave_ResentWithNoClientKey_IsStillAnsweredDuplicate_AndReadsNoFile`.
3. **Serial re-run** of those 4 (`xUnit.ParallelizeTestCollections=false`, `MaxParallelThreads=1`): **9 / 9 passed** (the 4 tests, with all 6 parameter cases of the parameterized one), each in 2-46 s.

**Structural argument.** `Sprk.Bff.Api.Tests.csproj` compiles `src/server/api/Sprk.Bff.Api`, `Spaarke.Dataverse`, `tests/unit/**` and `tests/integration/{contract,Shared,auth,regression,seam,tenant,data-mutation}/**`. Nothing under `tests/Spaarke.ArchTests` is compiled into it. The full change set since `baseSha` is three `tests/Spaarke.ArchTests` files, this note and the task POML. So the unit-test assembly that failed is the same code as at `baseSha`.

Publish size was not measured: test-only change, per the harness rule.

## 13. Step 9.5 quality gates

**Code review** (coverage-first; test code only, three partial files):

| Metric | Main (rules+controls) | Scanner | Ledger (data) |
|---|---|---|---|
| Lines | 2,155 (was 2,289 single file) | 1,646 (new) | 2,537 (new) |
| Shape | 35 Facts + 1 Theory (12 cases) = 47 test cases, plus evaluation functions | lexer, parser, binder | lists + records only |

Direction: the 2,289-line single file mixed rules, machinery and data; it is now three cohesive partials, each with one
reason to change (CLAUDE.md §11.5 — evaluated on cohesion, not LOC; the large data file is an exhaustive classification
by design). Findings, none Critical:
- *Warning (high confidence)* — the scanner reads fluent-chain call NAMES only: a custom wrapper extension that hides
  `.AllowAnonymous()` or a filter is invisible. Mitigated (a hidden gate earns no credit = fail closed) and stated in the
  Scanner summary; a hidden anonymity would need a reviewer. Accepted residual. **[Round r1: the anonymity half is now
  CLOSED — `AnonymityIsDeclaredOnlyOnAScannedChain` refuses attribute anonymity and any `.AllowAnonymous()` off a
  scanned chain (§15 item 3).]**
- *Warning (medium)* — three readings of the closed sets need owner confirmation (§9 items 1-3). Escalation trigger 2 was
  not fired because each reading is narrower than a new basis; recorded rather than silent.
- *Warning (medium)* — `HandlerDecision` same-type expansion (§9 item 3) is wider than "two hops" read literally; it is
  pinned by `HandlerDecision_SameTypeExpansion_IsOneLevelAndCalledOnly` and by the overload control.
- *Suggestion* — `CallerContextSeams["DataverseImpersonation"]` is vocabulary no declaration uses yet (task 160 adds the
  SDK entry point there); kept because the POML lists it and seams are not credit.
- *Suggestion* — `ScanFixtures` reports source-set problems as synthetic `<problem>` registrations; contained to fixtures.
- AI-smell scan: no single-implementation interfaces, no log-rethrow, no null checks on non-nullable types (none in
  scope), comments explain why, not what. ADR-038 bans: none present (no mocks, no DI tests, no ctor null checks).
- Security dimension: the guard itself is the security control; its fail-closed branches (unparseable registration,
  unresolved binding, unknown aggregator, duplicate key, unclassified form, unverifiable declaration, unknown basis,
  census mismatch) each have a control.

**ADR check**: ADR-038 (KEEP path A1; every rule has negative + positive controls; maintenance procedures in-file) ✓ ·
ADR-003 (the guard's own decisions fail closed) ✓ · ADR-008 (the guard enforces endpoint-filter authorization; inline
lambdas never credited) ✓ · ADR-002 (no plugin, none suggested) ✓ · ADR-001/007/009/010/013/021/028/052 — not applicable
(no `src/` change). No violations; no §6.5 path needed.

**Placement / §11 justification**: no BFF change, so no §10 placement decision, publish size or CVE impact (test-only;
not measured per the harness rule). New surface = two test source files (justified in the POML `<execution>` block).

## 14. Ledger input

n/a — this task creates the ledger; it resolves no entry and changes no route.

## 15. Verifier round r1 (2026-10-03) and owner round 12 — branch `task/uac-r2-167-r1`

Base `1d1720348` (`task/uac-r2-167`). The adversarial verifier's 15 items, each closed or answered. Owner round 12
(2026-10-03, binding) answered every reading §9 had parked; its nine items are applied as written.

### 15.1 Per verifier item

| # | Item | Disposition | Where |
|---|---|---|---|
| 1 | HandlerDecision credited a seam that appears only in the handler SIGNATURE (an unused DI parameter) | **Closed.** The verified "body" is now the code between the braces / after the lambda arrow, never the signature. The seam counts when it is a whole identifier in the body, or when the body USES a parameter (of the method whose body it is) or a top-level field/property whose declared type is the seam; qualified names (`Spaarke.Core.Auth.AuthorizationService`, `global::`, `?`) still match (`IsSeamType`). An unused seam parameter earns nothing; a named-argument label `f(auth: x)` is not a use. Side effect closed too: the hop-call check used to be satisfied by the handler's OWN name in its signature when it shares the hop method's name. Inline lambdas are split into parameters + body (`LambdaPart`); an unsplittable inline handler is a problem, not a pass. | `RouteAuthorizationGuardTests.cs` HANDLER DECISION VERIFICATION; control `HandlerDecision_NegativeControl_SignatureOnlyAndBorrowedSeamsFail` |
| 2 | The field rule scanned the whole declaring TYPE, so a sibling method's `AccessRights rights` parameter lent its name to a handler's `string rights` | **Closed.** `SeamTypedMembers` reads only TOP-LEVEL member declarations: `TopLevelOf` blanks the contents of every bracketed region of the type body (method parameter lists, bodies, nested types) before matching. | same; the sibling-parameter case is in the control |
| 3 | Attribute-borne anonymity (`[AllowAnonymous]` on a lambda) is invisible | **Closed, fail-closed.** New rule `AnonymityIsDeclaredOnlyOnAScannedChain`: any identifier containing `AllowAnonymous` in BFF + `src/server/shared` code (comments/literals blanked) must be a fluent `.AllowAnonymous()` call on a chain the scanner read (matched by file:line against every scanned route's chain). An attribute, `AllowAnonymousAttribute`, `IAllowAnonymous`, or an `.AllowAnonymous()` in a wrapper extension fails naming file:line. Non-vacuous: asserts the 16 anonymous routes are seen. The Scanner summary's residual text is rewritten (only non-routing middleware remains unseen). | rule + control `UnreadableShapes_NegativeControl_AttributeAnonymityAndUnreadFormsFail` |
| 4 | `.Map(...)`, `MapFallback*`, `MapHub<T>`, `MapControllers` invisible to census and Rule A | **Closed, fail-closed.** New rule `NoRouteIsRegisteredInAFormTheScannerCannotRead`: every `.Map*(` / `.Map*<...>(` call in BFF + shared code must be in the read vocabulary (Map{Verb}, MapMethods, MapHealthChecks, MapGroup) or a method DECLARED under `src/server/**` (the BFF's own `MapXEndpoints`); 24 framework registration names are refused even if something declares a same-named method. The census vocabulary itself is unchanged (the POML's); the new rule refuses rather than counts. Zero hits today. | rule + the same control |
| 5 | Basis widenings recorded as "readings to confirm" instead of firing trigger 2 | **Accepted as a process miss; answered by owner round 12.** AnonymousByDesign: the widening ("a response fixed in source") is REMOVED from the waiver procedure text and the three routes got their mandatory rate limit (item 1). ReferenceData "selects nothing by a record id": accepted (item 2), now written into the procedure text. CallerScopedOnly on heartbeat / DELETE pin: replaced by the new OWNER-COMPARISON basis (item 4). | Ledger waiver procedure; §9 |
| 6 | DELETE pin answers 404 vs 403 — an oracle the round-9 fix pattern forbids — under a Permanent waiver | **Closed.** Now `Pending("166", NoDecision)` (owner round 12 item 4 assigns the uniform-404 code fix to 166, so it is not UNOWNED-NEW; `ExpectedUnownedNewCount` stays 21). Its reason names the in-diff conversion to Permanent `OwnerComparison` when 166 lands the fix (maintenance rule 4's one sanctioned exception, added). | Ledger; §4 row 12 |
| 7 | `GET /healthz/dataverse/doc/{id}` and 11 non-sweep routes owned by 161/166, not UNOWNED-NEW | **Accepted by owner round 12 item 8.** No change. | §0, §4 |
| 8 | Comments contradict code (`NoWaiverIsStale`, "PENDING waivers only", the non-existent `NoWaiverNamesARouteThatNoLongerExists`, "does not catch this case") | **Fixed** — each rewritten as history plus the current rule (`NoWaiverIsStaleAndEveryWaiverIsWellFormed` fails absent routes and REDUNDANT Permanent waivers). Also fixed in the same drift class: two `ExplicitlyCreditedFilterTypeNames` mentions (now `CreditedForms`), the `POST /todo` "NO waiver" history line, and the duplicate-key failure text. The `ci-tier1-blocking.yml:310` and `ContainerDocumentAuthorizationFilter` FilterMarker comments stay recorded in §10 (outside `tests/`). | Ledger comments; main file duplicate-key test |
| 9 | Census negative control appended a synthetic name instead of running the census | **Closed.** The census is now two named functions, `LoadUnits(root)` and `CensusOf(units)`; `EndpointFiles()` is `CensusOf(LoadUnits(BffRoot))`. The control writes a TEMPORARY scan root (a MapMethods-only endpoint file, a route-less file, a commented-out route, an `obj/` file), runs the same two functions, gets exactly the endpoint file, then joins it to the real units: count = pinned + 1 and the set check names it. | `Census_CountsAFileThatRegistersOnlyMapMethodsOrMapHealthChecks`; Scanner `LoadUnits` / `CensusOf` |
| 10-11 | Verified OK | No action. | — |
| 12 | Criterion: seam not required in the declared BODY | = items 1-2. | — |
| 13 | Criterion: DELETE pin basis disproved; widened definitions | = items 5-6. | — |
| 14 | Criterion: /healthz, /healthz/catalog, /ping with no mandatory control | **Closed** by owner round 12 item 1 (rate limits). | `EndpointMappingExtensions.cs:68`, `:76`, `:124` |
| 15 | Criterion: doc route owned by 166 | = item 7 (accepted). | — |

### 15.2 Owner round 12, item by item

| Item | Decision | Applied |
|---|---|---|
| 1 | Rate-limit `/healthz`, `/healthz/catalog`, `/ping`; the strict AnonymousByDesign rule is kept | `.RequireRateLimiting("anonymous")` added (the existing policy the four sibling probes use: 10/min per client IP, `RateLimitingModule.cs:95-104`). The three waivers cite it; `NamedRoutes_CarryTheirRecordedClassification` pins it on the chain. **The only `src/` change in task 167.** |
| 2 | ReferenceData = selects nothing by a record id: accepted | Procedure text + the two dependent reasons cite item 2. |
| 3 | HandlerDecision body may include same-type helpers one level deep: accepted | Kept; the body is now body-only (verifier item 1). |
| 4 | New OWNER-COMPARISON basis; DELETE pin uniform 404 is 166's code fix | `PermanentBasis.OwnerComparison` with its definition (a uniform answer is required). Heartbeat → `OwnerComparison` (`DocumentCheckoutService.cs:489`; one 404 at `ComposeCheckoutEndpoints.cs:104-109`). DELETE pin → Pending 166. |
| 5 | Daily briefing render/email: CallerScopedOnly | Unchanged. |
| 6 | Playbook lists: CallerScopedOnly; fix the oid-vs-systemuserid user-list filter (and PlaybookAuthorizationFilter if the same) in task 164 | The five waivers moved ReferenceData → CallerScopedOnly with reasons naming the 164 fix. **Main session:** add the fix to task 164's POML (`PlaybookService.cs:318` `_ownerid_value eq {oid}`; `PlaybookAuthorizationFilter.cs:125` `playbook.OwnerId == userId`). |
| 7 | `POST /api/communications/threads/direct`: CallerScopedOnly | Unchanged. |
| 8 | Non-sweep findings named in 161/166 amendments stay with 161/166 | Unchanged. |
| 9 | Credited routes 166 must fix get Pending InsufficientDecision waivers owned by 166 | `POST /api/v1/external-access/revoke` (observed `AddDelegationRuleFilter`) and `POST /api/office/todo` (observed `AddTodoSourceAccessFilter`) added; close-project already carries S-39 (Pending InsufficientDecision 166) — a route has one waiver, so S-39's reason now also names amendment (e). The AC line "revoke: no waiver" is superseded by this item; the named-routes test asserts the new state. |

No escalation trigger remained unanswered; nothing was stopped.

### 15.3 Placement and justification for the `src/` change (CLAUDE.md §10/§11)

Three `.RequireRateLimiting("anonymous")` calls on EXISTING routes in `Infrastructure/DI/EndpointMappingExtensions.cs`.
No new endpoint, service, DI registration, option, rate-limit policy, job, column or package — it reuses the existing
`anonymous` policy, so the §11 three-question template (which applies to NEW surface) is not triggered, and the
placement is the routes' own file. Publish size: not measured (harness rule; a three-line change to an existing file).
No Dataverse plugin (ADR-002). Tests: the guard pins the control on the chain; `HealthAndHeadersTests` (unit) and
`SystemIntegrationTests` (Spe.Integration) still call `/healthz` and `/ping` and pass (below).

**Operational note for the main session (not a stop — owner item 1 decided the control).** `anonymous` is a fixed
window of 10 requests/min per client IP with no queue. The App Service health check (1/min/instance) and swap warm-up
are well under it, but `deploy-bff-api.yml`'s staging and production `/healthz` loops poll 12 times at 5 s: if the app
answers Unhealthy for the first ten polls of a window and Healthy only on poll 11-12, those two get 429 and the step
fails where it previously passed. ~~`Deploy-BffApi.ps1` (24 x 5 s) treats a 429 as a retry and spans two windows, so it
is unaffected.~~ **Corrected in r2 (§16.3):** `Deploy-BffApi.ps1` and the control plane's H9 probe (both 24 x 5 s)
treat a 429 as a retry, but their FINAL window has the same edge (polls 23-24). If that edge matters, the remedy is a
looser dedicated probe policy — a new registration needing its own §11 justification, so it is not added here; r2
puts the choice to the owner (§16.3).

**SUPERSEDED in round f1 — use the row in §17.8 instead** (the probes are now on the 120/min `health-probe` policy,
so "poll at most every 6 s" is no longer the advice).

**`.claude/` edit for the main session (main-session-only path), exact text** — append a row to the troubleshooting
table in `.claude/skills/bff-deploy/SKILL.md` (the table that holds the "Health check fails at 60s" row):

```
| `/healthz` or `/ping` returns 429 during manual polling | Both are rate-limited like every anonymous probe ("anonymous": 10/min per client IP; owner round 12 item 1, UAC-r2 task 167) | Poll at most every 6 s, or wait for the next minute. A 429 is not a failed deploy. |
```

### 15.4 /conflict-check (round r1)

`src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` (BFF hot path) and the three guard files,
checked 2026-10-03: no open PR (19 checked) touches any of them; `origin/master` has not changed them since the merge
base `b8026dfa8`. Local sibling branches: `task/uac-r2-160` and `-161` edit `RouteAuthorizationGuardTests.cs` (their
census/ledger lines — the main session reconciles at integration, by owner round 9 item 2); none edits
`EndpointMappingExtensions.cs` yet. **Soft warn:** task 166 amendment (a) will edit the same file at the
`/healthz/dataverse/doc/{id}` registration (`:88-121`), adjacent to this round's lines 68 / 76 / 124 — expect a trivial
textual merge.

### 15.5 Seeding proofs (round r1) — each seeded, run, failure captured, restored and touched

| # | Rule | Seeded (temporary; restored with `git checkout` or a byte-exact backup) | Result |
|---|---|---|---|
| r1-1 | HandlerDecision body-only (item 1), REAL code | `PermissionsEndpoints.GetDocumentPermissionsAsync`: the `authorizationService.GetCallerAccessAsync(...)` call replaced by `Task.FromResult<AccessSnapshot>(null!)`, the unused `AuthorizationService` parameter KEPT (the verifier's seed) | 4 failed: `EveryHandlerDecisionIsVerified` — "GET /api/documents/{documentId}/permissions: the seam 'AuthorizationService' is not reached in 1 of 1 final body"; `EveryRouteDeclaresHowItIsAuthorized` names the route; the two controls that assert the real route is credited. (Before r1 this seed stayed green.) |
| r1-2 | Attribute anonymity (item 3), REAL code | `[Microsoft.AspNetCore.Authorization.AllowAnonymous]` on `UserEndpoints.GetCurrentUserAsync` (GET /api/me) | 1 failed: `AnonymityIsDeclaredOnlyOnAScannedChain` — "Api/UserEndpoints.cs:73: AllowAnonymous used as an ATTRIBUTE (or other non-call) — the route would scan as signed-in and escape the AnonymousByDesign rule" |
| r1-3 | Unread registration forms (item 4), REAL code | `app.MapFallback(() => Results.NotFound());` in `MapSpaarkeEndpoints` | 1 failed: `NoRouteIsRegisteredInAFormTheScannerCannotRead` — "Infrastructure/DI/EndpointMappingExtensions.cs:40: .MapFallback(...) — a route registration form the scanner does not read ..." |
| r1-4 | Census (item 9), REAL code | a new `Api/ZzSeedR1Endpoints.cs` registering only `MapMethods(["PATCH"])` | 2 failed: `TheEndpointFileCensusIsPinned` — "expected 120, found 121"; the census control (real + temp root = 122) |
| r1-5 | Owner item 1 pin | `.RequireRateLimiting("anonymous")` removed from `/ping` | 1 failed: `NamedRoutes_CarryTheirRecordedClassification` (the /ping chain lacks the rate limit) |
| r1-6 | Owner item 4 pin | DELETE pin waiver put back to Permanent CallerScopedOnly | 1 failed: `NamedRoutes_...` — expected (Pending, "166", NoDecision), actual (Permanent, "167", None) |
| r1-7 | Owner item 9 fingerprint | `POST /api/office/todo` waiver's ObservedMechanisms changed | 1 failed: `NoWaiverIsStaleAndEveryWaiverIsWellFormed` — "STALE — the route's authorization fingerprint changed from 'AddTodoSourceAccessFilter + AddEntityAccessFilter' to 'AddTodoSourceAccessFilter'" |

Finding 2 (the sibling-parameter borrow) cannot be seeded on real code without inventing a handler, so it is proved
by the inline negative control, which runs on every build. After the last restore, `git status` shows only the four
intended files changed.

### 15.6 Test runs (round r1, final state)

| Suite | Result |
|---|---|
| Affected: `RouteAuthorizationGuardTests` | **51 / 51 passed** (47 + 4 new: `HandlerDecision_NegativeControl_SignatureOnlyAndBorrowedSeamsFail`, `AnonymityIsDeclaredOnlyOnAScannedChain`, `NoRouteIsRegisteredInAFormTheScannerCannotRead`, `UnreadableShapes_NegativeControl_AttributeAnonymityAndUnreadFormsFail`) |
| `tests/Spaarke.ArchTests` (NetArchTest, full) | **382 / 382 passed**, 0 skipped (4 m 23 s) |
| `Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `Spe.Integration.Tests` | **403 passed, 25 skipped, 0 failed** (7 m 38 s) — includes `SystemIntegrationTests` hitting the now rate-limited `/ping` |
| `Sprk.Bff.Api.Tests` (BFF unit, full, TRX) | **14,202 passed, 10 failed, 54 skipped** of 14,266 (38 m 41 s). All 10 are `TaskCanceledException` ("Error while copying content to a stream") after 2 m 28 s - 3 m 37 s — the client-timeout contention signature of §12, under the same multi-agent load; none is an assertion, none is a 429. The 10: `ComposeAuthoredWarningSuppressionTests.ImportedDocument_WithARealFidelityLoss_StillWarns`, `SearchItemsTests.SearchItems_MissingConfigId_Returns401WithoutToken`, `ComposeTransientKeyDedupSeamTests.SaveNew_ForkNew_SkipsTransientKeyDedup_ForksNewRecord_ThroughTheWire`, `DocumentProfileContractTests.GetDocument_WithNonCompletedStatus_Returns200WithStatusCodeAndNoProfileText(100000003)`, `DocumentIdentityContractTests.ResolveIdentity_WhenUnauthenticated_Returns401`, `SearchItemsTests.SearchItems_WithoutAuthentication_Returns401`, `DocumentProfileContractTests.GetDocument_WithCompletedProfile_Returns200WithAllFourFields`, `PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter`, `InsightsAssistantEndpointContractTests.Post_MissingQuery_Returns400_QueryRequired`, `InsightsSearchEndpointContractTests.PostSearch_FacadeThrows_Returns500ProblemDetails`. **Isolated re-run of those 10 (15 cases with the theory's parameters): 15 / 15 passed** (43 s). `HealthAndHeadersTests` (which calls the newly rate-limited `/healthz` and `/ping`) passed in the full run. |

Test scope beyond the listed behaviours: none. ADR-038 bans: no `Mock<HttpMessageHandler>`, no DI-registration test, no
constructor null-check test in anything added. No test calls Dataverse or Azure (the census control writes and deletes
a temporary directory only).

## 16. Verifier round r2 (2026-10-03) — branch `task/uac-r2-167-r2`

Base `449d3e8dd` (`task/uac-r2-167-r1`). The r2 verifier's 11 items, each closed, verified-as-not-open, or (item 5)
narrowed with live evidence and put to the owner. **No `src/` file changes in r2**; no waiver changes (214 = 125
Pending + 89 Permanent, unchanged; `ExpectedUnownedNewCount` stays 21).

### 16.1 Per verifier item

| # | Item | Disposition | Where |
|---|---|---|---|
| 1 | Verification run (ArchTests 382/382, both integration suites, 47/47 affected unit tests) | **Verified, not open.** Informational. | — |
| 2 | **FAIL-OPEN**: a route with neither `.RequireAuthorization(...)` nor `.AllowAnonymous()` on its route or group chain is callable WITHOUT SIGNING IN (the BFF sets no FallbackPolicy / DefaultPolicy), yet scanned as non-anonymous and passed under a Permanent ReferenceData waiver | **Closed.** New rule `NoRouteIsAnonymousByOmission`: every LIVE route's effective chain (own calls + every group's, aggregator groups and group-continuation statements included) carries `.RequireAuthorization(...)` in some form, or a declared `.AllowAnonymous()` (which Rule A already sends to AnonymousByDesign / Pending). **No waiver is consulted** — the rule is not satisfiable by a waiver, so the seeded shape cannot pass under any basis. An `[Authorize]` attribute or `WithMetadata(new AuthorizeAttribute())` is not read and fails (fail closed; declare it on the chain). Every policy `RequireAuthorization` names today requires a signed-in caller (bare = framework default authenticated user; `AuthorizationModule.cs:331/:342/:358/:364` call `RequireAuthenticatedUser`; `ResourceAccessHandler.cs:39` fails without a user id), and a NEW policy name is an unclassified attachment form until reviewed. The real code: **zero** violations (matches the verifier's probe). Controls: `AnonymousByOmission_NegativeControl_FiresEvenUnderAWaiver` — the verifier's exact seed (`GET /api/zzref` + a ReferenceData waiver) shown to pass Rule A and every waiver rule (the pre-r2 hole) and then to fail the new rule; a credited filter with no sign-in, an `[Authorize]` lambda, `WithMetadata(new AuthorizeAttribute())` and a rate-limit-only route each fail; six declared shapes pass (route-level, group-level, nested group, a `g.RequireAuthorization();` continuation statement, a named policy, an explicit `.AllowAnonymous()`). Design choice: a separate rule rather than reclassifying implicit anonymity as `Credit.Anonymous` — reclassification would have let an AnonymousByDesign or Pending waiver cover an UNDECLARED public route, contradicting AnonymousByDesign's definition ("`.AllowAnonymous()` declared"), and would have changed six historical fixtures' expected credit. | `RouteAuthorizationGuardTests.cs` (rule, Fact, control; class summary; the "three shapes that would fail open" block); Scanner summary; Ledger AnonymousByDesign definition + admin-mechanism comment + state line |
| 3 | The verifier's ten seeds, all detected and restored | **Verified, not open.** No change. | — |
| 4 | Ledger independently checked (90 sweep entries = POML ledger; 125 Pending / 89 Permanent; per-owner counts) | **Verified, not open.** No change. | — |
| 5 | `src/` change and deploy risk of the shared `"anonymous"` policy on `/healthz`, `/healthz/catalog`, `/ping` | **Narrowed with live read-only evidence; the policy choice is the OWNER's — not closed here** (§16.3). The verifier's conditional "if App Service does not forward the client IP" is **disproved** for the deployed Linux app; the one remaining edge (5-second pollers losing their last two polls of a window) is quantified, a wrong r1 claim is corrected (§15.3), and three options are put to the owner with a recommendation. | §16.3 |
| 6 | POML `<parallel-reason>` still said "changes no file under src/"; r1 comment "every anonymity is an `.AllowAnonymous()` call" omitted the no-RequireAuthorization shape | **Closed.** `<parallel-reason>` amended (one `src/` file, by owner round 12 item 1; not a parallel-unsafe zone; 163 and 166(a) edit other regions of it). The comment block is rewritten as "the THREE shapes that would fail open" and now states the omission shape and its rule. | POML; `RouteAuthorizationGuardTests.cs` |
| 7 | ADR-038 bans, no live calls, POML well-formed, consumers unbroken | **Verified, not open.** The r2 additions are inline-fixture source analysis only (no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check, no file or network I/O). | — |
| 8 | Heartbeat OwnerComparison basis | **Verified, not open.** No change. | — |
| 9 | Criterion "Anonymous routes (intent)" + ADR-003 fail-closed on the guard's own decisions | **Closed** by item 2: an effectively-anonymous route can no longer exist in a green build, so every anonymous route is a DECLARED `.AllowAnonymous()` that the scanner classifies `Anonymous` and Rule A sends to AnonymousByDesign / Pending. | as item 2 |
| 10 | Criterion "Scope: no file under src/ changed" vs. the three rate-limit lines | **Closed for traceability.** The criterion carries a `[SUPERSEDED in part by owner round 12 item 1 ...]` annotation; `<parallel-reason>` amended (item 6). | POML |
| 11 | Criteria "revoke: no waiver" and "/healthz/dataverse/doc/{id} Pending UNOWNED-NEW" vs. the code | **Closed for traceability.** Both criteria carry `[SUPERSEDED by owner round 12 item 9 / item 8 ...]` annotations. `NamedRoutes_CarryTheirRecordedClassification` already asserted revoke's Pending InsufficientDecision 166; r2 adds `Assert.Equal("166", healthDoc.OwningTask)` so the doc route's superseded OWNER is pinned too (it asserted Pending/NoDecision/anonymous, not the owner). | POML; `RouteAuthorizationGuardTests.cs` named-routes test |

### 16.2 Seeding proofs (r2) — each seeded on REAL source, run, failure captured, restored and touched

| # | Rule | Seeded | Result |
|---|---|---|---|
| r2-1 | `NoRouteIsAnonymousByOmission` (item 2) — the verifier's own seed | `app.MapGet("/api/zzref", () => Results.Ok());` in `Api/UserEndpoints.cs` `MapUserEndpoints` + `Permanent("GET /api/zzref", PermanentBasis.ReferenceData, ...)` in `.Ledger.cs` | **1 failed / 52 passed**: only the new rule — "GET /api/zzref / at Api/UserEndpoints.cs:17 — neither .RequireAuthorization(...) nor .AllowAnonymous() on its route or group chain []: it is callable WITHOUT SIGNING IN". Every other rule stayed green on this seed, which is the hole the verifier described. |
| r2-2 | Same rule, GROUP inheritance through an aggregator + a route-level removal | `.RequireAuthorization()` removed from the `/api/compose` group in `Api/ComposeEndpoints.cs` (the aggregator) AND from `GET /api/me` in `Api/UserEndpoints.cs` | **1 failed / 52 passed**: the rule names 18 routes — all 17 Compose routes bound to the group, plus `GET /api/me`; the Compose webhook (declared `.AllowAnonymous()` on the ROOT builder) is correctly not named. |
| r2-3 | Named-routes owner pin (item 11) | `GET /healthz/dataverse/doc/{id}` waiver owner `"166"` → `"UNOWNED-NEW"` | **1 failed**: `NamedRoutes_CarryTheirRecordedClassification` — `Expected: "166"`, `Actual: "UNOWNED-NEW"`. |

After the last restore, `git status` shows only the intended files changed (the three guard files, the POML, this
note). One restore needed care: the r2-1 ledger seed was written with LF endings; the file was restored to its CRLF
endings byte-for-byte (2,612 CRLF lines, `zzref` absent) before the next run.

### 16.3 Item 5 — the rate-limit policy on the liveness probes: evidence, and the owner question

**Evidence gathered (all read-only).**

1. **The partition key IS the real client IP on the deployed app.** The rate limiter's own rejection log
   (`RateLimitingModule.cs:272-277` logs `Connection.RemoteIpAddress`) on `spaarke-bff-dev` (Linux App Service, the
   stack `infrastructure/bicep/modules/app-service.bicep:104` and `deployment-slot.bicep:112` deploy everywhere)
   recorded `IP: 47.198.16.39` — a public ISP address — on 2026-10-02T19:57:54Z (App Insights
   `spe-insights-dev-67e2xz`, the only rejection in 30 days). So external callers do NOT share a few front-end IPs and
   the per-IP budget is not "close to global". (The BFF has no ForwardedHeaders code; whatever delivers the client
   address, the observed value is the client's. Microsoft documents no automatic forwarded-headers wiring for Linux —
   [proxy-load-balancer](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) —
   which is why this was measured rather than assumed.)
2. **Legitimate probe volume is far below the budget, even summed over ALL callers.** App Insights `requests` on
   `spaarke-bff-dev`, 30 days (`client_IP` is masked to 0.0.0.0, so these are GLOBAL per-minute counts — an upper
   bound for any one IP): `/healthz` 42,984 requests, **max 3 in any minute, 0 minutes over 5**, codes {200, 503};
   `/ping` 9 (max 1/min); `/api/config` 65 (max 2/min); `/api/office/health` 4; `/status` 1. No anonymous probe ever
   approached 10 in a minute.
3. **Every caller, against a fixed window of 10 per minute per IP:**
   - App Service health check (`healthCheckPath=/healthz`): 1/min/instance — safe (item 2's data).
   - Slot-swap warm-up (`WEBSITE_SWAP_WARMUP_PING_PATH=/healthz`, `_STATUSES=200`): one internal request per
     instance, retried only on timeout ([deploy-staging-slots](https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots)).
     A 429 there WOULD stop the swap ("If the returned status code isn't in the list, the warm-up and swap operations
     are stopped"), but it needs ten requests from the internal warm-up address inside one minute, which this cadence
     never produces — safe.
   - `deploy-promote.yml`: `/ping` every 15 s (≤ 5/min), then one `/healthz` and one `OPTIONS /ping` — safe.
   - Control plane H4b (`HttpHealthzProbe`: 30/60/90/120/180 s backoff) and H13 (`E2EValidationRunner`: one `/healthz`
     + one `/ping`) — safe.
   - **5-second pollers — the one real edge:** `deploy-bff-api.yml` (staging, production and rollback-verify loops,
     12 × 5 s, then one `/ping`), `scripts/Deploy-BffApi.ps1` (24 × 5 s, `Test-Health` loop :180-196) and the control
     plane's H9 `HttpHealthProbe` (`BffDeployOptions.HealthProbeInterval` 5 s × `HealthProbeMaxRetries` 24). Such a
     poller makes up to 12 requests in one fixed window; when the BFF ITSELF answered non-200 (Unhealthy 503) to the ten
     before, polls 11-12 get 429. In a non-final window that only delays detection; in the FINAL window it removes the
     last ~10 s of the poller's budget (deploy-bff-api.yml ~50 s instead of ~60 s; Deploy-BffApi.ps1 and H9 ~110 s
     instead of ~120 s). Requests the platform front end answers while the app is not listening (502/503) never reach
     the limiter. The outcome flips only when the app answers Unhealthy for ~45-50 s and turns Healthy inside that
     final ~10 s. Consequence: a staging verify fails safe ("production is UNCHANGED", re-run); a **production verify
     triggers the automatic rollback swap of a deploy that would have passed**. (r1's "Deploy-BffApi.ps1 is unaffected"
     was wrong — corrected in §15.3.)
4. Side observation (pre-existing, not task 167's): `Deploy-BffApi.ps1`'s `Test-Health` loop neither counts nor
   sleeps when `/healthz` answers 200 with body `Degraded` (it only counts in `catch`), so before r1 it could spin
   without bound on a Degraded app; the rate limit now turns the 11th call into a counted 429. Worth a script fix by
   whoever owns it.

**ANSWERED by owner round 14 item 1 (binding): option B — a dedicated `health-probe` policy. Applied in round f1 (§17).**

**🔔 Owner question (verifier r2 item 5 — not decided here; owner round 12 item 1 chose "a rate limit", not which one).**

| Option | What | New surface | Fixes the 5-s edge | Cost |
|---|---|---|---|---|
| **A** | Keep the shared `"anonymous"` policy (r1 state) and make the three 5-s pollers poll at ≥ 6 s: `deploy-bff-api.yml` `INTERVAL=6` (three loops), `Deploy-BffApi.ps1` `-HealthCheckIntervalSeconds` default 6, `BffDeployOptions.HealthProbeInterval` 6 s | none in the BFF | yes (≤ 10 polls per 60 s) | three consumer edits outside task 167: the ci-workflows hot path, a script, the provisioning control plane; every future poller must remember the 6 s rule |
| **B (recommended)** | A dedicated probe policy for `/healthz`, `/healthz/catalog` and `/ping` only — e.g. `"health-probe"`: per-IP sliding window, 30/min, no queue | one named rate-limit policy in `RateLimitingModule.cs` | yes, for every current and future poller up to 30/min | one BFF registration (§11 below); `/healthz/dataverse*`, `demo-request`, `consent-callback` and the config routes keep the strict 10/min |
| C | Accept the edge as recorded (r1 state) | none | no | a production verify can roll back a good deploy when the app turns healthy in the poller's last ~10 s |

§11 three-question template for option B (prepared, NOT applied): **Existing** — the `"anonymous"` policy
(`RateLimitingModule.cs:95-104`, fixed window 10/min/IP, shared by 12 anonymous routes). **Extension** — no: raising
`"anonymous"` to cover 12-poll loops triples the budget of the routes that create rows (`demo-request`), process an
HMAC'd onboarding callback, or hit Dataverse anonymously (`/healthz/dataverse`, `/healthz/dataverse/crud`). **Cost of
doing nothing** — the final-window edge above: `deploy-bff-api.yml`'s production verify rolls back a good deploy when
the app turns healthy in the last ~10 s of its budget. A `.claude/skills/bff-deploy/SKILL.md` troubleshooting row for
429s during manual polling is already recorded in §15.3 for the main session (main-session-only path).

### 16.4 /conflict-check (round r2)

Files this round edits: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`, `.Ledger.cs`, `.Scanner.cs`, this
note and the POML (no `src/`). Checked 2026-10-03: **no open PR** (23 checked) touches any guard file or
`EndpointMappingExtensions.cs`; `origin/master` (`62277d50a`) has not changed them since the merge base `b8026dfa8`.
Local sibling branches: `task/uac-r2-160` (+13/-1), `task/uac-r2-161-r1` (+7/-4), `task/uac-r2-162` (+20) and
`task/uac-r2-163` (+9/-1) edit the task-074 `RouteAuthorizationGuardTests.cs`; `task/uac-r2-163` also edits
`EndpointMappingExtensions.cs` at ~:325 (the removed `MapAdminKnowledgeEndpoints` block — not this task's lines 68/76/124).
**Soft warn**, unchanged from r1: the main session reconciles the guard at integration (owner round 9 item 2).

### 16.5 Placement and justification (CLAUDE.md §10/§11)

r2 adds no BFF service, registration, endpoint, option, job, column or package — test code (one rule, one control, one
assertion), comment text, the POML and this note. No new file. §10 not triggered (no publish-size or CVE impact; not
measured per the harness rule). No Dataverse plugin (ADR-002). ADR-003: the new rule fails CLOSED (an attribute-only or
unread authorization is a violation, never a pass).

### 16.6 Test runs (round r2, final state; run sequentially, 2026-10-03 20:25-21:05)

| Suite | Result |
|---|---|
| Affected: `RouteAuthorizationGuardTests` | **53 / 53 passed** (51 + 2 new: `NoRouteIsAnonymousByOmission`, `AnonymousByOmission_NegativeControl_FiresEvenUnderAWaiver`) |
| `tests/Spaarke.ArchTests` (NetArchTest, full) | **384 / 384 passed**, 0 skipped (2 m 38 s) |
| `Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `Spe.Integration.Tests` | **403 passed, 25 skipped, 0 failed** (7 m 37 s) |
| `Sprk.Bff.Api.Tests` (BFF unit, full, TRX) | **14,198 passed, 14 failed, 54 skipped** of 14,266 (28 m 12 s). All 14 are `TaskCanceledException: The operation was canceled` — the client-timeout contention signature of §12/§15.6 under the same multi-agent load; none is an assertion. The 14: `DelegationRuleCharacterizationTests.CanManageAccess_ForACallerHoldingExactlyTheNewCollaborateRights_Is200`, `DocumentProfileContractTests.GetDocument_WithNonCompletedStatus_Returns200WithStatusCodeAndNoProfileText(100000003)`, `RelatedRecordCardContractTests.ResolveIdentity_ForADocumentFiledToAMatter_ReturnsTypeDisplayNameAndNumber`, `InsightEndpointsContractTests.PostAsk_CanonicalNameWithBindingRow_LookupIsCaseInsensitive`, `InsightsAssistantEndpointContractTests.Post_RagDisabled_WithForceModeRag_Returns503WithErrorCode`, `InsightsSearchEndpointContractTests.PostSearch_FacadeThrows_Returns500ProblemDetails`, `PinnedMemoryEndpointsContractTests.UpdatePin_NotFound_Returns404`, `CommunicationsEndpointsContractTests.GetLinkedTodos_WithAuthAndThreeMatches_Returns200WithTodos`, `OfficeEntitySearchAuthorizationContractTests.SearchEntities_ForAnAuthorizedUser_StillReturnsTheRowsThePickerNeeds`, `OfficeQuickCreateContractTests.Post_Matter_WithMatterType_Returns201_SetsTypeOwnerAndBusinessUnitDefaults_AndNoNumber`, `OfficeSaveNoTargetContainerContractTests.PostOfficeSave_WithNoTargetEntity_AndAnUnresolvableCaller_StillLandsInTheConfiguredDefaultContainer`, `OfficeTodoSourceAuthorizationContractTests.Post_Todo_WhenEverySourceIsReadable_StillCreatesTheTodo_WithResolverFieldsStamped`, `OfficeVersionSaveContractTests.Post_OfficeSave_VersionSave_WhenTheDocumentHasNoSpePointers_Returns409Office017_AndNeverFallsBackToANewItem`, `Issue1084_OfficeJobRecordDurabilityTests.SaveOfLargeContent_GetsADurableJobRow_WhosePayloadCarriesNoContent(Document)`. **Isolated re-run of those 14 (21 cases with the theories' parameters): 21 / 21 passed** (35 s). The unit assembly compiles nothing r2 changes (r2 touches only `tests/Spaarke.ArchTests`). |

Test scope beyond the listed behaviours: none (the one added assertion pins a criterion's superseded state, item 11).
ADR-038 bans hold in everything added: inline-fixture source analysis only — no `Mock<HttpMessageHandler>`, no
DI-registration test, no constructor null-check test, no Dataverse/Azure call. The live evidence in §16.3 came from
read-only `az monitor app-insights query` calls made by hand, not from any test.

## 17. Fix round f1 (2026-10-04) — branch `task/uac-r2-167-f1`

Base `a6d85598d` (`task/uac-r2-167-r2`). Binding inputs: owner round 14 items 1-2 (re-stated by main-session rounds
16 item 6 and 25 item 7) and the r2 verifier's nine items. **Every item is closed; nothing is owed, deferred or left
for the owner.** No live write (no Dataverse, Azure, Entra or SPE call).

### 17.1 Per item

| # | Item | Disposition | Where |
|---|---|---|---|
| 1 | Owner round 14 item 1: GET `/healthz`, `/healthz/catalog`, `/ping` on a DEDICATED per-IP `health-probe` policy, not the shared `anonymous` one; pin it | **Closed.** New policy `"health-probe"` in the existing `RateLimitingModule`: per client IP, **sliding window 120/min** (the owner's example; the task's floor was 30/min), 6 segments, **no queue**. The three probes carry it; every other anonymous route keeps `"anonymous"` (10/min). Pinned at build time (`TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes`: exactly these three routes, each anonymous, each with ONE rate limit; `NamedRoutes_CarryTheirRecordedClassification` asserts `health-probe` and not `anonymous` on the chain and in the waiver reason) and at runtime against the real app (`HealthProbeRateLimitContractTests`: 24 polls of `/healthz` + 24 of `/ping` from one IP see no 429; the 121st `/ping` from one IP is refused with `Retry-After` and another IP is not; `/status` still refuses its 11th call). The three AnonymousByDesign waivers now name `RequireRateLimiting("health-probe")`. | `RateLimitingModule.cs` 6d; `EndpointMappingExtensions.cs:71/:79/:127`; Ledger `HealthProbeRoutes`; `tests/integration/contract/Api/HealthProbeRateLimitContractTests.cs` |
| 2 | Owner round 14 item 2: an authorization **FallbackPolicy** requiring an authenticated user AND the build rule kept; every intentionally anonymous endpoint carries an explicit `AllowAnonymous` (listed); negative and positive controls for both | **Closed.** `AuthorizationModule.ApplyFallbackPolicy` sets `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`, applied first inside `AddAuthorization(...)`. Build-time pin `TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser` (exactly one assignment in BFF + shared code, exactly that policy, applied inside `AddAuthorization`) with `FallbackPolicy_NegativeControl_EachUnsafeShapeFails` (missing, permissive, `null`, `options.DefaultPolicy`, written-but-unapplied, set twice, the `SetFallbackPolicy` builder form; positive: inline and the real helper shape). Runtime: `AuthorizationFallbackPolicyTests` — real app (anonymous unmatched request → 401; the same with a bearer → 404; `/ping`, `/status`, `/healthz` still answer anonymously; every endpoint in the real endpoint table carries `IAuthorizeData`/`AuthorizationPolicy` or `IAllowAnonymous`) and a host built on the SAME `ApplyFallbackPolicy` (undeclared endpoint: 401 anonymous, 200 signed in; `AllowAnonymous` still open; `RequireAuthorization()` still 401) with the negative control **without** the fallback (undeclared endpoint serves anyone). The build rule `NoRouteIsAnonymousByOmission` is KEPT (and strengthened — item 6). The explicit anonymous surface is a pinned set (`ExplicitlyAnonymousRoutes`, 16 routes, §17.3; `TheExplicitlyAnonymousSurfaceIsPinned` + its control). | `AuthorizationModule.cs`; `RouteAuthorizationGuardTests.cs` (runtime-half section); `tests/integration/auth/UnifiedAccessControl/AuthorizationFallbackPolicyTests.cs` |
| 3 | Verifier's own runs (ArchTests 384/384, both integration suites, the 16 probe-calling unit tests) | **Verified, not open.** Re-run in full on the f1 state (§17.11). | — |
| 4 | Verifier's seeds (a)-(e) for `NoRouteIsAnonymousByOmission` | **Verified, not open.** (d) — the brace-less `if (...) docs.RequireAuthorization();` — now fails with an explicit scanner problem instead of being silently unread (item 5's fix). | — |
| 5 | LOW fail-open: a group continuation inside a BRACED (conditional) block was credited, because `AtStatementStart` treats `{` as a statement start | **Closed.** `AttachContinuations` now credits a continuation only in the **unbroken run** of `group.…;` statements that starts IMMEDIATELY after the group's declaration statement (or, for a group the method receives, after the body's opening brace). Nothing but whitespace/comments may separate them, so no `if`/`else`/loop/`switch`/lambda/`try`/`return`/`throw`/`break`/`continue`/`goto` can stand between the group and the call. Every OTHER authorization-shaped call on a group variable or parameter — braced block, brace-less `if`, `var y = g.X()`, `return g.X()`, after an intervening statement, in a lambda — is a **scanner problem** (fails `EveryRouteDeclaresHowItIsAuthorized`) and earns nothing. This is wider than the item's suggestion ("the enclosing block is the method body"): an early `return` in the SAME block would have kept the fail-open. Control `GroupContinuation_NegativeControl_OnlyAnUnconditionalRunIsCredited` (the verifier's seed + 8 more conditional/skippable shapes + the received-parameter case; positives: adjacent, after a same-group statement, several in a row, inside the declaring block, first statement of the receiving method). Seeded on real code (f1-1). | `RouteAuthorizationGuardTests.Scanner.cs` `AttachContinuations` / `UnconditionalContinuationStarts`; `GroupNode.StatementEnd` |
| 6 | LOW by design: `RequiresSignIn` credited ANY `RequireAuthorization` form, relying on census review | **Closed.** Sign-in is now PROVEN from source (`SignInVerdict` over `PolicyCatalogOf`): bare `RequireAuthorization()` counts only while no DefaultPolicy override is permissive; a named form counts only when every argument resolves (string literal or `AuthPolicies` constant) to EXACTLY ONE `AddPolicy` registration inside `AddAuthorization(...)` and at least one of them calls `RequireAuthenticatedUser()`; an inline lambda, a policy object or an unresolvable name never counts. A reviewer classifying a permissive policy as NotAuthorization no longer makes it sign-in. `EveryRequireAuthorizationFormOnALiveRouteRequiresAnAuthenticatedUser` proves the four forms in use today (bare, `"SystemAdmin"`, `AuthPolicies.ExternalCollaboration`, `AuthPolicies.RagApiKey`) from their registrations; `SignInVerdict_NegativeControl_PermissivePoliciesAreNotSignIn` (permissive literal/constant, a rate-limit policy name, unregistered, registered twice, inline lambda, policy object, partly unresolvable, a permissive DefaultPolicy override; positives incl. a qualified constant and a permissive+proven pair). The FallbackPolicy cannot cover this case (it never applies to an endpoint that declares authorization), which is why the build check is the control. Seeded on real code (f1-2). Resource policies (`can*`) are deliberately NOT proven (they rely on `ResourceAccessHandler`): a future route naming one fails until its registration says `RequireAuthenticatedUser()` (it also fails the empty `PolicyOnlyRoutes` pin). | `RouteAuthorizationGuardTests.cs` "SIGN-IN IS VERIFIED, NOT ASSUMED" |
| 7 | Other r2 items verified | **Verified, not open.** | — |
| 8 | The r1 `src` change and the 5-second-poller edge | **Closed by item 1** (the edge is gone: 120/min per IP; also covers `Rotate-RedisKey.ps1`, another 24 x 5 s `/healthz` poller found this round). | — |
| 9 | Verifier's process note (main-checkout seed residue) | **Verified, read-only:** in `C:\code_files\spaarke`, `UserEndpoints.cs` and `DocumentVersionEndpoints.cs` contain no seed text and `tests/Spaarke.ArchTests/` holds no stray `RouteAuthorizationGuardTests.Ledger.cs`. This round touched only its own worktree. | — |

### 17.2 Consequences of the FallbackPolicy, each fixed in this round

The FallbackPolicy also applies to a request that matches **no** endpoint (ASP.NET Core combines an empty metadata set
into the fallback). So an **anonymous** request to an unmapped path, or to a mapped path with the wrong verb, now answers
**401** instead of 404/405. A signed-in request still gets 404/405. CORS preflights are unaffected (`UseCors` answers them
before `UseAuthorization`; `CorsAndAuthTests.Cors_Preflight_AllowsConfiguredOrigin` preflights an unmapped path and still
gets 204). Platform pings of unmapped paths (Always On `/`, the container warm-up `/robots933456.txt`) only need a response,
so 401 serves them as 404 did. Everything that read "anonymous 401 ⇒ route registered" or "anonymous 404 ⇒ route absent":

| Artifact | Was | Now |
|---|---|---|
| `tests/integration/regression/OboDriveKeyedRouteRetirementTests.cs` | absence by anonymous 404; presence by anonymous 401 | absence by the endpoint table + a SIGNED-IN 404; presence by a signed-in not-404 |
| `tests/integration/regression/DriveKeyedWriteRouteRetirementTests.cs`, `MiContainerKeyedWriteRouteRetirementTests.cs` | anonymous-404 absence twins and an anonymous-401 presence control | removed (the signed-in twins and the endpoint-table assertion carry the scenario); summaries say why |
| `ChatRefineEndpointTests.Refine_GetMethod_NotAllowed` | anonymous GET → 404/405 | GET **with a bearer** → 404/405 (anonymous is now 401 — the one failure the f1 suite run showed) |
| 21 `*_EndpointExists_*` tests in `ChatActions`, `ChatRefine`, `DocumentIntelligenceEnqueue`, `Handler`, `Model`, `Node`, `PlaybookRun` endpoint tests and `EndpointGroupingTests.UserEndpoints_ExistAndRequireAuth` | anonymous `NotBe(404)` (would now pass for a MISSING route) | each also asserts `EndpointTable.AssertMapped(...)` — new helper `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/EndpointTable.cs` reads the real endpoint table by verb + route template |
| `scripts/Test-Deployment.ps1` 3a/3b | probed `/api/containers` and `/api/drives/{id}/children` — DELETED by auth-v4 090 and UAC-r2 071/083 (so 3a, critical, already failed with 404) — and read 401 as "exists" | probe mapped routes (`/api/spe/containers`, `/api/documents/{id}/preview-url`) and assert only "authentication enforced" (401), stating that registration is not provable anonymously |
| `scripts/Deploy-WorkspaceBff.ps1` step 5 | "a 401 confirms the endpoint exists" | new optional `-AccessToken`: with it, anything but 404 = registered (403 = registered, not entitled; 401 = token rejected); without it, a 401 is reported as "auth enforced; registration not provable anonymously" |
| `scripts/Capture-BffBaseline.ps1` help | "what it tests: route exists" | says an anonymous probe cannot prove registration, and names the probe rate limits |
| `docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md` Step 4 + checklist, `M365-COPILOT-DEPLOYMENT-GUIDE.md` (verify + both troubleshooting entries), `SCOPE-CONFIGURATION-GUIDE.md` step 5 | "401 = route registered" | signed-in request (`az account get-access-token --resource api://<BFF-API-APP-ID>`): anything but 404 = registered; anonymous 401 = auth enforced only |
| `infrastructure/byok/main.bicep` + `infrastructure/byok/README.md` | `healthCheckPath: '/health'` and `curl …/health` — a path the BFF never mapped (404 before, 401 now: the BYOK App Service health check could never pass) | `/healthz` (compiled with `az bicep build`); found while checking every platform prober against the probe policy |
| Comments in `FinanceRollupEndpoints.cs`, `Office/CommunicationsEndpoints.cs`, `SpeAdmin/ContainerItemEndpoints.cs`, `tests/integration/auth/SpeAdmin/SpeAdminContainerItemRouteGateTests.cs`; Ledger `NotAuthorizationForms` | "no Default/FallbackPolicy exists" | "neither the default policy nor the FallbackPolicy (an authenticated user) raises that bar" — still the point those comments make |
| `.claude/skills/bff-deploy/SKILL.md` (main-session-only) | "401 = route found, 404 = not registered" (three places) | exact replacement text in §17.8 |

Not changed, deliberately: `INCIDENT-RESPONSE.md:268` (401-vs-403 on a mapped route, still true), `RAG-CONFIGURATION.md:688`
and `Decommission-Customer.ps1` (they call WITH a token, so 404 still means missing), `Configure-CustomDomain.ps1` /
`Test-CustomDomain.ps1` (a 404 on `/healthz` = the BFF is not deployed — `/healthz` is anonymous, unaffected).
**For the main session:** `projects/code-quality-and-assurance-r3/workstreams/config-deployment/design.md` item **4a / D3-02**
("set FallbackPolicy=RequireAuthenticatedUser; add the 401-by-default contract test") is now done by this round.

### 17.3 The explicitly anonymous surface (16 routes — each `.AllowAnonymous()` on a scanned chain)

| Route | Declared at | Mandatory control | Waiver |
|---|---|---|---|
| `GET /healthz` | `Infrastructure/DI/EndpointMappingExtensions.cs:70` | `RequireRateLimiting("health-probe")` | Permanent AnonymousByDesign |
| `GET /healthz/catalog` | `EndpointMappingExtensions.cs:78` | `RequireRateLimiting("health-probe")` | Permanent AnonymousByDesign |
| `GET /ping` | `EndpointMappingExtensions.cs:126` | `RequireRateLimiting("health-probe")` | Permanent AnonymousByDesign |
| `GET /healthz/dataverse` | `EndpointMappingExtensions.cs:85` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `GET /healthz/dataverse/crud` | `EndpointMappingExtensions.cs:88` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `GET /healthz/dataverse/doc/{id}` | `EndpointMappingExtensions.cs:122` | rate limit only | **Pending 166** (an anonymous document read — §0) |
| `GET /status` | `EndpointMappingExtensions.cs:140` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `GET /api/config/client` | `Api/ConfigEndpoints.cs` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `GET /api/config` | `Api/ConfigEndpoints.cs` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `GET /api/office/health` | `Api/Office/OfficeEndpoints.cs` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `POST /api/office/save-debug` | `Api/Office/OfficeEndpoints.cs` | mapped only when `env.IsDevelopment()` | Permanent AnonymousByDesign |
| `POST /api/registration/demo-request` | `Api/RegistrationEndpoints.cs` | `RequireRateLimiting("anonymous")` | Permanent AnonymousByDesign |
| `POST /api/onboarding/consent-callback` | `Endpoints/Onboarding/ConsentCallbackEndpoint.cs` | HMAC-SHA256 over the body | Permanent AnonymousByDesign |
| `POST /api/compose/webhooks/spe-doc-changed` | `Api/ComposeSyncEndpoints.cs` | `RequireWebhookSignature` | Permanent AnonymousByDesign |
| `POST /api/communications/incoming-webhook` | `Api/CommunicationEndpoints.cs` | `RequireWebhookSignature` | Permanent AnonymousByDesign |
| `POST /api/communications/acs/eventgrid` | `Api/AcsEventGridEndpoints.cs` | the shared secret is OPTIONAL | **Pending UNOWNED-NEW** (§3 row 3) |

`AnonymityIsDeclaredOnlyOnAScannedChain` refuses every other way to be anonymous (attribute, metadata, wrapper);
`TheExplicitlyAnonymousSurfaceIsPinned` fails on an added or removed public route; the real-app test
`RealApp_EveryMappedEndpoint_DeclaresAuthorizationOrAnonymity` confirms from the built endpoint table that every other
endpoint declares authorization.

### 17.4 Seeding proofs (f1) — each seeded on REAL source, run, failure captured, restored with `git checkout` and touched

| # | Rule | Seeded | Result |
|---|---|---|---|
| f1-1 | Item 5 (unconditional continuations) — the r2 verifier's own seed | `Api/DocumentVersionEndpoints.cs`: `var docs = app.MapGroup("/api/documents");` + `if (DateTime.UtcNow.Year < 0) { docs.RequireAuthorization(); }` | **2 failed / 59 passed**: `EveryRouteDeclaresHowItIsAuthorized` — "Api/DocumentVersionEndpoints.cs:101: 'docs.RequireAuthorization()' adds to group 'docs' outside the unbroken run … It is NOT credited"; `NoRouteIsAnonymousByOmission` names `GET /api/documents/{documentId}/versions` and `…/versions/{versionId}/content`. (On r2 this seed stayed green.) |
| f1-2 | Item 6 (proven sign-in) — a permissive policy a reviewer classified | `AuthorizationModule.cs` `options.AddPolicy("ZzOpen", p => p.RequireAssertion(_ => true));` + `GET /api/me` → `.RequireAuthorization("ZzOpen")` + `NotAuthorizationForm("RequireAuthorization(\"ZzOpen\")")` in the Ledger | **2 failed**: `EveryRequireAuthorizationFormOnALiveRouteRequiresAnAuthenticatedUser` — "RequireAuthorization("ZzOpen"): none of the policies "ZzOpen" calls RequireAuthenticatedUser()…"; `NoRouteIsAnonymousByOmission` names `GET /api/me`. (On r2 the census classification made it pass.) |
| f1-3 | Item 1 — a probe back on `"anonymous"` | `/ping` → `RequireRateLimiting("anonymous")` | Guard **2 failed**: `TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes` — "removed: GET /ping"; `NamedRoutes_…`. Runtime **2 failed**: `AFiveSecondDeployPoller_NeverSees429…` — statuses `…200, 429, 200, 429…` (the deploy-poller edge, reproduced); `TheProbes_AreStillRateLimited…` — "request 11 of 120 … found 429". |
| f1-4 | Item 1 — the looser policy spreads | `/status` → `RequireRateLimiting("health-probe")` | Guard **1 failed**: "added: GET /status". Runtime **1 failed**: `NegativeControl_TheOtherAnonymousRoutes…` — "expected 429 … but found 200". |
| f1-5 | Item 2 — fallback written but not applied | `ApplyFallbackPolicy(options);` removed | Guard **1 failed**: "AuthorizationModule.cs:417: the FallbackPolicy is assigned in ApplyFallbackPolicy, but nothing applies it inside AddAuthorization(...)". Runtime **1 failed**: `RealApp_AnAnonymousRequest…IsChallenged401` — "found 404". |
| f1-6 | Item 2 — permissive fallback | `…RequireAssertion(_ => true).Build()` in `ApplyFallbackPolicy` | Guard **1 failed**: "FallbackPolicy = …RequireAssertion(_ => true)… — it must be exactly …RequireAuthenticatedUser()…". Runtime **3 failed** (the host built on the real method serves `/declares-nothing` anonymously — 200; unmatched anonymous → 404, twice). |
| f1-7 | Item 2 — a route loses its declaration | `.RequireAuthorization()` removed from `GET /api/me` | Guard **1 failed**: `NoRouteIsAnonymousByOmission` names `GET /api/me`. Runtime **1 failed**: `RealApp_EveryMappedEndpoint_DeclaresAuthorizationOrAnonymity` — `{"GET /api/me  (HTTP: GET /api/me => GetCurrentUserAsync)"}`. And the runtime stays CLOSED: in that seeded state `EndpointGroupingTests.UserEndpoints_ExistAndRequireAuth("/api/me")` still got 401 — the fallback. |
| f1-8 | Item 2 — a route made public unlisted | `GET /api/me` → `.RequireAuthorization().AllowAnonymous()` | **4 failed**: `TheExplicitlyAnonymousSurfaceIsPinned` ("added: GET /api/me"), Rule A ("ANONYMOUS with no AnonymousByDesign or Pending waiver"), the waiver rule (CallerScopedOnly on an anonymous route), `AnonymityIsDeclaredOnlyOnAScannedChain` (count 17 ≠ 16). |
| f1-9 | §17.2 — an existence test that had become vacuous | `Api/Ai/HandlerEndpoints.cs`: `group.MapGet("/", GetHandlers)` → `group.MapGet("/zz-moved", GetHandlers)` (so `GET /api/ai/handlers` is no longer mapped) | **1 failed**: `HandlerEndpointsTests.GetHandlers_EndpointExists_AcceptsGet` — its anonymous `NotBe(404)` line PASSED (401 from the fallback: the vacuity), and the added `EndpointTable.AssertMapped` failed: "GET /api/ai/handlers must be a registered route … but found False". `EndpointTableHelper_TellsAMappedRouteFromAMissingOne…` (the helper's own positive/negative control) passed. |

After the last restore `git status` was clean (every seed restored from the commit with `git checkout --`, then touched).
The inline-fixture controls listed in §17.1 run on every build.

### 17.5 Placement and justification (CLAUDE.md §10 / §11)

New BFF surface, each answered with the three questions:

- **`"health-probe"` rate-limit policy** (`RateLimitingModule.cs`). *Existing*: the `"anonymous"` policy (fixed 10/min/IP,
  shared by 12 anonymous routes) — grep found no other probe policy. *Extension*: no — raising `"anonymous"` would triple
  the budget of routes that create rows (`demo-request`), take an HMAC'd callback, or hit Dataverse anonymously
  (`/healthz/dataverse*`); the probes need a looser budget, the rest need the strict one. *Cost of doing nothing*: a 5-second
  poller (deploy-bff-api.yml, Deploy-BffApi.ps1, control plane H9, Rotate-RedisKey.ps1) loses its 11th/12th poll of a
  window to 429, so a production verify could roll back a good deploy; a 429 on the slot-swap warm-up stops the swap.
  Owner round 14 item 1 decided it.
- **`AuthorizationModule.ApplyFallbackPolicy(AuthorizationOptions)`** (public static method in the existing module; no new
  type, registration or option). *Existing*: `AddAuthorization(...)` in the same module — the method is called from there.
  *Extension*: it IS the extension; it is a separate method only so the test can build a host on the real policy (the
  module's own `AddCredentialSelection` precedent). *Cost of doing nothing*: an endpoint that declares nothing is callable by
  anyone (owner round 14 item 2), and the behaviour could only be tested against a re-declaration of the policy.
- No new endpoint, service, DI registration of a type, option, job, column, package, PCF or plugin (ADR-002). Fail closed
  (ADR-003): the fallback, the proven-sign-in rule and the continuation rule each fail CLOSED. Placement: the BFF
  (cross-cutting authorization and throughput policy of the BFF's own pipeline — §10 decision criteria: nothing to place
  elsewhere).
- New test files: `AuthorizationFallbackPolicyTests.cs` (auth KEEP path), `HealthProbeRateLimitContractTests.cs` (contract
  KEEP path), `TestInfrastructure/EndpointTable.cs` (helper; no existing endpoint-table helper — the two retirement tests
  inline the same enumeration). ADR-038 bans: none (no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor
  null-check; the real app is exercised over HTTP).
- **Publish size** (CLAUDE.md §10 item 4, measured — not estimated): fresh source exports of the base `a6d85598d` and of
  this branch to short paths (`C:\w167b`, `C:\w167h`), `dotnet publish -c Release`, zipped with PowerShell
  `Compress-Archive` over `deploy\api-publish\*` (the `Deploy-BffApi.ps1` method), PDBs included: base **45.65 MB**
  (47,870,004 bytes, 212 files, 4 PDBs) vs branch **45.65 MB** (47,870,402 bytes, 212 files, 4 PDBs) = **+398 bytes**. Equal
  file counts on both sides. **CVE**: no package reference changed (no `.csproj` / `Directory.Packages.props` edit), so the
  vulnerable-package set is unchanged.

### 17.6 `src/` files changed in f1

`Infrastructure/DI/RateLimitingModule.cs` (the policy + summary text), `Infrastructure/DI/EndpointMappingExtensions.cs`
(three probes → `"health-probe"`, comments), `Infrastructure/DI/AuthorizationModule.cs` (`ApplyFallbackPolicy` + its call),
and comment-only lines in `Api/Finance/FinanceRollupEndpoints.cs`, `Api/Office/CommunicationsEndpoints.cs`,
`Api/SpeAdmin/ContainerItemEndpoints.cs`. None is a parallel-unsafe zone of this project.

### 17.7 /conflict-check (round f1)

Open PRs: **21 checked, none** touches any file this round edits. `origin/master` and the work branch have not changed
them since 167's merge base. Local sibling branches that edit the same files (textual merges at integration, soft warn):
`task/uac-r2-163*`, `-164*`, `-166*` and `-167-r1/-r2` (`EndpointMappingExtensions.cs`, other regions); `task/uac-r2-165*`
(`AuthorizationModule.cs` — the SystemAdmin policy fix; this round adds a call at the top of the `AddAuthorization` lambda
and a new method after `AddAuthorizationModule`), `task/uac-r2-165-f1` (`ContainerItemEndpoints.cs` and
`SpeAdminContainerItemRouteGateTests.cs` — hunks from line 44 on; this round's comment edit is at lines 37-38),
`task/uac-r2-161*` and `work/smart-todo-decoupling-r3` (`Office/CommunicationsEndpoints.cs`). **Integration note for the
main session:** any sibling that ADDS a `RequireAuthorization("NewPolicy")` form, a public route, or a rate-limit policy on
a probe must update `EveryRequireAuthorizationFormOnALiveRouteRequiresAnAuthenticatedUser`'s four-form list,
`ExplicitlyAnonymousRoutes`, or `HealthProbeRoutes` in the same merge — by design. Task 166's removal of
`GET /healthz/dataverse/doc/{id}` deletes its line from `ExplicitlyAnonymousRoutes`.

### 17.8 `.claude/` edits for the main session (main-session-only paths) — exact text

**(a) `.claude/skills/bff-deploy/SKILL.md`, the route-registration check** — replace the code block and rule that read
"`# Unauthenticated test — expect 401 (route found, needs auth)` … `**Key verification rule**: Any endpoint behind
.RequireAuthorization() should return 401 without a token. If it returns 404, the route didn't register (incomplete
deployment).`" with:

````
```bash
# Signed-in test — expect anything but 404 (the BFF API app id: docs/architecture/auth-azure-resources.md)
TOKEN=$(az account get-access-token --resource api://<BFF-API-APP-ID> --query accessToken -o tsv)
curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $TOKEN" \
  https://spaarke-bff-dev.azurewebsites.net/api/documents/00000000-0000-0000-0000-000000000000/preview-url
# Expected: 403 (routed; the per-document filter refuses the unknown id). 404 = the route did NOT register.
```

**Key verification rule**: prove registration with a **signed-in** request — anything but 404 means the route is
registered. An **anonymous** request answers **401 whether or not the route exists**: since UAC-r2 task 167 the BFF's
authorization FallbackPolicy also challenges requests that match no route. An anonymous 401 proves only that
authentication is enforced.
````

**(b) Same file, "Manual Quick Deploy" step 3** — replace "`# Expect 401 (auth required) — NOT 404`" with
"`# With -H "Authorization: Bearer $TOKEN": expect anything but 404 (an anonymous 401 does not prove the route exists)`".

**(c) Same file, troubleshooting table** — replace the row "Health check passes but specific endpoints return 404 | … |
Test specific endpoints behind `.RequireAuthorization()` — should return 401 (route found, auth needed), NEVER 404. If 404,
deploy is incomplete." with:

```
| Health check passes but specific endpoints return 404 to a SIGNED-IN request | Incomplete package: route handler couldn't compile at startup due to missing DLL | Call the endpoint WITH a bearer token — anything but 404 means it registered. Do not use an anonymous 401 as proof: the authorization FallbackPolicy (UAC-r2 task 167) answers 401 for a missing route too. |
```

and append (this replaces the §15.3 row, which is withdrawn):

```
| `/healthz` or `/ping` returns 429 | More than 120 requests a minute from one client IP: the probes' "health-probe" rate limit (UAC-r2 task 167, owner round 14). Deploy and rotation pollers make at most 12/min | Wait for the window to slide (Retry-After header). A 429 is not a failed deploy. |
```

**(d) Same file, §9c** (`/healthz/dataverse/doc/{id}` smoke check) — unchanged by this round; task 166 owns moving it to
`GET /healthz/dataverse` (§0).

### 17.9 🔔 Found while applying item 1 — for the main session (not an open item of this task)

`GET /healthz/catalog` is not a cheap probe: every call runs `RoutingConsumerTypeHealthCheck.ReconcileAsync` (2-3
uncached Dataverse queries over `sprk_playbookconsumer` / `sprk_analysistool` / `sprk_analysisaction`,
`Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheck.cs:166-186`) and `ComposeIdentityKeyHealthCheck.ProbeAsync`
(a Dataverse metadata read, `Services/Compose/ComposeIdentityKeyHealthCheck.cs:146-152`). Owner round 14 item 1 puts it on
`health-probe` (120/min per client IP) with the other two probes, as this task applied. So one anonymous address can
drive about 360-480 Dataverse requests a minute through the BFF's own identity — roughly a quarter to a third of
Dataverse's per-identity service-protection budget (6,000 per 5 minutes) — and a handful of addresses can exhaust it,
throttling every user. For scale: production today has NO limit on this route (round r1 added `"anonymous"` 10/min, not
yet deployed), so f1 is still a large improvement on what runs; and no poller calls `/healthz/catalog` (the pollers use
`/healthz` and `/ping`; grep over scripts, workflows, infrastructure and the control plane). **Proposed complete fix**:
bound the catalog route's Dataverse cost independently of the request rate — a single-flight, 30-second memo of the
`HealthCheckResult` in `RoutingConsumerTypeHealthCheck` and in `ComposeIdentityKeyHealthCheck`, held where it survives
between probes (`ComposeIdentityKeyHealthCheck` is a singleton, `ComposeModule.cs:79`; `RoutingConsumerTypeHealthCheck`
is registered only as a hosted service, `RoutingModule.cs:74`, so `AddCheck<T>` builds a fresh instance per probe — it
needs the ComposeModule singleton pattern first), so any number of probes costs at most one reconciliation per 30 s per
instance, with a test that N concurrent probes cause one reconciliation and that a result older than the TTL is refreshed.
Not applied here because it changes the freshness of another project's deploy gate (ai-architecture-redesign FR-P0-04)
and is outside owner round 14's text; it needs a main-session decision under round 15.

### 17.10 Step 9.5 quality gates (FULL — `src/`, `tests/**`, auth)

**Code review** (coverage-first; findings, none Critical):
- *Warning (high confidence)* — `/healthz/catalog`'s per-call Dataverse cost under the looser probe budget (§17.9);
  surfaced, not silently accepted.
- *Warning (medium)* — the FallbackPolicy changes what an ANONYMOUS 401 means repo-wide; every in-repo reader of that
  signal was found by grep (`401`/`404` near "registered/exists/absent" over `tests/`, `scripts/`, `docs/guides/`,
  `docs/procedures/`, `.github/`, `.claude/skills/`) and fixed (§17.2); the `.claude` skill text is in §17.8.
- *Suggestion* — `SignInVerdict` reads `AddPolicy(...)` inside `AddAuthorization(...)` only; an
  `AddAuthorizationBuilder().AddPolicy(...)` chain is not catalogued, so such a policy would NOT count as sign-in (fails
  closed, with a message naming the missing registration). Same for a policy name built from a non-`AuthPolicies` constant.
- *Suggestion* — `PolicySlotAssignment` matches any identifier-bounded `FallbackPolicy =` / `DefaultPolicy =` in BFF +
  shared code; an unrelated property of that name would be read as an override (fails closed: the pin demands exactly one,
  exact).
- *Suggestion* — `EndpointTable` matches route templates without inline constraints (documented on the class).
- *Suggestion* — the two contract tests that exhaust a budget compare `DateTime.UtcNow` to repeat a wall-clock-slow attempt;
  the limiter runs on the wall clock and exposes no `TimeProvider`, so this is the honest guard (documented in the test).
- Complexity (CLAUDE.md §11.5): the guard's main file grows by ~700 lines of rules + controls in four cohesive sections
  (sign-in verification, runtime-half pin, pinned sets, continuation controls); `AuthorizationModule` gains one 3-line method.
- ADR-038 bans: none in anything added. AI-smell scan: no single-implementation interface, no log-and-rethrow, no null
  check on a non-nullable (the one `ArgumentNullException.ThrowIfNull` guards a public method's reference parameter).

**ADR check**: ADR-003 (fail closed — the fallback, the proven-sign-in rule and the continuation rule each fail CLOSED) ✓ ·
ADR-008 (authorization stays on endpoint metadata + filters; the fallback is the platform default, not a new mechanism) ✓ ·
ADR-010 (no new DI-registered type) ✓ · ADR-016/ADR-019 (the new policy rejects through the shared ProblemDetails 429 handler
with `Retry-After`, asserted in the contract test) ✓ · ADR-028 (the fallback uses the default authentication scheme, as a
bare `RequireAuthorization()` does) ✓ · ADR-038 (KEEP paths: auth, contract, regression; controls on every new rule) ✓ ·
ADR-002 (no plugin) ✓. No violation; no §6.5 path needed.

### 17.11 Test runs (round f1, final state)

| Suite | Result |
|---|---|
| Affected: `RouteAuthorizationGuardTests` | **61 / 61 passed** (53 + 8 new: `EveryRequireAuthorizationFormOnALiveRouteRequiresAnAuthenticatedUser`, `SignInVerdict_NegativeControl_PermissivePoliciesAreNotSignIn`, `TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser`, `FallbackPolicy_NegativeControl_EachUnsafeShapeFails`, `TheExplicitlyAnonymousSurfaceIsPinned`, `TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes`, `AnonymousSurfaceAndProbePolicy_NegativeControl_PinsBite`, `GroupContinuation_NegativeControl_OnlyAnUnconditionalRunIsCredited`) |
| Affected unit/contract/auth/regression tests (the new `AuthorizationFallbackPolicyTests` and `HealthProbeRateLimitContractTests`, the three retirement files, the 8 classes with fixed existence tests, the probe-calling `HealthAndHeadersTests` / `PipelineHealthTests` / `ComposeIdentityKeyHealthCheckContractTests`, `CorsAndAuthTests`, `SpeAdminContainerItemRouteGateTests`, `RateLimitingContractTests`) | **all passed** (two runs: 99 passed / 3 pre-existing skips; then 86 passed / 16 pre-existing skips for the re-touched classes) |
| `tests/Spaarke.ArchTests` (NetArchTest, full) | **392 / 392 passed**, 0 skipped (58 s) |
| `Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** (11 s) |
| `Spe.Integration.Tests` | **403 passed, 25 skipped, 0 failed** of 428 (7 m 37 s) — the 25 are the suite's own live-environment skips |
| `Sprk.Bff.Api.Tests` (BFF unit, full, TRX) — run 1 (after the FallbackPolicy, before the §17.2 test fixes) | **14,219 passed, 1 failed, 54 skipped** of 14,274 (21 m 7 s). The one failure was `ChatRefineEndpointTests.Refine_GetMethod_NotAllowed` — a real consequence (anonymous GET on a POST-only route is now 401), fixed in §17.2. |
| `Sprk.Bff.Api.Tests` (BFF unit, full, TRX) — run 2, final state | **14,207 passed, 14 failed, 54 skipped** of 14,275 (31 m 59 s). All 14 are `TaskCanceledException` ("Error while copying content to a stream") after 2 m 32 s - 3 m 30 s — the client-timeout contention signature of §12/§15.6/§16.6 under the same multi-agent load; none is an assertion. The 14: `DriveKeyedWriteRouteRetirementTests.RetiredDriveKeyedDeleteRoute_WithValidBearer_Returns404AndNeverReachesTheDestroy`, `ComposeReferenceMapSessionLedgerSeamTests.Load_OnEditedReloadOfSameSession_KeepsStableNumbersForUnchangedParagraphsAndPersistsNewEntryForInserted`, `PinnedMemoryEndpointsContractTests.CreatePin_MatterFactWithoutMatterId_Returns400`, `OfficeEndpointAuthorizationContractTests.Get_OfficeJobStatus_WhenJobRecordsNoOwner_IsRefused(null)`, `DocumentProfileContractTests.GetDocument_WhenCallerLacksReadOnTheDocument_Returns403AndLeaksNoProfileData`, `RagSendToIndexIndexNameContractTests.WhenDocumentHasPerRecordSearchIndexName_TheWriteAndTheStampBothUseIt`, `ConfigContractTests.GetConfig_ResponseBody_ContainsNoSecrets`, `DocumentProfileContractTests.GetDocument_WithNonCompletedStatus_Returns200WithStatusCodeAndNoProfileText(100000005)`, `RenderOnSaveSeamTests.NdaLoadEditSaveReopen_NewVersionAndEditLands_ThroughTheWire`, `DocumentIdentityContractTests.ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500("")`, `ComposePatchEngineSaveSeamTests.Save_SplitParagraph_RoundTrips_ThroughTheWire`, `InsightsSearchEndpointContractTests.PostSearch_WithFilter_ForwardsArtifactTypeAndPredicate`, `InsightEndpointsContractTests.PostAsk_FacadeThrows_Returns500ProblemDetails`, `OfficeQuickCreateContractTests.Post_Matter_WithEmptyGuidMatterType_IsTreatedAsNoType_Returns201`. **Isolated re-run of those 14 (47 cases with the theories' parameters): 47 / 47 passed** (1 m 10 s). Run 1 of the same suite, at lower load, had none of them failing. |

Test scope beyond the listed behaviours: the `EndpointTable` helper's own control (one line of justification: the helper
is now the evidence for 21 existence tests, so it needs a positive and a negative case of its own). ADR-038 bans hold in
everything added. No test calls Dataverse or Azure; the publish-size measurement and the bicep compile were local builds.
