# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-21
> **Phase**: Wave 2. Done (11/43): 001–006, 010–014. On master: 001–006, 010–013. 005 -Apply and the 013/014 OWNER items are open.

## Quick Recovery

| Field | Value |
|---|---|
| **HEAD** | master 2a38e6ce6c (PR #2184: 001–006, 010–013) plus `task/fm-014` merged on the work branch (not on master yet). Verified: sln 0/0, ArchTests 1214, targeted suites 2280 passed / 7 skipped |
| **Running** | nothing |
| **Next Action** | (1) UAC-r2 reviews 014: the new refusal code `record_creation.server_set_filing_lookup`, the pre-plan AppendTo check, the D7 entry, and the 013-r2 test re-target question. ebilling-r1 gets a FYI. (2) Next task **015**: owner and search-index swap (deps 013, 014). (3) Owner decisions: see the open items below |

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

5. **013 step 10 (FR-06):** UAC-r2's text is recommended: derive I-1/I-16 before the create (refuse on failure), then
   re-derive after; documents stay post-create. This blocks re-pointing pre-create stamp writers (017/018).
6. **Before 017:** caller-refused columns on projects and matters (#1608); the chat matter/project number (refuse, and
   drop it from the tool description); SystemPrincipal (keep the refusal, or approve an app-only RequestedBy check).
7. **014:** a mapped filing lookup that fails field security refuses with 403 (fail closed). Confirm.
8. **DI-004 / #2188:** source field security. This blocks 017.

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
