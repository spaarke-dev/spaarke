# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-07 (session b8). HEAD `77d468dcf7`, pushed.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **266** — per-stamp ontology writer identity (#2605). POML `tasks/266-stamp-ontology-writer-identity.poml` |
| **Status** | in-progress (started 2026-11-06). Rigor FULL, opus @ high, directional. |
| **Step** | Steps 1–5 DONE + pushed (`b514a9edca` bicep/H2a/H4b; `fefcfb09b8` H10 writer App User; `99fb858153` H7b S24 + v7; `77d468dcf7` H13 T7 writer setting + T3 writer no Graph role; 3307 tests). Now Step 6 — guide rows, ArchTests, reviews. |
| **Next Action** | Step 6: guide rows in `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (H2a writer UAMI, H4b setting, H10 writer App User, H7b S24 + 7 new `secure_setup.ontology_writer_*` codes, H13 T7/T3); `dotnet test tests/Spaarke.ArchTests` WITH build (reads customer.json); `/code-review` + `/adr-check`; adversarial-reviewer ×2 (auth). Step 7: TASK-INDEX ✅, POML notes, drift check, portfolio sync. Then ACME run-log + ontology reply. |
| **Files modified (this task)** | committed: customer.bicep/json, modules/app-service(-slot).bicep, manifest.yaml + generator + generated, ArmDeploymentRunner, BicepDeployOutputs, H2a, InterStepState (OntologyWriterClientId/ObjectId/SystemUserId), PerEnvSourceCatalog, HandlerRunInputs, IDataverseAppUserCreator (SecurityRoleName nullable), DataverseWebApiAppUserCreator, H10 handler + H10Rejections (+3 codes), tests (H2a, H4b, H10, creator). |
| **ACME (ontology applied 2026-11-06, owner go in their session)** | writer UAMI + both slots + app setting + app user in ACME Corporation + role + "Field Writers" profile; "Firm Reason Writers" waits for 1.3.0.0. Ids in their message — record in `notes/t186-acme-run-log.md` (no ids in commits beyond what the log already carries). Check read-only that the app user's azureactivedirectoryobjectid = writer principalId. |
| **Delegation** | Not delegated: the pieces are coupled through InterStepState / DAG / manifest; main session. |

**Task 266 decisions so far**: writer UAMI name `mi-spaarke-{customerId}-{env}-ontology-writer` (sent to ontology so their ACME hand-apply matches); H10 creates/adopts the writer app user in the customer BU and assigns it NO role; H7b S24 assigns role copy + exactly the two writer profiles (S23 BFF runtime user is an allowed role holder); ProcedureVersion → 7; H13 = config read-back (T7) + T3 Graph-role check, NOT a live Signal write (escalation trigger in the POML if ontology wants one).

**ACME facts (read-only, 2026-11-06)**: RG `rg-spaarke-acme-prod` (westus2), BFF `sprk-acme-prod-api` + slot `staging`, stamp UAMI `mi-spaarke-acme-prod`; Dataverse `spaarke-acme.crm.dynamics.com`; customer BU "ACME Corporation" `c3f22cfd-f6c8-f111-a05c-0022480b180a`; Ontology Service role in all 3 BUs; only "…Field Writers" writer profile present (SpaarkeMaster 1.2.3.1) — "…Firm Reason Writers" needs ≥ 1.2.4.0. Sent to ontology; they may hand-apply under the owner's go and send us the ids for the run log.

## 🔔 Owner questions open

1. **SpaarkeMaster 1.3.0.0** (merged #2595 → `5d3c0abac2`, NOT published): set spaarkedev1 to 1.3.0.0 → publish dry run → real → ACME 1.2.3.1→1.3.0.0 stage-and-upgrade → `Test-RolePrivilegeExpectations.ps1` with `spaarkemaster-1.3.0.0.json`. Option: wait for ebilling's 095a master PR (secures `sprk_invoicesubmission.sprk_sourceemaildocument`) and publish 1.3.0.0 from that commit.
2. Add `isuseraccessauditenabled` to the H6 org-settings contract (ACME off, dev on; recommend yes) + org-audit section in the role check.
3. #2443 read-only check of the production workforce + CIAM clients.
4. ACME application of #2605 (Azure + Dataverse), unless ontology does it under the owner's go.
5. Older: L56 AI content for ACME (H12a seeds 4 of 12 sets); demo accounts unclassified; dev BFF secrets printed locally (rotation); ISS-026 publisher verification.

## Branch state

- **On branch, not on master**: H7b S23 (`923990a791`, `ba484977b2`; procedure v6), `49ba9a70d3` (publish rollback pointer on same-commit re-run), task 266 POML. Goes to master with the next PR. ACME gets S23 only after a control-plane deploy (owner OK).
- Last master merge from here: PR #2438 → `d511a329` (2026-10-29).

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a before `sprk_invoicesubmission.sprk_sourceemaildocument` is secured (release or `scripts/ebilling/Set-EbillingSourceEmailFieldSecurity.ps1`).
- ACME BFF upgrade gated on A9 home sign-in (project CLAUDE.md §3); any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities (relayed standing instruction).
- Next stamp upgrade: run the role check (ontology) and confirm the businessunit→sprk_matter entity map is gone (field-mapping).

## 📋 Remaining (detail in the archive file)

- **H7c (#2429)**: partial work on `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed). Resume after ebilling's 089d option R reaches master (they send the SHA); merge master first. Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- **#2232**: dedicated eBilling storage account per stamp + `Finance__FileStore__*` settings + H13 checks; blocked on ebilling's module reaching master. EB-D-164: scanning required on prod stamps.
- **ebilling 166**: when on master, H7b check that `sprk_invoice.sprk_totalamount` / `sprk_approvedtotal` are secured and Writers has Read 4.
- **#2584**: `sprk_workassignmenttype` owner unclaimed. **#2594**: port the Pester 3.4 suites.
- **PR #2536** (skill restructure, tracking #2537): our review + dev dry run + D9–D15 decisions before merge; act on owner direction.
- **T186 close-out**: 6f Copilot package to the owner; carry open lessons into the guide/prereqs (run-log rows not `applied`); L15, L30; remove old broad L2 Graph roles; S18 columns.
- ACME live change still owed (owner OK): `AiSearch__AllowedIndexes__N` (fixed in code `8bef420b86`).
- Builds: D36, D37, T240c, T240d, A9 H3 changes, 203d, wrap-up 090. Repo cleanup of old-demo references.
- Report to owner: S1 plan cost; L49 ComposeIdentityKey DEGRADED on ACME BFF; task 264 vs 047 ordering; registry table in SpaarkeMaster.

## 🐞 Open defects

#2605 (task 266) · #1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
