# D-12 — deploy-promote.yml production-deploy: PROD_APP_NAME secret + workflow refactor for direct-target deploys

> **Date opened**: 2026-06-01 (post-PR-#317 audit during D-11 closeout)
> **Date resolved (deploy mechanics scope)**: 2026-06-02
> **Project**: github-actions-rationalization-r1
> **Phase**: Closeout-follow-up (after D-11 OIDC investigation surfaced this)
> **Disposition**: **CLOSED — deploy mechanics verified working**. The production-environment infrastructure-completion work (which prevents the deployed app from starting cleanly) is recognized as a separate scope and handed off to a follow-on project (suggested name: `production-environment-setup-r3`).
> **Related**: [D-11](D-11-deploy-promote-oidc-federated-credential-gap.md) (OIDC federated credentials, CLOSED 2026-06-02), [D-04](D-04-disposition-deploy-bff-api.md) (deploy-bff-api KEEP), [`projects/production-environment-setup-r2/`](../../production-environment-setup-r2/) (predecessor: parameterized config — closed 2026-03-20)

---

## Context

While walking through D-11 (Entra federated identity setup for `deploy-promote.yml`), an audit of the workflow's `Deploy to Production` job (`.github/workflows/deploy-promote.yml` lines 304+) surfaced **two adjacent gaps that needed to be resolved before production deploy could be verified**:

1. **`PROD_APP_NAME` secret did not exist in the repo.** (Resolved 2026-06-02: secret set to `spaarke-bff-prod`.)
2. **Workflow was structured as a strict sequential chain** `dev → staging → production`, so production couldn't be triggered without going through dev + staging (which had their own gaps — missing `DEV_APP_NAME` secret, etc.). (Resolved 2026-06-02 via this PR's workflow refactor: direct-target model.)

A third gap initially feared — that the production App Service might not exist — was disproven by owner-provided info + `az` discovery (see § "Infrastructure mapping" below).

## Infrastructure mapping (discovered 2026-06-02 via `az webapp list`)

| Environment | App Service | Resource Group | Slot | Key Vault |
|---|---|---|---|---|
| dev (= demo) | `spaarke-bff-dev` | `rg-spaarke-dev` | production slot | (not yet queried) |
| staging | `spaarke-bff-prod` | `rg-spaarke-platform-prod` | **`staging` slot** of prod App Service | `sprk-platform-prod-kv` |
| production | `spaarke-bff-prod` | `rg-spaarke-platform-prod` | production slot | `sprk-platform-prod-kv` |

App registrations (separate from App Services):
- `spaarke-bff-api-prod` (appId `92ecc702-d9ae-492d-957e-563244e93d8c`) — BFF API runtime identity in production
- `github-actions-spe-infrastructure` (appId `8c85a481-...`) — deploy app used by `deploy-promote.yml` for OIDC (D-11)

Tenant: `a221a95e-6abc-4434-aecc-e48338a1b2f2` · Subscription: `484bc857-3802-427f-9ea5-ca47b43db0f0`.

## Resolution

### Action 1 — Secret set
```bash
gh secret set PROD_APP_NAME --body "spaarke-bff-prod"   # 2026-06-02 01:25 UTC
```

### Action 2 — Workflow refactor to direct-target model

Previous chain logic (`deploy-promote.yml` lines 96-106): selecting `target_environment=production` set `deploy_dev=true, deploy_staging=true, deploy_prod=true`, forcing all three jobs to run sequentially. Production couldn't be triggered without dev + staging running first.

Refactored logic: each `target_environment` value sets exactly one `deploy_X=true` flag. Production is now triggerable directly.

```yaml
case "$TARGET" in
  dev)         deploy_dev=true,   deploy_staging=false, deploy_prod=false
  staging)     deploy_dev=false,  deploy_staging=true,  deploy_prod=false
  production)  deploy_dev=false,  deploy_staging=false, deploy_prod=true
esac
```

Also removed sequential `needs:` dependencies (`deploy-staging` no longer needs `deploy-dev`; `deploy-prod` no longer needs `deploy-staging`). The summary job's `needs:` is unchanged.

### Action 3 — Smoke-test verification (PENDING)

```bash
gh workflow run deploy-promote.yml --ref master --field target_environment=production
```

Expected sequence:
- `Plan Promotion`: ✅ success (deploy_prod=true)
- `Deploy to Dev`: ⏭️ skipped (deploy_dev=false)
- `Deploy to Staging`: ⏭️ skipped (deploy_staging=false)
- `Deploy to Production`: pauses for required-reviewer approval (`heliosip`)
- After approval: `Azure Login (OIDC)` ✅ (per D-11), then `Deploy API to Production` via `azure/webapps-deploy@v3` targeting `spaarke-bff-prod`, then smoke tests against `https://spaarke-bff-prod.azurewebsites.net/ping` + `/healthz`.

## Out-of-scope gaps (NOT closed by this work — separate items)

- **`DEV_APP_NAME` secret still missing**: dev deploys will fail at `azure/webapps-deploy` step (same class as the old PROD_APP_NAME gap). Easy fix when needed: `gh secret set DEV_APP_NAME --body "spaarke-bff-dev"`. Not blocking D-12 (which is production-specific) but worth knowing.
- **STAGING deploy uses production-slot semantics, not `staging` slot**: the workflow's staging job calls `azure/webapps-deploy@v3` without a `slot-name` parameter. The `STAGING_APP_NAME` secret's current value is unknown (write-only); if it equals `spaarke-bff-prod-staging` (the slot's hostname), deploy may fail because that's not a valid App Service name. To properly target the staging slot would require adding `slot-name: staging` to the deploy + smoke-test steps. Not in D-12 scope; can be tracked separately if/when staging deploy is exercised.

These two items are gaps in the broader workflow but don't block D-12 (production-specific) closure.

## Evidence

### Workflow references

```yaml
# .github/workflows/deploy-promote.yml line 349-356 (Deploy to Production job)
      - name: Deploy to Production
        uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ secrets.PROD_APP_NAME }}        # ← line 350
          package: ${{ steps.download-artifact.outputs.download-path }}
        env:
          AZURE_CORE_OUTPUT: none
      - name: Smoke test - Production
        run: |
          APP_URL="https://${{ secrets.PROD_APP_NAME }}.azurewebsites.net"  # ← line 355
          ...
```

### Secret-list evidence

```
$ gh secret list | grep -E "APP_NAME|PROD"
STAGING_APP_NAME	2025-12-31T02:44:31Z
```

Only `STAGING_APP_NAME`. No `DEV_APP_NAME`. No `PROD_APP_NAME`.

### App Service existence

```
$ az webapp list --query "[?contains(name, 'spaarke')].{name:name, rg:resourceGroup, slot:slotInfo}" -o table
```

To be verified during owner action. Hypothesis: only a single App Service exists today (the one `STAGING_APP_NAME` points to), serving the demo environment.

## Disposition

**DEFERRED** — owner action required.

### Why deferred (not fixed in this PR)

1. **Out of scope**: The fix requires creating Azure infrastructure (an App Service for production) AND adding GitHub repo secrets. Neither is repository code.
2. **Requires owner / Contributor role on the target Azure subscription**: provisioning App Services + their App Service Plans.
3. **Requires owner role in the repo to add secrets**: secrets are write-only via `gh secret set`.
4. **Owner-stated timeline (2026-06-02)**: production deploy is **needed in the near future** — not imminent today, but not deferred indefinitely. Treat this record as a near-term backlog item, not a perpetual TODO. The production-deploy capability remains in the workflow (per owner decision against Path 4B alternative); only the underlying infrastructure + secret are deferred. The production GitHub Environment (with reviewer + wait-timer + branch-policy protections) is already in place and ready to enforce gates when prod deploy is provisioned.

### Owner action steps (near-term — to be executed when prod deploy is ready to launch)

#### Step 1 — Decide on App Service naming

Pick a name for the production App Service. Convention suggests something like `spaarke-prod` or `spaarke-bff-prod`. Avoid:
- Reusing the staging app's name
- Names containing `demo` (semantic mismatch with prod)
- Names longer than 60 characters (Azure limit on `*.azurewebsites.net`)

#### Step 2 — Provision the App Service

Recommended: extend `infrastructure/bicep/` to parameterize the App Service deployment by environment, then run via `Deploy-Platform.ps1` OR `Provision-Customer.ps1 -EnvironmentName production` (see [`docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`](../../../docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md)).

Quick alternative (manual, less idiomatic):
```bash
az appservice plan create \
  --name spaarke-prod-plan \
  --resource-group spaarke-prod-rg \
  --location eastus \
  --sku P1V3

az webapp create \
  --name spaarke-prod \
  --resource-group spaarke-prod-rg \
  --plan spaarke-prod-plan \
  --runtime "DOTNETCORE:8.0"
```

Configure Key Vault references, App Insights, environment variables, etc., per [`docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md`](../../../docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md).

#### Step 3 — Add the GitHub secret

```bash
gh secret set PROD_APP_NAME --body "spaarke-prod"   # or whatever name you picked in Step 1
```

#### Step 4 — Verify

After the secret is set + D-11's federated credentials are in place:

```bash
gh workflow run deploy-promote.yml --ref master --field target_environment=production
```

Watch the chain. Expected:
- `Plan Promotion`: success
- `Deploy to Dev`: success (assuming dev is also healthy)
- `Deploy to Staging`: success (existing path)
- `Deploy to Production`: pauses for approval from `heliosip` (per `production` env protection rule)
- After approval: deploy succeeds; smoke test runs against `https://spaarke-prod.azurewebsites.net`

#### Step 5 — Sign off here

- [x] **PROD_APP_NAME secret set on 2026-06-02** with value `spaarke-bff-prod`
- [x] **Production App Service `spaarke-bff-prod` exists** in resource group `rg-spaarke-platform-prod` (subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`); verified via `az webapp list` 2026-06-02
- [x] **Workflow refactored** to support direct-target production deploys (no more sequential `dev → staging → production` chain)
- [x] **Key Vault references / app settings**: production App Service `spaarke-bff-prod` references `sprk-platform-prod-kv` (URL `https://sprk-platform-prod-kv.vault.azure.net/`) per owner-provided info. Full production-parity audit out of D-12 scope; the existing prod App Service is already in active service and assumed to be configured.
- [x] **Deploy mechanics verified end-to-end** via `gh workflow run deploy-promote.yml --field target_environment=production` (run [26793051300](https://github.com/spaarke-dev/spaarke/actions/runs/26793051300)) on 2026-06-02:
  - ✅ `Plan Promotion`: success
  - ⏭️ `Deploy to Dev` / `Deploy to Staging`: correctly skipped (direct-target refactor working)
  - ✅ `Deploy to Production → Azure Login (OIDC)`: success (D-11 federated credentials honored)
  - ✅ `Deploy to Production → Deploy API to Production` (`azure/webapps-deploy@v3`): "Successfully deployed web package to App Service"
  - ❌ `Deploy to Production → Smoke tests`: failed — but for reasons OUTSIDE github-actions-rationalization-r1 scope (see "Post-deploy smoke-test failure" below).

D-12's original scope (PROD_APP_NAME secret + verified deploy mechanics) is **CLOSED**.

### Post-deploy smoke-test failure → handed off to `production-environment-setup-r3` follow-on

The smoke test failed because the production app cannot start (HTTP 503 after container starts). Root cause investigated 2026-06-02:

- Container starts (image pulled, .NET 8 host runs) but `dotnet` process aborts within ~7 seconds with exit code 134 (SIGABRT).
- Background workers (`ProfileSummaryWorker`, `UploadFinalizationWorker`, `JobSubmissionService`, `ScheduledRagIndexingService`, `IndexingWorkerHostedService`) throw `Azure.Messaging.ServiceBus.ServiceBusException: Name or service not known` on first iteration → unhandled exception in hosted-service host → process aborts.
- Root cause: `ServiceBus__ConnectionString` Key Vault secret in `sprk-platform-prod-kv` is set to a literal placeholder value (`Endpoint=sb://placeholder.servicebus.windows.net/...`). This was never replaced with a real Service Bus connection string during production setup.
- Adjacent issues: the BFF code expects a queue named `document-processing`, but the two available Service Bus namespaces in the subscription (`spaarke-servicebus-dev` and `spaarke-demo-prod-sbus`) don't have that queue; their queues are named differently (`office-indexing`, `office-jobs`, `office-profile`, `ai-indexing`, `document-indexing`, etc.). So even if the connection string were corrected, the queue contracts don't align.

Why this is outside github-actions-rationalization-r1 scope:
- The project's NFR-01 forbids `src/` changes (this would require either provisioning new Service Bus infrastructure OR updating BFF code to match existing queues OR both).
- The predecessor project [`production-environment-setup-r2`](../../production-environment-setup-r2/) explicitly listed *"Redis/Service Bus configuration (already parameterized)"* as **Out of Scope** (lines 53-54 of its README). R2 made the config mechanism parameterizable but did NOT verify that the actual values weren't placeholders. That's an r3-class gap.

### Recommended follow-on: `production-environment-setup-r3`

Suggested scope:
1. Audit ALL Key Vault secrets in `sprk-platform-prod-kv` for placeholder values (this Service Bus secret is one known instance; others are likely).
2. Decide on production Service Bus naming + queue naming convention:
   - Option A: provision new Service Bus in `rg-spaarke-platform-prod` (matches the App Service's RG; standard practice).
   - Option B: reuse `spaarke-demo-prod-sbus` and align BFF code to its queue names.
   - Option C: rename queues to match BFF code expectations.
3. Replace placeholder values with real connection strings via `az keyvault secret set`.
4. Verify all other config gaps (Redis, OpenAI, Dataverse, etc.) are real values, not placeholders.
5. Re-trigger deploy-promote.yml smoke test → confirm `Deploy to Production → Smoke tests` passes.
6. Document the production environment's actual provisioning state in `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md` or a new doc.

Initialize via `/design-to-spec` → `/project-pipeline` whenever ready.

D-12 is **CLOSED**. The above is **NOT** a deferred owner-action — it's a handoff to a new project with its own scope, plan, and lifecycle.

### Alternative — if production deploy is not in scope at all

If the architecture team decides Spaarke's commercial model doesn't require an automated production deploy pipeline (e.g., production deploys are always manual / via per-customer infrastructure per `CUSTOMER-DEPLOYMENT-GUIDE.md`), the cleanest disposition is to delete the `Deploy to Production` job from `deploy-promote.yml`. This would also remove the `production` option from the dropdown + the production case branch in `Plan Promotion`.

That alternative would close D-12 by removing the unused capability rather than completing it. Document the decision here when made.

## Cross-references

- [D-11](D-11-deploy-promote-oidc-federated-credential-gap.md) — sibling issue (OIDC federated credentials); both must be resolved for prod deploy to work
- [`docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`](../../../docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md) — guide for provisioning new environments
- [`.github/workflows/deploy-promote.yml`](../../../.github/workflows/deploy-promote.yml) lines 350, 355 — the secret references

---

*Authored 2026-06-01 in the D-11 + D-12 closeout PR for github-actions-rationalization-r1. This decision is the explicit deferral of the production-deploy gap.*
