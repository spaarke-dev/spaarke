# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–100b) and `notes/decisions.md`. Task 185 decisions: `notes/185-invite-code-design.md` §11–§12. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-14 (checkpoint #34).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path (security counts); file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | Nothing. The ontology reviews are done: #1874 (145) CLEAN at `75114b170`, #1878 (042) CLEAN at `72fe75054`. Ontology merges them. Their D-139 follow-up PR (one DR per gate decision, written by an ADR-004 job) comes to UAC for review. |
| **Next Action** | 136/037/039 (+135 gate) wait on the owner's combined CIAM session, checklist §14 (the agent does the BFF grants on request). Workforce half PASSED 2026-10-14 (notes/task-136 §12). Then pick the next uac-r2 item with the owner: 036, 099, 090 wrap-up, 013/058 bookkeeping. |

## Done this session
- **185** (#1827, `6da771b96`): merged, deployed and the live gate passed. The SpaarkeMaster 1.2.3.0 release gate is met. The live CIAM bind is on the owner checklist §12.
- **187** (#1861, `8a1f3bb11`, security #1857): merged, deployed and the live gate passed (G4 is on the owner checklist §13). Owner rounds 99, 100 and 100b.
- **The secure family is its own project:** `projects/uac-secure-project-r3/` (pre-design), which starts after uac-r2. Task 178 moves there. The team type is decided as an owner team per family (round 100b).
- **K-145-3:** comment PR #1875 (base docs/ontology-platform-design) is open; ontology merges it.

## Other lanes
- **Ontology:**
  - CLEAN and merged or merging: #1854 (143), #1858 (148: report-only first, and UAC reviews the dev run before the owner enables writes), #1862 (144).
  - #1874 (145) CLEAN at `75114b170`; #1878 (042) CLEAN at `72fe75054`. #1871 closed (not planned).
  - Task 183 (#1797) closes after ontology's 148 live gate (412 → Conflict).
- **Provisioning:** 185's release gate is MET. SpaarkeMaster 1.2.3.0 is published (CI run 38603706393, master `bd52b6290`, PR #1860) and carries `contact.sprk_externalinvitecodehash`, secured: Writers 4/4/4, Readers no row. T240d builds on `6da771b96`.
- **SPA-r3:** has master merged into R3. 046 (WorkforcePrincipalResolver and the binder), 047, 048 (systemuser home-link columns, owner-approved schema) and 058 come to UAC for review.
- **word-add-in:** the owner is UATing add-ins on dev at `8a1f3bb11`. Their #1815 changes `Set-ExternalFlagForB2BGuests.ps1`. UAC's "no -Apply in Model 1" rule stays until the owner restates it.
- **Task 186 (baseline access only):** pending SPA-r3's principal contract. Tell provisioning when the Workforce role is final.
- **D-187-G1 (doc-type) is fixed**: on master as `c3445d45e` (#1880) and on dev at `4e23edeb2`. The 187 document leg re-ran and PASSED (15 lists now complete; no over-hiding).  #1886 filed (file-less documents listed, then 409).
- **Issues filed this period:** #1813, #1817, #1830 (triaged), #1857 (task 187, fixed).

## Open owner items
- The live CIAM sign-ins: checklist §12 (185's code-gated bind) and §11 (184's ExistingAccount option). §13: 187's optional live secure-wall proof.
- Ontology owner questions: #1876 (unfiled communication gate DR), #1877 (D-37 tie), DI-81 #1879 (column audit); and enabling 148's writes after its report-only dev run.
- Restate the Model 1 `-Apply` rule once #1815 merges.
- Task 186 ceiling decision.
- Checklist §14 (the combined CIAM session closes 135's gate, 136, 037 and 039). #1889: pre-marker secure containers are refused by SpeContainerOwnershipGuard (a provisioning fix, or a one-off marker write needing owner approval).
- #1747 and #1776.
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.

## Not started
136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping. (178 moved to uac-secure-project-r3.)
