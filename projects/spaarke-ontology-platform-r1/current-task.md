# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-17 (ebilling reviews + gate-job defect added; rest unchanged from 2026-11-16 checkpoint). State only; history in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-11.md` (previous checkpoint archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..**D-224**). Deferred work: one GitHub issue per item, label `ontology-deferred`,
> plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-156**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session `ontology-r1`: orchestrate R1; review every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`, `Services/Signals/Billing/**`, the DecisionWizard UI); answer peers; hold wrap-up 090 until owner UAT done. Peer messages are not owner approvals. |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design` (pushed). |
| **Active** | **185 🔄 [wip]** deployed to dev 2026-11-15 (master `acc96e1927`); owner ran checks A–D 2026-11-16, results + triage in `notes/task-185-notes.md`. Fix tasks (owner "All three") all merged: **193 ✅** #2855 `c0d8a5b74f` (Console auto-deployed 04:32Z); **194 ✅** #2856 `e476e9b61b` (SemanticSearch 1.1.87, RelatedDocumentCount 1.21.7); **195 ✅** #2861 `04f5c943f9` (template render finished text + caller-formatted values; Summary once). 194 + 195 client DEPLOYED to dev 2026-11-16 13:42Z from `04f5c943f9` (SemanticSearch 1.1.87, RelatedDocumentCount 1.21.7, DRV; deploy-log); chore PR #2868 merged (records the SemanticSearch bundle). 195 BFF live in dev BFF `be0420d763`. |
| **Running** | Nothing. |
| **Next Action** | (1) Owner rechecks on dev after a hard reload: C1/C2 lookup above the Email documents wizard (Semantic Search, DRV via Find Similar, DRV from a form) + Summary readable once; D1 Console Send Email lookups + Related to; A7 rule wizard. Template render (195 BFF) is live too (dev BFF `be0420d763`): apply a template with placeholders in Email documents and Console Send Email, send to self. (2) Owner open questions: invoice `lockedRecord` fix to ebilling-r1? (`CreateInvoiceWizard.tsx:139`); F12 check during Create Matter repro (A2/B5); section A new requests; retry AI draft on dev BFF `be0420d763`. (3) Close 185 when rechecks pass. |

## Open items

**Owner**
- 185 rechecks after 193–195 + deploy (above). 192 / 190 / 191 checks. 160 open (visual checks ride the 176 test; zz- comm "zz- UAT 176 fee or scope change (budget inquiry) - MAT-34929-0040" exists; delete it after).
- Owner's half-done 176 decision (budget revised + SR created, no email/DR, Signal closed, 2026-11-13): needs owner choice on clean-up — ask, do not act.
- ebilling 095f part 2 options (adjustments A/B, correct/confirm A/B/C, Create Invoice path) are the owner's.
- Temp folders `C:\t191m`, `C:\t191p` for owner to delete. 105 (v4 UAT checklist) open; 189 (Office add-in templates route) not written. **090 held.**

**ebilling-r1**
- **130e-1** (DEF-060 firm reason): ontology O6 review **PASS** 2026-11-16, extended to fix round 2 `56bf6dce82` (uac-r2 F3 NFKC name check); merged into `work/ebilling-r1` at `a6f1363070` (tip `da4ed0311f`). K2 writer preflight uses 1-arg Validate; K4 replay writes current version text (C1); K Format-char names never match C4 (in their notes). O2 = (a). Policy-version columns `sprk_rejectionreason`/`sprk_firmdescription` live on dev; Entity.xml via their task 079. Next from them: 130e-2 (Rule Review editor field, plain-text render). Later Signals pushes → re-glance.
- **#2839** (work→master, head `ced184c79a`): ontology sign-off POSTED (comment 6897983607) for writer/catalog/RecordProcedure tests/immutability test/BillingJudgedDates+evaluator, byte-identical to reviewed `4356eef89b` / `84e708682c`; **conditional on CI green**; any later push touching Signals → re-glance.
- 084h design PASSED (73ce24172c); owner accepted our §8 classes/tiers, DELETE note in JSON body, repair job 084h-3 required (server-side retry). 084h-1 reviewed in 2 rounds (PASS). Carried: **K1** — 084h-3 repair job retry list must classify BillingRecordLookupFailed, OwnerRefused (deleted core; needs a test), OwnerResolutionFailed, CreateFailed; **H-3** — 084h-2 service enforces the 2000-char note cap. Review both when 084h-2/3 arrive.
- **2026-11-17 reviews:** 095f-2a catalog (note on reject-invoice/return-to-firm) PASS; **084h-3** repair job PASS at `36b3f9004f` (F1 only ParentUnresolved permanent, other OwnerRefused codes retry; F2 permanent → Poisoned/dead-letter; refused lock → Failure) — sign-off given, they merge after their Opus pass + CI; **095f-2c** catalog line (override-submission + NoteParameter) PASS at `9414f92d9b`; 095f-2c writer change (Reason/Note/StoredContentReadable, `sprk_reason` column, `StoredNoteOf`) PASS at `c7ffb82f5a`; **095f-2d1** catalog (accept/reduce/disallow-line + NoteParameter) PASS at `06fe43b3e5`. **095f-2b** (full review, `22d4c29d36`): entries + exact-set amendment PASS; **F open**: add-adjustment decision id = raw client AdjustmentId (`InvoiceAdjustmentService.cs` ~842), and the widened `BillingLookupCodes` lets it pre-claim another invoice decision's derived id → fix = `IdOf("ebilling-add-adjustment-{invoice}-{adjustmentId}")` + test; re-check fix diff when they send the SHA.
- **Our defect found by ebilling-r1 (owner decision pending):** `GateDecisionRecordJob.cs` ~215-222 on master returns `Success` when the processing lock is refused; IdempotencyService fails open/stale-lock takeover after 1 min, so a decision record can be silently dropped. Proposed **task 196**: return `Failure` (dead-letters visibly; concurrent duplicate replays as AlreadyRecorded = known limit). Owner to choose: fix now (196, PR to master, uac-r2 BFF build) or file DI-156 + `ontology-deferred` issue.
- 095f part 2 design PASS (fixes 1–3, Q6–Q8). Owner decisions EB-D-239..243 (their `69745e926d`, 2026-11-16): new internal `sprk_invoice.sprk_decisionnote`; adjustments get DRs (095f-2b: 2 DeclaredBilling entries + our exact-set tests `DecisionRecordWriterTests.Billing.cs:191`, `DecisionActionCatalogTests.cs:348-354`, diff to us BEFORE merge = full review); correct/confirm note on the correction row (no writer change, :1266 literal untouched); optional note on 6 actions (095f-2a/2c/2d1 = light reviews). First wave 084h-2, 095f-2s, 095f-2e1 (no Signals).
- **Held PRs ours:** 174 (#2580, head `9423f37004`) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both, send SHAs to uac-r2, **and tell mcp-r1** (#2587 waits on them).

**mcp-r1**
- **#2587** (work/spaarkeai-mcp-bff-api-r1): ontology review PASS (one adversarial pass; F4 idempotent hint fixed in `efa348273f`); sign-off comment 6900723738. **Condition: must not merge before #2580 and #2579** (branch carries 174/175). Then DI-143 #2630 builds on `WorkItemPrivilege`.

**uac-r2** — #2859 (SpaarkeMaster 1.3.2.0 invoice-family field lock): ontology ACK 2026-11-16 on 4 new `DerivedBaseColumnSecurityExceptions` entries (invoice matter/project, approval invoice, submission invoice; evaluator + CoreAncestorResolver read as the BFF app user, Writers profile read 4; users read via Readers). SignalPredicateTests derived-base loop fails on dev until PR #2865 merges (list diff reviewed OK 2026-11-16; no import until owner go). Dev BFF deploy owner. Dev BFF = `be0420d763` (master `16771d1051` + mcp spike-v2, ~22:21Z 2026-11-16; contains 195 `04f5c943f9` template render; uac-r2 30-min error gate running). Owner retry of AI draft owed.

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, DI-148 #2671, DI-149 #2672, DI-154 #2813, DI-155 #2814; `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master. 176/160 run findings (Graph sentitems 400 after sendMail, `sprk_pendingspend` PATCH 400, `sprk_regardingreportcard` query on thread): check whether filed; detail archived in `notes/handoff-history/2026-11.md`.

**Worktree cleanup** (`scripts/worktree/Remove-WorktreeSafe.ps1` only): task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` after #2580/#2579 merge. `C:\wtdep185`, `C:\tmp\rv-084h1`, `C:\tmp\rev2587-850451d` removed 2026-11-15.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA. Console auto-deploys from master (CLAUDE.md §6). Code pages/PCFs: ours, after owner go, from master (D-126).
- Not on dev (nothing to update): `sprk_communicationpage` (CommunicationPage), `sprk_rulereviewpage.html`.
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`.
