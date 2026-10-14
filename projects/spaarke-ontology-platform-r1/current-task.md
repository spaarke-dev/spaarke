# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-14 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; **D-138 owner approvals**) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-138). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-78**; lanes may take 78+).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes toward the 035 BFF deploy → 146 live gate → 059/055 Console deploy. |
| **Blocker** | None from the owner (D-138 answered everything). |
| **Next Action** | Wait for agents 038, 042, 145 (PRs → uac-r2 review → merge). Merge watchers run for #1865 (041) and #1869 (061). When 145 merges: run **035** (deploy BFF to dev, D-126 procedure below). |

## Lanes (worktrees `C:\wts-NNN`; all branch from `origin/docs/ontology-platform-design`)

| Lane | PR | State | Next |
|---|---|---|---|
| **038** Signal read route (opus) | — | running; also adds read-time evidence redaction (145 F1 option B) | PR → uac-r2 → merge |
| **042** gate → DR writer | — | running; main-session decision A+C: additive `WriteGateDecisionAsync`, Judgement both paths, confirmedby/policyversion/gatesessionid null, correlation id in factsnapshot, criterion 4 amended (owner may override) | PR → uac-r2 → merge |
| **145** matter readers, evidence leak | — (local `fcb930525`) | running; F1 → option A (monotonic redaction pass in the job); DR snapshot redaction deferred to the owner as a new issue | PR → uac-r2 → merge |
| **041** append-only live test | #1865 `7bc1ab688` | main-session review CLEAN; merging on green | mark done |
| **061** Do-lane policies | #1869 `f34f50058` | merging on green; 4 policies on dev, `sprk_enabled=No`; grammar gained a null test (`9b382ede0`) | mark done |

Merge: `scratchpad/merge-when-green3.sh <pr> <full sha> docs/ontology-platform-design` (ignores the informational Code Quality job; exits on HEAD MOVED).

## Deploy plan (D-126, D-138 approved)

1. **035 BFF** after 145 merges: merge current master into the deploy branch (dev runs master `8a1f3bb11` exactly, uac-r2 confirmed, so nothing of theirs is off master), schema pre-flight on the target, hash-verify. **Before deploying, message word-add-in (`spaarke-wt-spaarkeai-word-add-in-r1-ea`) with the SHA and the restart time — the owner is UATing add-ins on dev.** Also tell uac-r2.
2. After 035: run 144's stamp job and 148's reconciler **report-only** on dev, record it, and send it to uac-r2. If CLEAN, enable on dev (D-138 delegates this): `CoreAncestor__StampReconciliation__ReportOnlyTables=","` (= 145's `scripts/Invoke-OntologyStampBackfill.ps1 -Apply`) and `Ontology__TodoAssigneeOwner__WritesEnabled=true`.
3. **146** live gate after 035 and the 6 h DR metadata cache key.
4. **059** worklist widget (needs 038) → **055** Console deploy (needs 059, 043, 035) → 049, 064, 105.

## Done today (2026-10-14)

#1844 (050, row live on dev), #1858 (148), #1862 (144), #1854 (143 K1/K2; #1855 closed), #1856 (058). DI-77 (#1868): the dev `sprk_recordtype_ref` row for `sprk_workassignment` was fixed to `sprk_name` (other environments are still open).

## Owner items still open

- The DR `sprk_factsnapshot` after a re-file into secure (145 F1): redact via a service write, or store references only. 145 files it as an issue.
- Q8: #1111 to master after 035 + 055.

## Coordination

- uac-r2 = `spaarke-wt-unified-access-control-r2-ac` (also post on #1355). Security F (#1857) is fixed by them in master `8a1f3bb11` (PR #1861). Secure-family owner team per family = their project uac-secure-project-r3 (owner round 100b).
- word-add-in = `spaarke-wt-spaarkeai-word-add-in-r1-ea` (notify before any dev BFF deploy).
