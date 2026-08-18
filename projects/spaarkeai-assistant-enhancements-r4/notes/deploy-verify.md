# 080 Deploy + Verify — spaarkeai-assistant-enhancements-r4

> **Status**: ✅ DEPLOYS EXECUTED (2026-08-18). BFF + advisory Action seed + SpaarkeAi code page all deployed to dev (spaarkedev1). Owner UAT (7 DoDs) remains.
> All R4 code is **merged to master** (PR #778) + this worktree branch is at current master. 040 (D9 + Refresh-clip fix) committed (`0702aad7e`).

---

## ✅ DEPLOY EXECUTION LOG (2026-08-18)

| Step | Result |
|---|---|
| **0. Worktree ← master** | Merged origin/master (13 commits) clean; pulled in `Verify-ComposeIdentityKey.ps1` + master BFF `ComposeEndpoints.cs`. Release build clean (0 errors). |
| **1. BFF deploy** | `Deploy-BffApi.ps1` → **44.96 MB**, all 4 critical files SHA-256 verified, `/healthz` 200. Routes registered (`/api/ai/chat/sessions`, `/api/ai/chat/playbooks` → **401** = auth-protected, not 404). |
| **2. Advisory `list-tasks` Action seed** | The R5 `Deploy-AnalysisAction.ps1` is **incompatible** with `list-tasks.action.json` (single top-level object, not `actions[]`; numeric `actionType`, not `actionTypeName`; doesn't map modelTier/temperature/groundedToolAllowList). Seeded via controlled REST PATCH **from the authored JSON** onto row `57651aad-8e85-f111-8075-7c1e5268570d`: `sprk_modeltier=100000002 (Reasoning)`, `sprk_temperature=0.3`, `sprk_groundedtoolallowlist=["spaarke.grid_overview","spaarke.daily_briefing_overview"]`, advisory `sprk_systemprompt` (3839 ch), `sprk_outputschemajson` (454 ch), `sprk_description`. **Verified** via read_query. |
| **2b. Grounded tools** | `spaarke.grid_overview` / `spaarke.daily_briefing_overview` are **code-registered handler constants** (GridOverviewHandler.ToolId / DailyBriefingOverviewHandler.ToolId, shipped R3) — NOT Dataverse `sprk_analysistool` rows. Runbook's "verify 2 tool rows" was a wrong assumption; the allow-list matches the code catalog. |
| **2c. Binding** | `list-tasks` `sprk_playbookconsumer` (5b1870b9-...) disposition = **Surface Launch (100000007)** ✅. |
| **3. SpaarkeAi code page** | Vite config resolves `@spaarke/*` → shared-lib **`src/`** (not dist/), so no separate shared-lib rebuild needed. `npm install --legacy-peer-deps` → clear cache → `npm run build`. Bundle verified to contain R4 markers: `workspace_tabs_snapshot` (023), `minHeight="0px"` (040), `onSuggestionCapabilitySelect` (021b). `Deploy-SpaarkeAi.ps1` → `sprk_spaarkeai` updated + published (5674 KB). |
| **4. Compose-r7 guard** | `Verify-ComposeIdentityKey.ps1` → `sprk_graphitemid_uk = Active` (exit 0) ✅. |

**Follow-up filed (script gap):** `Deploy-AnalysisAction.ps1` cannot seed advisory Actions authored as single-object JSON with numeric `actionType` + the R4 fields. Next advisory-Action author will hit the same gap. See "Notes" below.

**Remaining:** owner UAT of the 7 spec Success Criteria (below).

---

---

## ✅ Completed (careful prerequisites + gates)

| Step | Result |
|---|---|
| **Dataverse column** | Created `sprk_groundedtoolallowlist` (multiline text, 4000) on `sprk_analysisaction` via MCP `update_table`. Logical name verified EXACT (`sprk_groundedtoolallowlist`) — the BFF `AnalysisActionService.$select` (lines 44/175) reads it; without it every action-resolution query 500s. **Additive/nullable → safe for other worktrees.** ⚠️ Orphan `sprk_grounded_tool_allow_list` (first attempt, spaces→underscores) left behind — unused/harmless; delete in the maker portal (MCP has no delete-column). |
| **BFF publish size (§10 gate)** | `dotnet publish -c Release` → **44.96 MB compressed, delta 0.00 MB** vs the ~44.96 MB net10 baseline (incl. PDBs). ≤60 MB ✅. R4's additions are pure code (no new packages) → zero size impact. |
| **CVE gate (NFR-02)** | `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages** ✅. |
| **SDK** | 10.0.101 (≥ global.json 10.0.100 pin) — net10 publish, never net8. |

## ⏭️ Remaining runbook (owner-gated deploys — run in order)

**1. Seed the advisory `list-tasks` Action** (currently still ack-only: `sprk_groundedtoolallowlist` + `sprk_modeltier` are EMPTY on row `57651aad-8e85-f111-8075-7c1e5268570d`). Apply ALL fields from `infra/dataverse/actions/list-tasks.action.json` via the deploy script (NOT hand-MCP — the systemPrompt + outputSchema are large):
```
pwsh scripts/Deploy-AnalysisAction.ps1  # point it at infra/dataverse/actions/list-tasks.action.json
```
Sets: `sprk_groundedtoolallowlist = ["spaarke.grid_overview","spaarke.daily_briefing_overview"]`, `sprk_modeltier = Reasoning (100000002)`, `sprk_temperature ≈ 0.2`, the ADVISORY GROUNDING RULES `sprk_systemprompt`, and the widened-maxLength `sprk_outputschemajson`.
   - VERIFY the two grounded tool rows exist: `sprk_analysistool` with `sprk_toolid` = `spaarke.grid_overview` + `spaarke.daily_briefing_overview` (shipped R3 — confirm present + OBO-identity wording per task 020).
   - VERIFY the `list-tasks` Binding row (`sprk_playbookconsumer`, consumerType `list-tasks`) has `surface_launch` disposition (already mirrored).

**2. Deploy the BFF** (owner-confirm): `/bff-deploy` — the 44.96 MB net10 publish. **The column (step above) MUST exist first** (done ✅). Never from a net8 tree.

**3. Deploy the SpaarkeAi code page** (owner-confirm): `/code-page-deploy` (or `Deploy-SpaarkeAi.ps1`) — ships **021a+021b (typed suggestions) + 023 (follow-on cards) + 040 (D9 + Refresh-clip fix) TOGETHER** with the BFF. Retry on transient `0x80071151`. **Per-env note**: the 022 Daily Briefing / Smart To Do `layoutId` GUIDs in `surfaceLaunchRegistry.ts` are spaarkedev1-specific — confirm they match this env's `sprk_workspacelayout` rows.

**4. Verify the 7 spec Success Criteria DoDs** (owner UAT): P1 grounded cited summary + recommendation + Tasks opens · advisory fidelity (no 2nd dispatch surface) · P2 no dead-end + no id prompt · Briefing/SmartToDo follow-on cards (open/closed gating) · P3 preference loop · P4/D9 viewport (bounded transcript — already confirmed; also verify the Refresh-row clip is gone after the 040 fix) · BFF hygiene (44.96 MB, no CVE).

## Notes
- Deploys are last-write-wins on the shared BFF + code page — `/conflict-check` + coordinate with active worktrees (compose-r5/r6, assistant-r3) before deploying.
- `git rev-list origin/master..HEAD` currently includes the 040 commit (`0702aad7e`) not yet merged to master — merge/push it (or include in the deploy build) so the deployed code page carries the 040 fix.
