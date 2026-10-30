# Task 042 - Attachment classification migrated to INVOICE-CLASSIFY@v1

Status: done. Spec FR-46, D-52. Consumer type and Action code: `invoice-classify` (lowercase kebab, no version suffix; `INVOICE-CLASSIFY@v1` is the display name in the description and the JPS `$version`).

## What changed

- `AttachmentClassificationJobHandler` now calls `IInvoiceAi.ClassifyAsync` (constructor takes `IInvoiceAi` instead of `IInvoiceAnalysisService`) and maps the result to the existing `DocumentClassification` / `InvoiceHints` / Dataverse write by name.
- Fail closed (FR-46), no prompt fallback, no document text in logs (ADR-015):
  - `InvoiceAiException` `binding_missing` and `completion_failed`: `JobOutcome.Failure` while `Attempt < MaxAttempts`, then `Poisoned`. The reason code is logged and passed to `RecordClassificationFailure`. (`binding_missing` is not proof the Binding is absent - a transient routing read gives the same code, platform issue #1936 - so it is retried a bounded number of times.)
  - `output_invalid`: `Poisoned` at once.
  - `FeatureDisabledException` (Null facade, ADR-032): `Poisoned`, reason `ai_disabled`, nothing written, idempotency key not marked.
- Removed: `IInvoiceAnalysisService.ClassifyAttachmentAsync` and its implementation, `InvoiceAnalysisService.ClassificationPlaybookName`, `FinanceJsonSchemas.ClassificationResultSchema`, `FinanceOptions.ClassificationDeploymentName` (no appsettings entry existed), and the `ClassificationResult` record (its only use was the removed call; the `DocumentClassification` enum stays). Kept: `InvoiceHints`, `ExtractionResultSchema`, `ExtractionDeploymentName`, `GetPlaybookByNameAsync`, `GetStructuredCompletionAsync`, `ExtractInvoiceFactsAsync` (task 050 removes the rest).
- `ConsumerTypes.All` gained `InvoiceClassify` (28 entries), with the Binding row.
- New files: `infra/dataverse/actions/invoice-classify.action.json` (JPS-shaped, Fast tier, temperature 0.0), `infra/dataverse/outputschemas/invoice-classify.schema.json`, `infra/dataverse/inputschemas/invoice-classify.input.schema.json`, `scripts/ebilling/Ensure-ActionRow.ps1`, one row in `infra/dataverse/sprk_playbookconsumer-rows.json`, golden utterance GU-142.
- ArchTests ratchets shrank by one DEF-FIN entry: `AsymmetricRegistrationTests.KnownDefects` lost `AttachmentClassificationJobHandler <- IInvoiceAnalysisService`; `GateCombinationConstructibilityTests.KnownDefects` swapped it for `AttachmentClassificationJobHandler <- TextExtractorService` (same Applies predicate) because the oracle reports only the FIRST missing constructor parameter and `IInvoiceAi` (always registered, real or Null) now comes first. ADR-010 1:1 ceiling unchanged (162; no registration added or removed).

## Before / after (FR-46)

Method and caveats:

- 35 documents: 16 synthetic invoice PDFs (B-*), 6 real fee/invoice candidates (C-*), 8 outside-counsel-guideline / rate-card documents and 2 real rate cards (OCG-*, RC-*), plus 3 non-invoice documents and 2 edge cases (X-*). Document text was pypdf text, or Content Understanding markdown from spike 005 for scanned PDFs, capped at 60,000 characters. B-0079 had no text in the corpus and was replaced by B-0024.
- The `FinanceClassification` playbook does not exist in dev (ISS-002), so "before" was reproduced through the real `InvoiceAnalysisService` with the v1.0 prompt from `projects/x-financial-intelligence-module-r1/notes/prompts/classification-prompt.md` and the legacy schema; "after" went through `IInvoiceAi.ClassifyAsync` against the seeded Action and Binding in dev (Fast tier, the dev `gpt-4o-mini` deployment). Each path ran each document through the same harness (`temperature` 0.0 on both); the table is one run of each.
- Only ids, classes and confidences are recorded here, never document content.

| Document | Expected | Before (class, conf) | After (class, conf) | Class equal | Conf delta |
|---|---|---|---|---|---|
| B-0001 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0007 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0013 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0019 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.90 | yes | +0.00 |
| B-0026 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0031 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0043 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.90 | yes | -0.05 |
| B-0055 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0024 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.90 | yes | -0.05 |
| B-0067 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0091 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0103 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0115 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0127 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| B-0139 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.98 | yes | +0.03 |
| B-0150 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| C-01 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.90 | yes | +0.00 |
| C-02 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.95 | yes | +0.05 |
| C-03 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.90 | yes | +0.00 |
| C-04 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.90 | yes | +0.00 |
| C-05 | InvoiceCandidate | InvoiceCandidate 0.90 | InvoiceCandidate 0.90 | yes | +0.00 |
| C-06 | InvoiceCandidate | InvoiceCandidate 0.95 | InvoiceCandidate 0.95 | yes | +0.00 |
| OCG-berks | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| OCG-cook-forest-preserve | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| OCG-delaware | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| OCG-gatech | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| OCG-la-orm-attorney-billing | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| OCG-maricopa-opds | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| RC-cja-2026-ohsd | NotInvoice | NotInvoice 0.95 | NotInvoice 0.98 | yes | +0.03 |
| RC-youngconaway-lugano-exhibit-b | NotInvoice | NotInvoice 0.90 | NotInvoice 0.95 | yes | +0.05 |
| X-cover-letter | NotInvoice | NotInvoice 0.95 | NotInvoice 0.95 | yes | +0.00 |
| X-status-report | NotInvoice | NotInvoice 0.90 | NotInvoice 0.90 | yes | +0.00 |
| X-engagement-letter | NotInvoice | NotInvoice 0.90 | NotInvoice 0.92 | yes | +0.02 |
| X-garbled-scan | Unknown | Unknown 0.55 | Unknown 0.60 | yes | +0.05 |
| X-remittance | Unknown | NotInvoice 0.90 | NotInvoice 0.98 | yes | +0.08 |

Result:

- Class changed on 0 of 35 documents. Documents where "before" matched the expectation and "after" does not: 0. Errors: 0. No true invoice dropped out of InvoiceCandidate, so the escalation trigger did not fire.
- Both paths agree with the expectation on 34 of 35. The one divergence from my expectation, X-remittance, is NotInvoice in BOTH paths (I had expected Unknown), so it is not a regression.
- Hints on the 16 synthetic invoices (ground truth known): invoice number 16/16 before and after; total 15/16 before and after. NotInvoice documents carrying number or total hints: 0 before, 0 after.
- The escalation wording "confidence crossing `ClassificationConfidenceThreshold`" is moot: that option is declared but never read (DEF-018, #1988).

## Dev changes (all idempotent; nothing destructive)

- `sprk_analysisaction` row `invoice-classify`: id `aefb10ba-e0c8-f111-a05c-0022482913fc`, `sprk_modeltier` 100000000 (Fast), `sprk_temperature` 0.0, system prompt (JPS, 6540 chars) and output schema (3588 chars) from the mirrors, input schema from `inputschemas/`. Created by `scripts/ebilling/Ensure-ActionRow.ps1` (re-run: UNCHANGED).
- `sprk_playbookconsumer` row `invoice-classify|default|*`: id `210fa163-e1c8-f111-a05c-0022482913fc`, enabled, priority 500, Action `invoice-classify`. Seeded by `Seed-PlaybookConsumers.ps1` from the full mirror (36 rows upserted, 0 failed).
- `Seed-PlaybookConsumers.ps1 -DiffOnly` afterwards shows only four pre-existing differences (`assistant-suggest|default|`, `chat-summarize|reanalyze|*`, `create-project|default|*`, `create-todo|default|`: live rows missing from the mirror) and none for `invoice-classify`. The POML's "no difference" cannot be met literally until those four rows are added to the mirror (DEF-016, #1986). I did not run `-Export`, which would also drop the per-row `$comment` fields.
- Dev `/healthz` is Healthy (200). `/healthz/catalog` was already 503 before this task (eight Binding rows without a `ConsumerTypes` constant on this branch, DEF-017, #1987) and is still 503 for that reason alone; `invoice-classify` has a row and a constant.

## Deviations and findings

1. The POML says adding an `All` entry without a Binding makes `/healthz` Unhealthy. The code does the opposite (`RoutingConsumerTypeHealthCheck`): an `All` entry with no row is Degraded (forward-declared), a row with no `All` entry is Unhealthy. I corrected the block comment in `ConsumerTypes.cs`. Row first or constant first are both safe for `invoice-classify`, which has both.
2. `Deploy-ActionMirrors.ps1` and `Deploy-AnalysisAction.ps1` only PATCH contract columns of an existing row; neither creates a row or sets `sprk_modeltier` / `sprk_temperature`. Added `scripts/ebilling/Ensure-ActionRow.ps1` (reusable by 047 / 075 / 131: `-ActionCode`, `-DataverseUrl`, `-DryRun`; creates the row if missing, patches tier / temperature / input schema when different, then runs `Deploy-ActionMirrors.ps1 -Filter`). CLAUDE.md section 11: (1) existing: `Deploy-ActionMirrors.ps1`, which cannot create rows; (2) extension: it deliberately PATCHes only contract columns and sharing one row-creating mode with all mirrors would risk creating wrong rows, so the new script wraps it; (3) cost of doing nothing: three more Actions (047 / 075 / 131) would each need a hand-made row.
3. The Action mirror is JPS-shaped (like `triage-email.action.json`), not the `systemPrompt` / `outputSchema` shape of `agreement-classify`; the contract test therefore reads the sidecar for the schema.
4. Added golden utterance GU-142 (`invoice-classify`, event channel) so `GoldenUtteranceEvalSuiteTests` covers the new `All` entry.
5. Handler test is a seam test over the real facade, `ActionResolver`, `ActionRunner`, `TextExtractorService` and `RecordContainerResolver` (strict rule, `EmailProcessing:DefaultContainerId`) with only data boundaries doubled; it lives at `tests/integration/seam/Finance/AttachmentClassificationJobHandlerTests.cs` (KEEP path, tests/CLAUDE.md B5), not a mocked unit test.
6. `ClassificationConfidenceThreshold` is unused (DEF-018, #1988).
7. A clean NuGet restore of master currently fails under warnings-as-errors with NU1903 (high) on `System.Security.Cryptography.Pkcs` 10.0.11; this branch restores clean (`dotnet list package --vulnerable --include-transitive`: no vulnerable packages). Master was published with `-p:NuGetAudit=false` for the size comparison.

## BFF hygiene (CLAUDE.md section 10, NFR-01)

- Placement: in the BFF, no new endpoint, service, DI registration, package or background work; the handler's AI dependency moved to the existing `IInvoiceAi` facade (ADR-013). The handler registration is unchanged (still gated on `DocumentIntelligence:RecordMatchingEnabled`); `IInvoiceAi` is registered unconditionally (real or Null), so the handler no longer depends on `DocumentIntelligence:Enabled` for it.
- Publish size, convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -r linux-x64 --self-contained false -p:NuGetAudit=false`, framework-dependent linux-x64, Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4 PDBs), each side built from a fresh copy at a short path (`C:\wt042m`, `C:\wt042b`, `C:\wt042h`), 192 files on every side.

| Tree | Zip (MB) |
|---|---|
| master (fresh, `git archive master`) | 36.44 |
| work/ebilling-r1 at 872dbb780 (before task 042) | 37.17 |
| branch with task 042 | 37.18 |

- Delta of task 042 alone: +0.01 MB. Delta of the whole branch against master: +0.74 MB (other ebilling tasks, mostly the Content Understanding client). Far below the +5 MB trigger and the 55 / 60 MB thresholds.
- No new HIGH CVE.
- Tests: see the task report; Arch tests 1052 passed (ADR-010 ceiling 162 unchanged).

## Follow-ups for the orchestrator

- Merge gate ISS-009 (every BFF environment defines `CONTENT_UNDERSTANDING_ENDPOINT`) is unaffected.
- No ADR or pattern change is needed.
- Tasks 047 / 075 / 131 can reuse `Ensure-ActionRow.ps1`; each still needs its own `ConsumerTypes.All` entry with its Binding row in the same change.

## Adversarial review (one pass, read-only, given the diff)

- F4 fixed: `Ensure-ActionRow.ps1` coerced a null `sprk_temperature` on an existing row to 0 and reported it unchanged (the model would then run at its default temperature). It now patches a null temperature. Dry-run on the dev row: UNCHANGED.
- K4 fixed (doc): `IInvoiceAi.GetPlaybookByNameAsync` summary no longer names `FinanceClassification` as a live playbook.
- K4 recorded: for NotInvoice the legacy prompt returned an all-null hints object, stored as `{}` in `sprk_invoicehintsjson`; the Action returns `hints: null` for NotInvoice, so the column is now written as null. Nothing in `src/` reads the column apart from the handler.
- K3 recorded: the document reaches the model as a JSON string inside `## Input` rather than a raw `<document>` block (the same mechanism `DraftRulesAsync` uses); the before / after table above measures the effect (0 class changes). `binding_missing` retries are bounded by `MaxAttempts`, but Service Bus redelivery is immediate while `ConsumerRoutingService` caches a routing result for 5 minutes, so a transient null can outlast the retries (platform issue #1936).
- K2 recorded: the output-schema sidecar carries `$schema` / `$id` / `title` / `$comment-required` like `agreement-classify.schema.json`, and the contract test's validator is the tolerant function-parameters one. Strict-mode acceptance was proven live: all 35 documents ran through the dev Action row built from this sidecar with `jsonSchemaIsStrict: true`, 0 errors.
- Rework: no F1-F3 findings; one F4 fixed.
