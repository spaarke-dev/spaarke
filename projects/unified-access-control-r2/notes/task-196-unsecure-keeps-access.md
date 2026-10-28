# Task 196: Unsecure keeps all access (owner rounds 135 and 138)

## Rulings
- **Round 135:** making a record not Secure removes no access, in the MDA or the workforce app. It keeps every share on the record, the shares it passed down to filed work assignments and projects, the children's mirrored shares, and every contact/workforce grant. The owner change and the cleared flag still happen.
- **Round 138:** a later removal of a person from the now-ordinary record also removes what they got on its documents, events, to-dos and filed work assignments and projects through that share. Child access tracks the record's share list, so nothing stays hidden.

## Decisions taken under those rulings (main session, 2026-10-28)
| Map Q | Decision |
|---|---|
| Q2: records unsecured before 196 | The completion pass removes only mirror entries whose principal no longer holds a share on the root (the round-138 rule, applied retroactively). |
| Q3: re-secure after 196 | Kept shares become direct (`CoveredByExisting`). Accepted. |
| Q4: sharees' Read on hard-deny rows | Kept: same access as while Secure ("keep all access"). |
| Q5: Remove Secure confirmation and wizard copy | Drafted in the PR for owner review. Must say access is kept. |
| Q6: team shares invisible in Current Access | Existing UI limit; filed separately. |
| Q7 | = round 138 (follow the record). |
| Q8: a share to the Secure Record Owners team | Revoke if found, and log it. The team is memberless, so no person loses access. |
| Q9: document-level unsecure | Out of scope. |

**Flagged, accepted as consistent with round 135:**
- Collaborate/Full shares that existed only to reach isolated children stay.
- Sharees outside the BU keep access.
- A walled user kept as last reader keeps the share (No Access applies to secure records only).

## Plan
- **196a** (from master, after 193 merges):
  - delete Step 4's revoke sweep;
  - `KeepWhatAParentPassedOnAsync` ends provenance with the new reasons `kept-record-unsecured` / `kept-parent-unsecured`;
  - split `ParentMirrorAnswer.NotSecure` into `Unsecured` and `Gone`, so the job's net keeps on Unsecured and removes on Gone;
  - DTO fields kept at 0 / null;
  - client copy;
  - remove the guard entries (`PoaShareClientSingletonGuardTests:1690`, 193's declared entry);
  - docs.
- **196b** (after 194b merges):
  - Step 3.5 keeps the children's mirror on an Unsecure root pass, and reverts 194b's 3a';
  - round 138: an unshare on an ordinary root removes that principal's mirrors on its children and filed records. This needs a mirror-provenance record per kept mirror, or a recompute from the root's share list.
- Map with file:line: the Plan agent's report (2026-10-28), summarised here.
  - M1: Step 3.5 → `SecureChildShareSynchronizer.RemoveMirrorAsync`.
  - M2: `EndProvenanceForUnsecureAsync`.
  - M3: `RevokeAllSharesAsync`.
  - M4: `EndWhatAParentPassedOnAsync`.
  - M5: give-back.
  - M6: the job's net, at `GiveParentShareesAsync` (b) and `RecheckWrittenSharesAsync`.
  - M11: the Assigned-To Declined side effect, fixed by 196.
- Existing gap, filed separately (M12): the JIT Office-edit prune lists only `sprk_issecure = true` records, so JIT writer grants on an un-secured record's container are never pruned.
- Effort: about 5-6 dev-days. About 40 tests to touch (list in the map report).
