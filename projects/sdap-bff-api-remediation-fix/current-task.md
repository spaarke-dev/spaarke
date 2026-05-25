# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-05-24 (late evening)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023 — Multi-identity credential refactor (auth-r2 follow-on bug fix)** |
| **Step** | 3 of ~7 (TokenCredential DI singleton registered; PlaybookService + DataverseHttpServiceBase done; ~17 more services to refactor) |
| **Status** | **IN PROGRESS — architectural fix mid-execution**. User explicitly authorized full DI-singleton refactor over inline helper-band-aid. Build + deploy + retest still required before old dev decommission. |
| **Next Action** | Continue refactoring services to inject `TokenCredential` via constructor instead of constructing `new DefaultAzureCredential()`. See "Mini-Plan: Task 023" below for full file list + pattern. |

### Files Modified This Session (current state)

**Cutover artifacts (already committed):**
- `037c7e2c` — Linux dev migration (task 019)
- `2066b98e` — Cutover (config, workflows, scripts, webhook URL, Dataverse env var flip, auth doc)
- `5d476d34` — Phase 2 complete

**UNCOMMITTED — Task 023 refactor in progress:**
- ✅ `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` (NEW)
- ✅ `src/server/api/Sprk.Bff.Api/Program.cs` (added TokenCredential singleton registration)
- ✅ `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs` (refactored to call helper inline — needs DI injection refactor still)
- ✅ `src/server/api/Sprk.Bff.Api/Services/Ai/DataverseHttpServiceBase.cs` (same)

### Critical Context

**THE BUG**: 20+ BFF services construct `new DefaultAzureCredential()` without specifying which UAMI to use. On multi-identity App Services (System-Assigned + UAMI both attached), the credential chain fails with "Unable to load the proper Managed Identity." This surfaced when the user tested document upload after cutover and got `[useAiSummary] Failed to resolve playbook: HTTP 500` — root cause was `PlaybookService.cs:58 _credential = new DefaultAzureCredential()`.

**WHY IT EXISTS**: auth-r2 Phase C migrated services from `ClientSecretCredential` to `DefaultAzureCredential` but did NOT pass the UAMI ClientId. Worked accidentally on old dev because of some specific identity config that happened to resolve. Doesn't work on the new Linux dev where I attached UAMI explicitly + initial create command left SAM enabled too.

**THE FIX**: Architectural — register `TokenCredential` as DI singleton ONCE in Program.cs (calls `ManagedIdentityCredentialFactory.Create(config)`), inject into every affected service via constructor. NOT a helper-band-aid (rejected by user explicitly: "we don't want to just revert back we need to actually fix the issue").

**FIX SCOPE**: ~18 service refactors + 1 cross-assembly refactor (Spaarke.Dataverse/DataverseAccessDataSource.cs). Detailed file list in Mini-Plan below.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 023 (NEW — auth-r2 follow-on bug) |
| **Task File** | tasks/023-multi-identity-credential-refactor.poml (TO BE CREATED) |
| **Title** | Multi-identity credential refactor — inject TokenCredential via DI |
| **Phase** | Phase 2.5 (between Phase 2 and Phase 3 — discovered via cutover spot-check) |
| **Status** | in-progress |
| **Started** | 2026-05-24 (post-cutover) |
| **Rigor Level** | FULL (code change touches 18+ .cs files; potential compile breakage; deploy + retest required) |

---

## Mini-Plan: Task 023 — Multi-Identity Credential Refactor

### Pattern to apply

**Before** (broken):
```csharp
public class XService(IConfiguration configuration, ...)
{
    _credential = new DefaultAzureCredential();
}
```

**After** (correct):
```csharp
public class XService(TokenCredential credential, IConfiguration configuration, ...)
{
    _credential = credential;
}
```

Plus `using Azure.Core;` if needed, possibly remove `using Azure.Identity;` if no longer used.

### Files to refactor (status as of checkpoint)

**Already inline-fixed; need DI refactor (helper call → constructor injection):**
1. ✅ `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs:58`
2. ✅ `src/server/api/Sprk.Bff.Api/Services/Ai/DataverseHttpServiceBase.cs:41`

**Standard constructor pattern — straightforward fix:**
3. ⬜ `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:56`
4. ⬜ `src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentService.cs:40`
5. ⬜ `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs:71`
6. ⬜ `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs:77`
7. ⬜ `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs:72`
8. ⬜ `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/BulkRagIndexingJobHandler.cs:78`
9. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/DataverseCapabilityManifestLoader.cs:81`
10. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs:51`
11. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs:49`
12. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs:53`

**Services with options-aware path already (replace with injected):**
13. ⬜ `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs:221`
14. ⬜ `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs:415`
15. ⬜ `src/server/api/Sprk.Bff.Api/Services/Jobs/RecordSyncJob.cs:245-246` (ternary; consolidate to injected)

**DI module factories — use `sp.GetRequiredService<TokenCredential>()`:**
16. ⬜ `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiPersistenceModule.cs:66` (CosmosClient)
17. ⬜ `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs:93` (OpenAI inline)

**Mid-flight inline construction — promote credential to injected field:**
18. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs:391`
19. ⬜ `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionRestoreService.cs:363`

**Cross-assembly (Spaarke.Dataverse) — optional `TokenCredential? = null` with ClientSecret fallback preserved:**
20. ⬜ `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:65`

**LEAVE ALONE (already correct + has complex fallback):**
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — has OBO ClientSecret AND MI paths; correctly reads `Graph:ManagedIdentity:ClientId`. Could DI-ify later but not now.

### After refactor

1. Build: `dotnet build src/server/api/Sprk.Bff.Api/`
2. Run unit tests: `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — some may need constructor mocks updated
3. Deploy: `./scripts/Deploy-BffApi.ps1 -ResourceGroupName rg-spaarke-dev -AppServiceName spaarke-bff-dev`
4. Verify: `/healthz/dataverse` 200 + USER RETESTS document upload + AI summary
5. Re-enable SAM on new dev? — Probably NOT needed (UAMI is the canonical pattern). Keep SAM disabled.
6. Commit + push

### Deferred-by-this-fix items

- **OLD WINDOWS DEV STILL RUNNING** — pending operator OK after retest succeeds. Decommission via `az webapp delete` + `az appservice plan delete`.
- **Graph email subscription notificationUrl** — still points at old dev; auto-expires in ~3 days OR operator PATCHes for faster cutover. Doesn't block.
- **Phase 3 baseline (tasks 030-038)** — starts after task 023 + decommission done.

---

## Progress

### Completed Steps (across full project)

- Phase 0 ✅ (commit `e5350ef9`) — gate signed; all 9 tasks done
- Phase 1 ✅ (commit `385957a3`) — INVENTORY.md + 6 critical findings
- Task 019 ✅ (commit `037c7e2c`) — Linux dev provisioned + verified
- Cutover ✅ (commit `2066b98e`) — config/workflows/scripts/webhook/Dataverse env var flip
- Phase 2 ✅ (commit `5d476d34`) — CANDIDATES.md gate signed

### Current Step

Task 023 — multi-identity credential DI refactor. Started post-cutover after user spot-check revealed PlaybookService 500.

### Files Modified (Task 023 — uncommitted)

| File | State |
|---|---|
| `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` | NEW |
| `Program.cs` | TokenCredential singleton registered |
| `Services/Ai/PlaybookService.cs` | Calls helper inline (needs DI refactor) |
| `Services/Ai/DataverseHttpServiceBase.cs` | Calls helper inline (needs DI refactor) |
| `config/environments.json` | dev → new env (committed `2066b98e`) |
| `.github/workflows/deploy-office-addins.yml` | new URL (committed) |
| `.github/workflows/deploy-slot-swap.yml` | new env (committed) |
| 7 root-level dev utility scripts | updated to new hostname (committed) |
| `docs/guides/auth-deployment-setup.md` | UAMI vs SAM doc fix (committed) |

### Decisions Made

- **2026-05-20**: Pipeline scaffolding generated. Code-state deltas captured in `CLAUDE.md`.
- **2026-05-24** (task 001 — completed): Owner ACK'd all 9 §3 Resolved Decisions as-is.
- **2026-05-24** (Phase 0 complete): NFR-06 rollback drill verified at 2m 23s.
- **2026-05-24** (Phase 1 complete): 6 critical findings surfaced in INVENTORY.md.
- **2026-05-24** (task 019): Linux dev migration succeeded. UAMI cross-RG attachment proved correct.
- **2026-05-24** (cutover): Live traffic flipped to new dev via Dataverse env var. Spot-check found PlaybookService 500.
- **2026-05-24** (Phase 2): CANDIDATES.md gate signed; 3 SAFE, 1 MEDIUM, 0 HIGH, 15 REJECT.
- **2026-05-24** (NEW — task 023): Architectural credential injection chosen over helper-band-aid per explicit user direction "we don't want to just revert back we need to actually fix the issue."

---

## Next Action

**RESUME POINT for task 023**:

If context limit reached or new session starts, do the following:

1. Read this file (current-task.md) first
2. Read `projects/sdap-bff-api-remediation-fix/tasks/023-multi-identity-credential-refactor.poml` (to be created)
3. `git status` — see what's uncommitted
4. Continue file-by-file refactor per "Files to refactor" list above
5. Build verifies; deploy verifies functionally

---

## Blockers

**Status**: User waiting on retest of document upload after auth refactor lands. Once retest passes, decommission old dev + commit + move to Phase 3.

---

## Session Notes

### Current Session

- Project execution started 2026-05-24 from Phase 0 readiness state
- Single-session journey: Phase 0 → Phase 1 → task 019 (Linux migration) → cutover → Phase 2 → task 023 (auth fix)
- Context at ~35% as of this checkpoint

### Key Learnings

- **UAMI strategy worked perfectly** — cross-RG attachment of `mi-bff-api-dev` to new Linux App Service meant ZERO re-registration in Dataverse/Graph/Exchange. All cross-platform auth wiring lives on the UAMI principal.
- **20+ services have broken DefaultAzureCredential pattern** — surfaced by spot-check; was latent because old dev's identity config happened to resolve. This is an auth-r2 documentation + implementation gap.
- **Helper pattern (band-aid) rejected** — user wants architectural fix; DI-singleton TokenCredential injection across all affected services.
- **Spaarke.Core can't host the helper** — `Keep Libraries Focused` principle; helper stays in BFF; cross-assembly injection via DI handles `Spaarke.Dataverse`.
- **Linux App Service first-boot is ~13 minutes** — one-time container pull + .NET assembly JIT. Subsequent deploys normal.
- **Silent file-lock failure caught by hardened script** — task 009 rollback drill encountered real-world FAILURE-MODES G-2; auto-recover worked.

### Handoff Notes

If new session: the project is past Phase 2 gate. Currently mid-execution of a discovered auth bug (task 023). Once that's resolved + deployed + retested, the project naturally returns to its planned trajectory: decommission old dev, run Phase 3 baseline (tasks 030-038 — Group E parallel-safe + task 033's 48h calendar gate), then Phase 4.

---

## Quick Reference

### Project Context

- **Project**: sdap-bff-api-remediation-fix
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Master inventory**: [`inventory/INVENTORY.md`](./inventory/INVENTORY.md)
- **Phase 2 candidates**: [`CANDIDATES.md`](./CANDIDATES.md)
- **Migration record**: [`baseline/linux-dev-migration.md`](./baseline/linux-dev-migration.md)
- **Rollback drill record**: [`baseline/rollback-drill.md`](./baseline/rollback-drill.md)

### Applicable ADRs

- See `CLAUDE.md` → Resources → Applicable ADRs table (9 ADRs)

### Critical Auth Pattern (post-task-023)

```csharp
// Program.cs (already done)
builder.Services.AddSingleton<TokenCredential>(sp =>
    ManagedIdentityCredentialFactory.Create(sp.GetRequiredService<IConfiguration>()));

// Every service that needs Dataverse/Cosmos/OpenAI/AI Foundry auth
public XService(TokenCredential credential, IConfiguration configuration, ...)
{
    _credential = credential;  // injected
}
```

NEVER: `_credential = new DefaultAzureCredential();` — that pattern is now banned.

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **Mini-Plan for task 023**: read the file list + pattern; proceed file-by-file
3. **If task 023 is done but not committed**: build + deploy + verify
4. **If task 023 is committed**: move to old dev decommission, then Phase 3

**Commands**:
- `git log --oneline -10` — see recent commits
- `git status --short` — see uncommitted changes
- `git diff --stat HEAD` — see what's modified vs HEAD

---

*This file is the primary source of truth for active work state. Keep it updated.*
