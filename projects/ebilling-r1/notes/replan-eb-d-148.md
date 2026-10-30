# Re-plan after EB-D-148 (2026-10-21)

> Source: `notes/procedure-and-file-model.md` **v5.9, FINAL** (EB-D-148). Section numbers below are that note's. Reviews: `notes/procedure-model-reviews.md`.
> Scope: rewrite open tasks 079–087, add the new tasks of model §13, amend open task 150. Completed tasks are not touched.

## Breakdown

| ID | Status | Title | Model §§ | Deps | External gates |
|---|---|---|---|---|---|
| 080 | rewrite | Procedure schema: `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`; `sprk_ruleset.sprk_scope` Record (100000002) / Firm record (100000003) + record lookups; `sprk_procedure` on rule set; `sprk_procedureversion` + published marker + `sprk_publishstate` on policy version; `sprk_procedureversion` on rate card; `sprk_billingfirm` / `sprk_billingresponsible` on every core table from `FilingCores` | 3, 6, 7.2, 13 (080 row) | none | ontology-r1 full schema review PASS; notices to ontology-r1 and uac-r2 before and after; `-WhatIf` first; owner OK for the dev run |
| 088 | new | Stored File and submission schema: `sprk_storedfile` (purposes, Firm-visible, servable status, source ids, deterministic id, field security, Deep, Restrict/RemoveLink); submission marker on `sprk_document` (field-secured to the BFF app user, `ParentLineage.RefuseAppOnlyWrite`, field-mapping skip); invoice status reason **Superseded** (reuses 104's `sprk_supersedesinvoice`) | 4, 9.1 (marker, correction), 12 | none | as 080 |
| 115 | new | AddedFee (100000015) and Discount (100000016) rule types: option values, `RuleDomains.DomainOf`, validator refuses AddedFee forms until extraction supports them, Discount is a check only, handlers | 7 (AddedFee, Discount) | none | ontology-r1 review of the option values |
| 091 | new | Storage provisioning: blob containers, container-scoped storage role (R1), network Allow until prod hardening (K-3), customer-provisioning hand-off | 4.2 | none | owner OK for any live Azure change; no Key Vault changes |
| 099 | new | Finance step 0: one route-group flag in `FinanceAuthorizationFilter` on every `/api/finance` invoice and submission route and `/api/finance/rulesets/*` | 5.3, 13 (step 0 row) | none | uac-r2 review (auth) |
| 081 | rewrite | Procedure and Procedure Version service: versions, I-P1–I-P5, copy-forward, publish, date changes, retire reconcile (switch policies Off → D-170 sweep), retire **confirmation preview** (same selection method as the sweep; count as the caller; label; fault = no count), billing-admin privilege, step 0 | 3, 8, 15 answers 1–2 | 080, 099 | uac-r2 review |
| 089 | new | Assignment and override service: pin, exclude, change rule into the record-scoped rule set; override removal with its confirmation preview; caller-checked (F1-c); secure child registration for assignment rows and record-scoped rule sets; census waiver and guard rewrite (must-fire / must-not-fire); K-2 pin test | 6, 15 answer 2 | 080, 081 | uac-r2 review (secure family) |
| 082 | rewrite | Resolver (7.1): firm condition, gaps and holds ("No procedure associated with this invoice"), re-keyed version selection (7.2), `BILL-SYSTEM-SUBMISSION` exemption, ratchet ArchTest | 7.1, 7.2 | 080, 081, 089 | — |
| 083 | rewrite | Firm clause via the resolver, firm-facing name | 7 | 082 | — |
| 084 | rewrite | Rule Review / Procedure UI: back-end read models of 6.6 now; UI waits for the prototype (O-14) | 6.6 | 081, 089, 082 | prototype (O-14) |
| 085 | rewrite | Rate-card tiers re-keyed to Procedure Version; old scope column and backfill dropped | 7.2, 12 (D-120) | 080, 082 | — |
| 086 | rewrite | Secure child registration for invoice Stored Files | 5, 13 (086 row) | 088, 090 | uac-r2 review |
| 087 | **cancelled** | Secure BU snapshot writer — dropped (BU filter dropped, O-12) | 12 | — | — |
| 090 | new | File store service: staged blob → Stored File row Not yet servable → If-Match flip to Servable with blob-exists + SHA check; deterministic id f(submission, source version); internal download route applying the copy rule (B) and refusing every `sprk_isexternal` caller; download audit event; orphan reconcile as an ADR-036 scheduled job; detector for rows not servable after N minutes | 4, 9.1 (steps 2, 4–6), 5 | 088 | uac-r2 review |
| 092 | new | Submission and processing on files: 044/049/051 upload target becomes the file store; extraction reads bytes from it; 108 viewer through the internal download route; 028 containers and keys retire from code (D-55, D-67) | 9.2, 12 | 090 | — |
| 098 | new | (a′) `ExternalMarker` predicate on `sprk_invoice` (D7 rules 2–4 + roots' Restricted state; skips only a submission-marked .eml narrowing), taint closure through `SecureChildLineage` edges, surfaces from R1 (SPA list and module, firm findings DTO, firm file route, external invoice-index retrieval, AI tools); internal file route and upload refuse flagged-external callers | 9.1 (a′), 5 | 088 | uac-r2 review (two adversarial passes: tenant isolation) |
| 093 | new | Firm file routes `GET /external/invoices/{id}/files` and `…/files/{fileId}/content`; retire the four `/external/invoices/{id}/documents…` routes (`ExternalProjectDataEndpoints.cs:378-412`) in the same change; task 188 guard entry | 9.3 | 090, 098 | uac-r2 review; R3 deploy pairing (BFF then SPA) |
| 094 | new | Submit Invoice (communication UI, source document exists): all or nothing steps 1–6, file first (A), copy rule (B), replaces 050a's re-file | 9.1 | 086, 090, 098 | uac-r2 review |
| 095 | new | Submit Invoice from the Outlook add-in: `POST /api/finance/invoice-submissions/from-email`; marks; bytes-present check (422); email-only save (`SelectedAttachmentFileNames = []` overwritten); .eml resolution (isemailarchive → target core record, no parent → caller's own on duplicate → SHA); own-Restricted .eml + submission marker in the same create (ArchTest, no update payload); retryable vs permanent refusals; idempotency key; filters | 9.1 | 094, 088 | #2177 merged and deployed; uac-r2 review; word-add-in-r1 contract |
| 096 | new | Remove the server legs for Invoice as an Office save target (`OfficeDocumentPersistence.cs:47`) and invoice quick-create (`QuickCreateRequest.cs:180`) | 9.1 | 095 | word-add-in-r1 removes the add-in side first; server legs one release later |
| 097 | new | Corrected invoice: resubmission via 104's `sprk_supersedesinvoice`; old invoice closes Superseded inside step 4; Approved / Sent to AP / Paid refusal re-checked in step 4 with If-Match; Write on the old invoice, same `sprk_vendororg` and core record; undo reopens; old invoice's open Signals closed Superseded via `SignalWriter.CloseAsync` after step 5, durable and resumable | 9.1 (Existing invoice) | 094, 104, 088 | ontology-r1 review of any per-invoice close wrapper |
| 113 | new | Read-time banner on `/review` only: neutral text; app-only comparison; "Not yet checked"; internal callers only | 8.2, 15 answer 6 | 082, 100 | — |
| 114 | new | Override releases a gap hold: recorded human Override (D-24 / D-39) with a reason; Decision Record through `WriteBillingDecisionAsync`; that invoice only | 15 answer 3 | 082, 105 | — |
| 150 | amend | AP export batch writes its files as Stored Files, internal-only (`ExternalMarker` Never) | 10, 15 answer 4 | + 090 | — |
| 079 | rewrite | Deploy WP3 to dev: 080 + 088 + 115 schema, storage (091), BFF | — | 053, 080–083, 085, 086, 088–094, 098, 099, 115 | owner OK for each live step; Import-SolutionScoped.ps1 only; dev BFF deploys via SPA-r3 |
| 189 | new | Docs: "Procedure Library" retired; data-model docs for the new tables; file-store architecture note | 3.4, 12 | 079 | — |

## Parallel waves (proposal)

- W3-A (parallel): 080, 088, 115, 091, 099 — no shared files.
- W3-B: 081 (after 080, 099); 090 (after 088) in parallel.
- W3-C: 089 (after 081); 092, 098 in parallel (after 090 / 088).
- W3-D: 082 (after 089); 086 (after 088, 090); 093 (after 090, 098).
- W3-E: 083, 085, 084 (back end) after 082; 094 after 086, 090, 098.
- W3-F: 095 → 096; 097 (after 094, 104); 113 (after 082, 100); 114 (after 082, 105); 150 amend.
- W3-G: 079 deploy; then 189.
