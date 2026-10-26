# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-23 ~12:10Z (main session, context-handoff). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-190). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-128**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | UAT round 1, live on dev. Dev BFF is **`58ffc37da3`**: deployed by R3 at 11:18:12Z, verified by DLL ProductVersion, healthz 200. It carries every UAT-1 task: 158, 159, 161, 162, 163, 164, and 160 + #2314. The D-185 dev steps are **done**: worklist config 11:20Z (verified in sync), POL-MATTER-NOBUDGET v2 11:20:38Z (verified), and Console User policy Reads removed 11:21:51–53Z (21 → 19). uac-r2 independently confirmed the read-back; their server gate is **PASS** (11:18–11:51Z). Basic User needed no change (D-189). |
| **Status** | No lane running; no lane worktrees. Waiting on: (1) the **owner's browser checks** (list below); (2) ebilling's #1911 F1 merge SHA; (3) R3's next candidate `7479ce22a9` (uac-r2 census only; ontology OK sent), which needs the owner's approval in R3's session. |
| **Next Action** | On the owner's browser-check results: fix any failure (a lane per defect), or close 114, 160 and 163 (✅ + POML + TASK-INDEX; run the drift check). Then open the next docs→master PR (D-185 guard #2316 plus docs; test-only). |

## Owner browser checks (sent 2026-10-23 ~12:00Z; zz- test data, delete afterwards)

1. Worklist loads for a Console User. Rows show policy short name, work type and severity; no error banner (163, D-185).
2. Wizard Person and Lookup fields open the side pane; the *What was found* record opens in a modal above the wizard (158).
3. Assign Task on a no-budget item creates a Task event (159).
4. A no-budget matter offers Create budget (161).
5. Uploads to a WA, an invoice and a project show on that record's Documents (162).
6. Budget inquiry composer, with To pre-filled. An internal recipient gets the app link and the matter in the footer. An external contact gets the portal link and the subject in the footer (160 + #2314).

## Next, in order

1. Owner checks → close 114, 160 and 163, or fix.
2. **Next docs→master PR:** the D-185 guard `PolicyReadRoleGuardTests` (#2316, docs `20d7cbd47c`) plus today's docs. Merge master first; run the full ArchTests; send before and after notices if it touches the Console or BFF (it doesn't today).
3. **ebilling #1911:** when they send the F1 merge SHA, check `git diff origin/master <sha> -- src/server/api/Sprk.Bff.Api/Services/Signals/EarlierInvoiceReadability.cs` against the #2294 round we passed. The Core User conflict resolution keeps both lines. The owner decides these, via ebilling:
   - K1: Billing Administrator policy Read goes to Deep (uac-r2);
   - K2: Core User CRUD on BFF-only invoice tables. uac-r2 recommends Read + AppendTo only.
4. R2 §9 owner answers → `/design-to-spec`; 105 UAT; #1882 live re-run; 111 row 4; 090 wrap-up; the `[SIGNAL-EVALUATOR]` heartbeat.

## Open owner questions

- The browser checks above.
- Approve `7479ce22a9` in R3's session (uac-r2 census; optional).
- The owner is removing `C:\wb160p` by hand (said yes).

## Lane facts to carry

- **160 rules:**
  - Link by recipient (D-187): an enabled systemuser gets the app link, even when flagged external; anyone else gets the portal link.
  - The footer label is used ONLY when the matter is exactly Standard and every recipient is an enabled systemuser with `IsExternalAsync` false. Otherwise the subject is used (#2314).
  - Known limits: `IsExternalAsync` is cached for 10 minutes; Sent Items reveals whether an address is an enabled user (K2, accepted); a null access permission gets no label.
- **D-188:** the document parent link uses the unprefixed lookups (`sprk_matter` etc.). DI-127 / #2315 fixes `DocumentLinkFieldMap`'s "superseded" claim.
- **D-185 is enforced by** `tests/Spaarke.ArchTests/PolicyReadRoleGuardTests.cs` (on docs; not yet on master). uac-r2 extends its census to the codified child tables (in `7479ce22a9`).
- `RemovePrivilegeRole` payload: `{"Privilege":{"@odata.type":"Microsoft.Dynamics.CRM.privilege","privilegeid":…}}`. `PrivilegeId` is rejected.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft. Waits for `sprk_publishstate` on master.
- Five stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) and `DecisionRecordContextImmutabilityTests.CreateEntryPoints`. Wait for ebilling 074 on master.
- Close **#2212** when ebilling's `f0884a175f` reaches master. When ebilling 015 reaches master, update the `notes/102a-authoring-contract.md` residual row and D-161.
- The advisory ADR Compliance CI job is cancelled at about 5 minutes (#2009). It is not blocking.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `58ffc37da3`; Console `58ffc37da3` (run 39914343605).
- Worklist row `…5010` is in sync. POL-MATTER-NOBUDGET's current version is v2 `…6312`.
- Console User (root `51c924e2-…`): 19 privileges, no policy Reads; AppendTo on policy and policyversion remains (outside D-185, left).
- Worktrees: none for lanes. R2 is at `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`. Remove a worktree only with `Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
