# Task 047 - INVOICE-LINE-CODE@v1 Action and UTBMS line coding

Status: code complete and committed on `worktree-agent-a1d39cc5763f49814`. **Live dev seeding is held** for an owner decision (section 4). Spec: FR-22, NFR-05, D-05, D-46, D-51, D-52, D-64.

## 1. What shipped

| Area | Files |
|---|---|
| Action (JPS mirror, Fast tier, temperature 0.0, `$version` 1) | `infra/dataverse/actions/invoice-line-code.action.json` |
| Output and input schema mirrors | `infra/dataverse/outputschemas/invoice-line-code.schema.json`, `infra/dataverse/inputschemas/invoice-line-code.input.schema.json` |
| Binding row (consumerType = actionCode = `invoice-line-code`, consumerCode `default`, environment `*`) | `infra/dataverse/sprk_playbookconsumer-rows.json` |
| Closed catalog | `ConsumerTypes.InvoiceLineCode` added to `ConsumerTypes.All` (same change as the Binding row) |
| Eval-suite family for the new consumer type | `tests/integration/contract/Eval/golden-utterances.json` (GU-143; `FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily` requires it) |
| Service | `Services/Finance/Coding/`: `ILineCodingService`, `LineCodingService`, `CodeSetResolver`, `LineCodingGuard` (internal, pure), `CodingModels`, `ICodeSetSuggestionProvider` (+ `NoCodeSetSuggestionProvider`) |
| DI | `FinanceModule.cs`: three scoped registrations, unconditional (no flag block) |
| Tests | contract `InvoiceLineCodeActionContractTests`; seam `CodeSetResolverSeamTests`, `LineCodingSeamTests` (+ `FakeCodingReferenceData`); domain `LineCodingGuardTests` |
| ADR-010 ceiling | `tests/Spaarke.ArchTests/ADR010_DITests.cs`: 162 -> 164 (+2: `ILineCodingService`, `ICodeSetSuggestionProvider`), dated seam justification in the file |

Nothing in C# names a model, tier, temperature, prompt or schema (grep over `Services/Finance/Coding/` finds no deployment name, prompt text or schema literal; the only string constants are the code-set names "UTBMS Litigation / Activity / Expense" that task 018 seeds). `LineCodingService.ActionCode` / `ActionVersion` (1) are the pair the D-64 correction record stores.

### Placement and justification (CLAUDE.md section 10 and 11)

- **Placement (bff-extensions A.1):** in the BFF, `Services/Finance/Coding/`. It is synchronous reference-data reads plus one facade call per chunk, invoked from a job (049) that already runs in the BFF; no background work of its own, so ADR-052 has nothing to place. **Config home (A.6):** prompt, tier and temperature are Action-row columns (Home A); routing is the Binding row; nothing is on a Playbook header, Node row or scope.
- **ADRs cited:** ADR-010 (feature module; 2 seams), ADR-013 (only `IInvoiceAi` from `PublicContracts`), ADR-015 (no content in logs), ADR-032 (Null-Object default for the suggestion seam), ADR-038 (tests on KEEP paths), ADR-039/043 (closed catalog, no dispatch change), ADR-002 (read-only; the guard fails closed), ADR-044 (GUIDs stay typed `Guid`).

| New surface | Existing overlap | Extension? | Cost of doing nothing |
|---|---|---|---|
| `invoice-line-code` Action + Binding + `All` entry | `invoice-classify` pattern only; no coding Action exists | The catalog is the extension point; this is a new consumer type by definition | FR-22/NFR-05: lines have no UTBMS codes; code-based rules (072) cannot evaluate |
| `CodeSetResolver` | grep `utbms\|billingcode` in `src/server` found nothing before this task | Nothing to extend; `IGenericEntityService` is reused for reads | No way to pick the code set; codes could not be checked against a set |
| `LineCodingGuard` (internal static) | none | `$choices` enrichment cannot reach `lines[].taskCode` and cannot filter per set | A hallucinated code would be stored (FR-22 MUST) |
| `ILineCodingService` | none | n/a | 049 and 048 would need the code-set reads, chunked AI calls and the guard behind a concrete in every test |
| `ICodeSetSuggestionProvider` | none | n/a | 048 would have to edit `CodeSetResolver` to add FR-22 step 4 |

## 2. Behaviour

**Resolution order (FR-22), per invoice, recorded on every line as `CodeSetSource`:** (1) firm codes, (2) matter type default set, (3) practice area default set, (4) `ICodeSetSuggestionProvider` (flagged `NeedsReview`; the default provider proposes nothing until 048), (5) settings default (`sprk_defaultcodeset`, else the set named "UTBMS Litigation"). A step is usable only if the set is active and has at least one active Task code. A stale mapping is skipped with a warning (never coded against nothing). No usable set: `LineCodingException` reason `ebilling.coding.no_code_set`.

**Guard (`LineCodingGuard`):** every code the model returns is checked against the line's resolved set: matching ignores case/whitespace and the stored value is the set's own spelling. A code outside the set, or of a kind the line type does not take, is dropped and reported as a `RefusedCode` (the "code refused" finding input); it is never stored and never "corrected". Firm codes are kept beside the AI code with a `Mismatch` flag. Observations are kept only when their quoted text is in the narrative, one per type.

**Chunks:** sequential, in input order, size = `CodingChunkSize` clamped to 1-200. Each chunk is sent only the code lists its lines need (a token saving; the guard still checks the full set). Lines with no narrative are not sent (`NoNarrative`). A line with zero or two entries from the model is `NotReturned`, never guessed. Only entries keyed to a line of that chunk count (fix-round-1). Any failure fails the whole call; `InvoiceAiException` / `FeatureDisabledException` propagate unchanged.

### Deviations and interpretations (one line each)

1. **FR-22 step 1:** a set is selected when exactly one active set contains *every* distinct firm Task code on the invoice. Zero or several sets: not decisive, fall through. Only Task codes are used because the seeded Activity/Expense codes live in the shared "UTBMS Activity" / "UTBMS Expense" sets (018), so "all codes in one set" cannot hold across the kinds. The activity/expense allowed lists are the resolved set's own, else the shared sets'.
2. **No static `$choices` enum** (the POML's "may add"): `$choices` enrichment only reaches top-level string properties; `lines[].taskCode` is nested. The server-side guard is the control.
3. **Test placement:** unit tests live under `tests/unit/domain/Finance/Coding/` (ADR-038 KEEP path), not `tests/unit/Sprk.Bff.Api.Tests/Services/...`; they compile into the same project.
4. **Confidence is reported as 0 when any returned code was refused** (it no longer describes what is stored); pruning of per-chunk code lists and clamping of the chunk size are additions beyond the brief, justified by token cost and by the 1-200 column range.
5. **Resolution is per invoice (one core record), not per line**; the source is recorded per line.
6. **Binding `sprk_modeltieroverride` is not set**: it is not applied on the linear path (ISS-005) and no tier maps to gpt-5.4-mini.
7. **TASK-INDEX.md was not edited** (the launch brief forbids it; POML step 8 is the main session's).

## 3. Escalations (all need an owner decision)

**E1 - no dev tier resolves to gpt-5.4-mini (POML trigger 2).** Dev: Fast = Standard = deployment `gpt-4o-mini` (model gpt-4.1-mini); Reasoning = `gpt-5-reasoning` (gpt-5). The `gpt-5.4-mini` deployment exists on `spaarke-openai-dev` but no tier points at it. Probe on 2026-10-16: gpt-5.4-mini accepts `temperature` 0.0 and `reasoning_effort=low`, but **rejects `max_tokens`** (HTTP 400), and `OpenAiClient` sends `max_tokens` for every deployment that is not the configured `ReasoningModel`. So pointing FastModel at it breaks every Fast consumer unless `OpenAiClient` changes. Options: (a) change FastModel for all Fast consumers and teach `OpenAiClient` to omit `max_tokens` for it; (b) add a tier that maps to it; (c) set `ReasoningModel` to gpt-5.4-mini (changes every Reasoning Action); (d) accept gpt-4.1-mini for R1. The Action row carries tier Fast only, so any option is a platform/config change, not a change to this task's code.

**E2 - low reasoning effort (POML trigger 1).** `ActionRunner` never sets reasoning effort and `BoundInputs`/`LinearRunContext` carry no setting; D-51's "low effort" cannot be met without a platform dispatch change (ADR-043). Not attempted.

**E3 - accuracy below NFR-05 (POML trigger 3).** See section 5. 55.0% / 63.3% task top-1 against NFR-05's 80% (spike: 64-66%). Prompt not tuned, per the trigger.

## 4. Seeding (held) and the order that matters

Not run live: E1 changes what "Fast" means, and the Action row's tier is part of what gets seeded. When released, the order is: Action row, then Binding row **and** the BFF build carrying the `All` entry together. `RoutingConsumerTypeHealthCheck` reports drift in both directions (an `All` entry without a Binding row, or a row without an `All` entry), so seeding the row without the BFF, or the BFF without the row, turns /healthz Degraded or Unhealthy. The acceptance criteria "dev rows exist", "`-DiffOnly` shows no difference" and "/healthz Healthy" are therefore **not yet met**.

Read-only dry runs (2026-10-16, dev `spaarkedev1`):
- `scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode invoice-line-code -DryRun`: would CREATE the row (columns `sprk_actioncode, sprk_name, sprk_description, sprk_kind, sprk_allowsknowledge, sprk_modeltier, sprk_temperature, sprk_inputschema`).
- `scripts/Deploy-ActionMirrors.ps1 -Filter invoice-line-code -DryRun`: MISSING ROW (it will not create one; run Ensure-ActionRow first).
- `scripts/dataverse/Seed-PlaybookConsumers.ps1 -DryRun`: 37 mirror rows, no Dataverse calls.

Live commands for the owner, in order:
1. `scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode invoice-line-code`
2. `scripts/Deploy-ActionMirrors.ps1 -Filter invoice-line-code`
3. Deploy the BFF build containing this change **together with** `scripts/dataverse/Seed-PlaybookConsumers.ps1` (then `-DiffOnly`, expect no difference; check /healthz).

## 5. Accuracy sample (step 6)

Method: a throwaway harness (not committed) ran the real `LineCodingService -> InvoiceAi -> ActionRunner -> OpenAiClient` path against `spaarke-openai-dev` with the authored Action, the seeded UTBMS Litigation / Activity / Expense CSVs, and the synthetic evaluation set B: 10 items, 120 lines, 10 calls, scored against ground truth. The prompt was not tuned between runs.

| Model | Task top-1 | Activity top-1 | Refused codes | Not returned | Mean confidence correct / wrong |
|---|---|---|---|---|---|
| gpt-4.1-mini (current Fast, deployment `gpt-4o-mini`) | **55.0%** | 70.8% | 0 | 0 | 0.867 / 0.749 |
| gpt-5.4-mini (harness registered it as the reasoning model so `max_tokens`/temperature were omitted) | **63.3%** | 71.7% | 0 | 0 | 0.919 / 0.752 |

Both are below NFR-05's 80% and the extraction spike's 64-66%; the final measurement is task 162's. The guard refused nothing in either run (the models stayed inside the lists), so FR-22 held without the guard having to act. Small sample (120 lines, synthetic): read as direction, not as a pass/fail.

## 6. Verification

- Build: `dotnet build` of `Sprk.Bff.Api` and the test project exit 0.
- Task 047 test classes (contract + seam + domain): 102 pass / 0 fail on the final code. Fix-round-1's new test failed against the old service (1 fail, 23 pass in `LineCodingSeamTests`); fix-round-2's strengthened paging test fails when `RetrieveAllAsync` stops after the first page (1 fail, 24 pass in `CodeSetResolverSeamTests`).
- `tests/unit/Sprk.Bff.Api.Tests` (whole project, final code, `--no-build`): 23656 passed, 0 failed, 23 skipped, 23679 total, 31 min.
- `tests/Spaarke.ArchTests`: 1052 passed, 0 failed (includes the ADR-010 ceiling at 164 and the gate-combination constructibility tests, so the three new registrations resolve under every flag combination).
- Mutation checks run during development (101 tests then): the guard accepting any code -> 8 fail; the resolver ignoring the FR-22 order and the firm-code step -> 13 fail; chunk size ignored and per-chunk code-list pruning removed -> 8 fail. Each mutation was restored afterwards.
- jps-validate on the Action mirror: valid JSON, `$schema` correct, role/task present, 9 constraints (array), 1 output field with `structuredOutput`, 1 example, metadata complete, no template params, no `$choices`/`enum`, `IsJpsFormat` true, no legacy drift keys. The seam test renders the authored Action through the real `PromptSchemaRenderer`.
- Publish size (`dotnet publish` linux-x64 zip, 192 files each): fresh master 37.15 MB, `work/ebilling-r1` base 37.19 MB, with this task 37.21 MB. **Task delta +0.02 MB** (branch vs master +0.06 MB). No package added, so no new CVE surface.

## 7. Follow-ups for other tasks

- **048:** register the AI `ICodeSetSuggestionProvider` in place of `NoCodeSetSuggestionProvider` (one DI line); the resolver already validates the returned id (active set with Task codes) and flags `AiSuggestion` for review. Persisting a confirmed proposal as a mapping is 048's.
- **049:** map `LineCodingException.NoCodeSet` to Failed; let `InvoiceAiException` / `FeatureDisabledException` surface as the job's AI failure; turn each `RefusedCode` into a "code refused" finding input, `HasFirmCodeMismatch` into the mismatch finding, `CodeSetNeedsReview` into a review flag. Treat a `Coded` line with no AI code as needing review whatever its confidence (the guard does not zero confidence in that case). Chunks are sequential (about one model call per 25 lines): run the call from the job, never on a request path.
- **101:** the D-64 correction record stores `LineCodingService.ActionCode` and `ActionVersion`.
- **162:** final NFR-05 measurement; E1/E3 decide whether the number moves.

## 8. Known limits (K-class)

- L1 (K3): step 1 treats two sets that both contain every firm code as ambiguous even if one of them is inactive; the invoice then falls to the mappings (still a valid, recorded set).
- L2 (K4): a request with zero lines, or only lines without narratives, still resolves the code set (needed for the result) and so can throw `no_code_set`.
- L3 (K4): sequential chunks; parallelism is a later optimisation.
- L4 (K3): `RetrieveAsync` on a missing core record propagates the Dataverse fault unchanged.
- L5 (K2, 041 contract): a model observation with a blank `quotedText` fails the whole chunk (`InvoiceAiException` `ai.invoice.output_invalid`), and therefore the whole invoice's coding call, instead of dropping that one observation. Scenario: a small Fast-tier model emits `{"type":"VagueDescription","confidence":0,"quotedText":""}` as a "nothing here" placeholder on one line of a 150-line invoice; 049 gets an exception and no partial result. Not seen in 240 sampled lines on two models. Fix belongs to the 041 facade (drop blank-quote observations in `ParseCodeLines`) and is the 041 owner's call; 049's retry contract must treat `output_invalid` as retryable-once, then Failed.
- L6 (K3): a missing shared Activity/Expense code set degrades every line (no code of that kind) and is only visible as an error log.

## 9. Review results (Step 9.5)

Rigor STANDARD; code-review and adr-check run because `tests/**` is touched.

- **adr-check:** Compliant: ADR-001, 002, 004/036/052 (no background work), 007, 008 (no endpoints), 009 (no cache, reasoned), 013 (facade only), 015, 028, 029, 039, 043, 044, bff-extensions A-F. Warning: ADR-010, two single-implementation interfaces, ceiling raised by exactly 2 with falsifiable justification. Violations: none.
- **code-review:** one defect found and fixed (fix-round-1, commit `889fcd7f3`): an entry the model returned for a line outside its chunk was applied to that line, or made the line's own answer ambiguous. Also corrected the `DroppedObservations` doc. Metrics: all files cohesive (single responsibility each; `CodeSetResolver` 373 lines is one resolution algorithm plus its reads).
- **Adversarial pass (one, `adversarial-reviewer`, read-only, over `70bfb7586..889fcd7f3`):** no F-class defect on a real path. It confirmed as sound: guard bypass (case, whitespace, lookalike, wrong kind, blank), FR-22 order and skip rules, chunk/key handling, no swallowed failures, no content in logs or exceptions, and the contract chain (output schema, DTO, parser, input schema, Binding row, `All`, golden utterance, ADR-010 +2). It ran 568 related tests green. Its findings and what was done (fix-round-2, commit `66eaad322`):
  1. K2, not changed: `InvoiceAi.ParseCodeLines` (041) rejects a blank `quotedText` and fails the whole chunk as `output_invalid`, so the guard's empty-quote branch is not reachable from production. Kept as defence in depth (comment added to the test). See known limit L5.
  2. Test strength, fixed: `ASetLargerThanOnePage_IsLoadedInFull` still passed if paging was removed entirely because the fake returned every row when no page size was set. The fake now returns Dataverse's 5000-row default and the test uses 5203 codes; verified to fail when `RetrieveAllAsync` stops after the first page.
  3. K3, changed: a missing shared "UTBMS Activity"/"UTBMS Expense" set is now logged as an error (it silently leaves every Fee/Expense line without that code kind), not a warning.
  4. K4, already recorded: step 1 uses Task codes only and is resolved per invoice (deviation 1, L1).
  5. Comment drift, fixed: `FinanceModule.cs` and `CodeSetResolver` said the new services were scoped because they hold the scoped `IGenericEntityService`; it is a singleton. Lifetimes were already correct (scoped `IInvoiceAi` and suggestion provider; no captive dependency).
  6. This notes file was uncommitted at review time; committed with fix-round-2.
