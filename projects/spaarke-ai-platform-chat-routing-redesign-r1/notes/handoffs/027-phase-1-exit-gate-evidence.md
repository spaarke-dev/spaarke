# Task 027 — Phase 1 Exit Gate Evidence

> **Date**: 2026-06-22
> **Author**: Wave 1-J task 027 (MINIMAL rigor — pure verification)
> **Verdict**: ✅ **GO for Phase 2** (local verification path; task 026 deferred per owner)

---

## Summary

Phase 1 stable-ID migration is complete and verified. All grep audits pass; the integration regression suite is green; no residual `Guid.Parse("...")` hardcoded resolution sites remain in `Services/Ai/`. Two `const string` declarations for stable IDs in `AppOnlyAnalysisService.cs` are **intentional Pattern B execution-path constants** (task 020 design) — they are the **inputs to `IPlaybookLookupService.GetByIdAsync(...)`**, not residual hardcoded lookups.

Task 026 (bff-dev Azure deploy) is deferred to a managed window per owner authorization 2026-06-22; the exit gate uses **local verification only**.

---

## Acceptance criteria results

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `grep "Guid.Parse(\"44285d15\""` and the 4 sibling GUIDs return zero in `Services/Ai/` | ✅ ZERO matches |
| 2 | Literal playbook-name strings absent from `useAiSummary.ts` + `DocumentEmailWizard.tsx` (live code) | ✅ Only JSDoc/comments + debug log strings remain |
| 3 | `/by-name/` URL pattern absent from frontend live code | ✅ Only test-negation assertions + JSDoc comments remain |
| 4 | Task 025 integration regression suite green | ✅ 10/10 pass, 186 ms |
| 5 | Task 026 deploy evidence | ⏭️ **DEFERRED** (owner authorization 2026-06-22) — local-only path |
| 6 | Exit-gate evidence note written | ✅ this file |
| 7 | If GO: TASK-INDEX 027 → ✅; Phase 2 may begin | → applied below |

---

## Audit 1 — GUID literals in Services/Ai/

### Strict FR-02 acceptance check

```
grep -Rn 'Guid\.Parse\("(44285d15|2d660cad|fc343e9c|4a72f99c|18cf3cc8)' src/server/api/Sprk.Bff.Api/Services/Ai/
→ 0 matches
```

✅ **PASS** — no `Guid.Parse("...")` hardcoded GUID resolution remains.

### Loose prefix scan (informational)

```
grep -Rn '44285d15\|2d660cad\|fc343e9c\|4a72f99c\|18cf3cc8' src/server/api/Sprk.Bff.Api/Services/Ai/
```

| File:Line | Match | Classification |
|---|---|---|
| `AppOnlyAnalysisService.cs:67` | `const string DocumentProfilePlaybookId = "18cf3cc8-..."` | ✅ **Pattern B execution-path const** — input to `_playbookLookup.GetByIdAsync(...)` at line 129 (task 020 design) |
| `AppOnlyAnalysisService.cs:79` | `const string EmailAnalysisPlaybookId = "bc71facf-..."` | ✅ (Not in 5-GUID list; informational only — same Pattern B design) |
| `Chat/Playbooks/summarize-document-for-chat.playbook.json:2,65` | JSON playbook definition `sprk_analysisplaybookid` | ✅ **Not code** — playbook's own GUID in its definition file |
| `Chat/SessionSummarizeOrchestrator.cs:64,174` | XML doc comments noting removed GUID | ✅ **Documentation** — code uses `WorkspaceOptions.ChatSummarizePlaybookId` → `_playbookLookup.GetByIdAsync(...)` (lines 180–184) |

**Verdict**: ✅ No residual hardcoded GUID resolution. The 2 const declarations are Pattern B execution-path constants (per task 020 design rationale captured in `AppOnlyAnalysisService.cs:54-66`); they are *inputs* to the stable-ID lookup service, not bypasses of it.

---

## Audit 2 — Literal playbook-name strings in frontend

### `useAiSummary.ts`

```
grep -n 'Document Profile\|Summarize New File\|summarize-document-for-(chat|workspace)@v1' src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts
```

| Line | Match | Classification |
|---|---|---|
| 14, 32 | JSDoc | ✅ Documentation explaining stable-ID const |
| 305, 307, 338 | Inline comments + `console.log` | ✅ Debug observability; not a live name-resolution call |

**Live code path** uses `DOCUMENT_PROFILE_PLAYBOOK_ID` constant → `/api/ai/playbooks/by-id/${id}` (per task 021 commit; verified by Jest test `useAiSummary.playbookLookup.test.ts`).

### `DocumentEmailWizard.tsx`

```
grep -n 'Summarize New File\|Document Profile\|Create New Matter Pre-Fill\|Create New Project Pre-Fill' src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx
```

| Lines | Match | Classification |
|---|---|---|
| 70, 154, 174, 177, 617, 649 | JSDoc + inline comments | ✅ Documentation explaining stable-ID const + Pattern B migration |

**Live code path** uses `SUMMARIZE_NEW_FILES_PLAYBOOK_ID` constant → `/api/ai/playbooks/by-id/${id}` (per task 021 commit; verified by `DocumentEmailWizard.playbookLookup.test.ts`).

✅ **PASS** — no live literal name resolution in either file.

---

## Audit 3 — `/by-name/` URL pattern in frontend

```
grep -Rn '/by-name/' src/client/shared/Spaarke.UI.Components/src/
```

| File:Line | Match | Classification |
|---|---|---|
| `hooks/useAiSummary.ts:19` | JSDoc explaining retired route | ✅ Documentation |
| `components/DocumentEmailWizard/DocumentEmailWizard.tsx:160, 651` | JSDoc + inline comments | ✅ Documentation |
| `hooks/__tests__/useAiSummary.playbookLookup.test.ts:6, 98, 115` | Jest negation assertions (`not.toContain('/by-name/')`) | ✅ **Regression protection** — intentional |
| `components/DocumentEmailWizard/__tests__/DocumentEmailWizard.playbookLookup.test.ts:6, 50, 54, 62` | Jest negation assertions | ✅ **Regression protection** — intentional |

✅ **PASS** — no live `/by-name/` call sites in production code. Tests assert the absence — intentional regression protection per spec FR-03 acceptance.

---

## Audit 4 — Task 025 integration regression suite

```
dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ \
  --filter "FullyQualifiedName~Phase1StableIdMigration" --nologo
```

**Result**: ✅ Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: **186 ms**

Suite covers all 9 consumer surfaces:
- 5 Pattern A (Workspace options) — `MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService`, `WorkspaceFileEndpoints`, `SessionSummarizeOrchestrator`
- 4 Pattern B (frontend + AppOnly) — `AppOnlyAnalysisService` (Document Profile + Email Analysis), `useAiSummary.ts`, `DocumentEmailWizard.tsx`
- Plus `InvoiceExtractionJobHandler` (typed `FinanceOptions.InvoiceExtractionPlaybookId`)

---

## Audit 5 — Task 026 deploy evidence

⏭️ **DEFERRED** per owner authorization 2026-06-22. The bff-dev Azure deploy is held for a managed window. Exit gate uses **local verification only** (audits 1–4 above + BFF build clean).

---

## Branch + build state

- Branch: `work/spaarke-ai-platform-chat-routing-redesign-r1`
- Last commit: `c2920dd2c` (handoff) on top of `6b0d74605` (post-rebase)
- BFF build: **0 errors**, 16 pre-existing warnings (unchanged baseline)
- PR #409: still DRAFT (force-push updated)

---

## GO/NO-GO decision

🟢 **GO for Phase 2**

All Phase 1 exit criteria met by local verification. Phase 2 (WP1.5 Index Governance — MVP-trimmed to 11 tasks; task 030 demoted to verification per Q4; task 031 adds `sprk_jpsmatchingmetadata`) is unblocked.

**Next action**: Begin Wave 2-A. Read `tasks/030-*.poml` (verification-only per Q4) and dispatch.

---

## Related artifacts

- `notes/handoffs/025-phase-1-test-suite.md` — task 025 evidence
- `notes/handoffs/021-frontend-pattern-b-delta.md` — Pattern B migration deltas
- `notes/handoffs/Q1-refactor-playbookcode-to-playbookid.md` — Q1 field-naming refactor
- `notes/handoffs/014-playbookid-backfill-evidence.md` — Dataverse `sprk_playbookid` backfill
