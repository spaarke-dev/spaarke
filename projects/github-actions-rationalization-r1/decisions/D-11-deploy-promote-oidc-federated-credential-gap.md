# D-11 — deploy-promote.yml "Deploy to Dev" OIDC federated-credential gap

> **Date**: 2026-06-01 (closeout PR)
> **Project**: github-actions-rationalization-r1
> **Phase**: Closeout (post-PR-#317-merge)
> **Disposition**: **DEFERRED** — owner action required in Entra. NOT a workflow code bug.

---

## Context

After PR #317 merged to master (commit `287a9b7d` at 2026-06-01 19:42:34Z), the natural workflow chain fired:

- `SDAP CI` on master → SUCCESS at 19:42:39Z (Wave B fixes + suppression policy working as designed)
- `Environment Promotion` (`deploy-promote.yml`) triggered via `workflow_run` after SDAP CI → ran at 20:02:01Z

Job sequence on that run (`26778717525`):
- `Plan Promotion` → **success** (the Wave B cascade fix is correctly gating on SDAP CI success)
- `Deploy to Dev` → **failure** (NEW issue surfaced now that the workflow actually executes its jobs)
- `Deploy to Staging` / `Deploy to Production` → skipped (correctly — `needs:` chain)
- `Promotion Summary` → success (the Wave B `if:` filter on the summary job preserved the structural correctness)

The Wave B fix (task 010 — workflow-level `if:` on `summary` job) is **functioning correctly**. The new failure is on the `Deploy to Dev` job's `Azure Login (OIDC)` step.

## Evidence

Full error from the `Azure Login (OIDC)` step (run `26778717525`):

```
AADSTS700213: No matching federated identity record found for presented assertion subject
'repo:spaarke-dev/spaarke:environment:dev'.

Check your federated identity credential Subject, Audience and Issuer against the presented assertion.
https://learn.microsoft.com/entra/workload-id/workload-identity-federation

Trace ID: 4710c0b7-8dd0-457d-8702-66e468b40d00
Correlation ID: 2c4353fd-c062-434f-a960-e8036fbf9ee0
Timestamp: 2026-06-01 20:02:36Z

Login failed with Error: The process '/usr/bin/az' failed with exit code 1.
```

## Root cause

The Entra app registration that `deploy-promote.yml` uses for OIDC authentication does NOT have a federated identity credential configured for the subject pattern `repo:spaarke-dev/spaarke:environment:dev`.

Per GitHub Actions OIDC + Azure federated identity docs:
- When a workflow job declares `environment: dev` and requests an Azure token via `azure/login@v1+` with `client-id` + `tenant-id` (no client-secret), the OIDC token GitHub mints carries the subject claim `repo:{owner}/{repo}:environment:{env}` (in our case: `repo:spaarke-dev/spaarke:environment:dev`).
- That subject must match a federated identity credential pre-configured on the Entra app registration.
- Without a matching credential, Entra rejects the token request with AADSTS700213 (the exact error observed).

This is a one-time Entra-tenant configuration that was apparently never done for the `dev` environment (and presumably not for `staging` or `prod` either — they'd fail the same way if the chain reached them).

## Disposition

**DEFERRED** — explicit owner action required.

### Why deferred (not fixed in this PR)

1. **Out of scope**: The fix is in Entra app-registration configuration, not in repository code. The github-actions-rationalization-r1 project's NFR-01 explicitly limits us to `.github/`, `docs/`, `projects/`; even with the suppression-PR scope relaxation, Entra-tenant configuration is well outside what a code PR can touch.
2. **Requires owner access**: Adding federated identity credentials requires Application Administrator role (or higher) in the Entra tenant where the app registration lives.
3. **Doesn't block PR merge or the original FRs**: The cascade fix (FR-03) — making `deploy-promote.yml` not record `failure` when upstream SDAP CI fails — is verified working. This new failure is a different, pre-existing latent issue that was previously hidden by the cascade bug (when every workflow_run-triggered run was a 0s failure, nothing ever reached the OIDC step).

### What the owner needs to do (when ready)

In the Entra portal (Azure AD → App registrations):

1. Locate the app registration that `deploy-promote.yml` uses. The client-id is in the workflow's `azure/login` step (likely in a secret like `AZURE_CLIENT_ID` — check `gh secret list` for the exact secret name).
2. Open the app registration → **Certificates & secrets** → **Federated credentials** tab.
3. Click **Add credential** → scenario: **GitHub Actions deploying Azure resources**.
4. For the `dev` environment specifically, set:
   - **Organization**: `spaarke-dev`
   - **Repository**: `spaarke`
   - **Entity type**: `Environment`
   - **GitHub environment name**: `dev`
   - **Name** (free text): `spaarke-dev-deploy-promote-dev` (or similar)
   - **Description**: "Deploy to Dev via deploy-promote.yml — added 2026-06-XX per D-11"
5. Save. Federated credential is live within ~30 seconds.
6. Repeat for `staging` and `prod` environments (anticipated — the chain will fail at the next environment if you only do dev).
7. Verify by manually triggering the workflow: `gh workflow run deploy-promote.yml --ref master`.

### Alternative — if these environments aren't actively used

If `dev` / `staging` / `prod` environments are not actively used today and the `deploy-promote.yml` workflow is dormant by design, an alternative disposition is to **delete `deploy-promote.yml`** entirely (D-03 delete-by-default per design.md). This would be a separate decision; the project's Wave A inventory + Phase 2 audit kept this workflow because it was thought to be the canonical promotion path.

If owner picks this path, follow the same NFR-04 procedure (`git rm` + commit referencing this decision record).

## Follow-up tracking

This decision is the explicit deferral. Owner action is tracked in:
- This file (D-11) — root rationale + remediation steps
- `.github/WORKFLOWS.md` — should reference this D-record under `deploy-promote.yml` § Common failures
- `projects/github-actions-rationalization-r1/README.md` Open Items section (if not already; closeout-PR will update)

When owner applies the federated credentials, add a sign-off checkbox here similar to FR-12's owner sign-off in `.github/WORKFLOWS.md`.

## Owner sign-off

- [ ] Owner added federated credential for `repo:spaarke-dev/spaarke:environment:dev` on YYYY-MM-DD
- [ ] Owner added federated credential for `repo:spaarke-dev/spaarke:environment:staging` on YYYY-MM-DD
- [ ] Owner added federated credential for `repo:spaarke-dev/spaarke:environment:prod` on YYYY-MM-DD
- [ ] Verified by triggering `gh workflow run deploy-promote.yml --ref master` and confirming all 3 deploy jobs reach Azure Login successfully

OR

- [ ] `deploy-promote.yml` removed via `git rm` + commit referencing D-11 (alternative disposition)

---

*Authored 2026-06-01 in closeout PR for github-actions-rationalization-r1.*
