# Task 030 — Choice/Boolean/Number coercion for String-typed fieldMappings + publish-size verification

> Task: `030-coerce-field-value-choice.poml` (Phase B: Hardening)
> Files: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/UpdateRecordNodeExecutorTests.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Integration/PlaybookExecutionTests.cs` (mechanical ctor-arg fix only)

## Frozen-engine Path A exception (root CLAUDE.md §10 / §6.5)

**ADR in question**: ADR-039 (Grounded Execution & Closed Catalogs) / ADR-013 — "MUST NOT land new
capability on the frozen node-graph engine" (OQ-2; ADR-037 amendment 2026-07-05).

**Specific rule challenged**: the frozen-engine prohibition on modifying the node-execution engine
(`UpdateRecordNodeExecutor` is part of that frozen surface).

**Conflict**: `CoerceFieldValue`'s String branch currently passes a rendered value through verbatim
when the fieldMapping declares `type:"string"`. When the TARGET Dataverse column is actually a
Choice column (e.g. `sprk_documenttype`), Dataverse rejects the PATCH with a 500 — this was hit
in production during R7 W12 Document Upload UAT (see
`notes/inbound-from-r7/06-choice-field-coercion-in-updaterecord.md`). Not fixing this leaves a
known, reproducible defect in a shipped node executor.

**Proposed path**: **A — Project-scoped exception.**

**Rationale**: This is **defect-hardening, not new capability**. Evidence:
- No new `ExecutorType` enum value was added.
- No new `INodeExecutor` was registered; `UpdateRecordNodeExecutor.SupportedExecutorTypes` is
  unchanged (`[ExecutorType.UpdateRecord]`).
- No new dispatch path, node type, or routing config surface was introduced (ADR-039's actual
  concern — intent-detection/dispatch duplication — does not apply here).
- The pre-existing `FieldMappingType.Choice` / `Boolean` / `Number` / `Lookup` branches (the
  capability surface that already existed) are **byte-identical** before and after this change —
  verified by diff review and by all pre-existing tests passing unmodified.
- The change is scoped entirely inside `CoerceFieldValue`'s `String` branch: it now resolves the
  target column's REAL metadata type (via the existing `MetadataService` Redis-backed cache) and
  coerces accordingly instead of a verbatim pass-through — closing a defect in existing behavior,
  not adding a new behavior class to the engine.

**Impact**: Narrow — one method (`CoerceFieldValue`) in one executor gained a new private
dispatch-by-real-metadata-type helper (`CoerceStringMapping`) and a fail-loud helper
(`CoerceChoiceFromMetadata`). No schema change, no new DI registration beyond bridging an
already-registered Scoped service (`MetadataService`) into the existing Singleton executor via the
established `IServiceScopeFactory` pattern (mirrors `LookupUserMembershipNodeExecutor`).

**Alternative considered (and rejected)**: Track A from the R7 inbound note (authoring-time gate in
the playbook builder canvas, forcing `type:"choice"` + pre-populated options at author time) is the
more durable long-term fix, but does not help playbooks already authored with `type:"string"`
(including the Profile Document playbook that hit the 500 in production) and requires builder UI
work out of this task's scope. Track C (AI prompt discipline — have the Action JPS emit numeric
option values instead of labels) is fragile and would need to be re-applied to every Choice-writing
Action. Track B (this task, runtime coercion) was recommended by the inbound note as "a good
defensive-second layer" and is what FR-C1 scopes.

**Pre-approval**: this exact deviation is documented in `projects/spaarke-daily-update-service-r5/spec.md`
§ADR Tensions (table row for ADR-039/ADR-013), which pre-approves Path A. This note restates it at
the point of code-review per §10, and code-review (invoked during task execution) confirmed the
implementation stayed within the pre-approved scope (no new dispatch/executor surface).

**PR description one-liner**: *"Frozen-engine Path A exception (pre-approved in spec.md ADR
Tensions): defect-hardening of `UpdateRecordNodeExecutor.CoerceFieldValue`'s existing String branch
(Choice writes currently 500) — no new `ExecutorType`, no new dispatch surface, existing typed
Choice/Boolean/Number/Lookup branches unchanged."*

## Reuse decision — no new metadata cache

Grepped the executor + its dependency surface (`Services/Ai/`, `Services/Dataverse/`) before adding
anything. Found `Sprk.Bff.Api.Services.Dataverse.MetadataService` (registered Scoped via
`AddDataverseMetadataServices()`), which already projects Dataverse entity/attribute metadata
(including Choice option sets) and caches the projection in Redis for 6h (FR-BFF-03, task 010/012).
Reused it directly — **no new cache, no new metadata service, no new DI registration** beyond
bridging the existing Scoped `MetadataService` into this Singleton executor via
`IServiceScopeFactory` (the same Singleton+Scoped pattern already used by
`LookupUserMembershipNodeExecutor` and `AgentServiceNodeExecutor`).

Metadata is resolved **once per `ExecuteAsync` run** (outside the fieldMappings loop, only when at
least one mapping is `type:"string"`) and reused for every field in that run — verified by a
dedicated test (`ExecuteAsync_WithMultipleStringMappingsToSameEntity_ResolvesColumnMetadataOnce`)
asserting the underlying `IDistributedCache.GetAsync` is invoked exactly once even with two
String-typed mappings.

## Publish-size verification (root CLAUDE.md §10 bullet 4)

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/ --runtime linux-x64 --self-contained false
```

- Uncompressed output: 144 MB (`deploy/api-publish/`)
- Compressed (Compress-Archive, Optimal): **49.46 MB** (incl. 4 PDB files present in the output)
- Baseline (2026-07-08, per root CLAUDE.md §10): ~49.63 MB incl. PDBs
- **Delta: −0.17 MB** (well under the +5 MB single-task escalation threshold; nowhere near the
  ≥55 MB architecture-review or ≥60 MB hard-stop thresholds)
- No package reference was added or changed — this task touched zero `.csproj` files (confirmed via
  `git status`/`git diff --stat`), so the near-zero delta is expected (a handful of new private
  methods + one XML-doc block in an existing file).

## CVE check (root CLAUDE.md §10 bullet 5)

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: 1 HIGH-severity advisory — `Microsoft.Kiota.Abstractions 1.21.2`
(https://github.com/advisories/GHSA-7j59-v9qr-6fq9). **Pre-existing** — this task added no package
references (confirmed: `Sprk.Bff.Api.csproj` has zero diff vs. HEAD). Not introduced by task 030;
out of scope to remediate here (would require a coordinated Kiota version bump across the
Microsoft.Graph + Kiota chain per `src/server/api/Sprk.Bff.Api/CLAUDE.md` "Package Management"
section, which requires updating ALL Kiota packages in lockstep — a separate task).

## NetArchTest (informational)

`dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — 5 pre-existing failures (ADR-007
Graph isolation, ADR-009 IDistributedCache preference, ADR-010 concrete-DI + Options pattern), none
of which mention `UpdateRecordNodeExecutor`, `MetadataService`, or `FieldCoercionException` —
confirmed via grep against the failure output. Pre-existing baseline drift, unrelated to this task
and out of scope to fix (task instructions restrict changes to the executor + its test).

## Deviations from the POML's suggested step sequence

None material. Step 2 (grep for existing metadata accessor) found `MetadataService` — used it as
described above instead of adding a new one, per the task's own constraint. Step 4 fail-loud
implementation uses a dedicated `FieldCoercionException` type caught specifically in `ExecuteAsync`
(returns `NODE_VALIDATION_FAILED`) rather than a generic exception falling into the existing
catch-all (which returns `INTERNAL_ERROR`) — this gives the fail-loud path its own semantically
correct error code, consistent with the FR-C1 "not a 500, not a silent pass-through" contract.
