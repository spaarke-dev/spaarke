# Current Task State

> **Last Updated**: 2026-10-22, later (checkpoint: W3-8 running; #2255 applied on dev; the owner's decisions on #2235 and the stamp storage)
> **Format**: CURRENT state only. Rewrite each checkpoint; target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave W3-8 RUNNING: 081 (branch task/ebill-081-procedure-service) and 090 (branch task/ebill-090-file-store), each a background opus/high task-execute agent in its own worktree, based on 162369cdf5. Model FINAL (EB-D-148); draft PR #1911. |
| **Status** | 080, 088, 091, 099 and 119 ✅. #2255 soft delete (30 days) merged (b6edd748d8) and applied on dev. The combined tip passed: 5 projects build, unit 8563 and Arch 1349. work/ebilling-r1 is pushed. |
| **Next Action** | (1) When the 081 agent reports, send its range to uac-r2 (and to ontology-r1 if it touches their paths). Merge it FIRST. Run the broad tests and build all 5 projects. Push. (2) When the 090 agent reports, rebase it onto the 081 merge, then send it to uac-r2 and point them at the sizebytes test. Merge, run the tests, push. (3) Create a new task (task-create, main session) for the #2235 fix: field-secure sprk_invoicesubmission.sprk_findingsjson (BFF read/write only; this also closes DEF-029) and take it off the main form; Decision Records store the generic Duplicate sentence. Includes a schema run with notices. (4) Later: merge #1911 to master (check #2184 first), then tell customer-provisioning and R3 (#1829 closes). |

### Lanes
| Task | State | Waiting on |
|---|---|---|
| 081 Procedure service | Agent running. Merges first; it shares uac's creator-person files with 090 | Agent report |
| 090 File store service | Agent running. Its POML carries: the checked sizebytes conversion (refuse > int.MaxValue), one canonical source-version string, and downloads only on a clean scan time no earlier than Last-Modified (091 condition 3) | Agent report; rebase after 081 |
| O-14 prototype | First cut in spaarke-prototype/projects/ebilling.r1 | 084/089 correct its assumed DTOs |

### Owner decisions 2026-10-22 (later; do not ask again)
- #2235: FIX (approach above; a new task after 090).
- #2255: 30-day blob soft delete in all environments including dev. Done on dev (091 §8.8).
- Stamp storage: a DEDICATED eBilling storage account per customer stamp. customer-provisioning wires it in #2232 after #1911: the stamp creates the account, and the module is the sole owner of its blobServices. Recorded on #2232.
- The owner deleted `C:\p119`.

### Filed this session (open)
- #2237 (uac-r2 owns; DEF-036): DocumentProfileOutputMapper writes app-only without the refusal. Whichever of 088 and #2237 merges second adds sprk_restrictedbysubmission to the filtered set.
- #2242, #2259 and #2263: all closed by 050b, which deletes InvoiceExtractionJobHandler, the only caller of OutputOrchestratorService and DataverseUpdateHandler. The 050b POML notes the overlap with UAC #2258 and the guard exemption to drop.
- #1829 (R3 DEF-004): built by 051; R3 relabels it and closes it when #1911 merges.
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
