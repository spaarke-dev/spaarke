# CI Coordination — H2a Bicep-&gt;ARM-JSON Pre-Compile Step (task 117)

> **Author**: customer-provisioning-orchestration-r1 task 117 (Phase C'' Wave G-1)
> **Date**: 2026-08-19
> **Target file (committed)**: `.github/workflows/publish-provisioning-arm-artifacts.yml` (NEW)
> **New file (committed)**: `.github/workflows/schemas/provisioning-arm-manifest.json` (manifest JSON Schema, NEW)
> **Deps**: none (per POML `<deps>none</deps>`)
> **Sibling**: task 116 (H9 BFF artifact publish — `.github/workflows/deploy-bff-api.yml` extension); this task follows the same governance posture and reuses the same `provisioning-artifacts` blob container.

---

## 0. Governance — direct commit, not a coordination-note-only draft

Per the outer-task instruction for this execution: r1's `CLAUDE.md` "Coordination with other worktrees" section formally took direct-commit ownership of `.github/workflows/**` for Phase C'' scope as of commit `8281045052` (the same governance shift tasks 067/088/115/116 already applied under). This task's POML predates that shift and describes "draft the coordinated CI change ... for H2a's ARM SDK port" without specifying commit-vs-note discipline explicitly, but the same reasoning task 116 documented applies verbatim here: `ci-cd-unit-test-remediation-r1` remains dormant (last commit `d4538dfde`, 2026-06-28), and producing a draft-only note when the direct-commit path is already open (and used four times this same week) would just re-introduce the coord-note backlog r1 already resolved. **Path chosen: C — pivot to comply with the current, already-adopted governance.** The workflow + schema are committed directly, not drafted for a separate owner to apply.

The task's recommendation to prefer a NEW workflow file over extending `sdap-ci.yml` was followed: `publish-provisioning-arm-artifacts.yml` is a standalone, additive workflow with its own trigger, concurrency group, and job — it does not touch `sdap-ci.yml`'s `tenant-isolation` job or naming-conformance step (both added by governance commit `8281045052`), so there is zero risk of colliding with that file's recent structure.

---

## 1. What was verified before authoring

- **Template scope** (POML step 1 — "confirm exactly which Bicep template(s) H2a deploys at provision time"). Grepped `Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy.FileBicepTemplateInspector.ResolveTemplatePath` (the authoritative C# source H2a's current handler already uses for template selection):
  ```csharp
  private string ResolveTemplatePath(string tenancyModel)
      => string.Equals(tenancyModel, "Model1Shared", StringComparison.OrdinalIgnoreCase)
          ? Path.Combine(_options.BicepDirectory, "stacks", "model1-shared.bicep")
          : Path.Combine(_options.BicepDirectory, "customer.bicep");
  ```
  This resolves the scope precisely: **exactly two templates**, `infrastructure/bicep/customer.bicep` (default / `Model2Dedicated`) and `infrastructure/bicep/stacks/model1-shared.bicep` (`Model1Shared`). `stacks/model2-full.bicep` — a different, broader stack consumed by the unrelated `deploy-infrastructure.yml` workflow — is **NOT** in H2a's template set and is correctly excluded from this workflow's scope.
- **Module-graph escalation check** (POML `<escalation>` trigger — "if the customer-stamp Bicep templates include modules/child-resources that `az bicep build` cannot fully flatten into a single ARM-JSON artifact ... STOP and escalate"). Grepped both templates for `^module ` declarations: `customer.bicep` has 8 (`keyVault`, `storage`, `serviceBus`, `cosmosDb`, `membershipTopic`, `acsCommunication` [conditional], `signalr` [conditional]) and `stacks/model1-shared.bicep` has 17, **all** using local relative file paths (e.g. `'modules/key-vault.bicep'`, `'../modules/key-vault.bicep'`) — zero `Microsoft.Resources/templateSpecs` / template-spec-by-id references in either module graph. **Escalation trigger does NOT fire.**
- **Local `az bicep build` sanity check** (acceptance criterion 3 — "succeeds with 0 errors as a pre-authoring sanity check"). Ran locally against both templates (Bicep CLI 0.46.1, bundled with the environment's Azure CLI):
  ```
  az bicep build --file infrastructure/bicep/customer.bicep --outfile <tmp>/customer-arm.json
  # exit 0 — 6 linter WARNINGs (no-unnecessary-dependson x1, BCP318 possible-null x4,
  # use-secure-value-for-secure-inputs x1), ZERO errors.
  az bicep build --file infrastructure/bicep/stacks/model1-shared.bicep --outfile <tmp>/model1-shared-arm.json
  # exit 0 — no warnings, ZERO errors.
  ```
  Inspected the compiled output: `customer-arm.json` has 7 `Microsoft.Resources/deployments` nested resources (matches its module count, minus one conditional not literally counted at compile time in this pass); `model1-shared-arm.json` has 16 — confirming `az bicep build` DOES flatten every module into a nested-deployment resource inside a **single** ARM JSON file, exactly the shape `ArmDeploymentResource.CreateOrUpdateAsync` expects. This also resolves a stale concern from an earlier Wave-4 session note ("pre-existing BCP035/037/053 errors in `stacks/model1-shared.bicep` + `model2-full.bicep` inherited from task 032") — whatever caused that has since been fixed for `model1-shared.bicep` (0 errors verified above); `model2-full.bicep` is out of scope for this task regardless (not an H2a template).
- **Storage target consistency** (POML step 2 — "reuse the SAME `provisioning-artifacts` container if that keeps the consumption story simple for H2a's handler"). Read `.github/workflows/deploy-bff-api.yml`'s H9 extension (task 116) in full — confirmed it targets a `provisioning-artifacts` container on a `${{ vars.PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT }}` repo-variable-named storage account, via `az storage blob upload --auth-mode login` (OIDC, no stored key), and that the account **does not exist yet** (task 116's own escalation, § 3 there). This workflow reuses the identical container + repo variable — **one storage account, one container, one consumption story** for H2a's future `Azure.Storage.Blobs` client, per the task's explicit instruction. See § 3 below — the live-ceremony gap is the SAME one task 116 already filed; this task does not file a second, separate ask.
- **Versioning scheme consistency** (POML step 3). Reused the identical `buildId = {UTC yyyy.MM.dd}-{github.run_number}` + separate `sha = github.sha` scheme from `build-provisioning-sidecar.yml` (task 115) and `deploy-bff-api.yml`'s H9 extension (task 116) — deterministic, monotonic, git-anchored, zero collision risk across same-day dispatches.

---

## 2. What was authored

### 2.1 `.github/workflows/publish-provisioning-arm-artifacts.yml` (NEW)

Triggers: `push` to `master` on `infrastructure/bicep/customer.bicep`, `infrastructure/bicep/stacks/model1-shared.bicep`, `infrastructure/bicep/modules/**`, or the workflow file itself — plus `workflow_dispatch` for on-demand publishes (e.g. to re-publish after a platform storage account lands, without needing a source-touching commit).

One job (`compile-and-publish`), steps in order:

| # | Step | Purpose |
|---|---|---|
| 1 | Compute build identifiers | `buildId` + `sha`, per § 1 versioning scheme |
| 2 | `az bicep install` + record version | Pins/verifies the Bicep compiler explicitly rather than relying on first-use auto-install; captures `bicepCliVersion` for the manifest |
| 3 | Compile `customer.bicep` -> `customer-arm-{buildId}.json` | Hard-fails the job on any compile error (no `continue-on-error` — an invalid ARM JSON is not a signal to carry forward, unlike task 116's advisory r3 gates) |
| 4 | Compile `stacks/model1-shared.bicep` -> `model1-shared-arm-{buildId}.json` | Same hard-fail posture |
| 5 | Compute SHA-256 + size per artifact | For the manifest's integrity-check fields |
| 6 | Azure Login (OIDC) | Identical `azure/login@v2` pattern as `deploy-staging`/`swap-production`/`build-provisioning-sidecar.yml`/task 116's extension — no new secret |
| 7 | Push both ARM JSON blobs | `az storage blob upload --auth-mode login --overwrite false` (immutable per-buildId blobs) |
| 8 | Generate + push manifest (versioned + `latest` pointer) | Written AFTER both blob uploads succeed (never point at a missing blob); versioned copy is immutable (`--overwrite false`), the `provisioning-arm-latest.json` pointer is mutable (`--overwrite true`) |
| 9 | Summary | `$GITHUB_STEP_SUMMARY` write for operator visibility |

### 2.2 `.github/workflows/schemas/provisioning-arm-manifest.json` (NEW)

A draft-07 JSON Schema for `provisioning-arm-{buildId}.json` / `provisioning-arm-latest.json`, validated locally (`jsonschema.Draft7Validator.check_schema` + a conformant sample instance both pass). Documents:
- `buildId` / `sha` / `publishedAt` / `bicepCliVersion` top-level fields
- a `templates` object with exactly two keys, `customer` and `model1-shared` (stable file-basename identifiers, chosen over raw `TenancyModel` enum strings so the manifest shape does not need to change if the tenancy-model vocabulary grows)
- each entry's `resolutionHint` — a human-readable restatement of `FileBicepTemplateInspector.ResolveTemplatePath`'s exact branch, so a future audit can confirm the manifest and the C# selection logic still agree
- each entry's `armJsonBlobName` / `sha256` / `sizeBytes` — everything task 123's H2a handler needs to resolve → download → verify → deploy

### 2.3 Design decision: manifest keyed by template identity, not by raw TenancyModel string

`FileBicepTemplateInspector.ResolveTemplatePath` is a two-branch ternary: `TenancyModel == "Model1Shared"` (case-insensitive) → `model1-shared.bicep`, **else** → `customer.bicep` (the `else` branch covers `Model2Dedicated` and any future value not yet enumerated — it is a fallback, not an exhaustive match list). Mirroring that literally as manifest keys (e.g. one key per every possible `TenancyModel` string) would be both wrong (can't enumerate an open-ended "else") and brittle (the manifest schema would need to change every time the `TenancyModel` vocabulary grows). Instead the manifest is keyed by the two **template identities** (`customer`, `model1-shared`), with each entry's `resolutionHint` documenting the exact selection rule in prose. Task 123's H2a handler applies the SAME branch already coded in `FileBicepTemplateInspector` to pick which manifest key to read — this workflow does not attempt to re-encode that branch as data.

---

## 3. Escalation — live-ceremony item (per outer-task instruction: document, do NOT create)

**The `provisioning-artifacts` blob container's storage account does not exist** — this is the SAME gap task 116 already filed (`notes/h9-artifact-publish-ci-coord-pr.md` § 3), not a new, separate ask. `infrastructure/bicep/platform-controlplane.bicep` provisions 7 resources (UAMI, App Service Plan, Log Analytics, Cosmos, App Insights, Key Vault, App Service) — no `Microsoft.Storage/storageAccounts` resource. Until that lands (plus the `Storage Blob Data Contributor` RBAC grant to the OIDC service principal + the `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` repo variable populated), this workflow's blob-upload steps fail cleanly (missing/empty account name) — no other job depends on this workflow, so the failure is fully contained to a single, easily-re-run workflow dispatch once the account exists.

**This task does NOT create the storage account, container, or RBAC grant** — per the outer-task instruction, flagged here for whichever Bicep task picks up the platform storage account (the same open item task 116 flagged; resolving it once unblocks BOTH task 116's and this task's artifact-publish steps).

**No new escalation was needed for the module-graph / template-spec-by-id trigger** — see § 1 above; both templates compose exclusively via local relative-path module references, so no nested-template-strategy change is forced onto task 123's handler design.

---

## 4. Runbook implication (for task 123's implementer)

- **H2a's SDK-port handler** (task 123, Wave G-2) resolves `provisioning-arm-latest.json` (or a pinned `provisioning-arm-{buildId}.json` if a specific version is required for reproducibility of an upgrade-mode drift comparison) from the `provisioning-artifacts` container via `Azure.Storage.Blobs`, selects the `customer` or `model1-shared` entry per `FileBicepTemplateInspector.ResolveTemplatePath`'s existing branch, downloads the referenced `armJsonBlobName`, verifies its SHA-256 against the manifest's `sha256` field, and passes the JSON content to `ArmDeploymentResource.CreateOrUpdateAsync` (with `WhatIfAtSubscriptionScopeAsync` available for the existing upgrade-mode drift-detection path, per design.md §4A row 1 / `IUpgradeDriftDetector`).
- **Releases that customer provisioning may consume MUST run this workflow** (or land via its `push`-to-`master` trigger on any `infrastructure/bicep/**` change in scope) so the `provisioning-artifacts` blob stays current — same runbook posture task 116 documented for the BFF artifact.
- **This workflow does NOT write to the Dataverse registry** — parity with task 116: it only PUBLISHES the candidate ARM JSON + manifest; task 123's handler is the one that actually deploys and (if applicable) records provenance at run time.

---

## 5. Acceptance criteria mapping (task 117's own POML)

| Criterion | Status |
|---|---|
| A coordination note exists with the drafted `az bicep build` step covering every Bicep template H2a deploys. | ✅ This note + the LIVE `publish-provisioning-arm-artifacts.yml` (both templates identified via `FileBicepTemplateInspector.ResolveTemplatePath`, § 1). |
| The drafted artifact versioning scheme matches task 116's established scheme (same storage container/versioning convention) for consumption-story consistency. | ✅ Same `provisioning-artifacts` container, same `vars.PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` repo variable, same `{UTC yyyy.MM.dd}-{run_number}` buildId scheme, same OIDC `azure/login@v2` auth pattern. |
| Local `az bicep build` against the identified template(s) succeeds with 0 errors as a pre-authoring sanity check (documented in this task's notes). | ✅ § 1 above — both templates compiled locally with Bicep CLI 0.46.1, exit code 0, zero errors (customer.bicep: 6 linter warnings only; model1-shared.bicep: zero warnings). |

---

*This file's title is retained as "coord-pr" for discoverability / cross-reference continuity with tasks 067/088/115/116's notes, even though — like those four — its content landed as a direct commit rather than a pending coordination artifact.*
