# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-23 ~12:10Z (main session, context-handoff). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-192). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-131**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | UAT round 1, live on dev. Dev BFF is **`58ffc37da3`**: deployed by R3 at 11:18:12Z, verified by DLL ProductVersion, healthz 200. It carries every UAT-1 task: 158, 159, 161, 162, 163, 164, and 160 + #2314. The D-185 dev steps are **done**: worklist config 11:20Z (verified in sync), POL-MATTER-NOBUDGET v2 11:20:38Z (verified), and Console User policy Reads removed 11:21:51–53Z (21 → 19). uac-r2 independently confirmed the read-back; their server gate is **PASS** (11:18–11:51Z). Basic User needed no change (D-189). |
| **Status** | No lane running; no lane worktrees. Waiting on: (1) the **owner's browser checks** (list below); (2) dev is on `de38296499` (R3; DLL-verified); master builds after `078f62bcad` are held until the contact security schema is applied (R3 + owner). **`OrdinaryMovesWritesEnabled` is on HOLD (2026-10-27).** uac-r2's first dev report planned to move 14 signals from their matter's BU1 team to the root BU. The cause, sent to uac-r2: `SecureChildReconciler.ContextFor` sets no primary parent, so the resolver takes the business unit of the first lookup in attribute order (the to-do). SignalWriter makes the core primary. A second gap: 27 signals with a Project or Work Assignment core have no typed lookup to the core. uac-r2 confirmed: 49 of the 52 planned moves were wrong, across every table. Fix **194b** (`fix/uac-194b-ownership-primary`): (A) `RecordOwnershipContext.ForExistingRow` with a fixed primary per table, matching each writer; (B) take a user-owned filed parent's business unit from that parent's own filing; (C) a time slice; a writer-parity test. Ontology's review points, sent: (1) (B) changes the owner of Signals with a Work Assignment core (22/27 WAs are user-owned), so SignalWriter's reconcile check (`EnsureOwnershipMatches` :629; `CoreRecordChanged` :621) will fire every night until the rows are re-owned; the PR lists those rows and plans the re-own, and the writer is not loosened. (2) Stamps are not columns on the row; re-derive them, and compare the full parent list. (3) No-core Signals: report only, since `TodoAssigneeOwnerReconciliationJob` owns them. 194b @ `3fc7ef2b4f` reviewed (2026-10-27):
- **F1:** a Decision Record's commit-time parents include the subject of every Signal it resolves. Those subjects are not all stored, because the Signal→DR link is skipped for Signals closed during the review. So add the subjects of the linked Signals, and never move a secure-owned DR to ordinary; report it instead.
- **F2:** (B) changes the create-time owner of no-core Signals and DRs whose subject is not a personal to-do. Count them; exclude them or re-own them; the writer is not loosened.

- **Slice 2 @ `21c86a7d20` (2026-10-27):** F1-d, F2 (13 no-core Signals and 3 no-core DRs; 0 answers change) and F1-c look right as described. uac-r2 asked whether the re-file pass should hold DRs too: it breaks task 144 criterion 3, so the implementer exempted it (`_refileChildPass`). **Ontology answered (c):**
  - Take the DR's Signal subjects from `sprk_steps.resolvedSignalIds`. That is the complete, immutable commit-time set: `DecisionRecordWriter.cs:1995`; `Memo` refuses rather than truncates; there is no update path.
  - Hold and report only when the set cannot be read: no key, a parse failure, a Signal missing or unreadable, or a subject table that can't be derived.
  - Drop the exemption. Criterion 3 is unchanged.
  - Parity point: `ResolvedSignalSubjectsAsync` adds every lookup on the Signal; the writer adds only the subject.
  - Fallback if (c) is out of scope: (a), with criterion 3 reworded to "DR held and reported". Never (b).

Next: re-check the head uac-r2 sends after (c) or (a) lands. Previous: dev BFF `3ff2c04bf5` has been live since 2026-10-27 01:29:15Z (R3 after-notice; DLL 1.0.0+3ff2c04bf5; 30-minute gate clean). |
| **Next Action** | On the owner's browser-check results: fix any failure (a lane per defect), or close 114, 160 and 163 (✅ + POML + TASK-INDEX; run the drift check). (The D-185 guard is on master: #2344 `0b035c23fd`.) |

## Owner browser checks (sent 2026-10-23 ~12:00Z; zz- test data, delete afterwards)

1. Worklist loads for a Console User. Rows show policy short name, work type and severity; no error banner (163, D-185).
2. Wizard Person and Lookup fields open the side pane; the *What was found* record opens in a modal above the wizard (158).
3. Assign Task on a no-budget item creates a Task event (159).
4. A no-budget matter offers Create budget (161).
5. Uploads to a WA, an invoice and a project show on that record's Documents (162).
6. Budget inquiry composer, with To pre-filled. An internal recipient gets the app link and the matter in the footer. An external contact gets the portal link and the subject in the footer (160 + #2314).

## Next, in order

1. Owner checks → close 114, 160 and 163, or fix.
2. **Docs→master:** done. PR #2344 merged as `0b035c23fd` on 2026-10-27 (D-185 guard `PolicyReadRoleGuardTests` + notes); the branch and main checkout are synced to master.
3. **ebilling #1911:** F1 merge `bed558cb33` checked 2026-10-26: PASS for ontology. `EarlierInvoiceReadability` = the #2294 round; the combined diff is empty on every ontology file; Core User drops `InvoiceLineItem` (ebilling 029). K1 and K2 were decided by the owner in ebilling's session (ebilling `notes/decisions-log.md`, `399813b8f7`): EB-D-150 keeps Global policy Read on Billing Administrator (a census exception in #2322); EB-D-151 keeps D-90's full Core User rights on the five invoice working tables, a known limit. Nothing ontology-side is open.
4. **ebilling 089** (`task/ebill-089-assignments` @ `9082a05a04`), reviewed 2026-10-27:
   - Agreed: the record-rule code `BRR-{policyId N}`, on condition the code is immutable; the census waiver made Permanent; the widened scope guard and the new census guard.
   - Ontology Administrator's Deep Read on Policy and PolicyVersion: no change. It is a business-unit placement dependency, as in §20.
   - Billing Administrator's Global Read on Policy and PolicyVersion: we recommend lowering it to Deep. That revisits EB-D-150 and is an owner decision in ebilling's session.
5. R2 §9 owner answers → `/design-to-spec`; 105 UAT; #1882 live re-run; 111 row 4; 090 wrap-up; the `[SIGNAL-EVALUATOR]` heartbeat.

## Open owner questions

- **Census finding, resolved (2026-10-26):** Deep Read on Signal, Decision Record and budget revision depends on the UAC business-unit restructure (§5.2), not on a role change. Security-roles §20. No ontology task.
- The browser checks above.
- The owner is removing `C:\wb160p` by hand (said yes).

## Lane facts to carry

- **160 rules:**
  - Link by recipient (D-187): an enabled systemuser gets the app link, even when flagged external; anyone else gets the portal link.
  - The footer label is used ONLY when the matter is exactly Standard and every recipient is an enabled systemuser with `IsExternalAsync` false. Otherwise the subject is used (#2314).
  - Known limits: `IsExternalAsync` is cached for 10 minutes; Sent Items reveals whether an address is an enabled user (K2, accepted); a null access permission gets no label.
- **D-188:** the document parent link uses the unprefixed lookups (`sprk_matter` etc.). DI-127 / #2315 fixes `DocumentLinkFieldMap`'s "superseded" claim.
- **D-185 is enforced by** `tests/Spaarke.ArchTests/PolicyReadRoleGuardTests.cs` (on master since #2344 `0b035c23fd`). uac-r2 extends its census to the codified child tables (in `7479ce22a9`).
- `RemovePrivilegeRole` payload: `{"Privilege":{"@odata.type":"Microsoft.Dynamics.CRM.privilege","privilegeid":…}}`. `PrivilegeId` is rejected.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft. Waits for `sprk_publishstate` on master.
- Five stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) and `DecisionRecordContextImmutabilityTests.CreateEntryPoints`. Wait for ebilling 074 on master.
- Close **#2212** when ebilling's `f0884a175f` reaches master. When ebilling 015 reaches master, update the `notes/102a-authoring-contract.md` residual row and D-161.
- The advisory ADR Compliance CI job is cancelled at about 5 minutes (#2009). It is not blocking.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF **`de38296499`** (UAC 194 #2349 + field-mapping #2331; R3 deploy, verified by DLL ProductVersion 2026-10-27). Previous: `3ff2c04bf5`. Console `58ffc37da3` (run 39914343605).
- **R3 deploy hold (2026-10-27):** #2360 (contact identity hardening) merged to master as `078f62bcad`. Until the contact security schema is applied and `-Verify` passes, no BFF built from master at or after that commit goes to dev. R3 sequences this with the owner. #2360 touches no ontology-owned file.
- Worklist row `…5010` is in sync. POL-MATTER-NOBUDGET's current version is v2 `…6312`.
- Console User (root `51c924e2-…`): 19 privileges, no policy Reads; AppendTo on policy and policyversion remains (outside D-185, left).
- Worktrees: none for lanes. R2 is at `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`. Remove a worktree only with `Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
