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
- UAC server gate (02:24–02:54Z): PASS.
  - Health: no 403s or 5xx.
  - Ordinary moves: report-only, moved=0.
  - Proof reads: the dev proofs pass.
  - Pre-existing: ACME 0x80072560 (#2163); SECURE-CENSUS steady at 292 per 30 minutes; Redis lease renewal failures, filed by UAC as #2431.
  - No Office or identity traffic at night; UAC watches the next daytime window.
- After-notices were sent about 03:00Z to ontology, word-add-in (which then releases #2420), field-mapping, ebilling and provisioning.

## 9. Dev BFF `9f4afe82ac` (2026-10-30)

- The first candidate, `c91b99e575`, was held for word-add-in's finding #2468 and never deployed. It was replaced by master `9f4afe82ac` (#2471, the #2468 fix, on top of #2470). Compared with `c91b99e575`, src/server changes only RecordOwnershipResolver.cs and RecordFamilyReader.cs.
- 194b (`2cd0f62e2c`) never reached dev before this deploy: a live read at 12:38:47Z showed `136568cf70`. The Kudu list showed the previous deployment at 2026-10-29 02:20:34Z. UAC's Dataverse read found no affected document, so no repair was needed.
- Owner go: "go BFF deploy of 9f4afe82ac". OKs for this exact SHA: UAC, word-add-in, ebilling, ontology (on condition that Router is green), field-mapping (filtered suites 6119 passed). Provisioning has a standing OK.
- CI run 41064608665: Router green. The Release build in `C:\wtbffe-9f4` had 0 warnings.
- Pre-deploy checks: no app setting matches `*OrdinaryMovesWritesEnabled*` (0 of 217 names, read-only). phaseA: live `136568cf70`, healthz 200.
- Deploy: `scripts/Deploy-BffApi.ps1 -Environment dev` from `C:\wtbffe-9f4`. The 4 critical files were SHA-256 verified, health passed, and both CORS origins are present.
- Readback 14:40:39Z: ProductVersion `1.0.0+9f4afe82ac9fef2aaf725464f46bae09eadf33db`, healthz 200, healthz/dataverse 200, `/api/me` anonymous 401.
- Rollback (owner approval): `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261030T143826Z` (222 files, `136568cf70`).
- After-notices were sent about 14:42Z to UAC (App Insights gate), ontology (168/173 proof), field-mapping (task 076), word-add-in, ebilling (hold until "gate passed, you can go") and provisioning.
- UAC server gate (14:40–15:12Z, compared with the prior 7 days): PASS.
  - Clean: 0 5xx (the 6 failures are anonymous 401 probes); 0 [OWNERSHIP-REFUSED]; 0 OFFICE_022/014; the SecureChild reconciler made no reassign or put-back writes, and ordinary moves ran report-only.
  - Pre-existing, unchanged: [SECURE-CENSUS] root-BU reach until the BU1 move; #2163 FLS proof 403 and [ID-LINK-RECON] for spaarke-acme; 1 transient Redis lease renew.
- About 15:13Z: R3 sent ebilling "gate passed, you can go".
- 15:21:14Z: ebilling applied its EB-D-173 dev settings: `Ebilling__Enabled=true`, `Ebilling__StoredFileReconcileEnabled=false`, plus the three `Finance__FileStore__*` settings (blob endpoint, tenant id, malware scanning off). The app restarted. Afterwards, healthz returned 200 and an anonymous finance route returned 401. To roll back, ebilling removes the five settings.

## 10. Dev BFF `9f4e3820b2` (2026-10-30, deployed by uac-r2)

- Owner go given in uac-r2's session. R3 gave its OK for this exact SHA, on condition that Router passes. It contains the live `9f4afe82ac`, #2450 `929dc42d31` (the nine grid allow-list columns) and `e0df773395`. No non-comment `sprk_invoicestatus` in src/server. None of the 16 changed src/server files is an external endpoint, ExternalAccessModule, FetchService or the contact-identity code, but external writes do pass through the changed ownership code.
- uac-r2 deployed at 19:22:50Z. Router passed about 19:20Z. Package 39.07 MB; the 4 critical files match by SHA-256; healthz and CORS passed.
- uac-r2's gate reports [OWNERSHIP-REFUSED] and 4xx/5xx on the external routes separately. Before each settings switch, uac-r2 sends R3 a notice with the time: `SecureRoot__Inheritance__RootOwnerWritesEnabled` first, then `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled`. Ontology lifted its gate on the latter. Each switch restarts the app.
- The previous build (`9f4afe82ac`) is the rollback target. Rollback is uac-r2's to run, with owner approval.
- uac-r2 gate, 19:22:50–19:43Z: PASS. 0 failed requests and 0 internal 5xx. **The external routes had no traffic in the window**, so external writes through the changed ownership code are not yet exercised. Only the old warnings remain (SECURE-CENSUS, #2163).
- About 19:45Z: uac-r2 set `SecureRoot__Inheritance__RootOwnerWritesEnabled=true`. The app restarted. It is report-only while the moves switch is off. `OrdinaryMovesWritesEnabled` is due about 10 minutes later, with its own notice.
- About 19:53Z: uac-r2 set `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled=true`. The app restarted. The 5-minute reconciler now writes owners: 10 WAs move from the root team to the BU1 team, with their rows and documents; 12 WAs and 6 projects move to their own unit's team; and the ordinary-moves plan is applied. It converges in about 2–3 runs. uac-r2 gates it afterwards, with the external routes split out. These are internal ownership changes. If portal row counts or record opens change after 19:53Z, check this first.
- uac-r2 post-flip gate, 19:53:19–20:13Z: PASS.
  - Requests: 0 5xx. The 4 failures are anonymous platform probes. 0 [OWNERSHIP-REFUSED] anywhere. External routes again had no traffic.
  - Moves: all 28 roots were re-owned in 2 runs (19:55 and 20:00Z), as planned. The ordinary-moves pass is converging (about 21 rows plus 2 events so far), with refused=0 and failed=0. Documents are in place, with no container move.
  - Only old warnings remain: SECURE-CENSUS, #2163, and the AI catalog drift at startup (since 10-22).
  - uac-r2 has no more settings changes. The dev BFF is free for R3's next deploy, which needs fresh OKs and an owner go for its SHA.
- Still unexercised: an external-user write through the new ownership code. The owner's portal check (task 002) is the first chance.

## 11. Dev BFF `b083d62183` (2026-10-31, deployed by uac-r2)

- Owner go in uac-r2's session; R3 OK for this SHA. It contains #2450, e0df773395 and field-mapping #2486, whose analysistool re-seed field-mapping runs in its own session. Deployed at 19:52:25Z. The 4 critical files match by SHA-256 and healthz passed. Settings unchanged: both write switches are still on. uac-r2's gate splits out the external routes.
- The dev SPA is the same SHA since 19:12:41Z (R3, owner go).
- uac-r2 gate, 19:52:25Z → ~20:12Z: PASS. 23 requests, 0 failed, 0 5xx, 0 [OWNERSHIP-REFUSED]; the reconciler wrote nothing. **External routes again had no traffic.** Only the old warnings remain (SECURE-CENSUS, #2163). uac-r2 has no further deploy planned until its task 195 merges; it will send the SHA first.
