# Task 041 — IInvoiceAi capability contract (for 042 / 047 / 075 / 131)

Code: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` — `IInvoiceAi.cs`, `InvoiceAi.cs` (facade + `InvoiceAiOutputParser`), `NullInvoiceAi.cs`, `InvoiceAiContracts.cs` (DTOs + `InvoiceAiException`), `ConsumerTypes.cs`.

## What each Action task must keep in step

The facade serializes the request as the `## Input` operand (camelCase) and parses the Action output strictly. The Action's `sprk_inputschema`, its output schema (`infra/dataverse/outputschemas/`) and `InvoiceAiOutputParser` must agree. Change all three together.

| Method | Consumer type (joins `ConsumerTypes.All` in the task that seeds the Binding) | Operand sent | Output parsed (required = fails closed if missing) |
|---|---|---|---|
| `ClassifyAsync` | `invoice-classify` (042) | `documentText` | `classification` (InvoiceCandidate / NotInvoice / Unknown, required), `confidence` (0–1, required), `hints` (object or null: vendorName, invoiceNumber, invoiceDate, totalAmount, currency, matterReference), `reasoning` (optional) |
| `CodeLinesAsync` | `invoice-line-code` (047) | `lines[]` {lineKey, date yyyy-MM-dd, lineType Fee/Expense, narrative, hours, amount, billedTaskCode, billedActivityCode, billedExpenseCode}; `allowedTaskCodes[]`, `allowedActivityCodes[]`, `allowedExpenseCodes[]` {code, description, guidance} | `lines[]` {lineKey (req), taskCode / activityCode / expenseCode (string or null), confidence (req), observations[] (req, may be empty) {type BlockBilling/VagueDescription, confidence, quotedText — all req}} |
| `DraftRulesAsync` | `billing-rule-draft` (075) | `documentText`; `ruleTypes[]` {ruleType, description, parameterSchema (JSON object, not a string)}. `documentId` is logged only, not sent. | `proposals[]` {ruleType, parameters (JSON object → `ParametersJson` raw text), consequence, clauseQuote (all req), clauseLocation (opt)}; `notConverted[]` {text, reason (req), location (opt)} |
| `DraftFirmNoticeAsync` | `invoice-firm-notice` (131) | `outcome` (SubmissionRejected / ReturnedToFirm / InvoiceRejected / ApprovedWithAdjustments); `findings[]` {ruleSetName, ruleName, consequence, issue} — **no numeric member** in request or result (FR-50; guarded by `InvoiceAiContractTests`) | `subject`, `body` (both required, non-empty) |

Tenant id travels in `LinearRunContext.TenantId`, never in the operand.

## Failure contract (FR-46)

- `InvoiceAiException` (one new type, in `InvoiceAiContracts.cs`) with `ReasonCode` + `ConsumerType`:
  - `ai.invoice.binding_missing` — `IActionResolver` threw its documented `InvalidOperationException` (no Action routed / Action row missing). Permanent until the Binding is seeded.
  - `ai.invoice.completion_failed` — the run failed (model, transport, non-JSON output, misconfigured Action row), **or** the resolver failed with anything other than `InvalidOperationException` (e.g. a transient Dataverse read). Retryable.
  - `ai.invoice.output_invalid` — output missing a required member, wrong JSON kind, unknown enum name, numeric enum string, or a confidence outside 0–1 (refused, not clamped, because FR-26 compares it to a threshold).
- Passed through unchanged: `FeatureDisabledException` (kill switch — `ai.invoice.disabled` from `NullInvoiceAi`, `ai.linear-consumers.disabled` from the Null Action primitives) and caller cancellation.
- The facade never returns a default and never falls back to a prompt. Fallbacks the spec allows (131's fixed template) are the caller's.
- Logs: consumer type, reason code, subject id (DraftRules document id), exception type name, failing JSON path. The inner exception is not logged by the facade (its message could carry model output); it rides on the thrown exception.

**Why a new exception type (§11).** Existing: `FeatureDisabledException` (kill switch → 503; reusing it would turn a missing Binding into a "feature disabled" 503), `PlaybookNotFoundException` (by-name playbooks, the thing D-52 retires), `DispatchRejectedException` (chat dispatch). Extension: none of them carries a reason code with these semantics. Cost of doing nothing: 042 / 049 / 075 could not tell a permanent misconfiguration from a retryable failure, and FR-46's "fails closed with a logged reason" has no stable code.

## Not in this facade (callers own it)

- 047: chunking (FR-48), refusing any code outside the line's code set (FR-22), checking that every requested `lineKey` came back exactly once.
- 075: validating each proposal against its rule type (unknown type / schema-invalid → dropped and listed).
- 131: template insertion of amounts, line references, clause quotes; fallback template when the call fails or is disabled.
- Concurrency bounds on AI calls (ADR-016) — the callers' job loops.

## Deviations from the POML

- **Test placement.** The POML named `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PublicContracts/InvoiceAiCapabilityTests.cs`. `tests/CLAUDE.md` (owner decision 2026-10-12) requires new test files under a KEEP path, and mocking `IActionResolver` / `IActionRunner` is B5 when the real ones are cheap. So every per-method behaviour (operand, parse, the three failure codes) runs through the **real** `ActionResolver` + `ActionRunner` in `tests/integration/seam/Finance/InvoiceAiActionSeamTests.cs`, doubling only the Binding table, the Action row and the LLM. The Null-peer and FR-50 / FR-22 shape checks (no mocks) are in `tests/unit/domain/Ai/PublicContracts/InvoiceAiContractTests.cs`.
- **Tests beyond the named edge cases** (one line each): out-of-range confidence / bad enum (FR-26 threshold safety); transient resolver failure → `completion_failed` (retry semantics); kill-switch pass-through (keeps the ADR-032 503 contract); no-numeric request + finding types (131's escalation trigger).
- **Binding `sprk_modeltieroverride` is not applied on the linear path** (`ActionResolver` returns the Action; only `SessionDispatchOrchestrator` applies the override). Spec "Rules for the AI steps" says the model is the Action's tier *with* the Binding override. Set the tier on the Action row (D-51) until the platform gap closes — filed as ISS-005. Also found in passing: the frozen `## Input` renderer escapes non-ASCII as `\uXXXX` (ISS-006) — relevant to 075's clause quotes.
