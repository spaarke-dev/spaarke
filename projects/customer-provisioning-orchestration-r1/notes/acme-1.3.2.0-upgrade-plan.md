# ACME upgrade to SpaarkeMaster 1.3.2.0: plan

> Written 2026-11-20 (Desktop session `provisioning-r1`, owner "Yes, plan it"). Nothing in this plan has run. Each live step
> needs its own owner go (project `CLAUDE.md` §3 "Live actions").

## Where ACME stands (read-only checks, 2026-11-19/20)

| Item | Value |
|---|---|
| Registry row (`spaarkedev1`, `sprk_customerid = acme`) | Setup Status 2 (Ready), no current run, `sprk_bffversion` 2026.10.16-2, `sprk_solutionversion` "SpaarkeMaster Managed 1.3.0.0". ACME is the only stamp in the registry. |
| Dataverse `spaarke-acme.crm.dynamics.com` | SpaarkeMaster 1.3.0.0 managed. |
| BFF `sprk-acme-prod-api` | Push deployment of 2026-10-16 (build 2026.10.16-2); healthy. |
| L2 control plane (dev, `rg-spaarke-platform-dev`) | API and Worker last deployed 2026-11-10 ~23:30Z, before tasks 267 (#2676, 11-11), 268 (#2766), 269 (#2780) and 270 (#2862, #2871) reached master. It has none of the writer unit, S26, S27 or S28. |
| BFF artifact H9 would deploy (`sprkcpartifactsdev` `provisioning-artifacts/latest.json`) | buildId **2026.10.16-2**, the build ACME already runs. A run redeploys the same build; it does not upgrade the BFF. |
| Release-process §6a (users with an add-on role but no Core User) | None: two enabled users hold only Spaarke Basic User, one holds System Administrator. Nothing to do. |
| Task 267 K3 (`DoNotRemoveRolesOnChangeBusinessUnit`) | Not set (platform default: roles are removed on a unit change). H7b S24 will not quarantine the moved writer. |
| Data | 0 rows in `sprk_invoice`, `sprk_billingevent`, `sprk_invoicecorrection`, `sprk_invoicesubmission`, `sprk_communication`, `sprk_ruleset`, `sprk_matter`, `sprk_document`. The ebilling post-import script (`Deploy-EbillingSubmissionTrackingScopeSchema.ps1` steps 1–8) is not needed. |

## Steps

1. **Deploy the L2 control plane (dev) from master.** Owner go. `scripts/provisioning/Deploy-ControlPlane.ps1`, in-process
   with `-Confirm:$false` (gotcha: `pwsh -File` stops at the ConfirmImpact prompt). Check that the Exchange sidecar image
   pin matches the code (T186 L58) and that both API and Worker answer health afterwards. From here, any re-run of a stamp
   runs H7b procedure 10, which stops at S27 (Resumable) until the stamp has 1.3.2.0; ACME is the only stamp.
2. **Prepare the upgrade intake for ACME.** Same values as ACME's original run (subscription, `dataverseEnvUrl`, the
   environment type it already has; the registry row holds the subscription and resource names). Add `buildId = 2026.10.16-2`, so H9 refuses
   (Resumable, nothing written) instead of upgrading the BFF if a newer artifact is published before or during the run.
   Leave the AI spend limit out (the skill: on an upgrade run, omitting it leaves the setting alone).
3. **Run `/provision-environment acme` as an UPGRADE run.** Owner go (the skill also asks the operator to confirm upgrade mode).
   What it should do:
   - H6: managed stage-and-upgrade 1.3.0.0 → 1.3.2.0 (applies ISS028's five Core User removals on `sprk_invoicecorrection`).
   - H10: moves the ontology writer into its own unit (task 267). First live check of task 267 K2 (`businessunitid` PATCH);
     a refusal is Resumable.
   - H7b procedure 10: S24–S28 verify, S10–S12 put the BFF identities and every default team (except the writer unit's)
     in the BFF-managed profiles.
   - H4b rewrites the BFF app settings, including `AiSearch__AllowedIndexes` from H2b (closes the open ACME
     `AiSearch__AllowedIndexes__N` item).
   - H9 redeploys 2026.10.16-2 (a restart, same code).
   - The rest re-runs idempotently; H13 sets Ready and writes `sprk_solutionversion` "SpaarkeMaster Managed 1.3.2.0".
4. **Verify.** Solution 1.3.2.0 managed on ACME; registry row updated; `sprk-acme-prod-api` `/healthz`; Core User on
   `sprk_invoicecorrection` holds only Read/Append/AppendTo (`spaarkemaster-1.3.2.0.json`); a sign-in as a Basic User guest
   opens the Console. Record the run in `notes/t186-acme-run-log.md`.

## Risks and limits

- **The BFF stays on 2026.10.16-2 over the 1.3.2.0 schema.** 1.3.1.0 and 1.3.2.0 secure columns that later BFF builds
  write as the app; whether the 10-16 build writes any of them as the signed-in user is NOT checked. ACME has no invoice,
  billing or message data, so the exposure is limited to someone trying those features on ACME before its BFF upgrade (which
  waits on A9 home sign-in, #2443/#2532).
- **No downgrade.** H6 refuses a lower version; recovery is forward (fix and re-run). ACME holds no data, so a failed run
  costs time, not records.
- **Out of scope:** the BFF upgrade (A9 gate), H7c (#2429), go-live gates.
