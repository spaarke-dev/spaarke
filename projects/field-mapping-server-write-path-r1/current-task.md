# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-22 (018b complete on `task/fm-018b1`; review fix round 1 merged; re-check + verify running)
> **Phase**: Wave 2. Done (13/43): 001–006, 010–016 on master; 018a on master (36403e1f4b).
> **Live on dev: 1283c41817** (R3, 2026-10-22 12:50Z; includes dormant 018a, UAC #2244/#2249/#2248). **Active: task 018,
> phase 018b** (the live Office Matter/Project swap).

## Quick Recovery

| Field | Value |
|---|---|
| **Active task** | 018 (Office quick-create adapter), run as 3 PRs under ONE POML: **018a** service prerequisites → **018b** Matter/Project adapter → **018c** delete the Office core. The Invoice leg is NOT moved (ebilling-r1, owner decision L; task 096 deletes it) |
| **HEAD** | work branch = master 36403e1f4b + checkpoint docs. 018b lives on **`task/fm-018b1`** (worktree `C:\code_files\spaarke\.claude\worktrees\agent-a550606fde68f707f`), tip **be6628b378**: inventory, G1–G7 + G4 decisions, 1s service members, 1b fixture double, 1c swap + re-pointed suites + new tests, 018b-2 (secure-matter project, flag, #2238), K2 (mapping drops app-only-refused targets; owner/creator sets from #2249), review fix round 1, master 1283c41817 |
| **Running** | (1) Full verify of be6628b378 (`scratchpad/v018b-fix1/`). (2) Scoped Sonnet re-check of fix round 1 (ebb6688ee0..a94f1929d9 + da8ee69e72) |
| **Next Action** | Both clean → notes for fix round 1 + K2 + 018b-2 → ping UAC-r2 for the condition (b) review (G4 conditions, K2, the `sprk_accesspermission` create exemption + its Repair test, no `TODO(#2249)`) → on ACCEPT: merge `task/fm-018b1` into the work branch, publish delta + CVE, PR body (cite decisions.md 2026-10-22 entries, ADR-008 path A, release notes) → merge on Router + Client lint green → close #1614/#1617 citing the merge SHA (POML: on merge of the Office move) → tell UAC-r2 the SHA (018b is LIVE: R3 deploy candidate) → 018c |
| **018 progress** | 018a ✅ master. 018b: all passes done; Opus review → 4 findings (F2-1 provisioning pass-through, F3-2 map coverage, F4-3 assignee shape, F2-4 "from chat") fixed in a94f1929d9. #2124: create path fixed; Repair half is a 052 criterion (comment on #2124). Release notes: Office warning/refusal texts follow the service; Project-under-Matter owner = the matter BU team (Secure team under a secure matter); fill-only mapping; Default rules not applied when the source read faults |
| **CI note** | The Tier 1 `Arch Tests (full suite)` job also hits its 5-min timeout (#2009; master 5 of the last 8 runs). Re-run the failed jobs; it is not a test failure. Owner decision on raising it, as with #2224 |
| **Cleanup left for the owner** | Empty, unregistered folders (Remove-WorktreeSafe partial delete, "Permission denied"): `C:\code_files\spaarke\.claude\worktrees\agent-a10e5fe383a4dfcd2`, `...\agent-a80d401ebbcfb9d3e` |

## 018 — what is decided (all in the 018 POML and notes/decisions.md)

- **Office mapping becomes fill-only** (owner Q6/Q10; NFR-07 pins only error codes). Four Office contract assertions
  that pin overwrite are rewritten. Release note.
- **Contract fixtures follow the new write** (one Web API dictionary via `UpdateRecordFieldsAsync`, 412 retry). What
  each test asserts stays the same; changed assertions are listed.
- **UAC-r2 condition (b) for 018b:**
  - an Assigned To contact the caller names is REFUSED without AppendTo, and the code names `assignedTo`;
  - no token gives 500 `caller_rights_unverifiable`;
  - the membership event fires exactly once (the endpoint publish is removed);
  - a project under a secure matter is owned by Secure Record Owners and is secure;
  - a table-driven refusal map keeps every Office status (OwnerUnresolved 403).
- **The maker's-contact Assigned To default is DROPPED, not refused** (UAC-r2 concurs): the adapter shows
  `default_value_dropped`, and nothing writes Assigned To app-only afterwards.
- **018a new surface:** `ReferenceLookupColumns` (registry allow-list `RecordTableRule.OptionalReferenceLookups`:
  matter type, practice area, project type; `IsNeverAReference` throws for the for-person column, protected, pair,
  filing, access-conferring and assigned* columns), `PostMappingDefaults` (fill-only, step 3½, checked at step 5),
  K1 test, K3 fix.
- **Optional for 018b:** #2238 (DI-006), case-insensitive de-dup of step-9 server binds.
- **018c:** delete `OfficeRecordCreationService`; re-point or retire 9 test files; census; owner-guard allow-list;
  the #2188 guard `KnownCallSites` 3 → 2; remove the legacy `CreateTimeFieldMapping.Apply`; close #1614/#1617.

## Blocked: 017

Re-pointing the route and the chat create needs ebilling #1911 (still a draft) plus its reconcile, and these owner items:
FR-06 step 10, #1608 columns, the chat number, SystemPrincipal.

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- Launch adversarial-reviewer passes from the main session (one Opus per change set; a scoped Sonnet re-check per fix
  round). Resume an implementer with SendMessage for its fix rounds.
- **After ANY master merge, run the full ArchTests AND the related suites.** Rebuild ArchTests first; a stale DLL gives
  false failures.
- **Never run the unfiltered `Sprk.Bff.Api.Tests`** in an agent (the host reaches ~8 GB). Use the filtered set in
  `scratchpad/v016/verify.sh`.
- Call `Remove-WorktreeSafe.ps1` through PowerShell. If the delete is partial it leaves an unregistered folder; report it.
- CI: the required checks are `Router` + `Client lint (type-aware ratchet)`. Router reports only after the slow Tier 2
  unit shards finish (~25 min). The Tier 2 ADR job may be CANCELLED by its 5-min timeout after passing (#2224); read
  the log before merging.

## Open items for the owner

1. **016 Repair questions:** cross-BU re-own (skip for now); whether Repair re-owns roots; SYSTEM/INTEGRATION-owned
   unfiled rows.
2. **017 decisions:** FR-06 step 10 (UAC-r2 text recommended); #1608 columns; the chat matter/project number (recommend
   refusing it); SystemPrincipal.
3. **Live smoke of #2191 on dev** (Office Project-from-Matter quick-create, mapped values, a secured column stays empty).
4. **005 `-Apply` on dev**, then `-Verify`.
5. **014:** confirm the 403 for a mapped filing lookup that fails field security. **015:** index precedence notes.
6. **#2137**; **HIPC DEV 2** map removal; **model1-prod** verification; **prod** null `sprk_issecure` check before a
   prod #2191 deploy.
7. **#2224:** OK to raise the Tier 2 ADR job timeout.

## Cross-project obligations

- **UAC-r2:** reviews 018b and 017 on condition (b); holds #2190 and #2169; batches deploy candidates for R3.
- **ebilling-r1:** #1911 + reconcile before 017. Task 096 removes the Office Invoice leg. 016/040 call
  `CheckGenericUpdateAsync` once #1911 lands (016 has a `TODO(#1911)` seam).
- **R3:** deploys master to dev only on a batched candidate from UAC-r2, with the owner's go in R3's session.
- **ontology-r1:** 033 needs a design-note review first. 033 also carries the app-only metadata reader and the IsSecured
  skip; 052 is hard-blocked on 033.

## Next tasks

018a PR → 018b → 018c → 019. 017 once unblocked. Then 033 (before 052), 040 (needs 005, 019), 050 (needs 019).
