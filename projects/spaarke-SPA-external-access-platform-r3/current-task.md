# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-10 (Wave 1 agent work complete; waiting on the owner)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave 1. 010 and 050 are done. 001, 003 and 020 wait on the owner |
| **Step** | Owner gates |
| **Status** | Waiting on the owner. The worktree is clean and pushed (HEAD `a5e1dfbb0`) |
| **Next Action** | **On A7 approval:** write `notes/drafts/adr-028-a7-draft.md` into `.claude/adr/ADR-028-spaarke-auth-architecture.md` (main session, task 001 steps 4–7), then start Wave 2 (011, 012, 040; 030 after the 020 import). **On import approval:** run the 020 live steps in `notes/task-020-notes.md` |

### Critical Context
- The adversarial review policy is applied per task. The results are in each `notes/task-0NN-notes.md`.
- Parallel agents share this worktree and are told not to commit or touch the state files. The main session commits each task by explicit path.
- ISS-001 (#1623) was filed: the pre-existing once-per-process flag in `AuthorizationModule`.

---

## Wave 1 status

| Task | State |
|---|---|
| 001 ADR-028 A7 | Draft at `notes/drafts/adr-028-a7-draft.md`. **Needs owner approval** of four parts: A2 client, A3 modules, A1 scope, the self-registration rule. Also needs the split agreed: A7 carries R3's items, and the auth project's amendment carries §12b |
| 003 FR-12 spike | Done: `notes/spikes/fr-12-self-registration-decision.md`. Recommends the custom join flow (option B). **Owner choice pending**, plus four sub-questions (§ Questions). After the decision: record it in the doc §10 and in `notes/decisions.md`, then mark 003 done |
| 010 CIAM guard | ✅ `a5e1dfbb0` |
| 020 Schema | Source done and reviewed (`6e5984449`). **Owner-gated dev import**, and the owner's call on securing `sprk_intakedata`. Steps and rollback are in `notes/task-020-notes.md` (needs a new `scripts/Set-ChildRecordCreatorContactSchema.ps1`) |
| 050 Detail view | ✅ `72325f91e` |

## Next Actions

1. **Owner approves A7** → apply it in the main session:
   - add the pointer markers at ADR-028 :36, :52/:56, :71 and :125;
   - write `notes/coordination/2026-10-10-adr-028-amendment.md` (the split with the auth project);
   - write `notes/task-001-notes.md` with the owner's quote;
   - mark 001 done.
2. **Wave 2:** 011, 012 and 040 as agents (opus). 040 also needs the owner's workforce client registration. 030 starts once the 020 columns are live in dev.
3. **002** starts when the owner sends the per-tab column list.

## Open Owner Questions / Blockers

- **A7 approval** (001). It blocks 011, 012 and 040.
- **020:**
  - approval of the dev import;
  - whether `sprk_intakedata` is field-secured (it is unsecured as drafted).
- **003:**
  - option B?
  - consent to "Spaarke Registration" as a customer prerequisite;
  - which group contacts join (`sprk-{customerId}-users` or a separate group);
  - who registers `CustomerTenantIds` in the shared registry (T240c or 060);
  - where the service is hosted.
- **060:** the registration-service host, and whether it is R3 or R4.
- **002:** the grid column list.
- **040:** creation of the new single-tenant workforce client registration.
- **Portfolio:** R3 is not on the GitHub board.

## Dependencies tracked elsewhere

- **Provisioning:**
  - T240c directory;
  - T240d S1 (iv) token shapes. When they arrive, swap the `AssumedS1Shape` tests in `AuthorizationModuleCiamTests`;
  - H3 pre-authorization of the new workforce client;
  - #1562 mailbox.
- **UAC-r2 PRs #1583 and #1586:** merged and in this branch.
- **`/conflict-check`** before the BFF PR: `AuthorizationModule.cs` is shared with UAC-r2.
