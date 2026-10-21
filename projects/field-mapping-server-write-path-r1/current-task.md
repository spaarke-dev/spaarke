# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21 (checkpoint after #2191 merged and the 014 step-3 mask)
> **Phase**: Wave 2. Done (12/43): 001–006, 010–015. On master: 001–006, 010–013 (PR #2184) and the #2188 Office hotfix
> (PR #2191, 583f90c1d9; live on dev). 014 + 015 are on the work branch, waiting on UAC-r2's scoped check of the step-3
> mask, then a PR to master. 016 is in fix round 2.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | work branch `859752c0b1`: master 583f90c1d9 + 014 + 015 + the 014 step-3 mask (`task/fm-014-mask`). Verified: sln 0/0, ArchTests 1317/1317, related suites 2650 passed / 7 skipped. Pushed |
| **Running** | **016 fix round 2**: implementer agent, branch `task/fm-016`, worktree `C:\code_files\spaarke\.claude\worktrees\agent-a10e5fe383a4dfcd2`. Items: F1-e (map only from a parent the owner step resolved over, plus a gate on the source's own secure status, fail closed on a fault); F1-f (`sprk_decisionrecord` skipped ENTIRELY by Repair: append-only); UAC condition 2 (never write `sprk_accesspermission`, `sprk_accessinheritance`, `sprk_parentdocument` or `sprk_relatedcommunication` on a document, through `InheritedAccessPermission.RefuseAppOnlyWrite`); six surviving mutants; K2 notes; a reachability answer (no caller of `RepairAsync`) |
| **Waiting on** | UAC-r2's scoped check of `dfebc69803..859752c0b1` (014 step-3 mask, with guard before/after). The owner's answer on Repair field security (below) |
| **Next Action** | (a) UAC passes the mask → open the PR work branch → master for 014 + 015 (ADR-008 path A cited; Placement Justification; publish delta against a fresh master; CVE check) → CI full rollup → merge → close #2188 citing 583f90c1d9 and the PR merge SHA; close #1617/#1614 per the 014 notes once on master. (b) 016 fix round 2 reports → verify → scoped re-check → merge `task/fm-016` into the work branch. The Roslyn guard WILL fail on Repair's `ApplyFillOnly` call until the owner decides (see below) → send the 016 diff to UAC-r2 (condition b: Decision Record exception, owner outcomes) → mark 016. (c) Then 017/018 (blocking criteria in their POMLs) |

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
- **#2188 stays OPEN** until the 014 PR lands (UAC-r2). The closing note cites both merge SHAs.
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

1. **Repair field security (016; UAC-r2 concurs with option 1):** option 1, Repair skips any field-secured source
   column using 033's metadata reader, under UAC's five conditions: fail closed on metadata; skip the document
   access and D7 columns; a per-occurrence guard allow-list entry; tests; 052 blocked until 033. Option 3 (Repair never
   maps) is the fallback. Option 2 was rejected.
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
  - checking the 014 mask diff now;
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
