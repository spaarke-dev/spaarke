# Spaarke Auth System of Record — R1

> **Status**: Investigation in progress (started 2026-10-08)
> **Worktree**: `C:\code_files\spaarke-wt-spaarke-auth-system-of-record-r1` · **Branch**: `work/spaarke-auth-system-of-record-r1`
> **Code baseline**: `master` @ `45202953f`
> **Origin**: owner direction 2026-10-08 (from `spaarke-SPA-external-access-platform-r3`), after R3's Model 1 client
> sign-in questions showed that ADR-028, deployed code and several projects' notes disagree.

## Goal

One code-level record of how authentication and authorization work for **every user type** on **every surface**,
through to Dataverse, SharePoint Embedded, Graph and the AI services. It is meant to replace the existing auth
documentation as the single source of truth once reviewed.

## Evidence rules

1. Current code on `master` is the only authority. Every claim cites `file:line`.
2. Existing documents — ADR-028, `.claude/patterns/auth/`, `.claude/constraints/auth.md`, guides, architecture
   docs, other projects' notes — are leads only. Each claim taken from them is marked verified, stale,
   contradicted, or unverifiable, with the code evidence.
3. Unmerged work in other branches is recorded as pending, never as current behavior.
4. What code cannot prove (actual token claims, live Entra settings, host behavior) is marked
   "needs live verification" and goes into a test plan run with the owner.
5. Live Entra reads are read-only, and the exact commands are shown to the owner before they run.

## Layout

| Path | Contents |
|---|---|
| `working/` | One evidence file per area, written by the trace agents, corrected by independent verifiers |
| `auth-system-of-record.md` | The assembled record (written after verification) |

## Consumers waiting on this

- `spaarke-SPA-external-access-platform-r3` — spec conversion held until this record exists.
- `customer-provisioning-orchestration-r1` (t240 shared-client chain, 240d CIAM on stamps), `spaarkeai-word-add-in-r1`
  (task 122 `@spaarke/auth` tenant fix), `unified-access-control-r2` (Model 1 guest classification).
- A possible ADR-028 amendment (A7) for Model 1 client sign-in — not to be drafted until this record exists.
