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
- 2026-10-31: R3 OK'd uac-r2's next dev BFF candidate **d0c1c5dc3c** (task 195 #2496, project filing; owner go in uac-r2's session). Its src/server delta from b083d62183 is 5 files and touches no external endpoint. Non-BFF changes in the same window: the wizard web resource, two code pages, a ribbon import, and a backfill filing 7 projects under their matter (3 change BU). R3 asked uac-r2 to list any external grants on those projects or their matters.

## 12. Dev BFF `d0c1c5dc3c` (2026-11-01, deployed by uac-r2)

- Owner go in uac-r2's session; R3 OK for this SHA (§11). Deploy started at 02:56:14Z; the app started on the new build at 03:09:00Z (one platform deploy-status timeout, recovered automatically). The 4 critical hashes match, healthz is 200 and settings are intact. The 02:55:00Z restart was ebilling's setting change.
- Same window: wizard web resources (03:12Z), the Project ↔ Matter subgrid ribbon (~03:27Z, matter and project only), and the backfill (03:14:07Z → 03:14:58Z) filing 7 projects under their matter, with the 3 planned unit moves.
- External effect (uac-r2 live read ~03:20Z): none of the 7 matters is secure, and there are 0 No Access rows on the 7 projects or 7 matters, so the three projects with active grants (PRJT.10008.01, PRJT.10006.01, PRJ-000038) are unchanged for partners. PRJT.10001.01 inherits Restricted from REAL-2026-123456.02; it has no active project grants, so no grantee loses it (uac-r2 reports it to the owner).
- ebilling 015 ran at 03:14:51Z (R3 no objection): `sprk_invoice.sprk_invoicestatus` and its option set were deleted on dev. 20 `Sql Number: 207` faults on `SecureChildReconciler` descendant reads fell inside that delete window (03:14:36Z → 03:15:18Z); none since.
- uac-r2 gate, 03:09:00Z → ~03:31Z: PASS. 41 requests; the 4 non-2xx are anonymous 401s; 0 5xx, 0 [OWNERSHIP-REFUSED]; ownership passes refused 0, failed 0, converged since 03:28Z. **External routes again had no traffic.** The dev SPA stays `b083d62183`.

## 13. Dev BFF `fcf01480f4` (2026-11-01, deployed by R3)

- Content: master after PR #2500 (R3 task 079: #2458 + #2491). The src/server delta from `d0c1c5dc3c` is 3 files: `GrantAccessNotifier.cs`, `ContactIdentityStore.cs` and `AssignTaskRecipientGate.cs`. The build contains #2450, ebilling 015 (no non-comment `sprk_invoicestatus`) and ontology 077 (283466d40).
- Go: the owner in R3's session ("go BFF deploy of fcf01480f4"). OKs: uac-r2, ontology, word-add-in, field-mapping (no re-seed needed) and ebilling; provisioning standing. Router green on #2500.
- Pre-deploy checks:
  - The two write switches (`SecureRoot__Inheritance__RootOwnerWritesEnabled`, `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled`) are present and `true`, as they have been since §10. The §9-era "no such setting" check no longer applies.
  - ebilling's `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exited 0 (272 OK).
  - phaseA: live `d0c1c5dc3c`, healthz 200. Rollback snapshot `deploy/rollback/wwwroot-20261101T140218Z` (197 files).
- Deploy: `scripts/Deploy-BffApi.ps1 -Environment dev`, run from a detached worktree at `fcf01480f4`. Package 39.09 MB. Received at 14:03:20Z. The 4 critical files match by SHA-256; health and CORS checks passed.
- Readback at 14:04:42Z: ProductVersion `+fcf01480f4…`; /healthz 200, /healthz/dataverse 200, anonymous /api/me 401. 225 app settings, unchanged. ebilling's six keys are present.
- `sprk_externaltenantid` is field-secured (`IsSecured` true). Both BFF app users (5967251e…, 1e40baad…) are members of "Spaarke Identity Link Writers" (canread 4), so the new by-id home-tenant read can see the column.
- Rollback: redeploy `d0c1c5dc3c`, or restore the snapshot above.
- Next: uac-r2's 30-minute gate (external split). Ontology's CMRCL-479772 re-check with Tony Howe; the owner runs it. Expected: one email from the system mailbox, no bell, no wizard warning.
- uac-r2 gate, 14:03:20Z → 14:35:00Z: **PASS**.
  - 52 requests. The 6 non-2xx are anonymous 401s. 0 5xx and 0 [OWNERSHIP-REFUSED].
  - Ordinary-moves runs: moved 0, refused 0, failed 0. No [ROOT-OWNER] moves.
  - **External routes had no traffic.**
  - No [GRANT-NOTIFY] or [ASSIGN-TASK-NOTIFY] lines in the window.
  - The census findings are the expected ones for the users still in the root.
  - 6 `ReadProofAsync` exceptions come from the identity-link reconciler's **spaarke-acme** pass ("not a member of the organization", tracked as #2163). They started at 13:40:02Z, before this deploy. The spaarkedev1 pass reads 11 of 11.
- uac-r2's owner-approved move of the dev users to BU1 waits for R3's "done" on the CMRCL-479772 re-check.

## 14. Dev users and BFF app users moved to Spaarke Business Unit 1 (2026-11-01/02, uac-r2, owner-approved)

- uac-r2 ran the move step by step, with the owner's go for each.
  - **Product hand-over:** 263 configuration rows, including grid configurations, were reassigned to the root default team.
  - **Steps 1–2:** 11 internal users moved to BU1, with copies of their roles.
  - **Step 4:** 684 customer rows were reassigned to the BU1 team, and 119 children followed through the reconciler. These include all 32 `sprk_externalrecordaccess` rows, none under a secure root: 17 under roots that moved in this step, 15 under records already in BU1. Also 8 contacts.
  - **Step 5:** both BFF app users moved to BU1.
- **mi-bff-api-dev:** roles before [System Administrator], after [System Administrator @BU1]; no-role gap 6.8 s.
- **SDAP-BFF-SPE-API:** roles before [Delegate, Spaarke Ontology Service, System Administrator], after the same three @BU1; no-role gap 4.0 s.
- Both app users' field security profiles are unchanged by membership. R3 re-read `sprk_externaltenantid`'s field permissions after step 5: both BFF app ids are still in "Spaarke Identity Link Writers" (canread 4). /healthz and /healthz/dataverse are 200.
- `ExternalAccess:CustomerBusinessUnitId` was already BU1 (owner Q8), so nothing changed.
- External effect expected: none. Contact access comes from grants, and the BFF reads grants and contacts app-only.
- Step 3 (the administrator's own account) was **not run**. Dataverse refuses a self-move unless a role-keeping org setting is on, and the owner chose to leave that account in the root with all 5 roles. The move is closed: every other interactive user and both BFF app users are in BU1, and the customer families and the 32 grant rows are on the BU1 team.

## 15. Dev BFF `3740e1a105` (2026-11-02, deployed by R3; batch for field-mapping and ebilling)

- **Content:** master at #2520. Field-mapping 039 (0e3962069b), #2514 (a50ada7330), #2519 (961c6846af), #2520; ebilling #2507 (0878ca36ac) and #2501/#2503/#2510/#2512; uac #2509; #2516. 57 src/server files changed from `fcf01480f4`. The only external-surface change is ebilling 097f in `ExternalInvoiceSubmissionEndpoints.cs`: the replaced-by-correction note, under the same visibility rule, failing closed. #2450 is present, and there is no non-comment `sprk_invoicestatus`. Router green.
- **Go:** the owner in R3's session ("go bff deploy" of 3740e1a105).
- **OKs:** ontology (a9), uac-r2 (38), field-mapping (5c) and ebilling (7b); provisioning standing. Word-add-in's request expired undelivered (a session permission-mode hold), and the owner's go went ahead without it.
- **Pre-deploy checks:**
  - Settings: 225; both write switches true; ebilling's six keys present.
  - `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exited 0 (272 OK). ebilling's own four `-Verify` checks also exited 0 (its report).
  - phaseA: live `fcf01480f4`, healthz 200. Rollback snapshot `deploy/rollback/wwwroot-20261102T185123Z` (197 files).
- **Deploy:** `Deploy-BffApi.ps1 -Environment dev` from a detached worktree at `3740e1a105`. Package 39.29 MB. Received at 18:52:25Z. The 4 critical files match by SHA-256; health and CORS checks passed.
- **Readback** (18:53:42Z): ProductVersion `+3740e1a105…`; /healthz 200, /healthz/dataverse 200, anonymous /api/me 401. Settings are unchanged (225, ebilling reconcile off).
- **After-notices** sent to uac-r2 (gate), ebilling, field-mapping (its #2519 chat-tool re-seed and checks A–C run in its own session), ontology and word-add-in.
- **Rollback:** redeploy `fcf01480f4`, or restore the snapshot above.
- **UAC gate (uac-r2, 18:52:25Z–19:22:25Z): PASS, no reason to roll back.**
  - 30 requests, 0 5xx. The 7 failures are anonymous 401s.
  - External split: 0 external requests in the window; the 7-day baseline is unchanged. The 097f note path has had no live traffic yet.
  - Finance: 0 requests.
  - Ownership: no refusals and no reassign or put-back writes. FOLLOW-PARENT and SECURE-INHERIT show unsecured, refused and failed all 0.
  - SECURE-CENSUS: findings=0 at 19:00Z and 19:15Z.
  - Exceptions: none new (#2292, the known acme readability read, and the AI catalog drift check, all as before).
  - Information only, which uac-r2 is following up with field-mapping: from 19:05Z the ROOT-OWNER census reports 1 project filed under a matter with no link row, up from 0. It coincides with a new project (61 → 62), probably field-mapping's quick-create check. It has no effect on ownership or read access.

## 16. Dev BFF `5796774091` (2026-11-03, deployed by R3; ebilling 151c, Email to AP)

- **Content:** master at the #2526 merge (ebilling 151c: the AP export Email to AP and Mark Sent to AP routes, the server-send marker on `CommunicationService` and `IncomingCommunicationProcessor`, the missing-column retry). Also #2525 (office-addins only) and #2521 (tests only). The src/server delta from `3740e1a105` is #2526's 12 files. R3 checked that the external message post (`PostToRecordThreadAsync`) is unchanged. #2450 is present, and there is no non-comment `sprk_invoicestatus`.
- **CI:** the first Router run failed because the Tier 1 arch-test job was cancelled at its 10-minute limit with every step green (filed as #2527). The failed jobs were re-run, and Router went green at 2026-11-02 23:06:42Z.
- **Go:** the owner in R3's session ("go BFF deploy 5796774091").
- **OKs:** ebilling (7b), ontology (a9), uac-r2 (38), field-mapping (5c), word-add-in (dc); provisioning standing.
- **Pre-deploy checks:**
  - Settings: 225; both write switches true; Ebilling Enabled=true, StoredFileReconcileEnabled=false, MalwareScanningEnabled=false; the three name-only keys present.
  - `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exited 0 (272 OK).
  - phaseA: live `3740e1a105`, healthz 200. Rollback snapshot `deploy/rollback/wwwroot-20261103T132814Z` (197 files).
- **Deploy:** `Deploy-BffApi.ps1 -Environment dev` from a detached worktree at `5796774091`. Package 39.31 MB. Received at 2026-11-03 13:29:19Z. The 4 critical files match by SHA-256; health and CORS checks passed.
- **Readback** (13:30:33Z): ProductVersion `+5796774091…`; /healthz 200, /healthz/dataverse 200, anonymous /api/me 401. Settings unchanged (225).
- **After-notices** sent to all five. uac-r2's gate runs from 13:29:19Z, with the external, finance export-batches and verified server-send splits. ebilling then imports the ApExport code page, ribbons and views.
- **Rollback:** redeploy `3740e1a105`, or restore the snapshot above.
