# Task 084 — Deviations Report

> **Task**: 084-canonical-secret-catalog-manifest
> **Date**: 2026-08-17
> **Rigor**: FULL (opus/xhigh per POML metadata)
> **Status**: ✅ Complete

## Summary

**No material deviations from the POML specification.** All acceptance criteria met on the first task-execute pass. Task 084 delivers the canonical secret-catalog manifest + generator per spec.md FR-36 + §7.9 R1–R4, closing r3 KV federation design Phase 3b.

## Deliverables

| Deliverable | Path | Notes |
|---|---|---|
| Manifest (single source of truth) | `scripts/canonical-secret-catalog/manifest.yaml` | 32 canonical secret entries + `dev -> spaarke-spekvcert` vault exception |
| Generator (PowerShell) | `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` | 3 modes: default write, `-DryRun`, `-Verify` |
| Operator docs | `scripts/canonical-secret-catalog/README.md` | Invocation, add-new-secret workflow, CI wiring |
| Generated outputs (baseline) | `scripts/canonical-secret-catalog/generated/` | 4 artifacts, checked in as CI drift-check baseline |

Generated files:
- `Configure-AppServiceSettings.generated.ps1` (6,642 bytes)
- `Seed-CustomerKeyVault.generated.ps1` (23,964 bytes)
- `appsettings.tokens.generated.md` (23,921 bytes)
- `kv-secrets.generated.bicep` (25,779 bytes)

## Acceptance-criterion verification results

| # | Criterion (POML) | Method | Result |
|---|---|---|---|
| 1 | Manifest enumerates every KV secret with the schema | Manifest inventory from all 4 hand-maintained sources (seeder, template, tokens.md, Bicep) | ✅ 32 canonical entries + aliases documented |
| 2 | Dataverse-ClientSecret + BFF-API-ClientSecret carry `never_delete: true`; spaarke-spekvcert carries `exception_note` | Manifest source + `Test-BindingNeverDeleteInvariant` + `Test-DevExceptionInvariant` at generator startup | ✅ Both binding secrets present with `never_delete: true`; dev exception codified in `vault_name_exceptions.dev` with rationale block |
| 3 | `-DryRun` prints planned outputs without writing | Ran `Invoke-CatalogGenerator.ps1 -DryRun` | ✅ 4 planned outputs listed with byte + line counts, zero writes |
| 4 | Two runs produce BYTE-IDENTICAL outputs (deterministic) | Ran generator twice + SHA256 comparison of all 4 artifacts | ✅ All 4 SHA256 hashes identical |
| 5 | `-Verify` exits 0 on clean, non-zero on drift, with diff | Baseline verify (exit 0) + hand-edit drift injection (exit 2 with per-line diff) + manifest-add drift (exit 2 with cross-file diff) | ✅ Clean → exit 0, drift → exit 2 with helpful diff |
| 6 | Never-delete guard fails LOUDLY | Flipped `never_delete: true` → `false` on Dataverse-ClientSecret + ran generator | ✅ Exit 1 + explicit "BINDING never-delete invariant violated" diagnostic citing spec.md MUST rules + r3 handoff §4a + naming-exception-registry.md |
| 7 | PSScriptAnalyzer clean (zero errors) | `Invoke-ScriptAnalyzer` on generator | ✅ 0 errors, 0 warnings, 0 info (intentional design choices annotated via `SuppressMessageAttribute` with rationale) |

## §11 justification (from POML — cited here for commit body)

- **Existing**: Closest neighbors are individual seeder scripts + Configure scripts + tokens.md doc + Bicep secret set — each hand-maintained. Grep for `canonical-secret-catalog` returned none.
- **Extension**: No — the drift IS that 4 outputs coexist with no authoritative source. Extending any one perpetuates the drift; a manifest + generator is the design intent per FR-36 + r3 assessment §Phase 3b.
- **Cost-of-doing-nothing**: Without the manifest, 4-way drift persists indefinitely (already caused 3 AI-Search-key aliases in 3 casings + 6 orphan template references per §7.9 rename map; spec.md § New Components row 7 explicitly cites this failure mode). Every KV addition risks another alias entering only one output. Manifest is the single source that closes the drift.

## Design decisions taken (noted for future editors)

1. **YAML over JSON.** POML step 4 explicitly says "parses manifest.yaml." Uses `powershell-yaml` module — same pattern as sibling `scripts/seed-data/Invoke-SeedManifest.ps1` for consistency. Failure mode gives operator clear install instruction.
2. **Alphabetical sort by canonical-name** is the determinism enforcement point. Manifest edit order does not matter; consumers always see the same output.
3. **UTF-8 without BOM + LF line endings.** Determinism contract. `Compare-Artifacts` normalizes CRLF → LF defensively before comparison (guards against `git autocrlf=true` on Windows checkouts).
4. **Aliases live on the canonical entry, never as separate secrets.** The generator refuses to emit alias entries as separate KV slots — that would perpetuate drift. Alias collapse (task 085) is a manual pre-checked operation.
5. **`from-existing-kv` secrets get a "refused to overwrite" guard in the seeder.** The binding never-delete pair carries this behavior explicitly — running the seeder with `-SeedPlaceholders` refuses to touch existing values.
6. **Bicep module never embeds cleartext.** `secretValues` param is `@secure()` and defaults to `{}`; slots not populated get a placeholder marker. Real values are populated via Bicep upstream module outputs or out-of-band operator action.
7. **`SuppressMessageAttribute` used only for four intentional design choices**: `PSAvoidUsingWriteHost` (colored operator diagnostics), `PSUseShouldProcessForStateChangingFunctions` (pure string factories, not state-changing), `PSUseSingularNouns` (functions process multiple items), `PSUseBOMForUnicodeEncodedFile` (contract requires no-BOM). All annotated with justification strings — reviewer can audit.

## No ADR tensions surfaced

Task 084 is a scripts-only, single-source-of-truth manifest. It touches no BFF DI (root CLAUDE.md §10), introduces no new server code, and does not challenge any ADR MUST rule. §11 justification stands as-is; no §6.5 ADR tension applies.

## What NOT touched (per POML "What NOT to touch")

- `.claude/` paths — confirmed (grep + no edits).
- `src/server/**` — confirmed (no edits; only `Sprk.Provisioning.ControlPlane/Handlers/KvSecretsPopulation/StaticKvSecretManifest.cs` READ for reference; not modified).
- `src/server/api/Sprk.Bff.Api/Api/RegistrationEndpoints.cs` — confirmed (grep confirms no edit).
- `infrastructure/bicep/**` — confirmed (task 086 owns IaC alignment consuming the generator's Bicep output).

## Downstream consumers (for reviewer context)

Two tasks depend on this manifest + generator being in place:

- **Task 085 — Alias collapse for AI Search key** (Phase H): consumes the `aliases` field on the `AiSearch--AdminKey` canonical entry as the drift-target list.
- **Task 086 — IaC alignment**: replaces inline `Microsoft.KeyVault/vaults/secrets` blocks in `customer.bicep` / `model2-full.bicep` / `model1-shared.bicep` with a module reference to the generator's `kv-secrets.generated.bicep` output. The generator OWNS the Bicep secret-set output going forward (per POML constraint).
- **`StaticKvSecretManifest.cs`** in `Sprk.Provisioning.ControlPlane` remains the interim `IKvSecretManifest` implementation. When the file-backed reader lands (post-084 follow-on, out of scope for this task), the DI registration flips in Program.cs and H4 handler + tests remain untouched (parity with H1 Null-probe → real ARM probe pattern).
