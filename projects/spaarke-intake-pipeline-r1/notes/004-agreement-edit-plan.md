# Task 004 notes: app-only agreement edit plan (`IAgreementAi`)

Status: code, catalog artifacts and tests done; **the live dev run is not done** (needs the owner's OK, see "Needs a live dev run"). POML status stays `in-progress`.

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
- `AgreementSegmentation.CheckAnchors` runs on every edit before it enters the plan. Exact string comparison after collapsing whitespace on both sides (the engine's `norm()`): `para` equals a `para_key` (no prefix matching); `find`/`after` occurs exactly once in that section's text and does not cut a word (the engine's `word_edge_ok`); `old` occurs exactly once in `find` and does not cut a word. No trimming, case folding, quote normalisation or search of other sections.
- A failing edit is left out of the plan and returned in `AgreementEditPlan.RefusedEdits` as `(Index, Code, TopicId)`, codes `para_not_found`, `not_found`, `not_unique`, `word_boundary`, `old_not_in_find` (`AgreementRefusalCodes`). The facade never retries; the harness (task 015, option B) decides whether to ask for one rewrite. Summary numbering and bookmarks are computed over the kept edits only.
- The engine stays the final authority (visible text, revisions, fields, content controls, hyperlinks). The pre-check is deliberately no more lenient than the engine except whitespace collapse (a tab in the text and a space in the quote match; the engine matches raw text, so the engine may still refuse such a quote).
- The accelerator source does not itself define the code names; they are the closed set the coordinator specified. If the accelerator publishes a different set, rename in `AgreementRefusalCodes` only.
- Still thrown as typed `AgreementAiException` (never a plan): output shape wrong (`output_invalid`, with JSON path), a topic outside the pinned standard (`plan_invalid`), an assembled plan the accelerator schema rejects (`plan_invalid`, instance location only).

**Structured Outputs.** The model output is the strict subset: `{edits:[{topic, para, find|null, old|null, replace|null, after|null, insert|null, comment|null, section|null, issue|null, revision|null, priority High|Medium|Low|null}], unplaceable:[{topic, section_index, comment}]}`: every member required, optionals nullable, `additionalProperties:false`, no `oneOf`/`format`/`pattern`/length bounds. The accelerator's `edit-plan.schema.json` cannot be sent as is (`oneOf` on `find`/`after`, `format`, `pattern`, `minLength`), so the facade validates the assembled plan against it in code. `edits[]` itself is expressible, so the STOP trigger did not fire. `agreement-review`'s closed output contract is untouched (the second STOP trigger did not fire): the plan comes from the separate `agreement-edit-plan` Action.

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
- K2: the pre-check collapses whitespace, the engine matches raw text, so a find with a space where the text has a tab passes here and may be refused by the engine. The engine stays authoritative; a refused plan then comes back as an engine failure for the harness (task 015/001).
- K3: `find "*"` is checked only for `old` (the whole section text stands in for "everything after the number and heading").
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
