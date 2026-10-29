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
- **Token probe, customer-directory member (owner go; the owner signed in as the Dewey Cheatham test member in an isolated Azure CLI profile, deleted afterwards):** v2 access token for the dev BFF, `tid` = Dewey Cheatham `bc3aa7f4`, `acct` 0, `email` present, **`xms_edov` present, JSON boolean `true`**. A customer-directory work account therefore carries the verified-domain signal too. The bind's tenant leg stays as the fallback when the claim is absent.

## 4. `removeUnverifiedEmailClaim` (read 2026-10-28)

- Already `true` on the dev BFF app `1e40baad` and on the multitenant SPA client `5859e812` (set 2026-10-19, task 059 stage B and C2). Read back 2026-10-28: both `true`. No write needed on dev.

## 5. Removal sweep, report-only (read 2026-10-28 ~20:20Z)

- Switches on `spaarke-bff-dev`: `IdentityLink__Reconciliation__WritesEnabled` = true, and `RemovalSweepWritesEnabled` is **unset**. The sweep needs both, so it is **report-only**. No change was made.
- App Insights since 14:11Z: no `removal-sweep` lines. The dev environment's heartbeat shows `readability=Readable`, so the sweep was not skipped, and no failure line was logged.
- Direct count of the sweep's candidate queries on spaarkedev1 (filters from the code at `e56a3a51d4`): inactive, bound, unmarked contacts **0**; active grants of inactive or marked contacts **0**; contacts already marked removed **0**. The sweep has nothing to mark on dev.
- Gap found: a pass with no candidates leaves no telemetry. Filed as ISS-020, #2415.

## 6. Re-invite the unbound contacts (read 2026-10-28)

- Two contacts hold active grants without a binding: Jonathan Smith (2 grants) and Jane Doe (4 grants). Neither has an email address, so neither can be invited or bound. They are left as they are; the owner was told.

## 7. SpaarkeMaster 1.2.4.0 published (2026-10-29 02:24:48Z)

- Owner go. Peer OKs: UAC, ontology, field-mapping; provisioning had no objection, and its H6/H7b lock-columns check passed at `136568cf70`.
- Dry run `40803358901` (publish:false) on `136568cf70`: success.
- Master moved to `a4b6459da3` (#2400, a wizard UI fix) before the publish. The SpaarkeMaster source diff between the two commits is empty, and the owner re-approved naming `a4b6459da3`.
- Run `40806065681`, attempt 1: success. Blobs `dataverse-solution-SpaarkeMaster-1.2.4.0-managed.zip` and `-unmanaged.zip`; manifest `dataverse-solutions-2026.10.29-277-1.json` → `dataverse-solutions-latest.json`, read back as version `1.2.4.0`.
- Rollback: check that `dataverse-solutions-latest.previous.json` names 1.2.3.1, then copy it over latest. If it doesn't, re-point to 1.2.3.1's versioned manifest. New provisioning runs then refuse at H7b again.
- Follow-ups owned elsewhere:
  - Provisioning: after a stamp's stage-and-upgrade, confirm the removed role privileges are gone (ontology's check).
  - Field-mapping: check that the entity map is removed where 1.2.4.0 lands.

## 8. Dev BFF `136568cf70` (2026-10-29)

- Owner go; OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing).
- Readback 02:23:53Z: ProductVersion `1.0.0+136568cf701b68fe8b4132fe2223185a45f8f7a2`, healthz 200, healthz/dataverse 200, `/api/me` anonymous 401.
- Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261029T021735Z` (`e56a3a51d4`). Deploy worktree `C:\wtbffe-136`.
