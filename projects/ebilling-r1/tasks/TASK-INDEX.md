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
| 050a | ✅ [implemented, on master; owner answered §9 items 2-4 on 2026-11-23 (EB-D-262..264); the follow-ups are task 050c] | Redirect the review-confirm route into the submission operation (D-134) | FULL | opus / high | none | 042, 049 |
| 050c | ✅ [done; merged into work f36b7ee7ef; Opus review + fix round 1 (F1 reject during an unfinished undo is 503); uac-r2 APPROVED the authorization] | 050a follow-ups: Reject after a Failed confirm submission (EB-D-262); drop InvoiceDate/TotalAmount from the confirm request (EB-D-263); confirm stops asking Create on sprk_invoice (EB-D-264) | FULL | sonnet / high | none | 050a |
| 050b | ✅ [done] | Retire the legacy extraction path (deletion) | STANDARD | sonnet / high | none | 042, 049, 050a |
| 051 | ✅ [done] | SPA submission UI (shared wizard component) | STANDARD | sonnet / high | P2-D | 044 |
| 052 | ✅ [done; browser ui-tests deferred to 112, notes/052-createinvoicewizard-rewire.md] | `CreateInvoiceWizard` rewired to the internal entry point | STANDARD | sonnet / high | P4-C | 044, 051, 105 |
| 053 | ✅ [done 2026-11-24: owner checklist Q passed (accepted INV-123.1.1 on MAT-000113); Rejected check moved to 079 step 10; ISS-051 #3080 found] | Deploy WP2 to dev | STANDARD | sonnet / high | none (deploy) | 042, 043, 044, 045, 046, 047, 048, 049, 050a, 050b, 051, 054 |
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
| 082 | ✅ [082a done (CoreRecordChain), 082b done (ProcedureResolver, merged 7061b5b7ec); 082c-1 done (loader and evaluator on the resolver; merged da03f0a38f); C19 done (chain close; owner: effective date wins; merged cf13b920a3); 082c-3 done (SPA 202 provisional warning, 2 s bounded pre-check, held Accepted bar; 2 adversarial passes, F1s fixed; merged 08702087cb); 082c-3b done (held badge "In Review", R4 2026-11-05; merged a9c8139126); 082c-2 done (gap and fault holds, review detail via InvoiceOpenHolds, partner label only while In Review; 3 adversarial passes, fix rounds 1-4; ontology PASS, uac-r2 cleared; merged ef6eb01910); 082d done (Procedure snapshot in sprk_findingsjson; two passes, fix rounds 1-3 + K follow-ups; uac-r2 approved; merged 7cd62a1710); split in notes/082] | Procedure resolver (7.1): firm condition, gaps and holds, re-keyed version selection (7.2), snapshot, ratchet ArchTest | FULL | opus / high | P3-D | 080, 081, 089 |
| 083 | ✅ [merged 28cfaf7ad8: two adversarial passes, fix rounds 1-2; tip build clean, ArchTests 2181/0, filtered unit 6984/0] | Firm clause: GetFirmClauseAsync re-keyed to the invoice's snapshot, firm-facing name; 130-135 POML constraint lines | FULL | sonnet / high | P3-E | 082 |
| 084 | ✅ [done 2026-11-12: merged into work; uac-r2 review (F-A, F-B, F-D, K2) fixed in round 3 (1421e49296) and re-checked PASS; F-C decided as EB-D-220 (084f)] | Procedures and rules in force on the record (Part A): the two caller-read routes of model 6.6 (EB-D-217) | FULL | sonnet / high | P3-E | 081, 089, 082 |
| 084a | ✅ [merged into work; RecordProcedures code page, web resource sprk_recordprocedures; gate (no F1) + fix round 1; 107 jest tests; ui-tests run at 112 (Work Assignment form needs a Billing tab)] | Procedures on the record (B1): read-only Billing-tab panel (new code page RecordProcedures) | FULL | sonnet / high | P3-E2 | 084 |
| 084b | ✅ [done 2026-11-12: merged into work; adversarial review (10 findings) fixed in round 1; 197 jest tests; read-only state via 084g (EB-D-221); ui-tests at 112] | Procedures on the record (B2): Procedure wizard and rule Exclude on 089 routes | FULL | sonnet / high | P3-E2 | 084a |
| 084c | ✅ [done 2026-11-13: merged `a74278e22c`; Change for this record (New values step), 13 typed forms pinned to the server schemas by an ArchTest, field errors from a 400; adversarial review + fix round 1 (re-change of a changed or stale rule; rule bodies on the read model, uac-r2 PASS); scoped re-check clean; ui-tests at 112; rate-card reading EB-D-222] | Procedures on the record (B3): Change for this record, typed form per rule type | FULL | sonnet / high | P3-E2 | 084b, 084g |
| 084d | ✅ [all parts merged into work; ISS-021 dry-run/write parity added after master merge; split 2026-11-13 into 084d-1..5; design reviewed: uac-r2 + ontology-r1 OK with conditions, notes/084d-dry-run-design.md] | Procedures on the record (B4): before/after Impact dry run and Assign Procedure (ontology-r1 reviews) | FULL | opus / high | P3-E2 | 084, 084b |
| 084d-1 | ✅ [merged into work d24681ed38 (task/084d-1 f9f9528c3f); ArchTests 2570/0 incl. IL no-write guard; Procedure tests 1211/0; 6 mutation seeds each fail a test; peer review with 084d-2 diff] | Planning without writes: row overlay (four tables, fails closed), write-throwing planning service, Plan*Async extraction, IL no-write ArchTest (F1, F4) | FULL | opus / high | P3-E2 | 084d design |
| 084d-2a | ✅ [merged into work c313c6c211 (2a 224007eda7 + fix1 475c05ed8e); Opus adversarial + fix round; uac-r2 PASS; ontology-r1 OK; K-6 → ISS-019] | Access, read and routes: shared checks, filter order, synthetic load + collision (uac-r2), combined check, F3, three POST routes, ledger, four guards, publish delta (uac-r2 + ontology-r1 review) | FULL | opus / high | P3-E2 | 084d-1 |
| 084d-2b | ✅ [merged into work c313c6c211 (2b 97e8a59a2a + fix1 4c5d562619); Sonnet scoped review + fix round; uac-r2 PASS; ontology-r1 OK; limits in notes] | Impact: closes (F2 messages), shared supersede selection, invoice-id count with caps, affectedOpenInvoices | FULL | opus / high | P3-E2 | 084d-2a |
| 084d-3 | ✅ [merged into work 588526e579 (06139f6aca + review round 1 c357f67c98); ontology-r1 OK with order pin 07c618c163; tip tests 8545/0/4] | Parity matrix (a)-(c), mutation proofs, no-write and concurrency tests | FULL | opus / high | P3-E2 | 084d-2 |
| 084d-4 | ✅ [merged into work cd869e3050 (029428af92 + round 1 7af227b4c8); Opus review + Sonnet re-check PASS; jest 392/392] | Client: Impact step in both wizards on the dry run ("if saved now"), jest. Assign wizard moved to 084d-5 (no route lists assignable Procedures; Xrm reads of Procedure tables are barred) | FULL | opus / high | P3-E2 | 084d-2 DTOs |
| 084d-5 | ✅ [5a + 5b merged into work (8f5ff019fa, f86871820a); design OK by uac-r2 + ontology-r1; owner EB-D-227..230] | Assign Procedure: a server route listing the Procedures assignable on a record (EB-D-218 caller check, uac-r2 review), then the Assign wizard (billing admin only, Follow latest / Pin, Impact on the dry run, writes through 089) | FULL | opus / high | P3-E2 | 084d-4 |
| 084d-5a | ✅ [merged into work 8f5ff019fa; Opus review + 2nd Opus pass PASS + Sonnet re-check fixed; uac-r2 + ontology-r1 OK; EB-D-232 merged on top] | Server: GET .../procedures/assignable (50 + q, tooMany, one batched check, parity with 089's assign) + 089 assign refuses a non-Billing Procedure (EB-D-228) | FULL | opus / high | P3-E2 | 084d-4 |
| 084d-5b | ✅ [merged into work f86871820a (935894324a + review fixes 9b3b45e0e0); Opus review FIX → fixed; Sonnet re-check PASS; jest 420/420] | Client: Assign Procedure wizard (entry point gated by permissions; pick from the assignable list; Follow latest / Pin; Gap warning; Impact on the dry run; writes through 089) + jest | FULL | opus / high | P3-E2 | 084d-5a |
| 084e | ✅ [done 2026-11-12: merged into work; RulePrecedence shared by evaluator and read (parity tests); Outranked/CapOutranked marks with ontology-r1 wording; gate read entry with no log or meter, guarded by BillingGateForReadCallerGuardTests; ontology-r1 approved] | Rules in force (B5): one shared precedence function and the "Outranked by X" mark (EB-D-219; ontology-r1 reviews) | FULL | opus / high | P3-E2 | 084 |
| 084f | ✅ [merged into work; source only (2 Reads at Deep on 2 roles + until-191 allow-list + scripts); uac-r2 role-diff review PASS 2026-11-12; dev apply at 112 with owner OK] | Billing admins can read Procedure Versions and Procedure Assignments (EB-D-220): role source, reader role, until-191 guard | FULL | sonnet / high | P3-E2 | 084 |
| 084g | ✅ [done 2026-11-13: merged into work `fd885aaffb`; two adversarial passes + fix round 1 (B1-B7, A1-A4, R1); uac-r2 PASS; 129 contract + 243 jest tests; ui-tests at 112] | Procedures on the record: read routes return the caller's write permissions; 084b wizards open read-only from them (EB-D-221; uac-r2 reviews) | FULL | opus / high | P3-E2 | 084, 084b, 084e |
| 084h | ✅ [done: delivered by 084h-1..4 (all merged); deploy with 112] | Procedures on the record: each 089 per-record write (assign, pin, unpin, exclude, rule exclude, change, remove) files a Decision Record on the core record through the ontology Decision Record writer, with the optional note (EB-D-236) | FULL | opus / high | P3-E2 | 084, 089, 084g |
| 084h-1 | ✅ [merged into work df7dc16ac9 + fix round 2 e841d90118: one Opus adversarial pass, fix round 1, ontology-r1 path-B PASS with test changes F3-1/F3-2/item 3; RecordProcedure_* 116/116, writer+catalog+immutability 573/573; notes/084h-1-writer-entry.md] | Decision Record writer entry WriteRecordProcedureDecisionAsync (procedure-dr- key, RecordProcedureLookupFailed), catalog set RecordProcedureActions (seven codes) and writer tests W1-W8 (design 084h sections 3, 4.1, 8, 11) | FULL | opus / high | P3-E2 | 084h design (ontology-r1 PASS) |
| 084h-2 | ✅ [merged into work 606812068a + F4 test fix 93ead0d4b2; EB-D-239] | Recorder + ProcedureAssignmentService + endpoints: one Decision Record per committed per-record act, note (JSON body on DELETE), Unknown read-back, decisionRecord member, enqueue on failure | FULL | opus / high | none | 084h-1 |
| 084h-3 | ✅ [merged into work bb4ad3f578; EB-D-239 Q10; ADR-004 path A pending owner] | ADR-004 repair job RecordProcedureDecisionJob (J1-J5) | FULL | opus / high | none | 084h-2 |
| 084h-4 | ✅ [merged into work b5740ebd79; fix round 35b574b0c9; deploy after the BFF with 084h-2/3] | RecordProcedures UI: Note box on each change + NotRecorded warning | STANDARD | sonnet / high | none | 084h-2 |
| 085 | ✅ [merged into work 052307dc0b; one adversarial pass, two fix rounds (publish and create are check-then-use with 409 on a changed version or type; F1-b reverted per ontology ruling (c), durable fix DEF-061); uac-r2 and ontology-r1 approved] | Rate cards keyed to Procedure Version: import, copy-forward, resolver tiers re-keyed to the rank; no scope column | FULL | sonnet / xhigh | P3-E | 080, 082 |
| 086 | ✅ [done 2026-11-22: uac-r2 signed off; owner accepted the -Verify records for criterion 6; notes/086-storedfile-secure-child.md] | Invoice Stored Files join the secure family as a leaf child of sprk_invoice: lineage, role row, creator person, census | FULL | opus / high | P3-D (p-safe false) | 088, 090, 089 |
| 087 | ✖ [cancelled] | Secure BU snapshot writer — cancelled 2026-10-21 by EB-D-148 (BU filter dropped, model §12 / O-12) | — | — | — | — |
| 088 | ✅ [done] | Stored File schema, the sprk_document submission marker (field-secured, app-only refusal) and invoice status Superseded | FULL | opus / high | P3-A | 080 |
| 089 | ✅ [done 2026-11-23: owner answers EB-D-265 (allow change of a dropped rule) and EB-D-266 (child-unit proof waits for a customer with child units); notes "089 close-out"] | Assignment and override service (pin, exclude, change rule, removal preview); secure-child registration; K-2 pin test — 089a done; 089b done (2 adversarial passes, fixes merged 1848d67bdb); 089c done (2 passes, 2 fix rounds, re-check; merged 593c70f2b5); 089e done (2 passes, 3 fix rounds, uac-r2 approved; merged 6bb7d928a9; dev roles applied 2026-11-04); owner: stale-Signals (a)/(b), deviations 1-2 (notes/089-assignment-override-service.md) | FULL | opus / high | P3-C (p-safe false) | 080, 081 |
| 089e | ✅ [done: 2 passes, 3 fix rounds, uac-r2 approved (recorded in 089's row)] | 089c follow-ups: close stale base-rule Signals as Superseded on a rule change; a descriptive error for record-scoped rule sets; edit rights for Billing Administrator | FULL | sonnet / high | P3-E | 089 |
| 089d | ✅ [done 2026-11-04: option R, 2 passes, 2 fix rounds, merged 743bf32010; -Apply on dev not run (needs owner go + live-proof child BU)] | Billing Rule Reader role assigned in the customer top unit's context (owner chose R over the team), formerly: Billing Rule Readers team in the customer top unit (Read at Deep on sprk_policy) so child-unit billing admins can read and Exclude org-wide rules; guide and provisioning step | FULL | opus / high | none (p-safe false) | 089b |
| 090 | ✅ [done] | File store service: staged blob, Not yet servable row, verified flip; internal download route; orphan reconcile job | FULL | opus / high | P3-B | 088, 099 |
| 091 | ✅ [done] | File store provisioning: blob containers, container-scoped role, malware-scan setting, customer-provisioning hand-off | FULL | opus / high | P3-A | none |
| 092 | ✅ [done; core on master via 094; review-fix port merged into work 2026-11-04] | Submission on the file store: uploads stage Stored Files, extraction reads the blob, review gets file id, 028 retires | FULL | opus / high | P3-C | 090 |
| 093 | ✅ [merged into work a95f7e7297: two Opus adversarial passes, fix round 1 A-H incl. uac-r2 ruling B; uac-r2 PASS; ArchTests 2630/0; deploy pairs with spa-r4 R4 021] | Firm file routes GET /api/v1/external/invoices/{id}/files and …/content; the four invoice-document routes retire | FULL | opus / high | P3-D (p-safe false) | 090, 098 |
| 094 | ✅ [done; on master eaa0d44f7f (#1911)] | Submit Invoice from a source Document: all-or-nothing steps 1-6, file first (A), copy rule (B), 050a re-file retired | FULL | opus / high | P3-E | 086, 090, 092, 098 |
| 095 | ✅ [done; on master eaa0d44f7f (#1911); dev after the BFF deploy + EB-D-173] | Submit Invoice from the Outlook add-in: POST /api/finance/invoice-submissions/from-email (email-only save + submission) | FULL | opus / high | P3-F (p-safe false) | 094, 088 |
| 095a | ✅ [done; on work 9535e73cb7, scripts 76424a3b88; owner choice B column sprk_sourceemaildocument; scripts not run; notes/095a-submission-by-email-read.md] | GET /api/finance/invoice-submissions?internetMessageId=… : the latest readable attempt for an email (by-id body + submittedOn, submittedBy, isCorrection) for the add-in's "already submitted" banner | FULL | sonnet / high | none | 095, 097e |
| 095b | ✅ [done; on work 7d5604d65d; review pending with 095a] | Notify the submitter (in-app bell) when a submission reaches Accepted / Failed / refused; never affects the outcome; exactly once | FULL | sonnet / high | none | 095 |
| 095c | 🔄 [095c-1 done; 095c-2 merged a1159a3aff (reviewed, F1 retry fix running); 095c-3 merged 9c90660252 (reviewed, no F; K1-K4 folded into 095c-4); 095c-4 merged a4f5884e66 (review running); owner: no-firm invoices stay visible externally (notes §13). Option B (owner 2026-11-03): schema script + SpaarkeMaster source (not run), allocator Guid? + phase probe, scope write refused to generic writers; notes/095c-from-email-firm-optional.md sections 8-9] | Submit Invoice from email: firm optional, "firm needed" hold, approval refused without a firm (owner UAT 2026-11-03) | FULL | opus / high | none | 095, 095a, 095b, 097 |
| 095c-5 | ✅ [done 2026-11-04: 2 passes, 2 fix rounds, merged db1f70a605; dev -Apply/-Verify PASS 14:16Z] | The firm hold lives on the invoice too: field-secured sprk_invoice.sprk_firmhold, so unlinking or deleting the submission cannot lift it (uac-r2 2026-11-04); blocks the master PR | FULL | opus / high | none (p-safe false) | 095c-4 |
| 095d | 🔄 [095d-2b-2 MERGED into work 2fac72b8a5 (EB-D-204; Add line gate tests); 2b-3 running. 095d-2 ACCEPTANCE (114 pass 2 K2): a re-check writer that re-holds a submission removes the findings `override` stamp in the same write. 095d-1a done (allocator renumber + sprk_previoussequence); 095d-1b done (Set firm service + route; 2 passes, 3 fix rounds, uac-r2 cleared; merged d5b13b49ed); 095d-1c done (Decision Record after the apply, 503 incomplete, repeat finishes the record; stored findings typed-writer only; 1 pass, 2 fix rounds, uac-r2 095d-1 final cleared, ontology K-3 PASS; merged 75f63a86c0); live seam test moved: runs on dev after the master merge + uac-r2 deploy + P2 migration (owner go), gates any release beyond dev; Set firm P0 select guard merged with 082c-2; previoussequence MappingProtected fix merged ee8ff0984c; next 095d-2, 095d-3; 095d-1 never reaches master without 095d-2] | Set the firm on a no-firm invoice: Decision Record, renumber into the firm sequence, firm rules run | FULL | opus / high | none | 095c |
| 095d-1 | ✅ [done: delivered by 095d-1a, 1b (merged d5b13b49ed) and 1c (merged 75f63a86c0), all on master; reconciled 2026-11-24 from the 095d row] | Set the firm, part 1: renumber, route, one apply transaction, then record, close, link | FULL | opus / high | none | 095c |
| 095d-2 | ✅ [done: delivered by 095d-2b-1, 2b-2 (merged 2fac72b8a5) and 2b-3 (merged c2239d9d58), all on master; the re-hold stamp removal (brief R14) is in InvoiceProcedureHold; reconciled 2026-11-24] | Set the firm, part 2: the rules re-run, the hold release and the shared Signal close | FULL | opus / high | none | 095d-1 |
| 095d-2b-3 | ✅ [merged into work c2239d9d58] | The post-Set-firm re-run writes the automatic supersedes link in its commit (EB-D-206) | FULL | opus / high | none | 095d-2b-2 |
| 095e | ✅ [merged into work 7953d052d9 (0de1967a59 + review fixes 17e379c44e); Opus review FIX → fixed; re-check PASS; contract §2/§9/§11/§13 updated, sent to word-add-in-r1] | No invoice without a firm in every channel (EB-D-223: refused at intake, nothing written; held invoices keep Set firm); invoice name from the chosen firm (EB-D-226); firm {id, name} on the 095a by-email view (word-add-in banner) | FULL | sonnet / high | none | 095d |
| 095f | ✅ [done: part 1 merged bb28870b76; part 2 delivered by 095f-2s, 2a, 2b, 2c, 2d1, 2d2, 2e1, 2e2a, 2e2b, 2ui (all merged into work); the column script scripts/ebilling/Deploy-EbillingInvoiceCorrectionNoteColumn.ps1 runs before any environment beyond dev] | Correction note (EB-D-225): `correctionNote` on from-email Correction mode, saved on the new invoice; the Invoice Review adjust/decision steps write the same column. Contract to word-add-in-r1 (max length, missing-note rule, master SHA) | FULL | sonnet / high | none | 095e |
| 095f-2s | ✅ [done; dev -Apply run 2026-11-17 with the owner's go, uac-r2 notified before and after; -Verify PASS] | Columns sprk_invoice.sprk_decisionnote (EB-D-240) + sprk_invoicecorrection.sprk_note (EB-D-242): script, Pester, deploy-order guard, external must-not-fire | STANDARD | sonnet / high | none | — |
| 095f-2a | ✅ [merged into work 7849bd05a0; deploy gate: column on dev before master] | Lifecycle overload writing sprk_decisionnote; reject/return/approve notes; InvoiceDecisionNoteWritersGuardTests | FULL | opus / high | none | 095f-2s |
| 095f-2c | ✅ [merged into work 18733ac44a; fix round c7ffb82f5a; re-check fixes 9ae666fec8; ontology-r1 PASS catalog + writer; column on dev before master] | Override and blocked-return notes | FULL | sonnet / high | none | 095f-2a |
| 095f-2d1 | ✅ [merged into work f9d63c555b; ontology-r1 PASS catalog; ISS-029 known limit] | Accept/reduce/disallow line notes; bulk carries none | FULL | sonnet / high | none | 095f-2a |
| 095f-2e1 | ✅ [merged into work 7d5a3a369a; one adversarial pass, no F; K1-K3 in notes/095f-2e1-from-document-note.md] | from-document requires a note on a Correction (EB-D-237) | FULL | sonnet / high | none | — |
| 095f-2b | ✅ [merged into work a3ef7e1c93; fix 64c3e9ecbb (server-derived ids); ontology-r1 PASS catalog + exact-set amendment + fix; ISS-030 known limit] | Adjustment Decision Records add-adjustment/remove-adjustment + note (EB-D-241) | FULL | opus / high | none | 095f-2a |
| 095f-2d2 | ✅ [merged into work 6ea1ceb837; fix round 740e5166fa; ISS-028 owner question; column on dev before master] | Correct/confirm note on the correction row (EB-D-242) | FULL | sonnet / high | none | 095f-2s |
| 095f-2ui | ✅ [merged into work d3a61cc426; review + 3 fix rounds + tweak; known limits in notes; ISS-031/032 filed] | Invoice Review Note boxes on every decision | STANDARD | sonnet / high | none | 095f-2a, 2c, 2d1, 2b, 2d2 |
| iss030 | ✅ [merged into work 46bd83d459; add-path twin fixed in review round 1] | ISS-030 #2881: refused totals after an adjustment removal reactivates the row | FULL | sonnet / high | none | 095f-2b |
| iss031 | ✅ [merged into work ececf32dbd; review + 2 rounds; invoice reject/return guard added; ISS-034 owner q (f)] | ISS-031 #2882: blocked Override/Return refuses while the other verb's record exists | FULL | opus / high | none | 095f-2c |
| iss034a | ✅ [merged into work 02173d8497; Opus review + second pass, ontology-r1 + uac-r2 PASS; K5 owner ack pending] | ISS-034 #2888 server: operator route finishes a stranded blocked decision as recorded (EB-D-244) | FULL | opus / high | none | iss031 |
| iss034b | ✅ [merged into work d5934d6d51; Opus review + round 1 (F1 also fixes Override/Return forms closing when the hold clears)] | ISS-034 #2888 client: Invoice Review "Finish as operator" | STANDARD | sonnet / high | none | iss034a |
| iss028 | 🔄 [part 1 332515309f, part 2 92e6e5b629 (EB-D-249; Opus + round 1, uac-r2 OK); dev applied 2026-11-17 (EB-D-250/252, verify pass, uac-r2 notices sent); 1.3.2.0 expectations at master merge] | ISS-028 #2879: sprk_invoicecorrection writer-only (EB-D-248) | FULL | sonnet / high | none | 095f-2d2 |
| iss040 | 🔄 [merged into work; uac-r2 PASS; dev apply waits for owner OK; ships with the 1.3.2.0 (or next) SpaarkeMaster release] | ISS-040 #2901: rule set visibility native write hardening | FULL | sonnet / high | none | ISS-038 |
| iss046 | ✅ [done 2026-11-24, merged 234828cfb2] | ISS-046 #3001: forms script Section mode keeps cells it does not manage (invoice Batch quick view) | STANDARD | sonnet / high | none | 194 |
| iss048 | ✅ [done 2026-11-24, merged f4f5b06f52] | Invoice submission result screen: title and icon follow the outcome; no "Submit it again" for a record-setup failure (found in checklist Q) | FULL | sonnet / high | none | 053 |
| iss043 | ✅ [done 2026-11-24, merged 263aa4c674] | ISS-043 #2978 + ISS-035 #2889: multipart routes answer a malformed body with 400; a repeated firmInvoiceNumber or poNumber is refused | FULL | sonnet / high | none | 123 |
| iss029 | ✅ [done 2026-11-24, merged 5f75e3e930] | ISS-029 #2880: a line decision repeated with other content answers 409 id_reused and writes nothing | FULL | opus / high | none | 095f-2d1 |
| iss042 | ✅ [done 2026-11-24, merged 1b756a83d3] | ISS-042 #2933: firm rejection findings show firm labels, not raw valuesRead keys | FULL | sonnet / high | none | 130c, 134 |
| iss050 | ✅ [done 2026-11-24, merged 7da8f29320; Opus review, fix rounds 1-2 (pre-write renewal, approval-level text), Sonnet re-check; filtered tests 439/0; dev duration of a 1,000-line call still unmeasured] | ISS-050 #3059: the line-decision lock is renewed during a long call; a call that loses it stops and answers incomplete | FULL | opus / high | none | iss029 |
| iss047 | ✅ [done 2026-11-24, merged 219c0c6207; Sonnet review, fix round 1 (F1 word-marker rows, 132ui, ranges); Pester 28/0] | ISS-047 #3042: drift check reads iss-prefixed and hyphen-suffixed task ids | STANDARD | sonnet / high | none | none |
| iss041 | ✅ [done 2026-11-24, merged efab4736b6; Sonnet review, fix round 1 (stuck form after a refused notice, recorded-stage intro, stale draft), re-check PASS; jest 653/0] | ISS-041 #2930: finishing a stranded reject/return after a reload can still send the firm notice | FULL | sonnet / high | none | 132ui |
| iss049 | ✅ [done 2026-11-24, merged 7affeb4768] | ISS-049 #3057: chat document, visualization and SPE admin upload routes answer a broken multipart body with 400 | FULL | sonnet / high | none | iss043 |
| iss015 | ✅ [done 2026-11-24, merged dbbad24b09] | ISS-015 #2743 + ISS-026 #2877: the five eBilling code pages are linted by client-lint (local and CI); drift-check Pester in CI | FULL | sonnet / high | none | none |
| iss025 | ✅ [done 2026-11-24, merged 6e9054434c] | ISS-025 #2876 + DEF-055 #2600: failed jobs retried with back-off (scheduled re-send, logical attempt count) | FULL | opus / high | none | none |
| iss051 | 🔲 [open] | ISS-051 #3080: resolved UTBMS codes reach the Invoice Line code lookups (AP export and index carry the resolved code) | FULL | opus / high | none | none |
| iss052 | 🔲 [open] | ISS-052 #3082: the SPA "Invoice received" screen links to the invoice and its matter / project / work assignment | FULL | opus / high | none | none |
| iss027 | ✅ [done 2026-11-24, merged fbba523806] | ISS-027 #2878: RecordProcedures Reason boxes keep a long paste and hold Finish over the limit | FULL | sonnet / high | none | 084h-4 |
| 084h-5 | ✅ [squash-merged into work (EB-D-251 option B: derived id only for condition-cleared and Procedure); Opus review + fix rounds on the r1 branch; ontology-r1 PASS; uac-r2 OK; known limit: derivable record id, see uac-r2 thread] | Keyed Decision Records get a derived primary id: Dataverse refuses a duplicate (EB-D-247, ontology-r1 option 1) | FULL | opus / high | none | 084h-3 |
| iss034c | ✅ [merged into work ee94c7d4ea (server: completer in the stamp, atomic with the completing write); Opus review no F1/F3, F2 note fixed, K1-K4 in notes; AC5 History-tab display deferred: owner question] | Blocked decision stamp records who completed it (iss034a K5, EB-D-253) | FULL | sonnet / high | none | iss034a, iss034b |
| iss034d | ✅ merged into work | History tab "Finished by" for a blocked decision (EB-D-257; iss034c AC5) | FULL | sonnet / high | none | iss034c |
| iss032 | ✅ [merged into work 0ec908c554; Opus review F4 (stranded after the record) + F2 + K1 fixed in round 1; Sonnet re-check no F; K-limits in notes] | Invoice Review: finish a reject/return/approve stranded after its transition (ISS-032 #2883) | FULL | opus / high | none | 095f-2a, 095f-2c, 095f-2ui |
| 095f-2e2 | ✅ [B′ chosen, EB-D-245: delivered by 095f-2e2a + 095f-2e2b] | Internal Correction path | — | — | none | 095f-2e1 |
| 095f-2e2a | ✅ [merged into work cdb91493a9; Opus review no F-class; K5-K7; ISS-035 filed] | B′ server: internal multipart route accepts Correction + required note (one shared check) | FULL | opus / high | none | 095f-2e1 |
| 095f-2e2b | ✅ [merged into work f160fadc24; Opus + round 1; spa-r4 approved; shared lib 3.1.0; ISS-036 filed] | B′ client: Invoice Review "Submit corrected invoice" → wizard correction prop (spa-r4 conditions) | FULL | sonnet / high | none | 095f-2e2a |
| 095g | ✅ [merged into work e8dcb1ef80 (not pushed until #2822 merges); 2 Opus adversarial passes + fixes; uac-r2 APPROVED 367e5b2876; type-code script scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1 NOT run (owner go in this session + uac-r2 notices); routes answer 503 on dev until it runs; pane is word-add-in-r1's] | Assign a firm from the Outlook pane (EB-D-224): internal-only active-firm list + assign route on matter/project/work assignment, built to uac-r2's six conditions; two adversarial passes; uac-r2 reviews before merge | FULL | opus / high | none | 095e |
| 096 | ✅ [merged into work 98801e9b9f 2026-11-23; Opus review: no F1, F2 OFFICE_002 text fixed (f60f51a2a3); save and invoice quick-create now 400 OFFICE_023 before any read or write; notes/096-remove-office-invoice-legs.md; publish -0.04 MB vs master, no CVE; gate was slice C 148ec4dec0 + release #2999] | Remove the Office server legs for Invoice as a save target and invoice quick-create | FULL | sonnet / high | P3-F | 095 |
| 096b | ✅ [merged into work 1a316a2ca0; uac-r2 approved, no F; applied on dev 2026-11-11 (b144fe739e), -Verify pass; D3 role change deferred (R1, R2, R4, then the privilege)] | Close the remaining direct invoice-create paths: the generic child-record create route (`sprk_invoice`, `sprk_billingevent`), the model-driven New / quick-create invoice, user Create on `sprk_invoice`, and the dev cleanup (EB-D-201) | FULL | sonnet / high | none | 052, 112 |
| 097 | ✅ [split into 097a–097f, EB-D-179; all parts done] | Corrected invoice: the old invoice closes Superseded in step 4 (guarded) and its open Signals close Superseded | FULL | opus / high | P3-F (p-safe false) | 094, 104, 088 |
| 097a | ✅ [done; on work, ontology-r1 OK on the loader change] | Superseded lifecycle status (659490005), its transitions and every status reader; the duplicate rule ignores a superseded prior | FULL | opus / high | P3-F | 097 |
| 097b | ✅ [done; on work 932efea9cc; ships to master only with 097c] | Correction intake on both routes: contract fields, guards (Write, firm, core record, status, same number), target in `sprk_resubmissionof`, job snapshot | FULL | opus / high | P3-F | 097 |
| 097c | ✅ [done; on work] | Persister: the explicit target replaces the automatic lookup, guarded Superseded close in the commit transaction, compensation reopens the old invoice | FULL | opus / high | none | 097a, 097b |
| 097d | ✅ [done; on work 169c55a498 with the follow-up and the second-review fixes; ontology-r1 OK on ChangedSinceRead→Retry; field security for `sprk_resubmissionof` pending (owner OK)] | After-step closes the old invoice's open Signals as Superseded; resume and idempotency; notes; two adversarial passes | FULL | opus / high | none | 097c |
| 097e | ✅ [done; on work 8b49cb2432; shape agreed with word-add-in incl. replacedBy] | Correction prefill read for the add-in (firm, core record, status, tracking number; 095 contract item G3) | FULL | sonnet / high | none | 097b |
| 097f | ✅ [done; on work; word-add-in client condition built] | Superseded invoice's submission view: stays Accepted with a 'replaced by a correction' outcome reason and the correcting tracking number (EB-D-180) | FULL | sonnet / high | none | 097d |
| 098 | ✅ [merged 8b1aa1fa58; uac READY round 5] | (a′) on sprk_invoice: hide invoices with an isolated Submit Invoice source, and derived rows, from external callers | FULL | opus / high | P3-C (p-safe false) | 088, 090 |
| 099 | ✅ [done] | Finance step 0: refuse flagged-external callers on every /api/finance route (5.3; owner 2026-10-21) | FULL | opus / high | P3-A | none |
| 113 | ✅ [merged into work 84e708682c: one Opus adversarial pass (no leak, no false NotChanged), fix round F1-F5 + timeout hardening; ArchTests 2628/0] | Read-time Procedure-change banner on /review: app-only comparison with the snapshot, internal callers only | FULL | sonnet / high | P3-F | 082, 100 |
| 113b | ✅ [on work: one Sonnet adversarial pass, no F findings; its K2 (unknown state shown as nothing) closed: an unknown state with text shows as information. 14 tests, page suite 256/256, typecheck clean, build OK. Not deployed: ships with task 112, the page's first deploy (owner go)] | Invoice Review page shows the Procedure-change banner (113's `procedureChange`): Changed and CouldNotBeChecked as warnings, NotYetChecked as information, nothing for NotChanged or absent. Added 2026-11-15 on owner go (no task rendered it; 110 predates 113) | STANDARD | opus / high | P3-F | 113 |
| 114 | ✅ [done: an Override releases a Procedure hold (gap or unresolved; owner 2026-10-21; firm hold excluded) on Internal and SPA; release = code AND field-secured findings stamp; 2 adversarial passes (F1 forged marker, F4 PS case fixed); merged c4b8d43e8f; 114b (ontology D-201: closes only released Signals; FR-51 on unfiltered set) merged] | Override releases a no-version-in-force gap hold for that invoice only, recorded through WriteBillingDecisionAsync | FULL | sonnet / high | P3-F | 082, 105 |
| 115 | ✅ [on master b0679c6ce1 + f4874f4f8a; ontology-r1 PASS 12040e2a74; dev options read back 6edfdb6d6b] | AddedFee (100000015) and Discount (100000016) billing rule types: options, domain, schemas, validator refusals, handlers | FULL | opus / high | P3-A | none |
| 116 | ✅ [merged into work cd9d90aaa0 (fix rounds 1-6, EB-D-211/214/216/216a) and to master in PR #2724; index row was stale until 2026-11-17] | Procedure Version documents: drafting and readers keyed per version on its Stored File; sprk_sourcedocument retired | FULL | opus / high | P3-F (p-safe false) | 081, 085, 090 |
| 117 | ✅ [done 2026-11-23: merged f4a272136c; 2 Opus passes, fix round 1, scoped re-check K-only; uac-r2 APPROVED becd5b983c; Apply off until 079] | Core-record delete and Procedures: retire sweep for unlinked record-scoped rule sets; delete lists agreed with uac-r2 | FULL | opus / high | P3-D (p-safe false) | 089 |
| 118b | ✅ [done: 118b-1 (shared document read, contract, caller Read check, sameness) + 118b-2 (staging, SameSource parts list, persister branch); notes/118b-from-document-supporting-files.md] | Submit Invoice from document carries supporting documents (supportingDocumentIds → Supporting Stored Files) | FULL | opus / high | none | 094, 118a |
| 118 | ✅ [done: notes/118-submit-invoice-email-ui.md] | Submit Invoice in the email UI: toolbar button (replaces Link Invoice), flagged attachments, Email Review Invoice tab | FULL | sonnet / high | P3-F | 094, 118a |
| 118a | ✅ [done] | InvoiceAttachmentMarking shared component, split out of 118 for word-add-in-r1 (EB-D-159) | FULL | sonnet / high | P3-F | none |
| 119 | ✅ [done] | Read-time Duplicate withholding on /review: hide an earlier invoice the reader cannot read (D-176, #2118) | FULL | opus / high | P4-A | 100 |
| 120 | ✅ [done] | Spaarke Billing Administrator role; D-185: Basic/Core User lose prvReadsprk_ruleset; guard test | FULL | opus / high | P3-R (p-safe false) | 081 |
| 121 | 🔲 [open] | Procedure-table role grants after uac-r2 191: O-6 org Read, Billing Administrator Create/Write on sprk_procedure; flip UntilTask191 | FULL | opus / high | P3-R (p-safe false) | 120 |
| 122 | ✅ | #2235 fix: field-secure sprk_findingsjson (BFF-only), off the form; Decision Records store the generic Duplicate sentence | FULL | opus / high | P4-A | 119 |
| 123 | ✅ [done 2026-11-23: merged 72c416e948; uac-r2 approved; word-add-in-r1 agreed the contract; deploy condition open, see notes/123-invoice-supporting-file-upload.md §12] | Internal upload of supporting files to an existing invoice: POST /api/finance/invoices/{invoiceId}/files (EB-D-161) | FULL | opus / high | P3-F | 090, 092, 086 |
| 124 | ✅ [done] | Retire the Procedure Library container: remove Finance:ProcedureLibraryContainerId, close the per-customer SPE hand-off DEF-013 / #1967 (EB-D-163) | FULL | sonnet / high | P3-F | 116, 092 |

### Phase 4 — Review and approval (WP4)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 100 | ✅ [done; on master] | Review read model and line decision endpoints | FULL | sonnet / high | P4-A | 014, 016, 049 |
| 101 | ✅ [done; on work de4e296242, review F-1..F-5 closed] | Value / code corrections, structured capture, re-evaluation | FULL | sonnet / xhigh | P4-B | 100, 027, 072 |
| 102 | ✅ [done; on master eaa0d44f7f (#1911)] | Invoice-level adjustments | STANDARD | sonnet / high | P4-B | 100 |
| 103 | ✅ [done; on master eaa0d44f7f (#1911)] | Approval: eligible approvers, worklist, approve | FULL | opus / high | P4-B | 100, 021, 024, 025, 074 |
| 104 | ✅ [done; on work a4e0721bdc, adversarial F1-a/F1-b fixed] | Reject and Return to Firm; resubmission linkage | FULL | sonnet / high | P4-C | 103, 045 |
| 105 | ✅ [done; on master; §8 Q4 closed: 103 and 104 use its lock key] | Override (server) | FULL | sonnet / high | P4-B | 044, 049, 074 |
| 106 | ✅ [done] | Context panel API | STANDARD | sonnet / high | P4-A | 029, 040 |
| 107 | ✅ [done] | Related work product API | STANDARD | sonnet / high | P4-A | 029, 040 |
| 108 | ✅ [done] | Bounding-box document viewer (shared component) | STANDARD | sonnet / high | P4-A | none |
| 109 | ✅ [done] | ConsoleKit additions: outcome cards, count filters, disclosure, status labels | STANDARD | sonnet / high | P4-A | none |
| 110 | ✅ [done] | Invoice Review code page — lines, review pane, corrections, bulk, by-rule | STANDARD | opus / high | P4-C | 100, 101, 102, 108, 109 |
| 111 | ✅ [done] | Invoice decision wizard and context / related / history panes | STANDARD | sonnet / high | none | 110, 103, 104, 105, 106, 107 |
| 112 | 🔲 [open] | Deploy WP4 to dev | STANDARD | sonnet / high | none (deploy) | 084, 084a, 084b, 084c, 084d, 084e, 084f, 084g, 052, 100, 101, 102, 103, 104, 105, 106, 107, 110, 111, 079, 095, 096, 097, 113, 114, 118, 119 |

### Phase 5 — Firm feedback (WP5)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 130-0 | ✅ [done; fix1 tip 79f5d1a508 and F3b 8b413d3d22 merged in merge/ebilling-batch-130, uac-r2 approved] | External invoices: apply the firm rule on every path (design.md:414) | FULL | opus / high | none | 044, 051 |
| 130-0b | ✅ [done; 0f6b49b5ab merged in merge/ebilling-batch-130, uac-r2 approved] | Assistant tools' seam: apply the same firm rule for external callers | FULL | opus / high | none | 130-0 |
| 130a | ✅ [done; merged with 130b (2367fbab2c) in merge/ebilling-batch-130, uac-r2 approved] | Firm findings projection service (no routes) | FULL | sonnet / high | none | 083, 100 |
| 130b | ✅ [done; fix1 tip 2367fbab2c merged in merge/ebilling-batch-130, uac-r2 approved] | Firm outcome external GET routes: firm-scope 403 filter, ledger entries, auth tests; line reasons only from writer-created records | FULL | sonnet / high | none | 130a |
| 130c | ✅ [done; 3a49f67f65 merged in merge/ebilling-batch-130, uac-r2 approved] | FR-18: a rejected submission projects its findings from the submission's own stored findings (line, values read, rule, clause through GetFirmClauseAsync, flag-gated) | FULL | sonnet / high | none | 130b |
| 130d | ✅ [done; 69a8b2db72 merged in merge/ebilling-batch-130, uac-r2 approved] | Firm-safe reason text: every rejection a firm sees carries its reason code and a firm-safe description (table per reject-capable code, catalogue-driven guard) | FULL | sonnet / high | none | 130c |
| 130e-1 | ✅ [done; merged into work a6f1363070 from 56bf6dce82; Opus review + 2 fix rounds; ontology-r1 O6 PASS; uac-r2 PASS; column live on dev 2026-11-16] | DEF-060 writer: per-rule firm description copied onto the billing Signal firm reason; code-matched projection override | FULL | opus / high | none | 116, 130a-130d |
| 130e-2 | ✅ [merged into work 58dcc56689; review + 2 fix rounds; server validator: Turkish i fold + default-ignorables refused (ontology-r1 PASS); ISS-033 filed] | Rule Review editor: firm description field with the "firms see this" notice | STANDARD | sonnet / high | none | 130e-1 |
| 130 | ✅ [delivered by 130-0, 130-0b, 130a, 130b, 130c, 130d, 130e-1, 130e-2 (all merged; EB-D-202 In Review lines covered, notes/130-firm-findings-projection.md)] | Firm-visible findings projection (clause quotes, visibility filtering) | FULL | sonnet / high | none | 019, 049, 100, 083 |
| 131 | ✅ [on master fe51619cfc; dev seeded 2026-11-19 (EB-D-256) on BFF 5ae2804884] | Firm notice Action `INVOICE-FIRM-NOTICE@v1` with template fallback | STANDARD | sonnet / high | P5-B | 041, 130 |
| 130f | ✅ | Firm view: Rejected / Returned invoices show their line findings (EB-D-259) | FULL | sonnet / high | none | 130, 131 |
| 130g | ✅ [merged into work 6b84fb1db1 2026-11-20; server only, the SPA shows it in 134] | Firm view: show the reviewer-confirmed notice prose as the firm-safe reason (EB-D-259) | STANDARD | sonnet / high | none | 130f, 132 |
| 132ui | ✅ [merged into work 04718b3224; page deploy to dev needs the owner OK] | Invoice Review notify step: see, edit and confirm the 131 notice before Reject / Return (EB-D-258) | FULL | sonnet / high | none | 132 |
| 132 | ✅ [server half merged into work 42382e6d9f; Opus review + second access review + 4 fix rounds; uac-r2 PASS; firmnoticetext field-security dev apply waits for the owner's OK; K16 accepted] | Notifications: email, SPA feed, approver in-app | STANDARD | sonnet / high | none | 131, 024, 103, 104 |
| 133 | ⏭️ [cancelled] withdrawn by EB-D-254 (owner option b): no PDF file in R1; on-screen report (134, printable) and the findings table in the notice email (131/132)] | Adjustment report PDF | STANDARD | sonnet / high | P5-B | 130, 028 |
| 134 | ✅ [merged into work 5f7195b9eb 2026-11-21; spa-r4 review held until the PR to master; UI tests at 136] | SPA invoice detail and on-screen report | FULL | sonnet / high | P5-B | 130 |
| 135 | ✅ [merged; uac-r2 PASS incl. F2 fail-closed test, spa-r4 PASS; BillingGuidelines 57/0] | SPA Billing Guidelines view | STANDARD | sonnet / high | P5-B | 076, 130 |
| 136 | 🔲 [open] | Deploy WP5 to dev | STANDARD | sonnet / high | none (deploy) | 130, 131, 132, 133, 134, 135, 112 |

### Phase 6 — Export and index (WP6)

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 150 | ✅ [done; on work b3eaf99f9f, review fixes in; credit-memo batches need Deploy-EbillingInvoiceTotalsRange.ps1 per environment (owner OK)] | AP export batch (CSV + JSON, D-56 default mapping, snapshot); files as internal-only Stored Files (EB-D-148) | FULL | sonnet / high | P6-A | 014, 023, 025, 028, 103, 090 |
| 151 | ✅ [split into 151a/b/c, EB-D-181; all on work] | Mark Paid and export UI | STANDARD | sonnet / high | none | 150 |
| 151a | ✅ [done; on work 06c72f5571] | Mark Paid route (Sent to AP → Paid) and the export batch file download (CSV / JSON, stored bytes) | FULL | sonnet / high | none | 150 |
| 151b | ✅ [done; on work 40d3505ac8; on dev 2026-11-03; sprk_trackingnumber in source 2026-11-03] | Invoice views by status, Export to AP and Mark Paid ribbon buttons on views and form, the export modal (ADR-050) with Download, the Billing Export form's Download | FULL | sonnet / high | none | 151a |
| 151c | ✅ [split into 151c-1a/1b/2, EB-D-182; on master via #2526 (5796774091)] | Email to AP: the AP export email column on the eBilling settings record (owner OK), CSV + JSON attached, sent from the user's mailbox through the communication send path, recorded against the batch | FULL | sonnet / high | none | 151a |
| 151c-1a | ✅ [done; on work 60e19c8fbe; uac-r2 PASS after 2 rounds; adversarial review fixed; dev columns + batch form live 2026-11-02] | Email to AP route: server-only byte attachments on the send-as-user path, the communication owned like its batch (RecordOwnershipResolver), no unit-readable copies of the files, `sprk_apexportemail` on settings and `sprk_senttoapdate` on the batch (first successful email or Mark Sent to AP); negative test: a non-finance same-unit user can't read the email | FULL | sonnet / high | none | 151a |
| 151c-1b | ✅ [done; on work b1f05d71c5; review F1-F3 fixed; UI not yet imported on dev (owner OK needed)] | Email to AP UI: the email step in the export modal, form buttons, Sent to AP Date on the batch form and the invoice views (through the batch link) | FULL | sonnet / high | none | 151c-1a, 151b |
| 151c-2 | ✅ [done; on work 9402a48ad4; uac-r2 PASS (F1 re-check OK); adversarial review fixed; dev schema + form not yet run (owner OK needed)] | Batch link on the email: a plain `sprk_billingexport` reference column on `sprk_communication` (uac-r2 agreed; not a regarding column, kept out of family edges, lineage and resolvers) | FULL | sonnet / high | none | 151c-1a |
| 151d | ✅ [done; on branch worktree-agent-a222e9efd2d39dbc2 fb2b45ab8a; folded into 151e] | The invoice form shows the batch's Sent to AP Date (a read-only quick view or linked field through `sprk_invoice.sprk_billingexport`; no new invoice column) | STANDARD | sonnet / high | none | 151c-1b |
| 151e | ✅ [done; on branch worktree-agent-a222e9efd2d39dbc2 fb2b45ab8a] | Invoice form Files section: GET /api/finance/invoices/{id}/files (what the content route would serve), a Files page with Open / Download and a link to each file's source email Document; plus 151d's Sent to AP Date | FULL | sonnet / high | none | 090, 151b |
| 152 | ✅ [done; on work 62039c6887, review fixes in] | Invoice index from structured records | STANDARD | sonnet / high | P6-A | 049, 103 |
| 153 | 🔲 [open] | Deploy WP6 to dev | STANDARD | sonnet / high | none (deploy) | 150, 151, 152, 136 |

### Phase 7 — Evaluation and rollout (WP7) + wrap-up

| ID | Status | Title | Rigor | Model / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 160 | 🔲 [open] | End-to-end integration and Action seam tests | FULL | sonnet / high | P7-A | 079, 112, 136, 153 |
| 161 | 🔄 [harness, offline baseline and report (notes/calibration-report.md) done 2026-11-03; DEF-051 filed; live runs S1/S2 pending owner OK] | Calibration report from corrections and the evaluation set | STANDARD | sonnet / high | P7-A | 002, 049, 101 |
| 162 | 🔲 [open] | Accuracy and performance evaluation; tuning | STANDARD | opus / high | none | 005, 160, 161 |
| 163 | 🔲 [open] | UAT with the owner and dev rollout | STANDARD | sonnet / high | none | 162 |
| 164 | ✅ [done 2026-11-23: 164a-c merged; dev ribbon already in the target state (dry run 0 changes), -Verify PASS, SecureChild -Verify PASS; uac-r2 acked] | Add line (reviewer-added invoice line, EB-D-192/198/199) | FULL | opus / high | none | 100, 103 |
| 165 | ✅ [done: 165a 2fac72b8a5, 165b item 1 b49465d972, 165b-2 cd35ae8fc3, 165b-3 5ae5590b66, 165b-4 5ebe26a03d, 165c, 165d, 165e, 165f all merged into work] | Invoice integrity (EB-D-200): hold, approval, AP export and spend decide only from server-written sources | FULL | opus / high | none | 164a |
| 165c | ✅ [merged into work 1aff454e34] | External invoice visibility counts a submission's invoice link only when it is its own invoice | FULL | opus / high | none | 165a |
| 165e | ✅ [merged into work 7061729544; two passes, no F; uac-r2 approved; ships in the same batch as 165f] | External invoice visibility decides only from the invoice's own submission, failing closed | FULL | opus / high | none | 165c |
| 165f | ✅ [merged into work f647f83705; two passes (pass 2 no F); uac-r2 approved; ships with 165e; guard-hardening K follow-up queued] | External invoice visibility counts a submission's document link only when it is its own document | FULL | opus / high | none | 165e |
| 165b-2 | ✅ [merged into work cd35ae8fc3; stored totals must match the lines at approval and AP export (pending spend moved to 165b-4); one adversarial pass, 2 F4 + K2 fixed; dev 0 mismatches; tip ArchTests 2181/0, unit 7476/0] | Stored totals match the lines at approval and AP export (165 item 3; item 4 moved to 165b-4) | FULL | opus / high | none | 165b |
| 165b-3 | ✅ [merged into work 5ae5590b66; part A + B (EB-D-200, EB-D-208: counted statuses need the writer-created approve record under the approval key; Mark Paid too); Opus pass + fix round, Sonnet re-check no F; uac-r2 approved A and B; tip 2174/0 + 11236/0] | Approval completion decides only from server-written sources; counted statuses need the approve record (165 item 2) | FULL | opus / high | none | 165b |
| 165b-4 | ✅ [merged into work 5ebe26a03d + re-check K1/K2 24641fdf1a; counted spend needs the writer's approve record, pending excludes held invoices, writer id only when counted rows exist; one adversarial pass, two fix rounds, Sonnet re-check; ontology-r1 approved; tip build clean, ArchTests 2189/0, filtered 2167/0; dev-deploy gate: AuthorableColumns full-green after 166] | The spend paths decide only from server-written sources: pending spend excludes held invoices; counted spend needs the approve record (165 item 4 + EB-D-208 spend) | FULL | opus / high | none | 165b-3 |
| 165d | ✅ [merged into work 66b7c31315; option A at the person sites; uac-r2 approved] | The AP export claim `sprk_billingexport` is writer-only: refused to generic app-only writers and field mapping (EB-D-207) | FULL | sonnet / high | none | 165a |
| 166 | ✅ [merged into work `ab95fe67d6`; applied on dev 2026-11-09, -Verify pass] | `sprk_invoice` money and firm columns field-secured (EB-D-200) | FULL | opus / high | none | — |
| 189 | 🔲 [open] | Docs: retire "Procedure Library"; data-model docs for the Procedure and Stored File tables; file-store architecture note | STANDARD | sonnet / high | none | 079 |
| 190 | 🔲 [open] | Project wrap-up | FULL | sonnet / high | none | all |
| 191 | ✅ [done; on work e657bfda4c] | Invoice reader preflight at intake (422 `ebilling.submission.reader_not_configured` before anything is written) and the `ai.invoice.*` outcome codes in contract §9 (owner OK 2026-10-31, after the first dev UAT) | FULL | sonnet / high | none | 095 |
| 192 | ✅ [done; on work 25778a59b7] | Optional `filedUnder` on the from-email 409 `document_core_record_mismatch`: the record the email is filed under, only when the caller can Read it, never for a secure record | FULL | sonnet / high | none | 191 |
| 193 | ✅ [done 2026-11-23: merged c21bde55a2; Opus pass, 2 fix rounds, scoped re-check; uac-r2 APPROVED 46a89e55f8; dev -Apply on owner's go, -Verify PASS] | Retire sprk_ratecard core-record lookups (matter, project, work assignment): solution source, schema script, docs, guarded dev delete | FULL | sonnet / high | none (p-safe false, SpaarkeMaster) | 085 |
| 194 | ✅ [done 2026-11-23: merged 96bcf65dbb; dev -Apply + -Verify PASS; ISS-045 #2997] | Take the retired extraction fields off the invoice main and quick-create forms; drop PB-013 from the seed and verify script | FULL | sonnet / high | none | 050b |

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
| 084 | Split per EB-D-217: 084 Part A, then 084a → 084b → 084c, 084d after 084 and 084b, 084e after 084 (EB-D-219); all deploy with 112 |
| 116 | The `sprk_sourcedocument` column delete runs only after 079, with the owner's OK |
| 119 (external gate) | ontology-r1 task 157 (SignalReadService Duplicate read check) merged; the same rule is reused |
| 120 | Live dev role change: owner's DIRECT OK (D-185 came via ontology-r1) + notices to ontology-r1 and uac-r2 before/after; send ontology-r1 the role-XML diff (overlaps their #2298) |
| 121 (external gate) | uac-r2 task 191 merged and deployed on dev, and uac-r2's written agreement to flip UntilTask191; live run with owner OK + notices |
| 122 | ✅ done 2026-10-26: ontology-r1 PASS, notices before/after, owner OK (EB-D-149); live on dev, -Verify 0 |
| 118a | Builds the shared InvoiceAttachmentMarking component (file subpath export, agreed with word-add-in-r1 2026-10-21); word-add-in-r1 reviews that PR. 118 consumes it |
| 163 | UAT sign-off |
