# Current Task State — Set-Regarding and Field-Mapping Resolver R2

> **Last Updated**: 2026-07-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This project is CODE-COMPLETE, merged to master, and deployed to dev; it's in live UAT.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | set-regarding-and-field-mapping-resolver-r2 |
| **Status** | ✅ Implementation complete + merged to master + BFF deployed to dev + UAT in progress |
| **Master HEAD** | `4d85deef7` (worktree = origin/master = main-repo master, all synced) |
| **Next Action** | Commit task-016 convergence + push/merge. Then only **090 wrap-up** remains open. (User still to import `VisualHostSolution_v1.4.34.zip` for the UI refinement.) |
| **Task 016** | ✅ 2026-07-12 — matterService nav-prop convergence done (approach C; payload byte-identical; 183 tests green). Only 090 remains. |

### The v1.4.34 zip to import
`C:\code_files\spaarke-wt-set-regarding-and-field-mapping-resolver-r2\src\client\pcf\VisualHost\Solution\bin\VisualHostSolution_v1.4.34.zip`
`pac solution import --path "…VisualHostSolution_v1.4.34.zip" --publish-changes` → hard-refresh → footer shows v1.4.34.

### Critical Context
All 16 tasks done. During UAT, the profile-fetch endpoint 500'd; root cause was **3 compounding latent BFF bugs** (all pre-existing; this engine was the first real consumer), now fixed + verified live (endpoint returns 200 with 8 rules). A 4th (cosmetic) client refinement removed noisy warnings for empty source lookups. Everything is on master; BFF is deployed to dev; VisualHost v1.4.34 is the only thing left to import.

---

## Deployment / environment facts
- **BFF dev**: `spaarke-bff-dev` (`rg-spaarke-dev`) → Dataverse `spaarkedev1`; Dataverse app-user `1e40baad-e065-4aea-a8d4-4b7ab273458c` (client-secret). BFF API audience = `api://1e40baad…` (a token for it is gettable via `az account get-access-token --scope api://1e40baad…/.default`).
- **BFF auto-deploy workflow is INACTIVE** — deploys are manual via `scripts/Deploy-BffApi.ps1` (pwsh). Package ~46.49 MB is CORRECT (skill's 55-65 MB figure is stale). Endpoint verified 200: `/api/v1/field-mappings/profiles/sprk_matter/sprk_event`.
- **Dataverse (live in spaarkedev1)**: `sprk_expression` column added; 3 profiles seeded — Matter→Event (8 Copy), Matter→Invoice (4), Matter→Report Card (8); created the missing Report Card `sprk_recordtype_ref` row `5bc206a0-587b-f111-ab0e-7ced8ddc4a05`.
- **VisualHost** is the sole wizard host (via VisualHostRoot.tsx "+" create-wizard button). It imports the shared-lib wizards/engine from SOURCE (relative paths), so rebuilds bundle the latest engine. `@spaarke/auth` must be built (`npm run build` in src/client/shared/Spaarke.Auth) before a VisualHost `build:prod`.

## The 4 bugs fixed during UAT (all on master)
1. **`DataverseWebApiService` BaseAddress missing trailing slash** — `…/api/data/v9.2` + relative URI drops `v9.2` → versionless URL → Dataverse 500. Shared-infra fix (affects ALL DataverseWebApiService HTTP calls). Commit in `772717fd5`.
2. **Wrong `$expand` nav-prop** `sprk_fieldmappingprofile_fieldmappingrule` → `sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile` (`5c340aa55`).
3. **Unguarded `GetInt32()`/`GetBoolean()` on null response fields** in `MapToFieldMappingRuleEntity` (Dataverse `$expand` includes nulls) → guarded all reads (`772717fd5`).
4. **Client engine warned on empty source lookups** → now skips silently; warns only for populated-lookup-missing-annotation anomaly (`c9dde24a4`); VisualHost v1.4.34 bundles it (`4d85deef7`).

## Debugging lesson (recorded)
Hand-built repro queries with hardcoded GUIDs diverged from the BFF's actual path (skipped Step 1 LookupRecordTypeIdsAsync). The **container-log stack trace** (Kudu `…/api/vfs/LogFiles/<date>_containerStream.log`) was the turning point — pull logs earlier next time.

## Task status (all 16 + follow-ups)
- Phase 0: 001 ✅ 002 ✅ 003 ✅ · Phase 1: 010 ✅ 011 ✅ 012 ✅ 013 ✅ 014 ✅ 015 ✅ **016 ✅ (2026-07-12)** · Phase 2: 020 ✅ 021 ✅ 022 ✅ · Phase 3: 030 ✅ · Phase 4: 040 ✅ 041 ✅.
- **090 wrap-up NOT formally run** — the ONLY remaining task; `/test-diet`, `/repo-cleanup`, README/plan → Complete, lessons-learned still pending if a formal close is wanted.

## Deferred / follow-ups
- **016 ✅ DONE (2026-07-12)** — matterService converged onto shared `discoverNavProps` via new `toNavPropMap` adapter (approach C). Create payload byte-identical (equivalence test). §6.5 resolved with no ADR tension. See `notes/task-011-BLOCKED.md` RESOLUTION footer. Out-of-scope discovery sites still independent: `CreateProjectWizard.tsx` (component-level), `TodoRegardingUpdateBuilder.ts`.
- **To Do follow-on** child-creation path (`createTodoRegardingChild`, from invoice/reportCard wizards) left unwired — gracefully no-ops.
- **Matter/Project** parent link is post-create N:N, not pre-create regarding — engine fires only when an `association` is present (forward-compatible).
- **Report Card reverse-lookup**: registry `sprk_regardingfield=sprk_regardingreportcard` names the convention only; confirm if a physical column is needed.

## Notes
- No Dataverse plugin, no form script, no new PCF (owner constraints honored). BFF change additive (publish flat).
- Test posture: 18 engine unit tests; BFF DTO/push-regression tests; 16 pre-existing full-suite failures (WorkspaceShell/FilePreview/recordHeader/etc.) confirmed unrelated via stash-baseline.
