# Manifest-Driven Secret Catalog Pattern

> **Last Reviewed**: 2026-10-01
> **Reviewed By**: customer-provisioning-orchestration-r1 T245a — `from-intake-parameter` + `written-by-h3` added; `per_env_source` closed set; the run-parameter secret channel has no writer. (T226, 2026-09-30, rewrote it against the code.)
> **Status**: Current.

## When

Load this pattern when:
- Adding a new provisioning handler that seeds or reads KV secrets.
- Adding a new KV secret that BFF (or another consumer) references via `@Microsoft.KeyVault(...)`.
- Debugging a `@Microsoft.KeyVault(...)` reference that does not resolve at runtime.
- Extending `per_env_settings` (App Service settings applied by the H4b bulk-set).
- Reviewing a PR that touches `scripts/canonical-secret-catalog/manifest.yaml`.

## Read These Files (canonical source)

1. `scripts/canonical-secret-catalog/manifest.yaml` — **single source of truth**. `secrets:` holds one entry per Key Vault secret (`canonical_name`, `category`, `purpose`, `consumers`, `rotation_cadence`, `never_delete`, `exception_note`, `aliases`, `value_source`, `app_settings`, `tags`); `per_env_settings:` holds App Service settings that are literals, handler outputs or intake values, never KV references. A non-literal `per_env_source` must be one of the closed set in `…/BulkAppSettings/PerEnvSourceCatalog.cs` (`from-h2a-output:*`, `from-h3-output:bff_app_client_id`, `from-intake-parameter:*`) — the reader rejects an unknown source or a mislabelled origin. Changing behavior means changing this file, not the handlers.
2. `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` — deterministic generator. Writes four artifacts to `generated/`: `kv-secrets.generated.bicep` (called by `infrastructure/bicep/customer.bicep`), `Seed-CustomerKeyVault.generated.ps1`, `Configure-AppServiceSettings.generated.ps1` (run by H4b) and `appsettings.tokens.generated.md`. `-Verify` proves byte-identical regeneration. Never hand-edit `generated/`.
3. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/FileKvSecretManifest.cs` — H4's reader (the manifest is embedded in the Core assembly). It refuses an unrecognized `value_source` and enforces the BINDING never-delete invariant on the raw document.
4. `…/KvSecretsPopulation/H4KvSecretsPopulationHandler.cs` + `KvSecretValueResolver.cs` — H4 writes every entry to the customer's own vault except the `written-by-h3` ones (H3 writes those itself, after H4); the resolver maps each `value_source` to where its value comes from. `RunContextContractTests` rule (g) proves every entry has a source H4 can reach, or is a pinned gap with an owning task.
5. `…/BulkAppSettings/H4bBulkAppSettingsHandler.cs` — runs the generated Configure script: one batched settings write per slot → one restart cycle.
6. `infrastructure/bicep/customer.bicep` (`kvSecretValues`) — the writer for every `from-bicep-output` entry.
7. `.claude/adr/ADR-028-spaarke-auth-architecture.md` — managed identity first for outbound calls; secret lifecycle.

## `value_source` — a closed set (the reader refuses anything else)

| `value_source` | Where the value comes from | Examples |
|---|---|---|
| `from-bicep-output` | Written by `customer.bicep`'s `kvSecrets` module from the customer's **own** resources at H2a; H4 checks that it exists | the service endpoints (`DocumentIntelligence-ApiKey` retired by T243, `Redis-ConnectionString` by T242 — Redis is Entra-only and its endpoint is the per-env setting `Redis__Endpoint`) |
| `from-intake-parameter` | An intake value (`IntakeParameterCatalog`) H4 maps in `IntakeValueParameterKeys` | `TenantId`; `Communication-DefaultMailbox` (T245c) |
| ~~`from-topology-constants`~~ | Retired by T227e (2026-10-06) with its only entry, `SPE-ContainerTypeId`, which nothing read — the BFF takes the container type as a plain H4b setting. The reader now refuses it | — |
| `written-by-h3` | H3 writes it to the customer vault itself; H4 (which runs before H3) **skips** the entry | `BFF-API-ClientId`, `BFF-API-Audience` |
| `generated` | A cryptographically random value created by H4 | webhook signing keys |
| `from-run-parameter` | A KV reference on the run (`RunParameters.Secrets`). 🔴 **Nothing writes that map since task 245a** — every entry here is a pinned gap with an owning task | `ContentSafety-ApiKey` only (T246). T245b moved `Dataverse-ServiceUrl` to per-env settings; T245c moved `Communication-DefaultMailbox` to `from-intake-parameter` |
| `from-existing-kv` | Copied from another vault through a run reference — same writer-less channel | `Dataverse-ClientSecret`, `BFF-API-ClientSecret` — never reached on a new stamp: secret-free is the default (T225b, G21), so H4 omits both |

A new entry should not use `from-run-parameter` / `from-existing-kv`: there is no producer for them.

`from-shared-service` (task 200's H4-shared, which copied keys from `sprksharedprod-*` services into a shared vault) was **retired by T226 (2026-09-30)**. A customer stamp never reads a credential belonging to a shared service.

`from-platform-vault` (T245b — Spaarke-shared vendor keys copied from the platform vault) was **retired by T225b (2026-10-02)** with its only two entries, `BingSearch-ApiKey` and `LlamaParse-ApiKey` (owner D18: Bing Search v7 retired by Microsoft; LlamaParse unused).

## Constraints

- **BINDING** KV credential-lifecycle rule (§6.5 resolution 2026-08-25 per ADR-028 A4 / E-3 closure — supersedes the r3-handoff blanket never-delete): (1) H4 **omits** `BFF-API-ClientSecret` and `Dataverse-ClientSecret` in secret-free envs — every new stamp since T225b (no sentinel — §9.1 opaque `AADSTS7000215`); (2) do not purge soft-deleted rollback copies or delete live `Dataverse-ClientSecret` before 2026-11-23 (auth-v4 owns retirement per obligation 051-E); (3) original never-delete survives only for unmigrated envs; (4) E-1 SpeAdmin per-customer secrets protected indefinitely. `never_delete: true` on manifest entries still governs against test-cleanup / sweep / "temporary" removal. Full rule: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) §KV credential lifecycle.
- **BINDING** (§7.9 pre-check gate): before any secret rename/delete, verify LIVE App Service + KV + Dataverse-persisted config. FR-35 pre-check is enforced by a script AND by code-review checklist — bypassing either is a HARD violation.
- **Keyless first (owner D13, 2026-09-30).** Before adding a key for a per-customer Azure service, check whether the BFF can reach the service with the stamp UAMI — managed identity is the default (ADR-028). A key belongs in the catalog only when no MI path exists yet, as `from-bicep-output` from the customer's own resource, with the task that removes it named in `purpose`.
- **A KV-reference setting must point at a secret something writes.** Every `app_settings` name on an entry becomes `@Microsoft.KeyVault(...)` via H4b. App Service passes an unresolvable reference to the app as the **literal string**, which the BFF then sends as a credential.
- Adding a new secret = 1 manifest entry + the writer its `value_source` needs (for `from-bicep-output`, the `customer.bicep` `kvSecretValues` line); 0 handler changes. A key in both `secrets` (`app_settings`) and `per_env_settings` is emitted twice in the ONE generated settings batch; the generator orders KV references before per-env values, so the **per-env value wins** (`az webapp config appsettings set` keeps the last).
- Sonnet-5 literal execution: populate EVERY field of a new entry. The generator's checks fail hard on missing fields, deliberately.

## Key Rules (walk this for every manifest change)

1. **Extend the manifest first.** Never write a handler that hardcodes a secret name that isn't in the manifest.
2. **Prove determinism**: `pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify` → exit 0. Commit `generated/` in the same commit as the manifest; recompile `customer.json` (`az bicep build`) when `customer.bicep` changes.
3. **Never-delete entries**: if the manifest says `never_delete: true`, no handler emits a delete for that key, even under drift recovery.
4. **Wire the writer.** A `from-bicep-output` entry that `customer.bicep` does not write fails H4 on a fresh customer (the secret is absent and the resolver has no other source — the plan's G18 failure for the SPE container ids). A value a **later** handler produces and that is not a secret belongs in `per_env_settings` with a `from-{handler}-output` source and an H4b ← handler DAG edge, not in the vault (T227c moved the SPE container id there).
5. **A new `value_source` changes at least four places**: `FileKvSecretManifest` (reader), `KvSecretValueSource` (enum), `KvSecretValueResolver` (resolution) and the generator's allowed list — plus whatever supplies the value (for the intake-sourced ones: `KvSecretWriteRequest.IntakeValues`, H4's `IntakeValueParameterKeys` map and the seeder's SKIP branch; `FileKvSecretManifestTests` checks the map against the manifest) — plus a case in `RunContextContractTests.ClassifyManifestEntries`. Task 214 added `from-topology-constants` to the manifest alone, and every H4 run failed `ManifestReadFailed` until T226. Removing one is the same list in reverse (T227e removed `from-topology-constants` from all of them and added it to the reader's refused-sources test).
6. **Drift recovery is not scheduled rotation** — ADR-028's 90-day cadence bounds SCHEDULED rotations; an externally rotated key is a different failure mode. Handle via the ADR conflict protocol (see the [`manual-gates.md`](../../../provisioning-runs/_templates/manual-gates.md) escalation entry).
7. **Test update obligation** (bff-extensions.md § F, applied analogously): manifest PRs add/update tests in `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/` — at minimum a `value_source` mapping row in `FileKvSecretManifestTests`.

## Anti-patterns this catches

- ❌ Hardcoding a secret name in a handler (e.g. a `"AzureOpenAI-ApiKey"` string literal). Names come from the manifest via `IKvSecretManifest` / `IPerEnvSettingsManifest`.
- ❌ Sourcing a customer's secret from a shared service or another customer's resource — a cross-customer isolation break (why T226 retired `from-shared-service`).
- ❌ Adding a key for a service the BFF can already reach with managed identity (D13).
- ❌ An `app_settings` KV reference to a secret nothing writes (the BFF receives the literal reference string as its "key").
- ❌ "Temporary" delete of `Dataverse-ClientSecret` for a test cycle. Violates prong 2 of the KV credential-lifecycle rule (do not delete before 2026-11-23).
- ❌ Seeding `BFF-API-ClientSecret` into a secret-free environment "to be safe" or as a sentinel. Violates prong 1 (H4 omits — no sentinel; `AADSTS7000215` per §9.1). E-3 is closed; there is no fallback path.
- ❌ Renaming a secret without the §7.9 pre-check. Rename → every `@Microsoft.KeyVault(...)` ref silently fails → BFF boot failure.

## Recovery recipes

- **`-Verify` fails**: run the generator without `-Verify` to see the diff; check the entry has every field; re-run `-Verify`.
- **H4 rejects with `ManifestReadFailed`**: the reader refused the manifest; the diagnostic names the entry (unrecognized `value_source`, or the never-delete invariant). Fix the entry — or, for a genuinely new source, make the four-place change in rule 5.
- **H4 fails on a missing `from-bicep-output` secret**: confirm `customer.bicep` `kvSecretValues` writes it and the H2a deployment ran with the current template.
- **Secret rename needed**: file a task with the §7.9 pre-check as Step 1; verify live App Service + KV + Dataverse references BEFORE any rename; land the rename separately after all consumers use the alias.

## Worked example — a key from a new per-customer resource (only once no MI path exists)

1. Expose the key from the resource module and add it to `customer.bicep`:
   ```bicep
   'Speech-ApiKey': speech.outputs.speechKey   // in kvSecretValues
   ```
2. Add the manifest entry:
   ```yaml
   - canonical_name: "Speech-ApiKey"
     category: "ai"
     purpose: "Azure AI Speech key from the customer's own Speech resource. Interim until the BFF MI path lands (task NNN)."
     consumers:
       - "BFF: Speech:ApiKey"
     rotation_cadence: "90-days"
     never_delete: false
     exception_note: ""
     aliases: []
     value_source: "from-bicep-output"
     app_settings:
       - "Speech__ApiKey"
     tags: ["ai", "key"]
   ```
3. Regenerate + verify (`Invoke-CatalogGenerator.ps1`, then `-Verify`); `az bicep build` `customer.bicep` → `customer.json`.
4. No handler change: H4 checks the secret exists; H4b emits `Speech__ApiKey=@Microsoft.KeyVault(...)` from the regenerated Configure script.
5. Test: add `[InlineData("Speech-ApiKey", KvSecretValueSource.FromBicepOutput)]` to `FileKvSecretManifestTests.ReadAsync_RealEmbeddedManifest_MapsValueSourceCorrectly`.

## Cross-refs

- Related pattern: [handler-registration-completeness.md](handler-registration-completeness.md) (registering a new handler that consumes the manifest)
- Related pattern: [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md) (H4b is the mechanism)
- Related pattern: [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) (T1 trap — App Service needs the UAMI + KV RBAC for `@Microsoft.KeyVault(...)` to resolve)
- Related constraint: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) (BINDING never-delete list + pre-check gate)
