# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21
> **Phase**: Wave 2. Done (9/43): 001–006, 010, 011, 012. W1 complete except 005's live -Apply (owner OK). In flight: 013 (fix round 1).

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | `036bc69bab` on `work/field-mapping-server-write-path-r1`, pushed; master merged at 44063b3473, no overlap with 013's files |
| **Running** | (1) **013** agent (opus/xhigh), branch `task/fm-013` (base bad3195920, tip c09c73c1f7 before fixes), worktree `C:\code_files\spaarke\.claude\worktrees\agent-a87bbe45568936ca4`. Fix round 1 in progress: F1/F2 secure-root and autonumber-retry tests; F3 caller-set autonumber column refused as BlockedColumn; F4/F5 SystemPrincipal on an isolated/secure plan refused 403 `system_principal_unsupported` **before** PlanCreateAsync (main-session fail-closed decision); K1 census name join (expires 018); K2 never-throw post-create calls. Then a second adversarial pass (security). 004/005 merged (`task/fm-004-005`, 3 commits) |
| **Next Action** | On each report: merge with `--no-ff`, verify (sln, ArchTests, suites), mark the POML and TASK-INDEX, push. After 013: send the range to UAC-r2 (refusal codes, owner, post-create order) and ebilling-r1 (registry rule, corrections a–c) for review before any master merge. 005 -Apply waits on the owner (item 3). Next task: **014** (deps 002, 003, 013) |

## How wave-2 agents are run (lessons)

- `isolation: worktree` creates the worktree from **origin/master**, not this branch. Every brief must start with
  `git switch -c task/fm-NNN <current tip sha>` and a check that the POML exists.
- Agents commit on `task/fm-NNN`. The main session merges with `--no-ff`, runs the solution build, ArchTests and the
  security/field-mapping suites, then marks the POML and TASK-INDEX and pushes.
- New tests go under `tests/unit/domain/**` or `tests/integration/auth/**` (testing.md:132), never the POML's
  `tests/unit/Sprk.Bff.Api.Tests/...` path.
- Publish baseline: rebuild master fresh when origin/master has moved. Last fresh master build:
  39,290,595 B at 28274e04e.

## Open items for the owner

1. **HIPC DEV 2 map removal:** the heliosip pac profile token has expired. The owner must re-authenticate
   (`pac auth create --environment https://hipc-dev-2.crm.dynamics.com`) before the removal can run.
   model1-prod's copy is managed and goes with the next managed SpaarkeMaster upgrade (verify then). The demo
   environment no longer exists.
2. **#2137 timing leak:** the quick-create filter sites and `TodoSourceAccessFilter` need an owner decision on where
   to fix them. The push site is in task 040.
3. **005 live migration (dev):** `scripts/Set-FieldMappingRuleSyncMode.ps1 -Apply` needs the owner's OK. Dry run: 25 Copy
   rules, 21 would change (20 empty, 1 One-time), 4 already Manual Refresh. After the apply: rerun `-Apply` (expect 0
   changes) and `-Verify` (expect exit 0); record both in task-005-notes. Seeder gap: DI-003 / #2181 (task 040).
4. **012 OWNER REVIEW items, for 013–015:**
   - Document is marked search-indexed (FR-10) but no create path fills it today.
   - Mapping applies by default to memo, budget, KPI assessment, billing event and communication.
   - The per-path server defaults.
   - The #1608 work assignment gap: the route refuses its container, securitybu and access permission; the chat
     create tool allows them. The owner decides in 013.

## Cross-project obligations

- **UAC-r2:**
  - cleared 011 (decisions.md);
  - 017 goes to them before merge;
  - any refusal-code, owner or post-create-order change pings them before merge;
  - K2 (no shared member names) is an acceptance criterion of 013.
- **ebilling-r1:**
  - send the 012 reconciliation recipe (notes/task-012-notes.md) and the registry rule for review;
  - they review 025 and 032;
  - the second lander reconciles the invoice-family guard;
  - they will send the shas of their SignalWriter/DecisionRecordWriter range.
- **ontology-r1:** 033 needs a design-note review first. Signal, Decision Record and Budget Revision carry the
  `OntologyContentIsNotMapped` exclusion.
- **Ongoing:** check with external-access-r3 before any dev BFF deploy (019); they check with us before theirs.

## Next tasks after the wave

- **014 → 015 → 016 → 017/018 → 019** after 013 (W2 sequential core).
- **Owner item from 013 F4:** if a 017/018 server adapter needs a SystemPrincipal create under a secure root, it
  needs an app-only access check on `RequestedBy`. 013 fails closed until then.
- **040:** needs 005 and 019.
- **050:** needs 019.
- W1 is otherwise done once 004 and 005 land.
