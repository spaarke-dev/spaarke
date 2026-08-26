# Task 024 — Cosmos provisioning schema + POCO deviations

**Date**: 2026-08-17
**Task**: `024-author-cosmos-provisioning-schema.poml`
**Rigor**: FULL (Sonnet 5 @ high)
**Status**: All acceptance criteria met.

---

## 1. Bicep placement: NEW module (option b) chosen over extend (option a)

The task offered two paths:
- (a) Extend `infrastructure/bicep/modules/cosmos-db.bicep`
- (b) Add a new sibling `infrastructure/bicep/modules/cosmos-provisioning.bicep`

**Chose (b) — new module.** Rationale:

- `cosmos-db.bicep` is invoked **per-customer** (`accountName` is a parameter) and declares
  the `spaarke-ai` **runtime** database (containers: `sessions`, `prompts`, `audit`,
  `audit-partitioned`, `memory`, `memory-items`, `feedback` — all partitioned by
  `/tenantId` or `/subjectId` per ADR-014 + ADR-042).
- `spaarke-provisioning` is **fleet-scoped** L2 orchestration state (one account deployed
  next to the L2 control-plane App Service in `rg-spaarke-platform-{env}`), partitioned
  by `/customerId` per design.md §6.2.
- Distinct concerns → distinct modules (design.md §5.3 D13). Extending the per-customer
  module with a fleet-scoped resource would (i) break the module's per-customer
  invocation contract and (ii) create a partition-key discipline conflict inside a
  single module (`/tenantId` for BFF containers vs `/customerId` for the orchestration
  container).
- The new module preserves the existing runtime pattern (serverless, Session consistency,
  RBAC-only, continuous 7-day backup, TLS 1.2+) so the two Cosmos accounts share a
  single mental model; only the container-shape/purpose differs.

## 2. Compiled ARM verification (acceptance criteria 1, 2)

`az bicep build --file infrastructure/bicep/modules/cosmos-provisioning.bicep --stdout`
exits 0, no warnings, no errors. Compiled properties:

| Property | Compiled value |
|---|---|
| Database name (default) | `spaarke-provisioning` |
| Container name (default) | `runs` |
| `partitionKey.paths` | `["/customerId"]` |
| `partitionKey.kind` / `version` | `Hash` / `2` |
| `defaultTtl` (seconds) | `31536000` (365 days) |
| Consistency | `Session` |
| Serverless | `true` |
| `disableLocalAuth` | `true` (RBAC-only per ADR-028) |
| Composite indexes | `(status ASC, createdOn DESC)`, `(customerId ASC, createdOn DESC)` |

## 3. C# POCO field-name reconciliation (acceptance criterion 4)

The task POML contains a small internal tension between:

- **design.md §6.2** — canonical field list uses `id` and `createdAt`
- **Task POML constraint L45 + acceptance criterion L101** — explicitly list `runId`,
  `createdOn`, and `quarantine`

Cosmos SDK **requires** the JSON identity field to be named `id` (lowercase); any other
name causes `CreateItemAsync` / `ReadItemAsync` to fail with `"id" property required`.

**Reconciliation applied** (documented per CLAUDE.md §6.5 Path A — project-scoped
exception; NOT a §6.2 field-name invention):

| §6.2 field name | Task POML acceptance-criterion name | JSON attribute value used | C# property |
|---|---|---|---|
| `id` | `runId` | `id` (Cosmos SDK invariant) | `RunId` (semantic name) |
| `createdAt` | `createdOn` | `createdOn` (POML wins; not a Cosmos SDK constraint) | `CreatedOn` |
| `completedAt` | (not listed) | `completedOn` (parallel choice to createdOn for consistency) | `CompletedOn` |
| — | `quarantine` | `quarantine` (added; §4C rollback semantics) | `Quarantine` (composite `QuarantineInfo`) |

The composite index in the bicep uses `/createdOn` (matches JSON) — reconciler
queries can compose `WHERE c.status = 'Failed' ORDER BY c.createdOn DESC` and the
index answers directly.

## 4. Escalation trigger DID NOT fire

The POML's `<escalation><trigger>` reads: *"If design.md §6.2 gate/inter-step state
enum values or field names differ from what you can implement cleanly with
System.Text.Json defaults, STOP and escalate."*

All §6.2 enum values (`Pending` / `Verified` / `Cleared` for gate state; the six
statuses NotStarted / Running / WaitingOnGate / Completed / Failed / Cancelled +
Quarantined per §4C for run status) map cleanly to C# enums serialized via
`[JsonConverter(typeof(JsonStringEnumConverter))]` — no case-mapping loss, no
custom converter needed.

The `id` vs `runId` reconciliation is a **Cosmos SDK invariant**, not a
System.Text.Json defaults conflict; the trigger's phrasing does not fire here.

## 5. Scope discipline (constraint L44)

Explicitly deferred (per task constraint "do NOT wire the Cosmos client into DI
(that is task 037) and do NOT scaffold the .NET project (that is task 036)"):

- No `.csproj` for `Sprk.Provisioning.ControlPlane` (task 036)
- No `Microsoft.Azure.Cosmos` PackageReference (task 037)
- No DI registration for a `CosmosClient` (task 037)
- No runtime instantiation (task 037)

Syntactic validity of the 5 POCO files was verified by compiling them in a scratch
project under `net10.0` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` +
`<Nullable>enable</Nullable>`: **0 warnings, 0 errors**. Task 036 will pick up
these files by copying them into the newly-scaffolded `.csproj`.

## 6. BFF publish-size verification: N/A

This task touches ZERO files under `src/server/api/Sprk.Bff.Api/**`. No BFF publish
required per root CLAUDE.md §10. Task 037 (Cosmos client wiring) is where L2 DI
registration will land — and even then, in the L2 control-plane project, NOT the
BFF (per project CLAUDE.md MUST rule: "register provisioning handlers in L2
control-plane service, not the BFF").

## 7. Task 025 downstream expectation

Task 025's ArchTest will assert:
- `RunParameters.Secrets` is typed as `IDictionary<string, KeyVaultSecretRef>`, NOT
  `IDictionary<string, string>`. ✅ Satisfied.
- `KeyVaultSecretRef` has NO property named `Value`, `SecretValue`, or `Plaintext`.
  ✅ Satisfied — the record has `VaultName`, `SecretName`, `VersionId` only, plus a
  `ToKeyVaultReference()` method that renders the URI form.

## 8. Wave 1 parallel-dispatch note

Task ran as 1 of 6 concurrent sub-agents (Wave 1 Batch 1). Per dispatcher
instructions, `TASK-INDEX.md` and `current-task.md` updates are owned by the
dispatcher and are NOT touched here.
