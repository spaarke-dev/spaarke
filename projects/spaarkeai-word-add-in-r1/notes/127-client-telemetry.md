# Task 127: per-environment client telemetry (#1537)

Implementation date: 2026-10-09. Nothing has been deployed, and nothing was written to Dataverse. Every Dataverse read below used `systemform` / `customcontrol` in spaarkedev1.

## What changed

- **BFF.** The anonymous `GET /api/config/client` gains one nullable field, `appInsightsConnectionString`.
  - The value is the BFF's own `APPLICATIONINSIGHTS_CONNECTION_STRING`, the same setting `Program.cs` hands to `UseAzureMonitor()`. Bicep sets it from `monitoring.outputs.connectionString`, which is `appInsights.properties.ConnectionString`.
  - It is returned only when it parses as a plain App Insights connection string: documented keys only and a GUID `InstrumentationKey`. Otherwise the field is `null`. This means an unresolved Key Vault reference is never echoed.
  - Same route and same `anonymous` rate limit, so the RouteAuthorizationGuard ledger is unchanged.
- **`@spaarke/auth`.** New module `bffClientConfig.ts` holds the one cached `/api/config/client` fetch, moved out of `resolveRuntimeConfig.ts`.
  - It keeps the whole client config: tenant plus connection string.
  - It persists that config per BFF host for 24 hours in the existing `__spaarke_bff_tenant__` key.
  - Entries written before #1537 still answer tenant lookups, and the first telemetry lookup upgrades them.
  - New export: `getTelemetryConnectionString(bffBaseUrl)`. It returns `''` when no connection string is available and never throws.
- **`AppInsightsService`.**
  - `initialize()` accepts a connection string as well as a key.
  - New `initializeFromRuntime(provider)` makes one shared lookup and never rejects.
  - Calls made while that lookup is in flight are held, up to 50, so first-render events are not lost.
- **Surfaces.** None of the five reads a form property or `VITE_APP_INSIGHTS_KEY` any more.
  - SemanticSearchControl moves 1.1.84 → **1.1.85** and VisualHost moves 1.4.39 → **1.4.40**. Dev currently has 1.1.84 and 1.4.39 deployed, and no branch claims either new version.
  - The `appInsightsKey` manifest property is kept as optional and labelled "(ignored)", so existing forms stay valid.
  - The code pages DailyBriefing, EmailPage and CommunicationReconciliation initialise from `resolveRuntimeConfig().bffBaseUrl`.

## Form-binding inventory (spaarkedev1, read-only)

| Query | Result |
|---|---|
| `systemform` whose formxml contains `appInsightsKey` | **1**: Matter main form `4fa382f2-c273-f011-b4cb-6045bdd6a665` (objecttypecode 10473) |
| Forms binding `sprk_Sprk.SemanticSearchControl` or `sprk_Spaarke.Visuals.VisualHost` | 6 main forms: Matter `4fa382f2-…`, Budget `2da25f11-1009-f111-8407-7c1e520aa4df`, Work Assignment `7e578eef-761d-f111-88b3-7c1e520aa4df`, Event `eaf22dcb-9aff-f011-8406-7c1e525abd8b`, Invoice `93aa1c69-0406-f111-8406-7c1e525abd8b`, Project `5aa00242-5212-f111-8342-7ced8d1dc988` |
| Those forms that carry a static `<tenantId …>` | **1**: Matter main form |

Matter main form bindings, parsed from the live formxml:

| Control (forControl) | Custom control | Form factors | Static keys |
|---|---|---|---|
| `{535b5bb1-2a4e-4fb4-87aa-be8351b15fb2}` | `sprk_Sprk.SemanticSearchControl` | 0, 1, 2 | `tenantId` = `a221a95e-6abc-4434-aecc-e48338a1b2f2`; `appInsightsKey` = `7a15beb2-6bc5-4cc1-ab17-401556f88ef0` (the dev key), in all three |
| `{70ee0c61-5dab-4d13-a60c-ab24860d1ff4}`, `{fb095c1e-d24c-43fa-91a1-59efdbc8c06a}`, `{27a0e183-0918-49d9-b462-451f60b92b39}` | `sprk_Spaarke.Visuals.VisualHost` | 0, 1, 2 | **none**: only `chartDefinitionId` and layout |

**VisualHost bindings carry no static keys** on any form; this was checked on all six forms. The issue's concern was only that they might.

The repo's unpacked copy matches the live form: `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Matter/FormXml/main/{4fa382f2-c273-f011-b4cb-6045bdd6a665}.xml` and its `_managed.xml` twin, lines 1104/1105, 1114/1115 and 1124/1125.

## Exact form-edit and deploy plan (owner approval required)

The order matters. Deploy the code first, then remove the form values. With the new code the form values are already ignored, so removing them is clean-up with no behaviour change.

1. **BFF.** Deploy with `bff-deploy` and check the dev App Service.
   - Run `az webapp config appsettings list` (read-only) and confirm `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
   - `GET https://<bff>/api/config/client` should return a non-null `appInsightsConnectionString`.
   - If the setting is a Key Vault reference that does not resolve, the field is `null` and telemetry stays off. Fix the setting, not the code.
2. **PCFs.** Use `pcf-deploy` from a fresh short-path worktree of master after the merge.
   - Deploy SemanticSearchControl **1.1.85**.
   - Deploy VisualHost **1.4.40**.
   - For VisualHost, `pack.ps1 -VerifyOnly` must pass first.
3. **Code pages.** Use `code-page-deploy`: `sprk_dailyupdate` (DailyBriefing), `sprk_emailpage`, `sprk_communicationreconciliation`.
4. **Matter main form edit.** Form `4fa382f2-c273-f011-b4cb-6045bdd6a665`, `controlDescription forControl="{535b5bb1-2a4e-4fb4-87aa-be8351b15fb2}"`, for **each of the three** `customControl name="sprk_Sprk.SemanticSearchControl"` entries (formFactor 0, 1, 2):
   - delete `<appInsightsKey type="SingleLine.Text" static="true">7a15beb2-6bc5-4cc1-ab17-401556f88ef0</appInsightsKey>`;
   - delete `<tenantId type="SingleLine.Text" static="true">a221a95e-6abc-4434-aecc-e48338a1b2f2</tenantId>`. It is safe to remove: since task 123, `resolveSignInIdentity` prefers `sprk_TenantId`, and `@spaarke/auth` discovers the tenant otherwise;
   - leave `searchScope`, `showFilters`, `resultsLimit` and `compactMode` unchanged;
   - make no change to the three VisualHost bindings or to the other five forms;
   - publish the `sprk_matter` entity afterwards.
5. **Repo copy.** Re-export SpaarkeMaster through the normal release export, so that both `{4fa382f2-…}.xml` and `{4fa382f2-…}_managed.xml` lose the same six lines. Do not hand-edit them ahead of the export: the release branch regenerates them.
6. **Verify.**
   - Open a Matter record in dev.
   - The console must show no `[AppInsightsService]` warning.
   - App Insights for the environment must receive `customEvents`, for example `view_toggled` and `card_rendered`.
   - On a customer environment, those events land in that customer's resource and not in the dev resource.

## Review (code-review + adr-check, Step 9.5)

- **F1, fixed.** A persisted answer with no connection string was trusted for 24 h. If the clients deployed before the BFF, or the setting was added later, telemetry stayed off for up to a day. Such answers now expire after 1 h; the tenant keeps its 24 h. Test: `an answer without a connection string is re-checked after an hour`.
- **F2, fixed.** `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` listed the old `/api/config/client` shape and the wrong class name.
- **K4.** Anyone can read a browser connection string, so anyone could send junk telemetry to that resource. This is inherent to browser telemetry and already true with the form key; ADR-028 lists App Insights ingestion as a documented keyless exclusion.
- **K4.** Code pages drop errors caught before `resolveRuntimeConfig()` resolves, because there is no BFF URL yet. PCFs hold up to 50 calls only while the lookup is in flight.
- **K4.** In SemanticSearchControl a form-bound `apiBaseUrl` still overrides `sprk_BffApiBaseUrl`, and therefore the telemetry target. That is pre-existing precedence; no form binds `apiBaseUrl` in dev.
- **Owner note.** VisualHost declares `<external-service-usage enabled="false" />` but now calls the BFF; it already called App Insights. The declaration is not enforced at runtime, but changing it affects licensing classification, so it is left for the owner to decide.
- **adr-check.** Compliant with ADR-001, 008, 010, 012 (provider injected; no platform calls in the shared service), 022, 028, 029 (+758 B) and 038. One warning: VisualHost's relative-source import of `Spaarke.Auth/src/bffClientConfig` follows its existing relative-source precedent rather than ADR-012's `dist/` deep-import form. This is deliberate, to keep MSAL out of the VisualHost bundle (0 `msal` matches).

## Out of scope: same defect class, filed separately

- `src/solutions/LegalWorkspace/src/services/telemetry.ts:43-47` falls back to a **hardcoded dev connection string** (`InstrumentationKey=09a9beed-…`) when its env-var lookup returns nothing, so customer telemetry goes to dev.
- `src/solutions/SpaarkeAi/src/main.tsx:227-230` still reads the build-time `VITE_APP_INSIGHTS_KEY`. It is the Console, a hot path.
- `src/client/pcf/shared/utils/environmentVariables.ts` `getAppInsightsKey` / `loadSpaarkeConfiguration` read the Secret env var `sprk_ApplicationInsightsKey`, which a browser cannot read. No callers remain in the five surfaces.

All three can move to `AppInsightsService.initializeFromRuntime(() => getTelemetryConnectionString(bffBaseUrl))`, or, for LegalWorkspace, to its own init fed by `getTelemetryConnectionString`.
