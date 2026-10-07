# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-07 (main session; rewritten as state, not history — previous version archived at
> `notes/handoff-history/current-task-archive-2026-10-07.md`). Standing rules live in [`CLAUDE.md`](CLAUDE.md);
> decisions in [`notes/decisions.md`](notes/decisions.md). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No main-session task — orchestrating. Counts and critical path: `tasks/TASK-INDEX.md` header. |
| **Branch** | `docs/ontology-platform-design` (draft PR #1111), master merged in 2026-10-07 (`c56a30c3f`). |
| **Status** | In progress. Critical path to 031: 039 (waits on uac-r2 review #1355) → 037 → 031; 031 also needs 098 merged and 106. |

### Open items, in order

1. **Owner decision pending — writer credential vs tenant-isolation rule I5.** `Spaarke.ArchTests` = 810/811 on the
   merged branch (master = 811/811); the one failure is `OntologyWriterCredentialFactory.cs` building
   `ManagedIdentityCredential`, which cannot carry a `TenantId`. Proposed: build it like the central factory
   (`DefaultAzureCredential` + `TenantId` + the writer's client id, every non-managed-identity source excluded, a unit
   test pinning the exclusions) and update the ADR-028 row in `spec.md` §6. Explained to the owner 2026-10-07; waiting
   for a yes. Blocks Tier 1 on #1111.
2. **098 — PR #1359** (head `a63ad87be`). Agent folding in task 065 (D-27: Briefing reads `sprk_duedate`) and fixing
   nine tests whose in-file `process.env.TZ` pin does nothing. Then: one focused independent review → ask the owner to
   merge. Then mark 098 + 065 ✅, and start **106** (To Do dates → Date Only).
3. **079 / 039** — uac-r2 review requested in **issue #1355** (plus a comment about the Secure Record Owner Reads that
   sit outside their config). 039 starts when they answer. A direct cross-session message expired unread.
4. **Owner's procedure update (2026-10-07)** — step 1 (merge master) done; steps 2–3 (this restructure) done in the
   commit that introduced this file; reply to the owner with sizes and what moved.
5. **Owner chose all five parallel streams (2026-10-07)** — table below; stream table at top of TASK-INDEX. Merge stream branches back one at a time (036 and 046 both edit the route ledger). D-42..D-56 decided; no open points remain.
6. **Pass on to the owner** (no decision needed unless they object): D-25 in dev falls back to UTC because dev events
   fill only `sprk_assignedto`, which `AssignedToDefaults` (uac-r2) doesn't read; task 008 kept the writer's existing
   Read on `sprk_budget` (the evaluator needs it); the read-only Spaarke Platform forms show only Name and Owner.

### Running agents

| Agent | Work | Notes |
|---|---|---|
| 098 | PR #1359 follow-ups (065 + TZ tests) | Own worktree `C:\wt098`; no merge |
| credential fix | I5 tenant rule: writer credential rebuilt like the central factory (owner-approved) | This worktree; commits code only; main session commits its spec/decisions/CLAUDE.md lines |
| Stream C | 036 → 026 | `C:\wts-c` branch `stream/c-036-026` → main session merges into this branch |
| Stream C2 | 047 → 046 | `C:\wts-c2` branch `stream/c2-047-046` → merge back; needs work-assignment owner agreement |
| Stream D | 072 (then 073/074) | `C:\wts-d` branch `stream/d-072` → merge back |
| Stream B | 057 UI kit | `C:\wts-b` branch `feat/console-ui-kit-057` → own PR to master |
| Stream E | 060 | `C:\wts-e` (own PR or `stream/e-060` per POML) |
| Main session | 110 ADR-050 amendment (own PR to master) | then 056 |

### Housekeeping for the owner

Delete by hand (sandbox can't delete under `C:\`): `C:\wt081-base`, `C:\wt097m`, `C:\wtz`, `C:\wt097\TestResults097`,
`C:\wt081r`, `C:\wt081s`. Registered worktrees to remove after their PRs: `C:\wt081b`, `C:\wt081c`, `C:\wt081m`,
`C:\wt081`, `C:\wt092`, `C:\wt094`, `C:\wt095`, `C:\wt096`, `C:\wt097`, `C:\wt098` (after #1359).
