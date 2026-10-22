# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21 (016 ✅, UAC-r2 ACCEPT; PR to master next. #2220 fixed on master via #2223)
> **Phase**: Wave 2. Done (13/43): 001–006, 010–016. On master: 001–006, 010–015, #2191, #2223 (ef5912c7e8, #2220).
> 016 is on the work branch, with its PR to master in progress.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | work branch `ccb732ceb6` = 016 (incl. fix round 73ef4e0845, merged as 33e36aafb9) + master ef5912c7e8. Verified: sln 0/0, ArchTests 1347/1347, related suites 3492 passed / 7 skipped. Pushed |
| **016 state** | ✅ complete. UAC-r2 condition (b) found F1-1..F4-2; all fixed; UAC-r2 ACCEPT of the fix round. POML notes carry the summary. Draft PR body: scratchpad `pr-016.md` (update counts, master sha, the UAC line, the never-map codes, F2-1) |
| **Running** | Publish delta ef5912c7e8 vs ccb732ceb6 (`C:\tmp\g16m`, `C:\tmp\g16a`; the script removes them) + CVE |
| **Next Action** | (1) PR 016 → master; merge on Router + Client lint green and the full rollup explained (Tier 2 ADR job times out: #2224) → send UAC-r2 the merge SHA → portfolio sync (Tasks Completed = 13). (2) If #2221 lands first, add the `EmailArchiveCommunicationLinkSweepJob.LinkAsync` exemption (below). (3) Then 017/018. **Don't ask R3 to deploy**; UAC-r2 batches the candidate (016 + #2221 + #2223), and a BFF deploy needs the owner's go in R3's session |

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
  - **#2221 (UAC task 190, draft) vs 016's member-keyed #2170 guard:** whoever merges second adds the member exemption
    `EmailArchiveCommunicationLinkSweepJob.LinkAsync` with UAC's reason verbatim: "the BFF's own typed link writer:
    sets a NULL sprk_relatedcommunication to the exactly-one communication whose internet message id ordinal-equals
    the document's recorded key; never clears or re-points; conditional on row version (uac-r2 task 190)".
  - **#2220 (widened to matter + project, UAC 2026-10-21):** separate PR off master, branch
    `fix/fm-2220-matter-protected`; UAC reviews the diff (condition b: Office matter/project quick-create skip-and-warn;
    dev 0 rules).
- **ebilling-r1:** the reconcile commit before 017; 016 and 040 call `CheckGenericUpdateAsync` once #1911 lands. 016
  has a `TODO(#1911)` seam that skips invoice and billing-event rows.
- **R3 (external-access-r3):** deploys master to dev. Asks OK/HOLD per deploy; told when #2191 merged.
- **ontology-r1:** 033 needs a design-note review first.

## Next tasks

- 016 → 017/018 → 019. 017/018 blocking criteria include #2188 (now met in the service), the invoice-family rule, FR-06
  step 10, 015's K1/K3, and R3-1/R3-2.
- 033 (with the app-only metadata reader) before 052. 040 needs 005 and 019. 050 needs 019.
