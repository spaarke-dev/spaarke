# Task 051 (H8 SPE container-type handler) — deviations from POML literal wording

> Recorded per CLAUDE.md §6.5 (ADR/spec deviation surfacing) and task-execute Step 12.
> All three deviations below are **Path C — pivot to comply**: the codebase's actual,
> already-established conventions produce a strictly better outcome than the POML's
> literal wording, discovered during implementation. No ADR conflict, no exception,
> no amendment needed.

## Deviation 1 — "Root container" creation mechanism

**POML literal wording** (step 4): invoke `Create-NewContainerType.ps1` + `Register-*.ps1`
+ `New-BusinessUnitContainer.ps1`.

**What was implemented**: `Create-NewContainerType.ps1 -CreateTestContainer` only — this
single invocation creates the container-type, registers the owning app (steps 1-4 inside
the script), AND creates a root container (step 5, gated by `-CreateTestContainer`).

**Why**: `New-BusinessUnitContainer.ps1` requires an EXISTING Dataverse business-unit row
(`-BusinessUnitId` / `-BusinessUnitName`) and writes its output back to that row's
`sprk_containerid` column. Per design.md §4.1's handler DAG (`H4 → H3 → { H8, H9 }`), H8
runs BEFORE H5 (Dataverse environment creation) / H6 (solution import) — no customer
Dataverse environment, and therefore no business-unit row, exists yet at H8's point in
the pipeline. `-CreateTestContainer` creates a container via Graph API only, with zero
Dataverse dependency — exactly what H8 needs.

`Register-BffMiWithContainerType.ps1` was also inspected and rejected as the POML's
"Register-*.ps1" referent: it is a UAT-specific, hand-parameterized script (hardcoded
default TenantId/ContainerTypeId/OwnerAppId for one specific tenant) for granting a
SECONDARY guest app permission on an EXISTING container type — a different concern from
H8's "create container-type + root container" scope, and not safely re-invocable
per-customer as written.

## Deviation 2 — "Persist to Dataverse env-var" mechanism

**POML literal wording** (step 6 / acceptance criterion c): "Persist containerId to
`sprk_SharePointEmbeddedContainerId` (Dataverse env-var) AND KV secret
`customer-{customerId}-spe-container-id` (both writes verified)."

**What was implemented**: H8 writes `InterStepState.ContainerTypeId` (container-type id)
+ `InterStepState.SpeContainerId` (root container id) to Cosmos, and writes the
container-type id to the canonical KV secret `SPE-ContainerTypeId` (see Deviation 3). It
does NOT issue a live Dataverse Web API write against `environmentvariablevalue`.

**Why**: The target Dataverse environment does not exist yet at H8's point in the DAG
(H8 runs before H5/H6). design.md §10.3 itself says the env-var is "Set by H7" — this was
independently confirmed live during code-review's ADR-check pass: sibling task 050 (H7,
already landed in the shared `InterStepState.cs`) added exactly the field H8 needs
(`SpeContainerId`) and `H7DataverseEnvVarValuesHandler.cs` reads it as the source value
for `sprk_SharePointEmbeddedContainerId`, failing `Resumable`/`speContainerId not present`
if H8 has not populated it. H8 was updated (post-discovery, before commit) to populate
this field — closing the H8→H7 handoff exactly as H7's own code already expects.

## Deviation 3 — KV secret name

**POML literal wording**: KV secret `customer-{customerId}-spe-container-id` (also present
in design.md line 741, §7.7 "Integration secrets").

**What was implemented**: the §7.9-canonical name `SPE-ContainerTypeId`.

**Why**: `StaticKvSecretManifest.cs` (H4's KV-secrets manifest, already shipped) already
reserves a slot at `SPE-ContainerTypeId` with the comment "H4 pre-creates the slot; H8
populates the actual container ID value after 24h SPE replication completes." design.md
carries BOTH names — the older §7.7 table entry (`customer-{customerId}-spe-container-id`)
was not fully reconciled during the v3.2 §7.9 canonical-naming pass that produced the H4
manifest. Reusing H4's already-established slot avoids creating a duplicate/conflicting
secret for the same logical value, and is the more recently-reconciled, authoritative name.

## New surface introduced (not a deviation, but flagged for review)

- **`scripts/Get-SpeContainerMetadata-AppOnly.ps1`** (new) — a T6-compliant (confidential-
  client, cert-based) app-only GET verification script. The existing
  `scripts/Get-ContainerMetadata.ps1` uses a DELEGATED `az account get-access-token`, which
  would defeat the entire purpose of H8's T6 post-condition check (proving app-only access)
  and could mask a real T6 regression by succeeding under the operator's own delegated
  session. Modifying the existing script was out of scope (other operators/scripts may
  depend on its current delegated behavior for ad-hoc troubleshooting).
- **`SPE-OwnerCert-Pfx`** (new canonical name, `SpeContainerTypeOptions.DefaultCertSecretName`)
  — no prior canonical KV secret name existed for the SPE owner cert itself (only for the
  resulting container-type id). Flagged for reconciliation into the Phase H canonical
  secret-catalog manifest generator (task 084).
