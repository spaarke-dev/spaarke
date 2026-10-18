# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint, never prepend to it, and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** the round log `notes/session27-owner-decisions-and-research.md`, now at **round 102 (2026-10-18), items 1–10**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete inventory:** `notes/core-record-delete-inventory.md`.
> - **Narrative:** the checkpoint commit messages.

> **Last Updated**: 2026-10-18 (checkpoint #47).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push to release. Run the fix-round loop, then merge when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green, then deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | Choose the most technically correct, best-practice, code-efficient fix. Never take the quick route. Add no complexity without value. Every defect found is fixed or filed. |
| **Order** | 186 → 099b → 036 (each merges after the owner items below), then #2084 (after 099b), then the #2091 thread-join follow-up (after ontology 042b merges), then the 090 wrap-up and the UAT checklist. |

## OWNER items outstanding (asked 2026-10-18; do not act without an explicit answer)
1. **036 ADR conflict (auth)**: ADR-028 says "no OBO on the collaboration path", but the external-write confirmation uses an OBO probe. Answer **C / B / A**. Recommended **C**: use the app-only named-principal confirmation for every systemuser and drop OBO there. 036 is held until the owner answers.
2. **Role changes on dev**: the SRO role needs `prvReadsprk_ExternalRecordAccess` at Basic (script `Set-SecureRecordOwnerRolePrivileges.ps1 -Apply` then `-Verify`). The Workforce role (root `9deaac9c…`) needs sprk_servicerequest C/R/AppendTo/Delete at Basic plus sprk_document Create. The owner asked whether UAC can apply these. UAC applies them ONLY on an explicit "apply both role changes on dev".
3. **Model 1 `Set-ExternalFlagForB2BGuests.ps1 -Apply`**: NOT a go yet. Ask for the customer tenant id(s), run a dry-run list, then get an explicit "go".
4. **Core-record delete decisions 1–8**: see `notes/core-record-delete-inventory.md`. Recommendations:
   - a BFF delete operation with a preview, not cascades;
   - keep Decision Records; delete Signals;
   - block the delete while finance rows exist;
   - keep grants and the ledger;
   - unlink rows that are also filed elsewhere;
   - a named role only; the AI tool refuses;
   - delete SPE files, never containers;
   - delete attachments with their communication.
   Also, without needing a decision: commit `sprk_policy_matter` Cascade to the solution XML.
5. **036 flag**: the owner confirms the dev flip directly in R3's session after 036 deploys.

## Open PRs (UAC)
| PR | Issue | Worktree / branch | State |
|---|---|---|---|
| **#2078** (draft) | #1074, #1567 (036) | C:/wt036 `feat/uac-036-fr20-flag-gated-swap` @ **e7a8392bb** (pushed; master merged at 953fd6446) | Owner decisions (a) and (f) are built.<br>- **(a)**: a WorkforceHome-linked systemuser is confirmed app-only, naming the principal.<br>- **(f)**: `$orderby=modifiedon desc`, CacheVersion 2.<br>**Adversarial pass 1: 0 F.** Its K-items, to fix in the next round (batched with the owner's ADR answer):<br>- add a Verify bullet (vi) to guide §12.8 for a WorkforceHome live check;<br>- soften the guide's "exactly what the model-driven app allows" (team-inherited roles are an open question);<br>- add a 15 s linked timeout on the app-only calls;<br>- update the probe comments at :11 and :192-196.<br>The ADR edits are drafted in `notes/036-adr-edits-owner-a-f.md`. They are **NOT applied**: hold them until the owner's C/B/A answer. B1 wording note: the app-only calls DO send MSCRMCallerID, so "impersonating" is accurate. Pass 2 runs after that round. |
| #2063 (099b) | #1010 D-11 half | C:/wt099b @ a74a7d159 | Review complete. Waiting on the SRO role change (owner item 2). Then: merge, R3 deploy, backfill -Apply, VERIFY, live gate, close #1010. |
| #2061 (186, draft) | task 186 | C:/wt186 (master merged at 8d10c1155) | Review complete. Waiting on the Workforce role edit (owner item 2) and the owner's sign-off. Then: mark it ready and merge FIRST. |

## Merged 2026-10-18
- **#2094 `1473b34c6`** (#2091, part): the playbook update node, DataverseUpdateHandler and the field-mapping push now call `SecureChildReconciler.AfterRefileAsync`.
  - **Parent changes:** bind-string parent changes are handled in `RecordReparent.ParentChangesWithClearsIn`, with fail-closed bind validation.
  - **Predicates and reads:** the shared predicate is `RecordReparent.MayRefile`, and its map is `FilingParentOf`. `RefileIsolationRead` (one retry) is used by all 4 writers. `ParentsUnchanged` skips no-op re-files.
  - **Push:** the push re-files per child; a move out of Secure needs F3 rights.
  - **Node:** the entity set is resolved by metadata.
  - **Guard:** the IL guard is `RefileWriterAfterRefileGuardTests`. Its baseline is ThreadResolver, CommunicationService and InvoiceReviewService.
  - **Reviews:** 2 adversarial passes, 5 rounds.
  - **Deploy:** R3 has been told to include it in the next deploy. **Next: the live gate after the deploy.**
  - **Still open on #2091:** the thread join and reply-into-thread. This needs an `AfterRefileAsync` option to skip `ReconcileThreadForFilingAsync`, which must be added AFTER ontology 042b merges (it restructures AfterRefileAsync into RefileStepsAsync). Also fix SecureChildReconciler's stale writer list then.
- Earlier merges and gates: #2083 (gate PASS, closed), #2074, #2062, #2035, #2025 and earlier. Details are in the git log and the batch6/7 gate notes.

## Filed today (tracked)
- **#2091**: remaining ThreadResolver/CommunicationService wiring (UAC).
- **#2095**: InvoiceReviewService document refile skips the hook (ebilling).
- **#2097**: OutputOrchestrator's EntityReference/Money values are unwritable (orchestrator owner).
- **#2098**: the push costs about 17 calls per child, and a 500-child push can exceed 230 s (queue it).
- **#2087**: commented that the UAC part was already fixed by #2083.

## Peers (state)
- **R3**: deploying the dev BFF from master (20e7b71a1; ontology 102b + #2043). UAC said OK. UAC asked R3 to include #2094 next and to send an after-notice.
- **Ontology**:
  - **042b (#1876, PR #2096 → docs branch, head 0846c2b8d):** UAC-owned files were verified unchanged since the accepted round-2 diffs. It has the required GateDecisionRecordSharing dependency, the AfterRefileAsync hook for communications, and the Secure end-to-end test. Owner: DRs are internal-only (round 102 item 10). Known limits: #2092 and #2093.
  - **#2084:** the plan is agreed. UAC builds it after 099b merges, in one PR:
    - SecureChildLineage `sprk_policy` (via sprk_matter) and `sprk_policyversion` (via sprk_policy);
    - SRO Read on both tables (an owner role change);
    - RecordOwnershipResolver treats them as reparentable;
    - OntologyAdminAuthoringService :1154 `VersionPayload` binds `ownerid@odata.bind` from the resolver, failing closed via `Failure.Unavailable`, leaving :969/:989 untouched;
    - tests: must-fire, must-not-fire and resolver failure.
    Ontology runs 2 adversarial passes on it. The code-uniqueness oracle is an accepted known limit.
- **ebilling**:
  - **015:** re-check PASS.
  - **D-112:** a secure-root-set subset rule, with 4 conditions on `SecureRootsAboveAsync` (in their 072 POML).
  - **#2081 invoice part:** rounds 1–3 PASS (update-only impersonated overload plus wire tests). #2090 filed (Optimistic mode is a no-op).
  - **051:** rounds 2–3 PASS, and UAC's shared-reader change a93d97c49 is accepted.
  - **On their next master merge**, they resolve the FieldMappingEndpoints conflict with #2094: their guard first, then `UpdateExistingAsync` for invoice tables, then the gone code. UAC re-checks that hunk.
  - **Still open on #2081:** the other tables, the 4 invoice-family tables, InvoiceReviewService:716 and OwnedChildWrite:421/445.
- **Provisioning**: #2027 code is done (c28d9dc48, retention -1 plus read-back). Dev is not yet set: they're asking the owner directly. UAC does a read-only check after that.

## Remaining wrap-up 090
- 058 docs.
- Keep #1059, #1063 and #1077 open until their owner gates. Keep #961 until 099, and #1074/#1567 until 036. #1014 and #1564 are PARTIAL.
- The UAT checklist by user type. Include 099c Expiration, the #2076 note, and the **CIAM external user UAT, which the owner reported PASSED (round 102 item 9)**.
