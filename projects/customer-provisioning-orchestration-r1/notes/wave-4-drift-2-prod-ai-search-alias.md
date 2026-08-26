# Wave 4 Batch 4D — Drift-2: Prod AI Search Alias → Canonical `AiSearch--AdminKey`

> **Task**: Wave-4 drift-2 (follow-on from task 085 out-of-scope §7.9 remediation gap)
> **Date**: 2026-08-17
> **Executor**: Wave-4-Drift2-ProdAiSearchAlias (Sonnet)
> **Rigor**: STANDARD (PS-only source-code edits; no live prod mutation)
> **Base commit**: `ccd858b7c` (work/customer-provisioning-orchestration-r1)
> **Scope**: SOURCE ONLY — no `az` calls, no live prod KV touch, no App Service reconfigure

---

## 1. What this fixes

Task 085 successfully collapsed AI Search-key aliases in the **DEV** vault (`spaarke-spekvcert`) to canonical `AiSearch--AdminKey`, and also migrated `spaarke-bff-dev` App Service settings to reference the canonical KV secret name. Two prod-side PowerShell scripts still hard-coded the `ai-search-key` alias and were explicitly deferred by task 085 (§2.7 "Scripts" table `OUT OF SCOPE`).

Owner authorized (2026-08-17) folding the prod-script fix into Batch 4D per the "fix drift at discovery" principle. This is a **source-only** fix — no live prod KV or App Service mutation.

## 2. Files modified (2)

| File | Change |
|---|---|
| `scripts/Seed-ProductionKeyVault.ps1` | Line 170: `Set-VaultSecret -Name "ai-search-key"` → `Set-VaultSecret -Name "AiSearch--AdminKey"` (also updated description to cite the canonical-secret-catalog manifest) |
| `scripts/Configure-ProductionAppSettings.ps1` | Line 96: `KVRef 'ai-search-key'` → `KVRef 'AiSearch--AdminKey'` (in `DocumentIntelligence__AiSearchKey=…` binding) |

## 3. Files DELIBERATELY NOT touched

| File | Why NOT |
|---|---|
| `scripts/canonical-secret-catalog/manifest.yaml:434` (`- "ai-search-key"` under `aliases:`) | Task 084 owns the manifest. The alias entry is CORRECTLY declared — Phase H alias-collapse consumes the manifest to know what to reconcile. Removing it would mean the manifest can't guide future prod-side pre-check + collapse. Future collapse-task removes it after prod live-check. |
| `scripts/canonical-secret-catalog/generated/appsettings.tokens.generated.md:75,559` | Generator output; regenerates from manifest via `Invoke-CatalogGenerator.ps1`. Owned by task 084. |
| `scripts/ai-search/Deploy-AllIndexes.ps1` (contains `AzureAISearchApiKey` alias but reads canonical `AiSearch--AdminKey`) | Task 085 §2.7 flagged this as legacy back-compat write path; retirement gated on task 086 IaC alignment. Different alias, out of my scope. |
| `infrastructure/bicep/platform.json` (stale compiled artifact) | Task 086 regenerates via `az bicep build platform.bicep --outfile platform.json`. Explicitly out-of-scope per dispatch prompt. |
| BFF app-setting key name `DocumentIntelligence__AiSearchKey` | Already canonical per manifest (`app_settings:` list includes both `DocumentIntelligence__AiSearchKey` and `AiSearch__ApiKeySecretName`; both are canonical `__` convention). No rename needed. |

## 4. App-setting rename decision (per dispatch instruction #2)

The dispatch asked me to consider renaming the app-setting key itself if it used a flat non-canonical convention. Reading the manifest for the canonical `AiSearch--AdminKey` secret:

```yaml
canonical_name: "AiSearch--AdminKey"
app_settings:
  - "AiSearch__ApiKeySecretName"
  - "DocumentIntelligence__AiSearchKey"
```

Both are already canonical `__` (double-underscore) convention. The prod script uses `DocumentIntelligence__AiSearchKey`, which matches manifest canonical. **No app-setting key rename required — only the `SecretName=` value reference needed to change.**

Note: `Configure-ProductionAppSettings.ps1` does NOT define an `AiSearch__ApiKeySecretName` setting; it defines only `AiSearch__Endpoint` and the DocumentIntelligence binding above. The dev-side migration (task 085 §2.4) had both bindings because dev configures both surfaces; prod script currently only wires the DocumentIntelligence binding. Adding `AiSearch__ApiKeySecretName` to prod is out of scope for a drift-2 SecretName-alias fix and would expand blast radius beyond this task.

## 5. Verification

### 5.1 Grep in `scripts/` after edits

```
$ Grep pattern='ai-search-key' path='scripts/' output_mode=content
```

Result — **0 hits in the two target files**. Remaining hits are:
- `scripts/canonical-secret-catalog/manifest.yaml:434` — canonical alias declaration (task 084 owned)
- `scripts/canonical-secret-catalog/generated/appsettings.tokens.generated.md:75,559` — generator output (regenerates from manifest)

Neither of these is a prod-runtime consumer; both are intentional per §3 above.

### 5.2 PowerShell parse check

```
[System.Management.Automation.Language.Parser]::ParseFile(...)
```

- `Seed-ProductionKeyVault.ps1` → 0 parse errors
- `Configure-ProductionAppSettings.ps1` → 0 parse errors

### 5.3 PSScriptAnalyzer

Both scripts show **only pre-existing warnings** (all Warning severity, none Error):
- `PSAvoidUsingWriteHost` (many; script is user-facing console output — intentional)
- `PSUseBOMForUnicodeEncodedFile` (pre-existing encoding note)
- `PSUseDeclaredVarsMoreThanAssignments` — line 65 `$label` in Seed-ProductionKeyVault.ps1 (pre-existing; unrelated)
- `PSUseShouldProcessForStateChangingFunctions` — `Set-VaultSecret` verb (pre-existing; unrelated)

**Zero NEW warnings introduced by these one-line edits.**

## 6. Rationale

Task 085 correctly deferred the prod-side script fix because:
1. Prod BFF uses a **different vault** (`sprk-platform-prod-kv`, not `spaarke-spekvcert`); prod KV state was not directly at risk from the DEV alias delete.
2. Prod is currently decommissioned per r3 handoff; re-provisioning would run these scripts and re-emit the alias.

Fixing the source scripts now (drift-2) ensures:
- **Next prod re-provision** seeds the canonical `AiSearch--AdminKey` name, matching what BFF App Service settings will resolve.
- **No drift accumulation** — the two source-of-truth prod scripts and the canonical-secret-catalog manifest now agree on canonical.
- **No live-prod risk** — this commit does not touch any live prod resource; it only aligns the source-code layer.

## 7. Rollback

If a subsequent prod deploy regresses (e.g., a downstream consumer somewhere still expects the alias `ai-search-key`):

1. **Revert the commit**:
   ```
   git revert <this-commit-sha>
   ```
2. **Prod-live state is unaffected** — no runtime rollback needed (no live mutation happened).
3. **Root-cause the downstream consumer**, then re-apply with a coordinated fix.

## 8. Out-of-scope confirmations

Verified by grep + dispatch scope:
- ✅ NO edit to `.claude/**` (subagent write boundary)
- ✅ NO edit to `src/server/**` (BFF or L2 code)
- ✅ NO edit to `tests/**`
- ✅ NO edit to `infrastructure/bicep/**` (task 086 owns Bicep + platform.json regen)
- ✅ NO edit to `scripts/canonical-secret-catalog/**` (task 084 owns manifest + generator)
- ✅ NO edit to other PS scripts
- ✅ NO live prod KV / App Service mutation

## 9. Coordination

- No overlap with Batch 4D tasks 059/060/061 (L2 code)
- No overlap with task 086 (dev-side Bicep + BFF appsettings.json + `platform.json` regen)
- No overlap with drift-1 (BFF Auth + I5 ArchTest — different files)
- Sole modified surface: 2 prod PS scripts

## 10. TASK-INDEX update

Appended to "Follow-on drift surfaced during 4C" row: notes that drift-2 landed (this commit).

---

*Report authored per Batch 4D drift-2 dispatch requirements. Cross-references: `phase-h-alias-collapse-aisearch-2026-08-17.md` §2.7 (deferred prod-side entries) + `scripts/canonical-secret-catalog/manifest.yaml` canonical `AiSearch--AdminKey` declaration.*
