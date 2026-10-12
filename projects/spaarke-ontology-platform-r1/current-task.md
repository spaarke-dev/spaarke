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
| **Blocker** | none (usage limit reset; 4 agents running). |
| **Next Action** | Wait for agent notifications: 043 finisher → send #1665 head to uac-r2 for re-check; #1747 fix round → send head to uac-r2; #1746 combined fix round (opus) → send head to uac-r2 + scoped re-check; 025b → review. |

## Lanes (worktrees `C:\wts-NNN`; all branch from `origin/docs/ontology-platform-design`)

| Lane | PR | Local state | Resume with |
|---|---|---|---|
| **043 rework** (D-129/D-130 ownership, D-35 read, Q18 reminder) | #1665 (remote head `ca07a684b`, uac-r2 approved there) | `C:\wts-043` **3 unpushed commits**: `b4f0a385b` (DR owner via resolver as returned, decider contact in `sprk_assignedtointernal`, no-core Signal read = caller reads Signal AND subject), `0f5d16d36` (evaluator drops owner TZ fallback; send-reminder non-resolving), `9ff2d3877` WIP tests (unverified). Lane was mutation-checking "owner resolved before FR-51" (DecisionCommitService ~:852). | Implementer (sonnet): verify/finish tests, mutation checks, full Decision/Signal suites + ArchTests, ledger refs, squash WIP, push; report personal-To-Do resolver answer (D-130). Then scoped re-check + **uac-r2 re-check** of DecisionCommitService/DecisionRecordWriter/SignalCoreRecordAccess. Not covering the secure schema: wait for uac-r2's conformance matrix. |
| **025b** (D-122: Matter+Project, values from base spend/budget) | #1756 draft, head `235a3eeda` | clean (1354 unit + 962 ArchTests pass; 8 mutations; +0.007 MB) | **Adversarial review (opus) running.** Then fix round, then merge. Implementer's 7 deferred items in PR body — file as `ontology-deferred` issues after review unless review promotes one (esp. #1 "BudgetRevision trigger never enqueued", D-12 hook — check 043's commit route). |
| **Child-record contract** (owner round 95 via uac-r2; note `origin/work/unified-access-control-r2:projects/unified-access-control-r2/notes/ontology-standard-child-contract-2026-10-12.md`, C1–C9, 11 acceptance) | — | planning | Sequence: T1 schema (DR `sprk_regarding{communication,document,event,invoice,matter,project,todo,workassignment}` + polymorphic pair; confirm pair on Signal; scoped import) → uac-r2 lineage+census PR → T2 DR writer (own subject lookups, own subject as resolver parent, two-wall refusal in also-resolve) · T3 D-130 owner on SignalWriter/Suppression/Commit (F3; F2 blocked) · T4 register both tables with CoreAncestorRestamper/StampSourceColumns · T5 `sprk_matter` reader audit incl. dedupe key `SignalWriter.cs:695` + evidence leak (uac-r2 #1747 (e): communication subject/sender on matter Signals, `SignalEvaluator.cs:561-567`, `SignalWriter.cs:1243`) → live gate §5. **T1 needs owner confirmation in this session** (schema). Live read 2026-10-12: DR has no `sprk_regarding*`; Signal has 9 subject lookups (incl. servicerequest) + text pair (`sprk_regardingrecordtype` is String — deliberate, `SignalWriter.cs:238`; To Do/Event/Communication use Lookup). Proposed DR delta sent to uac-r2 (awaiting confirm): 9 lookups `sprk_regarding{communication,document,event,invoice,matter,project,servicerequest,todo,workassignment}` + `sprk_regardingrecordid` (String) + `sprk_regardingrecordtype` (Lookup→`sprk_recordtype_ref`) + `sprk_regardingrecordname/number/url` (String) = 14 columns. Recommend keeping Signal's text pair for R1 + deferred issue to align. Create T1–T5 POMLs via task-create. 043 keeps its scope. **T4 binding (uac-r2):** registration as a separate small commit; run `CoreAncestorStampReconciliationJob` report-only on dev first (`CoreAncestor:StampReconciliation:WritesEnabled=false`) and send uac-r2 the stale-row report; Signal is a stamped child, never in `IntermediateRootColumns` (pinned to TS by `CoreAncestorResolverTests`); `ClassifyStampSource` treats `sprk_regarding{matter,project,workassignment}` on Signal/DR as root (To Do convention), with a test. uac-r2 starts lineage+census PR when we send the live column list. |
| **#1661 secure suppression** | #1746 draft, head `35ce786da` (**hold head** for uac-r2's comment) | **Combined fix round running** (opus implementer) | Our review done (5 F + 2 K): R1 new planner leak (re-raise partition from previous episode's owner, `SignalReevaluationJob.cs:850/880`; hit partition must come from resolver truth); R2 stamps never withdrawn (re-derive/clear nightly + ApplyAsync; supersedes "policy switched to No" limit); R3 union test (ownerid==SRO OR SecureRootsAboveAsync Roots/Undetermined, fail closed); R4 audit F6 partition by set of secure roots; R5 tests + mutation; R6 integration tests `DecisionCommitFollowUpsTests.cs:54`/`DecisionCommitServiceTests.cs:212` need `IConfiguration` arg — whichever of #1665/#1746 merges second; R7 pre-existing episode-number leak → deferred issue. Asked uac-r2 whether the walk stopping at ordinary-team-owned rows (`SecureChildShareSynchronizer.cs:651-653`) is acceptable. After push: uac-r2 re-check + scoped re-check. |
| **#1659 filing trigger** | #1747 draft, head `5c51f8612` (**hold head** for uac-r2's comment; their review is queued) | clean | Adversarial review (opus) not done. Needs **word-add-in sign-off** on `ReconciledEmailFiling.cs` (both their sessions held/expired our message; request is a PR comment) — owner may approve on their behalf. |

Merge: `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` (`gh pr ready` first). Office-scope CI job hits its 20-min cap (F-49, #1718): merge on log evidence with a PR comment.

## Waiting / owner

- **Q22 (open):** which event column holds the individual — `sprk_assignedto1` (owner's Q17 note) or `sprk_assignedto` (uac-r2's OwnedChildWrite map, task 152)? All assigned-to columns are **contact** lookups.
- **Q20 (parked):** no-core Decision Records have no typed subject lookup, so SecureChildLineage can't re-file them on a later secure flip; waits for uac-r2's secure-schema conformance matrix (owner-requested audit of all ontology tables).
- **D-130 consequence to confirm:** a Decision Record about a personal To Do is expected to land in the decider's BU default team (visible to the BU).
- **Q7 live re-run:** attempted once; never ran (Azure CLI token timeout under load; no rows written, verified by query). Retry once when the machine is quiet: env `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`, filter `FullyQualifiedName~SignalWriterSeamTests.WriteAsync_EventAndTodoUnderAMatter_GroupUnderTheMatter_CreateAndReconcile_RefreshDueDate&DisplayName~sprk_todo`; then query zz-037 rows = 0.
- **Q11:** rename deferred F-nn → I-nn in one commit (025 has merged, so it can run now).
- **Q15:** EventId 50361 alert + runbook in task 035. **Q8:** #1111 to master after 035 + 055.
- **Q11 timing:** hold the F-nn → I-nn rename until #1746/#1747/025b merge (their branches touch the notes; avoid conflicts).
- **After ontology works:** close-out phase in TASK-INDEX (deferred issues batched by area).
- Owner decisions page https://claude.ai/artifact/ADCv42eKukX2BQx7d62Mop (Q1–Q19 answered 2026-10-12).

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. Their K2 is #1730 (keep 403 `caller_unresolved`). New deferred issues today: #1731 (F-70), #1742, #1743, #1744.
