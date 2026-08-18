# 080 Deploy + Verify — spaarkeai-assistant-enhancements-r4

> **Status**: PREP + GATES COMPLETE (2026-08-18). Remaining = seed advisory Action + 2 deploys + UAT (runbook below).
> All R4 code is **merged to master** (PR #778) + this worktree branch is at current master. 040 (D9 + Refresh-clip fix) committed (`0702aad7e`).

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
