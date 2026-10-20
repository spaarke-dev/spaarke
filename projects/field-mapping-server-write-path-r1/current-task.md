# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-20
> **Phase**: Wave 1 complete (001 ✅, 006 ✅, 010 ✅). No task in progress.

## Quick Recovery

| Field | Value |
|---|---|
| **Done** | 002 ✅, 003 ✅, 011 ✅. Merged and verified green; pushed at 83bf5cfa8 |
| **Running** | 004→005 agent (`task/fm-004-005`); 012 agent (`task/fm-012`, based on 83bf5cfa8) |
| **Waiting on** | UAC-r2 verdict on the 011 range c4a2a7b81..5120bf358. Gates the master merge; do NOT rewrite that range |
| **Task** | Wave 2 running: 002, 003, 004→005, 011 (4 background implementer agents, each in an isolated worktree) |
| **Status** | in-progress (launched from c4a2a7b81; master 2b07480ab, 0 behind) |
| **Next Action** | On each agent report:<br>• verify its branch (`task/fm-002`, `task/fm-003`, `task/fm-004-005`, `task/fm-011`);<br>• merge it into `work/field-mapping-server-write-path-r1`;<br>• build and test;<br>• mark the POML and TASK-INDEX;<br>• push.<br>For 011, send the diff range to UAC-r2 for review. For 005, the live `sprk_syncmode` migration needs the owner's OK (dry run only so far). |

Corrections given to every wave-2 agent:
- new tests go under `tests/unit/domain/**` (testing.md:132), not the POML paths;
- the master publish baseline is 39,281,358 B at 2b07480ab, reused only while master is unmoved.

## State

- Wave 1 is committed on `work/field-mapping-server-write-path-r1`.
- **001:** the engine is at `Services/Dataverse/FieldMapping/` with 88 tests in `tests/unit/domain/Dataverse/FieldMapping/`.
  Two pinned engine defects are filed as #2124 / DI-001, and **task 002 fixes them** (acceptance criterion added).
- **006:** dev maps removed. **Beyond-dev environments** (spaarke-demo, spaarke-model1-prod, HIPC DEV 2) still carry
  the map. Each needs an owner-approved removal before go-live (tracked in task-006-notes.md; fold into 053/090).
- **010:** coordination recorded in [notes/w2-coordination.md](notes/w2-coordination.md). Owner decisions from
  2026-10-20 are in [notes/decisions.md](notes/decisions.md):
  - FR-08 amended: a mapped filing lookup refuses;
  - W2 proceeds on master, not waiting for ebilling #1911.

## Cross-project obligations (binding; see w2-coordination.md)

- **Before every W2–W4 task:** rebase onto master and run `/conflict-check`. Whoever merges second rebases.
- **UAC-r2:**
  - reviews the 011 and 017 diffs before merge (send the ranges);
  - gets a ping before merge on any change to a refusal code, an owner decision or the post-create order;
  - 011 is a byte-for-byte move with unedited test assertions.
- **ebilling-r1:**
  - invoice-family guard: the create half goes to the registry (012), the refile half stays in RefileAsync, one
    copy only; message ebilling when 012/013 land;
  - reviews 025, 032 and the registry rule;
  - single-invoice-creator rule.
- **ontology-r1:**
  - 033 needs an ontology-owner design-note review and must keep the invariant list;
  - no field mapping into Signal or Decision Record rows;
  - leaving those writers out of the service is a valid outcome.

## Planning findings to carry into execution

- 004: profile `sprk_capabilitymod…` vs rule `sprk_compatibilitymode`. Describe the live column first.
- 005: rule `$select` lacks `sprk_syncmode`. ebilling adds an `IFieldMappingDataverseService` overload.
- 018: `ResolveDefaultAssigneeAsync` needs a new home. Invoice quick-create bypasses the Office core.
- 030: Office document save is create-then-update.
- 032: client-generated invoice GUID; ebilling fixed-id and transaction constraints.
- 034: `CreateCommunicationRaceProofAsync`.
