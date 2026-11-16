# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-16 (by context-handoff, at 86% context). State only; history in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-11.md` (previous checkpoint archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..**D-224**). Deferred work: one GitHub issue per item, label `ontology-deferred`,
> plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-156**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session `ontology-r1`: orchestrate R1; review every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`, `Services/Signals/Billing/**`, the DecisionWizard UI); answer peers; hold wrap-up 090 until owner UAT done. Peer messages are not owner approvals. |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design` (pushed). |
| **Active** | **185 🔄 [wip]** deployed to dev 2026-11-15 (master `acc96e1927`); owner ran checks A–D 2026-11-16, results + triage in `notes/task-185-notes.md`. Fix tasks (owner "All three"): **193 ✅** #2855 `c0d8a5b74f` (Console auto-deployed 04:32Z); **194 ✅** #2856 `e476e9b61b` (SemanticSearch 1.1.87, RelatedDocumentCount 1.21.7, NOT deployed); **195 🔄** implementer agent (Sonnet) running in `C:\wt195`, branch `fix/ontology-195-template-render-summary` (template render finished text + summary JSON + summary setState-in-render warning). |
| **Running** | Task 195 implementer agent (background). On its report: main-session review + independent Opus adversarial review → fix round → scoped re-check → sign-off comment + `gh pr merge --squash --auto` → mark 195 done (POML completion + TASK-INDEX + drift check) + `notes/task-195-notes.md`. |
| **Next Action** | (1) Finish 195 as above. (2) Then ask the owner's go for ONE dev deploy from master (D-126, scoped publish): SemanticSearchControl 1.1.87, RelatedDocumentCount 1.21.7, DocumentRelationshipViewer code page, plus any 195 client part; 195's BFF part goes via uac-r2's next dev BFF build (tell uac-r2 the SHA). (3) Owner open questions: send invoice `lockedRecord` one-line fix to ebilling-r1? (`CreateInvoiceWizard.tsx:139`); F12 check during Create Matter repro (A2/B5 unexplained: deployed bundles identical to Project); section A new requests (Project Draft with AI, To Do wizard launch + "New To Do" rename, invoice Quick Start/lookup/firm choice). (4) Close 185 when owner rechecks pass. |

## Open items

**Owner**
- 185 rechecks after 193–195 + deploy (above). 192 / 190 / 191 checks. 160 open (visual checks ride the 176 test; zz- comm "zz- UAT 176 fee or scope change (budget inquiry) - MAT-34929-0040" exists; delete it after).
- Owner's half-done 176 decision (budget revised + SR created, no email/DR, Signal closed, 2026-11-13): needs owner choice on clean-up — ask, do not act.
- ebilling 095f part 2 options (adjustments A/B, correct/confirm A/B/C, Create Invoice path) are the owner's.
- Temp folders `C:\t191m`, `C:\t191p` for owner to delete. 105 (v4 UAT checklist) open; 189 (Office add-in templates route) not written. **090 held.**

**ebilling-r1**
- **130e-1** (DEF-060 firm reason, branch `task/ebilling-130e-1` tip `99e380f997`): ontology O6 review **PASS** 2026-11-16 (K2 writer preflight uses 1-arg Validate; K4 replay writes current version text, C1). Later Signals pushes → re-glance. O2 = (a) (held fallback never carries a firm reason). Column apply + smoke Signal on their owner's go; notices before/after.
- **#2839** (work→master, head `ced184c79a`): ontology sign-off POSTED (comment 6897983607) for writer/catalog/RecordProcedure tests/immutability test/BillingJudgedDates+evaluator, byte-identical to reviewed `4356eef89b` / `84e708682c`; **conditional on CI green**; any later push touching Signals → re-glance.
- 084h design PASSED (73ce24172c). 084h-1 reviewed in 2 rounds (PASS). Carried: **K1** — 084h-3 repair job retry list must classify BillingRecordLookupFailed, OwnerRefused (deleted core; needs a test), OwnerResolutionFailed, CreateFailed; **H-3** — 084h-2 service enforces the 2000-char note cap. Review both when 084h-2/3 arrive.
- 095f part 2 design PASS (fixes 1–3, Q6–Q8); catalog/exact-set changes come to us in its PR.
- **Held PRs ours:** 174 (#2580, head `9423f37004`) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both, send SHAs to uac-r2, **and tell mcp-r1** (#2587 waits on them).

**mcp-r1**
- **#2587** (work/spaarkeai-mcp-bff-api-r1): ontology review PASS (one adversarial pass; F4 idempotent hint fixed in `efa348273f`); sign-off comment 6900723738. **Condition: must not merge before #2580 and #2579** (branch carries 174/175). Then DI-143 #2630 builds on `WorkItemPrivilege`.

**uac-r2** — dev BFF deploy owner. Dev BFF = `f451e0874d` (master `ce6c241964` + mcp spike, 2026-11-16 00:44Z; has a M.E.AI package mismatch that breaks AI draft, fix in their next build).

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, DI-148 #2671, DI-149 #2672, DI-154 #2813, DI-155 #2814; `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master. 176/160 run findings (Graph sentitems 400 after sendMail, `sprk_pendingspend` PATCH 400, `sprk_regardingreportcard` query on thread): check whether filed; detail archived in `notes/handoff-history/2026-11.md`.

**Worktree cleanup** (`scripts/worktree/Remove-WorktreeSafe.ps1` only): task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` after #2580/#2579 merge. `C:\wtdep185`, `C:\tmp\rv-084h1`, `C:\tmp\rev2587-850451d` removed 2026-11-15.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA. Console auto-deploys from master (CLAUDE.md §6). Code pages/PCFs: ours, after owner go, from master (D-126).
- Not on dev (nothing to update): `sprk_communicationpage` (CommunicationPage), `sprk_rulereviewpage.html`.
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`.
