# Plan: a complete demo environment with one repeatable process

> **Status**: DRAFT for owner review, 2026-11-26. Nothing built yet.
> **Asked by**: owner, 2026-11-26 ("we need a robust way to create a demo environment with full set of data").
> **Builds on**: `notes/data-setup-plan.md` (the four data tiers, D1–D8, owner decisions A–F of 2026-11-25).
> **Evidence**: ACME (`spaarke-acme`, SpaarkeMaster 1.3.3.0) after upgrade run `85561c4a` and the demo load of
> 2026-11-26; code read on master `af4cfb4527`.

## 1. Goal

One operator command turns a provisioned **Demo** stamp into a fully working demo. Every screen has data, documents
have real files with AI summaries and search hits, named personas can sign in, and the dates look current. The same
command resets and reloads. Nothing in it can run against a production customer.

## 2. Where we are

| Area | State | How |
|---|---|---|
| Product configuration: record types, event types, agreement types, triage categories, grids, layouts, field mapping, charts | **done**, every stamp | H12b, create-if-missing from git |
| Starter taxonomy (`legal-standard`: matter, project, document, contact and organization types, practice areas) | **done**, every stamp | H12b pack |
| Demo records: 15 organizations, 18 contacts, 10 matters, 9 projects, 29 events, 12 to-dos, 12 communications, 18 document rows | **done**, loaded on ACME 2026-11-26 | `scripts/Load-DemoSampleData.ps1` (Demo-only guard, tagged, idempotent, `-Reset`) |
| Secure records | **done**, ACME passes Step 6e | H7b |
| Document **files**, AI summaries, search index | **missing**: document rows are metadata only | — |
| AI content: actions, tools, playbooks, nodes | **missing**: H12a seeds 4 of 12 artifacts, 0 playbooks (L56) | — |
| Screen config that code finds by **dev id** (17 grid configs, 2 layouts, 1 playbook) | **missing**: External Access widgets fail closed on stamps (ISS-045 #3189) | — |
| UTBMS billing codes | **missing** on stamps (data plan D5) | operator script `Seed-EbillingBillingCodes.ps1` exists, no handler runs it |
| eBilling demo: invoices, budgets, billing events | **missing** (decision F deferred it) | — |
| Demo personas who can sign in | **partial**: two guests (jane.smith, john.james); guest Console sign-in never verified | H11 |
| Volume beyond the curated story | **none**: no generator exists. The separate `SPAARKE-DATA-CLI` repo stopped in May at phase 0 (empty loaders, deleted hosts) | — |
| Dates stay current | **no**: dates are offsets from the **load** day, so a demo loaded weeks ago looks stale | — |

## 3. Principles

- **Product data goes through provisioning; demo data goes through an operator script.** Product data is the tiers in
  `data-setup-plan.md` §2. Demo rows never pass through the control plane, so no production run can load them.
- **Use the product's own write paths for anything with server-side rules.** That means files, the document pipeline
  and the invoice family (`InvoiceFamilyCoreRecordGuard`). A loader that writes those tables directly would make rows
  the product never makes, and the demo would show behaviour no customer gets.
- **Same guard, same tag, same reset.** Every new section joins `Load-DemoSampleData.ps1`: the registry Demo guard, the
  `importsequencenumber` tag, children-first reset and the idempotent re-run.
- **Content lives in git** and is reviewed as a diff. It is never copied from dev at load time.

## 4. Workstreams

| # | Work | Why the demo needs it | Size | Depends on | Live action |
|---|---|---|---|---|---|
| W1 | **ISS-045**: seed the 17 grid configurations with their dev ids in H12b (fixed ids already supported), seed the 2 layouts with fixed ids or look them up by name, resolve the Document-email playbook by code, rebuild the PCF | External Access widgets and two workspace launches fail on every stamp today | 1–2 d | — | upgrade run (owner OK) |
| W2 | **AI content (L56 / data plan D3)**: export the shipped set from dev with primary keys (actions with schemas, tools, playbooks with canvas, nodes, N:N, consumers); a key-preserving writer in H12a in dependency order (tools → actions → playbooks → playbook N:N → nodes after H12c → node N:N → canvas → consumers) | Without it there is no AI analysis and no Document Profile summary, so W3's files would get no summaries | 3–4 d | owner content decision (which of dev's 34 playbooks ship) | upgrade run |
| W3 | **Document files**: a loader step that uploads each document's file through the BFF's record-file upload path, the one the Console uses (`PUT /api/obo/records/{entity}/{id}/files/…`), with a **user** token. The product pipeline then runs (`ProfileSummary`, `RagIndexing`). Write realistic `.docx`/`.pdf` content for all 18 documents, porting the "SAMPLE DATA" templates from `SPAARKE-DATA-CLI` phase 0 as content, not code. Reset deletes the files through the BFF as well. First confirm whether that call creates the `sprk_document` row itself or pairs with the Console's create call | Search, AI summaries, Find Similar, the document viewer and Compose all need real files | 2–3 d | **owner decision 1** (token), W2 | load on ACME (owner OK) |
| W4 | **UTBMS codes (data plan D5)**: move the `scripts/seed-data/ebilling/utbms-*.csv` codes and code sets into the `legal-standard` pack, seeded by H12b; set the default code set on matter types and practice areas; include DEF-005 #1929 | eBilling screens and invoice review need codes | 0.5–1 d | — | upgrade run |
| W5 | **eBilling demo**: budgets per matter, then invoices with lines that arrive through the product's own intake. Either submit demo invoice files through the finance file endpoint with W3's user token, or email them to the stamp mailbox so the inbound pipeline runs. The loader never writes `sprk_invoice` or `sprk_billingevent` directly | Spend, budgets and invoice review are part of the legal-ops story | 2–3 d | W3 (token), W4, **owner decision 3** | load (owner OK) |
| W6 | **Personas**: a fixed cast of 4–6 people (e.g. General Counsel, senior counsel, paralegal, legal ops / billing administrator, an outside-counsel contact). Licensed staff are invited by H11 from the intake; external contacts sign in through CIAM. The loader assigns owners, "assigned to" fields and to-dos to personas by key, so My Work, To Do and Daily Briefing show each person's own data. Verify a guest Console sign-in | A demo is shown *as someone*; today every row belongs to the ACME team | 1–1.5 d | **owner decision 4**; guest sign-in (blocked on ACME by the A9 home sign-in item) | H11 upgrade run + load |
| W7 | **Keep dates current**: a `-Redate` mode that shifts every tagged date by (today − load day), so a reset is not needed before a demo | A demo loaded last month shows overdue deadlines | 0.5 d | — | load |
| W8 | **Volume pack** (optional): a deterministic generator (fixed seed) that expands templates across the taxonomy into N extra matters, events, communications and document rows, tagged as a separate pack with the same loader and reset | Grids, charts, search and performance demos need more than 10 matters | 1.5–2 d | **owner decision 5** | load |
| W9 | **One command + verification + docs**: `New-DemoEnvironment.ps1 -CustomerId x` runs preflight → records → files → wait for the pipeline → billing → redate → verify. Preflight: registry Demo row, product config and AI content present. Verify: counts, every file has a summary, a search query returns hits, each persona reads their My Work. It writes a report under `runs/` and adds a "Demo environment" section to the deployment guide | One repeatable step instead of a checklist | 1 d | W1–W7 | — |

**Order**: W1, W4 and W7 now (small, independent) → W2 → W3 (after decision 1) → W6 → W5 → W8 if wanted → W9.
About **12–17 working days**. W2 and W3 are the largest pieces.

## 5. Owner decisions

| # | Question | Recommendation |
|---|---|---|
| 1 | **How does the loader get a user token for the stamp's BFF?** Files must go through the BFF as a user. The Azure CLI client is not pre-authorized on stamp BFF apps (AADSTS65001). | Pre-authorize one dedicated **public "Spaarke operator tools" client** on the BFF app registration of **Demo stamps only** (provisioning adds it when the registry type is Demo). The operator signs in with device code as a persona. Rejected: a new app-only "demo seed" endpoint in the BFF, because that is a privileged surface on every customer BFF, production included. Also rejected: writing files straight to SharePoint Embedded, which skips the document pipeline. **Security-sensitive: needs your explicit sign-off.** |
| 2 | Which AI content ships (L56)? | The set the product code calls today (PB-002 Document Profile, Summarize, the invoice-extraction and chat playbooks), plus the demo-story playbooks. Export from dev into git, with your review of the diff. |
| 3 | eBilling in the demo (decision F)? | Yes, after W4: legal spend is a core part of the story. |
| 4 | The persona cast: names, roles and who pays the licences. | 4 licensed personas as B2B guests from the `acmecorpdemo` tenant (the two that exist plus two), and 1 CIAM external contact. |
| 5 | Is the volume pack needed? | Not for the first full demo. Build it when a performance or search demo is scheduled. |
| 6 | `SPAARKE-DATA-CLI` | Retire it (archive the repo) and keep its scenario content as the source for W3 and W8 text. |

**Decided 2026-11-26 (owner):** decision 1 approved (operator client created, PR #3217, pre-authorized on ACME by run 7a7cbda1); decisions 2, 3, 4 and 5 adopted as recommended above ("Yes adopt the plan recommendations"). The owner signs in personas through the Claude desktop browser for W3/W6.

## 6. Until then (ACME today)

ACME already shows the curated story in every record grid. For a demo before W3, upload the 8 files in
`scripts/demo-data/sample-documents/` through the Console by hand. That runs the real pipeline, but there are no AI
summaries until W2.
