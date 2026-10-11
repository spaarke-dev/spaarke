# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-11 (Wave 5 complete; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None active. Every remaining task is blocked on owner decisions (below) |
| **Step** | Waiting on the owner |
| **Status** | Pushed. Verified on `b1556b42a` (036 merge): builds 0 warnings, ArchTests 940/940, full unit suite 19,732 passed / 54 skipped / 0 failed |
| **Next Action** | When the owner answers an item below, start the task it unblocks through task-execute (032 → 054 → 055; 020 import → 035 → 037/052; 003 → 056/060) |

### Critical Context
- **Parallel pattern:**
  - BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (shared repository; `git reset --hard <local R3 sha>` first), commit there, and the main session merges `--no-ff`.
  - SPA tasks run in this worktree, uncommitted; the main session commits by explicit path.
  - Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:**
  - Auth tasks get two fable adversarial passes; other FULL tasks get one.
  - Fix rounds use sonnet.
  - A scoped re-check runs when a fix changes the approach.
  - At most 4 agents at once.
- **Root `node_modules`** was installed for the pre-commit prettier. `npm install` rewrites `.husky/_/*` line endings; restore them with `git checkout -- .husky/_/`.
- **POML vs spec:** an owner decision recorded in the spec or the project CLAUDE.md wins over a POML. Surface any conflict to the owner before acting (036 lesson, 2026-10-11).

---

## Task state

| Task | State |
|---|---|
| 001, 010, 011, 012, 030, 033, 034, 036, 041, 050, 051 | ✅ done |
| 031 Document routes | Matter, work-assignment and service-request routes merged. Invoices and the SR container wait on the owner |
| 040 Workforce client | Code done. Waits on the owner's "Spaarke Workspace" registration |
| 042 Out-of-plane pages | Upload and no-mock done and reviewed. Waits on the owner's playbook page + SemanticSearch decision |
| 043 Prod CI | Code done. Waits on the owner's GitHub environments |
| 032, 035, 037, 039, 052–056, 060 | Blocked (owner decisions or 020 import) |

## Open Owner Questions / Blockers

1. **020:** approve the dev import, and say whether `sprk_intakedata` is field-secured. This blocks 035 → 037/052, and 038.
2. **032:** delete-own, (a) strict or (b) widened (recommended). This blocks 032 → 054 → 055.
3. **003:** option B plus its sub-questions. This blocks 056/060.
4. **031:** invoice documents (A inherit, recommended / B grantable / C defer); SR container (A secure regarding root, recommended / B keep BU).
   - **034 review:** a post on an invoice also stamps `sprk_regardingproject`, so partners on that project can read it. Decide with the invoice question.
5. **033:** privileged messages to external callers: show / mark / hide.
   - **034 review:** contact author names are shown to every external reader of the record (the versions route hides them). Confirm or restrict.
6. **042:** playbook page + SemanticSearch: (a) hide, recommended / (b) BFF task.
7. **036:** approve the email copy (`notes/task-036-notes.md`).
   - The sender is the constant `demo@demo.spaarke.com` until the per-customer mailbox (#1562) exists; decide before go-live.
   - The link uses `Customer:Id` as the customer key.
   - Follow-up: contact-issuer grants (`ContactGrantEndpoints`) still send no email.
8. **040:** register "Spaarke Workspace" and its pre-authorizations; add the `/auth-callback.html` redirect URIs on CIAM client `bd57e54e…`.
9. **041:** expose `user_impersonation` on CIAM BFF app `4a4d5126…` and consent it for `bd57e54e…` (070 precondition).
10. **043:** create GitHub environments `external-spa-dev` (9 vars) and `external-spa-prod` (9 vars + `TEAMS_APP_ID` + secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD`).
11. **002:** the column list. **060:** the host, and R3 or R4. **Portfolio:** R3 is not on the board.

## Dependencies tracked elsewhere

- **ISS-001** (#1623): `AuthorizationModule` once-per-process flag.
- **ISS-002** (#1633): intermittent chat streaming contract test (two occurrences).
- **ISS-003** (#1636): the shared `sanitizeEmailHtml` keeps style positioning and remote images; R3 contains it in the SPA only.
- **Copilot agent** still uses `access_as_user`; do not remove it from `1e40baad`.
- **Before the BFF PR:** `/conflict-check` (`AuthorizationModule.cs`, `Api/ExternalAccess/**` shared with UAC-r2); actionlint on both deploy workflows.
- **038 deploy order:** the 020 columns must be live in dev before the R3 BFF is deployed (creator stamp, message read and post).
