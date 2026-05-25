# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-05-25 (pre-compact checkpoint)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — task 023 COMPLETE; next is Phase 3 baseline |
| **Step** | — |
| **Status** | **Phase 3 READY** — Phase 0/1/2 + task 019 (Linux migration) + task 023 (auth refactor) all done; new Linux dev verified end-to-end including user's document upload + AI summary |
| **Next Action** | Three items in sequence: (1) decommission old Windows dev `spe-api-dev-67e2xz` + `spe-plan-dev-67e2xz` via `az webapp delete` + `az appservice plan delete`; (2) start Phase 3 baseline Group E tasks (030, 031, 032, 034, 035, 036, 038 — parallel-safe) + task 033 (48h App Insights calendar gate — start ASAP); (3) task 037 (commit BASELINE.md) gates Phase 4. |

### Files Modified This Session

**ALL COMMITTED — clean state.** Recent commits (in order):

| Commit | What |
|---|---|
| `e5350ef9` | Phase 0 COMPLETE — gate signed; tasks 001-009 done |
| `385957a3` | Phase 1 COMPLETE — INVENTORY.md + 6 critical findings |
| `037c7e2c` | task 019 — Linux dev migration (spaarke-bff-dev provisioned + verified) |
| `2066b98e` | task 019 cutover — Dataverse env var flipped, all references updated |
| `5d476d34` | Phase 2 COMPLETE — CANDIDATES.md gate signed |
| `6bfe193a` | task 023 checkpoint — DI-singleton TokenCredential WIP |
| `7fb1776f` | **task 023 COMPLETE** — auth-r2 architectural fix (19 services refactored) |

### Critical Context

**Where the project is**: Past Phase 2 gate. Two unplanned tasks intercepted between Phase 2 and Phase 3:
- **Task 019** (Linux dev migration) — resolved Phase 1 Finding 1 (dev was Windows; demo + prod are Linux). New `spaarke-bff-dev` provisioned in `rg-spaarke-dev`. Cross-RG UAMI attachment proved correct (zero re-registration in Dataverse/Graph/Exchange).
- **Task 023** (DI-singleton TokenCredential refactor) — auth-r2 follow-on bug. Discovered when user spot-checked cutover and `useAiSummary` PCF hook got HTTP 500 on playbook resolve. Root cause: 20+ services constructed `new DefaultAzureCredential()` without UAMI ClientId, failing on multi-identity App Services. User explicitly rejected band-aid: "we don't want to just revert back we need to actually fix the issue." Architectural fix: register `TokenCredential` as DI singleton, inject into all 19 affected services. Zero `new DefaultAzureCredential()` calls remain in BFF prod code (exception: GraphClientFactory's dual ClientSecret + MI path, intentional).

**Live state**:
- New Linux dev `spaarke-bff-dev` is the live traffic target (PCFs/Code Pages hit it via Dataverse env var `sprk_BffApiBaseUrl`)
- Old Windows dev `spe-api-dev-67e2xz` still running in parallel — needs decommission as next step
- User confirmed document upload + AI summary works end-to-end on new env

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | Phase 3 READY |
| **Status** | none — awaiting operator authorization to start Phase 3 sequence |
| **Started** | — |

---

## Next Actions (in execution order)

### Action 1: Decommission old Windows dev

Old `spe-api-dev-67e2xz` + `spe-plan-dev-67e2xz` are no longer needed. UAMI `mi-bff-api-dev` STAYS (used by new env). Old RG `spe-infrastructure-westus2` contains other resources — do NOT delete the RG.

```bash
az webapp delete --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0
az appservice plan delete --name spe-plan-dev-67e2xz --resource-group spe-infrastructure-westus2 --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0 --yes
```

### Action 2: Phase 3 baseline (tasks 030-038)

Per `TASK-INDEX.md` and `plan.md`. Dispatch on the new Linux dev `spaarke-bff-dev`.

**Group E parallel-safe** (~25 min active total):
- 030: `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — capture pass/fail counts + duration
- 031: Build warning count baseline (already 17 per task 023 build — just record)
- 032: Endpoint smoke-test pass (per `docs/guides/auth-deployment-setup.md` §9)
- 034: SHA-256 of deployed DLLs via Kudu VFS
- 035: Publish + zip metrics (uncompressed/compressed/file count)
- 036: Reflection-load probe (re-use Phase 1 deps.json approach — already documented)
- 038: DI registration count baseline (ADR-010 measurable binding)

**Sequential calendar gate**:
- 033: App Insights 48h baseline metrics — start the 48h window ASAP

**Sequential after all**:
- 037: Aggregate + commit `baseline/BASELINE.md` — Phase 3 gate; without it Phase 4 cannot start

### Action 3 (residual, doesn't block Phase 3)

- **Operator weekly monitoring** of worktree branches for new BFF-touching work during Phase 3-4
- **Operator G4 written facade adoption agreement** with Insights Engine owner — needed BEFORE Phase 4 task 046
- **Graph email subscriptions** — currently still pointing at old dev URL; ~3-day auto-expire OR operator can manually PATCH for faster cutover (only affects incoming email webhooks, not general PCF/Code Page traffic)

---

## Progress (full project)

### Completed Phases

- ✅ Phase 0 — Pre-flight resolution (commit `e5350ef9`)
- ✅ Phase 1 — Inventory + 6 critical findings (commit `385957a3`)
- ✅ Task 019 — Linux dev migration (commits `037c7e2c` + `2066b98e`)
- ✅ Phase 2 — Categorization + CANDIDATES.md gate (commit `5d476d34`)
- ✅ Task 023 — DI-singleton TokenCredential architectural refactor (commits `6bfe193a` + `7fb1776f`)

### Pending

- ⬜ Decommission old Windows dev (Action 1)
- ⬜ Phase 3 baseline tasks 030-038 (Action 2)
- ⬜ Phase 4 Apply changes (Outcome A SAFE + B MEDIUM + E parallel track)
- ⬜ Phase 5 Promote demo + prod
- ⬜ Phase 6 Prevention/codification
- ⬜ Task 090 — Wrap-up + LESSONS-LEARNED

### Decisions Made (all sessions)

- **2026-05-20**: Pipeline scaffolding generated.
- **2026-05-24** (task 001): Owner ACK'd all 9 §3 Resolved Decisions.
- **2026-05-24** (Phase 0): NFR-06 rollback drill PASS at 2m 23s.
- **2026-05-24** (Phase 1): 6 critical findings (dev/prod OS mismatch resolved via task 019; demo + prod exist; HIGH Kiota CVE → accepted risk; FR-A3 already no-op; pre-release pins valid; 4 zero-static-usage packages verified live).
- **2026-05-24** (task 019): Linux dev migration succeeded via UAMI cross-RG attachment.
- **2026-05-24** (cutover): Live traffic flipped via Dataverse env var.
- **2026-05-24** (Phase 2): 3 SAFE, 1 MEDIUM, 0 HIGH, 15 REJECT. Kiota HIGH CVE accepted risk per Decision C.1.
- **2026-05-24/25** (task 023): Architectural DI-singleton TokenCredential refactor over band-aid per user direction. 19 services refactored. Zero `new DefaultAzureCredential()` in BFF prod code remaining.

---

## Recovery Instructions (post-compact)

1. Read this file
2. Confirm `git log --oneline -3` shows `7fb1776f` as HEAD (task 023 COMPLETE)
3. Confirm new dev healthy: `curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz` → 200
4. Proceed with Action 1 (decommission) → Action 2 (Phase 3 baseline)
5. Reference docs/guides/auth-deployment-setup.md §9 for endpoint smoke-test list (task 032)
6. Reference projects/sdap-bff-api-remediation-fix/inventory/INVENTORY.md for Phase 1 baseline data
7. Reference projects/sdap-bff-api-remediation-fix/CANDIDATES.md for what Phase 4 will do

---

## Quick Reference

- **Project**: sdap-bff-api-remediation-fix
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **INVENTORY.md**: [`inventory/INVENTORY.md`](./inventory/INVENTORY.md)
- **CANDIDATES.md**: [`CANDIDATES.md`](./CANDIDATES.md)
- **Linux migration record**: [`baseline/linux-dev-migration.md`](./baseline/linux-dev-migration.md)
- **Rollback drill record**: [`baseline/rollback-drill.md`](./baseline/rollback-drill.md)

### Azure resources

| Env | App Service | RG | Subscription | Status |
|---|---|---|---|---|
| dev (NEW, Linux) | `spaarke-bff-dev` | `rg-spaarke-dev` | `484bc857-3802-427f-9ea5-ca47b43db0f0` | LIVE traffic |
| dev (OLD, Windows) | `spe-api-dev-67e2xz` | `spe-infrastructure-westus2` | `484bc857-3802-427f-9ea5-ca47b43db0f0` | parallel run; decommission next |
| demo (Linux) | `spaarke-bff-demo` | `rg-spaarke-demo` | `2ff9ee48-6f1d-4664-865c-f11868dd1b50` | unused |
| prod (Linux) | `spaarke-bff-prod` | `rg-spaarke-platform-prod` | `484bc857-3802-427f-9ea5-ca47b43db0f0` | unused (first real deploy via Phase 5 task 062) |
| UAMI (shared) | `mi-bff-api-dev` | `spe-infrastructure-westus2` | (same) | attached cross-RG to new dev; LEAVE IN PLACE |
