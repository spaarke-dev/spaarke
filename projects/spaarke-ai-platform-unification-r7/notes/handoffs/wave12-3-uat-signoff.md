# Wave 12.3 — Wizard End-to-End UAT Signoff Checklist

> **Task**: T145 — UAT all 5 wizards end-to-end in spaarkedev1 (Wave 12.3 gate)
> **Authored**: 2026-06-30 by task-execute STANDARD rigor (code-path + Dataverse-state verification)
> **Status**: **READY-FOR-OPERATOR-UAT** — all 5 prerequisite fixes (T140-T144 + T143-main-session + T124-FIX-A) confirmed landed; Dataverse state verified via MCP; end-to-end browser UAT pending operator.
> **Acceptance Criteria covered**: AC8 / AC9 / AC10 / AC11 / AC12 (from `notes/wave12-mvp-completion-plan.md` §3)

---

## TL;DR (the executive view)

| Wizard | Fix landed | Dataverse state verified | Code-path verified | Browser UAT status | AC# |
|---|---|---|---|---|---|
| 1. Wizard File Summary (`summarize-file`) | ✅ T140 (`b5e3aa82f`) | ✅ playbook 2-node clean | ✅ env-var fallback path | 🔲 OPERATOR | AC8 |
| 2. Document Create Profile (`document-profile`) | ✅ T141 (`89977022b`) | ✅ Save Profile DELETED; 3-node playbook | ✅ UpdateRecord node 3 carries field-mapping | 🔲 OPERATOR | AC9 |
| 3. Create Matter (`matter-pre-fill`) | ✅ T143 main-session (`3cb239e5d`) | ✅ EntityNameValidator DELETED; AI Analysis systemPrompt stripped | ✅ ACT-023 7-field contract preserved | 🔲 OPERATOR | AC10/AC12 |
| 4. Create Project (`project-pre-fill`) | ✅ T142 (`79af4befd`) | ✅ FK re-linked to ACT-024 | ✅ playbook 1-node with valid Tool+Schema | 🔲 OPERATOR | AC10/AC12 |
| 5. Create Work Assignment (no own playbook) | ✅ T144 (`558a6e594`) | ✅ N/A — reuses Matter endpoint | ✅ EnterInfoStep.tsx:175 hardcoded | 🔲 OPERATOR | AC10/AC12 |

**AC11 (Action output schema editing preserved)**: ✅ verified via code-path (see §3 below) — operator confirms at UAT by editing an ACT-XXX `sprk_outputschemajson` / `sprk_systemprompt` and re-running a wizard.

**Sandboxed agent disclosure**: the agent that compiled this signoff has NO browser and NO OBO bearer token for spaarkedev1. End-to-end "click in browser, see correct fields populate" is therefore deferred to the operator UAT session. All deterministic data-layer + code-path checks that DO NOT require browser have been completed and pass.

---

## 1. Per-wizard fix landing (commit ledger)

| # | Wizard | Fix | Commit SHA | Author | Date |
|---|---|---|---|---|---|
| 1 | Summarize File | App Service env-var `Workspace__SummarizePlaybookId` set on `spaarke-bff-dev` | `b5e3aa82f` | T140 agent | 2026-06-30 |
| 2 | Document Create Profile | Dataverse DELETE node `c9334fb7-a415-...` (Save Profile drift); Update Record node 3 PATCH fieldMappings | `89977022b` | T141 agent | 2026-06-30 |
| 3 | Create Project | Dataverse PATCH `sprk_actionid` FK on `dacac491-4f6c-...` → ACT-024 | `79af4befd` | T142 agent | 2026-06-30 |
| 4 | Create Matter — diagnostic | Audit 123 §10 written; deferred apply (sandboxed) | `df0dd01ae` | T143 agent | 2026-06-30 |
| 4 | Create Matter — applied | DELETE `c3c5226d-5b71-...` (EntityNameValidator); strip systemPrompt override from `444b06d3-d418-...` | `3cb239e5d` | main session (MCP-applied) | 2026-06-30 |
| 4 | Create Matter — closure | T143 POML status → completed | `5316cbe36` | main session | 2026-06-30 |
| 5 | Create Work Assignment | Inheritance verified — no code change needed | `558a6e594` | T144 agent | 2026-06-30 |
| ∅ | Wave-5 backfill-health follow-up | Document Summary node executortype 0 → 40 (DeliverOutput); fixed via T124 sweep that surfaced from T143 systemPrompt incident | `20bad1793` | T124 agent | 2026-06-30 |

All commits are on `work/spaarke-ai-platform-unification-r7` and pushed to `origin`.

---

## 2. Dataverse state verification (via `mcp__dataverse__read_query`, 2026-06-30)

### 2.1 Wizard 1 — Summarize File playbook (`4a72f99c-a119-f111-8343-7ced8d1dc988`)

```
| Node                | executortype | actionid                              |
| Start (order 1)     | 33 (Start)   | null (structural)                     |
| AI Analysis (order 2)| 0 (AiAnalysis)| ddaa441e-9f19-... (ACT-025 File Summary)|
```

Matches audit 121 §2 expected good state. Tool linkage to `General Analysis` (GenericAnalysisHandler) verified pre-fix in audit. **Status: ✅ data is clean. Only env-var was missing; that's now fixed (commit b5e3aa82f).**

### 2.2 Wizard 2 — Document Profile playbook (`18cf3cc8-02ec-f011-8406-7c1e520aa4df`)

```
| Node                  | executortype       | actionid                              |
| Profile Document (1)  | 0 (AiAnalysis)     | bb356968-ebe9-... (ACT-011 Document Profiler)|
| Update Record (3)     | 22 (UpdateRecord)  | null (structural)                     |
| Index Document (4)    | 41 (DeliverToIndex)| null (structural)                     |
```

**Note**: `Save Profile` node (was `c9334fb7-...`, executionorder 2) is **ABSENT** (DELETED per T141). Execution order skips 2 → 3, which is per audit 122 §3.1 Option A spec. Update Record node 3 now carries the actual field-mapping config. **Status: ✅ broken node removed; canonical PATCH path preserved.**

### 2.3 Wizard 3 — Create Project playbook (`fc343e9c-3460-f111-ab0b-7c1e521b425f`)

```
| Node                       | executortype   | actionid                              |
| Extract Project Fields (1) | 0 (AiAnalysis) | 1e838114-7919-... (ACT-024 New Project Field Extraction)|
```

Pre-fix: `sprk_actionid` was NULL (audit 123 §5.2). **Post-fix: FK re-linked.** AiAnalysisNodeExecutor.Validate will now find Tool + Schema. **Status: ✅ data fix applied; awaiting browser UAT confirmation that LLM extraction populates the wizard form.**

### 2.4 Wizard 4 — Create Matter playbook (`2d660cad-d418-f111-8343-7ced8d1dc988`)

```
| Node              | executortype   | actionid                              |
| Start (order 1)   | 33 (Start)     | null (structural)                     |
| AI Analysis (2)   | 0 (AiAnalysis) | 89cc641a-df18-... (ACT-023 New Matter Field Extraction)|
```

**Note**: `Entity Name Validator` node (was `c3c5226d-5b71-...`, executionorder 3) is **ABSENT** (DELETED per T143 main-session fix). Matches audit 123 §10.4 spec.

AI Analysis node `configJson` post-fix:
```json
{
  "__canvasNodeId": "node_1772743778436_khspxdutg",
  "__actionType": 0,
  "modelDeploymentId": "cdfa4e52-7c16-f111-8343-7c1e520aa4df"
}
```

**Critical**: `systemPrompt` override key is **ABSENT** — clobbering bug from audit 123 §10.3 is fixed. ACT-023 7-field JPS output schema is the sole instruction source. **Status: ✅ both 2026-06-30 deterministic bugs fixed.**

### 2.5 Wizard 5 — Create Work Assignment

No own playbook; reuses `/api/workspace/matters/pre-fill` (hardcoded at `EnterInfoStep.tsx:175`). Inherits Wizard 4 fix by construction. **Status: ✅ N/A — verified via T144 code-path trace.**

---

## 3. Code-path verification (no browser required)

### 3.1 AC11 — Action output schema editing preserved (the "operator-tunable surface" contract)

Per audit 122 §0 and audit 123 §2: the `sprk_outputschemajson` (or JPS `output.fields[]` embedded in `sprk_systemprompt`) of each Action is the **operator-tunable contract** that:

- The LLM emits (enforced via OpenAI constrained decoding when `output.structuredOutput: true`)
- The orchestrator passes through unchanged to `NodeOutput.StructuredData`
- The downstream consumer (UpdateRecord node OR wizard UI JSX) reads via fixed field names

**Code path verified intact**:
- `AiAnalysisNodeExecutor.cs:1316-1342` — `StructuredData = toolResult.Data` (JsonElement passthrough)
- `UpdateRecordNodeExecutor.cs:337-374` — template substitution `{{output_aiAnalysis.output.<fieldName>}}` resolves against StructuredData
- `MatterPreFillService.cs:710` + `ProjectPreFillService.cs:433` — JSON parse from StructuredData with explicit field-name mapping

**What "editing preserved" means in practice (operator confirms at UAT)**:
- Maker edits `sprk_outputschemajson` of ACT-023 (Matter), ACT-024 (Project), ACT-011 (Document Profile), or ACT-025 (File Summary) in Power Apps maker portal
- Adds/removes a field, or changes a field type/constraint
- Re-runs the wizard
- For Wizards 1/2 (Summarize File, Document Profile): new field appears in returned `StructuredData` and is consumed by downstream UpdateRecord / SSE consumer
- For Wizards 3/4/5 (Prefill Matter/Project/WA): new field appears in `PreFillResponse`; wizard UI needs hardcoded field binding to render it (per audit 123 §0 — this is a known limitation, not a regression)

**Caveat**: per audit 123 §0, the three Prefill wizards' UI layer has hardcoded field bindings (`fieldExtractor` + `lookupResolvers` + JSX). Editing the Action schema to ADD a field DOES flow through the BFF response shape (because of `additionalProperties: true` on the JSON schema), but the wizard form will not render the new field until the React component is updated. **This is per-design**, documented in audit 123 §0, and **NOT a wizard regression** — AC11 is satisfied for the round-trip extraction; UI-rendering of arbitrary new fields is out of MVP scope.

### 3.2 BFF App Service env-var (Wizard 1)

- Pre-fix: `Workspace__SummarizePlaybookId` setting absent from `spaarke-bff-dev`
- T140 fix: `az webapp config appsettings set --settings "Workspace__SummarizePlaybookId=4a72f99c-a119-f111-8343-7ced8d1dc988"`
- Post-fix: App Service auto-restarted; healthz returned 200 in 0.44s; endpoint smoke (multipart + dummy bearer) returns 401 → route mapped + auth intact
- Defense-in-depth fallback at `WorkspaceFileEndpoints.cs:307` now exercises correctly

### 3.3 Sub-agent boundaries (CLAUDE.md §3)

All Wave 12.3 fixes used MCP `mcp__dataverse__*` tools from the main session (not from sub-agents) for write operations. Sub-agents are confined to read-only (audit) and code-only (BFF C#) tasks. **Compliant with sub-agent write boundary.**

---

## 4. Operator UAT checklist — suggested test scenarios per wizard

The agent cannot run browser UAT. Operator runs each in spaarkedev1; sign off in §5.

### 4.1 Wizard 1 — Summarize File (AC8)

**URL**: SpaarkeAi widget → Summarize Files wizard, OR `https://spaarkedev1.crm.dynamics.com/` → SummarizeFilesWizard code page

**Pre-conditions**: Have 1-3 representative documents (e.g., a contract PDF, a memo .docx, a generic .txt) in a SharePoint Embedded container accessible from the workspace.

**Scenario**: Upload 1 file → wait for SSE stream → verify response shape

**Pass criteria (AC8)**:
- [ ] SSE stream emits `document_loaded` → `extracting_text` → `context_ready` → `analyzing` → `result` chunks → `done`
- [ ] Final `result` chunk contains structured fields matching `ISummarizeResult` contract:
  - [ ] `tldr` (1-2 sentence summary, present)
  - [ ] `summary` (longer paragraph, present)
  - [ ] `shortSummary` (~3-5 sentence, present)
  - [ ] `fileHighlights` (array, present)
  - [ ] `practiceAreas` (array, present)
  - [ ] `mentionedParties` (array, present)
  - [ ] `callToAction` (string, present)
  - [ ] `confidence` (0..1, present)
- [ ] No HTTP 500 / 502 from BFF
- [ ] No JSON parse errors in browser console
- [ ] Wizard UI renders all fields without "(empty)" placeholders for fields that should be populated

**Negative scenario**: Upload a 0-byte file → expect graceful error message, not 500

### 4.2 Wizard 2 — Document Create Profile (AC9)

**URL**: SpaarkeAi widget → Upload Document wizard, OR DocumentUploadWizard code page

**Pre-conditions**: Be in a matter or project context where you have write permission to `sprk_document`.

**Scenario**: Upload 1 file → progress through Step 1 (file selection) → Step 2 (profile generation) → verify inline profile-card populates → save

**Pass criteria (AC9)**:
- [ ] Profile card in Step 2 populates with: documentType (from enum), tldr, summary, keywords (comma list), entities (people + orgs)
- [ ] No console error about `UpdateRecordNodeExecutor.Validate` failing (this was the pre-fix symptom)
- [ ] On save: `sprk_document` row created with `sprk_filesummary`, `sprk_filetldr`, `sprk_extractorganization`, `sprk_extractpeople`, `sprk_filetype`, `sprk_filekeywords`, `sprk_documenttype` populated
- [ ] No 500 from POST `/api/ai/analysis/execute`
- [ ] Index Document step succeeds (Deliver To Index node 4 emits success)

### 4.3 Wizard 3 — Create Matter (AC10/AC12)

**URL**: CreateMatterWizard code page (typically launched from a "New Matter" button)

**Scenario**: Drop 1-2 files (a representation letter, an engagement agreement) → click "Pre-fill from files" → wait for AI extraction → verify form fields populate → review/edit → submit

**Pass criteria (AC10/AC12)**:
- [ ] Pre-fill spinner appears
- [ ] `POST /api/workspace/matters/pre-fill` returns 200 (not 500, not empty)
- [ ] Form fields populate from LLM extraction:
  - [ ] Matter Type (lookup) — should resolve via `searchMatterTypes`
  - [ ] Practice Area (lookup) — should resolve via `searchPracticeAreas`
  - [ ] Matter Name (text)
  - [ ] Summary (text — from `matterDescription`)
- [ ] No console error about Validator node failure (was T143 pre-fix symptom)
- [ ] Confidence shown if present
- [ ] Saving creates `sprk_matter` row with expected values

### 4.4 Wizard 4 — Create Project (AC10/AC12)

**URL**: CreateProjectWizard code page

**Scenario**: Drop 1-2 files (a project charter, a SOW) → click "Pre-fill from files" → wait → verify form fields populate

**Pass criteria (AC10/AC12)**:
- [ ] `POST /api/workspace/projects/pre-fill` returns 200
- [ ] Form fields populate:
  - [ ] Project Name
  - [ ] Description (server coalesces `projectDescription` + `description`)
  - [ ] Project Type (lookup) — resolves via `sprk_projecttype_ref`
- [ ] No "Action not found" / "empty extraction" symptom (T142 pre-fix symptom)
- [ ] Saving creates `sprk_project` row

### 4.5 Wizard 5 — Create Work Assignment (AC10/AC12)

**URL**: CreateWorkAssignmentWizard code page

**Two scenarios** (per audit 123 §1.3):

**5a. AI prefill from files** (reuses Matter endpoint):
- Drop 1-2 files → "Pre-fill from files"
- Pass: `POST /api/workspace/matters/pre-fill` returns 200; form Name + Description + Matter Type + Practice Area populate (other fields silently discarded per `EnterInfoStep.tsx:179-192`)

**5b. Record-copy prefill from selected Matter/Project/Invoice/Event** (independent of AI):
- Select a parent record → fields auto-copy
- Pass: copy succeeds; no AI call made

**Pass criteria (AC10/AC12)**: Both scenarios complete; new `sprk_workassignment` row created.

### 4.6 AC11 — Action output schema editing (operator-confirmable)

**Scenario**: Open ACT-024 (`1e838114-7919-...`, "New Project Field Extraction") in Power Apps maker portal → edit `sprk_outputschemajson` to change a field's `description` text → save → re-run Create Project wizard with same files as in §4.4

**Pass criteria (AC11)**:
- [ ] LLM extraction reflects the changed instruction (visible via different field values, OR via no regression in extraction quality)
- [ ] No HTTP 500 from changed schema
- [ ] No code redeploy needed for the schema change to take effect

If operator wants to verify ADDING a new field, see audit 123 §0 caveat (UI rendering requires React update; the round-trip extraction WILL work but UI won't show the new field).

---

## 5. AC8-AC12 status assessment (this run)

| AC | Description | Status (code/data verified) | Operator confirms at UAT |
|---|---|---|---|
| AC8 | Wizard file summary returns structured summary matching Action output schema | ✅ PENDING-OPERATOR (env-var fixed; SSE path intact; ISummarizeResult contract intact in code) | 🔲 |
| AC9 | Document create profile returns structured profile fields matching Action output schema | ✅ PENDING-OPERATOR (Save Profile drift removed; Update Record node 3 carries 7-field mapping; ACT-011 JPS preserved) | 🔲 |
| AC10 | Create Matter / Project / Work Assignment wizards prefill form fields from LLM output | ✅ PENDING-OPERATOR (FK re-linked for Project; EntityNameValidator + systemPrompt fixed for Matter; WA inherits) | 🔲 |
| AC11 | Action output schema editing in maker portal continues to affect wizard behavior (preserved tunable surface) | ✅ PENDING-OPERATOR (code-path verified — AiAnalysisNodeExecutor passes StructuredData through; consumer reads via template substitution) | 🔲 |
| AC12 | All 5 wizards operator-verified end-to-end in spaarkedev1 | 🔲 PENDING-OPERATOR (5 of 5 prerequisite fixes landed; awaiting UAT session) | 🔲 |

**Recommendation**: operator runs §4.1 through §4.6 in spaarkedev1 in one session (~45 min). Marks PASS/FAIL inline. If any FAIL, file as ISS-NNN and gate Wave 12.5 wrap-up on the resolution. If all PASS, sign §6.

---

## 6. Operator signoff

| AC | Pass? | Tested by | Date | Defect # (if any) |
|---|---|---|---|---|
| AC8 — Summarize File | 🔲 | _____ | _____ | _____ |
| AC9 — Document Profile | 🔲 | _____ | _____ | _____ |
| AC10 — Prefill (3 wizards) | 🔲 | _____ | _____ | _____ |
| AC11 — Schema editing preserved | 🔲 | _____ | _____ | _____ |
| AC12 — All 5 end-to-end | 🔲 | _____ | _____ | _____ |

**Overall**: 🔲 PASS / 🔲 FAIL — Operator: ___________________ Date: ___________________

---

## 7. Defect tracking (if any surface)

If any AC fails during operator UAT, file each defect via `/project-defer-issue-tracking` as ISS-NNN. Reference:
- Audit doc(s) for that wizard (`notes/audits/wave12-12{1,2,3}-*.md`)
- This signoff section that documented the expected behavior
- The commit SHA where the fix was applied

Critical defects (P0/P1) gate Wave 12.5 wrap-up. Cosmetic defects (P2/P3) can be deferred to a follow-up Wave.

**This run**: 0 defects surfaced from code-path + Dataverse-state verification. No ISS-NNN filed.

---

## 8. References

- **Wave 12 plan**: `notes/wave12-mvp-completion-plan.md` §3.2 (ACs)
- **Audits**:
  - `notes/audits/wave12-121-wizard-file-summary.md`
  - `notes/audits/wave12-122-document-create-profile.md`
  - `notes/audits/wave12-123-three-prefill-wizards.md`
  - `notes/audits/wave12-124-wave5-backfill-health-sweep.md`
- **Task POMLs**: `tasks/140-restore-wizard-file-summary.poml` through `tasks/145-wizard-end-to-end-uat.poml`
- **Per-wizard commit SHAs**: §1 above
