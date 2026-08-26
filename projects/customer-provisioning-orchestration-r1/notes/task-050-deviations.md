# Task 050 (H7 Dataverse Env-Var Values Handler) — Deviations

> **Task**: 050-implement-h7-dataverse-env-var-values-handler.poml
> **Date**: 2026-08-17
> **Rigor**: FULL

Per CLAUDE.md §6.5, deviations from the task POML's literal wording are documented here rather than silently
resolved. All are **Path C (pivot to comply)** with the more detailed / more authoritative design.md sections —
none required an ADR amendment or a project-scoped ADR exception beyond what H6 (task 049) already established.

## D1 — Required-field set: 5 (not 7) hard-required upstream values

**POML wording** (constraint + step 2): "H7 pre-condition: all 7 upstream values available in Cosmos
interStepState; fail deploy if not" and acceptance criterion #2 lists exactly 5 keys
`{bffAppRegId, dataverseEnvUrl, openAiEndpoint, tenantId, speContainerId}` as the negative-test set.

**Conflict**: The POML's prose ("all 7 upstream values") and its own acceptance criterion (5 named keys) disagree
with each other, and both disagree in scope with design.md §10.2, which documents 3 of the 7 env-vars
(`bffApiBaseUrl`, `msalClientId`, `shareLinkBaseUrl`) as **optional run parameters with explicit defaults**
(`https://api.spaarke.com`, "typically same as bffApiAppId", empty string respectively).

**Resolution (Path C)**: The handler treats the 5 AC#2-listed keys as hard-required (`MissingUpstreamState`
failure if absent) and resolves the other 3 via parameter-with-documented-default per design.md §10.2. This
satisfies AC#2 literally (all 5 named keys still fail hard) while also honoring design.md's documented defaults
exactly as written. `dataverseEnvUrl` is treated as a connection target (not itself one of the 7 written values)
consistent with how H6 (task 049) treats the same field.

## D2 — Auth mechanism: confidential-client (BFF app-reg), not `az account get-access-token` or DefaultAzureCredential/MI

**POML wording**: Silent on auth mechanism beyond "cross-env token cache pattern for Dataverse" (citing
`RegistrationDataverseService.cs` as a pattern reference) and "config-driven seeding."

**Reference implementation actually used a THIRD mechanism**: `scripts/Provision-Customer.ps1` Step 8 (the
literal reference this handler ports) authenticates via `az account get-access-token` — the OPERATOR's own
interactive `az login` session. That mechanism has no automated-service equivalent; L2 runs unattended.

**Why not DefaultAzureCredential/MI** (the other candidate, and H5's pattern for its read-only `WhoAmI` probe):
design.md §4.1's handler-catalog ordering is `H5 → H6 (solutions) → H7 → H10 (app-user, needs H6 solutions) → H11`
— **H10 (the handler that registers the MI as a Dataverse Application User) runs AFTER H7.** At H7's point in the
DAG, the L2 App Service's managed identity has no Dataverse App User record yet, so an MI-based write would 403.

**Resolution (Path C — comply, following H6's established precedent)**: H7 authenticates via
`Azure.Identity.ClientSecretCredential` using the SAME confidential-client identity H6 (task 049) already uses
for solution import — the BFF Entra app-reg (`InterStepState.BffAppRegId`, H3 output) + a client secret sourced
from `EnvVarValuesOptions:ClientSecret` (Wave C5 wires this to a Key Vault reference; Wave C4 requires an explicit
app-setting, mirroring `SolutionImportOptions:ClientSecret`). This is documented as a NEW required precondition
(`MissingClientSecret`) beyond the POML's literal 5-key list, because without it no write can occur — this is a
necessary technical addition, not an optional interpretation.

## D3 — New `InterStepState.SpeContainerId` field — cross-task coordination note (H8 / task 051)

H7 reads the SPE root-container id (source for `sprk_SharePointEmbeddedContainerId`) from a NEW
`InterStepState.SpeContainerId` field, added by this task as a controlled schema extension (same discipline as
task 049's `ImportedSolutions` addition — see `InterStepState.cs` remarks).

**Coordination gap observed**: task 051 (H8, running in parallel in the same wave) was inspected at design time
(its POML step 6) and, as authored, has H8 persist the container id directly to the Dataverse env-var **and** a KV
secret, but does **not** describe writing this new Cosmos `InterStepState.SpeContainerId` field. At the time this
task landed, H8's actual committed code (`H8SpeContainerTypeHandler.cs`) had not yet been inspected for whether it
populates the field in practice.

**Resolution (Path C, with a residual runtime risk called out for operator/Wave-C5 awareness)**: H7's own
correctness does not depend on H8's code — H7 defines a clear contract (`InterStepState.SpeContainerId` must be
non-empty or H7 fails `Resumable`/`MissingUpstreamState`), which is the correct, safe behavior regardless of which
upstream handler populates it. If H8 (as landed) does not populate this field, H7 will correctly block with a
`MissingUpstreamState` diagnostic naming `speContainerId` until the field is populated (by H8, or by an operator
Cosmos patch) — it will NOT silently proceed with a wrong/missing value, consistent with the task 024
client-fails-fast contract this handler exists to protect. This is a coordination item for the Wave C5 reconciler
work, not a defect in H7 itself.

## D4 — Test file location: separate test project, not `.Tests.cs` alongside the handler

**POML wording** (`<relevant-files>`): lists
`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/H7DataverseEnvVarValuesHandler.Tests.cs`.

**Resolution (Path C — comply with established codebase convention)**: every existing handler test file (H0, H1,
H2a, H2b, H3, H4, H5, H6, H12a, H12b) lives in the separate `Sprk.Provisioning.ControlPlane.Tests` project under
`Handlers/{Handler}Tests.cs`, not co-located `.Tests.cs` files. The test file for this task was placed at
`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H7DataverseEnvVarValuesHandlerTests.cs` to
match the actual, consistent codebase pattern rather than the POML's aspirational path.
