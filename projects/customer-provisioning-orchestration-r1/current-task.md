# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-15 (Desktop session `provisioning-r1`; status check + master review; branch fast-forwarded to master `ce6c241964`, BFF + L2 build clean).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress. Last completed: **269** (Set-CustomerOwnerTeams never treats "Spaarke Ontology Writer" as a customer unit; owner go 2026-11-13) — PR #2780 merged → `3ab55ee877` (2026-11-13). Before it: 268 → PR #2766 `4b45bdf3fe`. |
| **Next Action** | Owner decides: (a) new task for the #2799 invoice-family field lock on new stamps (see "Master review 2026-11-15"); (b) with owner go: H7c (#2429) — `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed): merge master into it (it also edits Set-CustomerOwnerTeams.ps1 — resolve against task 269), review, resume. |
| **Done 2026-11-11/13** | PR #2676 (task 267) → `df93d2029c`. ebilling #2689 → `2fc434c912`. Task 268 → PR #2766 `4b45bdf3fe`; #2725 / ISS-028 filed (19 operator scripts excuse System Administrator by name; ebilling fixing its 4 sites). SpaarkeMaster 1.3.1.0 published 2026-11-13. |
| **Quality alert** | #1821 mutation-nightly open (2026-11-15); not started. |
| **Working tree** | Clean after the close-out commit. |

## 🔔 Owner questions open

1. ACME migration (writer unit + S25 + now S26) through a NEW run, owner OK; first read ACME's DoNotRemoveRolesOnChangeBusinessUnit (task 267 K3).
2. Open from before: `version-compat-matrix.json` stale v1 cell; #2443/#2532 go-live gates (prod workforce client, CIAM BFF app T240d, routing T240c, GitHub env vars, A9 on ACME, DNS); L56 AI content for ACME; demo accounts; dev BFF secrets rotation; ISS-026.

## Follow-ups unblocked by ebilling #2689 (not started)

- Done: task 269 Set-CustomerOwnerTeams writer-unit exclusion (PR #2780 → `3ab55ee877`). ebilling-r1 merged it with their EB-D-220 change (cdde3d69b4): Pester 81/81.

## Master review 2026-11-15 (430 commits `9fef284de0`..`ce6c241964`)

- No change to infrastructure/Bicep, `Provision-*`, the L2 control plane, Key Vault / ADR-028 A4, or Entra registrations.
- **Gap (not tracked yet):** uac-r2 #2799 adds a per-environment field lock on 24 invoice-family lookups (`scripts/Set-InvoiceFamilyFieldSecurity.ps1`; guide §7.4). It is NOT in SpaarkeMaster 1.3.1.0 (`sprk_Invoice.sprk_Matter` IsSecured=0 in source), and neither L2 nor `/provision-environment` runs it, so a new stamp has the lock missing. Same for `Set-ThreadFlagFieldSecurity.ps1`, which the guide says "provisioning runs" but nothing does. H7b checks only S14/S26. Proposed: a task — H7b S-check (fails closed) + operator step in the skill, or the lock packaged in the next SpaarkeMaster (owner choice).
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
- Never remove System Administrator from the BFF identities.

## 📋 Remaining (detail in the archive file)

- Decision Record Reader role (ebilling DEF-059): H10 `AdditionalSecurityRoleNames` + strip + role-expectations; blocked on `task/ebilling-def059` reaching master; needs a task (owner go).
- field-mapping 054 (Direct unit): reserved-list + reader-profile membership when it ships.
- #2232 eBilling storage per stamp; #2584; #2594; PR #2536 review; T186 close-out; ACME `AiSearch__AllowedIndexes__N` live change (owner OK); builds D36, D37, T240c, T240d, A9 H3, 203d, wrap-up 090.
- Task 266 K2 to confirm on next live H7b run.

## 🐞 Open defects

#1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
