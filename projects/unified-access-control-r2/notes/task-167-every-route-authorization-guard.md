# Task 167 — the every-route authorization guard

> **Owner mandate**: owner round 9 item 3 (`notes/session27-owner-decisions-and-research.md`) — "every BFF route must
> declare how it is authorized: a record-level check, an admin policy, or an explicit, reasoned waiver ... A new route
> with only a sign-in check fails the build."
> **Evidence**: `notes/route-authorization-sweep-2026-10-02.md` (82 findings, 90 route keys).
> **Branch**: `task/uac-r2-167` from `work/unified-access-control-r2` @ `6b243f092`; verifier round r1 on
> `task/uac-r2-167-r1` (§15). **One `src/` file changed, by owner decision**: owner round 12 item 1 rate-limits
> `GET /healthz`, `GET /healthz/catalog` and `GET /ping` (`Infrastructure/DI/EndpointMappingExtensions.cs`, three
> `.RequireRateLimiting("anonymous")` lines). Nothing else under `src/` changed.

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
fails where it previously passed. `Deploy-BffApi.ps1` (24 x 5 s) treats a 429 as a retry and spans two windows, so it
is unaffected. If that edge matters, the remedy is a looser dedicated probe policy — a new registration needing its own
§11 justification, so it is not added here.

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
