# Project Plan: eBilling R1

> **Last Updated**: 2026-10-15
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) (authoritative) · Design: [design.md](design.md) · Decisions: [notes/decisions-log.md](notes/decisions-log.md) D-01 – D-66

---

## 1. Executive Summary

**Purpose**: Deliver one complete outside-counsel invoice loop on Spaarke. A firm submits a PDF invoice in the partner SPA. Spaarke reads it with a Content Understanding custom analyzer, codes each line to UTBMS, and evaluates it against published billing rules. It rejects only reliably read blocking failures, routes everything else to an eligible approver for line review and approval, tells the firm the outcome with guideline citations, and exports approved invoices to AP.

**Scope (deliverables)**:
- Schema: Invoice Line on `sprk_billingevent`; submission, approval, rate card, UTBMS code set, rule set, external identifier, export, settings and correction tables; Signal / Decision Record line subject; `statecode`/`statuscode` lifecycle (retire `sprk_invoicestatus`).
- AI on the Action + Binding approach: Content Understanding extraction, UTBMS coding, rule drafting, firm notice, Finance classification migration.
- Submission (SPA and internal wizard), the processing job, the in-memory rule evaluator with 11 billing rule types, the rejection rule.
- Invoice Review code page (built from the owner-reviewed prototype), approval / reject / return / override, firm feedback (SPA detail, Billing Guidelines view, PDF report, notifications), AP export, invoice index.
- Evaluation against NFR-04 – NFR-06, feature flag, dev rollout.

**Timeline**: 78 tasks in 8 phases · **Estimated effort**: 32 – 42 working days of task execution (≈ 2–4 h per task, deploy and evaluation tasks longer), plus owner review time at the FR-03 go / no-go and UAT.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (concise versions in `.claude/adr/`):
- **ADR-001 / ADR-019** Minimal API endpoints; ProblemDetails with stable error codes.
- **ADR-002** No plugins; one server-side owner per invariant (submission, invoice creation, totals, transitions, approval). `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`.
- **ADR-003 / ADR-008 / ADR-028** Endpoint-filter authorization on every route; CIAM firm users scoped to their firm and accessible core records.
- **ADR-004 / ADR-017 / ADR-036 / ADR-052** Queue work as `IJobHandler` in the BFF; persisted job status; no hand-rolled timers.
- **ADR-007** SPE only through `SpeFileStore`.
- **ADR-010** DI inside `AddFinanceModule` (and the existing AI facade registration); Options with `ValidateOnStart()`.
- **ADR-012 / ADR-021 / ADR-050** Shared components, Fluent v9, canonical modal shell.
- **ADR-013 / ADR-015 / ADR-039 / ADR-043** AI only through `PublicContracts` (`IInvoiceAi`), Action + Binding, closed catalogues, schema-validated output, minimum text, versioned Actions; seam test on dispatch change.
- **ADR-018 / ADR-032** Feature flag; null-object kill switch for every facade method.
- **ADR-024 / ADR-034** Dual-field polymorphic core-record lookups; core-ancestor stamping.
- **ADR-047** Notification spine.
- **ADR-038** (docs/adr) Testing strategy.

**From Spec** (binding; full list in spec "MUST Rules"):
- Every invoice is created by the server-side submission operation; totals and transitions are server-side only.
- Rules are evaluated only from published, immutable policy versions; findings cite policy, version and clause; unknown rule types are refused fail-closed everywhere.
- AI never rejects (I-5); rejection needs Reject consequence + every field read ≥ threshold + reject-eligible type (FR-26).
- No Power Fx / low-code rule language; no by-name playbook lookup; no C# AI output schemas; no new capability on the frozen node-graph engine.
- One new package only (`Azure.AI.ContentUnderstanding` 1.1.0, GA API `2025-11-01`).

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Content Understanding custom analyzer for extraction (D-49, D-53) | Per-field confidence + page / bounding box for every line column; DI invoice model has no timekeeper / narrative | FR-03 spike is the go / no-go gate; DI layout + extraction Action is the fallback |
| Extraction and coding separate (D-46); coding as an Action (D-52) | Code sets vary per record; coding needs context outside the PDF | `INVOICE-LINE-CODE@v1`, enum-restricted, server-side refusal of out-of-set codes |
| Models gpt-5.2 (CU) and gpt-5.4-mini (coding) (D-51) | gpt-4o family retires 2027-04-14 | WP0 creates deployments; model refresh in 2027 |
| One rules system, two strategies (D-23) | Ontology keeps FetchXML push-down; eBilling evaluates an invoice in hand | Shared `RuleType` set, schemas, validator, describer from ontology #1111 |
| `statecode`/`statuscode` lifecycle (D-41) | OOB status model; no separate payment status | `sprk_invoicestatus` retired; ontology-r1 notified |
| Settings singleton (D-59) | Owner wants approver defaults, threshold and confidence weights tunable without deployment | FR-48 |
| Firms see guideline content, not documents (D-63, D-66) | Procedures internal; rejections must cite their source | Clause quotes on findings, SPA Billing Guidelines view, per-rule-set "visible to firms" flag |
| Corrections captured as structured data (D-64) | Calibration now; tuning platform later without dependency | Correction record (WP1) + capture in review (WP4) |
| Review screen from the prototype (D-58) | Owner-built prototype `spaarke-prototype/projects/ebilling.r1` answers layout questions | Layout A, badge + pane, separate correct vs reduce, findings grouped by rule |

### Discovered Resources

**ADRs (concise, `.claude/adr/`)**: ADR-001-minimal-api · ADR-002-thin-plugins · ADR-003-authorization-seams · ADR-004-job-contract · ADR-007-spefilestore · ADR-008-endpoint-filters · ADR-010-di-minimalism · ADR-012-shared-components · ADR-013-ai-architecture · ADR-014-ai-caching · ADR-015-ai-data-governance · ADR-017-job-status · ADR-018-feature-flags · ADR-019-problemdetails · ADR-021-fluent-design-system · ADR-024-polymorphic-resolver-pattern · ADR-027-subscription-isolation-and-dataverse-solution-management · ADR-028-spaarke-auth-architecture · ADR-029-bff-publish-hygiene · ADR-032-bff-nullobject-kill-switch · ADR-034-user-record-membership · ADR-036-background-job-infrastructure · ADR-039-grounded-execution-closed-catalogs · ADR-040-session-ledger · ADR-043-ai-capability-execution-spine · ADR-047-notification-action-spine · ADR-050-canonical-modal-shell · ADR-052-workload-placement · `docs/adr/ADR-038-testing-strategy.md`.

**Constraints**: `bff-extensions.md` (binding) · `api.md` · `auth.md` · `ai.md` · `data.md` · `jobs.md` · `testing.md` · `plugins.md` · `pcf.md` · `webresource.md` · `config.md` · `azure-deployment.md` · `provisioning.md` · `theme-consistency.md` · `react-versioning.md` · `agent-cost.md`; rule `.claude/rules/bff-hygiene.md`.

**Patterns**: api/{endpoint-definition, endpoint-filters, background-workers, error-handling, service-registration, send-email-integration, membership-consumer} · dataverse/{entity-operations, polymorphic-resolver, web-api-client, relationship-navigation} · ai/{public-contracts-facade, endpoint-di-symmetry, indexing-pipeline, text-extraction, analysis-scopes} · testing/{unit-test-structure, mocking-patterns, integration-tests} · auth/{spaarke-sso-binding, uac-access-control, spe-writer-identity-matching} · webresource/{full-page-custom-page, code-page-wizard-wrapper} · ui/{modal-shell, record-modal-selection, fluent-v9-component-authoring, fluent-v9-theming} · provisioning/handler-registration-completeness.

**Skills**: dataverse-create-schema · dataverse-deploy · dataverse-mcp-usage · bff-deploy · code-page-deploy · jps-action-create · jps-validate · azure-deploy · spe-integration · fluent-v9-component · ui-test · adr-check · code-review · project-defer-issue-tracking · test-diet · context-handoff · conflict-check · ci-cd (external SPA deploy workflow).

**Knowledge**: `docs/guides/ai-guide-consumer-wiring.md` · `docs/architecture/finance-intelligence-architecture.md` · `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` · `AI-SEARCH-INDEX-CATALOG.md` · `SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md` · `external-access-spa-architecture.md` · `docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md` · `SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` · `docs/standards/TEST-ARCHITECTURE.md` · `docs/data-model/{sprk_invoice, sprk_billingevent, sprk_financial-related-entities, sprk_invoice-matter-project, field-mapping-reference, schema-corrections, sprk_workassignment}.md`.

**Reusable code (canonical references)**:
- Facade: `Services/Ai/PublicContracts/CommunicationTriageAi.cs:70-123` + `NullCommunicationTriageAi.cs`; facade to reshape `IInvoiceAi.cs` / `InvoiceAi.cs` / `NullInvoiceAi.cs`.
- Actions: `Services/Ai/LinearConsumers/ActionRunner.cs` (`$choices` :424), `IActionRunner.cs`, `ModelTierDeploymentResolver.cs`; JPS exemplar `.claude/skills/jps-action-create/examples/document-profiler.json`.
- Finance (migrate): `Services/Finance/InvoiceAnalysisService.cs`, `Services/Finance/Models/FinanceJsonSchemas.cs`, `Services/Jobs/Handlers/{InvoiceExtractionJobHandler, AttachmentClassificationJobHandler, InvoiceIndexingJobHandler}.cs`, `Infrastructure/DI/FinanceModule.cs`.
- Write path: `Services/Dataverse/CoreAncestorResolver.cs`. DI fallback: `Services/Ai/DocumentIntelligenceService.cs`.
- SPA upload: `src/client/external-spa/src/pages/DocumentUploadPage.tsx`. Wizard: `src/solutions/CreateInvoiceWizard/`.
- Review UI reference: `c:\code_files\spaarke-prototype\projects\ebilling.r1` (README, `src/review/DocumentView.tsx`, `src/ui/kit.tsx`, screenshots v1).

**Scripts**: `scripts/Deploy-BffApi.ps1` · `scripts/Import-SolutionScoped.ps1` (only way to import a solution) · `scripts/ai-search/Deploy-AllIndexes.ps1` · `scripts/Deploy-ActionMirrors.ps1` · `scripts/dataverse/Seed-PlaybookConsumers.ps1` · `scripts/seed-data/{Invoke-SeedManifest, Deploy-Actions}.ps1` · `scripts/Deploy-WizardCodePages.ps1` · `scripts/Set-ContainerId.ps1` · `scripts/Capture-BffBaseline.ps1` · `.github/workflows/deploy-external-spa.yml` · schema exemplar `scripts/Deploy-ChartDefinitionEntity.ps1`.

**Schema baseline (SpaarkeMaster source, 2026-10-15)**: `sprk_billingevent` lacks nearly all FR-04 columns · `sprk_invoice` lacks core-record dual fields, totals, supersedes, batch lookup, lifecycle statuscodes · `sprk_timekeeper` exists as a stub (extend) · `sprk_signal` / `sprk_decisionrecord` lack `sprk_regardinginvoiceline` · `sprk_policy` / `sprk_policyversion` exist (extend) · Matter, Project, Work Assignment carry `sprk_mattertype` / `sprk_practicearea` · new: rate card, rate, code set, code, submission, approval, external identifier, export, settings, rule set, correction record.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0 (WP0) Baseline and AI foundations   001–005  baseline, eval set, model + analyzer provisioning, extraction spike (go/no-go)
Phase 1 (WP1) Data model                    010–029  schema scripts (parallel) → apply + export → server invariants; status retirement
Phase 2 (WP2) Submission and extraction     040–053  flag, IInvoiceAi reshape, CU client, submission op, resolution, coding, job, SPA + wizard
Phase 3 (WP3) Rules                         070–079  rule types, handlers, evaluator, rejection, decision actions, drafting, rule review, import
Phase 4 (WP4) Review and approval           100–112  review API, corrections, adjustments, approval, reject/return, override, panels, review page
Phase 5 (WP5) Firm feedback                 130–136  firm projection, notice Action, notifications, PDF report, SPA detail, Billing Guidelines
Phase 6 (WP6) Export and index              150–153  AP export, Paid, invoice index
Phase 7 (WP7) Evaluation and rollout        160–163  E2E + seam tests, calibration, accuracy tuning, UAT
Wrap-up                                     190
```

### Critical Path

`003 → 004 → 005 (go/no-go) → 043 → 049 → 100 → 110 → 111 → 160 → 162 → 163 → 190`, with `070 → 071 → 072 → 049` joining at 049, and WP1 `029` gating all server work.

**Blocking dependencies**:
- **Ontology PR #1111** — merged to master 2026-10-15 (`f292ad119`) and into this branch; 015, 026 and 070+ are startable.
- **005 go / no-go** (owner) blocks 043; a no-go switches 043 to the DI-layout + extraction-Action fallback (same facade method).
- **029** (schema applied to dev + exported) blocks all server tasks that read or write new columns.

**High-risk items**: see §8.

---

## 4. Phase Breakdown

Task-level detail (deps, groups, tier, effort) is in [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md); each task's contract is its POML.

### Phase 0: Baseline and AI foundations (WP0)

**Objectives**: know the current Finance pipeline's real state; have an evaluation set; provision models and the analyzer; decide Content Understanding go / no-go on evidence.

| ID | Task | FR |
|---|---|---|
| 001 | Baseline existing Finance pipeline and dev rollups | FR-01 |
| 002 | Assemble evaluation set and manifest | FR-02 |
| 003 | Deploy dev model deployments and Content Understanding defaults | FR-47 |
| 004 | Content Understanding analyzer definition v1 and deploy script | FR-47 |
| 005 | Extraction spike — four arms, coding model comparison, package size; go / no-go | FR-03 |

**Outputs**: `notes/baseline-finance-pipeline.md`, `notes/evaluation-set/manifest.md`, `infrastructure/content-understanding/sprk-invoice-v1.json`, `scripts/content-understanding/Deploy-Analyzers.ps1`, `notes/extraction-spike.md`.

### Phase 1: Data model (WP1)

**Objectives**: every table and column the spec needs, applied to dev and exported to SpaarkeMaster source; server-side invariants for core-record stamping, lifecycle and totals; `sprk_invoicestatus` retired.

| ID | Task | FR |
|---|---|---|
| 010 | Invoice Line columns on `sprk_billingevent` | FR-04 |
| 011 | Retire `sprk_invoicelineitem` | FR-04 |
| 012 | Invoice header schema: core-record fields, totals, supersedes, batch lookup, lifecycle statuscodes | FR-05, FR-06, FR-07, FR-13 |
| 013 | Core-ancestor derivation and one-lookup guard for the invoice family | FR-05 |
| 014 | Invoice lifecycle transition service | FR-06 |
| 015 | Retire `sprk_invoicestatus` — data, readers, forms, views, docs; notify ontology-r1 | FR-06 |
| 016 | Invoice totals calculator | FR-07 |
| 017 | Timekeeper (extend stub) and rate card schema | FR-08 |
| 018 | UTBMS code sets: schema, mapping lookups, seed files, seed script | FR-08 |
| 019 | Policy store extensions, `sprk_ruleset`, visible-to-firms flag | FR-09, D-66 |
| 020 | `sprk_invoicesubmission` schema | FR-10 |
| 021 | `sprk_invoiceapproval` schema | FR-11 |
| 022 | `sprk_externalidentifier` schema and firm-scoped lookup | FR-12 |
| 023 | `sprk_billingexport` schema | FR-13 |
| 024 | `sprk_billingapproverrole` into source; firm billing contact | FR-14 |
| 025 | eBilling settings table and BFF settings reader | FR-48 |
| 026 | Signal / Decision Record invoice-line subject and derivation | FR-15 |
| 027 | Correction record schema | D-64 |
| 028 | Containers: config keys, internal-only access, non-indexed exports, provisioning hand-off | FR-16 |
| 029 | Apply WP1 schema to dev; export SpaarkeMaster source; forms and views | deploy |

### Phase 2: Submission and extraction (WP2)

| ID | Task | FR |
|---|---|---|
| 040 | eBilling feature flag, options and null-object fallbacks | FR-45 |
| 041 | Reshape `IInvoiceAi` to capability methods; consumer-type keys | D-52 |
| 042 | Migrate attachment classification to `INVOICE-CLASSIFY@v1` | FR-46 |
| 043 | Content Understanding client and `ReadInvoiceAsync` | FR-21 |
| 044 | Submission operation and endpoints (SPA + internal) | FR-17 |
| 045 | Concurrency-safe tracking numbers `{invoice}.{n}` | FR-10 |
| 046 | Resolution and combined confidence | FR-23, D-47 |
| 047 | UTBMS coding Action `INVOICE-LINE-CODE@v1` and code-set resolution | FR-22 |
| 048 | AI code-set mapping proposals (admin-confirmed) | FR-22 |
| 049 | `InvoiceSubmissionProcessing` job | FR-20 |
| 050 | Retire the legacy extraction path | FR-21, D-19 |
| 051 | SPA submission UI | FR-18 |
| 052 | `CreateInvoiceWizard` rewired to the internal entry point | FR-19 |
| 053 | Deploy WP2 to dev | deploy |

### Phase 3: Rules (WP3)

| ID | Task | FR |
|---|---|---|
| 070 | Billing rule-type catalogue: schemas, describer, validation, fail-closed refusal | FR-24 |
| 071 | Rule handlers for the 11 billing rule types | FR-24 |
| 072 | `InvoiceComplianceEvaluator` | FR-25 |
| 073 | Rejection rule and reject-eligibility at publish | FR-26 |
| 074 | Billing actions in `DecisionActionCatalog` | FR-30 |
| 075 | Rule drafting Action `BILLING-RULE-DRAFT@v1` and job | FR-27 |
| 076 | Rule set endpoints: proposals, edit, publish, visibility | FR-28 |
| 077 | Rule Review code page | FR-28 |
| 078 | Rate card and timekeeper import and publish (overlap validation) | FR-29, FR-08 |
| 079 | Deploy WP3 to dev | deploy |

### Phase 4: Review and approval (WP4)

| ID | Task | FR |
|---|---|---|
| 100 | Review read model and line decision endpoints | FR-31, FR-07 |
| 101 | Value / code corrections, structured capture, re-evaluation | FR-31, D-64 |
| 102 | Invoice-level adjustments | FR-32 |
| 103 | Approval: eligible approvers, worklist, approve | FR-35, FR-11 |
| 104 | Reject and Return to Firm; resubmission linkage | FR-36 |
| 105 | Override (server) | FR-37, FR-19 |
| 106 | Context panel API | FR-33 |
| 107 | Related work product API | FR-34 |
| 108 | Bounding-box document viewer (shared component) | FR-31 (prototype F-4) |
| 109 | ConsoleKit additions: outcome cards, count filters, disclosure, status labels | FR-31 (prototype F-3) |
| 110 | Invoice Review code page — lines, review pane, corrections, bulk, by-rule | FR-31, FR-32 |
| 111 | Invoice decision wizard and context / related / history panes | FR-33 – FR-36 |
| 112 | Deploy WP4 to dev | deploy |

### Phase 5: Firm feedback (WP5)

| ID | Task | FR |
|---|---|---|
| 130 | Firm-visible findings projection (clause quotes, visibility filtering) | FR-38, D-66 |
| 131 | Firm notice Action `INVOICE-FIRM-NOTICE@v1` with template fallback | FR-50 |
| 132 | Notifications: email, SPA feed, approver in-app | FR-39 |
| 133 | Adjustment report PDF | FR-40 |
| 134 | SPA invoice detail and on-screen report | FR-38 |
| 135 | SPA Billing Guidelines view | FR-49 |
| 136 | Deploy WP5 to dev | deploy |

### Phase 6: Export and index (WP6)

| ID | Task | FR |
|---|---|---|
| 150 | AP export batch (CSV + JSON, D-56 default mapping, snapshot) | FR-41 |
| 151 | Mark Paid and export UI | FR-42 |
| 152 | Invoice index from structured records | FR-43 |
| 153 | Deploy WP6 to dev | deploy |

### Phase 7: Evaluation and rollout (WP7)

| ID | Task | FR |
|---|---|---|
| 160 | End-to-end integration and Action seam tests | NFR-08 – NFR-10, ADR-043 |
| 161 | Calibration report from corrections and the evaluation set | D-47 |
| 162 | Accuracy and performance evaluation; tuning | FR-44, NFR-01 – NFR-06 |
| 163 | UAT with the owner and dev rollout | FR-44, FR-45 |
| 190 | Project wrap-up | — |

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| Ontology PR #1111 (Signals code) on master | Merged 2026-10-15 (`f292ad119`) | Low | Build on it; ontology-r1 reviews changes to its files |
| Azure OpenAI gpt-5.2 / gpt-5.4-mini / gpt-5.6-luna in dev | Not deployed | Medium (quota) | Task 003; cross-region deployments allowed by CU |
| Content Understanding GA `2025-11-01` on `spaarke-docintel-invoices-dev` (West US) | Resource exists | Low | Region supported; analyzer as code |
| Graph PDF conversion | Existing | Low | Reuse |
| ontology-r1 review (FR-09, FR-15, FR-24, FR-30); uac-r2 review (FR-05, FR-15) | Agreed | Medium | Reviews recorded in task notes; cross-session messages |
| customer-provisioning-orchestration (containers for customer environments) | Hand-off | Low | Task 028 files the hand-off; dev containers already exist |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| Finance Intelligence pipeline | `Services/Finance/`, `Services/Jobs/Handlers/Invoice*` | Production code; migrated by this project |
| Action + Binding spine | `Services/Ai/LinearConsumers/` | Current |
| External SPA | `src/client/external-spa/` | Current |
| ConsoleKit / WizardShell / PanelSplitter | `src/client/shared/Spaarke.UI.Components/` | Current |
| Owner prototype | `spaarke-prototype/projects/ebilling.r1` | v1, owner review |

---

## 6. Testing Strategy

Per `docs/adr/ADR-038-testing-strategy.md` and `docs/standards/TEST-ARCHITECTURE.md`; each task's POML states its test scope (no numeric counts).

- **Unit**: rule handlers (must-fire / must-not-fire per type), evaluator determinism, rejection-rule conditions, totals (I-1, percent on fees after line adjustments), transition matrix, tracking-number allocation, combined confidence, code-set resolution, firm-visibility filtering, export mapping.
- **Integration / seam**: submission → processing → invoice (all-or-nothing, idempotent re-delivery); each new Action through `IActionRunner` (ADR-043 seam); authorization (403 for other firm / inaccessible core record / non-eligible approver / missing Override privilege); fail-closed unknown rule type in every consumer (seeded proof — security-class guard); `SignalPairTypeSchemaAgreementTests` and census ArchTests (FR-15).
- **Evaluation**: FR-03 spike and FR-44 on the evaluation set (NFR-04 – NFR-06), latency p90 (NFR-01, NFR-02).
- **UI**: `<ui-tests>` per frontend task incl. ADR-021 dark mode; UAT script (163).

---

## 7. Acceptance Criteria

Graduation criteria = spec "Success Criteria" 1 – 12 (mirrored in README). Phase gates:
- **Phase 0**: evaluation set manifest complete; spike results and owner go / no-go recorded.
- **Phase 1**: schema in dev and SpaarkeMaster source; transition matrix and totals tests green; `sprk_invoicestatus` gone from code, forms, views and docs; FR-15 schema-agreement and census tests green.
- **Phase 2**: an SPA submission produces an invoice or a rejection with findings; re-delivery creates no duplicates; no caller of `ExtractInvoiceFactsAsync` / `GetPlaybookByNameAsync` remains.
- **Phase 3**: 11 rule types with must-fire / must-not-fire tests; billing rule body refused by the Ontology compiler; a published rule set with clause citations from a real OCG.
- **Phase 4**: review → approve with I-1 totals and one Decision Record; reject / return / override paths; review page UI tests.
- **Phase 5**: firm sees outcomes, clause quotes, Billing Guidelines; email + PDF report.
- **Phase 6**: export batch CSV + JSON in the output container; Sent to AP → Paid; invoice searchable within 5 minutes.
- **Phase 7**: NFR-04 – NFR-06 met or owner-accepted; UAT signed off.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Content Understanding misses accuracy targets on law-firm invoices | Medium | High | FR-03 three-arm spike + preview arm; fallback DI layout + extraction Action behind the same facade method |
| R2 | Ontology rule / Decision Record contracts change under us after #1111 | Low | Medium | ontology-r1 reviews FR-09, FR-15, FR-24, FR-30 PRs; message before changing shared files |
| R3 | Model quota / region for gpt-5.x in dev | Medium | Medium | Task 003 early; CU allows cross-region deployments |
| R4 | UTBMS coding below 80% top-1 | Medium | Medium | Code guidance text; firm-supplied codes; low-confidence codes flagged, never silently used; tuning platform later |
| R5 | Confidence miscalibration causes a wrongful rejection | Low | High | Combined confidence (D-47), 0.90 threshold, reject-eligible types only, calibration report (161), NFR-06 test |
| R6 | `sprk_invoicestatus` retirement breaks ontology rollups | Medium | High | FR-06 reader list; ontology-r1 reviews its files; notify on PR open |
| R7 | Signal / DR schema change conflicts with ontology or uac-r2 | Medium | Medium | Conditions agreed (FR-15); schema-agreement and census tests; reviews |
| R8 | BFF publish size from the CU SDK | Low | Medium | Measured in 005 and per task; ≥ +5 MB needs justification |
| R9 | Live dev changes (schema, Azure deployments) without owner OK | Low | Medium | Deploy tasks are prescriptive with an owner-OK escalation trigger |

---

## 9. Next Steps

1. Owner compacts the session, then starts with `work on task 001` (Wave 0-1: 001, 002, 003 can run in parallel; 040 and 041 can start alongside — see TASK-INDEX).
2. Ontology #1111 is merged (`f292ad119`); notify ontology-r1 when the `sprk_invoicestatus` and schema PRs open.
3. Owner go / no-go at the end of 005.

---

**Status**: Ready for Tasks
**Next Action**: Execute Wave 0-1 (`001`, `002`, `003`) and Wave 2-0 (`040`, `041`).
