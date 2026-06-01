# D-12 — deploy-promote.yml production-deploy gap: missing `PROD_APP_NAME` secret + unverified App Service

> **Date**: 2026-06-01 (post-PR-#317 audit during D-11 closeout)
> **Project**: github-actions-rationalization-r1
> **Phase**: Closeout-follow-up (after D-11 OIDC investigation surfaced this)
> **Disposition**: **DEFERRED** — owner action required (secret creation + infrastructure check)
> **Related**: [D-11](D-11-deploy-promote-oidc-federated-credential-gap.md) (OIDC federated credentials), [D-04](D-04-disposition-deploy-bff-api.md) (deploy-bff-api KEEP)

---

## Context

While walking through D-11 (Entra federated identity setup for `deploy-promote.yml`), an audit of the workflow's `Deploy to Production` job (`.github/workflows/deploy-promote.yml` lines 304+) surfaced two adjacent gaps that will block production deploy even after D-11's federated credentials are added:

1. **`PROD_APP_NAME` secret does not exist in the repo.**
   - The workflow references `${{ secrets.PROD_APP_NAME }}` at lines 350 (`azure/webapps-deploy` step) and 355 (smoke-test URL construction).
   - `gh secret list` shows only `STAGING_APP_NAME`; no `DEV_APP_NAME` or `PROD_APP_NAME` exists.
   - With the secret missing, the `Deploy to Production` job will substitute an empty string, causing the deploy to target a non-existent App Service or to fail with a clearer "name required" error from Azure CLI.

2. **The corresponding Azure App Service may not exist.**
   - Even if `PROD_APP_NAME` were set, there's no evidence in `infra/` or recent deployment history that a production-tier App Service has been provisioned.
   - The `staging` App Service exists (per `STAGING_APP_NAME` secret); production was apparently never built.

These issues are sibling to D-11: both are pre-existing latent gaps that were hidden by the Wave-B-fixed cascade bug. Once D-11's federated credentials land, the OIDC step will succeed, but the deploy step will fail downstream.

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
4. **No urgency unless prod deploy is imminent**: the production deploy pipeline is not in active use (per the github-actions-rationalization-r1 Wave A inventory, `deploy-promote.yml` had zero invocations resulting in successful prod deploys in the 30 days prior to project start).

### Owner action steps (when ready to use prod deploy)

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

- [ ] PROD_APP_NAME secret set on YYYY-MM-DD with value `<name>`
- [ ] Production App Service `<name>` exists in subscription `<sub-id>`
- [ ] Key Vault references / app settings configured for production parity with staging
- [ ] End-to-end deploy verified via `gh workflow run` (Step 4 above)

OR (alternative disposition):

- [ ] Production deploy path deleted from `deploy-promote.yml` (only dev + staging supported); D-12 closed with rationale; commit references this record per NFR-04 + NFR-06

### Alternative — if production deploy is not in scope at all

If the architecture team decides Spaarke's commercial model doesn't require an automated production deploy pipeline (e.g., production deploys are always manual / via per-customer infrastructure per `CUSTOMER-DEPLOYMENT-GUIDE.md`), the cleanest disposition is to delete the `Deploy to Production` job from `deploy-promote.yml`. This would also remove the `production` option from the dropdown + the production case branch in `Plan Promotion`.

That alternative would close D-12 by removing the unused capability rather than completing it. Document the decision here when made.

## Cross-references

- [D-11](D-11-deploy-promote-oidc-federated-credential-gap.md) — sibling issue (OIDC federated credentials); both must be resolved for prod deploy to work
- [`docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`](../../../docs/procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md) — guide for provisioning new environments
- [`.github/workflows/deploy-promote.yml`](../../../.github/workflows/deploy-promote.yml) lines 350, 355 — the secret references

---

*Authored 2026-06-01 in the D-11 + D-12 closeout PR for github-actions-rationalization-r1. This decision is the explicit deferral of the production-deploy gap.*
