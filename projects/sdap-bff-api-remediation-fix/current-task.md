# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-05-25 (task 024.A SpeDocumentViewer done + bodyFormat 'Text'→'PlainText' fix across LegalWorkspace+SemanticSearchControl + UAMI Mail.Send grant + Exchange policy)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | task 024 (URL source-of-truth refactor) — IN PROGRESS. Sub-step A (SpeDocumentViewer) ✅ DONE this session. Sub-steps B/C/D pending. |
| **Step** | 024.A complete (2026-05-25). User verified Document + SPE preview works on dev with v1.0.27. Multiple other BFF calls also smoke-tested as working. Solution zip artifact: `src/client/pcf/SpeDocumentViewer/solution/bin/SpaarkeSpeDocumentViewer_v1.0.27.zip` |
| **Status** | **Phase 4 still BLOCKED on two gates** (48h App Insights window 2026-05-27 + G4 facade adoption agreement). Task 024 remains in flight (sub-steps B/C/D), but does NOT block Phase 4 — it's parallel-track cleanup work surfaced during Phase 3 baseline. |
| **Next Action** | (1) Phase 3 gate watch: 48h App Insights window closes 2026-05-27 UTC — capture metrics per `baseline/app-insights-baseline-start.md`, then unblock Phase 4. (2) Task 024.B: audit + refactor remaining PCFs (EmailProcessingMonitor, UniversalQuickCreate, UniversalDatasetGrid, SemanticSearchControl, RelatedDocumentCount, DocumentRelationshipViewer, AssociationResolver, ScopeConfigEditor) per the same pattern used for SpeDocumentViewer. (3) Task 024.C: audit Code Pages. (4) Task 024.D: update build-time `.env.development` files. (5) Task 024 final acceptance grep + commit. |

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
| `7fb1776f` | task 023 COMPLETE — auth-r2 architectural fix (19 services refactored) |
| `ca38909a` | checkpoint — task 023 done; Phase 3 ready |
| `0a8dae14` | **Phase 3 COMPLETE — BASELINE.md committed; old Windows dev decommissioned; 8 of 9 baseline tasks done** |
| **(pending)** | **task 024.A — SpeDocumentViewer v1.0.27 deployed + user-verified (URL source-of-truth refactor; bffApiUrl property removed; env-var-driven)** |

### Critical Context

**Where the project is**: Phase 3 closed except for the 48h calendar gate. BASELINE.md ([`BASELINE.md`](./BASELINE.md)) is the authoritative reference for every Phase 4 regression check. All Phase 3 metric-capture tasks (030, 031, 032, 034, 035, 036, 037, 038) completed in a single parallel-dispatch wave this session. Task 033 started its 48h window — completes 2026-05-27 UTC.

**Key Phase 3 findings (newly surfaced)**:
1. **Test project builds but DOESN'T compile** (task 030 finding) — 69 unique compile errors across 17 test files. ~50% from task 023's TokenCredential param addition, ~50% pre-existing from other in-flight projects. Test suite is unusable as a regression signal. Phase 4 acceptance for Outcome E falls back to task 053's FR-E2 grep acceptance + manual smoke + bff-deploy hash-verify. Test project repair is OUT OF SCOPE for this project — flagged for separate sub-project.
2. **323 BFF endpoints enumerated** (task 032). 1 candidate 404 regression: `POST /api/office/quickcreate/{entityType}` returns Kestrel 404 with empty body. 2 routes return 500 (`/api/finance/matters/{matterId:guid}/recalculate` + `/api/finance/projects/{projectId:guid}/recalculate`) — investigate at Phase 4 task 047 (Finance facade migration). 3 routes anonymously return 200 (`/api/config/client`, `/api/office/health`, `/api/admin/builder-scopes/status`) — code-review flag.
3. **265 DI registrations baseline** (task 038) — ADR-010 gap measured. Phase 4 ceiling for Outcome E (task 054 gate): ≤273.
4. **212.5 MB uncompressed / 287 files / 72.9 MB zip** publish baseline (task 035). Phase 4 Outcome A targets: ≤150 MB / ≤240 files / ≤60 MB zip.

**Live state**:
- New Linux dev `spaarke-bff-dev` is the sole live traffic target. Old Windows `spe-api-dev-67e2xz` + `spe-plan-dev-67e2xz` DELETED this session.
- BASELINE.md committed as Phase 3 gate artifact.
- 48h App Insights calendar window started 2026-05-25 UTC; earliest closure 2026-05-27.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | Phase 4 BLOCKED (waiting on 48h gate + G4 agreement) |
| **Status** | none — awaiting calendar gate + operator G4 agreement |
| **Started** | — |

---

## Next Actions (in execution order)

### Action 1 (operator, calendar-bound): wait for 48h App Insights window to close

Earliest: **2026-05-27 UTC**. At that point, follow the runbook in [`baseline/app-insights-baseline-start.md`](baseline/app-insights-baseline-start.md) to capture the 48h metrics and produce `baseline/app-insights-48h.json`. Then commit that file and update TASK-INDEX task 033 from 🔄 → ✅.

### Action 2 (operator, asynchronous): secure G4 facade adoption agreement

Per Phase 0 task 005 + UQ-04 residual: contact the `ai-spaarke-insights-engine-r1` project owner to confirm any Engine PR merged after this project's Phase 4 task 046 (facade creation) uses the `Services/Ai/PublicContracts/` facade rather than direct `IOpenAiClient` / `IPlaybookService` injection. Belt-and-suspenders to FR-C6 CI gate that lands in Phase 6 task 082. Without this agreement, Phase 4 task 046 carries the risk of immediate post-merge violation by a sibling project. Record the agreement (PR comment, email, or written note) and link it from current-task.md before starting task 046.

### Action 3 (operator, optional, low risk): residual hygiene items

- **Weekly monitoring of worktree branches** for new BFF-touching work during Phase 3–4 — per Phase 0 task 004 followup commitment. Run `git log master..<branch> -- src/server/api/Sprk.Bff.Api/` for active branches weekly.
- **Graph email subscriptions** — currently still pointing at old (now-deleted) dev URL `spe-api-dev-67e2xz.azurewebsites.net`. Three options: (a) wait ~3 days for auto-expiry, (b) operator manually PATCHes each subscription to the new URL, (c) accept temporary email-webhook downtime. Only affects incoming email webhooks — general PCF/Code Page traffic was unaffected by the migration.

### Action 4 (Claude, after Action 1 + Action 2 both complete): start Phase 4

Resume with `/task-execute projects/sdap-bff-api-remediation-fix/tasks/040-publish-linux-x64.poml`.

---

## Progress (full project)

### Completed Phases

- ✅ Phase 0 — Pre-flight resolution (commit `e5350ef9`)
- ✅ Phase 1 — Inventory + 6 critical findings (commit `385957a3`)
- ✅ Task 019 — Linux dev migration (commits `037c7e2c` + `2066b98e`)
- ✅ Phase 2 — Categorization + CANDIDATES.md gate (commit `5d476d34`)
- ✅ Task 023 — DI-singleton TokenCredential architectural refactor (commits `6bfe193a` + `7fb1776f`)
- ✅ Phase 3 — Baseline metric capture + BASELINE.md (this session; commit pending)

### Pending

- 🔄 Phase 3 task 033 — App Insights 48h calendar gate (in flight; closes 2026-05-27 UTC)
- ⬜ Operator G4 facade adoption agreement with Insights Engine owner
- ⬜ Phase 4 — Apply changes (Outcome A SAFE + B MEDIUM + E parallel track)
- ⬜ Phase 5 — Promote demo + prod (62/63 conditional on UQ-02 already RESOLVED YES)
- ⬜ Phase 6 — Prevention/codification
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
- **2026-05-25** (Phase 3 close): Old Windows dev decommissioned (`az webapp delete` + `az appservice plan delete` both succeeded). BASELINE.md committed. 8 of 9 Phase 3 tasks complete; only 033 (48h calendar gate) remains in flight. Test project found to have 69 compile errors (mixed task 023 fallout + pre-existing breakage from other projects); test project repair flagged out-of-scope for separate sub-project. 1 candidate 404 regression in `/api/office/quickcreate/{entityType}`; 2 server-errors in finance recalculate endpoints — both flagged for Phase 4 investigation but not blocking. ADR-010 gap measured at 265 registrations (target ≤15).
- **2026-05-25** (task 024.A — URL source-of-truth refactor for SpeDocumentViewer): User's Document detail page broke immediately after decommission — `useDocumentPreview` fetched `spe-api-dev-67e2xz/api/documents/.../view-url` (ERR_NAME_NOT_RESOLVED). Root cause: per-instance `bffApiUrl` PCF property in form customization explicitly set to old URL. Per user direction ("Architectural fix + Full sweep + Remove fallbacks"), refactored SpeDocumentViewerHost.tsx to async-resolve URL from Dataverse env var `sprk_BffApiBaseUrl` via `getApiBaseUrl(webApi)`. Removed `bffApiUrl` property from manifest. Updated `environmentVariables.ts` to throw clear error if env var missing (no URL fallback). Bumped version 1.0.26 → 1.0.27 in 7 places (manifest source + UI footer + packed manifest + source manifest mirror + Solution.xml + solution.xml + pack.ps1). Built (426KB bundle, 0 old-URL refs verified). Packed `SpaarkeSpeDocumentViewer_v1.0.27.zip`. Imported via `pac solution import --force-overwrite --publish-changes` to SPAARKE DEV 1. User republished Custom Page + verified preview + multiple other BFF calls working. Task 024 sub-steps B (other PCFs), C (Code Pages), D (`.env.development` files) remain pending.

- **2026-05-25** (bodyFormat 'Text'→'PlainText' fix — secondary blocker found during 024.A testing): User exercised Email Document flow → POST /api/communications/send returned 400. Root cause: BFF enum `BodyFormat` is `{PlainText, HTML}` but multiple client sites sent literal `'Text'` (an incorrect alias that never existed in the server enum). Fixed 7 source sites across 3 surfaces — all `'Text'` → `'PlainText'`:
  - Shared lib (`@spaarke/ui-components`): `EntityCreationService.ts:77` type contract, `SummarizeFilesDialog.tsx:488`, `workAssignmentService.ts:599`
  - LegalWorkspace solution: `FilePreviewDialog.tsx:328` (firing), `SummarizeFiles/SummarizeFilesDialog.tsx:466`, `CreateWorkAssignment/workAssignmentService.ts:577`
  - SemanticSearchControl PCF: `SemanticSearchControl.tsx:760`
  Rebuilt + deployed LegalWorkspace SPA (`sprk_corporateworkspace.html` web resource) and SemanticSearchControl PCF (v1.1.42 → v1.1.43, all 5 manifest/version locations bumped, packed via `Solution/pack.ps1`, imported via `pac solution import --force-overwrite --publish-changes` per `/pcf-deploy` skill). dist_temp/ stale build artifacts in @spaarke/ui-components cleaned up (6.5 MB / 1,152 files removed — verified not referenced anywhere). User confirmed email-send fix unblocked the 400.

- **2026-05-25** (UAMI Mail.Send Auth-r2 gap — third blocker after 400 cleared): Email-send returned 403 "Access is denied" from Graph after the 400 was fixed. Root cause: `Graph__ManagedIdentity__Enabled=true` means BFF's `GraphClientFactory.ForApp()` uses UAMI `mi-bff-api-dev` (`5967251e-171c-46fe-a6c2-ef843c90309d`) but UAMI had ZERO Graph app role assignments. BFF API app reg (`SDAP-BFF-SPE-API`, `1e40baad-...`) has Mail.Send admin-consented from earlier setup, but cert-mode path isn't active under MI mode. (SharePoint Embedded operations still worked because SPE uses container-type registration, not Graph app roles.) Resolution per `auth-deployment-setup.md` §5+§7:
  1. **Mail.Send (Application) granted to UAMI** via `az rest POST /v1.0/servicePrincipals/{uami-objId}/appRoleAssignments` (role id `b633e1c5-b582-4048-a93e-9f11b44c7e96`, Graph SP objId `ba630d35-...`). Grant returned 200 + verified via re-query. Done by Claude.
  2. **Exchange ApplicationAccessPolicy** created by operator via EXO PowerShell `New-ApplicationAccessPolicy -AppId 5967251e-... -PolicyScopeGroupId spaarke-central-email@spaarke.com -AccessRight RestrictAccess`. ScopeIdentity = "Spaarke Email Access20260310215505". `Test-ApplicationAccessPolicy` returned `Granted` for both `mailbox-central@spaarke.com` and `demo@demo.spaarke.com`.
  3. Awaiting Exchange propagation (up to 30 min) before live Graph calls honor the policy. User retest pending.
  **Identified Auth-r2 gap (broader scope)**: UAMI `mi-bff-api-dev` is missing all Graph application permissions that the BFF API app reg has (Mail.Send was the first to surface). A complete Auth-r2 follow-on should grant the full role parity (Mail.Read for inbound polling, MailboxSettings.Read, etc.) to UAMI + create Exchange policies as needed. Out of scope for task 024; should be tracked as a separate Auth-r2 cleanup project.

---

## Recovery Instructions (post-compact)

1. Read this file
2. Confirm `git log --oneline -3` shows the Phase 3 close commit as HEAD (or `ca38909a` if Phase 3 commit pending)
3. Confirm `spaarke-bff-dev` healthy: `curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz` → 200
4. Check current UTC date — if past 2026-05-27, the 48h calendar gate has closed → follow Action 1 to capture App Insights metrics, then proceed with G4 (Action 2), then Phase 4 (Action 4)
5. Reference [`BASELINE.md`](./BASELINE.md) for every Phase 4 regression check
6. Reference [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) for status of every task
7. Reference [`CANDIDATES.md`](./CANDIDATES.md) for Phase 4 candidate ordering (Outcome A SAFE first; Outcome E parallel track independent of A)

---

## Quick Reference

- **Project**: sdap-bff-api-remediation-fix
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **INVENTORY.md**: [`inventory/INVENTORY.md`](./inventory/INVENTORY.md)
- **CANDIDATES.md**: [`CANDIDATES.md`](./CANDIDATES.md)
- **BASELINE.md** (Phase 3 gate): [`BASELINE.md`](./BASELINE.md)
- **Linux migration record**: [`baseline/linux-dev-migration.md`](./baseline/linux-dev-migration.md)
- **Rollback drill record**: [`baseline/rollback-drill.md`](./baseline/rollback-drill.md)
- **App Insights 48h runbook**: [`baseline/app-insights-baseline-start.md`](./baseline/app-insights-baseline-start.md)

### Azure resources

| Env | App Service | RG | Subscription | Status |
|---|---|---|---|---|
| dev (Linux) | `spaarke-bff-dev` | `rg-spaarke-dev` | `484bc857-3802-427f-9ea5-ca47b43db0f0` | LIVE traffic; only dev target |
| ~~dev (Old, Windows)~~ | ~~`spe-api-dev-67e2xz`~~ | ~~`spe-infrastructure-westus2`~~ | (same) | **DELETED 2026-05-25** |
| demo (Linux) | `spaarke-bff-demo` | `rg-spaarke-demo` | `2ff9ee48-6f1d-4664-865c-f11868dd1b50` | unused; Phase 5 target |
| prod (Linux) | `spaarke-bff-prod` | `rg-spaarke-platform-prod` | `484bc857-3802-427f-9ea5-ca47b43db0f0` | unused; Phase 5 task 062 first real deploy |
| UAMI (shared) | `mi-bff-api-dev` | `spe-infrastructure-westus2` | (same) | attached cross-RG to new dev; LEAVE IN PLACE |
