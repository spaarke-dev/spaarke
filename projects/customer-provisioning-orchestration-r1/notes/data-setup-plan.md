# Data setup for customer stamps — reference values and demo records

> **Status**: DRAFT plan for owner review, 2026-11-24. Nothing built yet.
> **Asked by**: owner, 2026-11-24 ("populate reference lookup tables with standard values … and a set of test records so
> the system can be demonstrated").
> **Next**: the full demo environment (files, AI content, billing, personas, volume) is planned in `notes/demo-environment-plan.md` (2026-11-26).
> **Related**: ISS-039 (#3075), L56 (`t186-acme-run-log.md`), ISS-037 (H12b workspaces), `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`.

## 1. What we found (read-only, 2026-11-24)

Row counts, spaarkedev1 (authoring) vs spaarke-acme (the demo stamp, provisioned by run `3b6f7b65`):

| Table | dev | ACME | What it is |
|---|---|---|---|
| `sprk_recordtype_ref` | 15 | 14 | product config — **seeded by H12b** |
| `sprk_eventtype_ref` | 17 | **0** | product config (field config JSON, side-pane form, colour, due-date rules); code hard-codes 12 dev row ids — **ISS-039** |
| `sprk_agreementtype` | 10 | **0** | product config (AI classification cues, thresholds) — ISS-039 |
| `sprk_triagecategory` | 10 | **0** | product config (classifier guidance) — ISS-039 |
| AI content (actions, tools, playbooks, nodes, consumers) | 80 actions / 51 tools / 34 playbooks | **0 playbooks** | product config — **L56**: H12a seeds only 4 of 12 artifacts; the repo holds a fraction of dev's set; code hard-codes the Document Profile playbook id |
| `sprk_mattertype_ref` | 5 | 0 | customer taxonomy |
| `sprk_mattersubtype_ref` | 0 | 0 | customer taxonomy |
| `sprk_practicearea_ref` | 6 | 0 | customer taxonomy (has a default billing-code set) |
| `sprk_projecttype_ref` | 2 | 0 | customer taxonomy |
| `sprk_documenttype_ref` | 6 | 0 | customer taxonomy |
| `sprk_contacttype_ref` / `sprk_organizationtype_ref` | 2 / 1 | 0 / 0 | customer taxonomy |
| `sprk_usertype_ref` / `sprk_accounttype_ref` / `sprk_countryregion_ref` | 0 / 0 / 0 | 0 | taxonomy, empty everywhere |
| `sprk_billingcode` / `sprk_billingcodeset` | 161 / 8 | 0 / 0 | standard UTBMS codes (eBilling) |
| matters / projects / events / documents / contacts / accounts | 208 / 34 / 129 / 687 / 27 / 3 | **all 0** | business data |

So ACME, our demo stamp, works for record types only: no event types, AI playbooks, taxonomy or records.

Existing tools (inventory 2026-11-24):

| Tool | Seeds | State |
|---|---|---|
| H12b `AppConfigSeed/` | record types, grid config, workspace layouts, field-mapping profiles, charts | works; create-if-missing; the pattern to extend |
| H12a `AiSeedChain/` + `scripts/seed-data/manifest.yaml` | AI type lookups, knowledge, skills, output types | 4 of 12 artifacts; L56 open |
| `scripts/ebilling/Seed-EbillingBillingCodes.ps1` + `scripts/seed-data/ebilling/utbms-*.csv` | billing codes, code sets | current (2026-11-14); operator script, not in provisioning |
| `scripts/Load-DemoSampleData.ps1` + `scripts/demo-data/demo-records.json` | contacts, matters, projects, events, documents (+8 sample files to SPE), charts | idempotent, `-EnvironmentUrl` required; **outdated**: records set only name/number/description — no matter type, practice area, event type or any other lookup; written for the retired `spaarke-demo` |
| `scripts/Migrate-DataverseData.ps1` | copies reference tables env→env | a migration tool; copying from dev is against the "build the process, not the environment" directive |
| spaarke-data CLI (`C:\code_files\SPAARKE-DATA-CLI`, "Meridian" scenario) | accounts, contacts, documents, events, budgets, billing events, communications | **stale** (2026-05-18): targets spaarkedev1 + the deleted `spe-api-dev-67e2xz`; some table names no longer exist (`sprk_practicearea`) |
| Self-service demo registration (BFF) | demo-user access | access only, no data |

No document lists the standard reference values or describes a demo-data step for a stamp.

## 2. Four kinds of data, four rules

| Tier | Content | Who decides the values | When it is written | Rule |
|---|---|---|---|---|
| **1. Product configuration** | event types, agreement types, triage categories, record types (done), AI content (L56), grid/workspace/field-mapping config (done) | Spaarke (it is part of the product; code depends on it) | **every provisioning run**, every stamp | Source-controlled files with **fixed primary keys** (code references ids). Create if missing; a row that differs from source is reported as drift, never overwritten (customers may tune thresholds). |
| **2. Starter taxonomy** | matter types, matter subtypes, practice areas, project types, document types, contact types, organization types, user types, account types, country/region | Spaarke ships a standard legal pack; the **customer owns it after** | once, at first provisioning, when the intake selects a pack | Source-controlled pack with stable codes and ids. Create if missing by code; never update or delete afterwards (the customer edits and deactivates). |
| **3. Standard code sets** | UTBMS billing codes and code sets; default code set on matter types and practice areas | industry standard (UTBMS) | with Tier 2 | Port the existing `Seed-EbillingBillingCodes.ps1` data; same rule as Tier 2. |
| **4. Demo records** | accounts, contacts, matters, projects, events, to-dos, documents with files, invoices/budgets, communications | Spaarke, for demonstrations | **only on a demo stamp**, by the operator, on request | Never on a production customer. One versioned dataset that uses the Tier 1–3 values; an idempotent load and a **reset** that removes only demo-tagged rows. |

Option-set (choice) values ship inside SpaarkeMaster and need no seeding.

## 3. Mechanism (recommendation)

- **Tiers 1–3 in the L2 control plane, not a script.** H12b already seeds record types create-if-missing from embedded
  content; extend that pattern into one primary-key-preserving reference seeder (`ReferenceDataSeeder`), fed by
  `scripts/seed-data/reference/*.json` embedded in the Worker like `manifest.yaml`. One writer, used by H12b (Tiers 1–3)
  and H12a (AI content, L56). Reason: "end state: provisioning runs E2E with no human interaction"; a stamp must work
  without an operator remembering a script.
- Why not a Dataverse package (Configuration Migration / Package Deployer)? It needs `pac` at provision time (the L2
  host has none) and a second packaging pipeline beside the CI-published SpaarkeMaster.
- **Content comes from dev once, by export, then lives in git.** An export script reads the current dev rows (with ids)
  into the JSON files; the owner reviews the diff; provisioning seeds from git only. Re-exports are deliberate PRs.
  This follows the owner's rule: build the process, not the environment (never copy from dev at provision time).
- **Guard (enforcement ladder):** an ArchTest fails when a Dataverse row id hard-coded in product code (pattern
  `xxxxxxxx-xxxx-f011-8406-…`) is not in a Tier 1 seed file. It catches the next hard-coded id at PR time.
- **Tier 4 is an operator script, not a handler**: refresh `Load-DemoSampleData.ps1` (it is idempotent and already
  uploads files to SPE through the BFF) rather than revive the spaarke-data CLI, which targets deleted hosts. The
  control plane never carries demo data, so a production run cannot load it by mistake.

## 4. Work breakdown

| # | Work | Size | Depends on | Live action |
|---|---|---|---|---|
| D1 | `Export-ReferenceData.ps1`: read Tier 1 tables (event types, agreement types, triage categories) from spaarkedev1 with ids into `scripts/seed-data/reference/`; strip environment-specific values (form ids are solution-stable — verify) | 0.5 d | — | read-only on dev |
| D2 | `ReferenceDataSeeder` in H12b (create-if-missing by id, drift report), tests; hard-coded-id ArchTest with a ratchet baseline | 1–1.5 d | D1 | — (closes ISS-039 in code) |
| D3 | L56: export AI content (actions + schemas, tools, playbooks with `sprk_playbookid` + canvas, nodes, N:N, consumers) with ids; H12a writes it through the D2 seeder in dependency order (tools → actions → playbooks → playbook N:N → nodes after H12c → node N:N → canvas → consumers) | 3–4 d | D2 | read-only on dev |
| D4 | Starter taxonomy pack `legal-standard` (draft in §6, owner approves); intake key `referenceDataPack` (`legal-standard` default, `none`); H12b seeds it once | 1 d + owner review | D2, owner decision B | — |
| D5 | UTBMS codes and code sets into the pack (port the CSVs + set-membership logic of `Seed-EbillingBillingCodes.ps1`); default code set on matter types / practice areas | 0.5–1 d | D4 | — |
| D6 | Demo dataset v2: rewrite `demo-records.json` to use the pack (every matter typed and classed by practice area, events typed, documents typed, contacts/orgs typed); add to-dos, invoices/budgets if eBilling demo is wanted; tag demo rows (e.g. a `[DEMO]` number prefix or a dedicated column) and add `-Reset`; point it at a stamp (`-CustomerId` → registry row → Dataverse + BFF URLs) | 1.5–2 d | D4 | — |
| D7 | Apply to ACME: an upgrade run (H12a/H12b re-run with the new content), then `Load-DemoSampleData.ps1 -CustomerId acme` | 0.5 d | D2–D6, merged + Worker deployed | **yes — owner OK per action** |
| D8 | Docs: deployment guide section "Reference and demo data" (the four tiers, the intake key, the demo load and reset); `scripts/seed-data/README.md` refreshed | 0.5 d | D7 | — |

Order: D1 → D2 (fixes ISS-039) → D4/D5 and D3 in parallel → D6 → D7 → D8. About 9–11 working days in total; D3 (AI
content) is the largest single piece.

## 5. Owner decisions needed

> **Decided 2026-11-25** (owner "proceed with the feed of test records"; the recommendations below were taken): A report only · B `legal-standard` by default, `none` opt-out · C the §6 draft as written · D operator script on demo stamps only · E refresh `demo-records.json` · F eBilling demo records deferred until D5. In flight: D1+D2 PR #3088 (review fixes pushed), D4 and D6 implementers, ISS-042 #3105 (event-type read Global).

| # | Question | Recommendation |
|---|---|---|
| A | Tier 1 drift: when a stamp's event type or triage category differs from source, report it or overwrite it? | **Report only.** Customers may tune classifier thresholds; overwriting would silently undo it. |
| B | Starter taxonomy: seed the standard legal pack on every new stamp by default, or only when the intake asks? | **Default on** (`referenceDataPack: legal-standard`), with `none` for a customer who brings its own taxonomy via data migration. |
| C | The pack's content (§6 draft). | Review and edit the lists; the dev values are included. |
| D | Demo data: operator script on demo stamps only (recommended), or a provisioning step behind an intake flag? | **Operator script.** Keeps demo rows out of the control plane entirely. |
| E | Demo dataset: refresh `demo-records.json` (recommended) or revive the spaarke-data CLI "Meridian" scenario? | **Refresh `demo-records.json`**; borrow Meridian's richer content (budgets, billing events, communications) where wanted. Retire or re-point the CLI separately. |
| F | Should the demo include eBilling (invoices, budgets, billing events)? | Yes if eBilling is part of the demo story; it needs D5 first. |

## 6. Draft `legal-standard` pack (for decision C)

Dev values are marked †. Codes are short, stable keys used for create-if-missing.

- **Matter types**: Litigation† · Commercial† · Employment† · Intellectual Property – Patent (Patent†) · Intellectual
  Property – Trademark (Trademark†) · Corporate / M&A · Regulatory & Compliance · Real Estate · Finance & Banking ·
  Privacy & Data Protection · Investigations · Tax · Bankruptcy & Restructuring · General Advisory
- **Matter subtypes** (by type; examples): Litigation → Commercial Dispute, Employment Dispute, IP Litigation, Class
  Action, Arbitration, Appeal · Commercial → Contract Drafting, Contract Review, Procurement, Licensing · Employment →
  Advice, Investigation, Separation · Patent → Prosecution, Opposition, Portfolio Management
- **Practice areas**: Appellate† · Intellectual Property Patents† · Intellectual Property Trademarks† · Mergers &
  Acquisitions† · Banking & Finance† · Commercial Transactions† · Litigation & Disputes · Employment & Labor · Real
  Estate · Regulatory · Privacy & Cybersecurity · Tax · Antitrust & Competition · Corporate Governance
- **Project types**: Litigation† · Intellectual Property – Patent† · Contract Lifecycle · Due Diligence · Regulatory
  Filing · Compliance Program · Policy Review · Internal Investigation
- **Document types**: Closing Statement† · Asset Purchase Agreement† · Patent Application† · Office Action† · Loan
  Agreement† · Non-Disclosure Agreement† · Contract · Amendment · Engagement Letter · Pleading · Motion · Brief ·
  Correspondence · Memorandum · Board Resolution · Invoice · Evidence / Exhibit · Court Order
- **Contact types**: Outside Counsel† · Attorney† · Client Contact · In-house Counsel · Paralegal · Expert Witness ·
  Opposing Counsel · Judge / Court Staff · Vendor Contact
- **Organization types**: Law Firm† · Client · Vendor · Court · Government Agency · Opposing Party · Expert Firm ·
  Insurer
- **User types / account types**: no dev values and no known consumer — confirm whether anything reads them before
  shipping values (otherwise leave empty).
- **Country / region**: ISO 3166-1 (249 rows) if anything reads it; no dev values today — confirm the consumer first.
