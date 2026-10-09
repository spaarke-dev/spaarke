# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-09 late (main session). State only — history is in git log, task `<completion>`
> blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: ontology critical
> path only**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-115; **D-115** = main session merges reviewed, green ontology PRs to master too; **D-107** = merge reviewed ontology PRs into the project branch when green; **D-113** = uac-r2's answers). Issues for after the
> project: [`notes/defer-issues.md`](notes/defer-issues.md) (D-106 section, F-1..F-49). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology critical-path lanes. 116 tasks: **65 ✅**, 2 🔄 (039, 130: code merged, live gates pending), 36 🔲, 9 ⏸️ deferred (D-106), 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111). Last master sync `217b8f3c6`; **master now has #1390 (039, `c45cb4719`) and dropped NSubstitute** (`1ef69821eb`) → sync master into the project branch only AFTER #1549 merges. |
| **Rule** | **D-106:** fix only what ontology functionality needs; document everything else in `notes/defer-issues.md`. Every sub-agent/reviewer prompt carries it. |
| **Critical path** | 039 live gate (D-67) → 037 → 031 evaluator → 032/033/034 → 035 deploy BFF → 038/050 → 059 worklist → 055 deploy Console → 064. Decision path: 040 (after 039 gate) + 046 (after 048) → 043 commit route → 058 wizard → 045. |
| **Next Action** | (1) On #1549 merge: pull the branch, then merge `origin/master` into `docs/ontology-platform-design` (brings 039 + Moq conversions), build + Signals tests, push. (2) On uac-r2's "deployed" message (SHA + healthz): run the **039 live gate** (POML step 5; expect the reconcile to re-own existing Signals/DRs under Secure roots within ~2 min). (3) On #1391 merged: start **048** (mechanical move, off master, own PR, uac-r2 review; merge per D-115). |

## Running now

| What | State | Then |
|---|---|---|
| **#1549** NSubstitute → Moq for 3 Signals tests (base project branch; head `149fcde87`, review APPROVE) | Merge job `bfzxx644i` (`merge-when-green.sh`) waiting on CI | Master sync (Next Action 1); remove worktree `C:\wts-nsub` |
| **#1391** (uac-r2 org-owned AppendTo fix, master) | uac-r2 merging on green, then deploys **master HEAD** BFF to dev (includes `c45cb4719`) — this is our D-67 deploy too | uac-r2 SendMessages SHA + healthz → 039 live gate; 048 starts |

## Waiting on others

| What | Waiting for | Then |
|---|---|---|
| **048** (write core → `Services/Dataverse`) | #1391 merged (uac-r2 agreed order: #1391 → 048 → 046; #1501 rebases later) | 046 (`OwnedChildWrite.CreateAsync` via `POST /api/v1/child-records/sprk_workassignment`, D-113) → 126 (BU-depth AppendTo rule, D-113) |
| **074** recall exit gate | Owner's labelling page https://claude.ai/artifact/VVoBr33LckyjoUGeWit3aU (24/92) | Finish → `status/final.complete` → resume the 074 agent |
| **130** live proof, **111/114** | The D-112 full E2E dev deploy (035 BFF + 055 Console + ontology solution) | Mark 130/111/114 done on it |

## Open owner questions / owner actions

- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV: r2-r5 (wizards) belong to 111; r6/r7/r8 to deferred 121/122/129.
- **Housekeeping (owner, by hand):** `C:\wt120d`, `C:\wtr1386`, `C:\wtrv1460`, `C:\wtrv1480*`, `C:\wts-b56`, `C:\wts-135`, `C:\wts-131`; older list in `notes/handoff-history/2026-10.md`.

## Coordination

uac-r2: post on #1355 **and** SendMessage their session (`spaarke-wt-unified-access-control-r2-*`); they reply by SendMessage.
