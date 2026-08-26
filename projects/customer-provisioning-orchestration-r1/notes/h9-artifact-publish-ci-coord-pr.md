# Coord-Note-Turned-Direct-Commit — H9 Artifact Publish Workflow Extension (task 116)

> **APPLIED 2026-08-19** — task 116's POML was authored under the OLD coordination model ("draft the coord PR, do not commit `.github/workflows/**` directly"). By the time task 116 executed, r1's CLAUDE.md had already been updated (commit `8281045052`) to formally take direct-commit ownership of `.github/workflows/**` for Phase C'' scope — the same governance shift that let tasks 067/088/115's queued coord-notes get applied directly on 2026-08-19. **This task DEVIATES from its own POML's literal acceptance-criteria wording** (which describes "a coordination note exists containing the full drafted YAML diff") **per CLAUDE.md §6.5 Path C (pivot to comply with the now-current, more-permissive governance)**: rather than producing a draft-only coord-note for a dormant worktree to apply later, the extension was authored DIRECTLY into `.github/workflows/deploy-bff-api.yml`. This note is kept (rewritten) as the audit trail / design-rationale record, not as a pending-application artifact.
>
> **Author**: customer-provisioning-orchestration-r1 task 116 (Phase C'' Wave G-1)
> **Date**: 2026-08-19
> **Target file (committed)**: `.github/workflows/deploy-bff-api.yml` (extended, not new)
> **New file (committed)**: `.github/workflows/schemas/bff-artifact-manifest.json` (manifest JSON Schema)
> **Deps**: none (per POML `<deps>none</deps>`)

---

## 0. Deviation record (CLAUDE.md §6.5 Path C)

- **What the POML said**: "author it as a coordination note + drafted YAML diff, per the same coordinated-PR discipline as task 115" — i.e., produce a note describing the diff for a separate owner to apply.
- **What actually happened**: the calling instruction for this task execution explicitly superseded that discipline, citing r1's CLAUDE.md "Coordination with other worktrees" section (rewritten as of commit `8281045052`, 2026-08-19): *"r1 holds direct ownership only because `ci-cd-unit-test-remediation-r1`'s declared 28-day window expired with that worktree dormant... re-check this condition before any FUTURE r1 task touches `.github/workflows/**`."* That re-check was performed (see § 1 below) and the condition still holds — the ci-cd-r1 worktree remains dormant (last commit `d4538dfde`, 2026-06-28, per task 115's own coord-note) — so the same direct-ownership posture task 115 used for `build-provisioning-sidecar.yml` applies here too.
- **Path chosen**: **C — pivot to comply** with the CURRENT (more permissive, already-adopted) governance rather than manufacture a coord-note for an owner who no longer needs one. This is not a violation of any ADR MUST/MUST NOT rule; it is a straightforward re-application of a decision the project already made for tasks 067/088/115.
- **Rationale**: producing a draft-only note when the direct-commit path is already open and already used twice this same day (067/088/115 applied; 115 itself was literally the sidecar-workflow precedent) would re-introduce exactly the multi-week coord-note backlog r1 escalated and resolved on 2026-08-19. Committing directly is strictly more useful (the workflow file is now real, testable, reviewable in-PR) with zero added risk — this is a pure CI-workflow addition, additive-only, fully reversible (delete the new steps + the schema file).

---

## 1. What was verified before authoring

- `ci-cd-unit-test-remediation-r1` worktree activity: unchanged from task 115's finding same-day — dormant since `d4538dfde` (2026-06-28).
- `projects/customer-provisioning-orchestration-r1/CLAUDE.md` "Coordination with other worktrees" section confirmed to carry the 2026-08-19 direct-ownership note (quoted in § 0 above) at the time this task executed.
- `.github/workflows/deploy-bff-api.yml`'s existing `build` job: `dotnet publish` (Publish step) → `actions/upload-artifact@v6` (`bff-api-build`, retention 7 days) — confirmed present, unmodified in structure; the H9 extension is appended AFTER the existing "Upload build artifact" step, before `test:` job begins.
- `.github/workflows/build-provisioning-sidecar.yml` (task 115, applied same-day) reused as the canonical OIDC + repo-variable-placeholder pattern for this extension (`azure/login@v2` with `secrets.AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID`; `vars.*` for a non-secret resource-name placeholder).
- `Sprk.Provisioning.ControlPlane.Handlers.BffDeploy.IR3GateVerifier` / `DotnetR3GateVerifier` (the H9 handler's INTERIM gate-verification seam, task 052) read in full — this is the authoritative source for gate naming + semantics (`R3GateKind`: Analyzers/GodClassRatchet/ArchTests/NamingConformance/GraphAppRoleParity; `R3GateStatus`: Passed/Skipped/Failed) and for the exact test filters each gate uses today (`GodClassGuardTests`, `TenantIsolation`, `naming-conformance-check.ps1` — no live-Azure gates run inline). The manifest's `gates` object mirrors this exactly so task 132 (H9 re-scope, Wave G-3) can deserialize `latest.json` straight into the existing `R3GateStatus` enum with zero new vocabulary.
- `scripts/naming-conformance-check.ps1` — confirmed it takes only `-SelfTest`, no `-CustomerId` parameter (the L2 `DotnetR3GateVerifier` code passes a `-CustomerId` argument the script does not declare — a pre-existing, unrelated L2 mismatch, NOT touched by this task; the CI invocation here correctly omits it since this is a static repo-wide scan, not a per-customer check).
- `infrastructure/bicep/platform-controlplane.bicep` — confirmed NO storage account resource exists yet (7 resources: UAMI, App Service Plan, Log Analytics, Cosmos, App Insights, Key Vault, App Service). The `provisioning-artifacts` blob container therefore cannot be created by this task — see § 3 Escalation.

---

## 2. What was authored (summary — see the files themselves for full content)

### 2.1 `.github/workflows/deploy-bff-api.yml` — `build` job extension

11 new steps appended to the existing `build` job, after "Upload build artifact":

| # | Step | Purpose |
|---|---|---|
| 1 | Compute build identifiers (buildId + sha) | `buildId = {UTC yyyy.MM.dd}-{github.run_number}` (matches the sidecar workflow's `semver_tag` convention — deterministic, monotonic, human-sortable, git-anchored via the separate `sha` field). **This resolves the POML's escalation trigger**: the version source is neither a raw timestamp nor unstable — it is date + monotonic run-number, so no escalation was needed. |
| 2 | Gate: god-class ratchet | `dotnet test ... --filter FullyQualifiedName~GodClassGuardTests`, `continue-on-error: true` (captured, not job-aborting) |
| 3 | Gate: tenant-isolation ArchTests I1-I5 | `dotnet test ... --filter FullyQualifiedName~TenantIsolation` — matches `DotnetR3GateVerifier.RunArchTestsGateAsync`'s exact filter, i.e. the manifest's `archTests` key is the 5 I1-I5 tests, NOT the full 65-test `Spaarke.ArchTests` suite |
| 4 | Gate: naming-conformance | `./scripts/naming-conformance-check.ps1` (no args — static scan) |
| 5 | Translate gate outcomes to R3GateStatus values | Maps step `.outcome` (success/failure) → `Passed`/`Failed` string; `r3AnalyzersAsErrors` is hard-set `Passed` here (see § 2.3) |
| 6 | Create artifact zip | `bff-api-{buildId}.zip` from `./publish` (PDBs excluded, matching the repo's NFR-01 "excl. PDBs" size-reporting convention); captures `sizeBytes` |
| 7 | Azure Login (OIDC) | Same `azure/login@v2` pattern as the existing `deploy-staging`/`swap-production` jobs in this same file — no new secret |
| 8 | Push artifact zip to blob | `az storage blob upload --auth-mode login` (OIDC, no stored key) |
| 9 | Generate + push `latest.json` manifest | Written AFTER the zip upload succeeds (steps are sequential, no `continue-on-error` on either upload step) — satisfies the "never point at a missing blob" ordering requirement structurally, not just by convention |
| 10 | Fail job if any r3 gate is red | `if: always()` — runs after the manifest is published so the failure is durably recorded, but still marks the workflow run red so operators see it immediately |

### 2.2 `.github/workflows/schemas/bff-artifact-manifest.json` (NEW)

A draft-07 JSON Schema for `latest.json`, documenting every field + the `gates` sub-object with the exact `Passed`/`Skipped`/`Failed` enum (matching `R3GateStatus` verbatim) and a `description` on `graphAppRoleParity` explaining why it is always `Skipped` from this workflow (see § 2.3). This satisfies the "is the manifest schema documented" reporting requirement independent of this note.

### 2.3 Design decision: `r3AnalyzersAsErrors` and `graphAppRoleParity` are NOT independently computed

- **`r3AnalyzersAsErrors`**: the existing "Build" step (earlier in the SAME job) already runs with `Directory.Build.props`' `TreatWarningsAsErrors=true` and has no `continue-on-error`. GitHub Actions aborts a job on a failed step by default — so reaching the H9-extension steps at all already proves this gate passed. Re-running `dotnet build ... /warnaserror` a second time (as `DotnetR3GateVerifier.RunAnalyzersGateAsync` does at provision time today) would be redundant work inside the SAME job. Recorded as a hard-coded `"Passed"`.
- **`graphAppRoleParity`**: this is architecturally NOT a property of the platform-BFF build artifact — it is a property of the DEPLOYMENT TARGET (whether a specific customer's UAMI service principal has the 14 expected Graph app-role assignments per `GraphAppRoles.cs`). A single platform-wide `latest.json` cannot carry a per-customer answer to that question, and the only place this check currently runs live is the ADVISORY `graph-app-role-parity` job in `nightly-health.yml` (itself `continue-on-error: true` at the job level, so even that job's own `conclusion` is deliberately masked — not a reliable green/red signal to scrape). Rather than fabricate a signal by querying a masked nightly-run conclusion via the GH API (fragile, and the wrong architectural layer for the answer regardless of masking), this manifest key is **always recorded `"Skipped"`** with a diagnostic explaining why, and the schema's field description explicitly instructs task 132's H9 handler to perform its OWN live per-environment Graph check rather than trust this key. This is a deliberate, documented scoping decision — not a silent gap — and is the one place this task's design departs from a literal "all 5 gates ride in the manifest" reading of DS-4 §5's prose. It does not weaken the H9 contract: the manifest's 4 artifact-scoped gates (`r3AnalyzersAsErrors`, `godClassRatchet`, `archTests`, `namingConformance`) are real, computed-in-this-job signals; `graphAppRoleParity` was never something an artifact-level manifest could honestly answer.

---

## 3. Escalation — live-ceremony item (per outer-task instruction: document, do NOT create)

**The `provisioning-artifacts` blob container's storage account does not exist.** `infrastructure/bicep/platform-controlplane.bicep` currently provisions 7 resources (UAMI, App Service Plan, Log Analytics, Cosmos, App Insights, Key Vault, App Service) — no `Microsoft.Storage/storageAccounts` resource. Three live-ceremony items block this workflow from succeeding if triggered today (documented in the workflow's own step comments too):

1. **Create the storage account + `provisioning-artifacts` blob container** (canonical naming, e.g. `stsprkplatform{env}`) — likely belongs in `platform-controlplane.bicep` alongside the other platform-shared resources, or its own small Bicep module. Coordinate with the Bicep task owners (108 ✅, 101 ✅, 109 pending per the outer-task's coordination-hygiene note) before adding a new resource to `platform-controlplane.bicep`.
2. **Grant the OIDC service principal** (`secrets.AZURE_CLIENT_ID` — the same identity `deploy-staging`/`swap-production`/`build-provisioning-sidecar.yml` already use) `Storage Blob Data Contributor` RBAC on that account, so `az storage blob upload --auth-mode login` succeeds without a stored key.
3. **Populate the `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` GitHub Actions repo variable** with the account name once it exists (a storage-account name is not confidential — this follows the exact `vars.SIDECAR_ACR_LOGIN_SERVER` placeholder precedent from task 115).

Until all three land, the "Push artifact zip to provisioning-artifacts blob" step fails cleanly (empty/missing account name) — the rest of the existing `deploy-bff-api.yml` pipeline (build/test/deploy-staging/verify/swap/verify-production/rollback) is completely unaffected, since this is an additive step sequence inside the `build` job only. This is the same "fails cleanly until the dependency lands" posture task 115 documented for `vars.SIDECAR_ACR_LOGIN_SERVER` / the ACR resource.

**This task does NOT create the storage account, container, or RBAC grant** — per the outer-task instruction, this is flagged here as a live-ceremony escalation item for the project owner / whichever Bicep task picks up the platform storage account.

---

## 4. Runbook implication (design.md DS-4 §5 point 4)

- **Releases that customer provisioning may consume MUST run this workflow** (or a `workflow_run` follow-on) so the `provisioning-artifacts` blob stays current. `deploy-bff-api.yml` is `workflow_dispatch`-only (no `push: master` auto-trigger, per the dotnet-10-upgrade-r1 2026-08-11 change already documented in this file's header) — so the artifact-publish side effect only happens when an operator explicitly dispatches this workflow, same as the existing deploy behavior.
- **`sprk_bffversion` registry column** (per `ADR-044-dataverse-guid-canonicalization.md` + this project's CLAUDE.md registry-key-pattern list) records the deployed artifact's `buildId` once H9 (task 132) actually deploys it to a customer's BFF App Service — this workflow only PUBLISHES the candidate artifact + manifest; it does not write to the registry (H9 does, at actual deploy time).
- **H9 refuses to deploy if the manifest's gate results are missing or red** (design.md DS-4 §5 point 4, verbatim): task 132's handler MUST treat a manifest fetch failure, a missing `gates` object, or any of `r3AnalyzersAsErrors`/`godClassRatchet`/`archTests`/`namingConformance` being `"Failed"` as a hard refusal — this is documented as a `MUST` in the manifest schema's `gates` description (§ 2.2) and repeated here for the task-132 implementer.

---

## 5. Acceptance criteria mapping (task 116's own POML)

| Criterion | Status |
|---|---|
| A coordination note exists containing the full drafted YAML diff for `deploy-bff-api.yml`'s build job extension. | Superseded per § 0 — the actual diff is now LIVE in `.github/workflows/deploy-bff-api.yml`, not merely drafted here. This note documents the design + deviation instead of a pending diff. |
| The drafted diff pushes `bff-api-{version}.zip` AND `latest.json` to a `provisioning-artifacts` blob container, both OIDC-authenticated (no stored secret). | ✅ Both `az storage blob upload` calls use `--auth-mode login` against the existing `azure/login@v2` OIDC session; zero stored keys/secrets added. |
| `latest.json`'s documented schema includes `version` (as `buildId`), `sha`, `size` (as `sizeBytes`), and `gates` fields sourced from the SAME job's existing computed values (not a re-run). | ✅ See `.github/workflows/schemas/bff-artifact-manifest.json`. 4 of 5 gates are genuinely computed in-job; `graphAppRoleParity` is deliberately `Skipped` (§ 2.3) rather than fabricated. |
| The coordination note explicitly documents the runbook implication (releases customer provisioning may consume MUST run this workflow) and the `sprk_bffversion` registry-column consumption point. | ✅ § 4 above. |

---

*This file's title is retained as "coord-pr" for discoverability / cross-reference continuity with tasks 067/088/115's notes, even though — like those three — its content landed as a direct commit rather than a pending coordination artifact.*
