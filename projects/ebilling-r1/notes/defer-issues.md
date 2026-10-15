# Defer / Issue Tracking — ebilling-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label ebilling-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — InvoiceLiveFactResolver reads non-existent sprk_invoice columns (sprk_total, sprk_status)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | `/project-pipeline` task authoring (POML 015 discovery) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1904 |

**Description**

`InvoiceLiveFactResolver` reads `sprk_total` and `sprk_status` from `sprk_invoice`. Neither column exists: the table has `sprk_totalamount`, and status is `statecode` / `statuscode` plus the legacy `sprk_invoicestatus`. Requesting a column that doesn't exist fails the Dataverse retrieve, so the invoice live facts fail or come back empty.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs:70-77`, `:142-143`, `:297-316`
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`

**Suggested fix**

Read `sprk_totalamount` and `statuscode`, in task 015's scope.

**Estimated effort**: 1–2 hours

**Blockers**: none

**Related**: #1771. The private invoice-status copies tracked there now carry an added comment: `FinancialCalculationToolHandler.cs:44-45` uses status values 100000002 and 100000003, which do not exist. Both are fixed in scope by task 015.

---

### DEF-001 — Customer stamps need gpt-5.2 / gpt-5.4-mini and a Content Understanding region (hand-off to customer-provisioning-orchestration-r1)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (step 8 hand-off) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1915 |

**Description**

eBilling R1 deployed `gpt-5.2` (Content Understanding extraction, FR-47, D-51, D-53) and `gpt-5.4-mini` (UTBMS coding, D-46) in dev, with a CU resource whose model defaults map to them. Customer stamps built from `openai.bicep` / `PinnedModelCatalog` have neither model and no CU-capable (AIServices) resource. Without them the extraction and coding Actions fail on a stamp: CU analyze has no completion deployment, and the coding Action resolves a deployment that does not exist. gpt-4o / gpt-4o-mini retire 2027-04-14.

**Entry-points**

- `infrastructure/bicep/modules/openai.bicep` and `PinnedModelCatalog` (owned by customer-provisioning-orchestration-r1; not edited by ebilling-r1).
- CU GA regions (api 2025-11-01): australiaeast, canadacentral, eastus, eastus2, japaneast, southcentralus, southeastasia, swedencentral, uksouth, westeurope, westus, westus3 (westus2 is NOT listed). Cross-region Bring Your Own Capacity is supported.
- Dev reference inventory: `projects/ebilling-r1/notes/003-model-deployments.md`.

**Suggested fix**

Add the model pins and a CU resource + `PATCH contentunderstanding/defaults` step to the stamp template or handlers (single owner for the pin catalog), with `Cognitive Services Content Understanding Reader` for the stamp BFF identity in the runtime RBAC module (keyless, ADR-028 A6).

**Estimated effort**: unknown - needs design in customer-provisioning-orchestration-r1.
**Blockers**: none for eBilling R1 (dev only); needed before any customer-stamp eBilling go-live.
**Related**: ebilling-r1 tasks 003, 004, 043.

---

### DEF-002 — Dev BFF still uses key-based Azure OpenAI / Document Intelligence settings

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | backlog |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (found, not caused) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1916 |

The dev App Service has `Ai__OpenAiKey`, `AzureOpenAI__ApiKey`, `DocumentIntelligence__OpenAiKey` and `DocumentIntelligence__DocIntelKey` (values not read). ADR-028 A6 requires keyless access. Out of eBilling scope; the new Content Understanding path is keyless.

---

### DEF-003 — Dev deployment `gpt-4o-mini` serves gpt-4.1-mini (deprecated 2027-04-14)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | backlog |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (found, not caused) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1917 |

The deployment name misdescribes its model and the model retires 2027-04-14. eBilling does not use it (coding uses `gpt-5.4-mini`, D-46).

---

### ISS-002 — Dev cannot run the legacy Finance extraction pipeline (no playbook-id setting, no FinanceExtraction / FinanceClassification playbooks, no gpt-4o deployment)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline; escalation trigger 1) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1918 |

**Description**

`spaarke-bff-dev` has no `Finance__InvoiceExtractionPlaybookId` setting, so `InvoiceExtractionJobHandler` marks every invoice Failed at `:329-336`. The `FinanceExtraction` and `FinanceClassification` playbook rows that `InvoiceAnalysisService` loads by name do not exist in dev (and have no seed in the repo), and the dev OpenAI account has no `gpt-4o` deployment (the `FinanceOptions` extraction default). Any Confirm in dev enqueues a job that cannot succeed.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:328-336`
- `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceAnalysisService.cs:35,41`
- `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs:37,42`

**Suggested fix**

None before 044: the path is replaced by Content Understanding + Actions (D-52) and deleted in 050. Owner decides whether to repair dev for an interim run.

**Estimated effort**: 1-2 hours (config + seed), only if the owner wants an interim run
**Blockers**: owner decision (task 001 escalation)
**Related**: ebilling-r1 tasks 001, 044, 050; evidence `notes/baseline-finance-pipeline.md`

---

### ISS-003 — Legacy InvoiceExtraction persists nothing on success (output mapping unreachable, placeholder loads, no Extracted status, no billing events)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (superseded by 050 deletion) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1919 |

**Description**

The handler's only success-path write is `OutputOrchestrator.ApplyOutputMappingAsync`, which is a no-op because `PlaybookService.GetPlaybookAsync` never selects `sprk_configjson`; `LoadInvoiceRecordAsync` (and the indexing handler's loads) are placeholders; success never sets the extraction status; no `sprk_billingevent` rows are ever written by this path. Related existing issues: #229, #233, #1087 (wrong-entity failure-status write).

**Entry-points**

- `InvoiceExtractionJobHandler.cs:339, 499-527, 559-583`
- `Services/Ai/PlaybookService.cs:223-274`; `Services/Ai/OutputOrchestratorService.cs:317-346`
- `InvoiceIndexingJobHandler.cs:259-328`

**Suggested fix**

No fix: task 050 deletes the legacy extraction path (D-71); 044/152 build the replacement and the real indexing inputs.

**Estimated effort**: n/a (deleted by 050)
**Blockers**: none
**Related**: #229, #233, #1087; ebilling-r1 tasks 044, 050, 152

---

### ISS-004 — AttachmentClassification job is never enqueued; finance architecture doc describes triggers and writes that do not exist

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1920 |

**Description**

No code in `src/` submits an `AttachmentClassification` job, and `EmailToDocumentJobHandler` (named as the trigger in `docs/architecture/finance-intelligence-architecture.md`) does not exist. Dev has 0 classified documents. The same doc also claims extraction writes billing events and that the snapshot job runs after extraction; neither is true.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AttachmentClassificationJobHandler.cs:38`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs:156-159, 168-174`
- `docs/architecture/finance-intelligence-architecture.md` (data flow steps 1-4, 7)

**Suggested fix**

Task 042 migrates classification to an Action + Binding with its own trigger; correct the architecture doc in the eBilling docs work.

**Estimated effort**: 30 min (doc), trigger design is task 042
**Blockers**: none
**Related**: ebilling-r1 tasks 001, 042; rollup evidence is on existing issue #1663 (comment 2026-10-15)

---

### ISS-005 — Linear AI path ignores the Binding's sprk_modeltieroverride

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before 047 / 075 tune their tier |
| **Filed** | 2026-10-15 |
| **Source** | Task 041 (found in passing; platform code, not changed) |
| **GitHub Issue** | _to file (main session — sub-agent did not create it)_ |

**Description**

Spec "Rules for the AI steps" says the model is the Action's tier *with* the Binding's `sprk_modeltieroverride`. On the linear path (`IActionResolver` → `IActionRunner`, which every `IInvoiceAi` capability method uses) the override is never applied: `ActionResolver.ResolveAsync` returns the Action row, and `ActionRunner` resolves `action.ModelTier` only. Only `SessionDispatchOrchestrator` (chat dispatch) applies `binding.ModelTierOverride`. A maker who sets the override on an eBilling Binding sees no effect.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionResolver.cs:44-72`
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs:144` (`ModelTierDeploymentResolver.Resolve(action.ModelTier, …)`)
- Contrast `Services/Ai/Chat/SessionDispatchOrchestrator.cs:630`

**Suggested fix**

Platform (ADR-043 engine owner): have the linear resolver return the Action with the Binding's override folded into its tier. Not in 041 scope (its escalation trigger forbids ActionRunner changes). Workaround for R1: set the tier on the Action row (D-51).

**Estimated effort**: 1–2 hours plus a seam test

**Blockers**: platform owner decision

**Related**: ebilling-r1 tasks 041, 042, 047, 075, 131; ADR-016, ADR-043

---

### ISS-006 — `## Input` operand escapes every non-ASCII character as \uXXXX

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | low (cost, not correctness) |
| **Filed** | 2026-10-15 |
| **Source** | Task 041 code review (found in passing; frozen platform format, not changed) |
| **GitHub Issue** | _to file (main session — sub-agent did not create it)_ |

**Description**

`PromptInputSection.Render` serializes the operand with `new JsonSerializerOptions { WriteIndented = true }`, whose default `JavaScriptEncoder` escapes every non-ASCII and HTML-sensitive character (`€` → `€`, `§` → `§`, `'` → `'`). Document text sent through `## Input` (`ClassifyAsync`, `DraftRulesAsync`) therefore costs several tokens per such character and is harder for the model to quote back verbatim — relevant to `clauseQuote` in rule drafting. The format is golden-pinned (ADR-043 frozen contract), so a fix is a platform decision.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/PromptInputSection.cs:37`
- Golden test `tests/integration/seam/Ai/ContextBinderActionRunnerSeamTests.cs` (`PromptInputSection_Render_ProducesTheFrozenGoldenFormat`)

**Suggested fix**

Use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (prompt text is not HTML) and re-pin the golden test, coordinated with the ADR-043 engine owner. Measure in 075's evaluation whether clause quotes are affected first.

**Estimated effort**: 1 hour plus re-pinning

**Blockers**: platform owner decision

**Related**: ebilling-r1 tasks 041, 075; ADR-043

---

### DEF-004 — SpendSnapshotService ignores sprk_costtype and approved amounts: new Adjustment lines would be summed as spend

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before the first Adjustment line is stored as Invoiced (tasks 014 / 016 / 100) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 010 (reader of a column task 010 extended; not changed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1923 |

**Description**

`SpendSnapshotService` reads `sprk_costtype` into `BillingEventData.CostType` but never uses it. Spend is `billingEvents.Sum(e => e.Amount)` over every line of the matter / project with `sprk_visibilitystate = Invoiced`, summing the billed `sprk_amount`. Task 010 added the option Adjustment (100000002) to `sprk_costtype`, plus `sprk_approvedamount` and `sprk_linestatus`. Once Adjustment lines are stored as Invoiced, their `sprk_amount` (never negative: the column is 0 .. 1,000,000,000) is added to spend instead of reducing it, and Disallowed / Reduced lines still count at the billed amount. Not reachable today: nothing writes those lines yet.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs:185, 235, 253, 268, 286, 402, 455-459`

**Suggested fix**

Settle the spend definition with the line writers (exclude or sign Adjustment lines; sum `sprk_approvedamount`, falling back to `sprk_amount` when unreviewed) and add a unit test.

**Estimated effort**: 1-2 hours plus test

**Blockers**: owner decision on the spend definition for adjusted lines

**Related**: ebilling-r1 tasks 010, 014, 016, 100; evidence `notes/010-invoice-line-columns.md`

---

### DEF-005 — UTBMS LOC 2013 revised activity/expense lists and IP expense codes E125-E131 are not seeded

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before IP (patent / trademark) or eDiscovery customers' invoices are coded |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 018 (UTBMS code sets) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1929 |

**Description**

Task 018 seeded the original ABA Activity (A101-A111) and Expense (E101-E124) lists, which utbms.com publishes as clean spreadsheets. Not seeded: the LOC 2013 revised Activity list (A112-A128, incl. discovery / eDiscovery codes) and Expense list (X-series), and the seven IP expense codes E125-E131. utbms.com publishes them only as PDFs whose tables do not extract reliably (code and name columns misalign), so rows could not be produced with confidence. A patent / trademark / eDiscovery line carrying one of these codes is outside the seeded set: FR-22 cannot propose it and a firm-supplied one is flagged as a mismatch.

**Entry-points**

- `scripts/seed-data/ebilling/utbms-activity.csv`, `utbms-expense.csv`
- `scripts/ebilling/Seed-EbillingBillingCodes.ps1` (create-only: re-running after adding rows adds them without touching existing rows)

**Suggested fix**

Owner decides the product standard (original ABA, LOC 2013 revised, or ABA plus E125-E131). Then add hand-verified rows to the CSVs and re-run the seed.

**Estimated effort**: 2-3 hours plus the owner decision

**Blockers**: owner decision on which revision is the standard

**Related**: ebilling-r1 tasks 018, 047; evidence `notes/018-utbms-code-sets.md`

---

### DEF-006 — Billing code / code set primary names are not application-required in dev

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before admins edit code sets by hand |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 017 (found while fixing the same defect in its own script) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1931 |

**Description**

`Deploy-EbillingBillingCodeSchema.ps1` (task 018) asks for `ApplicationRequired` on the primary names `sprk_billingcode.sprk_code` and `sprk_billingcodeset.sprk_name` in the entity-create body. Dataverse ignores that property (like the primary-name `MaxLength`); dev reads back `RequiredLevel = None` on both. A form or API write can create a code or code set with an empty name, which the alternate key (`sprk_codeset`, `sprk_code`) cannot address.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (`Add-Table`, then `Set-PrimaryNameLength`)
- `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (`Set-PrimaryNameRequired`, the model fix)

**Suggested fix**

Add the `Set-PrimaryNameRequired` step to the 018 script after table creation and re-run it in dev (existing rows are unaffected).

**Estimated effort**: 30 minutes

**Blockers**: none

**Related**: ebilling-r1 tasks 017, 018; `notes/017-timekeeper-ratecard-schema.md` sections 6 and 7

---
