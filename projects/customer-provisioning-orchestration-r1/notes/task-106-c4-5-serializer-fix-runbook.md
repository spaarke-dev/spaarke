# Task 106 — C4.5 dual-Newtonsoft serializer fix — manual runbook note

**Date**: 2026-08-19
**Task**: 106 (Wave G-1)

## What changed

`RunStatus`, `GateState`, `QuarantineState` enums in
`src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/{ProvisioningRun.cs,GateState.cs}`
now carry a dual `[System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]`
+ `[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]`
attribute pair. The Cosmos SDK's default serializer (configured in
`Modules/CosmosModule.cs`) is Newtonsoft-based, not STJ-based — the STJ
attribute alone was silently ignored on the write path, so these enums were
persisted to Cosmos as integers instead of strings. This made
`CosmosActiveRunScanner`'s query (`WHERE c.status IN ('Running',
'WaitingOnGate')`) return zero rows forever, since the query compares
against the string form.

Same defect class as the already-landed #19 (Ttl null-serialization) and
#20 (RunId → `id` property name) fixes — DS-5 §C4.5.

## One-time manual cleanup — stale Cosmos document

DS-5 §C4.5 identified exactly ONE live Cosmos document written under the
pre-fix code: `runs/65109e91-...` (status `NotStarted`, a dead test run).
Its `status` field is almost certainly stored as an integer (`0` for
`NotStarted`) rather than the string `"NotStarted"`.

**This was NOT automated** — DS-5 explicitly rejected migration tooling for
a single document ("no migration tooling is warranted for a single
document"). An operator with Cosmos DB Built-in Data Contributor on the
`spaarke-provisioning` account (dev) MUST do ONE of the following before
relying on the reconciler/crash-recovery scan to see historical data
written under the pre-fix code:

1. **Delete** the document (`runs` container, partition key = its
   `customerId`, id = `65109e91-...`) — simplest, since it is a dead test
   run with no operational value. Recommended.
2. **PATCH** `status` (and, if populated, any `gateStates[*].status` /
   `quarantine.state`) to the string form matching the enum name
   (e.g. `"NotStarted"`) via the Cosmos Data Explorer or `az cosmosdb`
   item-patch, if the run needs to be preserved for any reason.

This document was NEVER in an active state (`Running`/`WaitingOnGate`), so
its pre-fix integer encoding did not cause a scanner miss in practice — the
cleanup is a hygiene/consistency step, not a live-incident recovery.

## Verification performed (task 106)

- `dotnet build` — 0 errors across Core + Api + Tests projects (post task
  100's concurrent Core/Api/Worker split; see task 106 completion report
  for the coordination detail).
- `dotnet test` — 8/8 new tests pass (`ProvisioningRunSerializerContractTests`
  ×5, `CosmosActiveRunScannerSeamTests` ×3 — the seam tests are env-guarded
  against `COSMOS_L2_SMOKE_ENDPOINT` and pass as a no-op skip when unset,
  matching the established `CosmosSmokeTests.cs` pattern from task 037).
- **Regression-proof verified manually**: temporarily reverted the
  Newtonsoft attribute on `RunStatus`, re-ran
  `Serialize_ViaNewtonsoftCosmosDefaultEquivalent_StatusIsJsonStringNotNumber`
  — it failed with `Expected ... JTokenType.String ... but found
  JTokenType.Integer`, reproducing the exact DS-5 §C4.5 defect signature.
  Restored the fix; test suite green again.
