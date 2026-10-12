# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-12 evening (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership via RecordOwnershipResolver) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-130). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`. Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. **75 ✅**, 1 🔄 (130), 27 🔲, 9 ⏸️, 4 🚫. 025 merged (#1664 → `58b0cfff4`). |
| **Blocker** | **Weekly usage limit hit 2026-10-12; resets 9pm America/New_York.** All 4 agents stopped mid-work. No agent launches before the reset. |
| **Next Action** | After the reset, resume in this order (each a fresh agent, `model` stated, cap 4): (1) 043 rework, (2) 025b, (3) adversarial review #1746, (4) adversarial review #1747. Then fix rounds per the lane table. |

## Lanes (worktrees `C:\wts-NNN`; all branch from `origin/docs/ontology-platform-design`)

| Lane | PR | Local state | Resume with |
|---|---|---|---|
| **043 rework** (D-129/D-130 ownership, D-35 read, Q18 reminder) | #1665 (remote head `ca07a684b`, uac-r2 approved there) | `C:\wts-043` **3 unpushed commits**: `b4f0a385b` (DR owner via resolver as returned, decider contact in `sprk_assignedtointernal`, no-core Signal read = caller reads Signal AND subject), `0f5d16d36` (evaluator drops owner TZ fallback; send-reminder non-resolving), `9ff2d3877` WIP tests (unverified). Lane was mutation-checking "owner resolved before FR-51" (DecisionCommitService ~:852). | Implementer (sonnet): verify/finish tests, mutation checks, full Decision/Signal suites + ArchTests, ledger refs, squash WIP, push; report personal-To-Do resolver answer (D-130). Then scoped re-check + **uac-r2 re-check** of DecisionCommitService/DecisionRecordWriter/SignalCoreRecordAccess. Not covering the secure schema: wait for uac-r2's conformance matrix. |
| **025b** (D-122: Matter+Project, values from base spend/budget) | none yet | `C:\wts-025b` branch `task/ontology-025b-threshold-project-values`, **WIP commit `274f24acb`** (unbuilt): new `Services/Finance/FinanceBaseValues.cs` (+tests), edits to FinanceRollupService, PredicateCompiler, RuleBodyDescriber, SignalEvaluator, SignalWriter, tests. Was about to build + run filtered tests. | Implementer (sonnet): build, test, mutation-check, finish per the original brief (grouped aggregates, MissingInput on null/0 budget, percent units, project columns, list untriggered spend events for a deferred issue), squash WIP, draft PR. |
| **#1661 secure suppression** | #1746 draft, head `35ce786da` (**hold head** for uac-r2's comment) | clean | Adversarial review (opus) not done (stopped at limit). Known required fix from uac-r2: secure = `ownerid == SRO` **OR** any ancestor root `sprk_issecure` (null counts as secure; `SecureChildShareSynchronizer.SecureRootsAboveAsync`), fail closed; ordinary→secure move withdraws all ordinary-partition state; the "first episode as ordinary" limit is a D-123 leak, not a K. One fix round after both reviews. |
| **#1659 filing trigger** | #1747 draft, head `5c51f8612` (**hold head** for uac-r2's comment; their review is queued) | clean | Adversarial review (opus) not done. Needs **word-add-in sign-off** on `ReconciledEmailFiling.cs` (both their sessions held/expired our message; request is a PR comment) — owner may approve on their behalf. |

Merge: `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` (`gh pr ready` first). Office-scope CI job hits its 20-min cap (F-49, #1718): merge on log evidence with a PR comment.

## Waiting / owner

- **Q22 (open):** which event column holds the individual — `sprk_assignedto1` (owner's Q17 note) or `sprk_assignedto` (uac-r2's OwnedChildWrite map, task 152)? All assigned-to columns are **contact** lookups.
- **Q20 (parked):** no-core Decision Records have no typed subject lookup, so SecureChildLineage can't re-file them on a later secure flip; waits for uac-r2's secure-schema conformance matrix (owner-requested audit of all ontology tables).
- **D-130 consequence to confirm:** a Decision Record about a personal To Do is expected to land in the decider's BU default team (visible to the BU).
- **Q7 live re-run:** attempted once; never ran (Azure CLI token timeout under load; no rows written, verified by query). Retry once when the machine is quiet: env `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`, filter `FullyQualifiedName~SignalWriterSeamTests.WriteAsync_EventAndTodoUnderAMatter_GroupUnderTheMatter_CreateAndReconcile_RefreshDueDate&DisplayName~sprk_todo`; then query zz-037 rows = 0.
- **Q11:** rename deferred F-nn → I-nn in one commit (025 has merged, so it can run now).
- **Q15:** EventId 50361 alert + runbook in task 035. **Q8:** #1111 to master after 035 + 055.
- **To do (docs, main session):** reword D-35/D-39/D-25 and spec lines (FR-14 `spec.md:265-271,300,382,435,646,658,723`) to team ownership; fix `docs/data-model/sprk_matter-related-tables.md:23` (`sprk_assignedtointernal` targets contact, not systemuser).
- **After ontology works:** close-out phase in TASK-INDEX (deferred issues batched by area).
- Owner decisions page https://claude.ai/artifact/ADCv42eKukX2BQx7d62Mop (Q1–Q19 answered 2026-10-12).

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. Their K2 is #1730 (keep 403 `caller_unresolved`). New deferred issues today: #1731 (F-70), #1742, #1743, #1744.
