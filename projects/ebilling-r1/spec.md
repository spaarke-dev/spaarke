# eBilling R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-10-14
> **Source**: `design.md` (with its 2026-10-14 revision block) · binding decisions `notes/decisions-log.md` D-01 – D-66
> **Portfolio**: Issue #1850 · Epic #1847
> **Binding constraint**: `.claude/constraints/bff-extensions.md` (CLAUDE.md §10)

## Executive Summary

eBilling R1 delivers one complete outside-counsel invoice loop on Spaarke: a law firm submits a PDF invoice through the partner SPA; Spaarke stores it, extracts the header and lines with AI, codes each line to UTBMS, and evaluates it against published billing rules derived from the client's guidelines and rate cards. Blocking failures on reliably extracted data are rejected in the submission session; everything else is accepted, flagged and routed to an eligible in-house approver, who accepts, reduces or disallows lines, applies invoice-level adjustments, and approves, rejects or returns the invoice. Firms see outcomes and reasons on screen and in a PDF report; approved invoices are exported to AP as a file. The design accommodates the fast followers (mailbox and Outlook intake, LEDES files, POs, GL/cost centre, accruals, taxes, EU e-invoicing, multi-matter invoices, auto-adjust) without structural change.

## Scope

### In Scope

1. Reference data and setup: law firms, timekeepers, rate cards, UTBMS code sets and codes (seeded from utbms.com, with guidance text), code-set mapping per matter type / practice area, billing approver roles, firm billing contact, one eBilling settings record (D-59).
2. Guidelines → rules: upload guideline / rate / procedure documents to the Procedures container; AI drafts rules from a closed billing rule catalogue with clause citations; human review and publish.
3. Partner SPA invoice submission (PDF), with in-session result, on-screen report and PDF report.
4. Internal invoice submission through `CreateInvoiceWizard`, sharing the server-side submission operation and UI components, with Override / Return on blocking failures.
5. Extraction with an Azure AI Content Understanding custom analyzer (header + lines, per-field confidence and source location), UTBMS coding, AI observations (block billing, vague description) — see Processing Pipeline.
5a. Finance AI on the Action + Binding approach (D-52): attachment classification migrated; legacy extraction replaced; coding and rule drafting built as Actions.
6. In-memory rule evaluation (11 billing rule types) and the submission rejection rule.
7. Invoice review: per-line Accept / Reduce / Disallow, value correction, invoice-level adjustments, context panel, related-work-product panel.
8. Approval (single eligible approver), whole-invoice Reject, Return to Firm, Override — all written as Decision Records.
9. Firm feedback: SPA invoice detail, on-screen and PDF adjustment report, AI-drafted email + SPA notifications (D-60), SPA Billing Guidelines view (D-66).
9a. Structured capture of every reviewer confirmation and correction (D-64) — the data a later tuning platform consumes.
10. AP export batch (CSV + JSON, configurable mapping) to the e-billing container; manual Paid marking.
11. Invoice documents in the general container (secure-record container when applicable); invoice AI Search index built from structured records.
12. Schema changes agreed with ontology-r1 (Signal / Decision Record line subject; all-core-type derivation; billing rule types; billing decision actions) and the `sprk_invoicestatus` retirement.
13. Baseline verification of the existing Finance pipeline, evaluation set, extraction spike (Content Understanding vs Document Intelligence vs current), model and analyzer provisioning, accuracy evaluation.

### Out of Scope (R1)

- AP mailbox (`ap@ebilling.spaarke.com`) and Outlook add-in intake (R2).
- LEDES 1998B / 1998BI / XML file upload; XML or JSON API intake.
- Auto-adjust and auto-approve; multi-level or sequential approval routing.
- Formal appeals (partial payment, iterative negotiation); payment of anything other than the full approved total.
- Accruals; purchase orders; GL account and cost-centre coding; taxes; multi-currency (USD only); EU e-invoice ingestion (only reference IDs are stored).
- AI historical-pattern checks; "no supporting work product" flag; benchmarking; Teams / Copilot / Spaarke Console surfaces.
- Firm-proposed timekeepers and rate-increase requests.
- Payment-status import from AP (Paid is marked manually in R1).
- Multi-matter invoices (the model supports them; R1 creates one core record per invoice).
- Invoice-to-record content matching and record-mismatch detection (the submitter selects the record; R2+ with mailbox intake — D-62, D-65).
- AI tuning platform: example retrieval into prompts, evaluation runs, AI quality admin console — separate project `projects/spaarkeai-tuning-and-feedback-r1`; eBilling R1 does not depend on it (D-64).
- Firm access to guideline documents (firms see published rule content only — D-66).

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/Finance/` — submission, review, approval, export, rule endpoints
- `src/server/api/Sprk.Bff.Api/Services/Finance/` — submission service, extraction / coding extensions, export, approval
- `src/server/api/Sprk.Bff.Api/Services/Signals/` — billing rule types, schemas, describer entries, `InvoiceComplianceEvaluator` (shared with ontology-r1 after PR #1111)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` — submission processing, rule drafting, indexing jobs
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvoiceAi.cs` (+ `InvoiceAi.cs`, `NullInvoiceAi.cs`) — facade reshaped to capability methods (read invoice, classify, code lines, draft rules, embed); by-name playbook lookup and raw completion removed (D-52)
- `src/server/api/Sprk.Bff.Api/Services/Ai/` — Content Understanding client behind the facade; `PublicContracts/ConsumerTypes.cs` — new consumer-type keys
- `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceAnalysisService.cs`, `Services/Finance/Models/FinanceJsonSchemas.cs`, `Configuration/FinanceOptions.cs` — legacy classification / extraction prompts, C# schemas and deployment names retired
- Content Understanding analyzer definition + deploy script (proposed: `infrastructure/content-understanding/sprk-invoice-v1.json`, `scripts/content-understanding/Deploy-Analyzers.ps1`)
- Action JPS definitions and Bindings (Dataverse `sprk_analysisaction` / `sprk_playbookconsumer`), output-schema mirrors in `infra/dataverse/outputschemas/`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` — registrations
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` — invoice / line derivation (uac-r2 review)
- `src/client/external-spa/` — Invoices widget submit, progress, result, invoice detail, report
- `src/solutions/CreateInvoiceWizard/` + shared wizard component in `src/client/shared/Spaarke.UI.Components/`
- `src/solutions/` — new Invoice Review and Rule Review code pages; `sprk_invoicespage` views
- `src/dataverse/solutions/SpaarkeMaster/Entities/` — schema (see FR-04 – FR-16)
- `infrastructure/ai-search/spaarke-invoices-index.json`, `scripts/ai-search/Deploy-AllIndexes.ps1` — invoice index
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — Procedures and e-billing container IDs, eBilling options, feature flag
- `projects/spaarke-ontology-platform-r1` — notification only (status change, schema PRs)

## Glossary (binding)

| Term | Meaning |
|---|---|
| Invoice | Header record (`sprk_invoice`); unit of submission, approval and payment |
| Invoice Line | `sprk_billingevent` (display "Invoice Line"); unit of spend and review; types Fee, Expense, Adjustment |
| Invoice-level adjustment | Client-applied Adjustment line on the invoice: percent of fees after line adjustments, or fixed amount, with reason and optional rule |
| Core record | Matter, Project or Work Assignment (extensible); the **line's** core record is authoritative |
| Submission | One attempt (`sprk_invoicesubmission`), tracking number `{firm invoice number}.{n}` |
| Finding | Automated result on an invoice or line, persisted as `sprk_signal`, citing policy, version and clause |
| Line review outcome | Accept · Reduce · Disallow |
| Approval portion | Invoice × core record (`sprk_invoiceapproval`); one per invoice in R1 |
| Eligible approvers | Users in the assignment fields selected by the core record's `sprk_billingapproverrole` |
| Rule set | Published group of rules from one source document (`sprk_ruleset`) |

**Invariants**
- **I-1** Approved total = billed total − Σ line adjustments − Σ invoice-level adjustments; computed server-side only.
- **I-2** An invoice cannot be approved while any line is Pending or Flagged.
- **I-3** Payment is invoice-level, always the full approved total, one payment; never per line, never reopened.
- **I-4** Submissions and invoices are Rejected; lines are Disallowed.
- **I-5** An AI-observation finding never rejects a submission.
- **I-6** Invoice lifecycle is `statecode` / `statuscode` only (no separate payment status).

## Processing Pipeline (binding — D-46 – D-53)

One submission runs these steps inside the `InvoiceSubmissionProcessing` job (FR-20). AI steps are marked **AI**; everything else is deterministic code.

| Step | What happens | Component | Output |
|---|---|---|---|
| 0 Intake | Validate caller / firm / core record; store PDF; create `sprk_document` + `sprk_invoicesubmission`; enqueue | Submission operation (FR-17) | Submission, Received |
| 1 Read + extract (**AI**) | One analyze call to the custom analyzer: header fields, Fee time-entry table, Expense table, Timekeeper summary — every field with confidence and page / bounding box | Content Understanding custom analyzer `sprk_invoice_vN` (gpt-5.2) via `IInvoiceAi.ReadInvoiceAsync` (FR-21) | Extracted invoice (typed) |
| 2 Check & resolve | Arithmetic (hours × rate = amount; lines → section totals → total due); timekeeper roster match (exact → normalized → AI-assisted); rate card in force per line date; core record and firm from submission context; **combined confidence** per field (D-47) | `Services/Finance` resolvers (FR-23) | Resolved lines + confidence |
| 3 Code & observe (**AI**) | Lines in chunks (default 25) to the coding Action: UTBMS task / activity / expense from the core record's code set (closed list), confidence, block-billing / vague-description observations with quoted text | Action `INVOICE-LINE-CODE@v1` (gpt-5.4-mini, low effort) via `IInvoiceAi.CodeLinesAsync` (FR-22) | Codes + observations |
| 4 Evaluate | Every published rule in force, in memory | `InvoiceComplianceEvaluator` (FR-25) | Findings |
| 5 Decide | Rejection rule (FR-26): Reject consequence ∧ every field read ≥ threshold ∧ reject-eligible type | Submission service | Accepted / Rejected |
| 6 Persist | All-or-nothing: invoice and lines for **every submission that was read** (D-101): Accepted → In Review with approval portion and Signals; Rejected → invoice created Rejected, with findings + report; blocked (internal, awaiting Override / Return) → In Review, held. A submission that fails before it is read has no invoice | Submission service (FR-20) | Records |
| 7 After | Index, notifications, PDF report | Indexing job, notification spine, report (FR-39, FR-40, FR-43) | — |

Rules for the AI steps:
- Extraction (step 1) and coding (step 3) are separate, separately measured (D-46).
- Every AI step except Content Understanding runs as an **Action + Binding** through `IActionRunner` behind the `IInvoiceAi` facade (ADR-013, ADR-039; reference `PublicContracts/CommunicationTriageAi.cs`). Model chosen by the Action's tier, with the Binding's `sprk_modeltieroverride`. No by-name playbook lookup, no playbook-description prompts, no C# output schemas, no `FinanceOptions` deployment names (D-52).
- The analyzer definition is the extraction "prompt": a versioned JSON file in the repo, deployed per environment by script; field descriptions carry label aliases ("Professional Services", "Fees", "Time Detail", "Disbursements", "Costs").
- Fallback if FR-03 rejects Content Understanding: step 1 becomes DI `prebuilt-layout` (existing `DocumentIntelligenceService`) + an extraction Action; steps 2–7 are unchanged.

## Requirements

### Functional Requirements

#### WP0 — Baseline

1. **FR-01**: Verify the existing Finance invoice pipeline end to end in dev (extraction playbook PB-013 configuration, `InvoiceExtractionJobHandler`, writes to `sprk_invoice` / `sprk_billingevent`, `InvoiceIndexingJobHandler`) and check dev matter finance rollups (ontology DI-69, #1663). — Acceptance: `notes/baseline-finance-pipeline.md` records pass/fail per step with evidence; every defect found is fixed in scope or filed as a GitHub issue and listed.
2. **FR-02**: Assemble the evaluation set from public sources per `notes/research/test-data-sources.md`. — Acceptance: a manifest lists ≥ 6 fee applications, ≥ 150 synthetic invoices with ground truth, ≥ 10 real invoices, ≥ 15 OCGs, ≥ 5 rate cards, ≥ 300 seeded-violation and ≥ 300 clean invoices, each with source, licence and PII note; no item with a non-commercial or research-only licence is included.
3. **FR-03**: Extraction spike on ≥ 30 invoices from the evaluation set (including the largest available and ≥ 3 bundled PDFs, e.g. cover letter + invoice), three arms: (a) current text + LLM extraction, (b) Content Understanding `prebuilt-invoice` as-is, (c) the custom analyzer (D-49) on GA `2025-11-01`; plus arm (c′) the same analyzer on `2026-06-01-preview` with segment confidence and preview labeled-sample training (D-53), and a probe whether a preview-built analyzer runs under the GA API; plus a coding comparison gpt-5.4-mini vs gpt-5.6-luna, and an experiment with the 28-value activity code as an analyzer `classify` field. Also measures the `Azure.AI.ContentUnderstanding` publish-size delta and the preview-API features selected under D-53. — Acceptance: per-arm header and line-field accuracy, confidence calibration (fields ≥ 0.90 that were wrong), latency p90 and cost per invoice in `notes/extraction-spike.md`; a go / no-go decision for Content Understanding recorded (no-go → the DI layout fallback in Processing Pipeline).
3a. **FR-47**: Provision AI resources per environment (dev first): gpt-5.2 and gpt-5.4-mini deployments (and gpt-5.6-luna for the spike); Content Understanding model defaults on `spaarke-docintel-invoices-dev`; analyzer deploy script (create / replace `sprk_invoice_vN` from the repo JSON, API version pinned to GA `2025-11-01`, D-53) runnable in the deployment pipeline; managed-identity access from the BFF; config keys in `appsettings.template.json`. — Acceptance: script is idempotent (second run makes no change); BFF reaches the analyzer with managed identity, no key; a missing analyzer or deployment fails health check with a stable error code, not a runtime crash.

#### WP1 — Data model

4. **FR-04**: `sprk_billingevent` becomes the Invoice Line: display name "Invoice Line"; add line type (Fee / Expense / Adjustment), timekeeper lookup + timekeeper-name-as-billed, approved rate, approved amount, adjustment reason, reviewed by / on, line status (Pending, Clean, Flagged, Accepted, Reduced, Disallowed), UTBMS task / activity / expense code lookups plus codes-as-billed text and coding confidence, extraction confidence (JSON per field with source page), adjustment method (Percent / Fixed) and value for Adjustment lines. — Acceptance: columns in solution source; `sprk_invoicelineitem` removed after a check shows zero rows and zero code references (negative: removal blocked if any reference remains).
5. **FR-05**: Core-record relationships per ADR-024 on `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`: entity-specific lookups for Matter, Project and Work Assignment plus all five resolver fields; core ancestor stamped through `CoreAncestorResolver` on server writes. — Acceptance: exactly one entity-specific lookup populated per record; a write populating two is refused; resolver fields always complete; uac-r2 review recorded.
6. **FR-06**: Invoice lifecycle on `statecode` / `statuscode`: Active — In Review, Approved, Sent to AP; Inactive — Paid, Rejected, Returned to Firm, Cancelled. Allowed transitions: In Review → Approved | Rejected | Returned to Firm | Cancelled; Approved → Sent to AP | Cancelled; Sent to AP → Paid. Retire `sprk_invoicestatus`: migrate existing values (ToReview → In Review; Reviewed → In Review), update every reader, and notify ontology-r1 when the PR opens. Readers (per ontology-r1, on `docs/ontology-platform-design`, plus this repo's grep):
   - Ontology-owned (ontology-r1 reviews): `Services/Finance/FinanceBaseValues.cs` (`InvoiceStatus` :39, `InvoiceStatusReviewed` :53, `CountedInvoiceStatuses` :56, FetchXML `in` condition :173-174 — the single definition of counted spend) → change `CountedInvoiceStatuses` to the `statuscode` values In Review, Approved, Sent to AP, Paid and filter on **`statuscode`, not `statecode`** (Paid is Inactive); `Services/Finance/FinanceRollupService.cs` :47, :241-248; tests `tests/unit/domain/Signals/SignalReevaluationJobTests.cs` :2200, :2664-2670, `tests/integration/seam/Signals/FinanceBaseValuesSeamTests.cs` :36, :106 (live seam asserting the option set), `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceBaseValuesTests.cs` :24-42. This supersedes ontology D-137 Q24 ("spend counts only Reviewed").
   - Other: `Services/Finance/FinanceSummaryService.cs` :95, :386, :410; `Services/Finance/InvoiceReviewService.cs` :324-336, :843 (writes ToReview on create; uac-r2 last touched it); `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs` :172.
   - Dataverse solution: `OptionSets/sprk_invoicestatus.xml`, `Entities/sprk_Invoice` saved queries `{b9f6d045-…}`, `{1ad5e8f3-…}`, quick-create form `{c5e5807d-…}`, main form `{93aa1c69-…}`, `Other/Solution.xml`. No client code (`src/client`, `src/solutions`) reads it.
   - Docs: `docs/data-model/field-mapping-reference.md:156`, `sprk_invoice-matter-project.md`, `sprk_financial-related-entities.md`, `schema-corrections.md:68`, `.claude/skills/jps-action-create/examples/ACT-004.json:95`.
   — Acceptance: server enforces the transition matrix (test covers every allowed and a sample of disallowed transitions, e.g. Paid → In Review refused); grep finds no `sprk_invoicestatus` reader in code, forms, views or docs; the three test files and the live seam updated; PR lists every reader above; ontology-r1 notified when the PR opens and reviews the Ontology-owned files.
7. **FR-07**: Invoice amounts: billed total (`sprk_totalamount`), fees total, expenses total, line-adjustments total, invoice-adjustments total, approved total, all computed server-side per I-1. — Acceptance: approved total recalculates on every line or adjustment change; a client attempt to write approved total directly is refused; percent adjustment uses fees after line adjustments (test with fees + expenses shows expenses excluded).
8. **FR-08**: Reference entities with admin forms and views: `sprk_timekeeper` — **extend the existing stub entity** (today: name, contact, invoice and invoice-line-item lookups; review and drop the obsolete lookups) with (firm, name, classification, status Approved / Pending / Inactive, effective dates, linked external contact), `sprk_ratecard` + `sprk_ratecardrate` (firm, optional core-record scope, source document, status Draft / Published, effective dates; rate per timekeeper or classification with approved and rack rate, effective dates), `sprk_billingcodeset` (name, body e.g. ABA / LOC, version, source URL, status) + `sprk_billingcode` (code set, code type Task / Activity / Expense, code, phase, description, **guidance text** read by the coding Action, status); code-set mapping: a default-code-set lookup on `sprk_mattertype_ref` and `sprk_practicearea_ref` (Matter, Project and Work Assignment all carry `sprk_mattertype` / `sprk_practicearea` lookups — verified in solution source 2026-10-15). Seed files in the repo (one CSV per set with version and source URL, from utbms.com — D-32) loaded by an idempotent script. — Acceptance: overlapping effective-date ranges for the same timekeeper / classification on one card are refused; UTBMS litigation, counseling, project, bankruptcy, IP patent, trademark, activity and expense code sets seeded with version; re-running the seed changes nothing; an admin edit to guidance text is not overwritten by a re-seed.
8a. **FR-48**: eBilling settings record (D-59): a singleton table with admin form, auditing on, holding default approver roles per core-record type (seeded Assigned Attorney 1 + Assigned To Internal for Matter, Project and Work Assignment — D-54), reliability threshold (0.90), combined-confidence weights and caps (D-47), related-work date window (± 7 days), coding chunk size (25). Read by the BFF with a short cache. — Acceptance: a second settings row is refused; a changed threshold takes effect on the next submission without a deployment; values outside their valid range are refused on save; every change is in the audit history.
9. **FR-09**: Policy store extensions (shared with ontology-r1): `sprk_policy` / `sprk_policyversion` gain domain (Billing, Ontology, reserved Docketing), rule set lookup, clause reference (quote + location), consequence; new `sprk_ruleset` (domain, name, source document, scope client-default or firm, status, effective dates). — Acceptance: schema in solution source; ontology-r1 review recorded.
10. **FR-10**: `sprk_invoicesubmission` with tracking number `{firm invoice number}.{n}`, channel (SPA, Internal; reserved Mailbox, Outlook, API), submitter, firm, core record, document, processing status (Received, Processing, Accepted, Rejected, Failed), findings JSON for rejections, report document, resulting invoice, resubmission-of. — Acceptance: second submission of INV-002 gets INV-002.2; numbering is concurrency-safe (two parallel submissions never share a number).
11. **FR-11**: `sprk_invoiceapproval` (invoice, core record, status Pending / Approved / Rejected / Returned, approved amount, decision record, eligible approvers resolved at acceptance). — Acceptance: one portion per invoice in R1; eligible approvers re-resolved when the core record's assignment fields or `sprk_billingapproverrole` change before a decision.
12. **FR-12**: `sprk_externalidentifier` (regarding per ADR-024, identifier type, value, issuing organization / system). Types in R1: firm invoice number, firm client-matter number, law-department matter number, PO as quoted; reserved: AP document number, VAT invoice number, e-invoice / clearance ID, Peppol message ID. — Acceptance: a firm-quoted client-matter number resolves to its core record for that firm only.
13. **FR-13**: `sprk_billingexport` (created by / on, invoice count, total, format, file document, status) and invoice lookup to its batch. — Acceptance: schema in solution source.
14. **FR-14**: Bring `sprk_billingapproverrole` (multi-select, global choice) on Matter, Project and Work Assignment into solution source; an empty value means the environment default from the settings record (FR-48) — no record-creation path is changed. Add a firm billing-contact lookup on `sprk_organization`. — Acceptance: both in SpaarkeMaster source; existing dev values preserved; a record with an empty field resolves approvers from the settings default for its type (test per core type).
15. **FR-15**: Signal / Decision Record changes agreed with ontology-r1 (owner approved, D-31): `sprk_regardinginvoiceline` lookup on `sprk_signal` and `sprk_decisionrecord`; per-line dedupe key with existing keys unchanged; `SignalWriter.RegardingLookupByEntity` extended through `TodoRegardingBuilder` / `sprk_recordtype_ref`; invoice and line subjects derive `sprk_corerecordtype` / `sprk_corerecordid` for every core type (stamp follows the subject); new lookup registered with `SecureChildLineage` and census ArchTests; firm-visible reason text. — Acceptance: `SignalPairTypeSchemaAgreementTests` and uac-r2 census tests green; an invoice-line Signal on a Project-scoped invoice carries core type Project (negative: not Matter); ontology-r1 and uac-r2 reviews recorded.
16. **FR-16**: Containers (D-55): **Procedure Library** and **E-Billing Output Files**, instances of an existing container type (dev: *Spaarke PAYGO 1* `8a6ce34c-6055-4681-8f87-2f4f9f921c06`; dev containers already created by the owner — IDs in `notes/decisions-log.md` D-55). Add their IDs as configuration keys alongside `EmailProcessing:DefaultContainerId` / `Communication:ArchiveContainerId`, and add both containers to customer provisioning (coordinate with customer-provisioning-orchestration). Procedure Library documents are internal only (D-63) and go through the standard document pipeline into `spaarke-files-index` / `spaarke-discovery-index` (filterable by `containerId` for the help system); export files are documents but are not AI-indexed. — Acceptance: provisioning step repeatable per environment; guideline, rate-card and procedure documents land in Procedure Library; export files land in E-Billing Output Files; invoice PDFs land in the general container (or the secure-record container for secure core records); no CIAM (firm) user can open or search a Procedure Library document (negative test); an export file never appears in a search result.

#### WP2 — Submission and extraction

17. **FR-17**: One server-side submission operation behind two entry points (external SPA route, internal route): validates caller, firm and core-record access; uploads the PDF through `SpeFileStore`; creates `sprk_document` (type Invoice) and `sprk_invoicesubmission`; enqueues processing; returns `202` with job id and status URL. — Acceptance: external caller submitting for a core record they cannot access → 403; for another firm → 403; non-PDF → 400 ProblemDetails with stable error code; file over the configured size limit → 413; no client path creates `sprk_invoice` directly.
18. **FR-18**: SPA submission UI in the Invoices widget: choose an accessible core record, upload one PDF, firm invoice number (required, D-91) and optional PO number; progress from job status; result screen showing Accepted (tracking number) or Rejected with the on-screen report (per finding: line, values read, rule, clause) and PDF download. — Acceptance: leaving the page and returning shows the same result; Fluent v9; works at phone width.
19. **FR-19**: `CreateInvoiceWizard` rewired to the internal entry point, sharing the wizard component with the SPA via `@spaarke/ui-components`; internal user selects firm and core record; on a blocking failure the user chooses Override (reason required, requires the eBilling Override privilege) or Return to firm. — Acceptance: user without the privilege sees no Override option and the server refuses an override call (403); override writes a Decision Record.
20. **FR-20**: `InvoiceSubmissionProcessing` job (ADR-004): extract → resolve → code + observe → evaluate → persist outcome. Every submission that was read creates its invoice and lines (D-101, amended 2026-10-17). Accepted: invoice In Review, lines, approval portion, Signals, invoice link on the document; enqueue indexing; notify eligible approvers. Rejected: invoice created with status Rejected (lifecycle service), lines, findings and report linked to the submission and the invoice; notify firm. Blocked (internal, awaiting Override / Return to firm): invoice In Review, held from approval until decided. Failure: status Failed, retryable, no partial invoice. — Acceptance: idempotent per submission id (re-delivery creates no duplicates); a failure after line creation leaves no invoice (all-or-nothing); job status transitions persisted (ADR-017).
21. **FR-21**: Extraction through the Content Understanding custom analyzer `sprk_invoice_vN` (D-49), called by `IInvoiceAi.ReadInvoiceAsync`: header (firm, firm invoice number, invoice date, billing period, firm matter reference, PO as quoted, fees / expenses / adjustments / total due, currency) and lines (date, timekeeper as billed, title / classification as billed, hours, rate, amount, narrative, codes as billed, line type) from Fee, Expense and Timekeeper-summary tables, each field with confidence and page / bounding box stored in the line's extraction-confidence JSON (FR-04). Replaces `InvoiceAnalysisService.ExtractInvoiceFactsAsync` and the extraction `FinanceJsonSchemas`, which are removed with their callers moved (D-52). SPA and internal submissions skip attachment classification. — Acceptance: meets NFR-04 on the evaluation set; currency other than USD → finding (RequiredData) not a crash; a document the analyzer cannot read (encrypted, > page limit, no invoice found) → submission Failed or Rejected with a stable reason code, never a partial invoice; grep finds no remaining caller of `ExtractInvoiceFactsAsync` or `GetPlaybookByNameAsync`.
22. **FR-22**: UTBMS coding and AI observations as the Action `INVOICE-LINE-CODE@v1` (consumer type `invoice-line-code`, Binding, default tier per D-51) called by `IInvoiceAi.CodeLinesAsync` in line chunks (size from FR-48): per line, task / activity / expense code chosen from the closed code tables for the line's code set, with confidence. The code set is resolved per line in this order: (1) the set implied by firm-supplied codes on the invoice, (2) the core record's matter-type mapping, (3) its practice-area mapping, (4) an AI suggestion, flagged as a finding for review, (5) the settings default (Litigation). Each code's description and guidance text go to the model with the code list. An admin action asks the AI to propose code-set mappings for unmapped matter types / practice areas; the admin confirms each (no mapping is applied unconfirmed). No retrieved examples in R1 (D-64); observations block billing and vague description with confidence and quoted text; firm-supplied codes retained, mismatch → finding. The allowed codes reach the model as a closed enum (`$choices` enrichment if it can filter by code set — verify in the task; otherwise the code list is passed as input and the output enum set per call). — Acceptance: an output code not in the core record's code set is refused server-side even if the model returns it; meets NFR-05; the Action's output schema is mirrored in `infra/dataverse/outputschemas/`.
22a. **FR-46**: Migrate Finance attachment classification to the Action + Binding approach (D-52): Action `INVOICE-CLASSIFY@v1` (consumer type `invoice-classify`), called through `IInvoiceAi.ClassifyAsync` from `AttachmentClassificationJobHandler`; remove `GetPlaybookByNameAsync` / `GetStructuredCompletionAsync` from `IInvoiceAi` and the classification / extraction deployment names from `FinanceOptions`; keep `GenerateEmbeddingAsync`. — Acceptance: classification results on the existing Finance test set are no worse than before (before / after recorded); the facade's Null-Object peer covers every new method (kill switch, ADR-032); a missing Binding fails closed with a logged reason, not a fallback to a hard-coded prompt.
23. **FR-23**: Resolution: extracted timekeeper names matched to the firm's roster (exact, then normalized, then AI-assisted with confidence); unmatched timekeeper → ApprovedTimekeeper finding; firm and core record from the submission context; rate card in force on each line date. — Acceptance: an unmatched name never silently maps to a different timekeeper below the reliability threshold.

#### WP3 — Rules

24. **FR-24**: Eleven billing rule types (ApprovedTimekeeper, ApprovedRate, TimeIncrement, DailyHoursCap, NonBillableActivity, ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate, RequiredData, AiObservation), each with a JSON Schema, a describer entry, validator handling and a handler, in the shared closed `RuleType` set grouped by domain. Every rule-body consumer refuses a type it cannot evaluate, fail-closed (including the Ontology `PredicateCompiler` / evaluator, the describer and save-time validation), following ontology task 061 / PR #1869. — Acceptance: per type, must-fire and must-not-fire tests; a billing rule body passed to the Ontology compiler is refused, never treated as true; no data-driven type registry; no Power Fx or expression language.
25. **FR-25**: `InvoiceComplianceEvaluator` (in-memory strategy): evaluates every published rule in force on each line's date for the line's core record and firm (firm rule set overrides client default for the same rule type); computes suggested adjustments; emits findings with policy, version, clause and the values read. — Acceptance: deterministic (same input → same findings); evaluation of a 150-line invoice within NFR-01's budget; findings written through `SignalWriter`.
26. **FR-26**: Rejection rule: a submission is rejected only if a rule with consequence Reject fails, every field it read has extraction confidence ≥ the reliability threshold (default 0.90, configurable), and the type is reject-eligible (1, 2, 6–10). Otherwise accepted with a finding. — Acceptance: tests for each condition false; an AiObservation rule configured as Reject is refused at publish.
27. **FR-27**: Rule drafting: the Action `BILLING-RULE-DRAFT@v1` (consumer type `billing-rule-draft`, Binding; Standard tier, Reasoning if the evaluation needs it) reads a guideline / rate / procedure document and proposes rules only from the billing catalogue, each with parameters, consequence and clause quote + location; unsupported text listed as "not converted". Called through `IInvoiceAi.DraftRulesAsync`; runs as a job (ADR-004). — Acceptance: output schema-validated against the rule-type schemas (ADR-039); a proposal for an unknown type is dropped and listed; the Action is versioned by its `@vN` code (ADR-015).
28. **FR-28**: Rule Review code page: proposed rule beside its clause; admin edits, rejects or accepts; publish runs `PolicyVersionValidator`; published versions immutable with effective date; rule set assigned to client default or a firm; rule set "visible to firms" flag (default on for billing guidelines and rate cards, off for internal procedures — D-66), shown to the admin at publish as "clause quotes will be visible to firms". — Acceptance: editing a published version creates a new version; non-admin cannot publish (403); a rule from a rule set with the flag off never shows its clause text to a firm (its findings show rule name and reason only).
29. **FR-29**: Rate card and timekeeper import from a spreadsheet template (Draft → Published), keeping the source document in the Procedures container. — Acceptance: invalid rows reported per row; nothing published until the admin publishes.
30. **FR-30**: Billing actions in `DecisionActionCatalog` (line accept / reduce / disallow; invoice approve / reject / return; override), each with a `PlanLane`; every billing decision, human or not, is recorded through the additive writer entry `DecisionRecordWriter.WriteBillingDecisionAsync` (idempotent by decision id; amended by D-100, §6.5 path B, 2026-10-17), filed under the line or the invoice (D-75, D-101). — Acceptance: ontology-r1 review recorded; every approve / reject / return / override produces exactly one Decision Record.

#### WP4 — Review and approval

31. **FR-31**: Invoice Review code page: header and totals; lines grid with status, findings (rule, version, clause), suggested adjustment; per-line Accept / Reduce (approved amount + reason) / Disallow (reason); bulk accept of Clean lines; correction of misread extracted values and AI codes (audited, re-evaluates affected rules). Every confirmation and correction is also stored in structured form (D-64): field, AI value, final value, AI confidence, Action / analyzer code + version, outcome type (Corrected, Confirmed individually, Accepted in bulk, Unreviewed), reviewer and role, when. Only Corrected and Confirmed-individually outcomes count as labels; bulk acceptance is recorded but is not a confirmation (prevents the AI learning its own errors — `projects/spaarkeai-tuning-and-feedback-r1/notes/research/market-ai-tuning-and-feedback.md`). UI per `notes/review-screen-prototype-brief.md` (prototype first, D-58). — Acceptance: reason mandatory for Reduce / Disallow; a correction is attributable and visible in history; a correction is distinguishable from a Reduce (one says the reading was wrong, the other that the bill is wrong); the stored corrections can be queried per Action version (feeds the D-47 calibration report).
32. **FR-32**: Invoice-level adjustments by the reviewer: add Adjustment lines (percent of fees after line adjustments, or fixed amount) with reason and optional rule. — Acceptance: totals per FR-07; an adjustment that would make the approved total negative is refused.
33. **FR-33**: Context panel (deterministic): core-record status and dates, budget vs spend (existing rollups), prior invoices from this firm on this core record, this firm's disallow history, timekeeper rate history. — Acceptance: data loads within NFR-02; panel degrades with a message, not an error, when a source is unavailable.
34. **FR-34**: Related work product panel (on demand per line): documents on the line's core record within the line's date window (configurable ± days), authored by the line's timekeeper (via linked external contact), ranked by semantic similarity of the narrative using the files index filtered to the core record. Read-only. — Acceptance: never returns documents from another core record; returns an empty state when none match.
35. **FR-35**: Approval: eligible approvers resolved from the core record's `sprk_billingapproverrole`; the invoice appears in each eligible approver's "Invoices to review" view and in-app notification; any one approves (I-2 enforced); Decision Record records who decided; status → Approved; approved total frozen. — Acceptance: non-eligible user → 403; approve with a Flagged line refused; second approval attempt after the first is a no-op (idempotent).
36. **FR-36**: Whole-invoice Reject (final, reason required) and Return to Firm (reason required; invoice → Returned to Firm, Inactive; firm resubmits; the accepted resubmission creates a new invoice linked to the one it supersedes). — Acceptance: Returned / Rejected invoices excluded from rollups and the export; Duplicate rule ignores Rejected / Returned attempts for the same invoice number.
37. **FR-37**: Override (internal submission, D-24): accept a submission despite a blocking failure with a mandatory reason; requires the eBilling Override privilege; one person. — Acceptance: Decision Record written; the overridden finding remains visible to the reviewer.

#### WP5 — Firm feedback

38. **FR-38**: SPA invoice detail: tracking number, status, billed and approved totals, every Reduced / Disallowed line and invoice-level adjustment with reason, rule set name + version, section reference and quoted clause text (D-66), and the on-screen report; internal findings not shown before a decision. — Acceptance: a firm user sees only its own firm's invoices; Pending findings never appear.
38a. **FR-49**: SPA **Billing Guidelines** view (D-66): the published rules in force for the firm (client default + that firm's rule sets with "visible to firms" on), grouped by rule set, each with plain-language description (`RuleBodyDescriber`), parameters, clause quote, section reference and effective dates; generated from published policy versions, never from the source document. — Acceptance: a firm sees only client-default and its own rule sets; rule sets with the flag off never appear; no link to a Procedure Library document exists anywhere in the SPA.
39. **FR-39**: Notifications to the submitter and the firm's billing contact by email (shared mailbox, `CommunicationService`) and SPA notification feed (notification spine, ADR-047) on submission rejection, Return to Firm, whole-invoice Reject, and approval with adjustments; in-app notification to eligible approvers on acceptance. Email wording is drafted by FR-50. — Acceptance: no email to the firm when an invoice is approved with no adjustments (SPA status only); missing billing contact → submitter only, logged.
39a. **FR-50**: Firm notice drafting (D-60): Action `INVOICE-FIRM-NOTICE@v1` (consumer type `invoice-firm-notice`, Binding) writes subject and explanatory prose from the structured findings and outcome; amounts, line references, clause quotes and the findings table are inserted from data by the template, not by the model; for Return to Firm and whole-invoice Reject the reviewer sees and can edit the draft before sending; in-session automatic rejections send the draft directly; if the Action fails or is disabled, a fixed template is used. — Acceptance: the model's output schema contains no numeric fields; a draft mentioning a line or amount not in the findings is impossible by construction (prose only); fallback template test; the sent text is stored with the submission / invoice.
40. **FR-40**: Adjustment report generated as a document and converted to PDF (existing Graph conversion), stored with the submission or invoice and attached to the email; same content as the on-screen report. — Acceptance: report lists every finding / adjustment shown on screen, nothing internal.

#### WP6 — Export and index

41. **FR-41**: Export batch: a finance user selects Approved, not-yet-exported invoices; the system produces CSV and JSON from a configurable field mapping, stores the file as a Document in the E-Billing Output Files container, and moves invoices to Sent to AP. Default mapping = the D-56 column set (one row per invoice; JSON adds `lines[]`); exported values are snapshotted with the batch. — Acceptance: an invoice cannot be in two batches; Returned / Rejected / In Review invoices cannot be selected; mapping change needs no code change; a later edit to the core record or firm does not change a past batch file.
42. **FR-42**: Mark Paid manually (finance role) from Sent to AP. — Acceptance: only Sent to AP → Paid allowed.
43. **FR-43**: Invoice AI Search index built from structured invoice and line records (firm, core record, timekeeper, UTBMS codes, amounts, status, narrative) on acceptance and on status / approval changes; invoice PDFs follow the standard document pipeline into the files index. — Acceptance: an accepted invoice is searchable by firm, core record and UTBMS code within NFR-03; rejected submissions are not in the invoice index; index tenant-filtered.

#### WP7 — Evaluation and rollout

44. **FR-44**: Measure extraction, coding and rejection precision against NFR-04 – NFR-06 on the evaluation set; tune; UAT with the owner. — Acceptance: results in `notes/evaluation-results.md`; any target missed has an owner decision recorded.
45. **FR-45**: eBilling behind a feature flag (ADR-018) with null-object fallbacks (ADR-032); enabled in dev first. — Acceptance: flag off → endpoints return a stable disabled error code and the SPA hides submission.

### Non-Functional Requirements

- **NFR-01**: In-session result p90 ≤ 2 minutes for invoices ≤ 20 pages / ≤ 150 lines; the user may leave and is notified.
- **NFR-02**: Review page and panels load in ≤ 3 s p90 for invoices ≤ 150 lines.
- **NFR-03**: Accepted invoice appears in the invoice index within 5 minutes.
- **NFR-04**: Extraction accuracy on the evaluation set: header fields ≥ 95%; line date, timekeeper, hours, rate, amount ≥ 90%.
- **NFR-05**: UTBMS task code top-1 accuracy ≥ 80%; low-confidence codes flagged, not silently used by rules.
- **NFR-06**: Zero submission rejections caused by misread values on the evaluation set.
- **NFR-07**: Volume: ~100–200 submissions per environment per month, month-end peaks ×5; processing via Service Bus absorbs peaks.
- **NFR-08**: All background handlers idempotent per unit of work; all status transitions persisted.
- **NFR-09**: Every line change, adjustment, override, approval, rejection and return attributable and timestamped.
- **NFR-10**: External users see only their firm's submissions and invoices and only for core records they can access; firm-visible text contains only facts about that firm's invoice.
- **NFR-11**: AI: minimum text sent; logs carry identifiers, sizes, timings and outcome codes only; Actions and the analyzer definition versioned; privileged narratives flagged on Signals / Decision Records (ADR-015); no model retiring before 2027-06-30 is used in production (D-51).
- **NFR-12**: One new NuGet package only — `Azure.AI.ContentUnderstanding` (D-50); no new npm packages; no licensed or fee-bearing dependencies (D-29); BFF publish-size delta < 1 MB for the project excluding that package, with the package's delta measured in FR-03 and reported per task against a fresh master build.
- **NFR-13**: UI uses Fluent v9 and shared components; SPA screens work at phone width.

## Technical Constraints

### Applicable ADRs

- **ADR-001** Minimal API; ProblemDetails; no endpoints in Functions.
- **ADR-002** No plugins; server-side owner for every invariant (submission, invoice creation, totals, transitions, approval).
- **ADR-003 / ADR-008 / ADR-028** Endpoint-filter authorization; CIAM users scoped to firm and accessible core records.
- **ADR-004 / ADR-017 / ADR-036 / ADR-052** Queue work as `IJobHandler`; job status; no hand-rolled timers; placement below.
- **ADR-007** All SPE operations through `SpeFileStore`.
- **ADR-010** Registrations inside `AddFinanceModule`; Options with `ValidateOnStart()`.
- **ADR-012 / ADR-021** Shared components; Fluent v9.
- **ADR-013** AI through `PublicContracts` (`IInvoiceAi`, reshaped to capability methods).
- **ADR-043** (Proposed) AI capability execution spine — Actions run through `IActionRunner`; a dispatch change needs a `tests/integration/seam/**` vertical-slice test.
- **ADR-015** AI data governance.
- **ADR-018 / ADR-032** Feature flag, null-object kill switch.
- **ADR-019** ProblemDetails with stable error codes and correlation id.
- **ADR-024** Dual-field polymorphic core-record relationships.
- **ADR-034** Core-ancestor stamping for child-inheritance access.
- **ADR-038** Testing strategy (`docs/adr/ADR-038-testing-strategy.md`).
- **ADR-039** Closed catalogues and schema-validated AI output; AI findings advisory; the Binding table is the only routing surface for AI capabilities.
- **ADR-047** Notification spine.

### MUST Rules

- ✅ MUST route every invoice creation through the server-side submission operation; no client creates `sprk_invoice` or lines directly.
- ✅ MUST compute totals and enforce status transitions server-side.
- ✅ MUST populate exactly one core-record lookup plus all five resolver fields (ADR-024).
- ✅ MUST return `202` + job id for submission; persist job status transitions (ADR-017).
- ✅ MUST access SPE only through `SpeFileStore`; MUST NOT create a new SPE container type.
- ✅ MUST evaluate rules only from published, immutable policy versions; every finding cites policy, version and clause.
- ✅ MUST refuse unknown rule types fail-closed in every consumer.
- ✅ MUST keep AI output schema-validated against closed catalogues (codes, rule types).
- ✅ MUST run every prompted AI step as an Action + Binding through `IActionRunner` behind `IInvoiceAi`; MUST select models by tier.
- ❌ MUST NOT look up playbooks by name, use a playbook description as a prompt, keep AI output schemas in C#, or name model deployments in `FinanceOptions`.
- ❌ MUST NOT land new capability on the frozen multi-node playbook engine.
- ❌ MUST NOT reject a submission on an AI observation or on low-confidence inputs.
- ❌ MUST NOT add Power Fx or any expression / low-code rule language.
- ❌ MUST NOT add plugins, Dataverse business rules or Power Automate for invariants.
- ❌ MUST NOT copy Ontology Signals code; co-build from `docs/ontology-platform-design` until PR #1111 merges.

### Existing Patterns to Follow

- Finance pipeline (being migrated, not copied): `Services/Finance/InvoiceAnalysisService.cs`, `InvoiceReviewService.cs`, `Jobs/Handlers/InvoiceExtractionJobHandler.cs`, `Jobs/Handlers/AttachmentClassificationJobHandler.cs`, `Infrastructure/DI/FinanceModule.cs`, `docs/architecture/finance-intelligence-architecture.md`.
- AI capability wiring (copy this): `docs/guides/ai-guide-consumer-wiring.md`; facade `Services/Ai/PublicContracts/CommunicationTriageAi.cs:70-123` (+ `NullCommunicationTriageAi.cs`); `Services/Ai/LinearConsumers/IActionRunner.cs`, `ActionRunner.cs` (`$choices` enrichment :424); `LinearConsumers/ModelTierDeploymentResolver.cs`; JPS exemplar `.claude/skills/jps-action-create/examples/document-profiler.json`.
- Document Intelligence fallback: `Services/Ai/DocumentIntelligenceService.cs` (`ParseDocumentLayoutAsync`), `PublicContracts/DocumentLayout.cs`; client construction (key or managed identity) `TextExtractorService.cs:103-127`.
- External upload: `src/client/external-spa/src/pages/DocumentUploadPage.tsx`, `Api/ExternalAccess/`.
- Signals / policy (after #1111): `Services/Signals/` (`RuleType`, `RuleBodySchemaValidator`, `PolicyVersionValidator`, `RuleBodyDescriber`, `SignalWriter`, `Actions/DecisionActionCatalog.cs`).
- Write path: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `Services/Dataverse/CoreAncestorResolver.cs`.
- Index: `docs/architecture/AI-SEARCH-INDEX-CATALOG.md`, `Jobs/Handlers/InvoiceIndexingJobHandler.cs`.
- Notifications: `docs/architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md`, `Services/Communication/CommunicationService.cs`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff-api>YES — Finance module endpoints (submission, review, approval, export, rules), job handlers, IInvoiceAi reshape + Content Understanding client + new Actions / consumer types (Finance AI migration, D-52), `Azure.AI.ContentUnderstanding` package, Services/Signals billing rule types and InvoiceComplianceEvaluator (shared with ontology-r1), CoreAncestorResolver derivation (uac-r2 review), FinanceModule registrations</bff-api>
  <spaarke-ai>NO</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-CLAUDE-md>NO</root-CLAUDE-md>
</hot-path-declaration>
```

### Placement Justification (`.claude/constraints/bff-extensions.md`, ADR-052)

| Component | Host | Reason |
|---|---|---|
| Submission, review, approval, export, rule endpoints | BFF | B1 caller identity and user-attributed SPE upload; B4 live request; ADR-052 forbids user-facing endpoints in Functions |
| Submission processing, rule drafting, indexing jobs | BFF `IJobHandler` | B2 large share of BFF domain code (Finance AI, Signals, Dataverse); B3 low volume (NFR-07); queue absorbs month-end peaks; tie-breaker fewer moving parts |
| Notifications | Existing spine | Reuse |

Size: one new package, `Azure.AI.ContentUnderstanding` (D-50; delta measured in FR-03 — ≥ +5 MB in one task needs explicit justification per CLAUDE.md §10); otherwise estimated < 1 MB compressed delta; the 60 MB ceiling applies per task. Boundary: AI via `IInvoiceAi` facade only (Content Understanding client inside `Services/Ai`, not `Services/Finance`); DI inside `AddFinanceModule` and the existing AI facade registration; endpoint filters on every route.

### New Components (§11 three-question gate)

| New component | Existing overlap | Can extend instead? | Cost of doing nothing |
|---|---|---|---|
| `sprk_invoicesubmission` | `sprk_invoice` status | No — rejected attempts would become invoices and pollute rollups and Ontology facts | No audit of rejections / resubmissions; no `{invoice}.{n}` tracking |
| `sprk_timekeeper` (extend existing stub) | Stub entity in SpaarkeMaster (name, contact, invoice / line-item lookups) | Yes — extended, not new | ApprovedTimekeeper / ApprovedRate rules cannot run |
| `sprk_ratecard`, `sprk_ratecardrate` | none found | — | ApprovedRate cannot run |
| `sprk_billingcode` | none found | — | AI coding has no closed list; code rules cannot run |
| `sprk_billingcodeset` + default-code-set lookups on `sprk_mattertype_ref` / `sprk_practicearea_ref` | Code-set text on `sprk_billingcode` | No — a lookup target is needed for the mapping and for versioning a set as a unit | Coding cannot pick the right set per matter; every line coded against all sets |
| eBilling settings table (singleton) | `Configuration/*Options.cs` (appsettings); no Dataverse settings entity found | No — appsettings are deploy-time and not admin-editable or audited | Approver defaults, threshold and confidence weights need a deployment to change (owner requires tunable, D-47 / D-59) |
| Firm-notice Action `INVOICE-FIRM-NOTICE@v1` + Binding | Fixed email templates (`CommunicationService`) | Template remains the fallback and carries all data; the Action adds prose only | Owner requires AI-drafted wording (D-60) |
| SPA Billing Guidelines view + endpoint | `RuleBodyDescriber`; SPA invoice detail | Reuses the describer and published policy versions; new read endpoint only | Firms cannot see the rules they are measured against (D-66) |
| Structured correction record (shape decided in WP1 — columns on the line vs a child table) | Dataverse audit history | No — audit is not queryable per Action version or field | No calibration report (D-47); tuning platform has no history to load later (D-64) |
| `sprk_ruleset` | `sprk_policy` category / scope | No — a policy is one rule; a guideline publishes, scopes and cites as a unit | An OCG cannot be published or assigned as a unit |
| `sprk_invoiceapproval` | Decision Record gate fields | No — Decision Records are append-only history, not pending state with eligible approvers | Worklist cannot show whose decision it is; multi-matter later needs restructuring |
| `sprk_externalidentifier` | `sprk_invoicenumber` | No — many typed identifiers per record | Firm, law-department, AP and VAT numbers cannot be tracked |
| `sprk_billingexport` | none found | — | No record of what went to AP; double-payment risk |
| `sprk_organization` billing-contact lookup | `sprk_PrimaryContact1/2` | No — primary contacts are relationship contacts, not the billing recipient | Firm notifications have no defined recipient beyond the submitter |
| New columns on `sprk_invoice`, `sprk_billingevent`, `sprk_signal`, `sprk_decisionrecord`, `sprk_policy*` | Existing entities | Yes — extended, not new entities | See FR-04 – FR-15 |
| Procedures and e-billing container IDs (config) | `DefaultContainerId`, `ArchiveContainerId` | Yes — same pattern, new keys | Guidelines and exports mixed into the general container, hurting search and analysis (D-35) |
| `InvoiceSubmissionService` + endpoints + job | `InvoiceReviewService` (classification gate) | Reuses extraction; different trigger and outcome | No server-side submission owner |
| `InvoiceComplianceEvaluator` + billing rule handlers | Finance `SignalEvaluationService`; Ontology FetchXML evaluator | No — neither evaluates an invoice in hand (D-23) | No rule evaluation at submission |
| Rule-drafting Action `BILLING-RULE-DRAFT@v1` + Binding + job | NDA review playbook | No — needs closed-schema rule output | Admins hand-configure every rule |
| Coding Action `INVOICE-LINE-CODE@v1` + Binding | Legacy `FinanceExtraction` playbook (no coding) | No — new capability; separate from extraction (D-46) | Lines have no UTBMS codes; code rules and spend-by-phase cannot run |
| Classification Action `INVOICE-CLASSIFY@v1` + Binding | Legacy `FinanceClassification` playbook | Replaces it (migration, D-52) | Finance stays on the deprecated by-name lookup; two AI patterns for invoices |
| `IInvoiceAi` capability methods (`ReadInvoiceAsync`, `ClassifyAsync`, `CodeLinesAsync`, `DraftRulesAsync`) | `IInvoiceAi` (by-name playbook + raw completion) | Yes — same facade reshaped, not a new facade | Finance code keeps calling raw completion with C# prompts and schemas |
| Content Understanding client (inside `Services/Ai`) + `Azure.AI.ContentUnderstanding` package | `DocumentIntelligenceService` (`prebuilt-layout`) | No — DI layout has no time-entry fields or per-field confidence for generated values; CU is a different service and API | Per-field confidence and source location must be hand-built; DI invoice model cannot read timekeeper / narrative (research note §2) |
| Analyzer definition JSON + deploy script | Action rows (Dataverse) | No — the analyzer lives in the Content Understanding resource, not Dataverse | Analyzer is configured by hand in Studio per environment, unversioned |
| Review / approval / export / rule endpoints | `/api/finance/invoice-review/*` | Same module, new operations | Surfaces have no API |
| Invoice Review and Rule Review code pages | `sprk_invoicespage` (grid) | Grid remains the list; review needs a detail workspace | Reviewers cannot act per line |

## ADR Tensions (per CLAUDE.md §6.5)

| Rule | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| D-53 (project decision; ADR-013 AI platform posture) | "Build on GA Content Understanding `2025-11-01`; the preview is adopted only when it reaches GA" | The owner chose the `2026-06-01-preview` analyzer as primary (task 005 go / no-go, D-81): about 4 points more accurate on hard templates (day-month dates, hh:mm hours) | **A — project exception** | Owner decision 2026-10-15. Guard rails: a GA fallback analyzer kept in step and selectable by configuration; both measured in 161 / 162; REST client covers both API versions (no prerelease SDK); preview retirement / GA tracked as #1945; at GA the analyzer is re-created and re-tested. Cite in the PR; code-review approves explicitly. |

> Apart from the row above (added after design, 2026-10-15), no ADR tensions surfaced at design time. All listed ADRs apply without exception. Coordination items (not ADR conflicts): the billing rule types extend the Ontology's closed `RuleType` set under its CM-7 / CM-8 rule (agreed with ontology-r1); the `sprk_invoicestatus` retirement requires notifying ontology-r1. This section is re-checked by `/adr-check` during implementation.

## Success Criteria

1. [ ] A firm user submits a PDF invoice in the SPA and sees Accepted or Rejected with reasons within NFR-01 — Verify: UAT script + timing log.
2. [ ] A blocking failure on reliable inputs rejects; the same failure on low-confidence inputs is accepted and flagged — Verify: FR-26 tests + evaluation set (NFR-06).
3. [ ] An admin uploads an OCG and publishes a rule set where every rule cites its clause — Verify: UAT with ≥ 3 real OCGs from the evaluation set.
4. [ ] An eligible approver reviews lines, applies an invoice-level adjustment and approves; totals satisfy I-1; one Decision Record written — Verify: integration test + UAT.
5. [ ] Reject and Return to Firm work as specified; resubmission gets `{invoice}.{n+1}` and a new linked invoice — Verify: integration tests.
6. [ ] The firm sees adjustments and reasons on screen and in the PDF report and receives the email — Verify: UAT.
7. [ ] Approved invoices export to CSV and JSON in the e-billing container and move to Sent to AP — Verify: integration test.
8. [ ] Accuracy targets NFR-04 / NFR-05 met or owner-accepted — Verify: `notes/evaluation-results.md`.
9. [ ] Signal / Decision Record schema changes merged with ontology-r1 and uac-r2 reviews; census and schema-agreement tests green — Verify: CI.
10. [ ] `sprk_invoicestatus` retired; ontology-r1 notified — Verify: grep + message record.
11. [ ] Only the approved package added; publish-size delta within NFR-12 — Verify: per-task measurement.
12. [ ] No Finance code uses by-name playbook lookup or `FinanceOptions` deployment names; every prompted AI step resolves through a Binding — Verify: grep + seam test (ADR-043).

## Dependencies

### Prerequisites

- Ontology PR #1111 (`docs/ontology-platform-design`) on master before the first evaluator task (FR-25) **and before FR-06's reader updates and FR-15** — as of 2026-10-15 `Services/Signals/` and `Services/Finance/FinanceBaseValues.cs` exist only on that branch (auto-merge pending); until then design against it, never copy.
- Evaluation set (FR-02) before accuracy work (FR-44).
- Procedures and e-billing containers provisioned (FR-16) before rule documents and exports.

### External Dependencies

- ontology-r1 review of FR-09, FR-15, FR-24, FR-30; uac-r2 review of FR-05, FR-15.
- Azure OpenAI deployments gpt-5.2 and gpt-5.4-mini (plus gpt-5.6-luna for the spike) — none deployed in dev today; gpt-4o / gpt-4o-mini retire 2027-04-14 (D-51). Created in FR-47.
- Azure AI Content Understanding on `spaarke-docintel-invoices-dev` (AIServices, West US, `rg-spaarke-dev`); per-customer environments need a Content Understanding region or cross-region model deployments.
- Graph PDF conversion (existing) for FR-40.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Approvers | Who can approve? | Configurable per core record via multi-select `sprk_billingapproverrole`; any one eligible approver (D-33, D-40) | FR-11, FR-14, FR-35 |
| Whole-invoice rejection | Is there a final "not payable" outcome? | Yes — Inactive status Rejected (D-34, D-41) | FR-06, FR-36 |
| Firm recipients | Who at the firm is notified? | Submitter + firm billing contact (D-45) | FR-14, FR-39 |
| Status model | Which status fields? | OOB `statecode` / `statuscode`; retire `sprk_invoicestatus`; notify ontology-r1 (D-41) | FR-06 |
| Payment | Partial payments? | No — full approved total, one payment, never reopened (D-41) | FR-06, FR-41 |
| Invoice adjustments | Percent base? | Fees only, after line adjustments; fixed amount also allowed (D-42) | FR-07, FR-32 |
| Return to firm | Reopen or new record? | New invoice record linked to the superseded one (D-43) | FR-36 |
| Containers | Where do documents go? | Invoices → general (secure-record container if secure); guidelines / rate cards / procedures → Procedures; exports → e-billing (D-35, D-44) | FR-16 |
| Invoice index | What feeds it? | Structured invoice and line records (D-44) | FR-43 |
| Submission tracking | Resubmission numbering? | `{firm invoice number}.{n}` (D-37) | FR-10 |
| Report | PDF only? | PDF and on screen (D-36) | FR-18, FR-38, FR-40 |
| Volume | Expected load? | ~100–200 per environment per month (D-38) | NFR-07 |
| Rule precedence, override, threshold, visibility state, reviewer notification, rollout | Defaults | Accepted (D-39) | FR-25, FR-26, FR-37, FR-39, FR-45 |
| Schema approval | Signal / Decision Record changes | Approved (D-31) | FR-15 |
| Standards rights | UTBMS / LEDES usable? | Freely usable (D-32) | FR-08 |
| Extraction service | Document Intelligence or Content Understanding? | Content Understanding custom analyzer; FR-03 is the gate (D-49) | FR-03, FR-21, FR-47 |
| Package | SDK or raw REST? | `Azure.AI.ContentUnderstanding` approved (D-50) | NFR-12 |
| API version | Preview or GA? | GA `2025-11-01` + SDK 1.1.0; preview only as a spike arm, adopted at GA (D-53) | FR-03, FR-47 |
| Approver defaults | Default roles? Where configured? | Assigned Attorney 1 + Assigned To Internal; in one eBilling settings record (D-54, D-59) | FR-14, FR-48 |
| Containers | Type, storage, visibility | Spaarke PAYGO 1 in dev; Procedure Library + E-Billing Output Files created; SPE not Blob; procedures internal only (D-55, D-63) | FR-16 |
| Firm guideline visibility | How do firms see the rule behind a finding? | Clause quote + reference on each finding, plus a Billing Guidelines view generated from published rules; no document access (D-66) | FR-28, FR-38, FR-49 |
| AP export | Which columns? | Market-standard default set (D-56) | FR-41 |
| Firm emails | Wording? | AI-drafted, data inserted by template, reviewer can edit for Return / Reject (D-60) | FR-50 |
| Tuning platform | Dependency? | Separate project; R1 does not depend on it; R1 stores corrections in structured form (D-61, D-64) | FR-31 |
| Record matching | In R1? | No, and no mismatch detection (D-62, D-65) | — |
| Models | gpt-4o? | gpt-5.2 extraction, gpt-5.4-mini coding (D-51) | FR-22, FR-47 |
| Playbook approach | Migrate Finance? | Finance and eBilling AI on Action + Binding; classification migrated (D-52) | FR-21, FR-22, FR-27, FR-46 |

## Assumptions

- **Date window for related work product**: ± 7 days around the line date, configurable — affects FR-34.
- **Submission file size limit**: 25 MB, configurable — affects FR-17.
- **Eligible approvers re-resolution**: event-driven on assignment change, with a recheck when an approver opens the invoice — affects FR-11, FR-35.
- **Override privilege**: a new security role privilege "eBilling Override"; single person — affects FR-19, FR-37.

## Unresolved Questions


- [ ] Container provisioning for customer environments (which container type per customer; added to customer-provisioning-orchestration) — Blocks: FR-16 outside dev only.

---

*AI-optimized specification. Original design: `design.md`; decisions: `notes/decisions-log.md`.*
