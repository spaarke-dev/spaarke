# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21 (016 merged into the work branch and verified; awaiting UAC-r2 condition (b))
> **Phase**: Wave 2. Done (12/43): 001–006, 010–015. ALL on master: 001–006, 010–013 (#2184), #2191 (583f90c1d9,
> live on dev), 014 + 015 (#2214, cf0886360a). #2188 is CLOSED. 016 code complete on the work branch.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | work branch `4c5ac9f5ad` = merge of `task/fm-016` (c7ee5bcd0a, --no-ff as c16705d6aa) + master f1b3dadcf5. Verified: sln 0/0, ArchTests 1349/1349, related suites 3156 passed / 7 skipped. Pushed |
| **016 state** | Code complete. Owner chose field-security option 1. #2188 guard: new per-member `UnmaskedCallExemptions` (`…/RecordCreation/RecordCreationService.Repair.cs\|RecordCreationService.RepairMappingAsync`, Uses = 1), controls + 5 mutants killed, `KnownCallSites` = 3. #2170 guard member-keyed (UAC option c). `SourceColumnsRepairMayCopy` seam is `TODO(task 033)`. Diff for review: `git diff 3583ac04cb...c7ee5bcd0a` |
| **Running** | Publish-size delta f1b3dadcf5 vs 4c5ac9f5ad (`C:\tmp\f16m`, `C:\tmp\f16a`; remove with Remove-WorktreeSafe.ps1 afterwards) + CVE check. UAC-r2 asked (2026-10-21) for ACCEPT / F items on the 016 diff |
| **Next Action** | (1) UAC-r2 ACCEPT → mark 016 ✅ (POML, TASK-INDEX, portfolio Tasks Completed = 13) → PR to master with publish delta, CVE, full CI rollup; F items → resume implementer a10e5fe383a4dfcd2. (2) Then 017/018 |

## Key facts since the last checkpoint

- **#2191 (the #2188 Office hotfix) merged** as 583f90c1d9 after UAC-r2 ACCEPT and green CI; deployed to dev by R3.
  - The source is read as the caller (design D mask).
  - K2: the pair is derived from the source; under a secure source only the derived pair is accepted
    (`secure_source_filing_mismatch` 403).
  - AppendTo is checked on the derived pair for every source (`caller_cannot_file_under_parent`).
  - An empty secure flag is not retryable.
  - The guard is a Roslyn syntax walker (`Microsoft.CodeAnalysis.CSharp` 5.9.0 in ArchTests).
  - A mask inside a local function, lambda or anonymous method doesn't count outside it.
  - `global using` and csproj `<Using>` imports of the engine are violations.
- **#2188 CLOSED** citing 583f90c1d9 and cf0886360a. #1614/#1617 stay OPEN deliberately (no live route uses the
  unified create yet; the Office mapped-X gap is live until 018); closing them is an 018 criterion.
- **R3 deploy OKs given:** 84291ecea8, 583f90c1d9 (live), 446c83d927, f1b3dadcf5. The 30-minute reading after #2191
  was clean, with 0 new refusals logged (no Office quick-create run yet).
- **014 step 3** (`RecordCreationService.Mapping.cs`) now masks the source through `MappingSourceCallerMask.ApplyAsync`
  (`KnownCallSites` = 2).
- **015:** UAC-r2 ACCEPT on condition (b). K1 (a step-7 catalog fault test) and K3 (the search-index bind-key case) are
  blocking criteria on 017/018. #2206 (the stamped index isn't re-derived on owner change) is pre-existing.
- **033** owns the APP-ONLY column-metadata reader for system-principal creates AND Repair (criterion added).
  **052 is hard-blocked on 033** (TASK-INDEX deps 016, 033, 050).
- **#1617:** confirmed live on Office for a mapped filing lookup naming a DIFFERENT ordinary record X (no AppendTo on
  X); closed by 018 moving Office onto the unified create.

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- **Nested-agent limit:** launch the adversarial-reviewer passes from the main session.
- When an agent stops, resume it with SendMessage(agentId) for its fix round; it keeps context.
- Tests go under KEEP paths. The full `Sprk.Bff.Api.Tests` run shows load flakes; rerun failures alone.
- **After ANY master merge into the work branch, run the full ArchTests AND the related suites, not just the test
  you expect to fail.** (2026-10-21: a 015 test broke on the #2191 merge and was only caught by the next agent.)
- A stale ArchTests DLL can show a false census failure after a merge; rebuild before trusting it.
- `Remove-WorktreeSafe.ps1` from Bash mangles backslash paths; call it through PowerShell.

## Open items for the owner

1. ~~Repair field security~~: DECIDED 2026-10-21, option 1 (notes/decisions.md); implemented in 016, the skip lands in 033.
2. **016 owner questions:**
   - cross-BU owner moves on ownership-parent tables (recommend keeping the skip and having 052 report the count);
   - whether Repair re-owns roots at all;
   - whether SYSTEM- and INTEGRATION-owned unfiled rows are refused instead of moved.
3. **Post-deploy live smoke of #2191 on dev:** Office quick-create of a Project from an ordinary Matter, ideally as a
   user without a field-security profile.
   - It should be created and filed under the matter, with mapped values.
   - A secured column should stay empty.
   - `sprk_issecure` should come back null, not false.
4. **005 `-Apply` on dev:** 21 of 25 Copy rules move to Manual Refresh; then `-Apply` again (expect 0) and `-Verify`.
5. **FR-06 step 10:** UAC-r2's text is recommended. It blocks re-pointing pre-create stamp writers in 017/018.
6. **Before 017:** #1608 columns; the chat matter/project number (recommend refusing it); SystemPrincipal.
7. **014:** confirm the 403 for a mapped filing lookup that fails field security (UAC recommends keeping it).
8. **015:** document index precedence; the Office index until 018; the unfiled-communication refusal; the census gap.
9. **#2137** (the push half is in 040). **HIPC DEV 2** map removal after `pac auth create`. Verify model1-prod after
   the next managed upgrade.
10. **Prod deploy of #2191:** first check prod for matters and projects with a null `sprk_issecure`, and for active
    `sprk_recordtype_ref` rows for both tables (dev: 0 null, both rows active).

## Cross-project obligations

- **UAC-r2:**
  - ACCEPTed the 014 mask (merged in #2214); deciding on the #2170 per-type ratchet vs 016;
  - reviews the 016 diff before merge (Decision Record exception, document column skip, owner outcomes);
  - reviews 017/018 on condition (b);
  - holds #2190 and #2169;
  - pending ArchTest #2170 (`EveryGenericUpdateWriter_AsksTheAppOnlyRefusal`) will cover Repair's write.
- **ebilling-r1:** the reconcile commit before 017; 016 and 040 call `CheckGenericUpdateAsync` once #1911 lands. 016
  has a `TODO(#1911)` seam that skips invoice and billing-event rows.
- **R3 (external-access-r3):** deploys master to dev. Asks OK/HOLD per deploy; told when #2191 merged.
- **ontology-r1:** 033 needs a design-note review first.

## Next tasks

- 016 → 017/018 → 019. 017/018 blocking criteria include #2188 (now met in the service), the invoice-family rule, FR-06
  step 10, 015's K1/K3, and R3-1/R3-2.
- 033 (with the app-only metadata reader) before 052. 040 needs 005 and 019. 050 needs 019.
