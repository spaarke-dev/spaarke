# Task 069 — Seed Manifest + Generator Authoring Notes

> **Deliverables**: `scripts/seed-data/manifest.yaml` (declarative catalog) + `scripts/seed-data/Invoke-SeedManifest.ps1` (thin orchestrator).
> **Rigor**: FULL. Model tier: opus @ high (per POML — high-blast-radius declarative schema authoring).
> **Author**: task-execute sub-agent (Wave 3 Batch 3A, parallel dispatch — worktree `customer-provisioning-orchestration-r1`).
> **Depends on**: Task 004 drift resolution (`notes/ai-seed-drift-resolution-2026-08.md` §4a matrix).
> **Consumed by**: Task 070 (H12a AI seed chain handler).

## What was authored

1. **`scripts/seed-data/manifest.yaml`** — 12 active artifacts + 5 retired-artifact exclusions. Every active row cites its `driftMatrixRef` back to task 004's §4a / §5a decision. Playbooks are the HYBRID case (MVP `playbooks.json` remains authoritative for the 4 shipped playbooks — Quick Doc Review, Contract Analysis, Document Profile PB-011, Risk Scan — pending a Phase C'-post per-playbook R7 mirror export). `aimodeldeployment` is a PLACEHOLDER row (H12c populates); it is declared for schema completeness and the generator emits a PLACEHOLDER marker rather than attempting a deployment. `-multinode` playbook, `spaarke-playbook-embeddings` index, phantom `tools.json`, MVP `actions.json` monolith, and any dispatcher-shaped appsettings key are all in `retiredArtifacts` with citations (ADR-039, task-004 drift resolution, `$comment-051-ruling`).

2. **`scripts/seed-data/Invoke-SeedManifest.ps1`** — thin PowerShell 7+ orchestrator. Uses `powershell-yaml` (0.4.12, already installed) to parse the manifest, validates schema (required fields + deployer script existence), runs a mandatory retired-artifact check (fails fast on any id / path / name overlap between `artifacts` and `retiredArtifacts`), topologically sorts by `dependsOn` with clear diagnostics on unknown or cyclic dependencies, and then fans out to the existing seeder scripts (`Deploy-TypeLookups.ps1`, `Deploy-Knowledge.ps1`, `Deploy-Skills.ps1`, `Deploy-Playbooks.ps1`, `Deploy-OutputTypes.ps1`, `Seed-PlaybookConsumers.ps1`) with per-mode parameter binding. Four modes: `-DryRun` (default, prints planned invocations, never touches env), `-Live` (executes), `-Verify` (invokes `Seed-PlaybookConsumers.ps1 -DiffOnly` for the round-trip idempotency assertion), `-RetiredCheckOnly` (repo-hygiene lint gate).

## POML acceptance criteria — verification evidence

| # | Criterion | Evidence |
|---|---|---|
| 1 | manifest.yaml enumerates every AI-seed artifact | 12 active artifacts, each with `id / type / authoritativeSource / dependsOn`. Verified by generator loading with `-DryRun` (all 12 planned). |
| 2 | Generator parses manifest + invokes seeders in dependency order | Topological sort output: `type-lookups → aimodeldeployment → knowledge → skills → input-schemas → output-schemas → tools-r7 → actions-r7 → playbooks-mvp → action-outputschema-patches → output-types → playbook-consumers`. |
| 3 | -DryRun prints planned invocations without executing | Verified — dry-run against synthetic env URL prints `[DRY-RUN] pwsh -File "…" …` lines for each deployer, exits 0, no side effects. |
| 4 | Second run against same dev env is a no-op | **Deferred (env-credentialed)** — see "Deviations" below. Underlying seeders each declare their idempotency mode in the manifest (`existence-check-then-insert` for MVP scripts, `alt-key-upsert` for `Seed-PlaybookConsumers.ps1`). Generator provides `-Verify` mode wrapping `-DiffOnly` for the mechanical round-trip proof at H12a run-time. |
| 5 | Zero references to retired artifacts in active list | 7 grep hits for `spaarke-playbook-embeddings|dispatcher` — all inside header comments or `retiredArtifacts` block; ZERO inside the `artifacts` block (verified via block-slice grep in the task session). Retired-check pass verified by `-RetiredCheckOnly` mode. |
| 6 | Missing / cyclic dependency causes clear diagnostic | Three negative tests exercised: unknown-artifact-x → exit 1 with `Artifact 'alpha' declares dependency on unknown artifact 'unknown-artifact-x' — cannot resolve seed order. Known artifacts: alpha`; alpha ↔ beta cycle → exit 1 with `Cyclic dependency detected in manifest — artifacts stuck in cycle: alpha, beta`; retired-artifact id / name overlap → exit 1 with per-violation lines. |
| 7 | PSScriptAnalyzer green | Clean with project `PSScriptAnalyzerSettings.psd1` (which already excludes `PSAvoidUsingWriteHost` project-wide — line 46, "Write-Host is intentional in deployment/interactive scripts"). Two real warnings surfaced pre-fix (`PSReviewUnusedParameter -Force`, `PSUseBOMForUnicodeEncodedFile`) were addressed: `-Force` is now a properly-scoped function parameter, file re-saved with UTF-8 BOM. |

## Deviations

- **AC #4 (twice-run idempotency)** was not executed against a live Dataverse env in this session — the parallel-dispatch context did not include an `az login`-authenticated shell and none of the sibling agents (034 Bicep test / 041 H0 preflight / 042 H0.5 endpoint) require it either. The generator's `-Verify` mode wraps `Seed-PlaybookConsumers.ps1 -DiffOnly` which IS the mechanical H12a idempotency assertion; the H12a handler task (070) will exercise both `-Live` then `-Verify` back-to-back against `spaarkedev1` as part of its acceptance suite. All seeder scripts wrapped by this generator already have their own idempotency semantics per the manifest's `deployer.idempotencyMode` field (`existence-check-then-insert` for MVP scripts — see `Deploy-Actions.ps1 Test-RecordExists`; `alt-key-upsert` for `Seed-PlaybookConsumers.ps1` — see its header lines 20-26).

- **Generator does NOT execute per-file loops for R7 artifacts** (`actions-r7`, `tools-r7`, `input-schemas`, `output-schemas`, `action-outputschema-patches`). These are declared in the manifest with `deployer: null` and `deployerOwnedBy: H12a`. This is intentional per POML constraint *"Manifest is declarative YAML; generator is a thin PowerShell orchestrator — no seeding logic in the generator itself; it fans out to the existing seeder scripts"*. The H12a handler (task 070) owns the per-file loops (drift-resolution §5a M2/M3/M7 deltas). Dry-run output shows these as `PENDING — no deployer script yet; owned by H12a`.

## Step 9.5 Quality Gates

- **PSScriptAnalyzer** (Info + Warning + Error, project settings): CLEAN.
- **Manifest self-validation**: passes the generator's schema check + retired-check.
- **Retired-artifact enforcement (ADR-039)**: 5 exclusions declared, 0 violations.
- **ADR-039 compliance**:
  - Terminal artifact = `playbook-consumers` (Binding table — the single AI routing surface).
  - `-multinode` playbook explicitly excluded (frozen-engine amendment 2026-07-05).
  - `spaarke-playbook-embeddings` explicitly excluded (retired index).
  - Dispatcher-shaped appsettings keys blocked by `retiredArtifacts.dispatcher-any` keyPattern.
- **ADR-013 compliance**: Generator does not inject or invoke any AI-internal type (`IOpenAiClient` / `IPlaybookService`) — it wraps existing PowerShell seeders that talk to Dataverse via `az` token acquisition + REST. No BFF surface added.
- **CLAUDE.md §11 three-question gate** (from POML `<justification>` block):
  - Existing: two imperative scripts (`Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1`) with no manifest binding them.
  - Extension: not viable — the drift IS that they coexist without an authoritative declaration; extending either perpetuates it.
  - Cost of doing nothing: R14 two-source drift persists; new customers stand up with an ambiguous seed set; next artifact addition compounds it (spec.md § New Components row 7 explicitly cites this failure mode).

## Downstream contract (task 070 H12a)

H12a reads `scripts/seed-data/manifest.yaml`, filters `artifacts` where `scope == 'ai-seed'`, and either:
- Invokes `Invoke-SeedManifest.ps1 -Live` for the fully-scripted subset, then handles the R7 `PENDING` artifacts via per-file loaders (H12a-owned code implementing drift-resolution §5a M2/M3/M7); or
- Re-implements the topological loop in C# (`ProvisionCustomerJobHandler.Ai.SeedChain`) using the manifest as data, invoking each deployer via `Process.Start`.

Either shape is compatible with the manifest; H12a picks based on `Sprk.Bff.Api.Services.Registration` conventions.

## Files created

- `scripts/seed-data/manifest.yaml` (~340 lines)
- `scripts/seed-data/Invoke-SeedManifest.ps1` (~530 lines)
- `projects/customer-provisioning-orchestration-r1/notes/task-069-seed-manifest-authoring.md` (this file)
