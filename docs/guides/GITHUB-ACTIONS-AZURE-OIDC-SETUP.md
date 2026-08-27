# GitHub Actions → Azure OIDC Setup

> **Created**: 2026-08-27
> **Applies to**: every workflow in `.github/workflows/` that calls `azure/login@v2`
> **Symptom this fixes**: `AADSTS700213: No matching federated identity record found for presented assertion subject 'repo:spaarke-dev/spaarke:...'`

---

## 1. What the error means

Spaarke's deploy workflows store **no Azure password**. Instead, GitHub mints a short-lived signed token that asserts:

> *"I am the repository `spaarke-dev/spaarke`, running on branch `master`."*

Azure accepts that token only if the **exact sentence** has been pre-registered on the app registration as a **federated identity credential** (FIC). The subject string in the error is the sentence GitHub presented; `AADSTS700213` means Azure had no matching entry.

**Nothing is broken or expired when you see this.** The entry was never created. This is first-time setup.

> **Verified 2026-08-27**: no workflow in this repository had ever successfully authenticated to Azure via OIDC. `deploy-infrastructure` showed 4/4 green runs, but its `azure/login` steps sit in jobs that are skipped on push — only "Validate Bicep" actually ran. Do not read a green deploy workflow as evidence that OIDC works.

---

## 2. The five subjects you need

GitHub's assertion changes depending on **how the job runs**. A job that declares `environment:` asserts `environment:NAME` — *not* the branch. So one credential is not enough.

| Subject | Needed by |
|---|---|
| `repo:spaarke-dev/spaarke:ref:refs/heads/master` | `publish-provisioning-arm-artifacts`, `build-provisioning-sidecar`, `publish-dataverse-solutions-manifest` |
| `repo:spaarke-dev/spaarke:pull_request` | `deploy-infrastructure` (PR validation) |
| `repo:spaarke-dev/spaarke:environment:dev` | `deploy-infrastructure`, `deploy-spaarke-ai` |
| `repo:spaarke-dev/spaarke:environment:staging` | `deploy-bff-api` |
| `repo:spaarke-dev/spaarke:environment:production` | `deploy-bff-api`, `deploy-spaarke-ai` |

Only the first unblocks the currently-red workflow. **The rest will fail identically the first time they actually run** — create all five now rather than rediscovering this four more times.

---

## 3. Step 1 — identify the app registration

`AZURE_CLIENT_ID` is a write-only GitHub secret; it cannot be read back from the UI or the API.

Azure portal → **Microsoft Entra ID → App registrations → All applications**. Find the deployment app (named `spaarke-*`). Its **Application (client) ID** must equal the value stored in `AZURE_CLIENT_ID`.

If you cannot confirm the match, do not guess — adding credentials to the wrong app leaves the original error unchanged and adds a misleading entry to a second app.

---

## 4. Step 2 — create the credentials

### Option A — Azure CLI (all five at once)

```bash
APP_ID="<Application (client) ID from Step 1>"

for SUB in "ref:refs/heads/master" "pull_request" \
           "environment:dev" "environment:staging" "environment:production"; do
  NAME="gh-$(echo "$SUB" | tr ':/' '--')"
  az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"$NAME\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:spaarke-dev/spaarke:$SUB\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"
done
```

`issuer` and `audiences` are fixed protocol values. **Do not change them** — a mismatch produces the same `AADSTS700213` with no additional diagnostic.

### Option B — Portal

App registration → **Certificates & secrets** → **Federated credentials** → **Add credential** → scenario **GitHub Actions deploying Azure resources**.

- Organization: `spaarke-dev`
- Repository: `spaarke`
- Entity type: **Branch** (`master`), then repeat for **Pull request** and **Environment** (`dev`, `staging`, `production`)

---

## 5. Step 3 — verify

```bash
az ad app federated-credential list --id "$APP_ID" -o table
```

Then re-run the failed workflow: **Actions → Publish Provisioning ARM Artifacts → Run workflow**. The `Azure login (OIDC)` step should now succeed.

---

## 6. Authentication is not authorization

The FIC lets the workflow **prove who it is**. It grants **no permissions**. Each workflow also needs an RBAC role on whatever it touches:

| Workflow | Needs |
|---|---|
| `publish-provisioning-arm-artifacts` | `Storage Blob Data Contributor` on the account in `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` |
| `publish-dataverse-solutions-manifest` | same storage account |
| `build-provisioning-sidecar` | `AcrPush` on the registry in `SIDECAR_ACR_LOGIN_SERVER` |
| `deploy-bff-api`, `deploy-infrastructure`, `deploy-spaarke-ai` | scope-appropriate role on the target resource group / subscription |

**If login succeeds and the next step returns 403, that is the RBAC gap — not a regression in this setup.** The two failure modes look similar in the log and are commonly conflated.

---

## 7. Related

- Tenant-isolation invariant **I1** (`spec.md` FR-28 / `design.md` §4D): never hardcode a tenant ID. These credentials are tenant-scoped by construction — the app registration lives in exactly one tenant.
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke auth architecture.
- [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — the authoritative operator guide for standing up a customer environment.
- Azure docs: [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
