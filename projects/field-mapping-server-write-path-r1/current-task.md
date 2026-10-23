# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-22 (task 018 complete; #2281 fixed (1fa0cf5cb8); project blocked on 017)
> **Phase**: Wave 2. Done (14/43): 001–006, 010–016, 018 on master (018a 36403e1f4b, 018b ef8a91a157 #2261, 018c **ab7cf4fc94** #2266).
> **Live on dev: 1fa0cf5cb8** (R3, 2026-10-23 00:00Z; carries 018b LIVE, 018c, #2282). Waiting on the owner's 3 Office quick-creates + the Work Assignment attorney-copy check, then this session verifies read-only. **Active: none (blocked on 017).**

## Quick Recovery

| Field | Value |
|---|---|
| **Active task** | None. Every pending task goes through 019, which needs 017 (blocked) |
| **HEAD** | work branch = master 1fa0cf5cb8 + this checkpoint. No task branches or agent worktrees open |
| **Running** | Nothing |
| **Next Action** | (1) When the owner reports the dev creates: read-only MCP queries on dev for the newest sprk_matter / sprk_project rows by the owner (ownerid team, sprk_issecure, sprk_regardingrecordid + type, Assigned To, sprk_ai_search_index) and the newest work assignment (sprk_assignedattorney1 copied); report. (2) Unblock 017: ebilling #1911 (DRAFT) + reconcile, owner decisions in "Open items" 2 |
| **018 progress** | COMPLETE. 018c deleted `OfficeRecordCreationService` + `CreateTimeFieldMapping.Apply`; `ResolveDefaultAssigneeAsync` moved to `IOfficeService`; guards updated (KnownCallSites 2, owner allow-list empty, census). Opus review: F3 fixed, K4 fixed, K2 known limit (cancellation seam test timing). Publish −21,556 B (37.73 MB). #2253 (UAC) carries `WriteIdentity.AppOnly` in `DropAppOnlyRefusedRules` + must-fire/must-not-fire tests (`RecordCreationServiceMappingSeamTests.AppOnlyAccessSets.cs`); nothing for this project |
| **#2281 (out-of-task defect, fixed)** | PR #2282, merge **1fa0cf5cb8**. `sprk_targetfieldtype` uses the global set `sprk_fieldmappingtypes` (100000000+n, Currency 100000007); legacy rows and `sprk_sourcefieldtype` hold bare n. `FieldMappingColumns.NormalizeFieldType` reads both at the single rule read (`MapToFieldMappingRuleEntity`); Currency → Number. Client `describeError` shows the Xrm message. Ontology reviewed (no findings); dev check after the BFF deploy that carries it: Create Work Assignment from a matter with `sprk_assignedattorney1` copies it. Known limit: one bad column still voids the profile's client `$select` (task 035 deletes the engine). Seeder writing `1` = #2283, already fixed on the provisioning branch |
| **CI note** | #2252 raised the Tier 1 arch job timeout to 10 min (merged). The Tier 2 ADR job (#2224) may still be cancelled by its timeout; read the log |
| **Cleanup left for the owner** | Empty, unregistered folders (Remove-WorktreeSafe partial delete, "Permission denied"): `C:\code_files\spaarke\.claude\worktrees\agent-a10e5fe383a4dfcd2`, `...\agent-a80d401ebbcfb9d3e`, `...\agent-a1ced7a9c67154b2b` (018c, merged) |

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
  `projects/field-mapping-server-write-path-r1/scripts/verify-suites.sh <repo> <log-dir>`. Publish-size delta + CVE:
  `scripts/publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>` (same folder).
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

019 once 017 lands. 017 once unblocked. Then 033 (before 052), 040 (needs 005, 019), 050 (needs 019).
