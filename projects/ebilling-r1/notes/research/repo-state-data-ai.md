# Repo state — data, AI, ontology, decisions (2026-10-14)

Code-mapper inventory of `work/ebilling-r1` at `630373cbb` (= master). Facts only; interpretation lives in `../solution-overview.md`.

## 1. Dataverse schema

| Item | Facts | State |
|---|---|---|
| `sprk_invoice` | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`. Columns include `invoicenumber`, `invoicedate`, `invoiceduedate`, `totalamount`, `currency`, `invoicestatus`, `extractionstatus`, `extractedjson`, `confidence`, `visibilitystate`, `vendororg`, `matter`, `project`, `regardingagreement`, regarding + search-index fields. Docs: `docs/data-model/sprk_financial-related-entities.md`, `docs/data-model/sprk_invoice-matter-project.md` (`sprk_invoice.md` is a 5-line stub). | working |
| `sprk_invoicelineitem` | `.../sprk_InvoiceLineItem/Entity.xml`: only `amount`, `invoice`, `billingevent`, `name`, `recordsummary`. Undocumented. | stub |
| `sprk_billingevent` | De facto line item: `amount`, `quantity`, `rate`, `costtype` (Fee/Expense), `description`, `eventdate`, `linesequence`, `timekeeper` (text), `timekeeperrole` (choice), `roleclass`, `vendororg`, `visibilitystate`. Alternate key `invoice` + `linesequence`. | working |
| Budget | `sprk_budget`, `sprk_budgetbucket`, `sprk_BudgetRevision` (undocumented). | working |
| Spend | `sprk_spendsnapshot` (invoiced vs budget, variance, velocity), `sprk_spendsignal` (BudgetExceeded, BudgetWarning, VelocitySpike). Ontology notes say spendsnapshot has zero rows. | working in code |
| Vendor / law firm | `sprk_organization` (`organizationtype`, `domain`, `organization_number`). | working |
| Matter rollups | 9 denormalised fields on `sprk_matter` / `sprk_project` (`sprk_totalspendtodate`, `sprk_remainingbudget`, `sprk_monthlyspendtimeline`, …). | working |
| Rate card, accrual | none | absent |
| LEDES / UTBMS codes | none in code or schema | absent |
| Timekeeper | text/choice columns on `billingevent` only; no entity | partial |
| Invoice status | `sprk_invoicestatus`; option set in `docs/data-model/schema-corrections.md` | partial |

## 2. Existing finance / invoice AI

- Archived project `projects/x-financial-intelligence-module-r1`: "implementation complete, pending deployment" (2026-02-12), 34/35 tasks; wrap-up task not closed. Deployment status unverified.
- BFF `src/server/api/Sprk.Bff.Api/Services/Finance/` (registered by `AddFinanceModule`, `Program.cs:102`):
  - `InvoiceAnalysisService` — classify attachment (gpt-4o-mini: InvoiceCandidate / NotInvoice / Unknown) then extract header + lines (gpt-4o structured output; `Models/FinanceJsonSchemas.cs`, `InvoiceHeader.cs`, `BillingEventLine.cs`). Playbooks "FinanceClassification" / "FinanceExtraction" load from Dataverse (`Infrastructure/DI/FinanceModule.cs`, `Services/Ai/PublicContracts/IInvoiceAi.cs`).
  - `InvoiceReviewService` — human confirm/reject gate; creates `sprk_invoice`, enqueues `InvoiceExtractionJobHandler`. Endpoints `/api/finance/invoice-review/confirm` and `/reject` (`Api/Finance/FinanceEndpoints.cs:37,51`).
  - `SpendSnapshotService`, `SignalEvaluationService` (threshold strategies), `FinanceRollupService`, `FinanceSummaryService` (Redis), `InvoiceSearchService` (AI Search index `spaarke-invoices-dev`; `NullInvoiceSearchService` fallback).
  - Tools: `Tools/FinancialCalculationToolHandler.cs`, `InvoiceExtractionToolHandler.cs`.
  - `Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs` — Invoice fact provider for the ontology.
- UI: `src/solutions/sprk_invoicespage`, `src/solutions/CreateInvoiceWizard`.
- Docs: `docs/architecture/finance-intelligence-architecture.md`, `docs/guides/finance-intelligence-user-guide.md`, `docs/guides/finance-spend-snapshot-visualization-guide.md`.
- Gaps: email-attachment driven only; compliance = spend-vs-budget thresholds only; no guideline/rate checks; no per-line approval.

## 3. Ontology project (`projects/spaarke-ontology-platform-r1`, Issue #1227)

- Status: design reviewed, `/design-to-spec` not run (current-task 2026-09-30); next action D-1 scoping spike. Signals code on stream branch `stream/c-036-026` (worktree `C:\wts-c`): `Services/Signals/` (`SignalWriter`, `PredicateCompiler`, `PolicyVersionValidator`, `RuleBodySchemaValidator`, `RuleType`, `Actions/DecisionActionCatalog`, `DecisionPlanService`), `Api/Signals/DecisionPlanEndpoints.cs`. Merge status unchecked.
- `notes/ontology-component-model.md` defines: six-stage pipeline (Connection / Insights / Action engines, §2.1, §5); binding modes, declared vs derived attributes (§4.4–4.5); **Policy** (§4.8) = `sprk_policy` + immutable `sprk_policyversion`, seven closed rule types (Threshold, AllowList, Switch, DecisionTable, Envelope, SlaDefinition, DelegationLimit); Policy ≠ playbook (§4.11); closed-vocabulary rule (§4.12, l.331: "A customer cannot write a policy about something we do not extract"); Decision Record (§4.13); Authority (§4.14); seven-step ingest template (§4.16); Worklist (§6.1).
- Invoice-specific: Invoice is a "mirror" class (l.243). **Decision 21 (l.657; `design.md:85,523,763`): no LEDES intake — invoice, budget and spend variance arrive as computed metrics from the e-billing platform.** `design.md:32`: rate compliance, accruals, invoice-line review are a non-differentiator (Legal Tracker, Onit, Brightflag do them). Example customer rules: "Any invoice over $50k needs my approval", "can't bill travel without pre-approval".
- Schema already in SpaarkeMaster: `sprk_Policy`, `sprk_PolicyVersion`, `sprk_Signal`, `sprk_DecisionRecord`; Signal and DecisionRecord have `regardinginvoice`.

## 4. Decision Record

- `sprk_DecisionRecord`: `decisionnumber`, `proposedaction`, `action`/`actioncode`, `reason`, `decisionoutcome`, `decidedon`, `confirmedby`, `authority`, `gatetier`, `gatesessionid`, `policyversion`, `factsnapshot`, `steps`, `followons`, `privilegeflagged`; regarding lookups incl. invoice, matter, project, document, communication.
- Semantics: append-only, written at the gate, one per gate decision. Flags are `sprk_signal` (`policy`, `severity`, `sentence`, `evidencerefs`, `decisionrecord`, `resolutiontype`).
- UI: `ConsoleKit/consoleStatus.ts` `resolveDecisionRecordState`. BFF write paths partial / on stream branch.

## 5. Rules from documents

- No generic guideline-document → rules converter.
- Target representation: policy rule bodies (`sprk_policyversion.sprk_rulebody`, `ruletype`) + validators / `PredicateCompiler` (stream branch).
- NDA review (`projects/ai-advanced-capabilities-nda-r1`, complete 2026-07-28): advisory, RAG-grounded playbook with closed output schema; no checkable rule objects.
- Not inspected in depth: `projects/ai-advanced-capabilities-agreements-r1`, `Services/Ai/EventRules/`, `Services/Ai/Insights/Playbooks/`.
- ADR-039 (amended) separates deterministic from advisory output.

## 6. Approval workflows

`InvoiceReviewService` confirm/reject (classification gate, not payment approval); `sprk_invoicestatus`; `billingevent.visibilitystate` (incl. Approved, WrittenOff); DecisionRecord `gatetier` / `authority` / `confirmedby` and the unified gate-resolve endpoint (`ChatEndpoints.cs:354`); ontology Authority + `DelegationLimit`. No per-line approval model.

## 7. Portfolio

Epic #1847 (scope: e-billing, invoicing, budgeting, finance reconciliation, outside counsel management, reporting/dashboards; no sub-issues). Project #1850. Related: `x-financial-intelligence-module-r1` (archived), `spaarke-ontology-platform-r1` (#1227).
