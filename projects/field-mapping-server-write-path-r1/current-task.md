# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21 (checkpoint at 85% context)
> **Phase**: Wave 2. Done (11/43): 001–006, 010–014. On master: 001–006, 010–013 (PR #2184). 014 is cleared by UAC-r2 but
> waits for the #2188 caller mask. 015 is built and under review. The #2188 Office hotfix (PR #2191) is in UAC fix round.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | work branch `937bf3fe1e` (+ docs) = master 2a38e6ce6c + 014 (`task/fm-014`, `-r3`). Verified: sln 0/0, ArchTests 1214, targeted suites 2292 passed / 7 skipped. Pushed |
| **Running** | (1) **#2188 Office hotfix** agent, branch `fix/fm-2188-office-source-read`, **PR #2191** (open, DO NOT MERGE until UAC-r2 re-checks). Worktree `C:\tmp\h2188w`; remove it only with `scripts/worktree/Remove-WorktreeSafe.ps1` after merge. Fixing UAC's verdict: guard F (using alias, method group); K3 (`$select` every mapped column); K4 (a six-type theory); K2 (derive the pair from the source before the plan; a caller pair can't lower isolation: a secure source accepts only the derived pair); then one adversarial pass. (2) **015 adversarial review pass 1** (opus) over `58d1a3548d..f019b6bef0` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-af331113ba5679c60`, branch `task/fm-015`). A background CI-wait on #2191 is also running |
| **Next Action** | (a) Hotfix reports → send the new head to UAC-r2 → their verdict → CI gate on the full rollup → `gh pr merge 2191 --merge` → fast-forward the main repo master. (b) Merge master into the work branch → brief an agent to route 014's step 3 through `MappingSourceCallerMask` (the guard forces it) → verify → UAC-r2 scoped check → PR 014 to master. (c) 015 review pass 1 → fix round (resume the 015 implementer or a fresh one) → review pass 2 → merge `task/fm-015` → UAC-r2 review (owner outcomes change: condition b) → mark 015. Office file conflicts are likely between 015 and #2191 (owner logic vs the source read); resolve them at the merge |

## Key facts since the last checkpoint

- **013:** on master. UAC-r2 and ebilling-r1 gave no-objection. Fix rounds 1 and 2: the SystemPrincipal refusal before
  the plan; BlockedColumn for the autonumber column; the Office file renamed `OfficeRecordCreationService.cs` (so the
  census can fire); I-16 order and (table, id) tests; pre-write runner tests; the ratchet {invoice, billingevent}.
- **014:** mapping is opt-in per request and fill-only; caller checks run on the merged payload; a mapped filing lookup
  refuses; other mapped values drop with `record_creation.mapped_value_dropped`; new code
  `record_creation.server_set_filing_lookup` (500); "filing" = the lists OR `ParentsOf` (F-B); D7 accessinheritance is
  protected; ebilling items 1–3. UAC-r2 no-objection on 2026-10-21; R3-1 and R3-2 go to 017.
- **#2188 / DI-004 design D (caller mask; decisions.md):** the app read gives types only; one OBO GET through
  `IDataverseUserClient`; a column non-null for the app but null for the caller is removed; a fault or timeout (15 s)
  makes the source Unavailable; an ArchTest guard is an allow-list per occurrence. The hotfix lands first, and 014 then
  reuses it.
- **015 (built, unreviewed):**
  - record-first owner everywhere (`ResolveOwningTeamAsync` is gone from create paths; ArchTest
    `CreateOwnerResolutionGuardTests`);
  - fill-only search-index default from the owning BU (matter, project, document);
  - **live Office change:** a Project under an ordinary Matter is now owned by the Matter's BU team (release note);
  - a secure mapped filing lookup gives 403.
  - `sprk_AI_Search_Index` navigation casing is confirmed in the solution XML.
  - Publish +27 KB.
  - Owner items: Document index source; Office search index uses the caller's BU until 018; the unfiled-communication
    refusal; the census gap for Office root creates; the I-5/I-6 doc update.

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>` plus a
  check that the POML exists.
- **Nested-agent limit:** an implementer can't launch its own reviewers while 3–4 agents are running. Launch the
  adversarial-reviewer passes from the main session instead.
- When an agent stops, its child reviewer's notification comes to the main session. Resume the parent with
  SendMessage (agentId), passing the findings.
- Tests go under KEEP paths: `tests/unit/domain/**`, `tests/integration/seam|auth|data-mutation/**`.
- The full `Sprk.Bff.Api.Tests` run (26k tests, about 47 min) shows load flakes. Rerun failing tests on their own
  before calling them real.

## Open items for the owner

1. **005 `-Apply` on dev:** 21 of 25 Copy rules would change to Manual Refresh. After applying: rerun `-Apply` (expect
   0 changes) and `-Verify` (expect exit 0).
2. **FR-06 step 10:** UAC-r2's text is recommended: derive I-1/I-16 before the create (refuse on failure), then
   re-derive after; documents stay post-create. This blocks re-pointing pre-create stamp writers in 017/018.
3. **Before 017:**
   - #1608: work-assignment server-owned columns refused for chat; caller-refused columns on projects and matters;
   - the chat matter/project number: recommend refusing it and removing it from the tool description;
   - SystemPrincipal: keep the refusal, or approve an app-only RequestedBy check (ADR-028).
4. **014:** a mapped filing lookup that fails field security gets 403. UAC-r2 recommends keeping it. Confirm.
5. **015:** the items listed above.
6. **#2137:** quick-create filter and `TodoSourceAccessFilter` timing. The push half is in 040.
7. **HIPC DEV 2:** remove the map after `pac auth create --environment https://hipc-dev-2.crm.dynamics.com`.
   model1-prod: verify after the next managed upgrade.
8. **Post-deploy:** a live smoke on dev for #2191 K5. A user without a field-security profile should get
   `sprk_issecure` back as null, not false.

## Cross-project obligations

- **UAC-r2:**
  - reviews 015, 017 and #2191, plus the 014 step-3 mask diff;
  - is pinged on any refusal-code, owner or post-create-order change;
  - holds #2190 (`sprk_regardingcommunication` is missing from the root plan) and #2169 (D7 push skip; pinning test
    `DocumentAccessPermissionPushTargetTests`, kept green by 040).
- **ebilling-r1:**
  - the reconcile commit before 017 (blocking criterion): register the invoice-family rule, the 012-recipe test, the
    real CoreRecordLookups, F2 (500 kinds), F4, the RegardingTrio protected columns;
  - 016 and 040 call `CheckGenericUpdateAsync`.
- **ontology-r1:** 033 needs a design-note review first.
- **external-access-r3:** check with them before any dev BFF deploy (019).

## Next tasks

- 016 → 017/018 → 019 after 015. Blocking criteria on 017: #2188 (DI-004) fixed in the service; the invoice-family
  rule registered; FR-06 step 10 decided before any pre-create stamp writer is re-pointed.
- 040 needs 005 and 019. 050 needs 019.
