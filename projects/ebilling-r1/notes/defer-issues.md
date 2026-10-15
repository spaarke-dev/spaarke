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
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1934 |

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
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1935 |

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
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `Set-PrimaryNameRequired` now runs for `sprk_billingcodeset.sprk_name` (ApplicationRequired) and `sprk_billingcode.sprk_code` is SystemRequired (alternate-key column), both in dev; the helper lives in `scripts/ebilling/EbillingSchema.psm1` (`notes/schema-script-hardening.md`). |
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

### DEF-007 — `sprk_ruleset` primary name is not application-required in dev

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `Deploy-EbillingPolicyStoreSchema.ps1` calls `Set-PrimaryNameRequired` for `sprk_ruleset.sprk_name`; dev now reads ApplicationRequired. |
| **Urgency** | before admins create rule sets by hand (task 076) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 020 (found while fixing the same defect in its own script) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1937 |

**Description**

`Deploy-EbillingPolicyStoreSchema.ps1` (task 019) asks for `ApplicationRequired` on `sprk_ruleset.sprk_name` in the entity-create body. Dataverse ignores that property; dev reads back `RequiredLevel = None`. A form or API write can create a rule set with an empty name. The rule set name is shown to firms and cited on every finding (D-66, D-79), so a nameless rule set yields a finding that cites nothing. Same class as DEF-006.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1` (`Add-Table`, then `Set-PrimaryNameLength`)
- `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (`Set-PrimaryNameRequired`, the model fix)

**Suggested fix**

Add the `Set-PrimaryNameRequired` step to the 019 script after `Set-PrimaryNameLength` and re-run it in dev (existing rows are unaffected).

**Estimated effort**: 30 minutes

**Blockers**: none

**Related**: DEF-006; ebilling-r1 tasks 019, 020

---

### DEF-008 — eBilling schema scripts fail a re-run on a stale lookup read (`0x80072551` is not treated as already-exists)

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `0x80072551` (and every other duplicate code) is recognised by one `Test-DuplicateRefusal` in `scripts/ebilling/EbillingSchema.psm1`, which all seven schema scripts now import; confirmed on a settled read before it counts as skipped. |
| **Urgency** | before the next schema script re-run (tasks 021-028 should copy the fixed helper) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 020 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1938 |

**Description**

A lookup (relationship) create that hits an existing relationship returns `0x80072551: NavigationPropertyName ... is not unique within an entity`. The scripts only treat "already exists" as a skip, so a re-run whose existence read is stale stops with exit 1 instead of reporting no change. "A second run reports no change and exits 0" is an acceptance criterion of every schema task. Nothing is lost (the scripts are re-runnable). Fixed in `Deploy-EbillingSubmissionSchema.ps1` (`Invoke-DvCreate`).

**Entry-points**

- Not fixed in: `Deploy-EbillingInvoiceLineSchema.ps1`, `Deploy-EbillingInvoiceHeaderSchema.ps1`, `Deploy-EbillingTimekeeperRateCardSchema.ps1`, `Deploy-EbillingBillingCodeSchema.ps1`, `Deploy-EbillingPolicyStoreSchema.ps1`, `Deploy-EbillingApprovalSchema.ps1` (grep: none contains `0x80072551`; not individually verified)
- `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (`Invoke-DvCreate`, the model fix)

**Suggested fix**

Add `0x80072551` to the duplicate test of each script's lookup-create helper (confirm on a settled read, throw if the column never becomes readable), or move the helper into one shared file.

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-006, DEF-007; ebilling-r1 task 020

---

### DEF-009 — ApplicationRequired is not enforced on API writes; eBilling schema scripts and docs rely on it (SystemRequired is)

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: SystemRequired (null-row guarded) on `sprk_ratecardrate.sprk_ratecard`, `sprk_billingcode.sprk_codeset` / `sprk_code`, `sprk_invoicesubmission.sprk_vendororg` / `sprk_trackingnumber`; other columns stay ApplicationRequired with scripts and `docs/data-model/*.md` corrected; per-column table in `notes/schema-script-hardening.md`. |
| **Urgency** | before any writer relies on a "required" column from schema 017-020 |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1939 |

**Description**

Verified live: a Web API create of `sprk_invoiceapproval` without the lookup marked ApplicationRequired succeeded. After raising `sprk_invoice` to SystemRequired and publishing, the same create is refused (`Attribute 'sprk_invoice' cannot be NULL`). Scripts, comments and docs of tasks 017-020 say an API write without the value is refused; only forms refuse. Where a null breaks an invariant, nothing but a server-side writer stands between the data and a broken row.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (`sprk_ratecardrate.sprk_ratecard`, line 665; `Set-PrimaryNameRequired` comment)
- `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (`sprk_codeset`, `sprk_codetype`, lines 531, 533)
- the primary-name steps and comments in the 019 and 020 scripts; the matching data-model docs (grep, not individually verified)
- `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1` (`Set-LookupRequiredLevel`, the working model)

**Suggested fix**

Raise the level to SystemRequired for each lookup or column whose null breaks an invariant (idempotent PUT of the attribute plus publish, as in `Set-LookupRequiredLevel`); keep primary names at ApplicationRequired and correct the comments and docs. Check that no writer, seed or import creates rows without the value first.

**Estimated effort**: 2 hours

**Blockers**: none

**Related**: DEF-006, DEF-007; ebilling-r1 tasks 017, 018, 019, 020, 021

---

### DEF-010 — eBilling schema scripts: table-create duplicate (`0x8009050d`) not recognised and false "has rows" in `Set-PrimaryNameLength`

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `0x8009050d` is in `Test-DuplicateRefusal` and `Set-PrimaryNameLength` counts rows with `$count=true` (no false "has rows"), both once in `scripts/ebilling/EbillingSchema.psm1`. |
| **Urgency** | before the next schema script re-run (tasks 022-028 should copy the fixed helpers) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1940 |

**Description**

(1) A table create whose existence read was stale is refused with `0x8009050d: Entities already exist`; the helpers match only `already exists` and a few codes, so the re-run stops with exit 1 instead of reporting no change. (2) Right after a table create, `Set-PrimaryNameLength` judged `@($rows).Count -gt 0` true on an empty table and left the primary name at 850 until the next run. Both fixed in `Deploy-EbillingApprovalSchema.ps1` (`Add-Table` / `Invoke-DvCreate` regex; row count by `$count=true` repeated until a count is returned).

**Entry-points**

- Regex not fixed in: `Deploy-EbillingBillingCodeSchema.ps1`, `Deploy-EbillingInvoiceHeaderSchema.ps1`, `Deploy-EbillingInvoiceLineSchema.ps1`, `Deploy-EbillingPolicyStoreSchema.ps1`, `Deploy-EbillingSubmissionSchema.ps1`, `Deploy-EbillingTimekeeperRateCardSchema.ps1` (grep for `already exists|0x80047013`)
- Row count not fixed in: the 017, 018, 019 and 020 scripts (grep for `@($rows).Count`)
- `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1` (the model fix)

**Suggested fix**

Port both changes to the other scripts, or move the helpers into one shared file so the next fix lands once. Nothing is lost today (the scripts are re-runnable).

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-008; ebilling-r1 tasks 017-021

---

### DEF-011 — `sprk_matter.sprk_billingapproverrole` is single-select; Project and Work Assignment are multi-select (D-33 / task 024)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-15 - owner OK; Matter column recreated as multi-select in dev (task 024) |
| **Urgency** | before task 024 and task 103 start |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1941 |

**Description**

D-33, FR-14 and task 024 describe `sprk_billingapproverrole` as multi-select on Matter, Project and Work Assignment. In dev, Project and Work Assignment hold a `MultiSelectPicklistAttributeMetadata`, but Matter holds a plain `PicklistAttributeMetadata` (one value). Approver resolution (task 103) cannot store or read the default "Assigned Attorney 1 + Assigned To Internal" on a Matter. Also noted: Matter and Project have no `sprk_assignedto` lookup, so the option "Assigned To" resolves only on Work Assignment. All assignment lookups that exist target `contact` (task 021's pre-check), so the eligible-approver store is unaffected.

**Entry-points**

- `sprk_matter.sprk_billingapproverrole` (dev metadata, read-only check 2026-10-15)
- `projects/ebilling-r1/tasks/024-approver-role-billing-contact.poml`; task 103

**Suggested fix**

In task 024 decide with the owner: replace the Matter column with a multi-select (a column type cannot change in place, so this is destructive and needs the owner's OK and a check that no Matter row has a value) or accept single-select on Matter and amend the spec. Task 103 handles both shapes until then.

**Estimated effort**: 1-2 hours

**Blockers**: owner decision (destructive change)

**Related**: D-33, D-54; ebilling-r1 tasks 024, 103

**Evidence (task 024, 2026-10-15, dev spaarkedev1)**

- Rows: `sprk_matter` 65 total (62 on the final read; test rows are being deleted by other work), **0 hold a value** in `sprk_billingapproverrole` (counted by `ne null` and by reading every row). Project 0 of 23, Work Assignment 0 of 22.
- Dependencies (`RetrieveDependenciesForDelete`, component type 2): **1**, the platform-generated `aiskillconfig` `ais_FormFillFieldOptOut_sprk_matter_sprk_billingapproverrole` (type 10314, Default solution), created with the column. No form (5 forms), view (9), personal view (1), chart or workflow / business rule of `sprk_matter` references the column; no other table's dependency is listed. So no form or view needs re-adding after the replacement.
- The column and the global choice are already SpaarkeMaster components (`sprk_matter` is a whole-table component).
- Prepared path: `scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1 -ReplaceMatterApproverRole`. It re-runs the guards and refuses (exit 2, nothing changed) when any Matter row holds a value, when anything other than that skill config depends on the column, or when a form / view / chart / workflow references it; otherwise it deletes the single-select column, waits until it is gone and creates `sprk_billingapproverrole` as a multi-select on the same global choice, then verifies and publishes. `-WhatIf -ReplaceMatterApproverRole` runs the guards without writing. Without the switch the script only reports the evidence and lists the column as deferred. **The switch has not been run** (it needs the owner's OK: deleting a column).
- The "Assigned To" option: Matter and Project have no `sprk_assignedto`; task 024 does not depend on it. Task 103 must treat option 100000006 as "no approver" on Matter and Project.
- Side effect to check on first use: whether the platform recreates the `FormFillFieldOptOut` skill config for the new column (it is deleted with the old one). Harmless either way.

**Resolution (2026-10-15)**: owner OK given. The column delete was first refused (0x8004f01f) by the generated `aiskillconfig`; the script now deletes that config first. `-ReplaceMatterApproverRole` then recreated `sprk_matter.sprk_billingapproverrole` as a multi-select on the global choice (0 rows before and after); re-run reports no change. #1941 closed.

---

### DEF-012 — eBilling schema scripts: `Set-PrimaryNameRequired` can undo the primary-name length change on a stale read

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: the shared module (`EbillingSchema.psm1`) waits for a settled read of the length `Set-PrimaryNameLength` set before the required-level PUT and checks it again afterwards (commit `0cfeec7c1`); the last two scripts that carried their own helper copies, `Deploy-EbillingExternalIdentifierSchema.ps1` (022) and `Deploy-EbillingExportSchema.ps1` (023), now import the module (the module gained `Set-PicklistRequiredLevel` for 022's choice key column). Evidence, dev spaarkedev1: both scripts re-run with `created : 0  changed : 0  skipped : 20` (022, key Active) and `skipped : 12` (023), "nothing changed - no publish", exit 0; 020 and 018 (module users) re-run unchanged after the module edit. |
| **Urgency** | before the next new-table schema script run (tasks 022 and 024-028 copy these helpers) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 023 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1942 |

**Description**

`Set-PrimaryNameLength` shortens a new table's primary name (850 to N); `Set-PrimaryNameRequired` then reads the attribute and PUTs the whole attribute back with ApplicationRequired. When that read still shows the old MaxLength (850), the second PUT silently restores it. Seen on the first run of `Deploy-EbillingExportSchema.ps1`: the log said "CHANGED max length 850 -> 100", verification then read 850, exit 1; a second run fixed it. Nothing is lost (empty table, re-runnable).

**Entry-points**

- `scripts/ebilling/EbillingSchema.psm1` (`Set-PrimaryNameRequired` / `Set-ColumnRequiredLevel`, same read-modify-PUT)
- `scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1` (own copy, task 022)
- model fix: `scripts/ebilling/Deploy-EbillingExportSchema.ps1` (`Set-PrimaryNameRequired` takes the wanted length and waits up to 60 s until the read shows it)

**Suggested fix**

Make the required-level update wait for a settled read of the length the previous step set (or set both in one PUT), once, in the shared module.

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-010; ebilling-r1 tasks 017-023

---
