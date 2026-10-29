# TASK-INDEX — ebilling-r1

> **Last Updated**: 2026-10-21 (re-plan after EB-D-148: 079–086 rewritten, 087 cancelled, 088–099, 113–117 and 189 added, 150 amended; brief `../notes/replan-eb-d-148.md`)
> **Task count**: 108 · **Spec**: [`../spec.md`](../spec.md) · **Plan**: [`../plan.md`](../plan.md) · **Decomposition brief**: [`../notes/drafts/task-decomposition.md`](../notes/drafts/task-decomposition.md)
> **Legend**: 🔲 [open] not started · 🟡 [wip] in progress · ⏸ [wip] waiting on an owner decision · ✅ [done] completed · 🔄 needs retry · ⏭️ [deferred]. The bracketed ASCII token is what `scripts/check-task-status-drift.ps1` reads (`[blocked]` there means blocked-shipped, a closed state, so it is not used for open blocks).

## Quick Recovery

To begin: `work on task 001`. Wave W0-1 (001, 002, 003) and W2-1 (040, 041) have no dependencies and may run in parallel. Ontology #1111 merged to master 2026-10-15 (`f292ad119`) and is merged into this branch.

## Task Registry

### Phase 0 — Baseline and AI foundations (WP0)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 001 | ✅ [done] | Baseline the existing Finance pipeline and dev rollups | STANDARD | sonnet / high | P0-A | none |
| 002 | ✅ [done] | Assemble the evaluation set and manifest | MINIMAL | sonnet / high | P0-A | none |
| 003 | ✅ [done] | Deploy dev model deployments and Content Understanding defaults | STANDARD | sonnet / high | P0-A | none |
| 004 | ✅ [done] | Content Understanding analyzer definition v1 and deploy script | STANDARD | sonnet / high | P0-B | 003 |
| 005 | ✅ [done] | Extraction spike — four arms, coding model comparison, package size; go / no-go | STANDARD | opus / high | none | 002, 004 |

### Phase 1 — Data model (WP1)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 010 | ✅ [done] | Invoice Line columns on `sprk_billingevent` | STANDARD | sonnet / high | P1-A | none |
| 011 | ✅ [done] | Retire `sprk_invoicelineitem` (owner OK before delete) | STANDARD | sonnet / high | P1-B (p-safe false) | 010, 017 |
| 012 | ✅ [done] | Invoice header schema: core-record fields, totals, supersedes, lifecycle statuscodes | STANDARD | sonnet / high | P1-A | none |
| 013 | ✅ [done] | Core-ancestor derivation and one-lookup guard for the invoice family | FULL | opus / high | none (p-safe false: uac-r2 security files, census test shared with 011) | 010, 012, 020, 021, 022, 023, 027 |
| 014 | ✅ [done] | Invoice lifecycle transition service | FULL | sonnet / high | P1-B | 012 |
| 015 | ✅ [done] | Retire `sprk_invoicestatus` — data, readers, forms, views, docs; notify ontology-r1 | FULL | opus / high | none (main-session) | 014, 029 |
| 016 | ✅ [done] | Invoice totals calculator (I-1) | STANDARD | sonnet / high | P1-B | 010, 012 |
| 017 | ✅ [done] | Timekeeper (extend stub) and rate card schema | STANDARD | sonnet / high | P1-A | none |
| 018 | ✅ [done] | UTBMS code sets: schema, mapping lookups, seed files, seed script | STANDARD | sonnet / high | P1-A | none |
| 019 | ✅ [done] | Policy store extensions, `sprk_ruleset`, visible-to-firms flag | STANDARD | sonnet / high | P1-A | none |
| 020 | ✅ [done] | `sprk_invoicesubmission` schema | STANDARD | sonnet / high | P1-A | none |
| 021 | ✅ [done] | `sprk_invoiceapproval` schema | STANDARD | sonnet / high | P1-A | none |
| 022 | ✅ [done] | `sprk_externalidentifier` schema and firm-scoped key | STANDARD | sonnet / high | P1-A | none |
| 023 | ✅ [done] | `sprk_billingexport` schema and invoice batch lookup | STANDARD | sonnet / high | P1-A | none |
| 024 | ✅ [done] | `sprk_billingapproverrole` into source; firm billing contact | STANDARD | sonnet / high | P1-A | none |
| 025 | ✅ [done] | eBilling settings table and BFF settings reader | STANDARD | sonnet / high | P1-A | none |
| 026 | ✅ [done] | Signal / Decision Record invoice-line subject and derivation | FULL | opus / high | none (p-safe false) | 010, 013, 029 |
| 027 | ✅ [done] | Correction record schema | STANDARD | sonnet / high | P1-A | none |
| 028 | ✅ [done] | Containers: config keys, internal-only access, non-indexed exports, provisioning hand-off | STANDARD | sonnet / high | P1-A | none |
| 029 | ✅ [done] | Apply WP1 schema to dev; sync SpaarkeMaster source; forms and views | STANDARD | sonnet / high | none (deploy) | 010, 011, 012, 017, 018, 019, 020, 021, 022, 023, 024, 025, 027, 028 |

### Phase 2 — Submission and extraction (WP2)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 040 | ✅ [done] | eBilling feature flag, options and null-object fallbacks | STANDARD | sonnet / high | P2-A | none |
| 041 | ✅ [done] | Reshape `IInvoiceAi` to capability methods; consumer-type keys | FULL | opus / high | P2-A | none |
| 042 | ✅ [done] | Migrate attachment classification to `INVOICE-CLASSIFY@v1` | STANDARD | sonnet / high | P2-B | 041 |
| 043 | ✅ [done] | Content Understanding client and `ReadInvoiceAsync` | FULL | opus / high | P2-B | 041, 005 |
| 044 | ✅ [done] | Submission operation and endpoints (SPA + internal) | FULL | opus / high | P2-C | 013, 040, 045, 029 |
| 045 | ✅ [done] | Concurrency-safe tracking numbers `{invoice}.{n}` | FULL | sonnet / xhigh | P2-B | 029 |
| 046 | ✅ [done] | Resolution and combined confidence | STANDARD | sonnet / xhigh | P2-B | 025, 029 |
| 047 | ✅ [done] | UTBMS coding Action `INVOICE-LINE-CODE@v1` and code-set resolution | STANDARD | sonnet / xhigh | P2-B | 041, 018, 025, 029 |
| 048 | ✅ [done] | AI code-set mapping proposals (admin-confirmed) | STANDARD | sonnet / high | P2-C | 047 |
| 049 | ✅ [done] | `InvoiceSubmissionProcessing` job | FULL | opus / high | none | 014, 016, 043, 044, 046, 047, 072, 073 |
| 050a | 🟡 [wip] | Redirect the review-confirm route into the submission operation (D-134) | FULL | opus / high | none | 042, 049 |
| 050b | 🔲 [open] | Retire the legacy extraction path (deletion) | STANDARD | sonnet / high | none | 042, 049, 050a |
| 051 | ✅ [done] | SPA submission UI (shared wizard component) | STANDARD | sonnet / high | P2-D | 044 |
| 052 | 🔲 [open] | `CreateInvoiceWizard` rewired to the internal entry point | STANDARD | sonnet / high | P4-C | 044, 051, 105 |
| 053 | 🔲 [open] | Deploy WP2 to dev | STANDARD | sonnet / high | none (deploy) | 042, 043, 044, 045, 046, 047, 048, 049, 050a, 050b, 051, 054 |
| 054 | ✅ [done] | Code-set matching: tuning data, full coverage and an AI review step (D-93) | FULL | opus / high | P2-D | 047, 048 |

### Phase 3 — Rules (WP3)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 070 | ✅ [done] | Billing rule-type catalogue: schemas, describer, validation, fail-closed refusal | FULL | opus / high | none | 019, 029 |
| 071 | ✅ [done] | Rule handlers for the 11 billing rule types | FULL | sonnet / xhigh | P3-B | 070 |
| 072 | ✅ [done] | `InvoiceComplianceEvaluator` | FULL | opus / high | P3-C | 071, 017, 018 |
| 073 | ✅ [done] | Rejection rule and reject-eligibility at publish | FULL | sonnet / xhigh | none | 072, 025 |
| 074 | ✅ [done] | Billing actions in `DecisionActionCatalog` | FULL | opus / high | P3-B | 070 |
| 075 | ✅ [done] | Rule drafting Action `BILLING-RULE-DRAFT@v1` and job | STANDARD | sonnet / high | P3-B | 041, 070 |
| 076 | ✅ [done] | Rule set endpoints: proposals, edit, publish, visibility | FULL | sonnet / high | P3-B | 019, 070 |
| 077 | ✅ [done] | Rule Review code page | STANDARD | sonnet / high | P3-C | 076 |
| 078 | ✅ [done] | Rate card and timekeeper import and publish | STANDARD | sonnet / high | P2-B | 017, 028, 029 |
| 079 | 🔲 [open] | Deploy WP3 to dev: Procedure and Stored File schema, rule types, storage settings, solution sync, BFF and SPA via SPA-r3 | STANDARD | sonnet / high | none (deploy) | 053, 080–083, 085, 086, 088–094, 098, 099, 115, 116, 117, 124 |
| 080 | ✅ [done] | Procedure schema: Procedure, Procedure Version, assignments; rule-set record scope; policy-version and rate-card links | STANDARD | sonnet / high | P3-A | none |
| 081 | ✅ [done] | Procedure and Procedure Version service: I-P1 to I-P5, copy-forward, publish, dates, retire with a caller-scoped preview | FULL | opus / high | P3-B (p-safe false) | 080, 088, 099 |
| 082 | 🔲 [open] | Procedure resolver (7.1): firm condition, gaps and holds, re-keyed version selection (7.2), snapshot, ratchet ArchTest | FULL | opus / high | P3-D | 080, 081, 089 |
| 083 | 🔲 [open] | Firm clause: GetFirmClauseAsync re-keyed to the invoice's snapshot, firm-facing name; 130-135 POML constraint lines | FULL | sonnet / high | P3-E | 082 |
| 084 | 🔲 [open] | Procedures and rules-in-force on the record: back-end read models of 6.6 now; UI after the prototype (O-14) | FULL | sonnet / high | P3-E | 081, 089, 082 |
| 085 | 🔲 [open] | Rate cards keyed to Procedure Version: import, copy-forward, resolver tiers re-keyed to the rank; no scope column | FULL | sonnet / xhigh | P3-E | 080, 082 |
| 086 | 🔲 [open] | Invoice Stored Files join the secure family as a leaf child of sprk_invoice: lineage, role row, creator person, census | FULL | opus / high | P3-D (p-safe false) | 088, 090, 089 |
| 087 | ✖ [cancelled] | Secure BU snapshot writer — cancelled 2026-10-21 by EB-D-148 (BU filter dropped, model §12 / O-12) | — | — | — | — |
| 088 | ✅ [done] | Stored File schema, the sprk_document submission marker (field-secured, app-only refusal) and invoice status Superseded | FULL | opus / high | P3-A | 080 |
| 089 | 🔲 [open] | Assignment and override service (pin, exclude, change rule, removal preview); secure-child registration; K-2 pin test | FULL | opus / high | P3-C (p-safe false) | 080, 081 |
| 090 | ✅ [done] | File store service: staged blob, Not yet servable row, verified flip; internal download route; orphan reconcile job | FULL | opus / high | P3-B | 088, 099 |
| 091 | ✅ [done] | File store provisioning: blob containers, container-scoped role, malware-scan setting, customer-provisioning hand-off | FULL | opus / high | P3-A | none |
| 092 | 🔲 [open] | Submission on the file store: uploads stage Stored Files, extraction reads the blob, review gets file id, 028 retires | FULL | opus / high | P3-C | 090 |
| 093 | 🔲 [open] | Firm file routes GET /api/v1/external/invoices/{id}/files and …/content; the four invoice-document routes retire | FULL | opus / high | P3-D (p-safe false) | 090, 098 |
| 094 | ✅ [done; on master eaa0d44f7f (#1911)] | Submit Invoice from a source Document: all-or-nothing steps 1-6, file first (A), copy rule (B), 050a re-file retired | FULL | opus / high | P3-E | 086, 090, 092, 098 |
| 095 | ✅ [done; on master eaa0d44f7f (#1911); dev after the BFF deploy + EB-D-173] | Submit Invoice from the Outlook add-in: POST /api/finance/invoice-submissions/from-email (email-only save + submission) | FULL | opus / high | P3-F (p-safe false) | 094, 088 |
| 096 | 🔲 [open] | Remove the Office server legs for Invoice as a save target and invoice quick-create | FULL | sonnet / high | P3-F | 095 |
| 097 | 🔲 [open] | Corrected invoice: the old invoice closes Superseded in step 4 (guarded) and its open Signals close Superseded | FULL | opus / high | P3-F (p-safe false) | 094, 104, 088 |
| 098 | ✅ [merged 8b1aa1fa58; uac READY round 5] | (a′) on sprk_invoice: hide invoices with an isolated Submit Invoice source, and derived rows, from external callers | FULL | opus / high | P3-C (p-safe false) | 088, 090 |
| 099 | ✅ [done] | Finance step 0: refuse flagged-external callers on every /api/finance route (5.3; owner 2026-10-21) | FULL | opus / high | P3-A | none |
| 113 | 🔲 [open] | Read-time Procedure-change banner on /review: app-only comparison with the snapshot, internal callers only | FULL | sonnet / high | P3-F | 082, 100 |
| 114 | 🔲 [open] | Override releases a no-version-in-force gap hold for that invoice only, recorded through WriteBillingDecisionAsync | FULL | sonnet / high | P3-F | 082, 105 |
| 115 | 🟡 [wip] | AddedFee (100000015) and Discount (100000016) billing rule types: options, domain, schemas, validator refusals, handlers | FULL | opus / high | P3-A | none |
| 116 | 🔲 [open] | Procedure Version documents: drafting and readers keyed per version on its Stored File; sprk_sourcedocument retired | FULL | opus / high | P3-F (p-safe false) | 081, 085, 090 |
| 117 | 🔲 [open] | Core-record delete and Procedures: retire sweep for unlinked record-scoped rule sets; delete lists agreed with uac-r2 | FULL | opus / high | P3-D (p-safe false) | 089 |
| 118 | 🔲 [open] | Submit Invoice in the email UI: toolbar button (replaces Link Invoice), flagged attachments, Email Review Invoice tab | FULL | sonnet / high | P3-F | 094, 118a |
| 118a | ✅ [done] | InvoiceAttachmentMarking shared component, split out of 118 for word-add-in-r1 (EB-D-159) | FULL | sonnet / high | P3-F | none |
| 119 | ✅ [done] | Read-time Duplicate withholding on /review: hide an earlier invoice the reader cannot read (D-176, #2118) | FULL | opus / high | P4-A | 100 |
| 120 | ✅ [done] | Spaarke Billing Administrator role; D-185: Basic/Core User lose prvReadsprk_ruleset; guard test | FULL | opus / high | P3-R (p-safe false) | 081 |
| 121 | 🔲 [open] | Procedure-table role grants after uac-r2 191: O-6 org Read, Billing Administrator Create/Write on sprk_procedure; flip UntilTask191 | FULL | opus / high | P3-R (p-safe false) | 120 |
| 122 | ✅ | #2235 fix: field-secure sprk_findingsjson (BFF-only), off the form; Decision Records store the generic Duplicate sentence | FULL | opus / high | P4-A | 119 |
| 123 | 🔲 [open] | Internal upload of supporting files to an existing invoice: POST /api/finance/invoices/{invoiceId}/files (EB-D-161) | FULL | opus / high | P3-F | 090, 092, 086 |
| 124 | 🔲 [open] | Retire the Procedure Library container: remove Finance:ProcedureLibraryContainerId, close the per-customer SPE hand-off DEF-013 / #1967 (EB-D-163) | FULL | sonnet / high | P3-F | 116, 092 |

### Phase 4 — Review and approval (WP4)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 100 | 🟡 [wip] | Review read model and line decision endpoints | FULL | sonnet / high | P4-A | 014, 016, 049 |
| 101 | 🟡 [wip] Part A + EB-D-175/176 + B1 on task/ebill-101-corrections; B2 running | Value / code corrections, structured capture, re-evaluation | FULL | sonnet / xhigh | P4-B | 100, 027, 072 |
| 102 | ✅ [done; on master eaa0d44f7f (#1911)] | Invoice-level adjustments | STANDARD | sonnet / high | P4-B | 100 |
| 103 | ✅ [done; on master eaa0d44f7f (#1911)] | Approval: eligible approvers, worklist, approve | FULL | opus / high | P4-B | 100, 021, 024, 025, 074 |
| 104 | ✅ [done; on work a4e0721bdc, adversarial F1-a/F1-b fixed] | Reject and Return to Firm; resubmission linkage | FULL | sonnet / high | P4-C | 103, 045 |
| 105 | 🟡 [wip] | Override (server) | FULL | sonnet / high | P4-B | 044, 049, 074 |
| 106 | ✅ [done] | Context panel API | STANDARD | sonnet / high | P4-A | 029, 040 |
| 107 | ✅ [done] | Related work product API | STANDARD | sonnet / high | P4-A | 029, 040 |
| 108 | ✅ [done] | Bounding-box document viewer (shared component) | STANDARD | sonnet / high | P4-A | none |
| 109 | ✅ [done] | ConsoleKit additions: outcome cards, count filters, disclosure, status labels | STANDARD | sonnet / high | P4-A | none |
| 110 | 🔲 [open] | Invoice Review code page — lines, review pane, corrections, bulk, by-rule | STANDARD | opus / high | P4-C | 100, 101, 102, 108, 109 |
| 111 | 🔲 [open] | Invoice decision wizard and context / related / history panes | STANDARD | sonnet / high | none | 110, 103, 104, 105, 106, 107 |
| 112 | 🔲 [open] | Deploy WP4 to dev | STANDARD | sonnet / high | none (deploy) | 052, 100, 101, 102, 103, 104, 105, 106, 107, 110, 111, 079, 095, 096, 097, 113, 114, 118, 119 |

### Phase 5 — Firm feedback (WP5)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 130 | 🔲 [open] | Firm-visible findings projection (clause quotes, visibility filtering) | FULL | sonnet / high | none | 019, 049, 100, 083 |
| 131 | 🔲 [open] | Firm notice Action `INVOICE-FIRM-NOTICE@v1` with template fallback | STANDARD | sonnet / high | P5-B | 041, 130 |
| 132 | 🔲 [open] | Notifications: email, SPA feed, approver in-app | STANDARD | sonnet / high | none | 131, 133, 024, 103, 104 |
| 133 | 🔲 [open] | Adjustment report PDF | STANDARD | sonnet / high | P5-B | 130, 028 |
| 134 | 🔲 [open] | SPA invoice detail and on-screen report | STANDARD | sonnet / high | P5-B | 130 |
| 135 | 🔲 [open] | SPA Billing Guidelines view | STANDARD | sonnet / high | P5-B | 076, 130 |
| 136 | 🔲 [open] | Deploy WP5 to dev | STANDARD | sonnet / high | none (deploy) | 130, 131, 132, 133, 134, 135, 112 |

### Phase 6 — Export and index (WP6)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 150 | 🔲 [open] | AP export batch (CSV + JSON, D-56 default mapping, snapshot); files as internal-only Stored Files (EB-D-148) | FULL | sonnet / high | P6-A | 014, 023, 025, 028, 103, 090 |
| 151 | 🔲 [open] | Mark Paid and export UI | STANDARD | sonnet / high | none | 150 |
| 152 | 🔲 [open] | Invoice index from structured records | STANDARD | sonnet / high | P6-A | 049, 103 |
| 153 | 🔲 [open] | Deploy WP6 to dev | STANDARD | sonnet / high | none (deploy) | 150, 151, 152, 136 |

### Phase 7 — Evaluation and rollout (WP7) + wrap-up

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 160 | 🔲 [open] | End-to-end integration and Action seam tests | FULL | sonnet / high | P7-A | 079, 112, 136, 153 |
| 161 | 🔲 [open] | Calibration report from corrections and the evaluation set | STANDARD | sonnet / high | P7-A | 002, 049, 101 |
| 162 | 🔲 [open] | Accuracy and performance evaluation; tuning | STANDARD | opus / high | none | 005, 160, 161 |
| 163 | 🔲 [open] | UAT with the owner and dev rollout | STANDARD | sonnet / high | none | 162 |
| 189 | 🔲 [open] | Docs: retire "Procedure Library"; data-model docs for the Procedure and Stored File tables; file-store architecture note | STANDARD | sonnet / high | none | 079 |
| 190 | 🔲 [open] | Project wrap-up | FULL | sonnet / high | none | all |

## Parallel Execution Plan

Run at most 4 concurrent task agents (session cap). Within a wave, tasks have no dependency on each other and no file overlap except the shared append points listed in the brief (`FinanceModule.cs` DI registrations, endpoint-mapping call site, `appsettings*.json`, `docs/data-model/*.md` per-entity sections) — executors re-read those files immediately before editing.

```
Phase 0
  W0-1 (parallel, 3): 001, 002, 003                                  — goal-eligible: NO (003 live Azure deploy)
  W0-2 (single):      004 — prereq 003                               — goal-eligible: NO (deploy; single task)
  W0-3 (single):      005 — prereq 002, 004; ends with owner go/no-go — goal-eligible: NO (exploratory; owner decision)

Phase 1
  W1-1 (parallel, 13; run in batches of 4): 010, 012, 017, 018, 019, 020, 021, 022, 023, 024, 025, 027, 028
                                                                     — goal-eligible: NO (live dev schema changes)
  W1-2 (parallel, 3): 014, 016, then 011* and 013* serially — prereq per deps — goal-eligible: NO (011 destructive; 013 security files)
                      *011 p-safe false (owner OK before delete); 013 p-safe false (uac-r2 security files; shares census test with 011)
  W1-3 (single):      029 — all schema tasks                         — goal-eligible: NO (deploy)
  W1-4 (sequential, main-session): 015, 026 — prereq 029             — goal-eligible: NO (.claude/ edit; ontology-owned files)

Phase 2 (W2-1 may start at once, alongside Phase 0/1)
  W2-1 (parallel, 2): 040, 041                                       — goal-eligible: NO (two tasks)
  W2-2 (parallel, 6; batches of 4): 042, 043, 045, 046, 047, 078 — prereq 041 / 005 / 029 / 025 / 018 / 017
                                                                     — goal-eligible: NO (043 new package + go/no-go dependency)
  W2-3 (parallel, 2): 044, 048                                       — goal-eligible: NO (auth)
  W2-4 (single):      051 — prereq 044                               — goal-eligible: NO
  W2-5 (single):      049 — prereq 014, 016, 043, 044, 046, 047, 072, 073 (joins Phase 3)
  W2-6 (sequential): 050a then 050b — prereq 042, 049 (050b also 050a; split by D-134)
  W2-7 (single):      053 deploy WP2                                 — goal-eligible: NO (deploy)

Phase 3
  W3-1 (single):      070 — prereq 019, 029                          — goal-eligible: NO (security-class fail-closed)
  W3-2 (parallel, 4): 071, 074, 075, 076 — prereq 070                — goal-eligible: NO (074 open owner question F-9)
  W3-3 (parallel, 2): 072, 077
  W3-4 (single):      073
  --- Procedure and file model (EB-D-148, 2026-10-21). Waves are authoritative; the POML <parallel-group> labels are the brief's. ---
  W3-6 (parallel, 4): 080, 091, 099, 115                              — goal-eligible: NO (live schema / Azure / auth; owner and peer gates)
  W3-7 (single):      088 — prereq 080                                 — goal-eligible: NO (live schema)
  W3-8 (parallel, 2): 081, 090 — prereq 080, 088, 099                  — goal-eligible: NO (auth)
  W3-9 (parallel, 3): 089, 092, 098 — prereq 081 / 090 / 088+090; 089 and 098 p-safe false (uac-r2 files), never together with 086 or 117
                                                                       — goal-eligible: NO (tenant isolation)
  W3-10 (parallel, 2): 082, 093 — prereq 089 / 090+098 (093 also uac-r2 188 merged) — goal-eligible: NO
  W3-10b (sequential): 086 then 117 — prereq 089 (086 also 088, 090); uac-r2 files, shared census — goal-eligible: NO (security)
  W3-11 (parallel, 4): 083, 084 (back end), 085, 094 — prereq 082 / 082 / 082 / 086+092+098 — goal-eligible: NO (094 isolation)
  W3-12 (parallel, 4): 116, 095, 113, 114 — prereq 085 / 094 / 082+100 / 082+105; 116 and 095 p-safe false (no shared files with each other)
                                                                       — goal-eligible: NO
  W3-12b (single):    118 email UI + shared marking component — prereq 094 — goal-eligible: NO
  W3-13 (single):     097 — prereq 094, 104; after 095 (shared InvoiceSubmission files) — goal-eligible: NO
  W3-14 (single):     096 — prereq 095 + word-add-in-r1 release gate   — goal-eligible: NO
  W3-5 (single):      079 deploy WP3 — prereq per its row               — goal-eligible: NO (deploy)
  W3-15 (single):     189 docs — prereq 079

Phase 4 (108, 109 have no deps and may start any time)
  W4-1 (parallel, 5; batches of 4): 100, 106, 107, 108, 109          — goal-eligible: NO (100 authorization)
  W4-2 (parallel, 4): 101, 102, 103, 105                             — goal-eligible: NO (auth / privilege guards)
  W4-3 (parallel, 3): 104, 110, 052                                  — goal-eligible: NO (auth in 104)
  W4-4 (single):      111
  W4-5 (single):      112 deploy WP4                                 — goal-eligible: NO (deploy)

Phase 5
  W5-1 (single):      130                                            — goal-eligible: NO (CIAM firm-scope guard)
  W5-2 (parallel, 4): 131, 133, 134, 135 — prereq 130                — goal-eligible: YES
      goal-condition: "All of the following hold in this session: (1) Every task in Wave W5-2 (131, 133, 134, 135) shows its acceptance criteria passing via transcript output — dotnet test for the BFF test projects exits 0 and the external SPA / shared UI test and build commands named in each POML pass; (2) each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings surfaced in the transcript; (3) git status shows only the wave's expected file changes. OR: a BLOCKED.md exists under projects/ebilling-r1/ documenting a root-CLAUDE.md §6 escalation, shown in transcript. Stop after 24 turns if neither state is reached."
  W5-3 (single):      132
  W5-4 (single):      136 deploy WP5                                 — goal-eligible: NO (deploy)

Phase 6
  W6-1 (parallel, 2): 150, 152                                       — goal-eligible: NO (two tasks)
  W6-2 (single):      151
  W6-3 (single):      153 deploy WP6                                 — goal-eligible: NO (deploy)

Phase 7
  W7-1 (parallel, 2): 160, 161
  W7-2 (single):      162 — owner decisions on missed targets
  W7-3 (single):      163 UAT — owner session
  W9   (single):      190 wrap-up
```

## Critical Path

`003 → 004 → 005 (owner go/no-go) → 043 → 049 → 100 → 110 → 111 → 112 → 160 → 162 → 163 → 190`

joined at 049 by `029 → 070 → 071 → 072 → 073 → 049`, and WP1 `029` gates all server work that reads or writes new columns.

## Owner gates

| Task | Gate |
|---|---|
| 005 | Content Understanding go / no-go (no-go → 043 uses the DI-layout + extraction-Action fallback) |
| 011, 015 | Explicit owner OK before deleting a table / option set / data |
| 074 | F-9 — Decision Record granularity (per line vs per invoice), with ontology-r1 |
| 102 | F-8 — source of the invoice-level adjustment rule list |
| 103 | F-2 — approve with undecided Clean lines (assumed: allowed, implicitly Accepted in bulk) |
| 104, 105 | F-7 — non-privileged Return of a blocked internal submission |
| 162 | Any missed NFR-04 – NFR-06 target needs a recorded owner decision |
| 080, 088, 115 | New schema under EB-D-148 (D-144 covered the old one): ontology-r1 full schema review PASS on the final script and -WhatIf; notices to ontology-r1 and uac-r2 before and after; owner OK for each dev run — **given 2026-10-21** (still after ontology-r1 PASS and notices). 088's real run follows 080's |
| 091 | Owner OK for any live Azure change — **given 2026-10-21** for the dev storage change; no Key Vault changes. **Escalation:** the stamp identity already holds Storage Blob Data Contributor at account scope (`storage-account.bicep:156-180`), so a container-scoped grant restricts nothing and K-3's mitigation does not hold; 091 stops and asks the owner if dev shows the same |
| 081, 086, 089, 090, 094, 095, 098, 099, 117 | uac-r2 review (auth / secure family / tenant isolation; 086 and 098 two adversarial passes) |
| 093, 098 (external gate) | uac-r2 task 188 (`ExternalDocumentNarrowing`) merged. **Met: merged on master 446c83d927 (#2170), 2026-10-21.** 093 also R3 deploy pairing (BFF then SPA; SPA side is R4's) |
| 095 (external gate) | #2177 merged and deployed — met on dev 2026-10-21 (master 84291ecea8) |
| 096 | word-add-in-r1 removes the add-in side first; server legs one release later |
| 084 | UI half waits for the prototype (O-14) |
| 116 | The `sprk_sourcedocument` column delete runs only after 079, with the owner's OK |
| 119 (external gate) | ontology-r1 task 157 (SignalReadService Duplicate read check) merged; the same rule is reused |
| 120 | Live dev role change: owner's DIRECT OK (D-185 came via ontology-r1) + notices to ontology-r1 and uac-r2 before/after; send ontology-r1 the role-XML diff (overlaps their #2298) |
| 121 (external gate) | uac-r2 task 191 merged and deployed on dev, and uac-r2's written agreement to flip UntilTask191; live run with owner OK + notices |
| 122 | ✅ done 2026-10-26: ontology-r1 PASS, notices before/after, owner OK (EB-D-149); live on dev, -Verify 0 |
| 118a | Builds the shared InvoiceAttachmentMarking component (file subpath export, agreed with word-add-in-r1 2026-10-21); word-add-in-r1 reviews that PR. 118 consumes it |
| 163 | UAT sign-off |
