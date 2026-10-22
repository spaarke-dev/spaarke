# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21 (016 on master; next 017/018)
> **Phase**: Wave 2. Done (13/43): 001–006, 010–016, ALL on master. 016 = #2228 (a7b956fb55); #2220 = #2223
> (ef5912c7e8). Portfolio synced (Tasks Completed = 13).

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | work branch `35c395b693` = master a7b956fb55's second parent; 0 commits not on master. Verified at 35c395b693: sln 0/0, ArchTests 1347/1347, related suites 3492 passed / 7 skipped, EmailArchive job tests 37/37. #2228 rollup CLEAN |
| **Running** | nothing |
| **Next Action** | Start 017 and 018 (both depend on 016; FULL, opus/high). First re-read their POML blocking criteria: #2188 (met), the invoice-family reconcile with ebilling-r1, FR-06 step 10 (owner item 5), 015's K1 (step-7 catalog fault test) / K3 (search-index bind key), R3-1/R3-2, and the #1614/#1617 closure (018). 017 and 018 share `RecordCreationService` files only via callers; run `/conflict-check` before parallelising. **Don't ask R3 to deploy**: UAC-r2 is batching #2223 + #2221 + 016 for R3, and a BFF deploy needs the owner's go in R3's session |
| **Cleanup left for the owner** | Empty, unregistered folder `C:\code_files\spaarke\.claude\worktrees\agent-a10e5fe383a4dfcd2` (Remove-WorktreeSafe refused after a partial delete; nothing in it) |

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
  - Done 2026-10-21: the `EmailArchiveCommunicationLinkSweepJob::LinkAsync` exemption (016 merged second); #2220 fixed
    in #2223; #2224 filed (Tier 2 ADR job's 5-min timeout cancels after all tests pass; advisory, owner OK needed).
- **ebilling-r1:** the reconcile commit before 017; 016 and 040 call `CheckGenericUpdateAsync` once #1911 lands. 016
  has a `TODO(#1911)` seam that skips invoice and billing-event rows.
- **R3 (external-access-r3):** deploys master to dev. Asks OK/HOLD per deploy; told when #2191 merged.
- **ontology-r1:** 033 needs a design-note review first.

## Next tasks

- 016 → 017/018 → 019. 017/018 blocking criteria include #2188 (now met in the service), the invoice-family rule, FR-06
  step 10, 015's K1/K3, and R3-1/R3-2.
- 033 (with the app-only metadata reader) before 052. 040 needs 005 and 019. 050 needs 019.
