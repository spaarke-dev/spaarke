# Task 163 — Search / RAG / knowledge / Insights route authorization

> **Task**: `tasks/163-search-rag-knowledge-insights-route-authorization.poml` (GitHub #1102; takes over #1041)
> **Branch**: `task/uac-r2-163` from `work/unified-access-control-r2` @ `91a1c1c83`
> **Date**: 2026-10-03 · **Rigor**: FULL · **Model**: Opus 5.5
> **Sweep findings closed**: #3, #4, #5, #6, #17, #20, #21, #26, #27, #28, #30, #40, #41, #46, #49 (17 route keys)

## 0. Outcome in one paragraph

All 17 route keys are closed. **Nine were DELETED** under the binding owner round 10 item 1 amendment
(no caller in the repo, in no published API description — evidence §2), **eight were FIXED**: every one now asks
Dataverse, as the caller, about the exact record it acts on (declaration filter, before the handler), or requires
the SystemAdmin policy, or trims its result rows to documents the caller can Read. Every index write lands in the
token's tenant partition. The one escalation (§3.4, trigger 3) was **answered by round 16 item 1 / round 25 item 3
and is implemented in fix round f1 (§15)**: `/api/insights/ask` asks **Write** on the subject when a node that can
write reaches it (`matter-health-single` → `persistEnvelope` → `sprk_matter.sprk_performancesummary`), Read
otherwise; caller parameters go through task 164's shared playbook-parameter policy; the Assistant path
(`/api/insights/assistant/query`) carries the same subject rule.

## 1. Step 0 — re-verification and read-only findings

### 1.1 Anchors
All POML anchors re-verified on `91a1c1c83` (line numbers drifted by a few lines; behaviour as the POML states).
One POML claim was **wrong**: `StructuredOutputStreamWidget.tsx` does not CALL `/api/insights/assistant/query` — it
only renders that endpoint's output (doc comment at :389). A second: the Create* wizards DO send `documentId`
(`createdDocumentIds`) to `/index-file`, not only a parent (matterService.ts:394-403, CreateEventWizard.tsx:431-440,
workAssignmentService.ts:582-591, invoiceService.ts:376).

### 1.2 (a) Caller inventory (src/, scripts/, src/solutions/CopilotAgent, .claude/skills, infrastructure, .github; tests are not callers — task 073 precedent)

| Route | In-repo callers | Published API description? |
|---|---|---|
| POST /api/ai/rag/search | scripts/Test-RagSharedModel.ps1, Test-RagDedicatedModel.ps1, scripts/load-tests/k6-ai-load-test.js:217, Run-LoadTest.ps1:87 | no |
| POST /api/ai/rag/index | Test-RagSharedModel.ps1:235/254, Test-RagDedicatedModel.ps1:181/312 | no |
| POST /api/ai/rag/index/batch | **none** (docs tables only: RAG-ARCHITECTURE.md, AI-DEPLOYMENT-GUIDE.md) | no |
| DELETE /api/ai/rag/{documentId} | Test-RagSharedModel.ps1:522, Test-RagDedicatedModel.ps1:233/364 | no |
| DELETE /api/ai/rag/source/{sourceDocumentId} | **none** (RAG-ARCHITECTURE.md diagram only) | no |
| POST /api/ai/rag/index-file | uploadOrchestrator.ts:597; SdapClient IndexFileOperation.ts:42 ← EntityCreationService.indexUploadedFiles ← Create Matter/Project/Event/Invoice/WorkAssignment wizards; NextStepsStep.tsx | no |
| GET /api/ai/knowledge/indexes/{indexName}/documents | **none** (not even in docs) | no |
| DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId} | **none** | no |
| POST /api/ai/knowledge/indexes/reindex/{documentId} | **none** | no |
| POST /api/ai/knowledge/test-search | **none** | no |
| POST /api/admin/knowledge/index-references | **none** (RAG-ARCHITECTURE.md table; the `add-reference-to-index` skill uses scripts/ai-search/*.ps1 direct to AI Search, not the BFF) | no |
| POST /api/admin/knowledge/index-reference/{knowledgeSourceId} | **none** | no |
| DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId} | **none** | no |
| POST /api/insights/ask | src/dataverse/forms/sprk_matter/insightCardMount.ts:262/306, insightWidgetOnLoad.ts:156/289 | no |
| POST /api/insights/assistant/query | none in code — but it is the subject of the **published cross-workstream contract** `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` v1.1 ("the canonical contract between the BFF and the Spaarke Assistant project … binding for the BFF side") | **yes** |
| POST /api/insights/search | none in code — published to makers as "the canonical caller path" with request/response shapes in docs/guides/INSIGHTS-ENGINE-GUIDE.md §7 and INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md; cited by the E3 contract | **judgment: treated as published** (§2.3) |
| POST /api/workspace/ai/summary | scripts/Deploy-WorkspaceBff.ps1:69 — the deploy smoke test FAILS the deploy on 404 ("ENDPOINT MISSING") | no |

Published descriptions searched: `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml` (36 paths, none of the 17),
`spaarke-api-plugin.json`, `declarativeAgent.json`, every `*openapi*`, `ai-plugin*`, `declarativeAgent*`,
`manifest.json` tracked in the repo (the `knowledge/` ones are third-party samples).

**ParentEntity types sent to /index-file**: Document Upload Wizard strips `sprk_` from the launch entity (any type the
record-keyed upload route accepts — the same `EntityAccessFilter` map, so every one resolves); Create wizards send
`sprk_matter|sprk_project|sprk_event|sprk_invoice|sprk_workassignment`, normalized to the short form by
EntityCreationService. **All resolve through `EntityAccessFilter.TryResolveEntitySet`** — escalation trigger 6 does not fire.

### 1.3 (b) Task 167
Not on this branch (`RouteAuthorizationGuardTests` has no OwningTask "163" waiver). Per the amendment: no waiver,
ledger or GovernedFiles edit. The census WAS bumped 120 → 119 because a whole endpoint file was deleted (the
amendment's explicit instruction, which overrides the POML body's "do not edit the census").

### 1.4 (c) FinanceAuthorizationFilter generalization
Answered by the binding amendment ("do NOT rename or move …; reuse it as it is"). **No `RecordAccessAuthorizationFilter`
was created**; the filter file is untouched. Conflict check found `task/uac-r2-159` edits the same file only to forward
`UniformRecordNotFound` to `ProblemDetailsHelper` (bytes unchanged) — compatible with every call made here.

### 1.5 (d) insights-ask playbooks and their Dataverse writes
Seeded bindings (`infra/dataverse/sprk_playbookconsumer-rows.json`): `matter-health-single` is bound twice, both as
`insights-ask` (default p500, named p400); **predict-matter-cost has no insights-ask binding** in the repo seed.

| Playbook | Node | Type | Writes |
|---|---|---|---|
| matter-health-single | `persistEnvelope` | UpdateRecord (actionType 22) | **`sprk_matter.sprk_performancesummary`** on `recordId = {{matterId}}` — the FR-14 envelope built from `groundCitations.output.{body,citations,dimensions}`, `{{tenantId}}`, run timestamp |
| matter-health-single | queryMatterContext / queryKpiAssessments / retrieveObservations / … | LiveFact / QueryDataverse / IndexRetrieve / Agent / Control / Output | reads only |
| predict-matter-cost | all nodes (AIAnalysis / Control / Output) | — | **no Dataverse write** |

The whole run is app-only (`InsightsPlaybookExecutionCache` → `PlaybookOrchestrationService.ExecuteAppOnlyAsync`).
**→ escalation trigger 3 FIRES** (§3.4).

### 1.6 (e) Delegated scopes containing "admin"; customer principals holding Admin
Read-only `az ad app show --id 1e40baad-…`: scopes `access_as_user`, `access_as_external_user`, `SDAP.Access`,
`user_impersonation` — **none contains "admin"**. But: `signInAudience = AzureADMultipleOrgs`, and the app defines one
app role, **`Admin` (allowedMemberTypes User, enabled)**, which satisfies the SystemAdmin policy. The workforce scheme
validates a single tenant (`AzureAd:TenantId = #{TENANT_ID}#`, a GUID per stamp; Model 1 customers are B2B guests in
the hosting tenant, whose role assignments the hosting tenant controls). Trigger 2 asks about the shared reference index
(TenantId "system") being writable by a customer admin: **that write path no longer exists** — the three
/api/admin/knowledge routes are deleted (§2). The two SystemAdmin routes kept here write/delete only the caller's own
token-tenant partition. Recorded for task 165 (which owns the SystemAdmin policy) rather than stopped on.

## 2. Route dispositions

### 2.1 Deleted (9) — owner round 10 item 1
| Route | Finding | No-caller evidence | Not-published evidence |
|---|---|---|---|
| POST /api/ai/rag/index/batch | #6 | §1.2: no src/script/skill/infra/workflow caller | not in CopilotAgent OpenAPI / plugin / manifests |
| DELETE /api/ai/rag/source/{sourceDocumentId} | #30 | §1.2 | same |
| GET /api/ai/knowledge/indexes/{indexName}/documents | #26 | §1.2 (not even documented) | same |
| DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId} | #27 | §1.2 | same |
| POST /api/ai/knowledge/indexes/reindex/{documentId} | #28 | §1.2 | same |
| POST /api/ai/knowledge/test-search | #3 | §1.2 | same |
| POST /api/admin/knowledge/index-references | #49 | §1.2; reference index is fed by scripts/ai-search/*.ps1 | same |
| POST /api/admin/knowledge/index-reference/{knowledgeSourceId} | #20 | §1.2 | same |
| DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId} | #21 | §1.2 | same |

Deleted with them ("services, filters and options that only the deleted routes used"): `Api/Ai/AdminKnowledgeEndpoints.cs`
(whole file → census 120→119), `Services/Ai/ReferenceIndexingService.cs` + its DI registration (AiModule),
`Services/Ai/Indexing/ISchemaMapper.cs`, `KnowledgeDocumentSchemaMapper.cs` (used only by that service), the
`MapAdminKnowledgeEndpoints` mapping, the request/response models of the deleted handlers, the I2 arch-test waiver for
the deleted file, `ReferenceIndexingServiceTests.cs`, `KnowledgeBaseReindexReplaceStaleChunksContractTests.cs`, the
deleted routes' tests in Spe.Integration.Tests `KnowledgeBaseEndpointsTests.cs` (health tests kept), and the
ReferenceIndexingService registrations in three Spe fixtures. **Kept deliberately**: `IRagService.GetIndexedDocumentsAsync`
/ `DeleteIndexedDocumentAsync` (now uncalled) — the POML limits RagService to two changes and the amendment names
services/filters/options, not members of a live service; recorded as a follow-up (§9). `DeleteBySourceDocumentAsync`
and `IndexDocumentsBatchAsync` stay (RagIndexingPipeline / FileIndexingService use them).

Absence is pinned by `tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs` (endpoint table + 404 with a
bearer + 401 positive controls on the surviving siblings).

### 2.2 Fixed (8)
| Route | Finding | Mechanism |
|---|---|---|
| POST /api/ai/rag/search | #5 | per-row trim in the handler: ONE `IAiAuthorizationService.AuthorizeAsync` per page (as the caller); null/non-GUID ids dropped; failed/denied → no rows; `TotalCount` = rows returned. Parent gate: `AddTargetedRecordAuthorizationFilter` → `FinanceAuthorizationFilter` Read on the parent's set when `Options.ParentEntityId` is set (uniform 404). `SessionId` → 400. Options rebuilt with token tenant + `CallerPrincipal`. |
| POST /api/ai/rag/index | #6 | `RequireAuthorization("SystemAdmin")`; tenant: filter + handler, partition = token tid |
| DELETE /api/ai/rag/{documentId} | #30 | `RequireAuthorization("SystemAdmin")`; query tenant must equal token tid (filter + handler); `RagService.DeleteDocumentAsync` deletes only a chunk with that key AND the caller's tenant |
| POST /api/ai/rag/index-file | #4, #1041 | tenant: new `FileIndexRequest` case in TenantAuthorizationFilter + handler check; partition = token tid. `AddTargetedRecordAuthorizationFilter` (when a document or parent is named): Write on `sprk_documents(DocumentId)`, AppendTo (`entity.associate_document`) on the parent; unresolvable → uniform 404. Handler: KnowledgeSource fields → 400; row read → null = uniform 404; no file / item mismatch / parent mismatch → 409; chunks carry the row's parent when it has one |
| POST /api/insights/ask | #17 | `AddFinanceAuthorizationFilter(ResolveAskSubjectTargets, UniformNotFound)`: Read on `sprk_matters(GUID)`; non-GUID → 400. Handler: raw playbook GUID only when `GetBindingByPlaybookIdAsync` returns an insights-ask binding; identifier parameters refused (`insights.parameters.not_accepted`); facade gets the CANONICAL subject. **Read sufficiency escalated (§3.4).** |
| POST /api/insights/assistant/query | #40 | `AddFinanceAuthorizationFilter(ResolveSubjectTargets, UniformNotFound)`: Read on the subject's set (ISubjectParser + EntityAccessFilter map). Runs before the SSE response opens. This is the only path into `AssistantToolCallHandler` (`IInsightsAi.AssistantQuery*` has no other caller; the chat `insights.query` tool is only a capability constant since R6 task 023), so the amendment's "Assistant tool path" carries the same subject authorization. |
| POST /api/insights/search | #41 | same declaration as /assistant/query |
| POST /api/workspace/ai/summary | #46 | `WorkspaceAuthorizationFilter` (identity) then `AddFinanceAuthorizationFilter(ResolveSummaryTargets, UniformNotFound)`: Read on sprk_events/matters/projects (map) or sprk_documents (constant), Record path; unsupported type → the service's own 400 body, no rights query; KeyNotFound → the same uniform 404 |

### 2.3 Judgment calls the main session may reverse
1. **/api/insights/search kept and fixed, not deleted.** No code caller; but the Insights maker guides publish it as the
   canonical API (request/response shapes) and the published E3 contract cites it. Fixing closes the hole either way;
   deletion is a one-commit follow-up if the owner reads "published" more narrowly.
2. **/api/workspace/ai/summary kept**: the deploy smoke script is a caller that fails the deploy on 404.

## 3. Deviations from the POML body (each with reason) and escalations

### 3.1 Deviations
| POML said | Done | Why |
|---|---|---|
| Generalize the filter into `RecordAccessAuthorizationFilter.cs` | Not created | Binding amendment: reuse FinanceAuthorizationFilter as is (rename = task 170). Acceptance criterion "the generalized filter …" is superseded. |
| A route whose declaration may name no record | `AddTargetedRecordAuthorizationFilter` (private, RagEndpoints.cs) runs `FinanceAuthorizationFilter.InvokeAsync` only when the request names a record | The filter denies zero-check declarations; /search without a parent and /index-file without document+parent must still work (POML). No second check loop — the evaluation is the Finance filter's own. |
| Workspace sprk_document → Document path | Record path on `sprk_documents` | The only rule in the Document path's chain is `OperationAccessRule` (same decision); the Record path makes the evaluated entity set observable, which the acceptance criterion requires ("asserted on the entity-set argument"). |
| Body parent must equal "the row's matter/project/invoice" | Must equal ONE of the row's matter/project/invoice lookups when the row carries any | The Create Invoice wizard dual-binds documents (`additionalBinds`), so a row can carry matter AND invoice; requiring the Step-3-first one would 409 a legitimate invoice upload. Chunks carry the matched row value (row id + row name). |
| index-file 500 detail | fixed detail AND `SdapProblemException` rethrown | the pipeline throws 400 INDEX_NOT_ALLOWED for a disallowed SearchIndexName; swallowing it into a fixed 500 would hide it (ADR-019). |
| Reuse `ProgrammableRecordAccessSource` | New `CallerAccessSeam` (superset) in the shared harness | the trim runs through the REAL `AiAuthorizationService`, which asks `GetUserAccessAsync` (that source throws there by design), and fail-closed tests need a faulting seam. |
| Scripts send "the tenant from the token (no change if it already does)" | Both scripts now decode `tid` from the token | they generated random tenant ids; they now assert 403 for every other-tenant step. |

### 3.2 Owner round 12
Item 6 (oid-vs-systemuserid in playbook lists) and item 9 waivers belong to tasks 164/166 — nothing of this task's
surface. Item 1 (rate limiting /healthz) is task 167's. No round-12 item touches these routes.

### 3.3 Escalation triggers evaluated
| # | Trigger | Result |
|---|---|---|
| 1 | non-admin caller of a SystemAdmin route | Only the two operator diagnostics (by design SystemAdmin now) and nothing else. Not fired. |
| 2 | "admin" scope / customer principal with Admin | No admin scope. App is multi-tenant with a User-assignable `Admin` role — reported §1.6; the shared-index write path is deleted, so not stopped. |
| **3** | **insights-ask playbook writes caller-influenced content** | **FIRED** — §3.4 |
| 4 | consumer needs knowledge-source chunks from search/test-search | test-search deleted; /search has no in-repo consumer needing them. Not fired. |
| 5 | consumer sends /ask a raw GUID not bound / identifier params | No product consumer (form scripts send the name + `{}`). Reported: the r1 live smoke runbook `projects/ai-spaarke-insights-engine-r1/notes/phase-1-live-smoke-runbook.md` (inactive project) sends predict-matter-cost's raw GUID with display-number subjects (`matter:M-2024-0341`); it will now get 400 until predict-matter-cost has an insights-ask binding and GUID subjects are used. Not reopened. |
| 6 | index-file parent type unresolvable | Not fired (§1.2). |
| 7 | live gate denies a wizard user | Pending live gate. |
| 8 | internal mixed-tenant batch caller | Both internal callers build single-tenant lists. Not fired. |
| 9 | another task generalizing the filter | Answered by the amendment. |
| 10 | new entity-set map / OperationAccessPolicy key / AiAuthorizationFilter change | None needed. |

### 3.4 🔔 ESCALATION — Read may be the wrong right for /api/insights/ask (trigger 3) — ANSWERED (round 16 item 1, round 25 item 3); implemented in §15

*(Historical record of the escalation as raised; the decision is "Write on the subject for a persisting playbook,
parameters through task 164's shared policy" — see §15.2.)*
- **Playbook**: `matter-health-single` (bound insights-ask default + named).
- **Node / field**: `persistEnvelope` (UpdateRecord, actionType 22) → **`sprk_matter.sprk_performancesummary`**, app-only.
- **How a reader influences it**: `parameters.currentGrade` (does not end in "id", so the specified rule passes it)
  feeds the synthesize node's `templateParameters.currentGrade` → the LLM's output is persisted onto the matter. With
  `parameters: {}` (the live shape) the written content is server-derived and the task-130 reasoning holds.
  `/assistant/query` passes `Parameters: null`, so it is unaffected. The identifier rule already pins the record
  written to the subject.
- **Implemented now**: Read on the subject (the floor every option keeps).
- **Options**: (A) /ask refuses every parameter except the subject's own `matterId` — simplest; the live callers send
  `{}`; the eval harness/smoke tests would send `{}` too. (B) allow-list the tuning keys the non-persisting playbooks
  use (`lookBackYears`, `currency`, `matterType`, `dealSizeBucket`, `opposingCounsel`), refuse the rest (incl.
  `currentGrade`). (C) require **Write** on the subject when any non-identifier parameter is supplied; keep Read for `{}`.
- **Recommendation**: (C) — live widgets unchanged, tuning still possible for writers, no per-playbook knowledge in the endpoint.

## 4. Placement + CLAUDE.md §10 / §11

**Placement**: in the BFF — existing routes on existing route files; no new service, DI registration, option, job,
column, package or endpoint (nine routes, one service, one DI registration and two abstraction types removed). Cited:
`.claude/constraints/bff-extensions.md` §A (no new packages → no publish-size/CVE delta from packages; publish-size
measurement skipped per the workflow instruction), ADR-001/008/010/013/019.

**§11 three questions** — the only new surface is code-internal:
- `AddTargetedRecordAuthorizationFilter` (private, RagEndpoints.cs). *Existing*: `FinanceAuthorizationFilter` (declaration
  machinery), `EntityAccessFilter` (Office body, passes without target), `RecordRouteAccessAuthorizationFilter` (fixed
  route keys) — grep `AddFinanceAuthorizationFilter|AddRecordRouteAccessAuthorizationFilter` in Api/. *Extension*: it IS
  an extension — it calls `FinanceAuthorizationFilter.InvokeAsync` unchanged; the amendment forbids editing that filter
  to add a "may name nothing" mode. *Cost of doing nothing*: /search without a parent and /index-file without a
  document/parent would be denied for every caller (the Finance filter denies zero-check declarations), breaking the
  Create wizards' indexing and the operator search script.
- `WorkspaceAiService.IsSupportedEntityType` / `UnsupportedEntityTypeMessage` (internal static). *Existing*: the private
  `SupportedEntityTypes` set. *Extension*: yes — exposes it instead of a second list. *Cost*: the declaration filter
  would need its own copy of the four types and the 400 text, which would drift.
- Test harness `RouteSweepAuthorizationHarness.cs` (test-only; shared by three test classes).

## 5. Route authorization ledger input (for task 167 — main session records at integration)

167 is not on this branch; nothing was added to `RouteAuthorizationGuardTests` except the census bump (whole file
deleted). Never count `.AddTenantAuthorizationFilter()`, `WorkspaceAuthorizationFilter` or the health route's
`AiAuthorizationFilter` as the per-resource decision. `R` = `Sprk.Bff.Api.Tests.Api.Ai.RagEndpointsAuthorizationContractTests`,
`I` = `Sprk.Bff.Api.Tests.Api.Insights.InsightsRouteAuthorizationContractTests`,
`W` = `Sprk.Bff.Api.Tests.Api.Workspace.WorkspaceAiSummaryAuthorizationContractTests`,
`K` = `Sprk.Bff.Api.Tests.KnowledgeAndRagRouteRetirementTests`.

| Route key | Mechanism that now decides | Deny test |
|---|---|---|
| `POST /api/ai/rag/search` | **handler decision: per-row trim as the caller** (`IAiAuthorizationService.AuthorizeAsync`, one call per page; RagEndpoints.cs references it — Rule B) + filter `AddTargetedRecordAuthorizationFilter` → FinanceAuthorizationFilter Read on a named parent | `R.Search_CallerWhoCanReadNoneOfTheRows_Gets200EmptyAndZeroCount`; `R.Search_WithANamedParentTheCallerCannotUse_IsTheUniform404_AndNoSearchRuns` |
| `POST /api/ai/rag/index` | admin policy `RequireAuthorization("SystemAdmin")` (+ token-tenant partition) | `R.Index_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsIndexed`; `R.Index_AdminNamingAnotherTenant_Is403_AndNothingIsIndexed` |
| `DELETE /api/ai/rag/{documentId}` | admin policy `RequireAuthorization("SystemAdmin")` (+ tenant-bound delete) | `R.DeleteChunk_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsDeleted`; `R.DeleteChunk_AdminNamingAnotherTenant_Is403_AndNothingIsDeleted` |
| `POST /api/ai/rag/index-file` | filter `AddTargetedRecordAuthorizationFilter` → FinanceAuthorizationFilter: "write" on sprk_documents(DocumentId), "entity.associate_document" on the parent; no named record → the caller's OBO download decides (handler decision) | `R.IndexFile_NamedDocumentTheCallerCannotWrite_IsTheUniform404_NothingIndexedOrStamped`; `R.IndexFile_NamedParentTheCallerCannotAppendTo_IsTheUniform404_NothingIndexed`; `R.IndexFile_BodyTenantNotTheTokens_Is403_AndNothingIsIndexedOrStamped` |
| `POST /api/insights/ask` | **(f1)** filter `AddInsightsAskAuthorizationFilter` (InsightEndpoints.cs) → shared policy 400, registered-playbook 400, then the ONE evaluator (`FinanceAuthorizationFilter`) over `PlaybookAuthorizationFilter.BuildSubjectRunChecksAsync`: "read" — or "write" when a node that can write reaches the subject — on sprk_matters(subject), plus each record parameter | `I.Ask_UnreadableAndAbsentMatters_AreTheIdenticalUniform404_AndTheFacadeIsNeverReached`; `I.Ask_PersistingPlaybook_AReaderWithoutWrite_GetsTheSameUniform404_AndNothingRuns` |
| `POST /api/insights/assistant/query` | **(f1)** filter `AddInsightsAssistantAuthorizationFilter` (InsightsAssistantEndpoint.cs) → `FinanceAuthorizationFilter` "read" on the subject (deny), then the Write question carried to the facade's run guard | `I.Assistant_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_NoFacade_NoSseFrame` |
| `POST /api/insights/search` | filter `AddFinanceAuthorizationFilter(ResolveSubjectTargets, UniformNotFound)` — "read" on the subject | `I.Search_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_AndSearchNeverRuns` |
| `POST /api/workspace/ai/summary` | filter `AddFinanceAuthorizationFilter(ResolveSummaryTargets, UniformNotFound)` — "read" on the named record | `W.UnreadableAndAbsentRecords_AreTheIdenticalUniform404_AndNothingIsRead` |
| `POST /api/ai/rag/index/batch` | **DELETED** | `K.RetiredRoutes_AreAbsentFromTheEndpointTable`; `K.RetiredRoute_WithAValidBearer_Is404NotRouted` |
| `DELETE /api/ai/rag/source/{sourceDocumentId}` | **DELETED** | same |
| `GET /api/ai/knowledge/indexes/{indexName}/documents` | **DELETED** | same |
| `DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}` | **DELETED** | same |
| `POST /api/ai/knowledge/indexes/reindex/{documentId}` | **DELETED** | same |
| `POST /api/ai/knowledge/test-search` | **DELETED** | same |
| `POST /api/admin/knowledge/index-references` | **DELETED** (whole file) | same |
| `POST /api/admin/knowledge/index-reference/{knowledgeSourceId}` | **DELETED** (whole file) | same |
| `DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId}` | **DELETED** (whole file) | same |

For 167's FilterMarker: `.AddTargetedRecordAuthorizationFilter(` matches `\.Add\w*AuthorizationFilter\s*[<(]`, and
RagEndpoints.cs references `Spaarke.Core.Auth.AuthorizationService` (Rule B). The census in this branch is **119**; any
Pending waiver 167 created for the nine deleted routes must be deleted at integration (NoWaiverIsStale does not catch a
deleted route — task 073 §6.1 edit 3).

## 6. Tests

### 6.1 New (closed set = the acceptance criteria + named fail-closed branches; 128 test cases across the five classes)
| File | Covers |
|---|---|
| `tests/integration/contract/Api/Ai/RouteSweepAuthorizationHarness.cs` | shared host: real Program + real filters/AuthorizationService/AiAuthorizationService; seams: IAccessDataSource (deny-by-default, recording, faulting), IRagService, IFileIndexingService, IDocumentDataverseService, IGenericEntityService, IInsightsAi, IConsumerRoutingService |
| `.../Api/Ai/RagEndpointsAuthorizationContractTests.cs` | /search (a)–(g) + fail-closed + uniformity + 500; /index; DELETE /{id}; /index-file (a)–(g) + fail-closed + uniformity (absent / unwritable / deleted-after-check) + 500; TenantAuthorizationFilter batch case (filter-level: no route binds the type after /index/batch was deleted) |
| `.../Api/Insights/InsightsRouteAuthorizationContractTests.cs` | /ask (a)–(e) + fail-closed + entity set; /assistant/query × matter/project/invoice × single-shot/SSE × forceMode; /search × 3 schemes; fail-closed |
| `.../Api/Workspace/WorkspaceAiSummaryAuthorizationContractTests.cs` | 4 types (+ case variant) × unreadable/absent; entity-set argument; deleted-after-check; unsupported type; reader 200; fail-closed |
| `tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs` | the nine deleted routes absent; 401 positive controls |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTenantGuardTests.cs` | DeleteDocumentAsync tenant-bound (own / other tenant / absent); batch mixed tenants (both overloads, case variant) |

Beyond the criteria (one line each): `Search_WithAReadableParent_RunsAndAsksAsTheCallerOnTheParentsSet` and
`Ask_TheRightsQuestionIsReadOnTheMatter_AsTheCaller` pin the evaluated entity set/token (criterion says "as the caller");
`IndexDocumentsBatchAsync_ACaseVariantOfTheSameTenant_IsAlsoRefused` pins the Ordinal partition comparison;
`TenantFilter_BatchWhoseEveryItemIsTheCallersTenant_PassesThrough` is the positive control for the batch case.

### 6.2 Existing tests changed (no authorized-caller assertion weakened)
| Test | Change | Reason |
|---|---|---|
| `RagEndpointsTests` (unit) | reflection call passes the new handler params; `ReferenceEquals(captured, request.Options)` replaced by "TenantId and TopK preserved, no index name introduced" | options are now always rebuilt (token tenant + caller principal); the NFR-02 intent is asserted |
| `InsightEndpointsContractTests` | `SampleSubject` `matter:M-1234` → a GUID subject; fixture: reader seam + GetBindingByPlaybookIdAsync → insights-ask | subject id must be a GUID; raw GUIDs must be bound |
| `InsightsSearchEndpointContractTests`, `InsightsAssistantEndpointContractTests` (+ Streaming, same fixture) | reader seam in the fixture | authorized-caller tests |
| `WorkspaceEndpointsContractTests` | three summary cases use a reader client | the route asks Dataverse first |
| `PredictMatterCostEvalHarnessTests` | tuple display ids → stable GUID for subject + `matterId`; fixture binds the playbook as insights-ask + reader seam | GUID subject; identifier rule |
| `Phase1SmokeTest` | `matter:M-FIXTURE-00x` → GUID subjects (one facade-subject assertion follows) | GUID subject |
| Spe `KnowledgeBaseEndpointsTests` | deleted-route tests removed; health kept | routes deleted |
| Spe `ReAnalysisFlowTests`, `ChatEndpointsTests` | dropped ReferenceIndexingService registration | service deleted |
| Spe `InsightsToolIntegrationTests` (live smoke, skipped in CI) | a 404 now fails with "the token's user cannot Read the subject" | uniform 404 meaning |
| ArchTests `RouteAuthorizationGuardTests` | census 120 → 119 | whole file deleted |
| ArchTests `I2_AiSearchTenantIdFilterTests` | removed the deleted file's waiver | file deleted |

Deleted: `ReferenceIndexingServiceTests.cs` (service deleted), `KnowledgeBaseReindexReplaceStaleChunksContractTests.cs` (route deleted).

## 7. Seeding proofs (each guard removed locally, named tests seen failing, restored via `git checkout` + touch)
Batches applied with `seed.ps1` (scratchpad); after each, `git status` was clean.

| Batch | Guard removed | Named test(s) that failed |
|---|---|---|
| b1 | /search per-row trim | `Search_CallerWhoCanReadNoneOfTheRows…`, `Search_KeepsOnlyRows…`, `Search_WhenNoRowHasAUsableDocumentId…`, `Search_Trim_FailsClosed…` |
| b1 | /index SystemAdmin | `Index_SignedInCallerWithoutSystemAdmin_Is403…` |
| b1 | DELETE /{id} SystemAdmin | `DeleteChunk_SignedInCallerWithoutSystemAdmin_Is403…` |
| b1 | /index-file record filter | `IndexFile_NamedDocumentTheCallerCannotWrite…`, `IndexFile_NamedParentTheCallerCannotAppendTo…`, `IndexFile_NamedDocument_WithNoOid_Is401` |
| b1 | TenantAuthorizationFilter every-item batch check (→ first item only) | `TenantFilter_BatchWhoseLaterItemNamesAnotherOrNoTenant_Is403` |
| b1 | RagService.DeleteDocumentAsync ownership lookup | `DeleteDocumentAsync_ChunkOfAnotherTenantOrAbsent_DeletesNothing…` |
| b1 | RagService batch mixed-tenant guard | `IndexDocumentsBatchAsync_MixedTenants…`, `…ACaseVariantOfTheSameTenant…` |
| b1 | /ask filter | `Ask_UnreadableAndAbsentMatters…`, `Ask_FailsClosed`, `Ask_TheRightsQuestionIsRead…` |
| b1 | /assistant/query filter | `Assistant_UnreadableAndAbsentSubjects…`, `Assistant_TheRightsQuestionNames…`, `AssistantAndSearch_FailClosed` |
| b1 | /insights/search filter | `Search_UnreadableAndAbsentSubjects…` (Insights) |
| b1 | workspace summary filter | `UnreadableAndAbsentRecords…`, `TheRightsQuestionNamesTheRecordsEntitySet…`, `FailsClosed` |
| b1 | route retirement (re-mapped POST /api/ai/knowledge/test-search) | `RetiredRoutes_AreAbsentFromTheEndpointTable`, `RetiredRoute_WithAValidBearer_Is404NotRouted` |
| b2 | /search parent gate | `Search_WithANamedParentTheCallerCannotUse…`, `Search_ParentGate_FailsClosed`, `…WithNoOid_Is401…`, `…UnreadableAndNonExistentParents…`, `Search_WithAReadableParent…` |
| b2 | /search SessionId refusal | `Search_WithSessionId_Is400_AndNoSearchRuns` |
| b2 | /index-file row comparisons (409) | `IndexFile_RequestThatDoesNotMatchTheDocumentRow_Is409…` |
| b2 | /index-file knowledge-source refusal | `IndexFile_KnowledgeSourceFields_Are400…` |
| b2 | fixed 500 details (Search, Index, Delete, IndexFile → `ex.Message`) | `Search_ServiceFault…`, `Index_ServiceFault…`, `DeleteChunk_ServiceFault…`, `IndexFile_PipelineFault…` |
| b2 | /ask raw-GUID binding check | `Ask_RawPlaybookGuidNotBoundAsInsightsAsk…` |
| b2 | /ask identifier-parameter rule | `Ask_IdentifierParameterOtherThanTheSubjectsOwn…` |
| b2 | workspace KeyNotFound → uniform 404 | `ADocumentDeletedBetweenTheCheckAndTheFetch…` |
| b3 | handler tenant checks only (search, index, delete, index-file) | **none failed** — the filter enforces alone (incl. the new FileIndexRequest case) |
| b4 | handler checks + filter (pass-through) | `Search_BodyTenantNotTheTokens_Is403`, `Index_AdminNamingAnotherTenant…`, `DeleteChunk_AdminNamingAnotherTenant…`, `IndexFile_BodyTenantNotTheTokens…` (+ the filter-level batch test) |
| b5 | filter only | only the filter-level batch test — **each handler enforces alone** (the #1041 "proven separately" half) |
| b6 | index-file handler check + only the new `FileIndexRequest` filter case | `IndexFile_BodyTenantNotTheTokens_Is403…` — the new case is what enforced in b3 |

## 8. Quality gates (task-execute Step 9.5)
**code-review** (standard depth, coverage-first): no Critical. Applied during review: canonical subject string passed to
the Insights facade (no parse-vs-use divergence; better cache keys); correlation ids on the new 401/403/400 problems;
removed a null check on a non-nullable dictionary key. Remaining **Warnings**: RagEndpoints.cs grew 1585 → ~1750 lines
(cohesive: one route group's handlers + its authorization helpers; no new responsibility class) — decomposition
candidate for the RAG-endpoints owner, not done here; the new tenant problems use the file's existing `code` extension
(SendToIndex precedent) rather than ADR-019's `errorCode`. **Suggestions**: the two now-uncalled IRagService members (§9).
AI-smell scan: no single-impl interfaces, no log-rethrow, no restating comments of note.
**adr-check**: ADR-001/002/003/007/008/009/010/013/019/028/038/052 compliant; no violations; no ADR tension. Zone B
(Api/Insights) imports only `Api.Filters` (PrecedentAdminEndpoints precedent).

## 9. Follow-ups — all CLOSED in fix round f1 (§15)
- ~~Remove the uncalled `IRagService` list/delete members~~ — removed (§15.4).
- ~~predict-matter-cost has no insights-ask binding~~ — proven not a defect and the only consumer (the r1 runbook)
  updated with the Binding prerequisite (§15.5).
- ~~Doc drift on ReferenceIndexingService~~ — fixed in five docs (§15.4).

## 10. `.claude/**` edit for the main session
`.claude/skills/add-reference-to-index/SKILL.md` line 176 — replace
`- \`src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs\` — BFF API indexing service`
with
`- (ReferenceIndexingService and /api/admin/knowledge/* were removed by unified-access-control-r2 task 163 — the scripts above are the only indexing path)`.

## 11. Conflict check
No open PR touches these files; master unchanged for them since the base. Sibling branches: `task/uac-r2-159` edits
`FinanceAuthorizationFilter.cs` (forwarders only — compatible); `task/uac-r2-160/161/167(-r1)` edit
`RouteAuthorizationGuardTests.cs` (this task only bumps the census → expected textual merge at integration).

## 12. Test results (2026-10-03, branch tip before the final commit; heavy machine contention — ~270 concurrent dotnet processes from other agents)
| Run | Result |
|---|---|
| BFF build | succeeded, 0 warnings, 0 errors |
| Affected tests (5 new classes + RagEndpointsTests + SendToIndexAuthorizationContractTests) | 141 / 141 passed |
| Existing tests touched (Insights contract ×4, Workspace contract, eval harness, Phase1 smoke, Finance contract, RagServiceTests) | 267 / 267 passed |
| Spe affected (KnowledgeBaseEndpointsTests, ReAnalysisFlowTests, ChatEndpointsTests, InsightsToolIntegrationTests) | 30 passed, 15 skipped (live smoke, env-gated) |
| **Full BFF unit suite** (	ests/unit/Sprk.Bff.Api.Tests, incl. contract/regression/seam/auth/tenant/data-mutation) | 14231 passed, 54 skipped, **100 failed — every one TaskCanceledException (HttpClient timeout after 3–6 min under contention)**, spread across Office/Compose/SpeAdmin/Workspace-layout/Insights/… classes. **Isolated re-run of all 96 failed names: 127 / 127 passed (2 m 39 s)** → contention, not a defect. |
| **NetArchTest** (	ests/Spaarke.ArchTests) | 346 / 346 passed (census 119) |
| **Sprk.Bff.Api.IntegrationTests** (full) | 104 / 104 passed |
| **Spe.Integration.Tests** (full) | 392 passed, 25 skipped (env-gated live smokes), 0 failed |
| Operator scripts parse | Parser.ParseFile → 0 errors for both |
| Seeding batches b1–b6 | §7 |

## 13. Manual live gate (main session, dev, after deploy; ids redacted to 8 chars)
Use existing non-admin test users in their current BU (e.g. `uac.child.user@demo.spaarke.com`, None on project
`65a3fab2`) and a reader.
```pwsh
$bff = "https://spaarke-bff-dev.azurewebsites.net"
$h = @{ Authorization = "Bearer $nonReaderToken"; "Content-Type" = "application/json" }
# (a) uniform 404 for the non-reader, 200 for a reader (repeat with $readerToken)
#     f1 / round 16 item 1: matter-health-single WRITES to the matter, so for /ask it is 200 only for a caller with
#     WRITE on the matter ($writerToken) and the uniform 404 for a reader without Write; repeat the reader check with a
#     non-persisting playbook when one is bound (predict-matter-cost — not deployed on dev as of 2026-10-04).
Invoke-WebRequest "$bff/api/insights/ask" -Method Post -Headers $h -Body (@{ question="matter-health-single"; subject="matter:$securedMatter"; parameters=@{} } | ConvertTo-Json) -SkipHttpErrorCheck
#     f1: a parameter the shared policy refuses is a 400 errorCode playbook.parameter-rejected, before any rights query
Invoke-WebRequest "$bff/api/insights/ask" -Method Post -Headers $h -Body (@{ question="matter-health-single"; subject="matter:$securedMatter"; parameters=@{ tenantId="x" } } | ConvertTo-Json) -SkipHttpErrorCheck
Invoke-WebRequest "$bff/api/insights/search" -Method Post -Headers $h -Body (@{ query="status"; subject="project:$secureProject" } | ConvertTo-Json) -SkipHttpErrorCheck
Invoke-WebRequest "$bff/api/insights/assistant/query" -Method Post -Headers $h -Body (@{ query="status"; subject="project:$secureProject"; forceMode="rag" } | ConvertTo-Json) -SkipHttpErrorCheck
Invoke-WebRequest "$bff/api/workspace/ai/summary" -Method Post -Headers $h -Body (@{ entityType="sprk_project"; entityId=$secureProject } | ConvertTo-Json) -SkipHttpErrorCheck
# (b) rag/search as the non-reader returns no chunk of a document under the secure matter
Invoke-RestMethod "$bff/api/ai/rag/search" -Method Post -Headers $h -Body (@{ query="<a phrase from a secure doc>"; options=@{ tenantId=$tid; topK=20 } } | ConvertTo-Json -Depth 4)
# (c) deleted routes are 404 with a bearer; the operator routes are 403 for a non-admin
Invoke-WebRequest "$bff/api/ai/knowledge/test-search" -Method Post -Headers $h -Body '{"query":"x"}' -SkipHttpErrorCheck        # expect 404
Invoke-WebRequest "$bff/api/admin/knowledge/index-references" -Method Post -Headers $h -SkipHttpErrorCheck                      # expect 404
Invoke-WebRequest "$bff/api/ai/rag/index" -Method Post -Headers $h -Body '{}' -SkipHttpErrorCheck                                 # expect 403
```
(d) Upload a file through the Document Upload Wizard on a matter the user can write → `sprk_searchindexcompletedon`
stamped on the new document; a Create Matter / Create Event / Create Work Assignment wizard upload still indexes (watch
escalation trigger 7 — f1 §15.6 proves from code and live data that the 409 cannot fire for these wizards). (e) If the
matter form's insight card loads today for a reader, it still loads (conditional: the form scripts call fetch with
`credentials:"include"` and no bearer — pre-existing). f1: the card's stored envelope still renders for a reader; a
REGENERATE (a run of matter-health-single, which writes the envelope) needs Write on the matter.

## 14. PR obligations
- Close GitHub **#1041** from the PR.
- List the nine deleted routes with §2.1's evidence.
- Cite the Placement Justification (§4, §15.8) and the §3.4 escalation with its round-16 answer (§15.2).
- Publish size: measured in f1 (§15.9).
- Integration: the 167 ledger reconciliation in §15.7 (rows, credited forms, and two 167 contract gaps with the
  complete fix proposed).

---

## 15. Fix round f1 (2026-10-04) — branch `task/uac-r2-163-f1`

> **Base**: `task/uac-r2-163` @ `a5ef01408` + `task/uac-r2-164-r1` @ `81c08b39d` merged → **`eb59a67a6`** (the
> baseSha). Binding inputs: owner/main-session **round 16 item 1** and **round 25 item 3** (decisions note on
> `work/unified-access-control-r2`), the POML amendments, and the verifier's 18 items. The "100% fix" standard: every
> item is closed, or proven wrong from code/data, or is a main-session integration step the binding amendment assigns
> to the main session (§15.7).

### 15.1 The merge (164-r1 into 163)
One textual conflict, `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` (the census): 163 wrote 120→119, 164
wrote 120→118. Recounted, not summed: **117** = 120 − `AdminKnowledgeEndpoints.cs` (163) − `PromptLibraryEndpoints.cs`
− `RecordMatchEndpoints.cs` (164). Both comment blocks kept. `RouteAuthorizationGuardTests` 15/15 at the merge commit;
the unit-test project built with 0 errors.

### 15.2 Item 1 — `/api/insights/ask`: Write on a subject the playbook writes; parameters through task 164's shared policy
**Rule (round 16 item 1):** a playbook with a node that writes to the subject needs **Write** on the subject — an
as-user pre-check, then the app-only write; Read suffices only for non-persisting playbooks.

**How "writes to the subject" is decided — ONE rule, not a fork.** The Insights orchestrator binds the subject to a
playbook parameter (`matter:{id}` → `matterId`, `InsightsOrchestrator.EnrichParametersFromSubject`, unchanged), so the
subject IS a record parameter and gets task 164's rule for record parameters: **Write when a node that can write
(side-effecting per `ExecutorSideEffects`, or unclassifiable) references `{{matterId}}`**. The rule was moved, unchanged,
from `PlaybookAuthorizationFilter.RecordParameterOperation` (which now delegates) to
`ExecutorSideEffects.CanWriteThroughParameter`, so the route filter (Api/Filters) and the facade's run guard
(Services/Ai/Insights) apply the same one. An EMPTY node list is Write (fail closed — the Legacy run mode writes).
Live check (read-only, the nine `sprk_playbooknode` rows of `matter-health-single` a0d49d0d, 2026-10-04):
`persistEnvelope` (UpdateRecord, 22) `recordId: {{matterId}}` and `synthesize` (AgentService, 60)
`templateParameters.matterId` → **Write**. `predict-matter-cost` (repo JSON; not deployed on dev): its AgentService node
does not reference `{{matterId}}`, every other node is read-only → **Read**.

**Mechanism** (`Api/Insights/InsightEndpoints.cs`, `AddInsightsAskAuthorizationFilter`), ordered so every 400 precedes
any rights query: oid → 401; body / question / subject / `matter:{guid}` → the handler's 400s; **parameters →
`PlaybookParameterPolicy.Evaluate`**, whose 400 is `PlaybookAuthorizationFilter.ParameterRejected` (now public) — the SAME
body `/execute` and `/run-playbook` return (`errorCode = playbook.parameter-rejected`); the playbook → registered
insights-ask Bindings only (a name by exact consumer code; a raw GUID only when the Binding targeting it is insights-ask);
then `PlaybookAuthorizationFilter.BuildSubjectRunChecksAsync` (new, public static) builds the checks — the subject at
read/write by the rule above, and every record parameter the policy returned by the SAME rule (a parameter naming the
subject folds into its check; the stronger right wins) — and the ONE per-route evaluator (`FinanceAuthorizationFilter`,
unchanged) asks them AS THE CALLER. Any denial, an absent matter, and a fault (node list unreadable) are the identical
uniform 404. The filter publishes `AuthorizedAskRun(PlaybookId, SubjectWriteAuthorized)`; the handler runs EXACTLY that
playbook (no second resolution that could pick another one) and refuses to run without it (uniform 404).

**What changed versus the f0 contract:** 163's own identifier rule (`FindRefusedIdentifierParameter`,
`insights.parameters.not_accepted`) is **deleted** — it was a fork of the policy. Consequences: undeclared tuning keys
(`lookBackYears`, `currency`, `matterType`) are now 400 (the shared allow-list; no Insights playbook node consumes them —
both playbook JSONs read); `tenantId` / `userId` / `run.*` / `start.*` / `userPreferences.*` are 400 (server-owned); a
`matterId` / `projectId` / `invoiceId` OTHER than the subject is no longer refused — it is authorized as its own record by
the shared rule (Read, Write when a writer references it).

**Server-owned `tenantId` bound by the server** (`InsightsOrchestrator.BindServerOwnedParameters`). The policy refuses
`tenantId` from a caller because "the server binds it"; on the Insights runs nothing did, so `{{tenantId}}` rendered
EMPTY — and both Insights playbooks need it (matter-health-single's AgentService validation, the `tenantId` of the
envelope it persists). It is now bound from `InsightsAgentRequest.TenantId` (the route's token tenant) after the subject
enrichment; any parameter value is overwritten. `EnrichParametersFromSubject` itself is untouched. `run.userId`: no
Insights playbook node uses `eq-userid` or the run user (both playbook JSONs and the nine live nodes read); the policy
refuses `userId`, so the run's user stays unset on this path (an `eq-userid` query would match nothing — fail closed).
Recorded, not changed.

**Kill switch kept** (ADR-032): with the AI compound gate off, `INodeService` is not registered; the helper then asks Read
on every record and does NOT establish Write, so `/ask` keeps its 503 (Null facade) instead of becoming a deny, and the
facade's run guard (§15.3) would refuse any writing run anyway.

### 15.3 Item 4 kept TRUE — the Assistant tool path gets the SAME subject rule
The verifier's item 4 ("the amendment is satisfied by the route gate") was true of the f0 Read gate; with item 1's Write
rule it would have become FALSE: `POST /api/insights/assistant/query` with `forceMode: "playbook"` runs the DEFAULT
insights-ask Binding — `matter-health-single` on dev — and with no `forceMode` the intent classifier (an LLM, steerable by
the query text) may route to any insights-ask playbook, so a Read-only caller could still cause the app-only write to
`sprk_matter.sprk_performancesummary`. The POML amendment requires the SAME subject authorization as `/ask`. Because the
playbook is chosen INSIDE the facade, the rule is applied in two halves that share one decision:
- **Route filter** (`AddInsightsAssistantAuthorizationFilter`): Read on the subject through `FinanceAuthorizationFilter`
  (the deny — uniform 404 before any SSE frame, unchanged); then, only when a playbook may run (`forceMode` not `rag`),
  the SAME evaluator asked for **Write** — an answer, not a deny — published for the handler. A fault, a missing token or
  a denial all answer "no Write".
- **Run guard** (`InsightsOrchestrator.RunUnlessItCanWriteTheSubjectAsync`, on a cache MISS, before the first node): a run
  whose node list can write to its subject (the same `CanWriteThroughParameter` rule on the subject's key), or whose node
  list is unreadable or empty, executes only when `InsightsAgentRequest.SubjectWriteAuthorized` is true (default false —
  fail closed). The flag travels from `AssistantQueryFacadeRequest.SubjectWriteAuthorized` through
  `AssistantToolCallHandler` (single-shot) and `StreamPlaybookPathAsync` (SSE). The refusal is
  `SdapProblemException(InsightsAgentRequest.SubjectWriteRequiredCode = "insights.subject.write_required")`; the endpoints
  map it to the uniform 404 (single-shot, and SSE before the stream opens) or to an `error` frame with that code and a
  fixed text after the stream opened (no record id). On `/ask` the guard is the backstop for a playbook that gains a
  writing node between the filter's decision and the run.
- No rights query is made in the guard; the rights are decided by the route filter as the caller (ADR-008 — the guard is a
  capability check against the established right, like the index-file 409 row comparison).

### 15.4 Item 12 — the minor points, each closed
| | Item | Resolution |
|---|---|---|
| (a) | `code` vs ADR-019 `errorCode` | The problems task 163 added to `RagEndpoints.cs` (tenant 401/403, index-file 409s and knowledge-source 400, SessionId 400, fixed 500s) now carry `errorCode` (ADR-019) AND `code` (same value; the SendToIndex precedent its clients read) AND `correlationId` — one helper, `RagProblemExtensions`. Tests assert both keys (`AssertErrorCodeAsync`). Pre-existing problems untouched. |
| (b) | RagEndpoints.cs ≈1750 lines | Closed by the standard, not by churn: CLAUDE.md §11.5 — "a large file is sometimes the right answer — a cohesive, single-responsibility component… note it in the PR". One route group's handlers plus their authorization helpers; no second reason to change. f1 added 17 lines here. Noted for the PR. |
| (c) | uncalled `IRagService` members | **Removed**: `GetIndexedDocumentsAsync`, `DeleteIndexedDocumentAsync`, `IndexedDocumentsPage`, `IndexedDocumentSummary`, RagService's implementations + `EnsureKnownIndex` + `DeleteChunksFromIndexAsync`, the NullRagService members and the seam fake's members (`VersionSaveAiRefreshSeamTests`). `GetIndexHealthAsync` stays (GET /indexes/health). |
| (d) | doc drift | Fixed in AI-ARCHITECTURE.md, AI-SEARCH-INDEX-CATALOG.md (row 1b `EnsureKnownIndex`, row 3 writers/readers, the FR-17 mapper line), INSIGHTS-ENGINE-ARCHITECTURE.md (4 places), RAG-ARCHITECTURE.md (diagram + component table) and AI-EMBEDDING-STRATEGY.md. A grep of `docs/` finds `ReferenceIndexingService` only in removal notes. |
| (e) | `.claude` edit | Main-session only (CLAUDE.md §3). Exact text in §10 (unchanged); a `.claude/` grep confirms it is the ONLY `.claude` reference to the removed code. |
| (f) | predict-matter-cost has no insights-ask binding | §15.5. |
| (g) | two judgment calls | Unchanged and still the main session's to reverse: `/api/insights/search` kept (the maker guides publish it as the canonical API), `/api/workspace/ai/summary` kept (the deploy smoke check fails the deploy on 404). Round 25 item 3 did not reverse them. |

### 15.5 Item 12(f) and item 1's runbook — predict-matter-cost and the r1 live smoke runbook
Read-only (2026-10-04): `sprk_analysisplaybook` on dev has **`matter-health-single` only — predict-matter-cost is not
deployed**, so no environment's `/ask` can run it today regardless of this task; the 400 for an unbound raw GUID is the
owner-approved contract (POML criterion (c)). The seed mirror (`infra/dataverse/sprk_playbookconsumer-rows.json`) is a
projection of the LIVE table ("do NOT hand-author routing here"), so no Binding row is added to it by hand. The only
consumer, `projects/ai-spaarke-insights-engine-r1/notes/phase-1-live-smoke-runbook.md`, is rewritten to the new subject
contract: a "subject contract" section (subject = a real `sprk_matterid`; question = an insights-ask Binding's code; the
shared parameter policy with the refused keys named; Read for predict-matter-cost, Write for matter-health-single; the
uniform 404), the Binding prerequisite as a **dry run → apply → verify** PowerShell block (then `-Export` + `-DiffOnly`
of `Seed-PlaybookConsumers.ps1`) — a live write for whoever runs the runbook, after deploying the playbook —, Steps 7 and 9
with GUID subjects, `parameters = @{}` and negative checks, and four new failure-mode rows. The E3 tool-call contract
(`projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` §2.2, §3.5.6, §5.1, §5.2) and the maker guides
(`INSIGHTS-ENGINE-GUIDE.md` §3.6, `BUILD-A-NEW-INSIGHT-CARD.md` §1.1) now state the subject rule and the parameter policy.

### 15.6 Item 11 — the index-file 409 cannot fire for the event / work-assignment wizards (proved from code and data)
- **Only create-time writes matter**: each wizard calls `/index-file` synchronously right after `createDocumentRecords`,
  before any async job. The create payload binds ONLY the launching record's lookup (`EntityCreationService.
  createDocumentRecords` → `${navigationProperty}@odata.bind` plus optional `additionalBinds`, which only the invoice
  wizard uses); the Document Upload Wizard binds exactly one lookup for its launch context (`uploadOrchestrator.ts`).
- **No server-side create-time stamp exists**: Spaarke ships no plugins (ADR-002); `sprk_document` has NO
  `sprk_regarding*` columns (live `describe`, 2026-10-04), so the core-ancestor stamp never touches it; the six live
  field-mapping profiles target event / invoice / work assignment / report card — none targets `sprk_document`.
- **Live data** (read-only): 0 documents link an event or a work assignment AND carry a matter, project or invoice (1
  document carries any event/work-assignment link at all).
- Therefore a work-assignment upload's row has no matter/project/invoice and the filter's AppendTo on the body parent
  decides (`HoldIndexFileRequestToRow` returns null) — the 409 needs a row lookup that nothing writes.
- **Found while proving it (pre-existing, outside the authorization scope; reported, not changed):**
  `CreateEventWizard.tsx:414-418` binds `sprk_Event@odata.bind` on `sprk_document`, which has NO `sprk_event` column (its
  event lookup is `sprk_relatedevent`, nav-prop `sprk_RelatedEvent` per `DocumentLinkField.All` and live metadata), so
  Dataverse rejects the document create and event uploads produce no document rows. The wizard then calls `/index-file`
  with no `documentId` (event parent, AppendTo) — which keeps working. Proposed fix for the wizard's owner: bind
  `sprk_RelatedEvent` (or resolve the nav-prop the way `workAssignmentService._resolveDocNavProp` does).

### 15.7 Item 13 — integration into task 167's ledger (main session, at integration)
Task 167 is not on this branch (`task/uac-r2-167-r2` is not an ancestor); the binding amendment assigns the ledger
recording to the main session at integration. Everything it needs, against `task/uac-r2-167-r2`
(`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`):
1. **Delete** all 17 `Pending(…, "163", Gap.NoDecision, …)` waivers (the nine deleted routes included — `NoWaiverIsStale`
   does not catch a deleted route).
2. **CreditedForms — add** (each file references `AuthorizationService`, so Rule B passes):
   ```csharp
   new CreditedForm("AddInsightsAskAuthorizationFilter", "Api/Insights/InsightEndpoints.cs",
       "Task 163 f1: the shared playbook-parameter policy, registered insights-ask playbooks only, then Read on the subject "
       + "matter (Write when a node that can write reaches it) and each record parameter, as the caller, through "
       + "FinanceAuthorizationFilter."),
   new CreditedForm("AddInsightsAssistantAuthorizationFilter", "Api/Insights/InsightsAssistantEndpoint.cs",
       "Task 163 f1: Read on the subject as the caller through FinanceAuthorizationFilter (uniform 404 before any SSE "
       + "frame); the Write question is carried to the facade's run guard."),
   new CreditedForm("AddTargetedRecordAuthorizationFilter", "Api/Ai/RagEndpoints.cs",
       "Task 163: FinanceAuthorizationFilter on the record a rag/search or index-file request names (Read on a search "
       + "parent; Write on the document, AppendTo on the parent); the per-row trim and the OBO download decide the rest."),
   ```
3. **NonDecidingAttachment `AddTenantAuthorizationFilter` (owner "163")** stays non-deciding (it still passes through when
   the request names no tenant), but its pinned Evidence quotes a line task 163 rewrote — update it to
   `if (requestedTenantIds.Count == 0) … return await next(context) (TenantAuthorizationFilter.cs:79-84)`.
4. **AdminOnlyRoutes** — the `Api/Ai/RagEndpoints.cs` group gains `"POST /api/ai/rag/index"` and
   `"DELETE /api/ai/rag/{documentId}"` (SystemAdmin index maintenance over the caller's own token-tenant partition).
5. **SweepFindings** — `ResolvedBy = "163"` and `ProofTest`:

   | Route | ProofTest |
   |---|---|
   | POST /api/ai/rag/search | `tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::Search_CallerWhoCanReadNoneOfTheRows_Gets200EmptyAndZeroCount` |
   | POST /api/ai/rag/index-file | `tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::IndexFile_NamedDocumentTheCallerCannotWrite_IsTheUniform404_NothingIndexedOrStamped` |
   | POST /api/ai/rag/index | `tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::Index_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsIndexed` |
   | DELETE /api/ai/rag/{documentId} | `tests/integration/contract/Api/Ai/RagEndpointsAuthorizationContractTests.cs::DeleteChunk_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsDeleted` |
   | POST /api/insights/ask | `tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Ask_PersistingPlaybook_AReaderWithoutWrite_GetsTheSameUniform404_AndNothingRuns` |
   | POST /api/insights/assistant/query | `tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Assistant_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_NoFacade_NoSseFrame` |
   | POST /api/insights/search | `tests/integration/contract/Api/Insights/InsightsRouteAuthorizationContractTests.cs::Search_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_AndSearchNeverRuns` |
   | POST /api/workspace/ai/summary | `tests/integration/contract/Api/Workspace/WorkspaceAiSummaryAuthorizationContractTests.cs::UnreadableAndAbsentRecords_AreTheIdenticalUniform404_AndNothingIsRead` |
   | the nine deleted routes (§2.1) | `tests/integration/regression/KnowledgeAndRagRouteRetirementTests.cs::RetiredRoutes_AreAbsentFromTheEndpointTable` |

6. **Two 167 contract gaps that block a clean record — the complete fix proposed (167's owner decides):**
   - **Retirement.** `LedgerViolations` fails an entry whose route key is absent ("do not drop or re-key — escalate"), so
     the nine routes deleted under owner round 10 item 1 (and task 164's deletions) cannot be recorded. Fix: an absent
     route key passes iff `ResolvedBy` is set AND `ProofTest` names a test that pins the absence (the retirement test
     above); the 90-entry / 82-finding counts stay (the entry stays, resolved).
   - **Admin-policy resolution.** `LedgerViolations` accepts `Credit.AdminOnly` only for `/api/spe/` routes, but owner
     round 9 item 3 accepts "an admin policy" as a declaration; `/rag/index` and `DELETE /rag/{documentId}` are resolved by
     `RequireAuthorization("SystemAdmin")`. Fix: accept AdminOnly for a sweep entry whose route is in `AdminOnlyRoutes`
     (pinned, with its reason) — i.e. drop the `/api/spe/` restriction or list these two keys beside it.
7. The census at integration is the master count after every retired and added file, recounted (this branch: 117).

### 15.8 Placement and CLAUDE.md §10 / §11 for the f1 surface
In the BFF, on existing routes — no new endpoint, service, DI registration, option, job, column, package or PCF. The new
code-internal surface, each answered:
- **`PlaybookAuthorizationFilter.BuildSubjectRunChecksAsync`** (public static). *Existing*: the run mode's private
  `AreRunRecordsAllowedAsync` (documents + record parameters for `/execute`) and `RecordParameterOperation` (the rule) —
  grep `RecordParameterOperation|AreRunRecordsAllowedAsync` in Api/Filters. *Extension*: it IS the extension — it reuses
  the rule and returns `FinanceAuthorizationCheck`s for the ONE evaluator instead of a second check loop. *Cost of doing
  nothing*: `/ask` would need its own copy of the record-parameter rule (the fork round 16 forbids) and could not ask
  Write on a persisting playbook's subject.
- **`AddInsightsAskAuthorizationFilter` / `AddInsightsAssistantAuthorizationFilter`** (internal, in their endpoint files —
  the `AddTargetedRecordAuthorizationFilter` precedent). *Existing*: `AddFinanceAuthorizationFilter` (synchronous
  declarations only — it cannot read a node list) and the edit-forbidden `FinanceAuthorizationFilter`. *Extension*: each
  runs the unchanged `FinanceAuthorizationFilter.InvokeAsync` after an asynchronous declaration. *Cost*: without them no
  Write question is possible on either route.
- **`ExecutorSideEffects.CanWriteThroughParameter`** — a MOVE of `RecordParameterOperation`'s body (Services cannot call
  Api/Filters); one rule for the filter and the run guard.
- **`InsightsAgentRequest.SubjectWriteAuthorized` / `AssistantQueryFacadeRequest.SubjectWriteAuthorized`** (init props)
  and `InsightsAgentRequest.SubjectWriteRequiredCode`. *Existing*: no field carries a route's decision to the facade.
  *Cost*: the classifier-chosen Assistant playbook could run a writing playbook for a Read-only caller.
- **`InsightsOrchestrator`** gains `INodeService` (registered beside it in `AddPlaybookServices`; the compound-off path
  registers the Null facade, never the orchestrator) and two helpers. Zone B grep (SPEC §3.5.4) re-run: **0 matches**
  across `Services/Insights/`, `Api/Insights/`, `Models/Insights/`.
- **`PlaybookAuthorizationFilter.ParameterRejected`** made public (the shared 400 body).
No ADR-013 change (`IPlaybookService` is not imported outside the grandfathered set). No plugin (ADR-002). Fail closed
(ADR-003): a missing service, an unreadable or empty node list, a faulting Write question and an absent decision all deny
or answer "no Write". Forbidden edits re-checked against the base: no diff to FinanceAuthorizationFilter,
EntityAccessFilter, SemanticSearchAuthorizationFilter, AiAuthorizationFilter, IAiAuthorizationService /
AiAuthorizationService, OperationAccessPolicy, AuthorizationModule, any ExternalAccess file, or
`InsightsOrchestrator.EnrichParametersFromSubject` (called, not edited); no `ServiceClient.CallerId`.

### 15.9 Item 16 — publish size (CLAUDE.md §10), measured
Fresh short-path worktrees (`C:\wt163m`, `C:\wt163a`, `C:\wt163b`, then `C:\wt163c` for the final head), each `dotnet
restore` + `dotnet publish -c Release -o deploy/api-publish --no-restore` + the deploy script's web.config tweak +
**PowerShell `Compress-Archive` (Optimal) over `deploy/api-publish/*`, PDBs included**; 0 MSB3030 on every side:

| Tree | Commit | Zip | Files |
|---|---|---|---|
| fresh `origin/master` | `fb8280aee` | **45.66 MB** | 212 |
| f1 base (163 + 164-r1) | `eb59a67a6` | **35.10 MB** | 190 |
| f1 code head | `e705ea46b` | 35.10 MB | 190 |
| f1 final code head | `1a7018f2f` | **35.10 MB** | 190 |

**This round's contribution: +0.00 MB** (base → head, equal file counts). Versus master: **−10.56 MB**, entirely from
task 164-r1's removal of the `QuestPDF` package with the retired export routes; the 22-file difference is exactly the
QuestPDF assets (`LatoFont/`, `QuestPDF.dll`, `libQuestPdfSkia.so`, `libqpdf.so` — a directory diff of the two publishes).
Ceiling 60 MB respected. **CVE**: `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages" on
the f1 head AND on master; no package changed in f1. All four measurement worktrees removed.

### 15.10 Seeding proofs (f1) — each guard removed locally, named tests failed, restored with `git checkout` + touch
Script `seed163f1.py` (scratchpad); after each batch `git status` showed `src/` clean.

| Seed | Guard removed | Named tests that failed |
|---|---|---|
| S1 | subject always Read (the round-16 Write rule) | `Ask_PersistingPlaybook_AReaderWithoutWrite…`, `Ask_PersistingPlaybook_ACallerWithWrite_Runs_AndTheRunCarriesTheEstablishedWrite`, `Ask_TheMatterFormsLiveShape…`, `Ask_ANodeListThatCannotBeShownNotToWriteTheSubject_NeedsWrite` ×2 |
| S2 | an empty node list no longer Write | `Ask_ANodeListThatCannotBeShownNotToWriteTheSubject_NeedsWrite(no nodes)` |
| S3 | record parameters not authorized | `Ask_ARecordParameterIsAuthorizedAsTheCaller_ByTheSharedRule` ×5 |
| S4 | the shared parameter policy not applied | `Ask_AParameterTheSharedPolicyRefuses_Is400_ItsBody_BeforeAnyLookupOrRightsQuery` ×8 |
| S5 | an unreadable node list treated as empty | `Ask_ANodeListThatCannotBeRead_IsTheUniform404_EvenForAWriter(null)` |
| S7 | the facade's run guard bypassed | `AnswerQuestionAsync_WithoutEstablishedSubjectWrite_ARunThatCanWriteTheSubject_IsRefusedBeforeItsFirstNode` ×4, `AnswerQuestionAsync_AProjectSubject_IsGuardedThroughItsOwnKey`, `AssistantQuery_PlaybookPath_…` ×2 |
| S8 | the run guard accepts null/empty node lists | `…_IsRefusedBeforeItsFirstNode(no nodes / unreadable node list)` |
| S9 | single-shot Assistant path drops the Write | `AssistantQuery_PlaybookPath_CarriesTheRoutesEstablishedSubjectWrite_SingleShotAndStreaming(False)` |
| S10 | streaming Assistant path drops the Write | `…_SingleShotAndStreaming(True)` |
| S11a | the Assistant Write question always "no" | `Assistant_WhenAPlaybookMayRun_…_AndCarried(writer)` ×2 |
| S11b | `forceMode: rag` still asks Write | `Assistant_ForceModeRag_AsksNoWriteQuestion_AndTheRunCarriesNoWrite` |
| S12a/b/c | the Assistant endpoint's three refusal mappings | `Assistant_TheFacadesRunGuardRefusal_BeforeAnyFrame_IsTheUniform404(False)` / `(True)`; `…_MidStream_IsAnErrorFrame_WithItsCode_AndNoRecordId` |
| S13 | the `/ask` handler's refusal mapping | `Ask_TheFacadesRunGuardRefusal_IsTheSameUniform404` |
| S14 | ADR-019 `errorCode` on the RAG problems | the nine RAG `…_Is409…`, `…_ServiceFault_Is500…`, `IndexFile_PipelineFault…`, `Search_WithSessionId_Is400…` cases |
| S15 | the server-owned `tenantId` binding | `AnswerQuestionAsync_CacheRequest_CarriesAllInputFieldsCorrectly`, `AnswerQuestionAsync_TheRunsTenantId_IsTheRequestsTenant_NeverAParameterValue` |

Batches: 1 = S1 S4 S9 S11a S12a S14 (26 failed / 173); 2 = S2 S3 S7 S11b S12b S13 (16 / 173); 3 = S5 S8 S10 S12c (5 / 173);
4 = S15 (2 / 174). Every seed has at least one failure attributable only to it.

### 15.11 Tests changed in f1 (ADR-038: no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests)
| File | Change | Why |
|---|---|---|
| `InsightsRouteAuthorizationContractTests` (27 test methods) | the /ask section rewritten to the round-16 contract (persisting vs non-persisting node shapes mirroring the live/repo playbooks; Write cases; unreadable/empty node lists; the shared policy's refusals ×8; record parameters ×5; run-guard refusal); Assistant Write-question cases ×6 and refusal mappings ×3; the identifier-rule test REMOVED with the rule | item 1, item 4 |
| `RouteSweepAuthorizationHarness` | `INodeService` seam (default: no nodes = Write, fail closed), `CallerAccessSeam.ThrowFromCheckNumber`, `RouteSweepNodeShapes.NonPersistingNodeService()` | the route reads the node list |
| `InsightsOrchestratorTests` | node seam; run-guard tests (+theory cases); Assistant propagation single-shot and streaming; tenantId binding ×2 | §15.3, §15.2 |
| `PredictMatterCostPlaybookTests` | node seam (non-persisting shape) | ctor |
| `InsightEndpointsContractTests`, `Phase1SmokeTest`, `PredictMatterCostEvalHarnessTests` | node seam (non-persisting); `lookBackYears` / `matterType` → the declared text key `matterDescription`; the eval harness sends only `matterId` (the golden dataset is unchanged) | the shared allow-list refuses undeclared keys; no authorized-caller assertion weakened |
| `RagEndpointsAuthorizationContractTests` | `errorCode` + `code` asserted on the 409 / 400 / 500 cases | item 12(a) |
| `VersionSaveAiRefreshSeamTests` | the two deleted `IRagService` members removed from its fake | item 12(c) |

### 15.12 Test results (f1, code head `1a7018f2f`; later commits are notes/POML only)
| Run | Result |
|---|---|
| BFF build (`Sprk.Bff.Api`) | **0 warnings, 0 errors** (TreatWarningsAsErrors) |
| Affected, during the round | Insights contract + orchestrator + smoke + eval + playbook tests **585 / 585**; 164/163 neighbours (PlaybookAuthorizationFilter, PlaybookRouteAuthorizationContractTests, PlaybookParameterPolicyTests, ExecutorSideEffect*, AnalysisAuthorizationFilterTests, RAG/Workspace/retirement/RagServiceTenantGuard, SendToIndex, RagEndpointsTests, WorkspaceEndpointsContractTests, Finance, ChatContext, AnalysisEndpointsAuthorization, PlaybookQueryTextEscaping, AiPlaybookPromptRecordMatchRouteRetirement) **587 / 587**; RAG after the member removal **183 / 183** + **71 / 71**; Spe `KnowledgeBaseEndpointsTests` **2 / 2** |
| **Full BFF unit suite** (`tests/unit/Sprk.Bff.Api.Tests`, compiles `tests/integration/contract/**`, regression, seam, auth) | **Test Run Successful — 14,610 total: 14,556 passed, 54 skipped, 0 failed** (20.7 min, one clean single run) |
| **NetArchTest** (`tests/Spaarke.ArchTests`) | **346 / 346** (census 117) |
| **Sprk.Bff.Api.IntegrationTests** (full) | **101 / 101** (three fewer than f0's 104: task 164-r1 deleted `PlaybookByNameDeprecationTests` with its route) |
| **Spe.Integration.Tests** (full) | **412 total: 387 passed, 25 skipped (env-gated live smokes), 0 failed** |
| Zone B grep (SPEC §3.5.4) | 0 matches |
| Seeding | §15.10 — 15 seeds, every one bit |

Item 17 (a clean single full run) is therefore met in f1: the run above had no failures of any kind.

### 15.13 Conflict check (f1)
Sibling fix-round branches touching f1's files (diff from `91a1c1c83`): `task/uac-r2-162-f1` edits
`PlaybookAuthorizationFilter.cs` (a hunk near line 75 — f1's edits are at `ParameterRejected` and
`RecordParameterOperation`/`BuildSubjectRunChecksAsync`, ~lines 500-640) and `INodeExecutor.cs` (it adds
`ExecutorSideEffects`, which f1 extends at its end) — integration order is 162 → 164 → 163 (round 16 item 7), so f1
lands on top of both and any conflict is the append at the end of `ExecutorSideEffects`. `task/uac-r2-165-f1`,
`166-f1` and `167-f1` touch `RouteAuthorizationGuardTests.cs` (the census — recount at integration, §15.7 item 7);
`167-f1` also touches the Ledger/Scanner files (§15.7). No sibling touches `InsightEndpoints.cs`,
`InsightsAssistantEndpoint.cs`, `InsightsOrchestrator.cs`, `AssistantToolCallHandler.cs`, the two PublicContracts
models, `IRagService.cs` / `RagService.cs` / `NullRagService.cs`, or the Insights tests.
