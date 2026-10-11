# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-10 (Wave 2 in progress; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave 2: 011, 012, 030, 040. Wave 1 is done, except 003 and 020, which wait on the owner |
| **Step** | Review fix rounds and merges |
| **Status** | Agents are running. Branch HEAD is `209ee27e0` (A7). 040 is uncommitted in this worktree |
| **Next Action** | As each agent reports: (1) merge the 011 worktree branch `worktree-agent-af42f82984fd95554` and the 030 worktree branch `worktree-agent-a19a81dfa2b1dbd0f` into this branch, then build, run targeted tests and ArchTests, mark both done and push; (2) commit 040 by explicit path once its fix round is green; (3) run two adversarial passes on 012, fix, merge, then add the app-only-token hardening |

### Critical Context
- **Parallel pattern:**
  - BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\`, commit there, and the main session merges.
  - SPA tasks run in this worktree, uncommitted; the main session commits by explicit path.
  - Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** two adversarial passes (fable) for auth tasks; fix rounds use sonnet; at most 4 agents at once.
- **ADR-028 A7** was applied and owner-approved on 2026-10-10 (`209ee27e0`). Quote: `notes/task-001-notes.md`.

---

## Wave 2 lanes

| Task | State |
|---|---|
| 011 Modules by user type and partner switch | Commit `3499109c4` in its worktree. Both passes found no F-class defects. Fix round running: tests that "" and "yes" fail startup, and stale comments in 8 files. Then merge |
| 012 Member-test guest branch | Implementer (opus) running in an isolated worktree. Next: two adversarial passes |
| 030 Creator stamping | Commit `633cce424` in its worktree. Both passes found no F-class defects. Fix round running: an attributable-principal check before the SPE write, the to-do stamp decided before any network call, principal pins in the integration fixtures, a seam-level strip test, live-test readback, the census entry, and notes |
| 040 Workforce client switch | Uncommitted in this worktree; 128/128 vitest. Review fix round running: no redirectUri on silent calls, a split workforce silent call, account filter by tenant, NAA gating, no fallback on `user_cancelled`, a Sign-in button, manifest 1.2.0, stale text. **Live criteria wait on the owner's client registration** |
| 001, 010, 050 | ✅ done |

## Next Actions

1. Merge 011 and 030 when their fix rounds report: `git merge --no-ff`, then `dotnet build src/server/api/Sprk.Bff.Api/`, the targeted External filter, ArchTests, and the full suite once. Set TASK-INDEX and POML to done, then push.
2. Commit 040. Keep it `[wip]`: its live criteria wait on the client registration.
3. Run 012's reviews and fixes, then merge.
4. App-only-token hardening (011 review K1) after 012 merges: deny when `CallerIdentity.FromPrincipal(user).Kind != UserDelegated` in `WorkforcePrincipalStrategy` / `CallerPrincipalAuthorizationFilter`.
5. Wave 3: 031, 033, 041, 043. Run `/conflict-check` before the BFF PR (`AuthorizationModule.cs` is shared with UAC-r2).

## Open Owner Questions / Blockers

- **020:**
  - approval of the dev import (steps and rollback in `notes/task-020-notes.md`);
  - whether `sprk_intakedata` is field-secured. **This blocks 038 deploy order.**
- **003:**
  - option B (custom join flow)?
  - consent prerequisite;
  - which group contacts join;
  - who registers `CustomerTenantIds`;
  - hosting.
- **040:**
  - create the "Spaarke Workspace" single-tenant client registration and its pre-authorizations (`notes/task-040-notes.md` §5);
  - add `/auth-callback.html` redirect URIs on the CIAM client `bd57e54e…`.
- **032:** delete-own option (a) strict or (b) widened, which is recommended.
- **002:** the grid column list.
- **060:** the host, and R3 or R4.
- **Portfolio:** R3 is not on the GitHub board.

## Dependencies tracked elsewhere

- **Provisioning:**
  - T240c directory;
  - T240d S1 (iv) token shapes. When they arrive, swap the `AssumedS1Shape` tests;
  - H3 pre-authorization;
  - #1562 mailbox.
- **ISS-001** (#1623): the once-per-process flag in `AuthorizationModule`, pre-existing.
- **The Copilot agent** still uses `access_as_user` (`scripts/Deploy-CopilotAgent.ps1:69`). Do not remove that scope from the interim client `1e40baad`.
