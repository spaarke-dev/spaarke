# Contact identity hardening: dev rollout record

> Live steps on dev for the contact identity hardening (PR #2360 `078f62bcad`, PR #2386 `e56a3a51d4`). Each step had the owner's go, with the commands and rollback shown first. Output is masked: claim names and types only, never tokens, emails, UPNs or object ids.
> Earlier dev setup for A9: `task-059-dev-setup-record.md`.

## 1. Contact security schema (2026-10-28)

- `scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Apply` on spaarkedev1, then a direct Standing Grant Administrators membership for the two BFF application users (deployment guide 6.5.2).
- `-Verify` after #2386, ~13:35Z: **VERIFY PASS, exit 0**. Step (g) printed OK (not OK-ROLE) for both application users; (h) OK.

## 2. Dev BFF `e56a3a51d4` (2026-10-28)

- Readback 14:11:58Z: ProductVersion `1.0.0+e56a3a51d431d924003948ce88ae85940eefbda8`, healthz 200, healthz/dataverse 200, `/api/me` anonymous 401.
- UAC server gate (14:11–14:41Z): PASS on health, report-only reconcile (moved=0) and proof reads. The caller-scoped live check is deferred under #2389.
- Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261028T140705Z` (`de38296499`).

## 3. `xms_edov` optional claim on the dev BFF app (2026-10-28 17:29:34Z)

- **Before (read-only):** dev BFF app `1e40baad`: token version 2; access-token optional claims `email, preferred_username, upn, acct, given_name, family_name`; Azure CLI pre-authorized.
- **Write:** PATCH `optionalClaims` on `1e40baad` with every existing entry plus `{"name":"xms_edov","essential":false}` in `accessToken`. Exit 0.
- **Read after:** access-token optional claims `email, preferred_username, upn, acct, given_name, family_name, xms_edov`.
- **Rollback:** PATCH `optionalClaims` back to the saved before-snapshot (the six claims above).
- **Token probe, Spaarke-directory member (Azure CLI, v2 access token for the dev BFF):**

  | | Before | After |
  |---|---|---|
  | `ver` / `acct` | 2.0 / 0 | 2.0 / 0 |
  | `email` present | yes | yes |
  | `xms_edov` | absent | **present, JSON boolean `true`** |

  So Entra emits `xms_edov` in v2 **access** tokens for a work account once the resource app requests it. Until now that had only been inferred from the documentation.
- A probe for a customer-directory (Dewey Cheatham) workforce user needs that user's sign-in and its own owner go (pending).

## 4. `removeUnverifiedEmailClaim` (read 2026-10-28)

- Already `true` on the dev BFF app `1e40baad` and on the multitenant SPA client `5859e812` (set 2026-10-19, task 059 stage B and C2). Read back 2026-10-28: both `true`. No write needed on dev.
