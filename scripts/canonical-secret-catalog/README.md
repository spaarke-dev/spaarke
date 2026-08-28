# Canonical Secret-Catalog Manifest + Generator (Phase H)

> **Authority**: spec.md FR-36 · §7.9 R1-R4 · [`AZURE-RESOURCE-NAMING-CONVENTION.md`](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) · [`naming-exception-registry.md`](../../projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md) · [`task-063-naming-standard-r1-handoff.md`](../../projects/code-quality-and-assurance-r3/notes/task-063-naming-standard-r1-handoff.md)
>
> **Delivers**: r3 KV federation design Phase 3b — the single source of truth from which the seeder, Configure script, tokens doc, and Bicep KV secret set are all generated.

---

## What this is

`manifest.yaml` is the **single source of truth** for every Key Vault secret used across the Spaarke fleet. `Invoke-CatalogGenerator.ps1` fans it out into four `generated/` artifacts that were, until Phase H, each hand-maintained (and drifted, per the r3 task 017 census — three casings for one AI-Search key, six orphan template references, four vault-naming conventions).

```
manifest.yaml
    |
    +--> generated/Seed-CustomerKeyVault.generated.ps1        (upsert every canonical KV secret slot)
    +--> generated/Configure-AppServiceSettings.generated.ps1 (App Service KV references, single-form syntax)
    +--> generated/appsettings.tokens.generated.md            (operator reference doc)
    +--> generated/kv-secrets.generated.bicep                 (Bicep KV secret-set module)
```

## Invocation

### Regenerate (default)

```powershell
pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1
```

Overwrites every file in `generated/`.

### Dry-run

```powershell
pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -DryRun
```

Prints planned outputs (name + byte count + line count) without writing anything.

### Verify (CI drift check)

```powershell
pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
```

Regenerates artifacts in memory + compares them byte-for-byte against `generated/`. Exits **0** if in sync, **2** with a per-file diff if drifted, **1** on validation failure.

Wire into CI to prevent operators from editing `generated/` by hand:

```yaml
# .github/workflows/canonical-secret-catalog-drift.yml (illustrative — coordinate with ci-cd-unit-test-remediation-r1 per r3 task-042 handoff)
- name: Verify canonical secret catalog is in sync
  shell: pwsh
  run: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
```

## Adding a new KV secret

1. **Edit `manifest.yaml`** — append (or slot in — order is irrelevant, output is alphabetically sorted) a new entry under `secrets:` with all required fields:

   ```yaml
   - canonical_name: "MyNew-Secret"
     category: "communication"      # or auth / identity / dataverse / data-services / spe / ai / email / monitoring / compose
     purpose: >-
       One-line human description of what this secret is for.
     consumers:
       - "BFF: MyModule:MySetting"
     rotation_cadence: "90-days"    # or manual-on-incident / N/A / 90-days-or-on-incident
     never_delete: false            # true ONLY for Dataverse-ClientSecret + BFF-API-ClientSecret (BINDING)
     exception_note: ""
     aliases: []                    # any drift spellings to alias-collapse (never emitted as separate secrets)
     value_source: "generated"      # from-existing-kv | from-bicep-output | from-run-parameter | generated
     app_settings:
       - "MyModule__MySetting"
     tags: ["communication"]
   ```

2. **Dry-run** to inspect the planned outputs:

   ```powershell
   pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -DryRun
   ```

3. **Regenerate** the four artifacts:

   ```powershell
   pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1
   ```

4. **Commit atomically** — the manifest edit + all four regenerated files in one commit. `-Verify` in CI will fail the PR otherwise.

## Adding an alias to a canonical secret

If an existing environment carries a drift spelling for a canonical secret (typically discovered during a naming-conformance audit), record it under `aliases:` on the canonical entry. **Do NOT create a separate manifest entry for the alias** — that perpetuates the drift the manifest exists to close.

Then, in a separate task (Phase H task 085 pattern):
1. Run the BINDING pre-check (LIVE App-Service settings + KV secret list + Dataverse-persisted config) per §7.9 R4.
2. Migrate consumers from alias to canonical.
3. Delete the alias from the vault.
4. Remove the alias entry from `manifest.yaml` and regenerate.

## Adding a new vault-name exception

Vault-name exceptions (currently only `dev -> spaarke-spekvcert`) live in `vault_name_exceptions:` on the manifest AND in [`naming-exception-registry.md`](../../projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md). Both must be updated. The generator surfaces the exception in `appsettings.tokens.generated.md`; downstream Bicep parameterizes the vault name at deployment time.

## BINDING invariants

The generator refuses to write outputs when either invariant is violated:

1. `Dataverse-ClientSecret` MUST exist in `secrets:` with `never_delete: true`.
2. `BFF-API-ClientSecret` MUST exist in `secrets:` with `never_delete: true`.
3. `vault_name_exceptions.dev` MUST equal `spaarke-spekvcert`.

Rationale in [`naming-exception-registry.md`](../../projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md) rows 1-3.

## Determinism contract

Two invocations of the generator against the same `manifest.yaml` produce **byte-identical** outputs:

- Secrets are sorted alphabetically by `canonical_name`.
- Consumer / app-setting / tag / alias lists are sorted alphabetically inside each secret.
- Vault-name exceptions are iterated in alphabetical key order.
- All files write UTF-8 without BOM, LF line endings, trailing newline.

`-Verify` is the enforcement point: any hand edit to `generated/` will be caught.

## Requirements

- PowerShell 7.0+ (pwsh)
- [`powershell-yaml`](https://github.com/cloudbase/powershell-yaml) 0.4.12+ — install once per machine:
  ```powershell
  Install-Module -Name powershell-yaml -Scope CurrentUser
  ```

## Related

- [`scripts/naming-conformance-check.ps1`](../naming-conformance-check.ps1) — read-only naming conformance gate owned by `code-quality-and-assurance-r3` (task 063).
- [`scripts/seed-data/`](../seed-data/) — same manifest-driven pattern applied to the AI seed chain (H12a).
- [`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/KvSecretsPopulation/`](../../src/server/services/Sprk.Provisioning.ControlPlane/Handlers/KvSecretsPopulation/) — H4 handler which will consume the manifest via `IKvSecretManifest` (currently interim `StaticKvSecretManifest`; swap to a file-backed impl reading the manifest at runtime after this task lands).
