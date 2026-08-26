# Manifest-Driven Secret Catalog Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Adding a new provisioning handler that seeds or reads KV secrets.
- Adding a new KV secret that BFF (or another consumer) references via `@Microsoft.KeyVault(SecretUri=...)`.
- Debugging a `@Microsoft.KeyVault(...)` reference that resolves to `null` at runtime.
- Extending `per_env_settings` (App Service app-settings applied via H4b bulk-set).
- Reviewing a PR that touches `scripts/canonical-secret-catalog/manifest.yaml`.

## Read These Files (canonical source)

1. `scripts/canonical-secret-catalog/manifest.yaml` — **single source of truth**. Every secret + every per-env app-setting is one entry. Task 084 / FR-36 established this file as the authority; changing behavior means changing this file, not the handlers.
2. `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` — deterministic generator. Emits Bicep parameter fragments + PowerShell secret-write invocations + `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` sections from the manifest. Determinism: `-Verify` proves byte-identical regen; drift → PR fails.
3. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SharedKvSecrets/H4SharedKvSecretsPopulationHandler.cs` — reference impl for the shared-tier flow. Filters manifest entries where `source.type == from-shared-service`; extracts from source Azure services via SDK; writes to shared KV.
4. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` — reference impl for `per_env_settings` — single batched `az webapp config appsettings set --settings k1=v1 k2=v2 ...` per slot → ONE App Service restart cycle.
5. `.claude/adr/ADR-028-spaarke-auth-architecture.md` — the 21 auth MUSTs that govern secret lifecycle (rotation cadence, UAMI reference identity, KV RBAC).
6. `.claude/constraints/bff-extensions.md` § F — asymmetric-registration + fixture-config-FIRST + empirical-reproduction-FIRST protocols that apply when a secret-related test/config bug surfaces.

## Constraints

- **BINDING** KV credential-lifecycle rule (§6.5 resolution 2026-08-25 per ADR-028 A4 / E-3 closure — supersedes the r3-handoff blanket never-delete): (1) H4 **omits** `BFF-API-ClientSecret` in secret-free envs (no sentinel — §9.1 opaque `AADSTS7000215`); (2) do not purge soft-deleted rollback copies or delete live `Dataverse-ClientSecret` before 2026-11-23 (auth-v4 owns retirement per obligation 051-E); (3) original never-delete survives only for unmigrated envs; (4) E-1 SpeAdmin per-customer secrets protected indefinitely. `never_delete: true` on manifest entries still governs against test-cleanup / sweep / "temporary" removal. Full rule: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) §KV credential lifecycle.
- **BINDING** (§7.9 pre-check gate): before any secret rename/delete, verify LIVE App Service + KV + Dataverse-persisted config. FR-35 pre-check is enforced by a script AND by code-review checklist — bypassing either is a HARD violation.
- Every manifest entry MUST declare a `source: { type, ... }` field. Valid types: `kv-ref | per-env-input | literal | from-bicep-output | from-shared-service`. Missing `source` → generator rejects the manifest.
- Adding a new secret: 1 manifest entry, 0 handler changes. The per-tenant H4 handler filters non-`FromSharedService` entries; the shared H4-shared handler filters `FromSharedService`; H4b filters `per_env_settings`. Cross-cutting entries (e.g., `AzureAd__TenantId`) may appear in both `secrets` and `per_env_settings` — last-write-wins is deterministic because H4b runs AFTER H4.
- Sonnet-5 execution literal-following: if you author a manifest entry, populate EVERY required field. Do not "leave for later" — the generator's `-Verify` fails hard on missing fields, which is deliberate.

## Key Rules (walk this for every manifest change)

1. **Extend the manifest first**. Add the entry with all required fields (`canonical_name`, `aliases`, `source`, `consumers`, `never_delete`). Never write a handler that hardcodes a secret name that isn't in the manifest.
2. **Prove determinism** with `pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify` → exit 0. Any generator output drift means the manifest change wasn't consumed correctly.
3. **Never-delete entries stay in every path** — H4 for per-tenant, H4-shared for shared-tier, H4b for per-env app-settings. If the manifest says `never_delete: true`, the handler MUST NOT emit a delete op for that key even under drift-recovery.
4. **Handler filtering discipline**:
   - H4 (per-tenant): `where source.type != from-shared-service`. Guarantees H4 doesn't chase drift on shared secrets that H4-shared owns.
   - H4-shared: `where source.type == from-shared-service`. Extracts from source Azure service via SDK; compares against KV; rotates only on drift; skips otherwise (idempotent + audit-logged).
   - H4b: `where per_env_settings != null`. Applied ALL in one batch → ONE restart cycle. See [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md).
5. **Drift-recovery is not scheduled rotation** — ADR-028's 90-day rotation cadence bounds SCHEDULED rotations; drift-recovery (source-service key was rotated externally) is a different failure mode. Handle via ADR conflict protocol (see [`manual-gates.md` example](../../../provisioning-runs/_templates/manual-gates.md) escalation entry).
6. **Test update obligation** (bff-extensions.md § F test-update-obligation, applied analogously here): PRs modifying `scripts/canonical-secret-catalog/manifest.yaml` MUST add/update tests in `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/Handlers/**` covering the new entry's shape.

## Anti-patterns this catches

- ❌ Hardcoding a secret name in a handler (e.g., `"AzureOpenAI-ApiKey"` string literal). Must come from the manifest via `IPerEnvSettingsManifest` / `ISharedKvSecretAccessor`.
- ❌ "Temporary" delete of `Dataverse-ClientSecret` for a test cycle. Violates prong 2 of the KV credential-lifecycle rule (do not delete before 2026-11-23; it is auth-v4's live rollback copy through the soak window — §6.5 resolution 2026-08-25).
- ❌ Seeding `BFF-API-ClientSecret` into a secret-free environment "to be safe" or as a sentinel/placeholder value. Violates prong 1 (H4 omits — no sentinel; the ordered selector fails opaquely with `AADSTS7000215` per §9.1). E-3 is closed; there is no fallback path.
- ❌ Manifest entry with `source: { type: literal }` for a customer-provided value. `literal` is for INTERNAL constants (e.g., `AzureAd__Instance: https://login.microsoftonline.com/`). Customer values use `per-env-input`.
- ❌ Rotating a `from-shared-service` secret via H4 (per-tenant handler). Ownership is H4-shared; H4 filtering excludes it.
- ❌ Renaming a secret without pre-check gate (§7.9). Rename → all `@Microsoft.KeyVault(SecretUri=...)` refs silently resolve to null → BFF SIGABRT cascade.

## Recovery recipes

- **Manifest change fails `-Verify`**: run generator without `-Verify` to see the diff; verify manifest entry has every required field per schema; re-run `-Verify`.
- **Drift detected on `from-shared-service` secret**: H4-shared audit-logs the drift; rotate proceeds automatically; result recorded in `handler-log.md`. If rotation itself fails, escalate per §6.5 (see manual-gates template).
- **Secret rename needed**: file a task with §7.9 pre-check as Step 1; verify live App Service + KV + Dataverse references BEFORE any rename; land rename via a separate PR after all consumers migrated to the alias.

## Worked example — adding a new source-service secret

Suppose we're adding a Cognitive Services Speech account. Steps end-to-end:

1. Manifest entry (`scripts/canonical-secret-catalog/manifest.yaml`):
   ```yaml
   secrets:
     - canonical_name: CognitiveSpeech-ApiKey
       aliases: [cognitive-speech-api-key]
       source:
         type: from-shared-service
         service-ref:
           resource-type: Microsoft.CognitiveServices/accounts
           name-pattern: sprksharedprod-speech
           extractor: cognitive-services-key1
       consumers:
         - CognitiveSpeech:ApiKey     # via @Microsoft.KeyVault(SecretUri=...) in App Service
       never_delete: false
       rotation_cadence_days: 90
   ```

2. Regenerate + verify:
   ```
   pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
   # Exit 0 = byte-identical regen; any drift → exit 1
   ```

3. H4-shared handler consumes it automatically via `where source.type == from-shared-service`. No handler code change needed.

4. If it needs to appear as an App Service app-setting (`@Microsoft.KeyVault(SecretUri=...)` ref):
   ```yaml
   per_env_settings:
     - key: CognitiveSpeech__ApiKey
       source:
         type: kv-ref
         kv-secret-name: CognitiveSpeech-ApiKey
       iOptionsModule: CognitiveSpeechOptions
       required: true
   ```

5. Regenerate; H4b handler picks up the new `per_env_settings` entry and applies it in the same bulk-set batch as everything else.

6. Test: add unit test in `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/Handlers/SharedKvSecrets/H4SharedKvSecretsPopulationHandlerTests.cs` covering the new entry's extractor.

## Cross-refs

- Related pattern: [handler-registration-completeness.md](handler-registration-completeness.md) (registering the new handler that consumes the manifest)
- Related pattern: [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md) (H4b is the mechanism)
- Related pattern: [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) (T1 trap — App Service must have UAMI + KV RBAC for `@Microsoft.KeyVault(...)` to resolve)
- Related constraint: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) (BINDING never-delete list + pre-check gate)
