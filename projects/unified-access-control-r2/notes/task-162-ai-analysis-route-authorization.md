# Task 162 — AI analysis routes: record authorization as the caller

> **Task**: `tasks/162-ai-analysis-routes-record-authorization.poml` (GitHub #1101; closes GitHub #233 **item 1 only**)
> **Branch**: `task/uac-r2-162` off `work/unified-access-control-r2` @ `91a1c1c83`
> **Sweep findings**: #1 export (critical), #2 GET (critical), #22 promote (high), #50 save (medium), #51 fork (medium), #52 execute (medium)
> **Outcome**: code complete and tested. The §3 escalation (221 analyses with no anchor) is **DECIDED** by owner round 15 and
> **implemented** in fix round f1 (§14): classified (every one lost its anchor AFTER creation — no writer drops it), every
> writer proven + tested, an anchor backfill script (dry run: 0 derivable), and the PERSONAL (creator-only, by systemuserid)
> branch of the ONE analysis-read rule. Round 25 item 2 (POST /create adopts G5) is implemented (§14.4). The manual live
> gate (§11, updated in §14.11) is the only open criterion.

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

## 3. Escalation trigger 1 FIRED — 221 active analyses have NO anchor (DECIDED: owner round 15; implemented in §14)

> **Superseded.** The options table below was the round-1 stop. Owner round 15 REJECTED "accept the 404", "row-Read
> fallback" and "backfill only" as partial, and decided the complete fix; round 25 item 2 confirmed it. What was built is in
> §14. The text below is kept as the record of the stop.

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
| `GET /api/ai/analysis/{analysisId:guid}` | filter: `AnalysisAuthorizationFilter` (AnalysisAccess) → the ONE analysis-read rule `ResolveAnalysisReadTargetsAsync` → `FinanceAuthorizationFilter` (Read on every anchor, Document + Record paths, OBO; no anchor → creator by WhoAmI systemuserid + the Read privilege, f1) | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Get_ReadableDocumentUnreadableMatter_IsUniform404`; personal: `…Get_NoAnchor_NotTheVerifiedCreator_IsUniform404` |
| `POST /api/ai/analysis/promote` | filter: `AnalysisAuthorizationFilter` (AnalysisPromote) → `FinanceAuthorizationFilter` (Privilege + analysis.attach + playbook-use) + handler session-owner / session-document checks | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Promote_BodyDocumentWithoutAttachRights_Is403` |
| `POST /api/ai/analysis/execute` | filters: `AnalysisAuthorizationFilter` (DocumentAccess, `IAiAuthorizationService`) then (AnalysisRun) → `FinanceAuthorizationFilter` | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Execute_ProfileBranchWithoutWrite_Is403BeforeAnyWrite` |
| `POST /api/ai/analysis/create` | filter: `AnalysisAuthorizationFilter` (AnalysisCreate, f1; round 25 item 2) → `FinanceAuthorizationFilter` (G5: Create privilege + analysis.attach on the document + playbook-use + Read on each scope row) | `Sprk.Bff.Api.Tests.Api.Ai.AnalysisEndpointsAuthorizationContractTests.Create_DocumentWithoutAttachRights_Is403` |
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

**Verifier round 1 (branch `task/uac-r2-162-r1`, 2026-10-03)** — three more seeds, for the three gaps the verifier
found. Each was applied by the runner to `AnalysisAuthorizationFilter.cs`, the test assembly rebuilt, the task's
contract + filter tests run, and the file restored byte-identically (SHA-256 asserted) and touched; `git status`
showed only this round's intended edits afterwards.

| Seed | Change applied | Red | Tests turned red |
|---|---|---|---|
| S21 | an anchor whose type has no entity set is SKIPPED (`continue`) instead of rejected (the verifier's seed, which survived before this round) | 6 | `162 GET: a READABLE document anchor beside an anchor whose type has no entity set (budget, communication, service request) is still the uniform 404 — the unmapped anchor is rejected, never skipped` (3 rows)<br>`162: an anchor whose type has no entity set REJECTS the whole analysis even when a readable document anchor sits beside it — it is never skipped` (3 rows) |
| S19p | the PROMOTE check-declaration catch turned into `next` | 1 | `162 promote: a fault while declaring the checks (the body playbook's lookup throws) denies 403 system_failure and writes nothing` |
| S22 | the evaluator resolved with `GetRequiredService` again (an unresolvable evaluator throws → 500) | 4 | `162: on /execute, an evaluator that cannot be resolved (not registered, or a dependency missing) DENIES 403 system_failure — not a 500, never next` (2 rows)<br>`162: on GET, an evaluator that cannot be resolved answers the uniform 404 — not a 500, never next` (2 rows) |

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
| Publish size (measured by the round-1 VERIFIER, recorded here in round 1; CLAUDE.md §10 procedure) | **Base** `91a1c1c83` (fresh worktree `C:\wvs162m`): **45.66 MB, 212 files**. **Branch** `task/uac-r2-162` (fresh worktree `C:\wvs162p`): **35.11 MB, 190 files**. **Delta −10.55 MB.** Both zipped with PowerShell `Compress-Archive` (Optimal) over the publish folder, **PDBs included**, from short paths. The file counts differ by exactly the 22 QuestPDF files this task deletes with the package (`QuestPDF.dll`, `libQuestPdfSkia.so`, `libqpdf.so` and 19 `LatoFont` files); every other file is on both sides, so the measurement is sound. Well under the 60 MB ceiling. (Round 1 changed one filter method and one comment — no package, file or reference — so the figure stands for `-r1`; not re-measured, per the round-1 brief.) |

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

BFF hygiene (CLAUDE.md §10): **placement** — in the BFF: these are existing BFF routes and filters; no new service, endpoint, registration, option, job, column or package (`.claude/constraints/bff-extensions.md` §A). Package REMOVED: QuestPDF. CVE check clean. Publish size: 45.66 → 35.11 MB (−10.55 MB), see the table above.

---

## 11. Manual live gate (main session; dev; existing non-admin users in their current BUs; verification reads only)

Adjusted from the POML for the deleted routes. Ids redacted to 8 characters in the results.

- [ ] (a) For an analysis anchored to a document U1 cannot read: `GET /api/ai/analysis/{id}` as U1 → 404 with
      `reasonCode sdap.access.deny.record_unavailable`; a random GUID → the identical body.
- [ ] (b) A user with Read on that document (and every other anchor) → 200 on GET.
- [ ] (c) **Owner round 15 (decided; f1)**: an anchorless analysis (one of the 221) → `GET` as a user who did NOT create it →
      the identical uniform 404; as Ralph Schroeder (its `createdby` for 155 of them) → 200. An app-created one (`createdby` =
      `SDAP-BFF-SPE-API` / `# mi-bff-api-dev`, 66 rows, no person recorded) → 404 for everyone. Steps (i)-(l) in §14.11.
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

---

## 13. Adversarial verifier, round 1 — item by item (branch `task/uac-r2-162-r1` off `task/uac-r2-162`)

The verifier found 14 items. Items 1–5 and 9–12 confirm the code, with no change asked. Items 6/14, 7, 8 and 13 asked
for changes. All are closed below. Nothing else was changed.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | Route findings closed; deletions verified (no caller, not published) | Confirmed. No change. |
| 2 | Both integration suites, ArchTests, task tests green in the verifier's run | Confirmed. Re-run in full this round (§10 / table below). |
| 3 | 15 unit-suite contention timeouts, all green in isolation | Confirmed as contention. This round's full run is reported below. |
| 4 | Publish size measured by the verifier: −10.55 MB | **Recorded** in §10 and in the POML `<placement>` (item 13). |
| 5 | All 17 seeds re-run red by the verifier; 5 extra seeds red | Confirmed. No change. |
| 6 / 14 | **TEST GAP**: seed S21 (skip an anchor with no entity set instead of rejecting it) SURVIVED, because the only case seeded the budget anchor ALONE | **Fixed.** Added two tests, each with a **readable document anchor beside** an unmapped anchor, covering every unmapped anchor type: budget, communication and service request. (1) Real host: `Get_ReadableDocumentBesideAnAnchorWithoutEntitySet_IsUniform404` (3 rows). The caller gets full rights on the document AND on a guessed entity set for the other anchor, and the orchestrator is set up to answer 200. The test asserts the uniform 404 and that `GetAnalysisAsync` is never called. (2) Unit: `BuildAnchorTargets_UnmappedAnchorBesideADocument_Rejects` (3 rows). It asserts a Rejection and zero checks. **S21 now turns 6 tests red** (§9). Criterion "Anchor rule (c)" is now proven, not vacuous. |
| 7 | No test proved that a PROMOTE check-building fault denies | **Fixed.** Added `Promote_CheckDeclarationFault_Denies`: `GetPlaybookAsync` throws for the body PlaybookId. The test asserts 403 `sdap.access.error.system_failure` and that nothing is written (no `CreateAnalysisAsync`, no session bind). Seed S19p (only the promote catch turned into `next`) turns it red (§9). |
| 8 | `EvaluateAsync` called `GetRequiredService<AuthorizationService>()` outside any try. A missing registration gave a 500 instead of the 403 `system_failure` that ADR-003 describes. | **Fixed** in `AnalysisAuthorizationFilter`. Both `EvaluateAsync` and `EvaluateFaultAsync` now resolve the evaluator through `ResolveEvaluatorOrNull`. It uses `GetService` inside a try/catch, so it covers a service that is not registered AND one that is registered but whose dependency is missing (resolution throws). Either way the filter denies through `DenyEvaluatorUnavailable` in the route's own deny shape: the **uniform 404 on GET**, so this path is no oracle either, and **403 `sdap.access.error.system_failure`** on promote and execute. It never calls next. Tests: `Run_EvaluatorUnavailable_Denies403SystemFailure` and `Get_EvaluatorUnavailable_IsTheUniform404`, 2 rows each (not registered / unresolvable). Seed S22 (`GetRequiredService` restored) turns all 4 red (§9). These are behaviour tests of the fail-closed branch, not DI-registration tests (ADR-038): each asserts the HTTP result and that next is never called. `FinanceAuthorizationFilter.cs` is untouched. Its own extension method has the same `GetRequiredService` shape, but it is out of this task's scope and is recorded as an observation for task 170 (the rename/generalization). The pre-existing `AddAnalysisAuthorizationFilter` lambda resolves `IAiAuthorizationService` with `GetRequiredService` the same way. That code is unchanged and outside this finding; it never calls next. |
| 9 | Ten behaviours checked and found correct | Confirmed. No change. |
| 10 | `ExecutorSideEffects` classification checked against every registered executor | Confirmed. The residual risk is already recorded in §5. No change. |
| 11 | No ADR-038 violations; no live writes; no `.claude/**` edits; no client broken | Confirmed. This round adds no `Mock<HttpMessageHandler>`, no DI-registration test and no ctor null-check test. No live calls of any kind. |
| 12 | Comments accurate. The handler comment on the PlaybookId 400 "slightly overstates" | Comment corrected in `AnalysisEndpoints.ExecuteAnalysis`. The branch is reached "only when the handler runs without that filter (a direct call, or the filter removed from the chain)". No code change. |
| 13 | **Criterion not met**: publish size not reported; the full unit-suite run was not clean | **Closed.** Publish size recorded (§10, POML `<placement>`): base 45.66 MB / 212 files, branch 35.11 MB / 190 files, −10.55 MB, Compress-Archive Optimal, PDBs included. The file-count difference is exactly the 22 deleted QuestPDF files, so the sides are otherwise equal. This round re-ran the full unit suite. Its result, including any contention timeouts and their isolated re-run, is in the table below. |

**Round-1 conflict check.** No open PR touches `AnalysisAuthorizationFilter.cs`, `AnalysisEndpoints.cs` or the
contract test (`gh pr list`, file filter). Same-project branches touching these files since `2026-09-25`: only
`task/uac-r2-146*` (`AnalysisEndpoints.cs` fork/promote/create owner stamping, already recorded in §10 and in the POML
`<conflict-check>`). This round's only `AnalysisEndpoints.cs` edit is one comment inside `ExecuteAnalysis`, a hunk 146
does not touch.

**Round-1 placement / justification.** This round adds no service, registration, endpoint, option, job, column or
package. Two private methods were added to the existing filter (`ResolveEvaluatorOrNull`, `DenyEvaluatorUnavailable`),
and `EvaluateAsync` changed from static to instance so it can log.

**Round-1 tests** (branch `task/uac-r2-162-r1`, 2026-10-03):

| Run | Result |
|---|---|
| `dotnet build tests/unit/Sprk.Bff.Api.Tests -warnaserror` / `dotnet build src/server/api/Sprk.Bff.Api -warnaserror` | Build succeeded, 0 warnings |
| Task + affected tests (contract, filter unit, mode pin, side-effect, promote / execute-dispatch / review-memo / PromoteDurableFk fixtures, PlaybookAuthorizationFilterTests) | 144/144 pass (11 new cases this round) |
| Seeds S21 / S19p / S22 | 6 / 1 / 4 red; each restored byte-identically |
| **Full BFF unit suite** | 14 224 pass, 54 skipped, **10 failed, every one `TaskCanceledException: The operation was canceled`**. These are HTTP test-host timeouts of 3 m 17 s to 3 m 33 s, hit while other agents' `testhost` processes were running. The areas are Compose seams and contracts, the document identity and profile contracts, external-access upload, and MI route retirement; this task touched none of them. **Re-run in isolation, all 10 pass (11 cases, counting a theory row).** This is contention; both results are reported. |
| **NetArchTest** | 346/346 pass |
| **Sprk.Bff.Api.IntegrationTests** (full) | 104/104 pass |
| **Spe.Integration.Tests** (full) | 403 pass, 25 skipped (already skipped before this task), 0 fail |

---

## 14. Fix round f1 — owner round 15, round 25 item 2, verifier round 2 (branch `task/uac-r2-162-f1` off `task/uac-r2-162-r1` @ `d1c5e6f34`)

Binding inputs read on `work/unified-access-control-r2` (`dee2d506b`): session-27 note rounds 1–25 (round 15 = this task's
anchorless-analysis decision; round 16 item 7 = 162 integrates before 164; round 25 item 2 = `/create` adopts G5; round 25
item 4 = 164's chat analysis host is decided by THIS task's analysis-read rule, one shared declaration) and the worktree's
`NOTE-FROM-MAIN.md` (escalations decided; never defer, never offer accept/sideline). Live Dataverse was read only (GETs,
`RetrieveAuditDetails`, `RetrievePrincipalAccess`, `RetrieveRolePrivilegesRole`); nothing was written live.

### 14.1 Round 15 item 1 — WHY the 221 analyses have no anchor (live, spaarkedev1, 2026-10-04)

Reproducible with `scripts/Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Classify`
(read-only; output below). 996 active `sprk_analysis` rows; **221 have no anchor** — the same 221 as round 1.

**The cause is parent deletion, not a writer.** The relationship `sprk_document_analysis_document`
(`sprk_analysis.sprk_documentid` → `sprk_document`) has **Delete = RemoveLink** (live `ManyToOneRelationships` metadata;
so do all 19 anchor/config lookups of `sprk_analysis`, and all 15 `sprk_document` → child relationships). Deleting a
document therefore **clears** its analyses' `sprk_documentid`. Attribute auditing is off for `sprk_documentid`, but entity
auditing is on for `sprk_document`: the audit log holds **452 deleted documents** (2025-09-29 → 2026-10-02), each with its
old name via `RetrieveAuditDetails`. Matching each anchorless analysis to them (by the document-id prefix the writer put in
the analysis name, by the document name, or by creation within 10 minutes of the document's create — the document-profile
pattern):

| Writer (inferred from the name pattern + creator) | parent deleted — proven (exactly one deleted document) | parent deleted — several candidates | no audit match | total |
|---|---|---|---|---|
| Playbook Library / Analysis Builder create, earlier direct client create (`"Analysis - <document>"`, createdby Ralph Schroeder) | 67 | 73 | 7 | **147** |
| Playbook Library / Analysis Builder via `POST /api/ai/analysis/create` (BFF app identity) | 1 | 0 | 3 | **4** |
| `AppOnlyAnalysisService` document profile / `AnalysisResultPersistence` (`"Document Profile - <ts>"`, `SDAP-BFF-SPE-API` 60 + `# mi-bff-api-dev` 1) | 25 | 2 | 34 | **61** |
| Create Analysis wizard (user-named, client `Xrm.WebApi`, createdby Ralph Schroeder) | 4 | 0 | 4 | **8** |
| `/promote` or `/create`, user-named, BFF app identity | 0 | 1 | 0 | **1** |
| **total** | **97** | **76** | **48** | **221** |

Per creator: Ralph Schroeder 155, `SDAP-BFF-SPE-API` 65, `# mi-bff-api-dev` 1. The newest row
(`6d0851cb`, "Document Profile - 2026-10-02 13:18:49", `# mi-bff-api-dev`) belongs to the restart-probe test documents the
BFF itself deleted at 13:28 that day (`restart-probe-10021222`, `restart-midflight-10021225`, `restart-kill2-10021322`) —
"something still creates anchorless analyses" is test-document deletion, not a writer.

**Classes per round 15:** (i) *created in a record's context by a writer that failed to record the anchor* = **0** (every
writer records it — §14.2); (i′) *created in a record's context, anchor cleared later by the parent's deletion* = **173**
proven or consistent, **48** with no audit match (their writers' code also always binds a document); (ii) *genuinely
standalone* = **0 identified** — no live writer can create one (the shared seam's FR-D9 guard, the wizard's guard, the MDA
form holds `sprk_documentid` ApplicationRequired). The analysis-read rule cannot tell (i′) from (ii) at request time and does
not need to: **every anchorless row is PERSONAL** (§14.3).

**Derivable anchors: 0.** The dry run checked every authoritative source: the polymorphic pair (0 rows carry it), the
analysis's chat sessions (`sprk_aichatsummary.sprk_documentid`; 2 sessions, one names a deleted document, one none), the
output document (`sprk_outputfileid` is itself an anchor, so no anchorless row has it). A source naming a deleted row is
reported, never written.

```
Repair-AnalysisAnchors — DRY RUN — https://spaarkedev1.crm.dynamics.com
Active analyses: 996; with no anchor: 221
Audit: 452 deleted documents
   73  Playbook Library / Analysis Builder create (earlier direct client create), parent-deleted (ambiguous)
   67  Playbook Library / Analysis Builder create (earlier direct client create), parent-deleted (proven)
   34  AppOnlyAnalysisService document profile / AnalysisResultPersistence (BFF app identity), unattributed
   25  AppOnlyAnalysisService document profile / AnalysisResultPersistence (BFF app identity), parent-deleted (proven)
    7  Playbook Library / Analysis Builder create (earlier direct client create), unattributed
    4  Create Analysis wizard (client Xrm.WebApi, user-named), parent-deleted (proven)
    4  Create Analysis wizard (client Xrm.WebApi, user-named), unattributed
    3  Playbook Library / Analysis Builder create (POST /api/ai/analysis/create), unattributed
    2  AppOnlyAnalysisService document profile / AnalysisResultPersistence (BFF app identity), parent-deleted (ambiguous)
    1  Playbook Library / Analysis Builder create (POST /api/ai/analysis/create), parent-deleted (proven)
    1  POST /api/ai/analysis/promote or /create (user-named, BFF app identity), parent-deleted (ambiguous)
Derivable anchors (the -Apply plan): 0 row(s)
  note e8f49896: its chat session names a document that no longer exists
DRY RUN — nothing written. Re-run with -Apply to write the plan.
```

### 14.2 Round 15 item 2 — every writer records its anchor (ADR-002 WP-1), each pinned by a test that fails if it is dropped

Writers found by grep of `src/**` (`CreateAnalysisAsync(`, `new Entity("sprk_analysis")`, `createRecord('sprk_analysis'`,
`sprk_analysises`) — there are no others:

| Writer | Anchor it records | Test that reddens if the anchor is dropped |
|---|---|---|
| `DataverseServiceClientImpl.CreateAnalysisAsync` — the shared seam (the one server-side invariant owner) | refuses a create with neither a document nor a regarding target (FR-D9 `ArgumentException`), binds `sprk_documentid`, stages the typed regarding lookup | `AnalysisRegardingWriteTests` (the stager); every BFF writer below goes through this seam |
| `AppOnlyAnalysisService.AnalyzeDocumentAsync` (document profile, the 65 app-created rows' main writer) | `sprk_documentid` = the profiled document | `AnalysisWriterAnchorTests.DocumentProfile_AnchorsTheAnalysisToItsDocument` (seed W1) |
| `AppOnlyAnalysisService.AnalyzeEmailAsync` | `sprk_documentid` = the .eml document | `AnalysisWriterAnchorTests.EmailAnalysis_AnchorsTheAnalysisToTheEmailDocument` (W2) |
| `AnalysisResultPersistence.StoreDocumentProfileOutputsAsync` (create branch) | `sprk_documentid` | `AnalysisWriterAnchorTests.ProfileOutputStorage_CreateBranch_AnchorsTheAnalysisToItsDocument` (W3) |
| `POST /api/ai/analysis/create` | `sprk_documentid` = the body document | `AnalysisEndpointsAuthorizationContractTests.Create_WithTheG5Rights_Is201AndAnchorsTheDocument` (W4) |
| `POST /api/ai/analysis/promote` | body/session document and/or the regarding matter/project | `Promote_OwnSessionReviewedDocumentShape_Is201` (document) and `Promote_OwnSessionWithRegardingMatter_Is201` (regarding, W5) |
| Insights observation mirror (`ObservationMirrorMapper` / `DataverseObservationMirror`) | `sprk_documentid` (BuildEntity throws on an empty id) | `DataverseObservationMirrorTests` (asserts `sprk_documentid`) |
| Create Analysis wizard (client, `Xrm.WebApi`) | `sprk_documentid@odata.bind` (refuses to finish without a document) | `CreateAnalysisWizardWidget.test.tsx` (asserts the bind) |
| MDA form | — | `sprk_documentid` RequiredLevel = ApplicationRequired (live) |

**Deleting a document clears the lookup — checked, and handled.** That is the source of every anchorless row (§14.1). The
row it leaves is decided by round 15 item 4 (personal; unverifiable → uniform 404), which is what §14.3 implements. Whether
an analysis should instead be DELETED with its document is a separate data-lifecycle question, recorded as finding
F-162-f1-1 in §14.8 with its complete fix; it changes no access answer.

### 14.3 Round 15 items 4–5 — the ONE analysis-read rule, with the PERSONAL branch

`AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync(HttpContext, Guid analysisId, ILogger?)` — the **same name and
signature** task 164 r1 extracted and already calls (`AiAuthorizationFilter`, chat analysis host) — is the only place the
rule is declared:

1. No caller token → uniform 404 (no app-only read is spent).
2. ONE app-only retrieve of `ReadRuleColumns` = every anchor column + `createdby` (all live; pinned by
   `ReadRuleRetrieve_SelectsAnchorsPlusCreatedBy_AllLive`).
3. Any populated anchor → Read on EVERY anchor (unchanged; an unmapped anchor type rejects).
4. **No anchor → PERSONAL** (`BuildCreatorTargetsAsync`): the caller's Dataverse **systemuserid** comes from
   `CallerRecordAccessProbe.GetCallerSystemUserIdAsync` (WhoAmI on the caller's OWN OBO token — it cannot name anyone else;
   never the Entra oid). The caller is the creator when that id equals `createdby`, or — for a row the BFF created as the
   application — the server-stamped `sprk_createdbyperson` (task 146 / owner round 13 item 9), read in its OWN query so a BFF
   deployed before that column exists still serves every anchored analysis and answers "cannot tell" (404) for this branch.
   The verified creator gets ONE check: the TABLE privilege `prvReadsprk_analysis` (live name; Core/Basic User Deep, AI
   Analysis User Basic), evaluated as the caller on the Privilege path — **never a row Read on the analysis** (a
   business-unit-depth row Read would expose one user's analysis to colleagues; `Get_NoAnchor_*` assert no `sprk_analysises`
   record call is ever made).
5. No probe, no systemuserid, a WhoAmI fault, a creator-column fault, a colleague, an app-created row with no person, or a
   creator without the privilege → the identical uniform 404 (`Get_NoAnchor_NotTheVerifiedCreator_IsUniform404`, 7 rows).
   An ANCHORED analysis gives its creator no bypass (`Get_Anchored_CreatorGetsNoBypass`).

Effect on the live 221 rows: Ralph Schroeder reads his 155 through `GET` (and the Copilot `getAnalysis`); the 66
app-created rows have no recorded person (they predate task 146's stamp) and answer 404 for everyone — round 15 item 4's
"anything that cannot be verified fails closed". After 146 is integrated, every NEW app-created analysis records its
requester, so an analysis that later loses its anchor stays readable by that person.

`sprk_createdbyperson` is spelled once: `src/server/shared/Spaarke.Dataverse/RecordCreatorPersonColumn.cs` is added here
**byte-identical** to task 146's file (blob `64acd27a`, as on `integ/uac-r2-batch4`), so the two branches add the same file and
merge without a conflict.

**Shared evaluation for task 164 (round 25 item 4).** The personal branch hands the evaluator a Privilege check. Task 164 r1's
private `AiAuthorizationFilter.IsAnalysisReadableAsync` evaluates only the Document and Record paths and treats any other path
as a deny (fail closed, so nothing is over-granted — but a creator's personal analysis would be refused as a chat host). f1
adds `AnalysisAuthorizationFilter.IsAnalysisReadableAsync(HttpContext, Guid, ILogger?)`, which evaluates the rule with the ONE
evaluator on all three paths (`IsAnalysisReadable_DecidesExactlyAsGet`, 4 rows: chat host and GET agree). **Integration edit
(main session, when 164 merges onto this):** replace the body of 164's private `IsAnalysisReadableAsync(HttpContext
httpContext, AuthorizationService authorizationService, Guid analysisId, string userId, string? token, CancellationToken ct)`
with exactly:

```csharp
        => await AnalysisAuthorizationFilter.IsAnalysisReadableAsync(httpContext, analysisId, _logger);
```

(its now-unused parameters may stay or go; its caller is unchanged). 164's `ResolveAnalysisReadTargetsAsync` extraction and
f1's are the same refactor; on conflict take f1's (a superset: it adds the personal branch).

### 14.4 Round 25 item 2 — `POST /api/ai/analysis/create` adopts G5 (matching promote)

New `AuthorizationMode.AnalysisCreate` (= 4, appended; pinned) + `AddAnalysisCreateAuthorizationFilter()` replace the old
Read-only DocumentAccess filter on `/create`. As the caller, before the app-only create: (1) the existing 400 for a body with
no document, then the handler's own validation (`AnalysisEndpoints.ValidateCreateRequest`, shared with the handler) — every
malformed body gets exactly the 400 it got before, with no rights query (`Create_MalformedBody_*`, 4 rows); (2) the Create
privilege `prvCreatesprk_analysis`; (3) `analysis.attach` (Read + AppendTo) on the document — `AttachDocumentCheck`, the same
check promote uses; (4) the playbook-use decision for a body PlaybookId (promote's); (5) **Read on every skill, knowledge and
tool row** the handler associates app-only — caller-chosen ids `/create` consumes (POML constraint: "scope found during
execution … is added to this task"); the same "may this caller use this configuration row" question as the playbook-use
decision; live: the child-BU test user has ReadAccess on all 31 skills, 33 knowledge and 51 tool rows, and Core/Basic hold
Read on the three tables at Deep, so no shipped flow loses access. `ActionId` is not consumed by the handler, so it is not
checked. Denies are the evaluator's 403 with `detail` "Access denied"; a declaration fault is 403 `system_failure`. Tests:
`Create_*` (7 facts/theories, 15 cases). Entity sets `sprk_analysisskills` / `sprk_analysisknowledges` / `sprk_analysistools`
are per-route constants read from live metadata (no map), pinned by `PersonalAndCreateNames_AreTheLiveNames`.

### 14.5 Verifier items 7 and 9 — the promote session-document check uses the SAME evaluator

The handler's direct `AuthorizationService.AuthorizeAsync` call and its own `catch { allowed = false; }` are gone. The session
document is checked with `AttachDocumentCheck(…, "session.documentId")` through
`AnalysisAuthorizationFilter.EvaluateOutsideFilterAsync` — the unchanged `FinanceAuthorizationFilter` evaluated outside a
filter chain, returning its own deny response. So a session-document deny is byte-identical to a body-document deny in EVERY
case — missing right, missing row, rights-query fault — not only "under normal conditions" (item 9). Tests:
`Promote_SessionDocumentFault_IsTheBodyDocumentFault403` (fault on both, identical bodies, `system_failure`, nothing written)
and `Promote_SessionDocumentCheckException_Denies` (item 7: an exception that ESCAPES `AuthorizationService` — its logger
throws while logging the data-source fault — denies 403 and writes nothing). The POML's handler constraint said a deny or an
exception returns `insufficient_rights`; for a missing right or row that is still exactly what is returned. A FAULT now carries
`sdap.access.error.system_failure` — the code the filter already gives the same fault on a body document — because the
constraint's purpose ("the same 403 body the filter's 403 carries") can only hold in every case if both go through one
renderer. Recorded here for the main session's review.

### 14.6 Round 15 item 3 — the backfill script

`scripts/Repair-AnalysisAnchors.ps1` (the repo's data-script pattern: dry run default, `-Apply` writes, `-Verify` exits 1 on any
derivable anchor left unwritten; `-Classify` adds the audit evidence; read-only metadata decides navigation properties and
entity sets — nothing pluralized). Its anchor list is pinned to `AnalysisAuthorizationFilter.AnchorColumns` by
`AnalysisAnchorRepairScriptAgreementTests` (seed A1). Dry run on dev: 0 derivable (§14.1). **Manual gate** for the main
session (dev; it writes only if a derivable anchor appears before it runs):

```powershell
.\scripts\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Classify   # dry run
.\scripts\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply      # only if the plan is non-empty
.\scripts\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify     # expect exit 0
```

### 14.7 Verifier item 8 — documentation AND infrastructure drift

Closed in this round, not left for doc-drift-audit: `docs/guides/AI-MONITORING-DASHBOARD.md` (export row, export alert row,
`ai.format` dimension, export KQL removed; rows renumbered), `docs/guides/MONITORING-AND-ALERTING-GUIDE.md` (export metric
table, alert 7 row + detail, dashboard row, customer export KQL, triage line removed; dashboard is 4 rows),
`docs/architecture/sdap-component-interactions.md` (the three rows now name only `DocxExportService` / `ChatWordExportEndpoints`).
**Found while checking:** the deleted metrics were still referenced by deployable infrastructure — `infrastructure/bicep/modules/
alerts.bicep` created a metric alert "Export Failures" on `customMetrics/ai.export.requests`, and `modules/dashboard.bicep` a
4-chart export row; `stacks/model2-full.json` (the compiled ARM template, identical to a fresh `az bicep build` before this
change) carried both. An alert on a metric that is no longer emitted never evaluates meaningfully, and Azure validates a
custom-metric alert's metric at create time. Removed: the alert resource and its `exportFailureAlertId` output, the dashboard
row (the cache row moves up from y 14/15 to 10/11), and `model2-full.json` regenerated with `az bicep build` (diff = the
removed resources, three `templateHash` values and the moved y positions). Every bicep entry point that uses the modules
builds (`alerts.bicep`, `customer.bicep`, `platform.bicep`, `model1-shared.bicep`, `model2-full.bicep`, `ai-foundry-stack.bicep`;
only pre-existing BCP036/BCP318 warnings). No live deployment was made; the next stack deployment drops the alert.

### 14.8 Finding F-162-f1-1 (data lifecycle; changes no access answer) — complete fix proposed

Deleting a document leaves its AI analyses (and their `sprk_analysisoutput` rows, also RemoveLink) behind with the deleted
document's content in `sprk_workingdocument` / outputs. Access is already correct (round 15: personal, or 404). The complete
fix for the lifecycle, for the owner's product decision because it deletes data on every future document delete: set
`sprk_document_analysis_document` (`sprk_documentid`) and `sprk_analysis_analysisoutput` to **Delete = Cascade** (working
versions, chat messages and email metadata already cascade), probe with a non-admin test user that deleting a TEST document
with a team-owned analysis still succeeds (a cascade that the deleting user's rights block would make documents undeletable),
then delete the 97 analyses whose deleted parent is proven by audit, as a dry-run/`-Apply`/`-Verify` script. Not built in
this round: it is outside the decided round-15 scope and is a data-deletion decision.

### 14.9 Seeded proofs (f1)

Each seed was applied by a runner (scratchpad `seeds_f1.py`) to the named file (exactly one occurrence), the BFF + test
assembly rebuilt, the task's tests run (contract, filter unit, mode pin, writer anchors, script agreement — 119 cases), and the
file restored byte-identically (SHA-256 asserted) and touched; `git status` afterwards showed only this round's intended edits.
**All 23 seeds turned at least one named test red.**

| Seed | Change applied | Red cases | Tests turned red |
|---|---|---|---|
| P1 | personal branch admits any caller (creator check skipped) | 5 | `Get_NoAnchor_NotTheVerifiedCreator_IsUniform404`; `IsAnalysisReadable_DecidesExactlyAsGet` |
| P2 | creator matched by the caller's Entra **oid** instead of the WhoAmI systemuserid | 4 | `Get_NoAnchor_NotTheVerifiedCreator_IsUniform404` (the oid row); `Get_NoAnchor_CreatorByCreatedBy_Is200`; `Get_NoAnchor_CreatorByStampedPerson_Is200`; `IsAnalysisReadable_DecidesExactlyAsGet` |
| P3 | the creator handed a ROW Read on the analysis instead of the table privilege | 4 | `Get_NoAnchor_CreatorByCreatedBy_Is200`; `Get_NoAnchor_CreatorByStampedPerson_Is200`; `Get_NoAnchor_NotTheVerifiedCreator_IsUniform404`; `IsAnalysisReadable_DecidesExactlyAsGet` |
| P4 | an unreadable `sprk_createdbyperson` (column not yet deployed) treated as the creator | 1 | `Get_NoAnchor_NotTheVerifiedCreator_IsUniform404` (person-column-not-yet-deployed) |
| P5 | the server-stamped person branch removed (createdby only) | 1 | `Get_NoAnchor_CreatorByStampedPerson_Is200` |
| P6 | an unresolvable caller systemuserid admitted | 1 | `Get_NoAnchor_NotTheVerifiedCreator_IsUniform404` (caller-systemuserid-unresolvable) |
| P7 | anchors ignored: every analysis decided by the personal branch | 4 | `Get_ReadOnEveryAnchor_Returns200AndAsksEachAnchorOnItsPath`; `Get_ReadableDocumentUnreadableMatter_IsUniform404`; `Get_Anchored_CreatorGetsNoBypass`; `IsAnalysisReadable_DecidesExactlyAsGet` |
| C1 | `/create`: the Create-privilege check removed | 2 | `Create_WithoutCreatePrivilege_Is403`; `Create_WithTheG5Rights_Is201AndAnchorsTheDocument` |
| C2 | `/create`: `analysis.attach` weakened to Read on the document | 1 | `Create_DocumentWithoutAttachRights_Is403` (read-only row) |
| C3 | `/create`: the playbook-use check removed | 3 | `Create_UnusablePlaybook_Is403` (2 rows); `Create_CheckDeclarationFault_Denies` |
| C4 | `/create`: the scope-row Read checks removed | 4 | `Create_UnreadableScopeRow_Is403` (3 rows); `Create_WithTheG5Rights_Is201AndAnchorsTheDocument` |
| C5 | `/create` back on the old Read-only document filter | 13 | completeness + every `Create_*` test |
| C6 | `/create`: body validation no longer runs before the rights queries | 1 | `Create_MalformedBody_IsTheExisting400WithNoRightsQuery` (empty-name) |
| C7 | `/create`: a check-declaration fault calls next | 1 | `Create_CheckDeclarationFault_Denies` |
| E1 | the shared evaluation fails open on any non-allow outcome | 5 | `Promote_SessionDocumentFault_IsTheBodyDocumentFault403`; `Promote_SessionDocumentCheckException_Denies`; `Promote_SessionDocumentWithoutAttachRights_Is403WithTheBodyDocumentDenyBody`; `IsAnalysisReadable_DecidesExactlyAsGet` |
| E2 | the promote handler ignores the session-document deny (item 7's fail-open, in its new form) | 3 | `Promote_SessionDocumentFault_IsTheBodyDocumentFault403`; `Promote_SessionDocumentCheckException_Denies`; `Promote_SessionDocumentWithoutAttachRights_Is403WithTheBodyDocumentDenyBody` |
| E3 | the shared evaluation allows when the evaluator cannot be resolved | 1 | `IsAnalysisReadable_EvaluatorUnavailable_IsFalse` |
| W1 | document-profile writer drops the document anchor | 1 | `AnalysisWriterAnchorTests.DocumentProfile_AnchorsTheAnalysisToItsDocument` |
| W2 | email-analysis writer drops the document anchor | 1 | `AnalysisWriterAnchorTests.EmailAnalysis_AnchorsTheAnalysisToTheEmailDocument` |
| W3 | profile-output storage drops the document anchor | 1 | `AnalysisWriterAnchorTests.ProfileOutputStorage_CreateBranch_AnchorsTheAnalysisToItsDocument` |
| W4 | `/create` handler drops the document anchor | 1 | `Create_WithTheG5Rights_Is201AndAnchorsTheDocument` |
| W5 | promote drops the regarding anchor | 1 | `Promote_OwnSessionWithRegardingMatter_Is201` |
| A1 | the backfill script's anchor list drops `sprk_regardingmatter` | 1 | `AnalysisAnchorRepairScriptAgreementTests.TheScript_UsesExactlyTheReadRulesAnchorColumns` |

Seeds P4, P6, C1, C3, C4, C7, E3, W1–W4 were first skipped by the runner because the files are CRLF and the multi-line
search text was LF ("found 0 times" — the runner refuses rather than guess); the runner then normalized line endings and the
whole set was re-run from scratch. The results above are that second, complete run.

### 14.10 Tests (f1)

All on `task/uac-r2-162-f1`, 2026-10-04, after the final code (the seeded runs are in §14.9).

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api -warnaserror` / `dotnet build tests/unit/Sprk.Bff.Api.Tests -warnaserror` | Build succeeded, 0 warnings |
| Task tests (contract, filter unit, mode pin, writer anchors, script agreement) | 119/119 pass (37 new cases this round) |
| Affected (`~Analysis` / `~ReviewMemo` / `~Promote` / `~PlaybookAuthorizationFilter` / `~OperationAccessPolicy` / `~ObservationMirror` / `~ExecutorSideEffect` / `~FinanceEndpointsAuthorization` / mode pin) | 693 pass, 7 skipped (pre-existing), 0 fail |
| **Full BFF unit suite** `dotnet test tests/unit/Sprk.Bff.Api.Tests` | **14 274 pass, 54 skipped, 0 failed** (27 m 32 s; four other agents' testhosts were running, no timeouts this time) |
| **NetArchTest** `dotnet test tests/Spaarke.ArchTests` | 346/346 pass |
| **Integration** `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | 104/104 pass |
| **Integration** `tests/integration/Spe.Integration.Tests` (full) | 403 pass, 25 skipped (pre-existing), 0 fail |
| `dotnet list package --vulnerable --include-transitive` (BFF) | "no vulnerable packages" (f1 adds no package) |
| Live dry run `scripts/Repair-AnalysisAnchors.ps1 -Classify` (read-only) | exit 0; 221 anchorless, 0 derivable (§14.1) |
| Bicep (`az bicep build`, every entry point using the edited modules) | all build; only pre-existing BCP036/BCP318 warnings |
| Publish size (CLAUDE.md §10; fresh `git archive` trees on short paths; `dotnet publish -c Release`; PowerShell `Compress-Archive` Optimal over `deploy/api-publish/*`; PDBs included, 4 PDBs on every side) | **Base** `91a1c1c83` (`C:\w162b`): **45.65 MB** (47 870 200 B), 212 files. **r1** `d1c5e6f34` (`C:\w162r`): **35.11 MB** (36 814 180 B), 190 files. **f1** `f86c3902b` (`C:\w162f`): **35.11 MB** (36 819 676 B), 190 files. **f1 vs r1: +5 496 B (+0.005 MB), identical file lists** (this round's own contribution). **Task 162 vs base: −10.54 MB**; the 22-file difference is exactly the deleted QuestPDF package (as the round-1 verifier found). Under the 60 MB ceiling. Re-measured by this round, not carried over (verifier item 14). |

### 14.11 Manual live gate — additions for f1 (dev; main session; read-only verification)

- [ ] (i) `GET /api/ai/analysis/{id}` for one of Ralph Schroeder's 155 anchorless analyses: as Ralph → 200; as
      `uac.child.user` → the uniform 404 (identical to a random GUID).
- [ ] (j) The same for one of the 66 app-created anchorless analyses → 404 for both users.
- [ ] (k) `POST /api/ai/analysis/create` as `uac.child.user` on a document it can Read and AppendTo, with the Playbook Library's
      usual scopes → 201; on a document it cannot read → 403 `insufficient_rights`; no `sprk_analysis` with that name exists
      afterwards.
- [ ] (l) Playbook Library / Analysis Builder "create analysis" still completes for a Core User.
- [ ] (m) `scripts/Repair-AnalysisAnchors.ps1` dry run → 0 derivable; `-Verify` → exit 0.

### 14.12 Placement, justification, conflicts

- **Placement (CLAUDE.md §10):** in the BFF — existing routes and the existing filter; no new service, endpoint, DI registration,
  option, job, column or package (`.claude/constraints/bff-extensions.md` §A).
- **New surface (CLAUDE.md §11), three questions each:** `AuthorizationMode.AnalysisCreate` + `AddAnalysisCreateAuthorizationFilter`
  (existing: the filter's modes; extension: one more mode on it, declaring checks only; cost: `/create` stays the weaker sibling
  round 25 rejected); `ReadAnalysisPrivilege`, three scope entity-set constants, `ReadRuleColumns`, `NoDocumentIdentifierDetail`
  (existing: `CreateAnalysisPrivilege` precedent; extension: constants in the same filter; cost: the personal branch and the
  scope checks have nothing to evaluate); `ResolveAnalysisReadTargetsAsync` / `IsAnalysisReadableAsync` / `EvaluateOutsideFilterAsync`
  / `AttachDocumentCheck` (existing: 164 r1 extracted the first, the evaluator exists; extension: static members of the existing
  filter over the unchanged evaluator — no second evaluation loop; cost: 164 and the promote handler would each re-implement
  evaluation and drift, as items 7/9 showed); `AnalysisEndpoints.ValidateCreateRequest` (existing: `ValidatePromoteRequest`;
  extension: the same pattern; cost: filter and handler 400s drift); `RecordCreatorPersonColumn` (existing: task 146's file,
  byte-identical); `scripts/Repair-AnalysisAnchors.ps1` (existing: `Backfill-CoreAncestorStamps.ps1` pattern; cost: round 15
  item 3 has no backfill). Removed: the export alert/dashboard row (infra) and the handler's direct `AuthorizeAsync` call.
- **ADRs:** ADR-002 (no plugins; one server-side owner per invariant — the seam's FR-D9 guard), ADR-003 (every new branch fails
  closed; seeds prove it), ADR-008 (checks in the route's filter; the session-derived check in the handler per #863, now
  through the same evaluator), ADR-010 (no new interface/registration), ADR-019 (reasonCode on every deny), ADR-038 (no
  `Mock<HttpMessageHandler>`, no DI-registration or ctor tests; the evaluator-unavailable and throwing-logger tests assert
  HTTP behaviour at module boundaries). `FinanceAuthorizationFilter.cs`, `PlaybookAuthorizationFilter.cs`,
  `EntityAccessFilter`, `SemanticSearchAuthorizationFilter` are unchanged in this round.
- **Integration (main session):** 162 before 164 (round 16 item 7) — §14.3's one-line edit to 164. Task 146 (`integ/uac-r2-batch4`)
  edits the same `/create` and `/promote` handlers: keep 146's owner resolution and `CreateAnalysisAsync(…, owningTeamId,
  createdByPersonId)` arguments AND f1's `ValidateCreateRequest` call; in `PromoteSession`'s parameter list drop 162-r1's
  `AuthorizationService authorizationService` (f1 removed it) and keep 146's `IRecordOwnershipResolver ownership`. Task 168 /
  round 19 item 4 adds `sprk_regardingrecordurl` (a String, not a Lookup) to `sprk_analysis` — no change to `LookupColumns`; a
  new Lookup would redden `EveryLiveAnalysisLookup_IsClassifiedExactlyOnce` until classified.
- **Observation (not authorization; for the main session):** the published Copilot `createAnalysis` schema
  (`spaarke-bff-openapi.yaml` `CreateAnalysisRequest`) has `documentId`, `playbookId`, `matterId` but no `name`, which the
  handler requires (400 "Analysis name is required." before and after this task).

### 14.13 Verifier round 2 — item by item

| # | Item | Disposition |
|---|---|---|
| 1 | Round 15 complete fix | **Done**: §14.1 classification (per writer, per class), §14.2 writers + tests, §14.6 script (dry run 0 derivable), §14.3 personal rule in the ONE declaration 164 calls. |
| 2 | Round 25 item 2: `/create` adopts G5 | **Done**: §14.4; tests + seeds C1–C7, W4. |
| 3 | Sweep findings closed, real-host negative tests | Confirmed; still green (§14.10). |
| 4 | Round-10 deletions independently checked | Confirmed — and the deleted metrics' infra references found and removed (§14.7). |
| 5 | Suites green, contention timeouts | Re-run in full this round (§14.10). |
| 6 | 13 of 14 seeds red | Confirmed. V1's target code no longer exists (item 7). |
| 7 | V1 survived: promote's fail-open catch untested | **Closed**: the catch is gone; the check goes through the shared evaluator; a test where an exception escapes `AuthorizationService` denies 403 and writes nothing; seeds E1/E2 redden it. |
| 8 | Doc drift | **Closed** in the three docs AND the two bicep modules + compiled ARM template (§14.7). |
| 9 | Promote reasonCode divergence | **Closed**: one evaluator, identical body in every case incl. faults (§14.5). |
| 10 | Behaviour checked and correct | Confirmed; `AnalysisCreate` added to the fail-closed switch. |
| 11 | No ADR-038 bans | Confirmed for the new tests (§14.12). |
| 12 | Client consumers not broken | Confirmed; `/create`'s consumer (Playbook Library / Analysis Builder `analysisService.ts`) needs rights ordinary users hold (§14.4); live gate (k)/(l). |
| 13 | Manual live gate pending | Still pending (main session): §11 (a)–(h) + §14.11 (i)–(m); item (c) is now decided. |
| 14 | Publish size and CVE not re-verified | **Re-measured** this round (§14.10). |

### 14.14 Integration (sweep lane, 2026-10-04) — round 34 items 1 and 2

Merged into `integ/uac-r2-batch4` as `8fd16bbea` (162-f1), after 159/160/161. Decisions round 34 (BINDING) closed two of
this note's open items on the integration branch:

**Round 34 item 1 — deleting a document deletes its AI analyses and their outputs (F-162-f1-1, §14.8).** New script
`scripts/Set-DocumentAnalysisCascadeSchema.ps1` (the `Set-*Schema.ps1` pattern: dry run by default, `-Apply`, `-Verify`;
solution membership through `scripts/common/DataverseSolutionMembership.ps1` / `Test-DvInSolution`, so
`SchemaScriptSolutionMembershipGuardTests` covers it — seeded: an own `solutioncomponents` read in its place reddens the
guard, restored). It sets `Delete = Cascade` on `sprk_document_analysis_document` (`sprk_analysis.sprk_documentid ->
sprk_document`) and `sprk_analysis_analysisoutput` (`sprk_analysisoutput.sprk_analysisid -> sprk_analysis`), changes no
other cascade value, and `-Verify` fails if Assign/Share/Unshare/Reparent/Merge is anything but NoCascade. Analyses with no
document (round 15's personal analyses) and analyses anchored only to a record have nothing to cascade from: untouched.

Read-only runs on spaarkedev1 (2026-10-04, operator az identity): both relationships unmanaged + customizable, currently
`Delete = RemoveLink`, `Archive = RemoveLink`, all else NoCascade; both already in SpaarkeCore through their referencing
table (`rootcomponentbehavior 0`). Dry run: two `WOULD set … RemoveLink -> Cascade`, zero writes. `-Verify`: **FAIL, exit 1**
(the two cascades) — the expected answer before the gate.

- [ ] **MANUAL GATE (main session; NOT run by the integration lane):** `-Apply`, then `-Verify` → exit 0. If Dataverse
      refuses the PUT because `Archive` must follow `Delete`, align `Archive` to `Cascade` in the script (one line) and
      re-run — record which.
- [ ] **Non-admin delete probe (after `-Verify` passes; dev; `uac.child.user@demo.spaarke.com`, round 11):**
      (1) create a TEST `sprk_document` the user may delete (Core User), an analysis on it as task 146 creates it
      (team-owned, `sprk_createdbyperson` = the user) and one `sprk_analysisoutput` under that analysis; also note one
      unrelated anchorless analysis id; (2) as the user, delete the document (MDA or Web API with the user's token);
      (3) expect: the delete SUCCEEDS (a cascade the user's rights blocked would make documents undeletable — the reason
      for the probe); the analysis and its output are gone (`GET` → 404); the unrelated anchorless analysis is unchanged.
      Record ids redacted to 8 characters and the result here.

**Round 34 item 2 — ratified §14.5.** Confirmed on the merged code: a rights-query FAULT on promote's session-document
check answers 403 `sdap.access.error.system_failure`, byte-identical to the same fault on a body document
(`Promote_SessionDocumentFault_IsTheBodyDocumentFault403`, `Promote_SessionDocumentCheckException_Denies`); a missing right
answers `insufficient_rights` (`Promote_SessionDocumentWithoutAttachRights_Is403WithTheBodyDocumentDenyBody`); and a
missing ROW — now pinned by the new `Promote_SessionDocumentMissingRow_IsInsufficientRights_NotSystemFailure` — answers
`insufficient_rights`, never `system_failure`. Seeded both ways in `PromoteSession` (every session-document deny →
`system_failure` reddens the missing-right and missing-row tests; every deny → `insufficient_rights` reddens both fault
tests), restored byte-identical.
