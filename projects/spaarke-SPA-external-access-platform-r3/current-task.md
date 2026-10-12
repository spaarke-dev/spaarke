# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-11 (020 dev import done; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None running. 035 is reviewed and fixed (merge waits on the owner: ownership); 052 waits on the owner (field sets, card targets); 032/054 wait on the owner (service-request delete, invoice documents) |
| **Step** | 020 done and committed (`460467c95`, `9cfa31e4c`). 035 implementation in an isolated worktree from `9cfa31e4c` |
| **Status** | Not pushed since `e2b547352`: 056 merge + 020 commits wait for the 056 re-check |
| **Next Action** | On each agent's completion: 056 → commit by path, push, mark done; 032/035 → two fable reviews for 035 (auth), one for 032, fix, merge `--no-ff`, verify |

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
- **POML vs spec:** an owner decision recorded in the spec or the project CLAUDE.md wins over a POML. Surface any conflict to the owner before acting (036 lesson).

---

## Active lanes

| Task | State |
|---|---|
| 035 Intake submit | Implemented and reviewed (two fable passes). Fix round `b6b4ab58f` (contact-unreadable 503, Dataverse 503, 256 KB cap, ledger guard) re-checked by the main session; branch head `b6b4ab58f` in worktree `C:\code_files\spaarke\.claude\worktrees\agent-ab4a5f6610ac67b9c`. **Merge blocked on the owner: who owns an intake row** (review pass 1 F2: I-6 / ADR-003, the owner census cannot see `BuildServiceRequestCreatePayload`). After the answer: implement the option (owner seam or census waiver + design.md ADR Tension), merge `--no-ff`, verify, mark done |
| 032 Delete-own | Merged `19b24414e` (documents, to-dos, events; review approved; fixes `f7cb07ac3`). Full verification is running. Stays 🔄 until the owner decides service-request delete (AC 1 names service requests) |
| 054 Detail documents + delete | Committed and pushed `83565c720` (fable review + fix round; SPA 650/650; server `/me systemUserId` + document-list `canCreate`). Stays 🔄 until the owner decides service-request delete (032) and invoice documents (031). Owed: live browser checks (cancel-then-delete and two deletes in one detail view, dark mode) |

Done: 001, 003, 010, 011, 012, 020, 056, 030, 033, 034, 036, 041 (code), 042, 050, 051.

## Next actions (in order)
1. After 035: 037 and 052.
3. After 054: 055.
4. Start 060 (the self-registration service is in R3, per the owner 2026-10-11).
5. Before the BFF PR, run `/conflict-check` (`AuthorizationModule.cs`, `Api/ExternalAccess/**` shared with UAC-r2) and actionlint on both deploy workflows.

## Open owner questions

1. **052 intake wizards:** approve the field sets (NDA: counterparty, purpose, mutual/one-way, deadline, notes; Policy Question: topic, question, urgency Low/Normal/High) and whether the NDA and Policy Question cards switch from their widgets to the wizards.
1. **032 service-request delete:** A refuse when any related row exists (recommended) / B cascade the caller's own related rows, refuse if staff rows exist / C allow only in the first status.
1. **035 licensed user with no contact:** the route answers 403 today. (a) keep the 403 and turn on `IdentityLink:Reconciliation:WritesEnabled` per environment / (b) create or link a contact at submit / (c) another attribution column for systemusers.
1. **035 owner of an intake row:** it is application-owned (root BU) today. (a) leave it until routing exists / (b) a configured intake owner team / (c) the acting user's team for systemusers, plus (a) or (b) for contacts.
1. **003 sub-questions:** Q2 customer consent, Q3 the group, Q4 the registry field owner, Q5 the host.
2. **031:**
   - Invoice documents: A inherit (recommended).
   - A service request regarding a secure matter goes to the secure container (recommended yes).
   - The owner confirmed one container per customer or business unit, and secure records only.
3. **033:** privileged messages to external callers. Options: (a) hide (recommended), (b) mark, (c) show. Explained to the owner 2026-10-11.
4. **036:** the address of the central sender mailbox (one Spaarke shared mailbox for all Model 1 customers is feasible). Then make `RegistrationEmailService.FromMailbox` configurable.
5. **040/041/043:** go/no-go on:
   - the app-registration steps given;
   - the CIAM `user_impersonation` steps;
   - the GitHub environment `external-spa-dev` (9 known values), and an empty `external-spa-prod` with protection rules.
6. **002:** the owner will send the column list.

## Dependencies tracked elsewhere
- **ISS-001** (#1623): `AuthorizationModule` once-per-process flag.
- **ISS-002** (#1633): intermittent chat streaming contract test (two occurrences).
- **ISS-003** (#1636): the shared `sanitizeEmailHtml` keeps style positioning and remote images; R3 contains it in the SPA only.
- **Copilot agent** still uses `access_as_user`; do not remove it from `1e40baad`.
- **038 deploy order:** satisfied in dev. Every other environment needs the 020 columns before the R3 BFF.
