# Progressive Fail-Fast Recovery Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Diagnosing BFF SIGABRT (exit 134) chain at boot — `AddOptions<T>.ValidateOnStart()` reports one missing IOptions module at a time, requiring ~40 sequential single-config fixes.

## Read These Files (task 203 fills)
1. `src/server/api/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` — the automation that kills the chain.
2. `src/server/api/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/BulkAppSettingsRejectionCodes.cs` — 15 machine-stable `h4b-*` codes.
3. `scripts/canonical-secret-catalog/manifest.yaml` § `per_env_settings` — the 8+ App Service settings covering every observed F20 SIGABRT trigger.
4. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F20 / F20a — original discovery.

## Constraints
- BFF Tier-1 IOptions validation runs ONE module at a time; each throws on first missing setting → operator sees only current failure.
- H4b handler MUST run AFTER H4a (per-tenant KV seed). Single batched `az webapp config appsettings set --settings @settings` → ONE App Service restart cycle.
- Post-condition: `/healthz` polled with 8-min backoff (30s / 60s / 90s / 120s / 180s); on failure, Kudu docker-log parser extracts `Unhandled exception. System.InvalidOperationException: <ModuleName>: ...` for actionable diagnostic.

## Key Rules (task 203 fills detail)
1. New BFF IOptions module → add corresponding entry to `per_env_settings` list in manifest (per_env_source: `literal | from-h<N>-output:<key> | from-h<N>-parameter:<key>`).
2. Deploy discipline: publish + operator invokes H4b → polls `/healthz` → parses Kudu logs on failure. NO manual `az webapp config appsettings set` single-setting fixes.
3. Nightly `IOptions-inventory-drift` ArchTest (task 203 authors per T201 deferred): scans `AddOptions<T>.ValidateOnStart()` in BFF DI + diffs against `per_env_settings` manifest entries → PR alert if drift.
