# Phase 0 Baseline — Smoke Test Results

> **Date**: 2026-06-21
> **Task**: 004 (Phase 0 smoke test)
> **Wave**: 0-B
> **Rigor**: STANDARD
> **Outcome**: ✅ **GO for Phase 1**

---

## Scope of Wave 0-A

Wave 0-A (tasks 001, 002, 003) executed with one task DEFERRED:

| Task | Status | Files touched |
|---|---|---|
| 001 — Delete LegalWorkspace CreateMatter/CreateRecordStep + Project + WorkAssignment siblings | ⏭️ **DEFERRED** to post-Phase 1 per owner (B-001 option b) | none |
| 002 — Delete PCF UniversalQuickCreate `useAiSummary.ts` duplicate | ✅ | `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` (deleted), `useRecordMatch.ts` (repointed to `@spaarke/ui-components/src/hooks`) |
| 003 — Scrub stale `3f21cec1-` GUID comments | ✅ | `Configuration/WorkspaceOptions.cs:35`, `Services/Workspace/ProjectPreFillService.cs:39` |

**Critical**: Because task 001 was deferred, **no LegalWorkspace files were modified by Wave 0-A**. Any LegalWorkspace build issues observed here pre-date this wave (last LW commit was `b2198d430` in `daily-update-r2`).

---

## Step Results

### Step 1 — BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`)

- ✅ **PASS**
- Errors: **0**
- Warnings: **16** (all pre-existing — obsolete `DemoProvisioningOptions` members during in-progress migration, CS1998 async-without-await on null-object/orchestrator stubs, CS8604/CS8601 nullable warnings in `ChatEndpoints` + `AgentEndpoints`)
- Output: `Sprk.Bff.Api.dll` produced
- Time: ~11s

### Step 2 — LegalWorkspace build (`npm install --legacy-peer-deps` then `npm run build`)

- ⚠️ **FAIL — pre-existing baseline issue, NOT introduced by Wave 0-A**
- Install: 229 packages added in ~2 min, OK
- Build failure: `[vite]: Rollup failed to resolve import "@spaarke/daily-briefing-components/widgets" from "src/sections/dailyBriefing/dailyBriefing.registration.ts"`
- Root cause: `dailyBriefing.registration.ts` was last modified in commit `b2198d430` (R2.1 hotfix from daily-update-r2 project). No commits to `src/solutions/LegalWorkspace/` since 2026-06-19 — Wave 0-A did not touch this code. The unresolved import is a baseline issue in the shared library `@spaarke/daily-briefing-components` `dist/` build output (same family as the PCF baseline drift below).
- **Verdict**: NOT a regression caused by Wave 0-A. Should be tracked separately; LegalWorkspace build is independent of Phase 1 BFF migrations.

### Step 3 — UniversalQuickCreate PCF build (`npm run build:prod`)

- ⚠️ **FAIL — exactly matched the KNOWN BASELINE drift bucket documented in task brief**
- All 10 errors observed are on the known list (last touched 2026-06-06, commit `d4614a11f`, PR #364):
  - `TS2307: Cannot find module '@spaarke/ui-components/dist/utils/themeStorage'` (missing dist build artifact)
  - `TS2305: '@spaarke/ui-components/src/hooks' has no exported member 'SseStreamStatus' / 'SseDataChunk' / 'UseSseStreamResult' / 'UseSseStreamOptions'`
  - `TS2322: Type 'ReactElement<unknown, ...>' is not assignable to 'ReactElement<any, ...>'` (duplicated `@types/react` in nested node_modules)
  - `TS1323: Dynamic imports are only supported when '--module' flag is set to ...` (tsconfig misalignment for `useChatFileAttachment.ts`)
  - `TS7006: Parameter 'isDark' implicitly has an 'any' type` (cascading consequence of the themeStorage TS2307)
- **Verdict**: Conditional pass — all errors are pre-existing in `@spaarke/ui-components`, predate Wave 0-A. None are caused by task 002 (deletion of `useAiSummary.ts`) — DocumentUploadForm.tsx already imports from `@spaarke/ui-components/src/hooks` (the canonical hook source).

### Step 4 — BFF unit tests (`dotnet test tests/unit/Sprk.Bff.Api.Tests/`)

- ✅ **PASS**
- **Passed: 7,313**
- **Failed: 0**
- **Skipped: 110**
- **Total: 7,423**
- Duration: 1m 21s

### Step 5 — Broken-reference grep verification

| Check | Result |
|---|---|
| `grep -rn "from.*UniversalQuickCreate/control/services/useAiSummary" src/` | **0 matches** ✅ |
| `grep -rn "import.*from.*'./services/useAiSummary'" src/client/pcf/UniversalQuickCreate/` | **0 matches** ✅ |
| Verify `DocumentUploadForm.tsx` imports `useAiSummary` from `@spaarke/ui-components/src/hooks` | ✅ Confirmed at `DocumentUploadForm.tsx:41` |
| `grep "3f21cec1-" Configuration/WorkspaceOptions.cs` | **0 matches** ✅ |
| `grep "3f21cec1-" Services/Workspace/ProjectPreFillService.cs` | **0 matches** ✅ |

(Note: stale bundle.js artifacts under `Solution/src/WebResources/.../bundle.js` reference the old `useAiSummary.ts` path. These are previously-emitted build outputs and will be regenerated on next successful PCF build; not a source-code regression.)

### Step 6 — BFF publish-size baseline (`dotnet publish -c Release`)

- Publish command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- **Uncompressed**: 146,204,631 bytes (~141 MB) — includes all RID-specific binaries and globalization satellite assemblies
- **Compressed (zip)**: **46,927,912 bytes ≈ 44.75 MB**
- **NFR-01 ceiling**: ≤60 MB compressed
- **CLAUDE.md §10 stated baseline (2026-05-26 post-Phase 5 Outcome A)**: ~45.65 MB
- **Delta vs §10 baseline**: -0.9 MB (slight reduction, well within noise floor)
- **Status**: ✅ Within all thresholds; no escalation needed

---

## Phase 0 GO / NO-GO Decision

**GO for Phase 1** ✅

Rationale:
1. BFF builds cleanly with zero errors; the 16 warnings are all pre-existing baseline noise.
2. BFF unit tests pass 100% (7,313 / 7,313 non-skipped).
3. Wave 0-A deletions (task 002) introduced ZERO broken references in source. The only matches are previously-emitted bundle.js artifacts that will be regenerated.
4. Wave 0-A comment scrub (task 003) is verified clean — zero `3f21cec1-` references remain in the two target files.
5. BFF publish size at 44.75 MB compressed — under the 45.65 MB baseline and well under the 60 MB hard ceiling.
6. Both client-build failures (LegalWorkspace + UniversalQuickCreate PCF) are documented PRE-EXISTING baseline drift unrelated to Wave 0-A and are not gating for Phase 1 BFF-focused work.

### Risks carried into Phase 1

1. **Shared lib `@spaarke/ui-components` dist drift** — affects PCF builds. Phase 1 work is BFF-focused, so this does not block the next wave, but any Phase 1 task that needs to build a PCF or LegalWorkspace bundle must first resolve the shared-lib build artifact issue. Track as a separate housekeeping item.
2. **LegalWorkspace `@spaarke/daily-briefing-components` import unresolved** — same family of shared-lib drift. Phase 0 task 001 deletions (DEFERRED) will need to coordinate with this housekeeping before Wave 0-A revisit.

---

## Files modified in this task

- `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/phase-0-baseline.md` (this file — NEW)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/TASK-INDEX.md` (task 004 row: 🔲📝 → ✅)

Also created (transient, not committed to git):
- `deploy/api-publish/` — publish output for size baseline
- `deploy/api-publish.zip` — compressed measurement
