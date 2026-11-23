# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-23 (Desktop session; ACME upgrade run `3b6f7b65` COMPLETED 14:01:48Z after fixes #2924, #2956, #2980).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress (T186-style live run, no POML). Last completed: ACME UPGRADE run `3b6f7b65-033d-400b-bba4-59541a0cc340` — SpaarkeMaster managed 1.3.0.0 → **1.3.2.1**, BFF pinned 2026.10.16-2, **Completed 2026-11-23 14:01:48Z**, registry Ready. Before it: task 270 (PR #2862 / #2871). |
| **Next Action** | **Owner said "Fix the run's defects" (2026-11-23): ISS-036, ISS-030, ISS-035, ISS-031 — each its own branch from master, PR each, owner merges.** State (checkpoint 2026-11-23 ~16:30Z): **ISS-031 (mine)** `C:\code_files\wt031` branch `fix/iss031-upgrade-run-from-ready`, UNCOMMITTED: new `Core/Models/RegistryRunAdmissionRule.cs` (+ tests `Tests/Models/RegistryRunAdmissionRuleTests.cs`), snapshot gains `ProvisionedOn` (registry client selects `sprk_provisionedon`), REG-07 in `RunsEndpoints.cs` uses the rule and moves Ready→InProgress itself after the try (fail closed 502 `registry-upgrade-transition-failed`), 3 new error codes in `ControlPlaneProblems.cs`, catalog const `ProvisionedOn`, 7 endpoint tests (`PostRuns_Iss031_*`) + ParseSnapshot test. Targeted 71/71 pass; full suite 3498 pass / 1 fail (pre-existing, below). LEFT: skill `.claude/skills/provision-environment/SKILL.md` Step 1a upgrade branch must put `provisionedOn` (= row's sprk_provisionedon) into the Step 4.0 POST (today it never does: ACME's upgrade ran H0/H2a/H4/H9 in FRESH mode, H2a drift check skipped) — then commit, push, PR (`Fixes #2926`). **ISS-036** branch `fix/iss036-intake-buildid` `eb99365b57` PUSHED (agent done; review, then PR `Fixes #2987`); after ISS-031 its parity test's `NotInTheIntakeFile` reason for `provisionedOn` ("skill never sends") must change to "skill sends it from the registry row"; agent's proposed SKILL.md buildId text (Step 1.0 `$buildId = $intake.buildId`, Step 4.0 send when set) to apply in main session. NOTE: H9 only REQUIRES buildId == latest.json's buildId (guard, not selector). **ISS-035** agent stopped: worktree `C:\code_files\spaarke\.claude\worktrees\agent-a126bec6462ddf060` commit `c86f6ffc85` NOT pushed + uncommitted edit to `Tests/Models/ProvisioningRunFailureGateClearTests.cs` — review/finish/push. **ISS-030** agent stopped: worktree `…\agent-aa721a11f29ed677c` UNCOMMITTED edits to `prereqs.yaml`, `PROVISIONING-PREREQUISITES.md`, `tests/scripts/Prereqs-Recipes.Tests.ps1` — review/finish. **Pre-existing master failure** `BffManagedLinkColumnLockListTests.MessageAccessColumnsAndDirectRecipient_EqualTheThreadFlagScriptTargets`: field-mapping 056b (`e50c97d72d`) added 3 `sprk_privatecontext*` columns to `Set-ThreadFlagFieldSecurity.ps1`; H7b S28 `SecureRecordSetupProcedure.MessageAccessColumns` (~line 260) still 5 — decide if the 3 ship in SpaarkeMaster (if not, S28 must not require them on stamps) then fix or file. ALSO open: guest Console sign-in (plan step 4, needs a person); stamp plan sizing (owner); ISS-032 #2942. Done today: ISS-037 #2991 (PR #2993 merged `5e4cf470d6`, ACME seeded, dev L2 Worker deployed from `ca1684dd2f`). Leftover folders for the user to delete by hand: `C:\code_files\wtcp` (28k files, not a worktree), `C:\code_files\wtsl` (empty). Quality alerts open: #1821, #2060, #2067. |
| **Done 2026-11-11/13** | PR #2676 (task 267) → `df93d2029c`. ebilling #2689 → `2fc434c912`. Task 268 → PR #2766 `4b45bdf3fe`; #2725 / ISS-028 filed (19 operator scripts excuse System Administrator by name; ebilling fixing its 4 sites). SpaarkeMaster 1.3.1.0 published 2026-11-13. |
| **Quality alert** | #1821 mutation-nightly open (2026-11-15); not started. |
| **Working tree** | Project worktree clean except this checkpoint; uncommitted work lives in `C:\code_files\wt031` and the two agent worktrees above. |

## 🔔 Owner questions open

1. DONE 2026-11-23: ACME migrated through upgrade run `3b6f7b65` (writer unit live at H10, H7b procedure 10 incl. S25–S28). Open from it: guest Console sign-in, stamp plan sizing.
2. Open from before: `version-compat-matrix.json` stale v1 cell; #2443/#2532 go-live gates (prod workforce client, CIAM BFF app T240d, routing T240c, GitHub env vars, A9 on ACME, DNS); L56 AI content for ACME; demo accounts; dev BFF secrets rotation; ISS-026.

## Follow-ups unblocked by ebilling #2689 (not started)

- Done: task 269 Set-CustomerOwnerTeams writer-unit exclusion (PR #2780 → `3ab55ee877`). ebilling-r1 merged it with their EB-D-220 change (cdde3d69b4): Pester 81/81.

## Master review 2026-11-15 (430 commits `9fef284de0`..`ce6c241964`)

- No change to infrastructure/Bicep, `Provision-*`, the L2 control plane, Key Vault / ADR-028 A4, or Entra registrations.
- **Gap → task 270 / ISS-029 / #2859 (done 2026-11-16, path A):** the #2799 invoice-family lock is not in SpaarkeMaster 1.3.1.0; uac-r2 packages it (planned 1.3.2.0) and H7b S27 verifies it. The message-access lock is in 1.3.1.0 and S28 verifies it; provisioning runs neither script.
- New BFF settings, off by default: `RecordRepair:Pass:GoLive` / `MaxRowsPerTable` / `ProductPrincipalIds` (field-mapping 052b scheduled repair pass). Nothing needed on a stamp until turned on.
- #2761 (uac-r2, `f23dfed281`): the identity-link job reconciles only rows whose `sprk_appservicename` is blank or this BFF's `WEBSITE_SITE_NAME` — keep H13 / Step 6a promoting that column; a REGISTRY-STALE row is now logged by the job.
- spaarkeai-mcp-bff-api-r1 (NOT on master yet): `Mcp:Enabled` (default false), ADR-028 A10 connector client (dedicated, Spaarke-tenant, single-tenant, pre-authorized on the stamp BFF app; one per stamp if it needs a secret, secret never in KV/app settings), M.E.AI 10.11.0, `Deploy-BffApi.ps1` + `publish-bff-artifact.yml` build an embedded widget, and it bumps our `Sprk.Provisioning.ControlPlane.NightlyTests` Logging.Abstractions pin to 10.0.13. Stamp work when Copilot goes to customers: H3 connector registration + pre-auth, `Mcp:Enabled` per stamp, T261 unaffected (delegated OBO, no app role).
- **H7c (#2429)**: `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed): merge master into it, review, resume (owner go). Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- Possible: S26-style check for ebilling 095a (`sprk_invoicesubmission.sprk_sourceemaildocument` lock) — would make that ordering rule mechanical.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured (ships in SpaarkeMaster 1.3.1.0 or via ebilling's script).
- No stamp gets a master BFF containing ebilling 116 / #2724 (on master since 2026-11-12 05:25Z) before SpaarkeMaster 1.3.1.0 is installed there: 1.3.1.0 adds `sprk_procedureversion.sprk_notconvertedlist`, which #2724 reads; without it the rule-set and procedure-version routes fail (ebilling-r1, 2026-11-12).
- SpaarkeMaster 1.3.1.0 (NOT 1.3.0.1; ebilling-r1 2026-11-12): carries 166 (what S26 checks), 095a/095c-1, DEF-061, DEF-059 Decision Record Reader role, uac-r2 193 `sprk_pairedsystemuser`, `sprk_notconvertedlist`. PUBLISHED 2026-11-13 03:22Z (owner go in ebilling-r1's session): run 43358397122 from master `d8b53ec0c3`; `dataverse-solutions-latest.json` = 1.3.1.0 (managed sha256 `4f7cda4c…32e6d4`), versioned manifest `dataverse-solutions-2026.11.13-318-1.json`; expectations `spaarkemaster-1.3.1.0.json`. Also carries field-mapping #2733 (secured message-access columns + BFF profile grants) and the #2725 profile-id fix. The #2724 gate is per stamp: 1.3.1.0 must be INSTALLED there (H6 on a new run), not just published. An environment that already has `sprk_invoicesubmission` rows runs `Deploy-EbillingSubmissionTrackingScopeSchema.ps1` steps 1–8 right after import; a new stamp needs nothing. ebilling will send the version + run when published.
- ACME BFF upgrade gated on A9 home sign-in; any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- **L2 Worker with H7b procedure version 10 (task 270, S27): ordering rule LIFTED 2026-11-20** — SpaarkeMaster 1.3.2.0 is published. Worker DEPLOYED 2026-11-20 (owner go). Now every existing stamp re-runs H7b (the version salts its key) and stops there (Resumable, S27) until a NEW run imports 1.3.2.0 at H6.
- Never remove System Administrator from the BFF identities.
- **Before ANY stamp's next SpaarkeMaster MANAGED UPGRADE past 1.3.2.1** (ebilling-r1 task 193, 2026-11-23): run `scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1` in report mode (no `-Apply`) against the stamp. The upgrade drops `sprk_ratecard.sprk_matter` / `sprk_project` / `sprk_workassignment` and any data in them, unguarded. Script on `work/ebilling-r1` `c21bde55a2` until their next PR reaches master.

## 📋 Remaining (detail in the archive file)

- Decision Record Reader role (ebilling DEF-059): H10 `AdditionalSecurityRoleNames` + strip + role-expectations; blocked on `task/ebilling-def059` reaching master; needs a task (owner go).
- field-mapping 054 (Direct unit): reserved-list + reader-profile membership when it ships.
- #2232 eBilling storage per stamp; #2584; #2594; PR #2536 review; T186 close-out; ACME `AiSearch__AllowedIndexes__N` live change (owner OK); builds D36, D37, T240c, T240d, A9 H3, 203d, wrap-up 090.

## 🐞 Open defects

#1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
