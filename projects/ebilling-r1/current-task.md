# Current Task State

> **Last Updated**: 2026-10-22 (checkpoint after 088 dev run, 119 and 088 merged, 091 closed)
> **Format**: CURRENT state only. Rewrite each checkpoint; target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave W3-8 next: 081 and 090 (prereqs 080, 088 and 099 are all done). Model FINAL (EB-D-148); draft PR #1911. |
| **Status** | 080 ✅, 088 ✅ (on dev, merged 7bb18eca5c), 091 ✅ (all 3 uac-r2 conditions met live), 099 ✅, 119 ✅ (merged 4a43d9a223). The combined-tip build and broad tests after the 088 merge were running at this checkpoint. |
| **Next Action** | (1) When the combined-tip tests pass: push work/ebilling-r1. If they fail: fix before pushing. (2) Launch W3-8: 081 and 090 via task-execute, each in its own worktree on opus/high, based on the pushed tip. Name in the briefs: 088's retype (sourceversion String 20, sizebytes Integer, checked conversion), the 090 constraints already in its POML, and 094's 409 pre-check note. (3) Later: #1911 merge to master (check #2184 first), then tell customer-provisioning. |

### Lanes
| Task | State | Waiting on |
|---|---|---|
| 081 Procedure service | Open, next | Launch after the push |
| 090 File store service | Open, next. Its POML carries: the checked sizebytes conversion (refuse > int.MaxValue), one canonical source-version string, and downloads only on a clean scan time no earlier than Last-Modified (091 condition 3) | Launch after the push |
| O-14 prototype | First cut in spaarke-prototype/projects/ebilling.r1 | 084/089 correct its assumed DTOs |

### Open owner decisions (asked or pending)
- #2235 (DEF-035): native surfaces still show Duplicate detail (form binding, reject and override/return Decision Records). Fix or known limit, now that 119 is merged.
- #2255 (DEF-038): file store has no blob soft delete or versioning. Retention period, and whether dev gets it.
- `C:\p119` (2.4 GB leftover publish output): the owner said delete it. Tools block deleting a folder directly under `C:\`; the owner deletes it in Explorer.

### Filed this session (open)
- #2237 (uac-r2 owns; DEF-036): DocumentProfileOutputMapper writes app-only without the refusal. Whichever of 088 and #2237 merges second adds sprk_restrictedbysubmission to the filtered set.
- #2242: closed by 050b (deletes InvoiceExtractionJobHandler); 050b drops uac's guard exemption if #2239 is merged.
- #2254 (DEF-037): /review accepts a submission planted by a user-writable sprk_invoice link; fix: InvoiceIdFor(submission.Id) == invoiceId.

### Owner decisions 2026-10-21/22 (do not ask again)
- 080 re-run yes. AddedFee key by fee kind (and exact code set for `other`). 091 option A. O-14 prototype started.
- Dev file store: new dedicated account; scanning "not enabled" on dev. P-3: not in R1. Matter spend stays visible to flagged-external users.
- 088 BigInt columns retyped (2026-10-22): sourceversion String(20), sizebytes Integer. The two empty columns were deleted on dev with the owner's OK.
- Earlier: dev schema runs of 080/115/088 after ontology PASS and notices; no Key Vault changes; 096 one release after word-add-in-r1's; 095 from-email shapes frozen.

### Agreed with peers
- uac-r2 delete design §15 (53569f3581): the planner keeps any row a Restrict relationship references from outside the plan ("kept: source of an invoice file"). 117 relies on it and needs no executor change.
- 095: the submission's own Restricted on the .eml is a NON-provisional own narrowing (not via ApplyEmailCommunicationOutcome's unidentified branch); must-fire test.
- 086: no sprk_storedfile privilege for External Collaborator or flagged-external users (UAC 191 records the same).
- 088's reader pre-flight checks 6 named business units (-ReaderBusinessUnits), never every default team.
- Later-task findings from ontology's 088 gate are in the POMLs: 094 (409 pre-check counts Stored File sources), 097 (Superseded is not an Active prior), 117, 150 (sizebytes cap), 097/098 (Restrict refusal).
- customer-provisioning: #2232 sequenced after the module reaches master. **Tell them when #1911 merges.**
- R3: OK for any dev BFF deploy whose changes don't touch finance, ebilling or sprk_invoice*. Dev runs master 1283c41817, which still lacks e0df773395 (015), so the sprk_invoicestatus delete stays blocked.
- 157 is on master (aa3dc49897). Master ArchTests timeout is now 10 min (#2252).

### Standing rules (unchanged)
- Worktrees: agents work in their own worktree; remove only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- After merges: build all 5 test projects and run the broad filter on the combined tip before pushing or sending ranges.
- Deploys: Import-SolutionScoped.ps1 only; dev BFF and SPA only via SPA-r3.
- Live writes and role changes need the owner's OK; schema runs need notices to ontology-r1 and uac-r2, before and after.
- Gates: no line upload route before #2047; 100 and 105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
- Dataverse BigInt columns can't be field-secured (memory note).
