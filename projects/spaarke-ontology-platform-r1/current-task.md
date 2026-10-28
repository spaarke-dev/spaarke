# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-28 ~19:00Z (main session, context-handoff). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-192). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-131**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT round 2** (owner, 2026-10-28; record: `notes/uat-v4-log.md` "Round 2 feedback"). Item 1 ✅ → 165 closed. Items 2–6 → **D-193..D-196** → tasks **167** budget (BUD- autonumber [schema approved], BU currency into both fields, category/year/dates/period), **168** Assign Task notify (after 167), **169** lane subtitles + collapse, **170** commit latency. **D-197** (UAC round 137): seeded policies are owned by the customer-BU restricted-content team in customer environments only; dev is unchanged; the admin-tab author wins → **171** (blocked on uac-r2 R4b-2c). |
| **Status** | **PR #2402 (169)** head `d66c7eb06a`: Sonnet review found 0 product defects and 5 test findings, all fixed (forced-hidden proof: 61 fail → revert 256 pass); conflict-check clean; **scoped re-check running**. **PR #2403 (167)** head `3c6b316899`: lane done (2606 filtered pass, ArchTests 1593, +12 KB). Deviations: currency is a Choice of currency ids, refusals 422, no budget alt key. **Opus review running**. **Lane 170** (`C:\wts-170`, opus) still running. **uac-r2 194b:** head `129a2225e3` re-checked: F1/F3/F4 hold; new F2 (ordinary pass Incomplete when the restricted team is unresolved), F3 tests a–e, and a live LIKE proof (report-only on dev) are required before `OrdinaryMovesWritesEnabled`. uac-r2 runs them in R4b-2c. Round 136 moves that team to the customer BU. #2382 fixed (#2394, dev roles live), DI-130 closed, role-export hold lifted. Dev: BFF `e56a3a51d4` (smoke PASS), Console `b445886fed`. |
| **Next Action** | (1) 169: when the re-check is clean and CI is green → `gh pr merge 2402 --merge`. (2) 167: Opus review → fix round (resume lane or new implementer) → re-check → CI → merge. Then apply live: Web API PUT `sprk_budgetnumber` AutoNumberFormat `BUD-{SEQNUM:6}` + PublishXml sprk_budget only (owner-approved D-193). Then launch 168 (opus implementer, `C:\wts-168`). (3) 170: review when its PR lands (Opus; uac-r2 review if `CallerRecordAccessProbe` changes). (4) docs→master PR once 167/169/170 are merged: Console auto-deploys; the BFF needs R3 to deploy, with notices to word-add-in, R3 and uac-r2. (5) uac-r2: re-check R4b-2c fix head plus the LIKE proof. (6) Owner: OK to delete zz row `57e37569-…` (sprk_workassignment); re-check project create; check 6 composer. Then close 114, 158–163, 166. |

## Owner browser checks (sent 2026-10-23; zz- test data, delete afterwards)

1. Worklist loads for a Console User. Rows show policy short name, work type and severity; no error banner (163, D-185).
2. Wizard Person and Lookup fields open the side pane; the *What was found* record opens in a modal above the wizard (158).
3. Assign Task on a no-budget item creates a Task event (159).
4. A no-budget matter offers Create budget (161; D-192 v3 for the no-budget items).
5. Uploads to a WA, an invoice and a project show on that record's Documents (162).
6. Budget inquiry composer, with To pre-filled. An internal recipient gets the app link and the matter in the footer. An external contact gets the portal link and the subject in the footer (160 + #2314).

## Next, in order

1. Owner re-check → close 114, 158–163, 165 and 166, or fix (see Next Action).
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

- BFF **`e56a3a51d4`** (R3 deploy 2026-10-28, DLL read back 14:11:58Z; OrdinaryMovesWritesEnabled off; ontology decide-lane smoke after R3's after-notice). Previous: `de38296499`. Console **`b445886fed`** (run 40704903667, 165 one dialog).
- **R3 deploy hold (2026-10-27):** #2360 (contact identity hardening) merged to master as `078f62bcad`. Until the contact security schema is applied and `-Verify` passes, no BFF built from master at or after that commit goes to dev. R3 sequences this with the owner. #2360 touches no ontology-owned file.
- **Ontology OK sent to R3 (2026-10-28) for dev BFF `e56a3a51d4`** (#2357–#2386 over `de38296499`, contains R3 077, no ontology-owned or ownership files). The only change that reaches us is fm-037: a work assignment's `sprk_assignedto` is copied from `sprk_assignedtointernal` (Assign Work). Conditions: OrdinaryMovesWritesEnabled stays off; #2360's schema plus `-Verify` come first; the owner's go names the SHA in R3's session. After R3's after-notice, run a decide-lane smoke (plan route plus one zz- Assign Work commit).
- Worklist row `…5010` is in sync. POL-MATTER-NOBUDGET's current version is **v3 `…6313`** (D-192, live 2026-10-28; re-evaluation re-raised 54 items on v3).
- Console User (root `51c924e2-…`): 19 privileges, no policy Reads; AppendTo on policy and policyversion remains (outside D-185, left).
- Worktrees: none for lanes. R2 is at `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`. Remove a worktree only with `Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
