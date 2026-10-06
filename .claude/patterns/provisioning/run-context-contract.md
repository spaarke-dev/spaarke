# Run-Context Contract Pattern

> **Last Reviewed**: 2026-10-01
> **Reviewed By**: customer-provisioning-orchestration-r1 T245a (G25); T245c (operator intake validated at the edge)
> **Status**: Current.

## When

- A provisioning handler needs a value — from the operator, from the run, or from another handler.
- A handler fails with a "missing X" rejection on a real run although its unit tests pass.
- `RunContextContractTests` fails.

## Read These Files (canonical source)

1. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/IntakeParameterCatalog.cs` — the closed set of intake keys; the ONLY contents of `run.Parameters.NonSecret` (written once, by `POST /api/runs`, which rejects any other key).
2. `.../Core/Models/InterStepState.cs` — typed handler outputs; each property is `[ProducedBy(HandlerIds.X)]` or `[NoProducer(reason)]`.
3. `.../Core/Reconciler/HandlerRunInputs.cs` — every handler's declared inputs (Intake / Output / Gap). Values L2 owns are NOT run inputs (T245b): validated Worker options (e.g. `SpeContainerOptions.ContainerTypeOwners`) and artifact versions computed from the artifact applied (`Handlers/ArtifactVersion.cs`).
4. `.../Core/Reconciler/DagAdvancer.cs` — `HandlerDependencies`; a required Output's producer must be an ancestor of the reader.
5. `.../Core/Handlers/BulkAppSettings/PerEnvSourceCatalog.cs` — H4b's closed `per_env_settings` source set.
6. `.../Tests/Reconciler/RunContextContractTests.cs` — the forcing function: DAG reachability, the intake catalog, a Roslyn source scan of each handler folder (reads = declarations, both directions), producer truthfulness, and the H4 manifest check against `customer.bicep`'s `kvSecretValues`.

## Key Rules

1. **Operator supplies it** → add the key to `IntakeParameterCatalog` (+ intake schema / skill if the operator must provide it), declare `RunInput.Intake(key)`. If the handler has rules for it, `POST /api/runs` applies **the same rules** — shared code where the rule is non-trivial (T245c: `UserProvisioningIntake` serves H11 and the endpoint), the handler's own rejection code where it has one — intake is fixed once the run exists, so a value the handler would refuse must be refused at the edge. Name the key with a `*ParameterKey` constant or an `IntakeParameterCatalog` member — the scan also catches a literal key beside the NonSecret map, but a constant is the convention.
2. **Another handler produces it** → typed `InterStepState` property with `[ProducedBy]`, written by that handler only; declare `RunInput.Output(nameof(...))`; add the DAG edge if the producer is not already an ancestor.
3. **Never** read another handler's output from `Parameters.NonSecret`, and never seed a value in a unit test that no production code writes.
4. **Declarations match reads, both ways.** The scan parses C# (aliases like `var s = run.InterStepState;`, `?.`, `!.` are all seen; comments, strings and `nameof` are not reads), so an undeclared read fails, and so does a declared input the folder never reads.
5. **`run.Parameters.Secrets` has no writer** (task 245a). Nothing may write it; only H4 may still read it, for the manifest entries pinned as gaps.
6. A failing contract test means the data flow is wrong. Fix the flow; a `Gap` is only for an input a named task is already fixing, pinned with its reason.

Evidence for why this exists: `projects/customer-provisioning-orchestration-r1/notes/run-context-dataflow-gap.md` (a real run could not get past H0).
