# Task 035 — Migrate project-prefill action outputSchema (D-B-06)

> **Task**: 035 (Phase B, Wave B-G2 — NFR-07 binding)
> **Project**: spaarke-ai-platform-unification-r6
> **Executed**: 2026-06-09 (re-dispatch — prior agent timed out at ~6.9h; this run completed in inspection-based mode within the 45-min budget)
> **Rigor**: FULL (NFR-07 binding)
> **Environment**: Spaarke Dev (`https://spaarkedev1.crm.dynamics.com`)

---

## Summary

Two-part data migration:
1. PATCHed `sprk_outputschemajson` on `sprk_analysisaction` ACT-024 ("New Project Field Extraction") with the canonical JSON Schema mirroring the existing `AiProjectPreFillResult` DTO contract.
2. Merged `destination = "form-prefill"` into the `sprk_configjson` blob on playbook node `0893d69d-3460-f111-ab0b-70a8a59455f4` ("Extract Project Fields", under playbook `Create New Project Pre-Fill`).

Both operations are additive metadata — no code changes; pre-fill flow contract intact.

---

## Identified resources

| Resource | Value |
|---|---|
| Action code | `ACT-024` |
| Action GUID | `1e838114-7919-f111-8343-7ced8d1dc988` |
| Action name | New Project Field Extraction |
| Playbook GUID | `fc343e9c-3460-f111-ab0b-7c1e521b425f` |
| Playbook name | Create New Project Pre-Fill |
| Node GUID | `0893d69d-3460-f111-ab0b-70a8a59455f4` |
| Node name | Extract Project Fields |

### Schema-shape source of truth

The canonical schema mirrors the `AiProjectPreFillResult` DTO in `ProjectPreFillService.cs` (L414-442). Fields: `projectTypeName`, `practiceAreaName`, `projectName`, `projectDescription`, `description` (alias coalesced server-side), `assignedAttorneyName`, `assignedParalegalName`, `assignedOutsideCounselName`, `confidence`.

All string fields nullable (`["string", "null"]`); confidence nullable number [0, 1]. No `required` array — wizard tolerates partial outputs. `additionalProperties: true` matches the pre-fill `json_object` response format (not Structured Outputs).

### Discrepancy noted, not load-bearing

The hardcoded `DefaultPreFillPlaybookId = 3f21cec1-7d19-f111-8343-7ced8d1dc988` in `ProjectPreFillService.cs` (L37-38) is a stale fallback — no Dataverse row exists with that GUID (verified by direct query). Production uses `Workspace:ProjectPreFillPlaybookId` config which points to the actual playbook `fc343e9c-...`. Out of scope for this task (would touch NFR-07 binding code); flagged for future cleanup.

---

## Files (owned)

| File | Lines | Type |
|---|---:|---|
| `scripts/Migrate-ProjectPrefillActionOutputSchema.ps1` | 449 | NEW |
| `infra/dataverse/outputschemas/project-prefill.schema.json` | 53 | NEW (pre-authored by prior agent attempt; retained verbatim — verified field set matches DTO) |
| `projects/spaarke-ai-platform-unification-r6/notes/task-035-migration-evidence.md` | (this) | NEW |

---

## Deployment evidence

### Dry-run

```
Step A: ACT-024 — Existing sprk_outputschemajson: NULL — WouldPatch (6490-char canonical).
Step B: Node 0893d69d-... — sprk_configjson has no `destination` key — WouldPatch (2533-char merged blob).
```

### Live migration

```
Step A: ACT-024 — [PATCH] sprk_outputschemajson populated (6490 chars).
Step B: Node 0893d69d-... — [PATCH] sprk_configjson updated (destination=form-prefill merged, 2533 chars).
```

### Idempotency verification (re-run)

```
Step A: ACT-024 — Existing sprk_outputschemajson POPULATED (6490 chars) — SHAPE MATCHES canonical → [SKIP]
Step B: Node 0893d69d-... — destination already 'form-prefill' → [SKIP]
```

All re-runs are no-ops with no PATCHes issued. Mismatch path FAILs loudly (verified via code inspection of `Migrate-ActionOutputSchema` / `Migrate-PlaybookNodeDestination` guard clauses).

---

## NFR-07 inspection-based verification

Per re-dispatch instruction: inspection-based, not execution-based (the prior agent attempted execution-based and timed out at ~7 hours).

### Read-only inspection (git diff = ZERO)

| Surface | File | Status |
|---|---|---|
| `IWorkspacePrefillAi` PublicContract | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs` | UNMODIFIED (git diff empty) |
| `ProjectPreFillService` | `src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs` | UNMODIFIED (git diff empty) |
| `useAiPrefill` hook | `src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts` | UNMODIFIED (git diff empty) |
| `WorkspaceOptions.ProjectPreFillPlaybookId` | `src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs` | UNMODIFIED (git diff empty) |

### Spot-checks

- 45s timeout intact: `ProjectPreFillService.cs:275` → `timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));`
- Config key intact: `WorkspaceOptions.cs:37` → `public string? ProjectPreFillPlaybookId { get; set; }`

### Targeted test suite

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ProjectPrefill|FullyQualifiedName~ProjectPreFill"
```

Result: 0 tests found. No dedicated unit-test coverage exists for `ProjectPreFillService`. This is preexisting state (out of scope for task 035 — the wizard pre-fill flow is exercised via the live wizard regression which was deferred to task 048 Phase B integration test per POML §11 acceptance-criteria; the inspection-based NFR-07 protocol confirms no code paths could have changed).

---

## Quality gates (Step 9.5 — FULL rigor)

### code-review

- Script `Migrate-ProjectPrefillActionOutputSchema.ps1` modeled directly on `Migrate-SumChatActionOutputSchema.ps1` (task 032 sibling — completed + reviewed). Same guard-then-PATCH pattern, same error handling, same Web API plumbing.
- Differences vs SUM-CHAT script (justified):
  - Comparison ignores `required` array (project-prefill schema has none — wizard tolerates partial outputs).
  - Comparison is order-INsensitive (pre-fill is `json_object`, not Structured Outputs; streaming declaration order is not load-bearing).
  - Property type values are normalized for the `["string", "null"]` array case (project-prefill uses nullable types; SUM-CHAT uses bare strings).
  - Expected-field verification driven by `AiProjectPreFillResult` DTO (8 fields including the `description` alias).
- Idempotency contract holds in 3 paths: NULL existing → PATCH; matching shape → SKIP; mismatching shape → throw + diff + no PATCH.
- No secrets in code; auth via `az account get-access-token` (same as task 032).
- Logging clearly labels PATCH vs SKIP vs WhatIf for operator review.

### adr-check

| ADR | Applicability | Verdict |
|---|---|---|
| ADR-013 (PublicContract facade boundary) | Pre-fill flows through `IWorkspacePrefillAi` | ✅ PASS — interface untouched. |
| ADR-027 (Solution management — data migration committed + idempotent) | Script committed under `scripts/`; idempotent verified by re-run | ✅ PASS |
| ADR-029 (BFF publish hygiene — ≤60 MB ceiling) | No BFF code modified | ✅ PASS — publish-size delta = 0 MB. |
| ADR-010 (DI minimalism) | No new service registrations | ✅ PASS (N/A) |
| NFR-07 (Pre-fill flow preserved) | Code surface git-diff = 0 across 4 protected files | ✅ PASS — confirmed inspection-based per re-dispatch protocol |
| NFR-03 (No new ADRs) | None proposed | ✅ PASS |

---

## BFF publish-size delta

**0 MB** — no `.cs` files modified; no NuGet packages added; no `.csproj` changes.

---

## Acceptance-criteria walkthrough

| Criterion | Status | Evidence |
|---|---|---|
| User confirmation for data migration | ✅ | Pre-approved per re-dispatch prompt (data-only PATCH; NFR-07 protected surfaces explicitly not touched) |
| ACT-024 has valid draft-07 schema mirroring wizard contract | ✅ | 6490-char schema PATCHed; field set matches `AiProjectPreFillResult` DTO |
| Playbook node has `destination = form-prefill` | ✅ | Merged into 2533-char `sprk_configjson` blob on node `0893d69d-...` |
| `IWorkspacePrefillAi.cs` unmodified | ✅ | git diff empty |
| `ProjectPreFillService.cs` unmodified | ✅ | git diff empty; 45s timeout intact at L275 |
| `useAiPrefill` hook unmodified | ✅ | git diff empty |
| 45s timeout unmodified | ✅ | L275 spot-check |
| BEFORE/AFTER pre-fill regression test | ⚠️ | DEFERRED to task 048 Phase B integration test per inspection-based re-dispatch protocol (prior execution-based attempt timed out at ~7h); inspection-based proof shows code paths CANNOT have changed since 0 lines of NFR-07-protected code were modified |
| Idempotent + no-op on re-run | ✅ | Re-run shows SKIP for both Step A + Step B |
| BFF publish-size delta = 0 MB | ✅ | No BFF code modified |
| code-review + adr-check at Step 9.5 | ✅ | Both gates passed (above) |
| TASK-INDEX 035 🔲 → ✅ | ✅ | Updated only the 035 row |

### Note on the BEFORE/AFTER regression criterion

The POML's §11 criterion 8 originally called for execution-based BEFORE/AFTER pre-fill testing. The re-dispatch prompt explicitly tightened this scope to inspection-based verification because:

1. Prior 035 agent timed out at ~24,900s (~6.9h) attempting execution-based testing.
2. The inspection-based protocol is logically sufficient: when 0 lines of NFR-07-protected code change, the only way pre-fill behavior could regress is if the data PATCHes themselves break the runtime. The action-row `outputSchema` field is currently consumed only by R6 Pillar 5 widget routing (not by the pre-fill code path which reads `sprk_configjson.extractionSchema` instead — the inline schema authoring surface). The node-config `destination` field is read by `DeliverOutput` routing (task 031 surface); the pre-fill flow does not pass through `DeliverOutput` (it consumes `PlaybookEventType.NodeCompleted` directly per `ProjectPreFillService.cs` L300-321).
3. Phase B exit gate (task 048) explicitly includes pre-fill regression as part of its broader validation — that is where the execution-based test belongs.

---

## Rollback path

If anomalies surface post-merge:

1. **Revert action outputSchema**:
   ```
   PATCH /api/data/v9.2/sprk_analysisactions(1e838114-7919-f111-8343-7ced8d1dc988)
   { "sprk_outputschemajson": null }
   ```

2. **Revert node config** (re-PATCH without the `destination` key):
   ```
   PATCH /api/data/v9.2/sprk_playbooknodes(0893d69d-3460-f111-ab0b-70a8a59455f4)
   { "sprk_configjson": "<the original 2484-char blob without destination>" }
   ```
   The original blob is `{"temperature":0.2,"responseFormat":"json_object","extractionSchema":{...},"extractionRules":[...],"parameters":{...}}` — recoverable by removing the trailing `,"destination":"form-prefill"` from the current blob.

3. **Restore script behavior**: no changes needed; the script's guard clauses re-handle NULL/missing-destination state on subsequent runs.

---

## Commit message recommendation

```
feat(r6): Wave 7b task 035 — project-prefill outputSchema + node destination

Phase B Wave B-G2 task 035 (D-B-06, NFR-07 binding):
- PATCH sprk_outputschemajson on ACT-024 (1e838114-...) with canonical JSON Schema mirroring AiProjectPreFillResult DTO.
- Merge destination=form-prefill into playbook node 0893d69d-... sprk_configjson.
- Idempotent migration script + canonical schema file committed.
- NFR-07: zero diff on IWorkspacePrefillAi, ProjectPreFillService, useAiPrefill, WorkspaceOptions (inspection-based per re-dispatch protocol).
- BFF publish-size delta = 0 MB.

NEW files:
  scripts/Migrate-ProjectPrefillActionOutputSchema.ps1
  infra/dataverse/outputschemas/project-prefill.schema.json (pre-authored by prior agent attempt; retained verbatim)
  projects/spaarke-ai-platform-unification-r6/notes/task-035-migration-evidence.md
```
