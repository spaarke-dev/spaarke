# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–99) and `notes/decisions.md`. Task 185 decisions: `notes/185-invite-code-design.md` §11–§12. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-14 (checkpoint #34).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path (security counts); file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | UAC reviews of ontology PR #1878 (task 042, DR for the comms rule gate). Waiting on ontology's fix SHA for #1874 (145 F1: early-return policies skip the stored-evidence redaction). |
| **Next Action** | Re-check #1874's fix diff when it arrives; reply on #1878. Then pick the next uac-r2 item from "Not started" with the owner (wrap-up mode). Re-run 187's document leg once SPA-r3's 064 doc-type fix is on master. |

## Done this session
- **185** (#1827, `6da771b96`): merged, deployed and the live gate passed. The SpaarkeMaster 1.2.3.0 release gate is met. The live CIAM bind is on the owner checklist §12.
- **187** (#1861, `8a1f3bb11`, security #1857): merged, deployed and the live gate passed (G4 is on the owner checklist §13). Owner rounds 99, 100 and 100b.
- **The secure family is its own project:** `projects/uac-secure-project-r3/` (pre-design), which starts after uac-r2. Task 178 moves there. The team type is decided as an owner team per family (round 100b).
- **K-145-3:** comment PR #1875 (base docs/ontology-platform-design) is open; ontology merges it.

## Other lanes
- **Ontology:**
  - CLEAN and merged or merging: #1854 (143), #1858 (148: report-only first, and UAC reviews the dev run before the owner enables writes), #1862 (144).
  - #1874 (145): 1 F open. #1878 (042): review running.
  - Task 183 (#1797) closes after ontology's 148 live gate (412 → Conflict).
- **Provisioning:** 185's release gate is MET. SpaarkeMaster 1.2.3.0 is published (CI run 38603706393, master `bd52b6290`, PR #1860) and carries `contact.sprk_externalinvitecodehash`, secured: Writers 4/4/4, Readers no row. T240d builds on `6da771b96`.
- **SPA-r3:** has master merged into R3. 046 (WorkforcePrincipalResolver and the binder), 047, 048 (systemuser home-link columns, owner-approved schema) and 058 come to UAC for review.
- **word-add-in:** the owner is UATing add-ins on dev at `6da771b96`. Their #1815 changes `Set-ExternalFlagForB2BGuests.ps1`. UAC's "no -Apply in Model 1" rule stays until the owner restates it.
- **Task 186 (baseline access only):** pending SPA-r3's principal contract. Tell provisioning when the Workforce role is final.
- **SPA-r3 064 doc-type fix** (`ExternalDataService.cs:142`, `sprk_documenttype` read as a string, which empties external document lists on dev) is on the R3 branch only. They were asked to land it on master.
- **Issues filed this period:** #1813, #1817, #1830 (triaged), #1857 (task 187, fixed).

## Open owner items
- The live CIAM sign-ins: checklist §12 (185's code-gated bind) and §11 (184's ExistingAccount option). §13: 187's optional live secure-wall proof.
- Ontology owner questions: #1876 (unfiled communication gate DR), #1877 (D-37 tie), #1871; and enabling 148's writes after its report-only dev run.
- Restate the Model 1 `-Apply` rule once #1815 merges.
- Task 186 ceiling decision.
- #1747 and #1776.
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
