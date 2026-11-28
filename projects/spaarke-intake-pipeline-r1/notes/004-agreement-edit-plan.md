# Task 004 notes: app-only agreement edit plan (`IAgreementAi`)

Status: code, catalog artifacts and tests done; live model runs done 2026-11-26 on gpt-5-reasoning (see "Live run 2026-11-26"; the deploy steps under "Needs a live dev run" are still open). POML status stays `in-progress`.

## What was built

| Piece | Where |
|---|---|
| Facade | `Services/Ai/PublicContracts/IAgreementAi.cs`, `AgreementAi.cs` (real), `NullAgreementAi.cs` (ADR-032 P3 peer), `AgreementAiContracts.cs` (DTOs, `AgreementAiException`, `AgreementRefusalCodes`) |
| Segmentation + anchor pre-check | `AgreementSegmentation.cs` |
| Plan assembly + schema validation | `AgreementEditPlanAssembler.cs` |
| Pinned standard | `PinnedNdaStandard.cs`, `Standards/spaarke-nda-mutual-v1.2.md`, `Standards/edit-plan.schema.json` (embedded resources, `Sprk.Bff.Api.csproj`) |
| Catalog | `infra/dataverse/actions/agreement-edit-plan.action.json`, `outputschemas/agreement-edit-plan.schema.json`, `inputschemas/agreement-edit-plan.input.schema.json`, Binding row in `sprk_playbookconsumer-rows.json`, `ConsumerTypes.AgreementEditPlan` (+ `All`) |
| DI | `AnalysisServicesModule.cs`: `NullAgreementAi` in `AddNullObjectsForCompoundOff`, `AgreementAi` in `AddPublicContractsFacade` (next to `InvoiceAi`) |
| Tests | `tests/integration/seam/Intake/AgreementAiEditPlanSeamTests.cs`, `tests/integration/contract/Catalog/AgreementEditPlanActionContractTests.cs` (also holds `AgreementEditPlanPinnedStandardContractTests`), `tests/unit/domain/Ai/PublicContracts/AgreementAiContractTests.cs`, fixtures `tests/fixtures/intake/agreement-edit-plan/` |

## Decisions

**Anchoring = closed para_key list + exact-match pre-check; no fuzzy matching anywhere.** (Coordinator correction, owner concern about probabilistic clause matching; ADR-049.)
- The Action's input is the accelerator's `segment_clauses.py` JSON (`sections` with `index`, `number`, `heading`, `para_key`, `text`, plus `has_tracked_changes`, `has_comments`, `has_hidden_text`), never free extracted text. `AgreementEditPlanRequest.SegmentationJson` carries it; the intake engine job's `segment` operation (task 001) produces it. A malformed segmentation is an `ArgumentException` before any Action runs. `hidden_text` is never forwarded to the model (only the flag).
- The prompt says `para` must be one of the supplied `para_key` values, character for character; a section whose `para_key` is null cannot be edited and is reported in the output's `unplaceable` list instead (topic, `section_index`, comment).
- `AgreementSegmentation.BeginPlan()` returns an `AnchorChecker` that checks every edit, **in plan order, against the clause as the kept edits before it leave it** (superseded the stateless `CheckAnchors`, live run 2026-11-26, D2; see "Live run 2026-11-26"). Raw ordinal comparison, no whitespace collapse, on the engine's own texts (`anchor_text.visible` / `anchor_text.editable` from `segment_clauses.py --anchor-text`): `para` equals a `para_key`; `find`/`after` occurs exactly once in the visible text and once in the editable text and does not cut a word (`word_edge_ok`); `old` occurs exactly once in `find`; `find "*"` is the editable text after the number and heading. No trimming, case folding, quote normalisation or search of other sections.
- A failing edit is left out of the plan and returned in `AgreementEditPlan.RefusedEdits` as `(Index, Code, TopicId)`, with the engine's codes `para_not_found`, `not_found`, `not_unique`, `in_existing_markup`, `word_boundary`, `old_not_in_find`, `crosses_markup`, `nothing_after_heading`, plus the BFF-only `edit_invalid` for a malformed edit (`AgreementRefusalCodes`). The facade never retries; the harness (task 015, option B) decides whether to ask for one rewrite. Summary numbering and bookmarks are computed over the kept edits only.
- The engine stays the final authority (fields, drawings, content controls, hyperlinks, and the K items below).
- The accelerator source does not itself define the code names; they are the closed set the coordinator specified. If the accelerator publishes a different set, rename in `AgreementRefusalCodes` only.
- Still thrown as typed `AgreementAiException` (never a plan): only faults of the output as a whole: not an object or no `edits`/`unplaceable` array (`output_invalid`, with JSON path), a bad `unplaceable` finding, an assembled plan the accelerator schema rejects (`plan_invalid`, instance location only). A malformed single edit (missing member, topic outside the pinned standard, a shape `validate_plan` rejects, text that is not XML 1.0) is refused on its own as `edit_invalid` (live run 2026-11-26, D1 and D4).

**Structured Outputs.** The model output is the strict subset: `{edits:[{topic (enum of the 24 pinned ids), para, find|null, old|null, replace|null, after|null, insert|null, comment|null, summaryRow: {section, issue, revision, priority High|Medium|Low} | null}], unplaceable:[{topic, section_index, comment}]}` (the four flat summary members became one nullable object on 2026-11-26, D1): every member required, optionals nullable, `additionalProperties:false`, no `oneOf`/`format`/`pattern`/length bounds. The accelerator's `edit-plan.schema.json` cannot be sent as is (`oneOf` on `find`/`after`, `format`, `pattern`, `minLength`), so the facade validates the assembled plan against it in code. `edits[]` itself is expressible, so the STOP trigger did not fire. `agreement-review`'s closed output contract is untouched (the second STOP trigger did not fire): the plan comes from the separate `agreement-edit-plan` Action.

**What the model does not write.** `author`, `initials`, `date`, `provenance`, `agreement.file_name`, `review_basis` (pinned standard name and version), `bookmarks`, summary `n` and `bookmark`. A model-chosen bookmark or `n` could collide and the engines refuse the plan.

**The standard comes from a pinned embedded copy, not retrieval.** `src/accelerators/**` is not in the published BFF, so `Standards/spaarke-nda-mutual-v1.2.md` and `Standards/edit-plan.schema.json` are byte copies, pinned by `AgreementEditPlanPinnedStandardContractTests` (equal to the accelerator files, 24 topic ids equal to `topics.yaml`). The version stamped on the plan is the version the model was shown. `AllowsKnowledge` is false on the Action row (`Ensure-ActionRow.ps1` default), so a stale KNW-011 cannot change a run. The standard's positions are test positions awaiting counsel's ratification (accelerator `status: mvp-test`); `review_basis.source` is `"customer"` (judgment call).

**Tier.** Reasoning, temperature 0.1, not graded live (one-column change on the row if the first dev run says otherwise).

**Binding.** Row appended after `invoice-firm-notice` (consumerType and actionCode `agreement-edit-plan`, `consumerCode: default`, no tool projection). The `agreement-review` (consumer `nda-review`), `agreement-classify` and task 012's `intake-harness` rows and action files are byte-unchanged (verified with `git diff`). The `intake-harness` row still targets `agreement-classify`; task 015 may repoint it.

**Classification.** `IAgreementAi.ClassifyAsync` runs the existing `agreement-classify` Action. It reads the live `sprk_agreementtype` registry and injects it with `AgreementTypeRegistryPromptAssembler.Materialize` (see the defect below), refuses a `subDomainKey` outside the registry (matched ignoring case, returned in the registry's spelling), refuses contradictory verdicts and empty registries, and fails closed if the deployed prompt has no placeholder. `AgreementClassifyResult.IsSubDomain("nda", threshold)` is how the harness decides: the synthetic MSA is not an NDA, and then the harness runs no edit-plan call (test `Classify_ForTheSyntheticMsa_IsNotAnNda_AndTheCallerBuildsNoPlan`).

## Placement Justification (root CLAUDE.md section 10 and `bff-extensions.md`)

- Placement: in the BFF, under `Services/Ai/PublicContracts/`, because the consumer is the BFF's own `IntakeEngineCompletionProcessor` / harness (task 014/015) running app-only in a background job, and ADR-013 requires CRUD/job code to reach AI only through the PublicContracts facade. Not a new endpoint, no new background work, no new package (JsonSchema.Net is already referenced).
- Component justification (section 11): **Existing**: `IInvoiceAi` is the same pattern but for finance Actions; `AgreementTypeRegistryPromptAssembler` and `IAgreementTypeRegistryReader` are reused for classify; the Console's `agreement-review` Action is chat-session-bound and its closed output contract (`overallRisk`, `flaggedSections`) cannot carry an edit plan. **Extension**: extending `IInvoiceAi` would put agreement AI in the finance facade; extending `agreement-review` is excluded by the escalation trigger. **Cost of doing nothing**: the intake NDA harness has no app-only way to classify a document or obtain a schema-valid edit plan, so FR-02 and the whole harness (task 015) cannot run.
- Interface (ADR-010): `IAgreementAi` has two implementations (real and Null peer), so it is a real seam.
- Hot-path: no change to Compose, Console or any hot path.
- Publish-size delta and CVE result: see the end of this file.

## Needs a live dev run (owner's OK, nothing was run live)

Not done and not claimed: that the model actually writes such edits. Tests prove everything the application owns using recorded model output (fixtures authored for this task, never read from an answer key).
1. Deploy order (project CLAUDE.md section 5, owner OK): BFF carrying `ConsumerTypes.AgreementEditPlan` first, then `scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode agreement-edit-plan` (creates the row with `sprk_allowsknowledge=false`), then `Seed-PlaybookConsumers.ps1` for the Binding row. A constant with no row is Degraded on `/healthz/catalog`; a row with no constant is Unhealthy.
2. A dev smoke run on the three synthetic NDAs plus the MSA (or leave it to task 008's evals): segmentation from `segment_clauses.py` -> `BuildEditPlanAsync` -> check `RefusedEdits` stays empty or small, plan validates, engine applies it in `--dry-run`.
3. Check the first run's tier and the 4000-token output cap against a real 24-topic review.

## Tests

See the end of this file for counts. Fixtures under `tests/fixtures/intake/agreement-edit-plan/`: `*.segmentation.json` are the raw output of `py -3.13 src/accelerators/core/engines/segment_clauses.py <corpus docx>` for nda-01, nda-02, nda-03 and msa-01 (Python 3.13 here because 3.11 has no lxml); `*.model-output.json` are recorded-style model replies authored for this task. The tests read only the synthetic corpus listed in the task; nothing under `answer-key/` was read or copied. I read the head of `evals/expected/nda-01-golden-plan.json` once while orienting (before the brief's rule was applied to it); nothing from it was copied or used.

## Known limits (K-class)

- K1: `AgreementAi`'s resolve-and-run helper repeats `InvoiceAi`'s private one (the eBilling facade is frozen until UAT). Extract when a third Action-backed facade needs it.
- K2: RESOLVED 2026-11-26 (the pre-check now matches raw text, like the engine).
- K3: RESOLVED 2026-11-26 (`find "*"` is the editable text after `LEAD_RE`, as in the engine).
- K4: the plan has no `ratings`, `unchanged` or `agreement.title/parties`; only `file_name`.
- K5: counsel ratification still gates rollout of the standard; KNW-011 rendering is a separate task.
- K6: `unplaceable` findings carry model text for display; they are returned, never logged.

## Defects found

- **#3238**: `agreement-classify`'s `{{agreementTypeRegistry}}` placeholder is substituted only by the new `IAgreementAi.ClassifyAsync`. No other production code calls `AgreementTypeRegistryPromptAssembler.Materialize`, and no C# outside `PublicContracts` dispatches the `agreement-classify` consumer type, so a Binding-driven run of that Action sends the literal token and the classifier has no registry. Filed, not fixed here (outside this task; the intake path is not affected).

## Verification

Test counts (the new tests plus the neighbouring catalog, routing, DI and finance suites; `dotnet test` on the three KEEP-path projects, filtered to the new classes and the Seam, Contract.Catalog and PublicContracts folders): **358 passed, 1 skipped (a pre-existing live-only test), 0 failed**. `Spaarke.ArchTests`: **2842 passed, 0 failed**. BFF build: 0 warnings, 0 errors.

Red before green: with the pre-check body replaced by `return null` (every anchor accepted), **14 tests failed**; with the real pre-check restored they pass.

## Publish size (CLAUDE.md section 10, NFR-01)

Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64), zipped with `Compress-Archive -CompressionLevel Optimal`, PDBs included, from short-path throwaway worktrees, all with **194 files**. The branch carries other tasks' BFF changes, so the "before" side is the branch commit just before this task's commit, which isolates this task's delta exactly; fresh `origin/master` (`24d53fb333`) is shown for scale.

| Tree | Zip | Branch with this task, minus this tree |
|---|---|---|
| `origin/master` @ `24d53fb333` | 43,659,680 B (41.64 MB) | +14,807 B (+0.01 MB) |
| branch before this task (`7f9b9c21da`) | 43,646,729 B (41.62 MB) | +27,758 B (+0.03 MB) |
| branch with this task (`52f693bea8`) | 43,674,487 B (41.65 MB) | |

Well under +5 MB, the 55 MB review trigger and the 60 MB hard stop. Worktrees removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## CVE

`dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive`: **no vulnerable packages**. No package was added or upgraded.

## Review (task-execute Step 9.5)

Checked against the code-review and adr-check criteria: ADR-013 (only `PublicContracts` types are exposed; no AI-internal type leaks), ADR-015 (every log line carries ids, codes, counts and JSON paths only; the inner exception is not logged; schema errors are reduced to instance pointers), ADR-032 P3 (Null peer registered in compound-off), ADR-010 (two implementations), ADR-039 (constant, Action row, Binding row and `All` move together; health-check test green), ADR-049 (anchors are validated, never searched for). Class-grep for text search in the plan path (`IndexOf`, `Contains`, `StartsWith`, `Regex`, `Levenshtein`, `Fuzzy`, `Similar`): the only text operations are ordinal, whitespace-collapsed occurrence counts in `AgreementSegmentation.CheckAnchors`. No F-class findings open; K1 to K6 above are the known limits. `agreement-review` and `agreement-classify` action files and their Binding rows are byte-unchanged (`git diff` 0 lines; the Binding file has 0 deletions).

## Live run 2026-11-26

Owner-approved model calls against dev Azure OpenAI (`spaarke-openai-dev`), `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, synthetic corpus only, nothing written to Dataverse or Azure. Plans, raw replies and engine reports stayed in the session scratchpad.

### First runs (gpt-4o-mini): three defects

The first two runs used gpt-4o-mini because the live test left the Reasoning deployment unset and the resolver fell back to Standard (the dev BFF maps Reasoning to `gpt-5-reasoning`, `DocumentIntelligence__ReasoningModel`, read with read-only `az`).
- **D1, a partial summary row lost the whole plan.** The schema had four flat nullable summary members; the model filled `section` only on one insertion, and the assembler threw `output_invalid` for the plan.
- **D2, the pre-check accepted an edit the engine then refused `not_found`.** nda-01 edit 4 and nda-02 edit 4.
- **D3, nda-03 (the injection NDA) got 0 edits and 1 refusal in both runs.**

### D1 fix

- The strict output schema replaces the four members with ONE nullable object, `summaryRow {section, issue, revision, priority}`, so a half row cannot be written (`type: ["object","null"]`, accepted by Azure strict mode in both gpt-5-reasoning runs). The assembler maps it to the accelerator's `summary` (which is unchanged, as is its pinned copy). `topic` became an enum of the 24 pinned ids.
- **One edit's fault never costs the plan:** any per-edit fault (missing member, wrong kind, topic outside the standard, a shape `validate_plan` rejects, half or blank summary row, text that is not XML 1.0) refuses that edit as `edit_invalid` and keeps the rest. Only output-level faults stay plan-level.
- Prompt: the summary constraint is written around `summaryRow`; an overlap rule was added ("must not overlap words an earlier edit changed ... never use find '*' on a section you already edit elsewhere"). `$version` stays 1 (never deployed).
- Tests: the malformed-edit theory (21 cases, including the live flat shape and a half row) asserts the one refusal and the surviving edit; contract tests pin the schema shape. Red: with per-edit faults thrown for the plan, 16 then-existing cases failed; with the old schema and action restored, 3 contract tests failed.

### D2 root cause and fix

Evidence (scratchpad `diag_plan.py` on the gpt-4o-mini plans, counts and codes only): every edit applied **alone** was `ok`, and the segmentation text equalled the engine's visible and editable text for every refused paragraph. Applied **in order**, edit 4 was `not_found` in both documents:
- nda-01: edit 4's 307-character quote covered words that edits 1 to 3 had already replaced in the same paragraph, so it was no longer in the visible text.
- nda-02: edit 4 was `find "*"` on a paragraph edits 1 to 3 had already changed.

The old pre-check was stateless (each edit against the original clause). Secondary gaps: it collapsed whitespace while `_locate` does not (old K2), checked `"*"` against the section text instead of the editable text after `LEAD_RE` (old K3), and had no editable text at all, so `in_existing_markup` could not be predicted.

Fix:
- Engine (minimal, opt-in, default output unchanged): `segment_clauses.py --anchor-text` adds `anchor_text {visible, editable}` per section, computed with `Redliner.visible_text` and `_run_char` so it cannot drift. The engine job's `segment` operation passes the flag. The segmentation reader refuses an editable section without `anchor_text`.
- BFF: `AgreementSegmentation.BeginPlan()` / `AnchorChecker` checks edits in plan order against a per-clause state the kept edits update as the engine's tracked changes do (a replaced span leaves visible and editable text; inserted text is visible, not editable; a markup point a later span may not cross). Raw ordinal comparison; codes are the engine's (`not_found`, `not_unique`, `in_existing_markup`, `word_boundary`, `old_not_in_find`, `crosses_markup`, `nothing_after_heading`, `para_not_found`). No fuzzy match, no repair.
- Ten sequential cases were verified on the real engine first (scratchpad `engine_cases.py`): overlap after a replacement (`not_found`), `"*"` after an edit (`not_found`), insertion inside a later quote (`not_found`), quote across an earlier deletion (`crosses_markup`), after a comment-only edit (`ok`), disjoint (`ok`), tab vs space (`not_found`), inserted text repeating a later quote (`not_unique`). The seam tests assert the same codes. Red: with the stateless check, 5 sequential tests failed.
- Closed code set: the BFF set gained the engine's `in_existing_markup`, `crosses_markup`, `nothing_after_heading` (already engine codes) and the BFF-only `edit_invalid`. **`edit_invalid` needs the owner's and the coordinator's confirmation**, and the accelerator session needs to hear about `--anchor-text`.

### D4 root cause (coordinator check, same day): our prompt showed the text as `\uXXXX` escapes

The garbled characters line up with the low byte of the intended ones (U+2019 → U+0019, U+201C → U+001C, U+201D → U+001D), so the coordinator asked whether our own pipeline drops the high byte. Evidence:
- **The response is not changed by our code.** `OpenAiClient.GetStructuredCompletionRawAsync` returns the SDK's `message.content` unchanged, and the run-2 nda-02 raw reply held the escape *text* `\u0019` (seven times), which the SDK's JSON decode can only produce if the HTTP body carried `\\u0019`.
- **Raw HTTP bytes, one call** (nda-02 prompt as rendered before the fix, Standard deployment `gpt-4o-mini`, which Azure reports as `gpt-4.1-mini-2025-04-14`; scratchpad `raw_bytes.py`, counts only): 38 escapes written by the model (`\\u0000`, `\\u0002`, `\\u001A` in the body), 0 raw control bytes, 0 transport escapes, and none of U+2019/U+201C/U+201D/U+00A7 anywhere in the reply. Every one of the 14 edits carried a control character. The earlier gpt-4o-mini plans (first runs) had U+001A in every summary section too. **Both deployments are affected.**
- **Our prompt is the cause.** `PromptInputSection.Render` (the single `## Input` producer) and `PromptSchemaRenderer`'s `## Examples` serialized JSON with the default encoder, which escapes every non-ASCII character: the nda-02 prompt showed U+201C and U+201D 9 times each, U+2019 and U+00A7 twice, and U+2013 once, all as six-character backslash-u escapes, plus the quote, apostrophe and ampersand as backslash-u escapes. Asked to quote a clause character for character, the models copied the escapes into their own JSON and garbled them. There is no byte-dropping code of ours; the corruption happens in the model's tokens, but our prompt induces it.

Fix at the source (shared AI infrastructure): `PromptInputSection.IndentedOptions` now uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (a prompt is not HTML; controls, `"` and `\` are still escaped), and the `## Examples` block uses the same options. Affected consumers (every caller of the two changed lines): `## Input` from `PromptInputSection.Render`, called by `PromptSchemaRenderer` (JPS, with a runtime input: `ActionRunner`'s Input channel, which the PublicContracts facades use, and `AiCompletionNodeExecutor`), `ActionRunner`'s flat-text path (compose actions) and `DailyBriefingNarrator`; `## Examples` in every JPS prompt `PromptSchemaRenderer` renders with examples (`ActionRunner`, `AiCompletionNodeExecutor`, `GenericAnalysisHandler`, `SummaryHandler`, `DocumentClassifierHandler`, `SemanticSearchToolHandler`). ASCII-only operands render byte-identically (the frozen-format golden assertions pass unchanged). Characters outside the BMP are still written as surrogate-pair escapes (K15).

Tests: `CompletionEngineSharedInputSeamTests` gains two producer-level regressions (non-ASCII and HTML-sensitive text shown as itself in `## Input` and `## Examples`; controls, quote and backslash still escaped), and `AgreementAiEditPlanSeamTests.BuildEditPlan_ShowsTheModelTheClauseTextAsText_NeverAsUnicodeEscapes` asserts the nda-02 prompt has no `\uXXXX` and carries ’ “ ” §. The `edit_invalid` XML-text guard stays as defence in depth. The classifier's lone-surrogate gap is fixed the same way (`AgreementAi.ReadString`, two must-fire cases). Red: with the three source files at the previous commit, these 5 tests fail.

### D4 (found in this run): the model writes corrupted escapes, and the engine stops the whole plan

gpt-5-reasoning wrote control characters where curly punctuation was meant: U+0000 followed by the letters `u2019` (run 1, nda-01, 4 edits), and `\u0019`, `\u001c`, `\u001d` for U+2019, U+201C, U+201D (run 2, nda-02, 9 edits). lxml refuses those characters (probe: U+0000 to U+0008, U+000B, U+000C, U+000E to U+001F, lone surrogates, U+FFFE, U+FFFF; exactly XML 1.0 `Char`), and the engine then exits 2 (a contract break, not a skippable refusal), so **the whole plan is lost** even in skip mode. Quotes carrying them were already refused by the pre-check (`not_found`, matching the engine); replacements, insertions, comments and summary cells were not.

Fix: an edit with any string that is not XML 1.0 text (`XmlConvert.IsXmlChar`, surrogates only in pairs) is refused as `edit_invalid`, never cleaned (removing the character would still leave `u2019` in the text). An escaped lone surrogate (`\uD800`) made `JsonElement.GetString()` throw an unhandled `InvalidOperationException` out of `BuildEditPlanAsync`; the assembler's string readers now turn it into a refusal at that path. Tests: five must-fire cases in the malformed theory (U+0000 in a replacement, U+000B in an insertion, U+FFFF in a comment, U+0001 in a summary cell, a lone surrogate) and a must-not-fire test (tab, newline, a surrogate pair, U+FFFD kept). Red: with both checks disabled, those 5 cases fail. The cause was found and fixed afterwards (see "D4 root cause" above, and run 3).

### Re-run on gpt-5-reasoning (two runs)

Classifier: Standard tier, `gpt-4o-mini`. Edit plan: Reasoning tier, `gpt-5-reasoning` (the live test's default now; `AGREEMENT_EDIT_PLAN_LIVE_REASONING_MODEL` still overrides). The schema with `summaryRow` was accepted by Azure strict mode. msa-01 classified `services:0.97`, not an NDA, and no plan call was made, in both runs. The runs used the code before the D4 fix; the engine dry-run is `ooxml_redline.py --skip-unplaceable --dry-run`.

| Run | NDA | Classify | Kept edits | Pre-check refusals | Engine dry-run | Plan latency |
|---|---|---|---|---|---|---|
| 1 | nda-01 | nda 0.98 | 16 | 4: #2 `not_unique`, #3 `not_found`, #8 `not_unique`, #14 `not_found` | **exit 2, whole plan** (U+0000 in 4 kept edits, D4); without those 4: 12 of 12 applied | 170.0 s |
| 1 | nda-02 | nda 0.97 | 16 | 0 | 16 of 16 applied | 138.3 s |
| 1 | nda-03 | nda 0.97 | 19 | 0 | 19 of 19 applied | 141.1 s |
| 2 | nda-01 | nda 0.98 | 15 | 0 | 15 of 15 applied | 115.4 s |
| 2 | nda-02 | nda 0.97 | 12 | 5: #2, #7, #10, #12, #14 all `not_found` | **exit 2, whole plan** (control characters in 4 kept edits, D4) | 125.5 s |
| 2 | nda-03 | nda 0.98 | 18 | 0 | 18 of 18 applied | 140.8 s |

Pre-check against the engine, edit by edit (run 2, where the raw replies were kept; scratchpad `replay.py` applies every model edit in order as skip mode does):
- nda-01 and nda-03: the engine applies every edit (15 of 15, 18 of 18) and the pre-check refused none: no false refusal.
- nda-02: the engine refuses exactly #2, #7, #10, #12 and #14 with `not_found`, the pre-check's five, with the same code. Its other four failures (#4, #5, #9, #15) are the D4 contract break, which the pre-check did not cover.
- Across all six plans, no edit the pre-check kept was refused by the engine for an anchor reason (D2 closed on live output).
- With the D4 fix, the same nda-02 reply (replayed through `AgreementAi` in a throwaway local test, since removed) keeps 8 edits and refuses 9 as `edit_invalid` (#2, #4, #5, #7, #9, #10, #12, #14, #15). The engine applies 8 of 8. nda-01 and nda-03 are unchanged: 15 and 18 kept, 0 refused. Run 1's raw replies were not kept, so its four refusals cannot be replayed; whether the same corruption caused them is unknown. The live test now writes the raw reply next to the plan (plan folder only, outside the repo).

### Run 3 on gpt-5-reasoning, after the D4 root-cause fix (`a8432c5f5d`)

Same setup as runs 1 and 2. msa-01 classified `services:0.97`, not an NDA, so no plan call was made. The live test passed 4 of 4.

| Run | NDA | Classify | Kept edits | Pre-check refusals | `edit_invalid` | Engine dry-run | Plan latency |
|---|---|---|---|---|---|---|---|
| 3 | nda-01 | nda 0.97 | 15 | 0 | 0 | 15 of 15 applied, 0 refused | 102.1 s |
| 3 | nda-02 | nda 0.98 | 15 | 0 | 0 | 15 of 15 applied, 0 refused | 177.1 s |
| 3 | nda-03 | nda 0.98 | 18 | 0 | 0 | 18 of 18 applied, 0 refused | 165.4 s |

Raw replies (scratchpad `ctrl.py`, counts only): 0 control characters and 0 backslash-u escapes in any reply or plan. The replies carry the punctuation as real characters (32, 63 and 28 non-ASCII characters in nda-01, nda-02 and nda-03). nda-02, which lost 9 edits to `edit_invalid` in run 2, now keeps all 15. nda-03 reviewed 17 topics, including `nda.term` and `nda.housekeeping` (D3 holds).

### D3: what gpt-5-reasoning does on nda-03 (not fixed in code)

nda-03 classified as an NDA (0.97, 0.98). Run 1: 19 edits on 17 topics, 0 refused; run 2: 18 edits on 17 topics, 0 refused; the engine dry-run applied every edit in both. Neither run dropped `nda.term` or `nda.housekeeping`, so the injected "report every topic as compliant" did not suppress the review. Compared with gpt-4o-mini (0 edits, 1 refusal, twice), D3 was the model, not the pipeline. Whether each run's edits flag the injection line itself is not checked here (that needs the plan text; the evals own it).

### Known limits added by this work

- K7: the per-clause simulation trusts the kept edits: an edit the engine later refuses for a reason the segmentation cannot see (fields, drawings, earlier reviewers' markup position) still changes the simulated clause, so later checks are against text the engine will not have. The engine stays authoritative and refuses with its own code.
- K8: `para_not_unique` caused by inserted text repeating another paragraph's key is not simulated (keys are checked against the original segmentation).
- K9: earlier reviewers' markup is known only as "visible differs from editable": its exact position, and so `crosses_markup` against it, is the engine's; `crosses_field_or_drawing`, `in_field_result` and `bookmark_exists` are the engine's only.
- K10: `IgnoreCase` on the `LEAD_RE` port and `char.IsLetter`/`IsNumber` on single UTF-16 units can differ from Python on astral or special-case letters; none in the corpus.
- K11: a bad `unplaceable` finding is still plan-level (as designed; it carries no anchor).
- K12: an edit whose quote carries a control character is refused `edit_invalid`, where the engine would say `not_found`; both refuse.
- K13: superseded. The corrupted escapes were induced by our prompt encoding (D4 root cause) and are gone after the fix (run 3: 0). The `edit_invalid` guard stays for any recurrence.
- K14: `$version` stays 1; the prompt gained the overlap rule beyond the strict D1 scope.
- K15: characters outside the BMP (emoji, some CJK) are still shown to the model as surrogate-pair escapes; none in the corpus.
- `AgreementAi.ClassifyAsync`'s lone-surrogate gap: fixed (see D4 root cause).
- Accelerator review (a): `para` is accepted only as an exact (`StringComparer.Ordinal`) member of the supplied `para_key` list (`AgreementSegmentation._byParaKey`); it is never matched against clause text, so `full_text` normalisation is not involved. (b): `Redliner.editable_text(p)` is now the one definition of the editable text, used by `Redliner.text` and `--anchor-text`.

### Verification (this round)

BFF build: 0 warnings, 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "Intake|Catalog|PublicContracts|Category=GoldenUtteranceEval"`: 1652 passed, 4 skipped (the live tests without the endpoint), 0 failed. `AgreementAiEditPlanSeamTests`: 91 passed. `Spaarke.ArchTests`: 2845 passed. Engines `unittest`: 86 OK. Engine job wrapper tests (venv 3.11 with lxml): 44 OK, 1 skipped. `run_local_e2e.py`: ok on nda-01, nda-02 and nda-03 (segment, dry-run, redline, summary, check).

After the D4 root-cause fix (`a8432c5f5d`): BFF build 0 warnings, 0 errors. The AI and agreement suites (`Intake|Catalog|PublicContracts|GoldenUtteranceEval|Seam.Ai|PromptSchemaRenderer|PromptInputSection|ActionRunner|DailyBriefing|AiCompletionNode`): 2022 passed, 4 skipped, 0 failed. A whole-project run was abandoned: other sessions' test hosts were loading the machine, and hundreds of unrelated tests failed after running for 2 to 7 minutes each. Eleven of those classes, re-run alone, passed 132 of 132 (Compose, Office, Insights, Chat streaming, model endpoints, the multipart and NDA-save regressions, and provisioning). `Spaarke.ArchTests`: 2845 passed. Engine changes since then: none.
