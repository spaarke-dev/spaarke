# eBilling R1 — project operating manual

> Read this with `current-task.md`, which holds the current state. Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

### What R1 delivers

One complete outside-counsel invoice loop:

1. SPA or internal submission of a PDF invoice.
2. Content Understanding extraction.
3. UTBMS coding through an Action.
4. Evaluation against published billing rules (11 types, shared rules system with ontology-r1).
5. A rejection rule.
6. The Invoice Review code page: line decisions, corrections, adjustments, approval, and Reject / Return / Override.
7. Firm feedback: SPA invoice detail, Billing Guidelines view, AI-drafted notice, PDF report, notifications.
8. AP export to Sent to AP, then Paid, plus an invoice index.

Finance attachment classification also migrates to Action + Binding.

### Out of scope (R1)

- Invoice-to-record matching and mismatch detection (D-62, D-65).
- The tuning platform (D-61, D-64). It is a separate project and **not a dependency**.
- Firm access to guideline documents (D-63, D-66).
- LEDES intake.
- Mailbox and Outlook channels.
- Multi-portion approval.
- Accruals, tax, VAT, PO, GL and cost centre (fast followers; the design must accommodate them).

Status lives in `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md` (**authoritative**; read the cited FR before every task). Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

### Write path and lifecycle

- **One server-side owner per invariant (ADR-002, no plugins).**
  - Invoices are created only by the submission operation (FR-17, FR-20).
  - Totals are computed only server-side (FR-07, I-1).
  - Transitions go only through the lifecycle service (FR-06 matrix).
  - Approval follows I-2.
  - Wizards preview only.
- **Status lives on `statecode` / `statuscode` (D-41).**
  - Active: In Review, Approved, Sent to AP.
  - Inactive: Paid, Rejected, Returned to Firm, Cancelled.
  - `sprk_invoicestatus` is retired. Never add a new reader.

### AI

- **AI goes only through `IInvoiceAi` (PublicContracts) → `IActionRunner` (ADR-013, ADR-039, ADR-043, D-52).**
  - No by-name playbook lookup.
  - No C# output schemas: the schema lives in the Action row.
  - No new capability on the frozen node-graph engine.
  - Every facade method has a Null peer (ADR-032).
  - A change to Action dispatch needs a seam test.
- **AI never rejects (I-5).** A rejection needs all three of the following (FR-26):
  - a failing Reject-consequence rule;
  - every field it read at combined confidence ≥ the threshold (default 0.90, from settings);
  - a reject-eligible type (1, 2, 6–10).
- **Codes come only from the resolved code set (FR-22).** The order is firm codes → matter type → practice area → AI suggestion (flagged) → settings default. An out-of-set code is refused server-side, never stored.

### Rules

- **Rules are closed typed templates with JSON parameters (D-27).**
  - No Power Fx or other low-code language.
  - Rules are evaluated only from published, immutable policy versions.
  - Every finding cites the policy, version and clause.
  - An unknown rule type is refused fail-closed in every consumer (FR-24). This is security-class: the proof must be seeded, not asserted.

### Packages, firm visibility, corrections, tracking

- **Content Understanding access:** preview `2026-06-01-preview` primary with a GA `2025-11-01` fallback analyzer, through a REST client; `Azure.AI.ContentUnderstanding` is added only if 043 shows a need (D-50, D-53 as amended by D-81, NFR-12).
  - Measure the publish-size delta against a fresh master build on every BFF task (root §10).
  - The preview `2026-06-01` is allowed only as spike arm (c′) in task 005.
- **Firm-visible output (D-66):**
  - It carries the rule set name and version, the section reference and the quoted clause text.
  - It never carries a Procedure Library document link.
  - Rule sets with "visible to firms" off are never shown to CIAM users.
- **Corrections are data (D-64).** Every line value or code change made in review writes a correction record with:
  - the AI value and the final value;
  - the confidence;
  - the outcome type;
  - the Action code and version;
  - the user.

  Only *Corrected* and *Confirmed individually* count as labels. *Accepted in bulk* never does.
- **Tracking numbers are `{firm invoice number}.{n}`** and are allocated concurrency-safe (D-37, FR-10).

### Deferred work

Deferred work and newly found issues go through `/project-defer-issue-tracking`. It writes `notes/defer-issues.md` **and** a GitHub issue, never one without the other.

### ADR tensions

No ADR tensions are declared (spec "ADR Tensions"). If one emerges, use root §6.5 — do not resolve it silently.

## 3. Owner directives and standing decisions

Full rationale for each decision is in `notes/decisions-log.md`.

- 2026-10-14 — D-18: extend `sprk_billingevent` as the Invoice Line; retire `sprk_invoicelineitem`.
- 2026-10-14 — D-23 / D-30: one rules *system*, two strategies. Ontology's Signals code (#1111) merged to master 2026-10-15 at `f292ad119` and is in this branch; build on it, never fork it.
- 2026-10-14 — D-24 / D-39: internal submission runs the same checks. Override needs a reason and the "eBilling Override" privilege; one person.
- 2026-10-14 — D-33 / D-40 / D-54: approvers are resolved from `sprk_billingapproverrole` on the core record. Any one eligible approver approves. An empty role means the settings default, seeded as Assigned Attorney 1 + Assigned To Internal.
- 2026-10-14 — D-37 / D-43: resubmission keeps the firm invoice number and gets a new `.n`. Return to Firm closes the invoice; the accepted resubmission is a new invoice linked as superseding it.
- 2026-10-14 — D-42: approved total = billed − line adjustments − invoice adjustments. A percent adjustment applies to fees *after* line adjustments.
- 2026-10-14 — D-44 / D-55: invoice PDFs go in the core record's general (or secure) container and the files index.
  - Guidelines, rate cards and procedures go in **Procedure Library** (internal only, indexed).
  - Export files go in **E-Billing Output Files** (not indexed).
  - Both use SPE, never Blob.
  - **Superseded for files by EB-D-148 (2026-10-21, `notes/procedure-and-file-model.md` 3.4, 4):** invoice PDFs, supporting files, Procedure sources and export files are Stored Files in the Finance file store (090, provisioned with the stamp by 091). 092 retired `Finance:EbillingOutputContainerId`; task 124 retires `Finance:ProcedureLibraryContainerId` after 116 (EB-D-163). No new reader of either container.
- 2026-10-14 — D-46 / D-49 / D-51 / D-53: extraction (Content Understanding custom analyzer `sprk_invoice_vN`, gpt-5.2) is separate from coding (`INVOICE-LINE-CODE@v1`, gpt-5.4-mini; gpt-5.6-luna is compared in 005).
- 2026-10-14 — D-47: field confidence is combined from four signals: analyzer confidence, arithmetic consistency, reference-data match and model self-report.
- 2026-10-14 — D-56: AP export default columns, with the mapping snapshotted per batch.
- 2026-10-14 — D-58: the Invoice Review UI follows the owner's prototype (`c:\code_files\spaarke-prototype\projects\ebilling.r1`, README + `src/review/`):
  - layout A (document on the right);
  - findings shown as a badge plus the pane;
  - Correct and Reduce kept separate;
  - findings grouped by rule, severity and count;
  - "Pending" shown as **Processing**;
  - invoice decisions in WizardShell;
  - line decisions in the pane.
- 2026-10-14 — D-59: one audited eBilling settings record holds approver defaults, the threshold, confidence weights, chunk size, related-work window and AP mapping.
- 2026-10-14 — D-60: firm notices are drafted by `INVOICE-FIRM-NOTICE@v1`. Amounts, line references, clause quotes and the findings table are inserted by code. A template is used when the AI is unavailable.
- 2026-10-15 — D-66 (confirmed): firms see guideline *content* — clause quotes on findings plus the SPA Billing Guidelines view — never the documents.
- 2026-10-15 — Pipeline: WP1 schema tasks (010–028) each write their own idempotent Web API script under `scripts/ebilling/` and apply it to dev. **Only task 029** syncs SpaarkeMaster solution source, forms and views. After 029, `src/dataverse/solutions/SpaarkeMaster/**` is edited only by serial tasks: 015 and 026 (their entities), 070 (billing values on the `sprk_policyversion.sprk_ruletype` option set) and the deploy tasks 053, 079, 112, 136 and 153 (sitemap / navigation entries for the pages they deploy, plus any columns or tables their phase added after 029, using 029's export-to-temp procedure). No parallel task edits it.

- 2026-10-15 — D-67: Procedure Library and Output Files container keys join `GraphModule.StampContainerSettingKeys` (app-only SPE writes allowed). Being retired with the keys (092, 124; EB-D-163).
- 2026-10-15 — D-68: external-identifier key = (firm, type, normalized value, core record).
- 2026-10-15 — D-69: confidence weights seeded provisionally; validated in 161 / 162 and checked at UAT (163).
- 2026-10-15 — D-70: CIAM users see only their own firm's invoices (`sprk_vendororg`), in addition to core-record and secure-root scoping (task 130; uac-r2 reviews).
- 2026-10-15 — D-71: legacy Finance extraction deleted; confirm route redirected into 044; ontology-r1 confirmed no dependency 2026-10-15; keep `sprk_totalamount` + matter/project lookup for `FinanceBaseValues`; send the 050 PR to ontology-r1 for review.

- 2026-10-15 — D-72 (F-2): approve with undecided Clean lines allowed (Accepted in bulk, not labels).
- 2026-10-15 — D-73 (F-7): Return of a blocked internal submission needs no privilege.
- 2026-10-15 — D-74 (F-8): adjustment rule list = published rules of applicable rule sets in force on the invoice date.
- 2026-10-15 — D-75 (F-9): one Decision Record per line decision (subject = the line itself, no 1:N invoice assumption) plus one per invoice decision.
- 2026-10-15 — D-76: Override and billing-admin are Dataverse table privileges checked via `CallerHoldsPrivilegeAsync`; no role-name or app-role checks.
- 2026-10-15 — D-77: any user with Write on the invoice decides lines.
- 2026-10-15 — D-78: approver re-resolution = live check at approve + recheck on GET + refresh job (or stale flag + manual refresh).
- 2026-10-15 — D-79: rule set with "visible to firms" off → firm sees reason + rule set name, never clause text or section reference.
- 2026-10-15 — D-80: task 001 baseline accepted without a live legacy run; dev not repaired (legacy path replaced in 044, deleted in 050).
- 2026-10-15 — D-81: extraction GO on Content Understanding with a preview exception (path A): preview analyzer primary, GA fallback analyzer kept in step and selectable by config, REST client for both versions (no beta SDK), 161 / 162 measure both.
- 2026-10-17 — D-96: an invoice core-lookup change is refused if its lines name another core; the line check runs as the app identity.
- 2026-10-17 — D-97: 015 approved in full, including the live delete of `sprk_invoicestatus`.
- 2026-10-17 — D-103 (supersedes D-98; = ontology D-166): counted spend = `statuscode` Approved, Sent to AP, Paid. In Review is not counted.
- 2026-10-17 — D-99: callers linked to several firms choose the firm when submitting (`firms` on `GET /external/me`).
- 2026-10-17 — D-100: billing Decision Records use the additive `DecisionRecordWriter.WriteBillingDecisionAsync` (§6.5 path B). `WriteAsync` stays byte-identical, and ontology-r1's 7 conditions apply.
- 2026-10-17 — D-101: every submission that is read creates its invoice. Rejected → created Rejected. Blocked → In Review, held until Override / Return. Their Decision Records file under the invoice; `sprk_decisionrecord` gets no submission lookup.
- 2026-10-17 — D-102: held invoices never count toward spend. This is automatic under D-103; 049 keeps a must-fire test on both sum paths (rollup and `FinanceBaseValues.ReadAsync`). Reviewed by ontology-r1.

- 2026-10-21 — **"Matter" is shorthand for any core record.** e-billing applies to `sprk_matter`, `sprk_project` and `sprk_workassignment`, and will extend to `sprk_agreement` in e-billing R2 (owner 2026-10-21: an agreement is a core record that can stand alone or sit under a matter or project). Every new table, lookup, field, control and rule is built to the core-record set (one list, like `InvoiceFamilyCoreRecordGuard.FilingCores`), never to `sprk_matter` alone. A test or a design that names only `sprk_matter` needs a reason.

- 2026-10-28 — **UAC round 130 (#2362): no secure-family entity.** Invoice-family children stay secure through the existing mechanism (SecureChildLineage rows, RecordOwnershipResolver, the reconciler); no family key, column or table. Binding: membership stays computable from stored data (primary-parent rule, one filing root); every ownership write goes through the resolver; new isolation tests use the shared predicate keyed on the Secure Record business unit, never a hard-coded team id; "everything in this family" reads go through field-mapping W7's `RecordFamilyReader`, never raw lookups.
- 2026-10-28 — EB-D-164 (malware scanning required on production stamps, off allowed on dev/staging; provisioning keys it on `environmentName`) and EB-D-165 (another user may re-attempt after a Failed submission); see notes/decisions-log.md.

### Open owner questions

None open from the prototype (F-2, F-7, F-8, F-9 decided as D-72 to D-75). Remaining owner gates sit in the tasks themselves (005 go / no-go).

## 4. Coordination

- **spaarke-ontology-platform-r1** owns the Signals, rules and Decision Record code: `Services/Signals/**`, `FinanceBaseValues.cs`, `PolicyVersionValidator`, `RuleBodyDescriber`, `DecisionActionCatalog`.
  - **#1111 merged to master 2026-10-15 (`f292ad119`)** and was merged into `work/ebilling-r1` the same day. It provides the Signal writer, rule grammar and evaluator, the Decision Record writer (`WriteAsync` for humans, `WriteGateDecisionAsync` for the gate) and `GET /api/v1/signals/worklist`. ontology-r1 reviews eBilling PRs into master.
  - ontology-r1 reviews FR-09, FR-15, FR-24 and FR-30, plus the `CountedInvoiceStatuses` change in 015.
  - Notify ontology-r1 when the `sprk_invoicestatus` retirement PR and the schema PRs open.
- **unified-access-control-r2** reviews FR-05 (core-ancestor derivation) and FR-15. It last touched `InvoiceReviewService.cs`. It shares `Infrastructure/ExternalAccess/**`: `parallel-safe=false` on tasks that edit it, and run `/conflict-check` before every BFF PR.
- **spaarke-SPA-external-access-platform-r3** shares the external SPA (`src/client/external-spa/**`) and `deploy-external-spa.yml`. Coordinate on the Invoices widget and the SPA notification feed.
- **customer-provisioning-orchestration-r1** received the hand-off for SPE container provisioning in customer environments (task 028, DEF-013 / #1967). EB-D-163 withdraws it: eBilling needs no SPE container on a stamp, only 091's file store; task 124 closes #1967 and tells them.
- **notification-spine-r1 / ADR-047** — notifications (132) go through the spine.
- **spaarkeai-tuning-and-feedback-r1** consumes our correction records later. Do not build anything for it in R1 beyond the D-64 record.
- **Prototype** (`spaarke-prototype/projects/ebilling.r1`) is the reference UI for 108–111. Its components are mocks: map them to real shared components (WizardShell, FormModal, ConsoleKit, StatusBadge, AiFieldTag, PanelSplitter / useTwoPanelLayout, DataGrid). Never import from the prototype repo.

## 5. Environment and live actions

- **Dev only in R1:**
  - Dataverse dev;
  - `spaarke-bff-dev`;
  - Content Understanding resource `spaarke-docintel-invoices-dev` (AIServices, West US, `rg-spaarke-dev`);
  - containers per D-55 (Procedure Library `b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`, E-Billing Output Files `b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`, container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`).
- **Additive dev schema** (new tables or columns via the task's idempotent script) is part of the schema task.
  - **Destructive changes need explicit owner OK before running:** deleting a table, column, option set or data. This covers 011 `sprk_invoicelineitem` and 015 `sprk_invoicestatus`.
- **Azure model deployments, analyzer creation and BFF / code-page / SPA deploys:**
  - They run only inside their named task (003, 004, deploy tasks 029, 053, 079, 112, 136, 153).
  - Each run is recorded in the task notes.
  - `scripts/Deploy-BffApi.ps1` for the BFF; `scripts/Import-SolutionScoped.ps1` is the **only** way to import a solution.
- **No secrets in the repo.** The Content Understanding endpoint is config; auth is managed identity (FR-47). Key Vault rules are in root §9.
- **Never delete** the owner-created containers or the evaluation-set source documents.

## 6. Gotchas — do not re-learn

- 2026-10-15 — The spec's file paths were corrected against code; trust them over design.md. Two examples:
  - `FinanceJsonSchemas` is at `Services/Finance/Models/FinanceJsonSchemas.cs`.
  - The tier resolver is `Services/Ai/LinearConsumers/ModelTierDeploymentResolver.cs`.
- 2026-10-15 — `sprk_timekeeper` **already exists as a stub** (name, contact, invoice and invoice-line-item lookups). Extend it, review its lookups and drop the obsolete ones. Do not create a new table.
- 2026-10-15 — `Services/Signals/**` and `FinanceBaseValues.cs` reached master only with #1111 (`f292ad119`). Before 015, 026 or 070+, check `git merge-base --is-ancestor f292ad119 HEAD`; if it fails, merge origin/master first. Never copy files across branches.
- 2026-10-15 — The prototype's `DocumentView` bounding-box contract is `{page, x, y, w, h}`, normalised 0–1, with a row box and a multiply tint. The page stays light in dark mode (F-4).
- 2026-10-15 — ConsoleKit StatusBar needs a label override (F-3).
- 2026-10-15 — Style a wrapper div, not the FluentProvider (F-10).
- 2026-10-15 — `src/server/api/CLAUDE.md` does not exist; do not cite it. Tests: `docs/standards/TEST-ARCHITECTURE.md`, `docs/adr/ADR-038-testing-strategy.md`, `.claude/patterns/testing/`.
- 2026-10-15 — A consumer-type constant goes into `ConsumerTypes.All` only in the same change that seeds its Binding row. `RoutingConsumerTypeHealthCheck` turns /healthz Unhealthy otherwise. Action codes are lowercase kebab without a version suffix (e.g. `invoice-line-code`); the spec's `INVOICE-…@v1` names map to these.
- 2026-10-15 — Action seeding: `Seed-JpsActions.ps1` is retired. Use `scripts/Deploy-ActionMirrors.ps1` (`infra/dataverse/actions/`) plus `scripts/dataverse/Seed-PlaybookConsumers.ps1`.
- 2026-10-15 — Matter, Project and Work Assignment carry lookup columns `sprk_mattertype` / `sprk_practicearea` (target tables `sprk_mattertype_ref` / `sprk_practicearea_ref`). Code-set mapping keys off those.
- 2026-10-15 — On dev, `RemoveAppComponents` against the `Spaarke Platform` app (`sprk_SpaarkePlatform`, an M365 Copilot app bundling bots, an MCP server and table-search indexes) returns 204 but removes nothing. Remove a table from that app in the app designer. `appmodulecomponent` rows cannot be DELETEd (0x80040800).
- 2026-10-17 — **Shared dev's BFF and external SPA are deployed only by SPA-r3, from its branch** (`deploy-external-spa.yml --ref work/spaarke-SPA-external-access-platform-r3`); R3's work is not on master. Our BFF or SPA code reaches dev only through R3's build. Deploy tasks (053, 079, …) coordinate with R3; they do not run `Deploy-BffApi.ps1` on their own. A change to a column the live SPA or BFF reads needs a transition window in which both shapes are admitted (015: the external invoices allow-list kept `sprk_invoicestatus` next to `statuscode`; 2026-10-29: that entry was removed on master in #2450, and the dev column delete waits for the next dev BFF deploy).
- 2026-10-17 — Task 015 delete order and the rule agreed with R3 (`notes/015-retire-invoicestatus.md` section 3.5): R3 deploys a BFF admitting both status columns, then the SPA; `-GridConfig`; 015 merges into R3's branch; R3 deploys a BFF with 015's Finance readers; `-Delete`; then a follow-up commit removes the transitional allow-list entries. **After the delete, every dev BFF deploy must contain 015, checked by content, not ancestry** (a squash merge breaks `merge-base --is-ancestor`). `Remove-EbillingInvoiceStatus.ps1` takes `-BffDeployAttestation` / `-SpaDeployAttestation` commit shas and checks that source.
- 2026-10-17 — No upload or storage route may accept an invoice line (`sprk_billingevent`) as a regarding/parent until uac-r2 #2047 lands (`sprk_invoice` as carrier). Today a document or to-do under a line in a secure matter gets 409 `container_ancestor_unverifiable` (task 026, D-94 condition 4).

- 2026-10-23 — **#2277 merge obligation (uac-r2, 090 round-2 K1).** Once master's `CreatorPersonColumnSecuredGuardTests` is in our branch, every stamped table must declare `sprk_createdbyperson` secured. It needs either `<IsSecured>1</IsSecured>` in Entity.xml, after an export, or a `SecuredOnlyByDeployScript` entry naming `scripts/Set-ChildRecordCreatorPersonSchema.ps1`; only tables in its `$Tables` may get one, and unstamped tables never do. Today that is 081's `sprk_procedure`, `sprk_procedureversion`, `sprk_policy` and `sprk_policyversion`, plus 090's `sprk_ruleset` (no column in Entity.xml) and `sprk_procedureassignment` (no Entity.xml). Any new stamped table adds its entry in the same change.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions-log.md` · `notes/defer-issues.md` (created on first deferral)
- Research: `notes/research/content-understanding-vs-docintel.md`, `notes/research/test-data-sources.md`, plus the LEDES / UTBMS / OCG references in `notes/research/`.
- Applicable ADRs:
  - API, plugins, auth: 001, 002, 003, 008, 010, 019, 028.
  - Jobs: 004, 017, 036, 052.
  - Storage: 007.
  - AI: 013, 014, 015, 039, 043.
  - UI: 012, 021, 050.
  - Flags and kill switch: 018, 032.
  - Records, access, notifications: 024, 034, 047.
  - Testing: `docs/adr/ADR-038-testing-strategy.md`.
- Related projects: spaarke-ontology-platform-r1, unified-access-control-r2, spaarke-SPA-external-access-platform-r3, customer-provisioning-orchestration-r1, spaarkeai-tuning-and-feedback-r1.
