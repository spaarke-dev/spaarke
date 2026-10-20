# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-20 (checkpoint at 85% context)
> **Phase**: Wave 2. Done: 001, 002, 003, 006, 010, 011. 012 ✅ (verified, pushed). 004→005 agent still running.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | `77ef24010e` on `work/field-mapping-server-write-path-r1`, **NOT yet pushed**. It contains the merge of `task/fm-012` (69b3a33047) plus origin/master; 0 behind master |
| **Running** | (1) Background verify of 77ef24010e: Spaarke.sln build, ArchTests (expect 1155), and the security/field-mapping/RecordTableRules unit filter. (2) The 004→005 implementer agent in an isolated worktree, branch `task/fm-004-005`, base c4a2a7b81 |
| **Next Action** | When the verify is green:<br>• mark 012 ✅ (POML status + notes line `rework: fix-round-1 task 012`, TASK-INDEX row `FR-11 \| opus / high \| FULL \| W2-A \| 010`);<br>• run `scripts/check-task-status-drift.ps1`;<br>• commit and push.<br>Then add the 012 OWNER-REVIEW items (below) to the 013 POML notes. When the 004/005 agent reports, merge `task/fm-004-005`, verify, and mark 004 and 005. 005's live sprk_syncmode migration needs the owner's OK (dry run only). |

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
3. **005 live migration:** the owner's OK is needed after the dry-run numbers arrive.
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

- **013:** opus/xhigh, sequential core. Deps 011 ✅ and 012 (being verified).
- **040:** needs 005 and 019.
- **050:** needs 019.
- W1 is otherwise done once 004 and 005 land.
