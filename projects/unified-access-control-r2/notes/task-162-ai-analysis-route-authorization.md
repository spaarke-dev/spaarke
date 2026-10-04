# Task 162 — AI analysis routes: record authorization as the caller

> **Task**: `tasks/162-ai-analysis-routes-record-authorization.poml` (GitHub #1101; closes GitHub #233 **item 1 only**)
> **Branch**: `task/uac-r2-162` off `work/unified-access-control-r2` @ `91a1c1c83`
> **Sweep findings**: #1 export (critical), #2 GET (critical), #22 promote (high), #50 save (medium), #51 fork (medium), #52 execute (medium)
> **Outcome**: code complete and tested; **one escalation trigger fired and is OPEN** (§3, the 221 analyses with no anchor) — the GET rule is implemented fail-closed as the constraint states, but must not be integrated until the owner answers §3.

---

## 0. What changed, in one paragraph

Three of the six routes are **deleted** (owner round 10 item 1: no caller in the repo, not in any published API
description): `POST /api/ai/analysis/fork`, `POST /{analysisId}/save`, `POST /{analysisId}/export`, together with
every service, option, package and test only they used (§2). The other three now decide as the caller, before any
read or write, with every new check evaluated by task 130's `FinanceAuthorizationFilter`:

| Route | Decision (as the caller) | Deny answer |
|---|---|---|
| `GET /api/ai/analysis/{analysisId:guid}` | Read on **every** populated anchor (parent) of the analysis; the anchor ids are read app-only (ids only, never content) | uniform 404 (`sdap.access.deny.record_unavailable`), identical for unknown / denied / no-anchor / faulting / handler `KeyNotFoundException` |
| `POST /api/ai/analysis/promote` | filter: Create privilege on `sprk_analysis` + `analysis.attach` (Read+AppendTo) on the body document and regarding matter/project + playbook-use decision; handler: session owner (before the already-bound 400) + `analysis.attach` on the session-derived document | 403 `insufficient_privilege` / `insufficient_rights`, detail `"Access denied"`; not-your-session = the missing-session 404 |
| `POST /api/ai/analysis/execute` | unchanged Read filter first; then the run filter: Write on every document for the document-profile branch or a playbook that can write (zero nodes, an unclassifiable node, any side-effecting node), else Read; playbook-use decision for any non-profile playbook | 403 `insufficient_rights` / `system_failure` |
| `POST /api/ai/analysis/create` | **unchanged** (Read on the document via `IAiAuthorizationService`) | unchanged |

The `AnalysisAuthorizationFilter` switch default now **denies** (403 `sdap.access.error.system_failure`), and the
"TRACKED: GitHub #233" pass-through is gone.

---

## 1. Step 0 — live facts (spaarkedev1, 2026-10-03, READ-ONLY)

All reads used `az account get-access-token` + Web API GETs and one `RetrievePrincipalAccess` function call. No writes.

### 1.1 Lookup attributes on `sprk_analysis` (27) and their classification

`EntityDefinitions(LogicalName='sprk_analysis')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets`.
The classification is ONE static table, `AnalysisAuthorizationFilter.LookupColumns`; `EveryLiveAnalysisLookup_IsClassifiedExactlyOnce`
pins it to this snapshot.

| Column | Target | Role | Why |
|---|---|---|---|
| sprk_documentid | sprk_document | **document anchor** | the analysed document |
| sprk_outputfileid | sprk_document | **document anchor** | the document the output was saved as |
| sprk_regardingdocument | sprk_document | **document anchor** | typed regarding |
| sprk_regardingmatter | sprk_matter | **record anchor** (sprk_matters) | typed regarding / core-ancestor stamp |
| sprk_regardingproject | sprk_project | **record anchor** (sprk_projects) | typed regarding / core-ancestor stamp |
| sprk_regardingworkassignment | sprk_workassignment | **record anchor** (sprk_workassignments) | typed regarding / core-ancestor stamp |
| sprk_regardinginvoice | sprk_invoice | **record anchor** (sprk_invoices) | typed regarding |
| sprk_regardingbudget | sprk_budget | record anchor — **no entity set in EntityAccessFilter → denies** | typed regarding |
| sprk_regardingcommunication | sprk_communication | record anchor — **no entity set → denies** | typed regarding |
| sprk_regardingservicerequest | sprk_servicerequest | record anchor — **no entity set → denies** | typed regarding |
| sprk_actionid, sprk_agreementtype, sprk_playbook, sprk_regardingrecordtype | config rows | not an anchor | configuration (the polymorphic pair's type row; the typed lookup written with it is the anchor) |
| sprk_assignedattorney1/2, sprk_assignedparalegal1/2 | contact | not an anchor | ASSIGNEES of the analysis, not parents; the caller's rights on a contact row say nothing about the analysis (see §3 note) |
| sprk_reviewerby, createdby, createdonbehalfby, modifiedby, modifiedonbehalfby, ownerid, owningbusinessunit, owningteam, owninguser | systemuser/team/BU | not an anchor | principals and business units |

`sprk_matterid` **does not exist** on `sprk_analysis` (`Attributes?$filter=LogicalName eq 'sprk_matterid'` → 0). So the SPE
branch of the old save route could never run (its retrieve faulted into the fallback). Moot now: the save route is deleted (§2).
There is no `sprk_regardingevent` / `sprk_regardingtodo` column.

Entity sets (live `EntityDefinitions.EntitySetName`): sprk_documents, sprk_matters, sprk_projects, sprk_workassignments,
sprk_invoices, sprk_budgets, sprk_communications, sprk_servicerequests, sprk_analysisplaybooks, contacts, and
**`sprk_analysises`** for sprk_analysis (not "sprk_analyses"; used only as the Privilege check's deny-log label).

### 1.2 Row counts — active `sprk_analysis` (994 rows, all statecode 0)

| Populated anchor | Rows |
|---|---|
| sprk_documentid | 773 |
| every other anchor column | 0 |
| **no anchor at all** | **221** (escalation §3) |
| only the polymorphic pair | 0 |
| any anchor with no entity set (budget / communication / service request) | 0 |
| a contact assignee | 5 (4 of them with no anchor) |

The 221 anchorless rows by creator: Ralph Schroeder 155, `SDAP-BFF-SPE-API` 65, `# mi-bff-api-dev` 1. By playbook:
none 101, Document Profile 97, Full Contract Analysis 9, Quick Document Review 5, Document Summary 4, Email Analysis 2,
others 3. By month: 2025-12 20, 2026-01 92, 2026-02 22, 2026-03 53, 2026-04 3, 2026-05 3, 2026-07 20, 2026-08 7,
**2026-10 1** (a "Document Profile - 2026-10-02" row — something still creates anchorless analyses, or deleting a document
clears the lookup; not established, see §3).

### 1.3 Privileges and role depths

`privileges?$filter=startswith(name,'prvCreatesprk_analysis')` → **`prvCreatesprk_analysis`** (schema name `sprk_analysis`,
all lower case — pinned by `CreateAnalysisPrivilege_IsTheLiveName`).

Root roles (`roleprivilegescollection`, depth mask 1 Basic / 4 Deep / 8 Global):

| Role | Create sprk_analysis | AppendTo document | AppendTo matter | AppendTo project | Write document | Read playbook |
|---|---|---|---|---|---|---|
| Spaarke Core User | Deep | Deep | Deep | Deep | Deep | Deep |
| Spaarke Basic User | Deep | Deep | Deep | Deep | — | Deep |
| Spaarke AI Analysis User | Basic | — | — | — | — | Global |
| Spaarke AI Analysis Admin | Global | — | — | — | — | Global |

→ Escalation trigger 2 does **not** fire: ordinary users (Core + Basic) hold every privilege G5 needs.
Note: a user with **only** Spaarke Basic User has no Write on sprk_document, so the document-profile branch of execute
(Document Upload wizard AI summary) needs Core User; that is the intended rule (the branch writes the document).

### 1.4 Shipped execute consumers

| Consumer | Playbook (by `sprk_playbookid` alt key) | Public | Nodes | Branch | Needs |
|---|---|---|---|---|---|
| useAiSummary (Document Upload wizard) | Document Profile `18cf3cc8` | yes | AI Analysis(0), Update Record(22), Deliver To Index(41) | **document-profile** (the `document-profile` Binding row's `sprk_playbook` = `18cf3cc8`) | Write on the document |
| DocumentEmailWizard + SemanticSearch combined summary | Summarize File `4a72f99c` | **no** (owner: a root-BU user) | AI Analysis(0), Start(33) — read-only | engine | Read on the playbook row + Read on the documents |

The new child-BU test user (`uac.child.user`, round 11) gets **ReadAccess** on both playbooks
(`systemusers(d6f8f439…)/RetrievePrincipalAccess`). → Escalation trigger 3 does **not** fire.

---

## 2. Routes deleted (owner round 10 item 1) — evidence

| Route | No caller in the repo | Not published |
|---|---|---|
| `POST /api/ai/analysis/fork` | Grep of `src/**` (ts/tsx/js/cs, incl. every built PCF `bundle.js`): only the BFF itself and its contract tests. The analysis-hub client never wired it (project complete 2026-08-03). | `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml` publishes `/api/ai/analysis/create` and `GET /api/ai/analysis/{analysisId}` only; `spaarke-api-plugin.json` / `declarativeAgent.json` name no analysis path. No other OpenAPI / ai-plugin / declarativeAgent / manifest in the repo outside `knowledge/` samples. |
| `POST /api/ai/analysis/{analysisId}/save` | same grep: none | not in the OpenAPI |
| `POST /api/ai/analysis/{analysisId}/export` | same grep: none in product code. `scripts/load-tests/k6-ai-load-test.js` defined an UNCALLED `testExport()` posting to the literal path `/api/ai/analysis/{analysisId}/export` (never a GUID, so the `:guid` route constraint never matched) — not a caller; removed with its README row. | not in the OpenAPI |

Deleted with them (each grep-verified to have had no other consumer):
- `Api/Ai/AnalysisForkContracts.cs` (`AnalysisForkRequest/Response`); the fork handler.
- `IAnalysisOrchestrationService.SaveWorkingDocumentAsync` / `ExportAnalysisAsync` + impls (+ `ExtractSummary`, `ConvertToFormat`).
- `AnalysisResultPersistence.SaveToSpeAsync` / `GetExportService` / `RecordExport` and its `ExportServiceRegistry` + `AiTelemetry` ctor params.
- `IWorkingDocumentService.SaveToSpeAsync` + `WorkingDocumentService.SaveToSpeAsync` (the app-only SPE upload) and its now-unused `IServiceProvider` dependency.
- `Services/Ai/Export/PdfExportService.cs`, `AnalysisPdfDocument.cs`, `EmailExportService.cs`, `ExportServiceRegistry.cs`; the three `IExportService` DI registrations + the registry. (`DocxExportService` STAYS — `ChatWordExportEndpoints` uses it as a concrete type.)
- `AnalysisOptions.EnableDocxExport / EnablePdfExport / EnableEmailExport / EnableTeamsExport` (+ `appsettings.template.json`, `CONFIGURATION-MATRIX.md`, `ai-document-summary.md`).
- `AiTelemetry.RecordExport` and the `ai.export.*` instruments.
- **Package `QuestPDF`** (BFF and unit-test csproj) — its only consumers were the deleted PDF export files. Publish size should shrink; not measured (the task brief says skip).
- Models `AnalysisExportRequest`, `ExportResult`, `ExportDetails`, `AnalysisSaveRequest`, `SavedDocumentResult` (files renamed to the surviving types: `ExportFormat.cs` keeps `ExportFormat` + `ExportOptions`; `SaveDocumentFormat.cs` keeps `SaveDocumentFormat` — `IExportService`/`ExportContext` carry them).
- Tests of deleted code only: `AnalysisForkEndpointContractTests.cs`, `PdfExportServiceTests.cs`, `EmailExportServiceTests.cs`, `ExportServiceRegistryTests.cs`, the save/export regions of `AnalysisOrchestrationServiceTests`, the `SaveToSpeAsync` tests + `SavedDocumentResultTests` in `WorkingDocumentServiceTests`, the `AnalysisSaveRequest`/`ExportResult` tests in `AnalysisEndpointsTests`. `CapturingChatDataverseRepository` (which lived in the fork test file and is used by the promote and review-memo fixtures) moved unchanged into its own file.
- Guards: `SpeWriteSinkContainerProvenanceGuardTests` loses the `WorkingDocumentService.cs UploadSmallAsync` site; its Rule D `DirectStampRead` set is now **empty** (the pin stays). `SessionOwnershipGuardTests`' AnalysisEndpoints row now describes `/promote` (it described only `/fork` and silently exempted `/promote`).

Historical docs left as history: `docs/adr/ADR-013-ai-architecture.md` (full ADR tree listing PdfExportService), `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`.

⚠️ **Integration conflict to expect**: task 146 (`task/uac-r2-146*`) adds owner stamping to the FORK handler and to
`AnalysisResultPersistence`'s constructor. At integration, drop 146's fork hunk (the handler is gone) and merge the
constructor by hand (146 adds `IRecordOwnershipResolver`; 162 removes `ExportServiceRegistry` and `AiTelemetry`).
146's create and promote hunks are independent of 162's (162's checks run BEFORE the creates).

---

## 3. 🔔 Escalation trigger 1 FIRED — 221 active analyses have NO anchor (OPEN)

> Trigger: "Step 0 finds … rows with no anchor at all … This task would make them unreadable for everyone. STOP and
> report the counts."

**Counts**: 221 of 994 active `sprk_analysis` rows (22%) have no populated anchor column; 0 have only the polymorphic
pair; 0 have only an unresolvable anchor type. 4 of the 221 carry a contact assignee. One was created 2026-10-02
("Document Profile - 2026-10-02 13:18:49").

**Who is affected**: only `GET /api/ai/analysis/{analysisId}` — the only analysis-id route left. It has no first-party
UI caller (grep, §2); its one published consumer is the Copilot agent's `getAnalysis` operation. MDA forms read
`sprk_analysis` directly through Dataverse and are unaffected.

**What the branch does today**: constraint (5) as written — no anchor → zero checks → the evaluator denies → uniform
404 (`Get_DenyCases_AreTheSameUniform404("no-anchor")`). This is the fail-closed reading; nothing on the branch makes any
analysis readable that was not before, and the alternative (pass-through for anchorless rows) is a fail-open the ADR-003
constraint forbids. **The stop**: the GET rule for anchorless rows is NOT to be integrated until the owner chooses:

| Option | Effect | Recommendation |
|---|---|---|
| **(a) Accept** — anchorless analyses are unreadable through this route | 221 rows answer 404 via the Copilot `getAnalysis`; nothing else changes | **Recommended** (fail closed; no first-party caller; the rows have no parent to inherit from) |
| (b) Fall back to the caller's OWN Read on the `sprk_analysis` row when no anchor is populated | anchorless rows readable by whoever Dataverse says can read them (owner, team, role depth) | acceptable if (a) is too strict; a one-line change in `BuildAnchorTargets` + one test; note 65 of the rows are owned by the BFF app identity |
| (c) Backfill the anchor | data repair, not this task | only if the rows' documents still exist |

**Separate finding for the owner (not this task's scope)**: something keeps producing anchorless analyses (97 named
"Document Profile", latest 2026-10-02, 65 created by the BFF app identity). Either an app-only create path writes
`sprk_analysis` without `sprk_documentid` (candidates: the document-profile output storage in
`AnalysisResultPersistence`, `AppOnlyAnalysisService`), or deleting a document clears the lookup (relationship
"Remove Link"). Not established here; worth an issue. Task 146 already re-owns analysis rows on the profile path.

---

## 4. The other escalation triggers

| # | Trigger | Result |
|---|---|---|
| 2 | roles lack Create / AppendTo | not fired (§1.3) |
| 3 | a shipped consumer's playbook is unusable or side-effecting for a Read-only user | not fired (§1.4): Document Profile takes the profile branch (Write by design); Summarize File is private but readable and read-only |
| 4 | executor type not knowable before the run | not fired: `PlaybookOrchestrationService` picks the mode from `GetNodesAsync(playbookId)` (`nodes.Length == 0` → Legacy) and dispatches on `node.SprkExecutortype` directly (`:1036-1130`) — the filter reads the same source |
| 5 | task 164 landed a differing decision | not fired: 164 has no branch yet; the decision is built here (§6) |
| 6 | save destination must be repointed | moot: the save route is deleted; `sprk_matterid` does not exist (§1.1) |
| 7 | ADR conflict | `ADR013_AiBoundaryTests` (FR-C6) failed when `AnalysisAuthorizationFilter` resolved `IPlaybookService`. **§6.5 Path C (comply)**: `PlaybookAuthorizationFilter` (already the grandfathered owner of that lookup) gained an `IServiceProvider` overload of `BuildPlaybookUseCheckAsync`; the analysis filter calls that and never names `IPlaybookService`. No exception, no grandfather-list change. |
| 8 | live gate denies a legitimate shipped flow | pending (manual gate, §11) |

Owner round 12 items that touch this task: item 6 (the oid-vs-systemuserid playbook owner defect) is task 164's; the
decision built here never compares an owner at all (§6). Item 9 does not name 162's routes.

---

## 5. Side-effect classification (`ExecutorSideEffects`, next to `ExecutorType`)

Classified 2026-10-03 by reading every executor registered in `AnalysisServicesModule.AddNodeExecutors` and
`InsightsIngestModule`. A value missing from `ReadOnly` is treated as side-effecting (fail closed);
`ExecutorSideEffectClassificationTests` fails on any value in neither or both lists.

- **Side-effecting**: CreateTask (Dataverse to-do), SendEmail (Graph mail), UpdateRecord (Dataverse row), CallWebhook
  (no executor; outbound HTTP), SendTeamsMessage (no executor), DeliverToIndex (RAG indexing job), CreateNotification
  (appnotification), AgentService (Foundry agent — tool actions outside BFF control), ObservationEmit (insights index
  upsert + Dataverse mirror).
- **Read-only**: AiAnalysis (tool handlers Summary / DocumentClassifier / SemanticSearch / GenericAnalysis — none
  writes), AiCompletion, Condition, Start, DeliverOutput, DeliverComposite, QueryDataverse, LookupUserMembership,
  GroundingVerify, LiveFact, IndexRetrieve, EvidenceSufficiency, DeclineToFind, ReturnInsightArtifact, Sanitization,
  EntityNameValidator, LoadKnowledge, ReturnResponse; and the unregistered AiEmbedding, RuleEngine, Calculation,
  DataTransform, Parallel, Wait (a node of an unregistered type fails at dispatch, so it cannot act).
- Residual (recorded, constraint 7): execute's documents are the only Write targets. A node that writes OTHER records
  (a CreateTask's to-do, an UpdateRecord's target) is not checked per record; and QueryDataverse READS app-only with
  configured FetchXML. Both are playbook-AUTHORING concerns (the playbook-use decision gates who may run them).

---

## 6. For task 164 — what to reuse

- **Playbook-use decision**: `PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(IPlaybookService | IServiceProvider, playbookId, source, ct)`.
  Public (`sprk_ispublic`) → no check; otherwise (including not found) → a `FinanceCheckPath.Record` "read" check on
  `PlaybookService.EntitySetName` (now `internal`), so Dataverse's own answer decides (ownership, team POA shares, role
  depth). It never compares `OwnerId` with an oid. `InvokeAsync`, `AddPlaybookOwnerAuthorizationFilter` and
  `AddPlaybookAccessAuthorizationFilter` are untouched — switching those routes (and fixing round 12 item 6 there) is 164's.
- **Side-effect list**: `Services.Ai.Nodes.ExecutorSideEffects` (+ `AnalysisAuthorizationFilter.RunCanWriteDocuments(nodes)`
  for the zero-node / null-type / side-effecting rule).
- **Not checked here, on purpose**: promote's SESSION-derived `PlaybookId` (`session.PlaybookId`) — chosen at session
  create (sweep #24), 164's surface. The session-derived DOCUMENT is checked here (in the handler).
- Sweep noteworthy for 164: `GET /api/ai/chat/sessions/by-analysis/{analysisId}` can now use
  `AddAnalysisRecordAuthorizationFilter()` (the anchor rule) instead of `AiAuthorizationFilter` (which checks the analysis
  id as a document id and so always 403s).

---

## 7. Guards — which case applied

Task 167 is **not** on this branch (it is on `task/uac-r2-167*`), so: `Api/Ai/AnalysisEndpoints.cs` is added to
`RouteAuthorizationGuardTests.GovernedFiles` as `Scope.RouteLevelGate` with a reason; **no waiver** names a route in that
file; `FilterMarker` and `DecisionServices` are unchanged; Rule A, Rule B, the census and `NoWaiverIsStale` pass. The main
session reconciles with 167 at integration: delete 167's Pending waivers for these routes (and for the three deleted
routes) and record the ledger rows below.

---

## 8. Route authorization ledger input

| Route key | Mechanism that decides now | Deny test |
|---|---|---|
| `GET /api/ai/analysis/{analysisId:guid}` | filter: `AnalysisAuthorizationFilter` (AnalysisAccess) → `FinanceAuthorizationFilter` (Read on every anchor, Document + Record paths, OBO) | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Get_ReadableDocumentUnreadableMatter_IsUniform404` |
| `POST /api/ai/analysis/promote` | filter: `AnalysisAuthorizationFilter` (AnalysisPromote) → `FinanceAuthorizationFilter` (Privilege + analysis.attach + playbook-use) + handler session-owner / session-document checks | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Promote_BodyDocumentWithoutAttachRights_Is403` |
| `POST /api/ai/analysis/execute` | filters: `AnalysisAuthorizationFilter` (DocumentAccess, `IAiAuthorizationService`) then (AnalysisRun) → `FinanceAuthorizationFilter` | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Execute_ProfileBranchWithoutWrite_Is403BeforeAnyWrite` |
| `POST /api/ai/analysis/create` | filter: `AnalysisAuthorizationFilter` (DocumentAccess) — unchanged, not a finding | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.EveryMappedAnalysisRoute_HasADenyCase_AndDeniesACallerWithNoRights` |
| `POST /api/ai/analysis/fork` | **DELETED** (no caller, not published — §2) | n/a |
| `POST /api/ai/analysis/{analysisId:guid}/save` | **DELETED** (§2) | n/a |
| `POST /api/ai/analysis/{analysisId:guid}/export` | **DELETED** (§2) | n/a |

---

## 9. Seeded proofs

Each seed was applied by `seeds.py` (scratchpad), the BFF + unit/contract test assembly rebuilt, the task's tests run,
and the source restored byte-identically (asserted) and touched. S6 and S7 originally named the fork route; with fork
deleted, S6 targets the same check on promote.

| Seed | Change applied | Red | Tests turned red (display names; theory rows collapsed) |
|---|---|---|---|
| S1 | AnalysisAccess returns next (pass-through restored) | 12 | `162 GET: Read on EVERY populated anchor returns 200 with the service's payload; each anchor is asked on its own path and set`<br>`162 GET: a readable document and an unreadable regarding matter is DENIED (AND, not OR) with the uniform 404`<br>`162 GET: unknown, denied, no-anchor, pair-only, faulting, unmapped-type and token-less requests are ONE uniform 404, never echoing the id, service never invoked`<br>`162 completeness: every route mapped under /api/ai/analysis has a deny case, and each denies a caller the seams report as None`<br>`AnalysisAccess_InvalidAnalysisIdFormat_Returns400`<br>`AnalysisAccess_MissingAnalysisId_Returns400` |
| S2 | AND changed to OR across anchors (only the first anchor is checked) | 2 | `162 GET: Read on EVERY populated anchor returns 200 with the service's payload; each anchor is asked on its own path and set`<br>`162 GET: a readable document and an unreadable regarding matter is DENIED (AND, not OR) with the uniform 404` |
| S3 | the regarding-matter column dropped from the anchor select | 3 | `162 GET: Read on EVERY populated anchor returns 200 with the service's payload; each anchor is asked on its own path and set`<br>`162 GET: a readable document and an unreadable regarding matter is DENIED (AND, not OR) with the uniform 404`<br>`162: the anchor retrieve selects only anchor columns that exist live; every lookup to a sprk_document is a document anchor` |
| S4 | the no-anchor deny removed (zero checks turned into next) | 2 | `162 GET: unknown, denied, no-anchor, pair-only, faulting, unmapped-type and token-less requests are ONE uniform 404, never echoing the id, service never invoked` |
| S5 | a handler KeyNotFoundException body echoing the id | 1 | `162 GET: an analysis deleted between the check and the read answers the SAME uniform 404 (handler KeyNotFoundException)` |
| S6 | the promote body DocumentId check removed (substitute for the deleted fork route) | 4 | `162 promote: a body document the caller cannot Read AND AppendTo (or that does not exist) is 403 insufficient_rights, nothing written`<br>`162 promote: with no body document, a SESSION document the caller cannot attach to is 403 with the SAME body as a body-document deny, nothing written` |
| S7 | the promote Create-privilege check removed | 6 | `162 promote: another user's ALREADY-BOUND session is the same 404, not the already-bound 400`<br>`162 promote: another user's session, or one with no owner, is the 404 a missing session gets - byte-identical apart from correlationId - and nothing is written`<br>`162 promote: the caller's own session with the rights is 201 for the {sessionId, name} shape (session document checked as the caller)`<br>`162 promote: with no body document, a SESSION document the caller cannot attach to is 403 with the SAME body as a body-document deny, nothing written`<br>`162 promote: without the Create privilege on sprk_analysis is 403 insufficient_privilege and writes nothing` |
| S8 | "analysis.attach" weakened to Read only | 3 | `162 promote: a body document the caller cannot Read AND AppendTo (or that does not exist) is 403 insufficient_rights, nothing written`<br>`162 promote: a regarding matter/project the caller cannot Read AND AppendTo (or that does not exist) is 403, asked of the right entity set, nothing written`<br>`162 promote: with no body document, a SESSION document the caller cannot attach to is 403 with the SAME body as a body-document deny, nothing written` |
| S9 | the promote owner comparison removed | 3 | `162 promote: another user's ALREADY-BOUND session is the same 404, not the already-bound 400`<br>`162 promote: another user's session, or one with no owner, is the 404 a missing session gets - byte-identical apart from correlationId - and nothing is written` |
| S10 | the promote owner comparison moved after the already-bound 400 | 1 | `162 promote: another user's ALREADY-BOUND session is the same 404, not the already-bound 400` |
| S11 | the promote session-document check removed | 1 | `162 promote: with no body document, a SESSION document the caller cannot attach to is 403 with the SAME body as a body-document deny, nothing written` |
| S12 | the execute profile-branch Write check removed | 1 | `162 execute: document-profile branch with Read but not Write is 403 before any field write, indexing enqueue or AI call` |
| S13 | the playbook-use check removed (promote and execute) | 4 | `162 execute: engine branch - a playbook not public and not readable, or nonexistent, is the same 403; a public one needs no Read on its row`<br>`162 promote: a body playbook that is not public and not readable, or does not exist, is the same 403; a public one needs no Read on its row` |
| S14 | the playbook-use decision switched to an owner comparison (an owned playbook is usable without asking Dataverse) | 1 | `162: a non-public or unknown playbook gets a Record-path Read check on its own row; Dataverse decides and the owner id is never consulted` |
| S15 | one side-effecting ExecutorType (UpdateRecord) reclassified read-only | 2 | `162 execute: engine branch with a side-effecting node, a node with no executor type, or zero nodes needs Write on every document`<br>`162: the executors that create, update, send, enqueue or delegate are side-effecting` |
| S16 | the zero-node (Legacy) case treated as read-only | 2 | `162 execute: engine branch with a side-effecting node, a node with no executor type, or zero nodes needs Write on every document`<br>`162: a run can write the documents when it has no nodes, an unclassifiable node, or any side-effecting node` |
| S17 | the switch default restored to next | 1 | `162: an AuthorizationMode with no case DENIES with 403 system_failure and never calls next` |

Every seed turned at least one named test red; after each, the file was restored byte-identically (asserted by the runner) and the working tree showed no source change. S9 and S11 were re-seeded once: their first spelling (`callerOid is null`) failed to COMPILE under nullable analysis, which proves nothing, so they were re-run with a non-nullable spelling.

---

## 10. Tests

All on this branch, 2026-10-03, after the final code (the seeded runs are in §9).

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror` | Build succeeded |
| Task tests (contract + filter unit + side-effect + mode pin), 79 cases | 79/79 pass |
| Affected suites (`~Analysis` / `~ReviewMemo` / `~Promote`, incl. promote + execute-dispatch fixtures) | 448 pass, 7 skipped (pre-existing), 0 fail |
| `OperationAccessPolicyCompletenessTests` + `PlaybookAuthorizationFilterTests` (unchanged) | pass |
| **Full BFF unit suite** `dotnet test tests/unit/Sprk.Bff.Api.Tests` | 14 119 pass, 54 skipped, **104 failed — every one `TaskCanceledException: The operation was canceled`** (HTTP test-host timeouts, durations 2–9 min; 8 other `testhost` processes from other agents were running). **Re-run in isolation**: all 104 (101 methods, 111 cases incl. theory rows) **pass**. None is in an area this task touched (Office, Insights, Compose, SpeAdmin, Workspace, health headers). Contention, reported both ways per the brief. |
| **NetArchTest** `dotnet test tests/Spaarke.ArchTests` | 346/346 pass |
| **Integration** `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | 104/104 pass |
| **Integration** `tests/integration/Spe.Integration.Tests` (full) | 403 pass, 25 skipped (pre-existing), 0 fail |
| `dotnet list package --vulnerable --include-transitive` (BFF) | no vulnerable packages (QuestPDF removed; nothing added) |
| Publish size | skipped per the task brief; expected to SHRINK (QuestPDF + four export files removed) |

Existing-test changes beyond the new tests (each one-line justified):
- `ExecuteAnalysis_WithPlaybookNotFound_ReturnsErrorChunk` → `…_Returns403BeforeTheStream` — the one expectation the POML inverts on purpose (an unknown playbook is now refused before the stream, identical to a denied one).
- The SPE `AnalysisTestFixture`, the promote fixture and the execute-dispatch fixture now return EXPLICIT allow/deny decisions (playbooks, nodes, access rights) — a loose mock's null would deny under this task.
- Tests of deleted code were deleted with it (§2); `CapturingChatDataverseRepository` moved unchanged to its own file because the promote and review-memo fixtures still use it.
- `SpeWriteSinkContainerProvenanceGuardTests`, `SessionOwnershipGuardTests`, `RouteAuthorizationGuardTests`, `OperationAccessPolicyCompletenessTests`: guard data updated for the deleted site / the promote row / the new governed file / the scope note.

### Quality gates (task-execute Step 9.5)

**code-review** (thorough, coverage-first): no Critical. Warnings / suggestions recorded, none blocking:
- W1 (medium confidence): `AnalysisAuthorizationFilter` (Api/Filters) now depends on `INodeService` and `IConsumerRoutingService` from the AI layer. ADR-013/FR-C6 forbids only `IOpenAiClient`/`IPlaybookService` outside `Services/Ai` (the arch test passes); the analysis routes ARE the AI API surface, the same as the grandfathered `PlaybookAuthorizationFilter`. Acceptable; noted for the ADR-013 owners.
- S1: the declaration-fault path hands the evaluator a throwing resolver so the deny renders through `FinanceAuthorizationFilter` (one deny shape, its log line). Unusual but documented at the call site; the alternative was editing FinanceAuthorizationFilter (forbidden) or a second deny renderer.
- S2: `/execute` now answers the rights 403 before the handler's multi-document 400 for a caller lacking Write; both are refusals; no information leaks.
- S3: `AnalysisAuthorizationFilter.cs` grew to ~650 lines, but it is ONE filter with four modes and a declarative column table — cohesive (one reason to change: who may act on an analysis). `AnalysisEndpoints.cs` SHRANK (1576 → 1349).
- Metrics: no new interfaces; no ctor-parameter growth (`WorkingDocumentService` and `AnalysisResultPersistence` each LOST parameters); no catch-log-rethrow; net −1 613 lines.

**adr-check**: compliant — ADR-001 (Minimal API, no new routes), ADR-002 (no plugins; decisions in the BFF), ADR-003 (fail closed: switch default, every fault, missing registration, null node list), ADR-007 (no Graph types; one SPE write SITE removed), ADR-008 (every check in the route's own filter chain; the session-derived checks in the handler per the #863 precedent, listed in SessionOwnershipGuardTests), ADR-010 (no new interface, service or registration; three registrations removed), ADR-013 (FR-C6 hit once → §6.5 Path C, §4 row 7), ADR-019 (reasonCode on every deny), ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; real-host deny tests). No violation.

BFF hygiene (CLAUDE.md §10): **placement** — in the BFF: these are existing BFF routes and filters; no new service, endpoint, registration, option, job, column or package (`.claude/constraints/bff-extensions.md` §A). Package REMOVED: QuestPDF. CVE check clean.

---

## 11. Manual live gate (main session; dev; existing non-admin users in their current BUs; verification reads only)

Adjusted from the POML for the deleted routes. Ids redacted to 8 characters in the results.

- [ ] (a) For an analysis anchored to a document U1 cannot read: `GET /api/ai/analysis/{id}` as U1 → 404 with
      `reasonCode sdap.access.deny.record_unavailable`; a random GUID → the identical body.
- [ ] (b) A user with Read on that document (and every other anchor) → 200 on GET.
- [ ] (c) **After the owner answers §3**: an anchorless analysis (one of the 221) → per the chosen option.
- [ ] (d) U1 promoting its own session with `regardingEntityId` = a matter U1 cannot read → 403; no `sprk_analysis`
      with that name exists afterwards.
- [ ] (e) U1 promoting ANOTHER user's session id → 404 "Session not found"; that session's
      `sprk_aichatsummary.sprk_analysis` unchanged.
- [ ] (f) Execute with the Document Profile playbook on a document U1 can Read but not Write → 403; the document's
      `modifiedon` unchanged.
- [ ] (g) `/fork`, `/{id}/save`, `/{id}/export` → 404 (route gone) for everyone.
- [ ] (h) Users with the expected rights still complete: Document Upload wizard AI summary (needs Write on the new
      document — Core User), DocumentEmailWizard summary, SemanticSearch combined summary, HistoryOverlay "set related",
      the reviewed-document promote. The child-BU user `uac.child.user` (round 11) is a good U1 for (d)–(f) and a good
      positive for the Summarize File consumer (it has Read on that playbook, §1.4).

---

## 12. `.claude/**` edits needed

None. (`.claude/skills/context-handoff/SKILL.md:282-287` mentions `EmailExportService` only as a worked example of a
checkpoint, not as guidance; leave it.)
