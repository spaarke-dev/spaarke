# Progressive Fail-Fast Recovery Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Diagnosing BFF SIGABRT (exit 134) chain at boot — `AddOptions<T>.ValidateOnStart()` reports one missing IOptions module at a time.
- Authoring a new BFF IOptions module that will need per-env app-settings.
- Reviewing a PR that touches `AddOptions<T>().Bind(...).ValidateOnStart()` in BFF DI.
- Onboarding a new operator (this is the #1 SESSION-4/5 lesson from the Model 1 Prod standup).

## Read These Files (canonical source)

1. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` — the automation that kills the chain. Applies ALL required BFF app settings in ONE batch call → ONE App Service restart cycle.
2. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/BulkAppSettingsRejectionCodes.cs` — 15 machine-stable `h4b-*` codes for actionable diagnostics.
3. `scripts/canonical-secret-catalog/manifest.yaml` § `per_env_settings` — the 8+ App Service settings covering every observed F20 SIGABRT trigger. Task 201 landed this manifest section.
4. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F20 / F20a — original discovery (SESSION 2). BFF exited 134 on missing `SpeAdmin:KeyVaultUri`; setting it → exited on `CosmosPersistence:Endpoint`; ~40 more modules each fail-fast in turn.
5. `src/server/api/Sprk.Bff.Api/Program.cs` — the top-level composition of `.ValidateOnStart()` calls. Every module here contributes to the fail-fast chain.

## Constraints

- BFF Tier-1 IOptions validation runs ONE module at a time; each throws on first missing setting → operator sees only the CURRENT failure. Serially chasing them burns hours per standup.
- H4b handler MUST run AFTER H4 (per-tenant KV seed) so `@Microsoft.KeyVault(...)` refs already resolve when app-settings apply.
- Single batched `az webapp config appsettings set --settings @settings-file` per slot → ONE App Service restart cycle. Multiple `az webapp config appsettings set --settings key=val` calls each restart independently → wasted minutes.
- Post-condition: `/healthz` polled with 8-min backoff (30s / 60s / 90s / 120s / 180s cumulative = 470s + probe latency); on failure, Kudu docker-log parser extracts `Unhandled exception. System.InvalidOperationException: {ModuleName}: ...` for actionable diagnostic.
- Nightly `IOptions-inventory-drift` ArchTest (planned, per task 203-followup) scans `AddOptions<T>().ValidateOnStart()` in BFF DI + diffs against `per_env_settings` manifest entries → PR alert if drift.

## Key Rules (walk this for every new IOptions module)

1. **Author the module + `.ValidateOnStart()`**. Standard BFF pattern: `services.AddOptions<TOptions>().Bind(config.GetSection("TSection")).ValidateDataAnnotations().ValidateOnStart();`.
2. **Add corresponding entry to `per_env_settings`**. Open `scripts/canonical-secret-catalog/manifest.yaml`; add entry with `key`, `source.type` (`kv-ref | per-env-input | literal | from-h<N>-output:<key>`), `iOptionsModule` (for the ArchTest), and `required: true` unless the module tolerates missing.
3. **Prove the generator is happy**: `pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify` → exit 0.
4. **Deploy discipline**: publish BFF (H9) → operator invokes H4b → H4b polls `/healthz` → parses Kudu logs on failure. NO manual `az webapp config appsettings set` single-setting fixes in production; they mask the manifest drift.
5. **Nightly ArchTest**: `AddOptions<T>().ValidateOnStart()` scanner in BFF DI compared against `per_env_settings` manifest — new BFF module without manifest entry → nightly PR alert.
6. **Rejection codes must be actionable**: H4b's `h4b-*` codes each point at either a manifest gap or an operator input gap. If a new module needs a new rejection code, add it to `BulkAppSettingsRejectionCodes.cs` with a docstring naming the manifest key + remediation.

## Anti-patterns this catches

- ❌ Adding a `ValidateOnStart` IOptions module in BFF DI without adding the corresponding `per_env_settings` manifest entry → BFF SIGABRT at deploy time; operator burns hours chasing single-setting fixes.
- ❌ Applying app-settings one at a time via multiple `az webapp config appsettings set --settings k=v` calls → each triggers a restart → 5+ minutes wasted per setting.
- ❌ Marking a required setting as `required: false` in the manifest to "unblock the standup" → hides real config completeness debt behind a runtime null-reference or misleading /healthz-green.
- ❌ Working around H4b with a hand-rolled script that reads a different manifest → single-source-of-truth violation; when the canonical manifest updates, hand-rolled script goes stale silently.

## Recovery recipes

- **BFF SIGABRT with exit code 134 at boot**: read the Kudu docker-log for `Unhandled exception. System.InvalidOperationException: {ModuleName}:` — that's the current missing IOptions module. Check `per_env_settings` manifest for the corresponding entry; if missing, add + regenerate + re-run H4b. If present but setting is empty on App Service, check per-env-input value for the run.
- **`/healthz` still red after H4b**: parse Kudu docker-log via H4b's built-in parser; the ModuleName extraction points at the specific gap. Do NOT loop-and-guess.
- **H4b times out (>8 min)**: App Service is thrashing on restart cycles. Verify only ONE `az webapp config appsettings set` call was made; verify slot swap timing; verify no other concurrent H4b invocation.

## Real example (SESSION 2 recovery narrative)

1. F20 observed: BFF SIGABRT on `SpeAdmin:KeyVaultUri`. Manually set. Restart. SIGABRT on `CosmosPersistence:Endpoint`. Manually set. Restart. SIGABRT on `DocumentIntelligence:Enabled`. And so on.
2. After ~5 rounds and 40 minutes, session context exhausted → pivoted to designing H4b (task 201).
3. Task 201 landed the manifest + handler + tests (SESSION 4). Now: one H4b call after H9 → all app-settings applied → single restart → `/healthz` green in <8 min.

## Worked example — H4b bulk-apply + Kudu log parse

Suppose BFF is SIGABRTing on boot after H9 deploy. Operator invokes H4b. Under the hood:

1. **H4b reads the manifest** — extracts every `per_env_settings` entry for the profile:
   ```yaml
   per_env_settings:
     - key: SpeAdmin__KeyVaultUri
       source: { type: literal, value: "https://{kv-name}.vault.azure.net/" }
       iOptionsModule: SpeAdminOptions
       required: true
     - key: CosmosPersistence__Endpoint
       source: { type: from-h2a-output, key: cosmos_endpoint }
       iOptionsModule: CosmosPersistenceOptions
       required: true
     # ... ~40 more entries
   ```

2. **H4b resolves per-env-inputs** — substitutes `{kv-name}`, reads `from-h2a-output` from the run's Cosmos state, reads `kv-ref` via `@Microsoft.KeyVault(SecretUri=...)` construction.

3. **H4b applies in ONE call**:
   ```
   az webapp config appsettings set \
     --resource-group {rg} \
     --name {app} \
     --slot production \
     --settings @/tmp/settings.json
   ```
   (settings.json is a JSON array of `{name, value, slotSetting}` objects.)

4. **H4b polls `/healthz`** with 8-min backoff:
   ```
   for delay in 30 60 90 120 180; do
     sleep $delay
     status=$(curl -s -o /dev/null -w '%{http_code}' https://{app}.azurewebsites.net/healthz)
     [ "$status" = "200" ] && echo "GREEN"; break
   done
   ```

5. **If `/healthz` red after 480s**, H4b fetches Kudu docker-logs:
   ```
   GET https://{app}.scm.azurewebsites.net/api/logs/docker
   # Auth: Bearer {L2 UAMI token with Website Contributor role — PRQ-E-05}
   ```
   Parser regex: `Unhandled exception. System.InvalidOperationException: (\w+Options): (.+)$` → extracts `{moduleName}`.

6. **Rejection code** if module extracted: `h4b-tier1-options-invalid` with payload `{module: "CosmosPersistenceOptions", missingKeys: ["Endpoint"]}`. Cross-reference `per_env_settings` manifest for that module; if entry exists, check per-env-input value; if entry missing, add it and re-run.

## Cross-refs

- Related pattern: [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) (H4b consumes the same manifest via `per_env_settings`)
- Related pattern: [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) (T1 — `@Microsoft.KeyVault(...)` refs must resolve or you'll SIGABRT even with correct app-settings)
- Related pattern: [handler-registration-completeness.md](handler-registration-completeness.md) (H4b is a handler; the 3-file dance applies)
- Related CLAUDE.md rule: `.claude/constraints/provisioning.md` § BFF-startup-completeness (task 203a authors)
