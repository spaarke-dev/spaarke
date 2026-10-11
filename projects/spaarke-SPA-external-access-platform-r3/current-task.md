# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-11 (Wave 4 in progress; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave 4: 034 and 042 are running. 032 waits on the owner's delete-own choice. The 043 review fix round is running |
| **Step** | Implementation, then reviews and fix rounds |
| **Status** | Pushed. Verified on `0f0409899`: builds have 0 warnings, ArchTests 940/940, full unit suite 19,626 passed / 0 failed |
| **Next Action** | As agents report: (1) **034** (opus, isolated worktree based on local `0f0409899`): two fable adversarial passes, a sonnet fix round, then merge; (2) **042** (opus, in this worktree, SPA only, uncommitted): commit by explicit path, then two passes and a fix round; (3) **043 fix round** (sonnet, workflows, `ci-cd-workflow.md`, `production-package.test.ts`, notes): commit by explicit path; 043 stays `[wip]` until the owner creates the GitHub environments. After each BFF merge: full unit suite, then push |

### Critical Context
- **Parallel pattern:**
  - BFF tasks run in isolated worktrees (shared repository, so they can branch from the local R3 ref), commit there, and the main session merges `--no-ff`.
  - SPA and workflow tasks run in this worktree, uncommitted; the main session commits by explicit path.
  - Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:**
  - Auth tasks get two fable adversarial passes.
  - STANDARD tasks get one pass.
  - Fix rounds use sonnet.
  - A scoped sonnet re-check runs when a fix changes the approach.
  - At most 4 agents at once.
- **Root `node_modules`** was installed in this worktree for the pre-commit prettier. `npm install` rewrites `.husky/_/*` line endings; restore them with `git checkout -- .husky/_/`.

---

## Task state

| Task | State |
|---|---|
| 001, 010, 011, 012, 030, 033, 041, 050 | ✅ done |
| 031 Document routes | Matter, work-assignment and service-request routes merged. **Invoices wait on the owner** (A inherit from parent / B grantable / C defer). **SR container: owner decision** (A: secure regarding root wins via a ChildAncestorLinks row / B: keep BU-scoped) |
| 040 Workforce client | Code done and reviewed. Waits on the owner's "Spaarke Workspace" registration (notes §5) |
| 043 Prod CI | Code committed (`0c0e1c4f1`); review fix round running. Waits on the owner's GitHub environments and `TEAMS_APP_ID` (notes §3) |
| 020 Schema | Waits on the owner's dev import |
| 003 Self-registration spike | Waits on the owner's choice |
| 034 Message send, 042 Out-of-plane pages | Running |
| 032 Delete own | Blocked on the owner's choice: (a) strict / (b) widened (recommended) |

## Open Owner Questions / Blockers

1. **020:** approval of the dev import, and whether `sprk_intakedata` is field-secured. This blocks 038.
2. **003:** option B, plus four sub-questions.
3. **040:** create the "Spaarke Workspace" client and its pre-authorizations. On the CIAM client `bd57e54e…`, add the `/auth-callback.html` redirect URIs.
4. **041:** expose `user_impersonation` on the CIAM BFF app `4a4d5126…` and consent it for `bd57e54e…`. This is a 070 precondition.
5. **043:** create the GitHub environments `external-spa-dev` (nine variables) and `external-spa-prod` (nine variables plus `TEAMS_APP_ID`, secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD`). `external-spa-dev` is a 070 precondition.
6. **031:** invoice documents (A/B/C) and the service-request container (A/B).
7. **033:** privileged messages to external callers: show / mark / hide.
8. **032:** delete-own, (a) or (b).
9. **002:** the column list. **060:** the host, and R3 or R4. **Portfolio:** R3 is not on the board.

## Dependencies tracked elsewhere

- **ISS-001** (#1623): the `AuthorizationModule` once-per-process flag. **ISS-002** (#1633): an intermittent chat streaming contract test.
- **Copilot agent:** still uses `access_as_user`. Do not remove it from `1e40baad`.
- **Provisioning:** T240c directory (remembered keys need a per-user scope); T240d token shapes; H3; #1562.
- **Before the BFF PR:** run `/conflict-check` (`AuthorizationModule.cs` is shared with UAC-r2). Run actionlint on both deploy workflows.
