# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint, never prepend to it, and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** the round log `notes/session27-owner-decisions-and-research.md`, now at **round 103 (2026-10-18)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete inventory:** `notes/core-record-delete-inventory.md`.
> - **Narrative:** the checkpoint commit messages.

> **Last Updated**: 2026-10-19 (checkpoint #50).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push to release. Run the fix-round loop, then merge when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green, then deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | Choose the most technically correct, best-practice, code-efficient fix. Never take the quick route. Add no complexity without value. Every defect found is fixed or filed. |
| **Order** | ~~186~~ → ~~099b~~ → ~~#2084~~ (947d98e31) → 036 (each merges after the owner items below), then #2084 (after 099b), then the #2091 thread-join follow-up (after ontology 042b merges), then the 090 wrap-up and the UAT checklist. |

## OWNER items outstanding (round 103, 2026-10-18)
1. **036 ADR conflict (auth): STILL OPEN.** The owner leans C and asked what the conflict is. It was explained plainly on 2026-10-18; the C / B / A answer is pending. 036 is held until then.
2. Role changes on dev: **DONE** (round 103 item 2). Other environments need the same edits at provisioning.
3. Model 1, ACME: the dry run found 0 changes, so nothing was applied (round 103 item 3).
4. Core-record delete: **APPROVED as D1–D8** (round 103 item 4). The design is `notes/core-record-delete-design.md` (4a353772b). D9 is already on master. **Owner Q1–Q5 are pending:** the named role A/B, SPE recycle bin vs permanent, empty dedicated containers, a DR for the delete, and Signals cited by kept DRs. Q6 (rate cards, eBilling tables) is with ebilling.
5. 036 flag: R3 is tasked and has acknowledged. It needs the merge sha, a deploy, and the owner's confirmation in R3's session.

## Open PRs (UAC)
| PR | Issue | Worktree / branch | State |
|---|---|---|---|
| **#2078** (draft) | #1074, #1567 (036) | C:/wt036 `feat/uac-036-fr20-flag-gated-swap` @ **e7a8392bb** (pushed; master merged at 953fd6446) | Owner decisions (a) and (f) are built.<br>- **(a)**: a WorkforceHome-linked systemuser is confirmed app-only, naming the principal.<br>- **(f)**: `$orderby=modifiedon desc`, CacheVersion 2.<br>**Adversarial pass 1: 0 F.** Its K-items, to fix in the next round (batched with the owner's ADR answer):<br>- add a Verify bullet (vi) to guide §12.8 for a WorkforceHome live check;<br>- soften the guide's "exactly what the model-driven app allows" (team-inherited roles are an open question);<br>- add a 15 s linked timeout on the app-only calls;<br>- update the probe comments at :11 and :192-196.<br>The ADR edits are drafted in `notes/036-adr-edits-owner-a-f.md`. They are **NOT applied**: hold them until the owner's C/B/A answer. B1 wording note: the app-only calls DO send MSCRMCallerID, so "impersonating" is accurate. Pass 2 runs after that round. |

## Merged 2026-10-18/19
- **#2102 (#2084) `947d98e31`**: matter-scoped rules and their versions join the secure family. Publish is refuse-before-write (409 `rule_held_secure`, 400 `scope_matter_secured` only when the requested matter is the sole cause, 503 `owner_unavailable`). One citation predicate, `(sprk_signal, sprk_policy)`. Ontology ran 2 passes plus 2 re-checks, and is CLEAN. **Next:** the owner approves SRO Read on policy/policyversion on dev (send ontology before and after notices), R3 deploys (BFF before or with the Console), then the live gate.
- **#2063 (099b) `d7eabba3b`**: merged after the union-resolved conflict with 186. R3 deploys it together with 186. **Next: after R3's after-notice, run `Backfill-RecordOwnership.ps1` -Apply, then VERIFY, then the 099b live gate, then close #1010.**
- **#2061 (186) `49279def1`**: the Workforce role on dev was edited. R3 includes it in the next deploy, after their task 058 agent finishes. **Next: the 186 live gate after R3's after-notice.**
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
- **#2101**: DELETE /api/v1/documents/{id} orphans the SPE file (found by the delete design).
- **#2100**: per-family Secure-team guard follow-up for task 178 (ebilling D-112 A1(b), K-i).
- **#2087**: commented that the UAC part was already fixed by #2083.

## Peers (state)
- **R3**: deploy candidate `f111de2b7` (live ad0e9e1a7 + 186 + 099b) has the UAC OK and waits on the owner. #2084 is to follow. The dev backfill dry run planned 101 grant rows (73 / 26 / 2 Secure).
- **R3 (036)**: the 036 flag flip is tasked and acknowledged. The next deploy includes 186, plus 099b if it has merged by then.
- **R3 (earlier)**: deploying the dev BFF from master (20e7b71a1; ontology 102b + #2043). UAC said OK. UAC asked R3 to include #2094 next and to send an after-notice.
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
  - **#2095 (InvoiceReviewService refile):** re-check of a69e703fc..5abdaf7fb found **F1**: the resume path stamps Confirmed before the isolation read. Sent back to them; waiting for the fix range. K1 is an optional CancellationToken.None test.
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
