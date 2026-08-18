# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-18 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Full deploy runbook: `notes/deploy-verify.md`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — **16 of 17 tasks done**; only 080 (deploy) partially remaining |
| **Task** | 🟡 **080** — Deploy + verify (owner-gated). PREP + ALL GATES DONE; the 3 production deploys + UAT remain. |
| **Status** | in-progress (paused before production deploys — heavy context use; clean handoff preferred over mid-deploy risk) |
| **Next Action** | Execute `notes/deploy-verify.md` §Remaining, in order: **(1)** seed advisory `list-tasks` Action from `infra/dataverse/actions/list-tasks.action.json` via `pwsh scripts/Deploy-AnalysisAction.ps1` (still ack-only: allow-list/modeltier/prompt empty on row `57651aad-8e85-f111-8075-7c1e5268570d`) → **(2)** `/bff-deploy` (44.96 MB net10 publish; column already exists) → **(3)** `/code-page-deploy` (ships 021a+021b+023+040) → **(4)** 7-DoD owner UAT. |

### Files Modified This Session (ALL COMMITTED — clean tree)
- `SprkChat.tsx` — 040 fix: collapse pin-to-top trailing spacer once content fills viewport (Refresh-row clip / dead-whitespace). Committed `0702aad7e`.
- `PreferenceNotPermissionInvariantTests.cs` — rebase-integration fix (AgentToolFilterContext 6→7 params for task-011 AdvisoryToolAllowList). In master via PR #778.
- `ChatEndpoints.cs` + `ChatEndpointsTypedFollowupsTests.cs` — D-024-01 fix (BuildTypedFollowups extraction + guard). In master (PR #778).
- `notes/deploy-verify.md` (new) — 080 gates + runbook. Committed `f22a7db83`.
- `040`/`024` POMLs + TASK-INDEX — statuses ✅.
- **Dataverse (not git)**: created column `sprk_groundedtoolallowlist` on `sprk_analysisaction` (+ orphan `sprk_grounded_tool_allow_list` to delete in maker portal).

### Critical Context
**ALL R4 code is MERGED to master (PR #778).** The 040 fix (`0702aad7e`) + the deploy-verify doc (`f22a7db83`) are on THIS worktree branch, ahead of master — merge/push them (or deploy from this branch, which has them) so the code-page deploy carries the 040 fix. 080 gates: **column created ✅, publish 44.96 MB Δ0 ≤60 MB ✅, no new HIGH CVE ✅.** The column MUST exist before the BFF deploy (done) or `AnalysisActionService.$select` 500s.

---

## Full State

### Done this session
- **PR #778 MERGED** to master (all E1–E3 + E2 client + fixes). Rebased 119 commits; fixed 1 real invariant test; merged past the non-blocking `Client Quality` red (repo-wide infra issue, tracked as issue #780).
- **040 COMPLETE** — D9 owner-confirmed resolved (bounded transcript); fixed the residual Refresh-row clip (spacer-collapse); 374/374 SprkChat tests.
- **080 PREP COMPLETE** — schema column, publish-size gate, CVE gate all green.

### Remaining (080 deploys — owner-gated)
See `notes/deploy-verify.md` for the exact runbook. Deploys are last-write-wins on the shared BFF + code page — `/conflict-check` + coordinate with compose-r5/r6 + assistant-r3 first. Never deploy BFF from a net8 tree (SDK is 10.0.101 ✅). Retry code-page publish on transient `0x80071151`. Verify 022 layout GUIDs match this env's `sprk_workspacelayout` rows.

### Standing constraints
- Commit footer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- ADR-042 memory hard-governance DEFERRED to #616 (trustLevel inert).
- After 080: **090** (wrap-up + `/test-diet`) is the last task.
