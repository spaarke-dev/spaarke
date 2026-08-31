# RS-2 — sprk_aitopicregistry "Matter Summary" target-field fix + InvoiceExtractionJobHandler comment trace

> Task 041 (RS-2). Environment: `spaarkedev1`. Executed 2026-08-25.

## 1. Dataverse data fix (steps 1-3) — COMPLETE, verified

**Row**: `sprk_aitopicregistry` id `cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6` (`sprk_topicname=matter-summary`, `sprk_playbookname=chat-summarize`).

**Before** (GET, pre-write):
```json
{"sprk_hostentity":"sprk_matter","sprk_enabled":true,"sprk_mode":"single","statecode":0,
 "sprk_topicname":"matter-summary","sprk_playbookname":"chat-summarize",
 "sprk_aitopicregistryid":"cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6","sprk_targetfield":"sprk_mattersummary"}
```

**Write**: `PATCH sprk_aitopicregistries(cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6)` body `{"sprk_targetfield":"sprk_recordsummary"}` → HTTP 204.

**After** (GET, re-query, ETag changed 14349324 → 23866967):
```json
{"sprk_hostentity":"sprk_matter","sprk_enabled":true,"sprk_mode":"single","statecode":0,
 "sprk_topicname":"matter-summary","sprk_playbookname":"chat-summarize",
 "sprk_aitopicregistryid":"cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6","sprk_targetfield":"sprk_recordsummary"}
```

`sprk_topicname`, `sprk_playbookname`, `sprk_hostentity`, `sprk_enabled`, `sprk_mode`, `statecode` are byte-identical before/after — only `sprk_targetfield` changed.

**Negative check**: full list of `sprk_aitopicregistry` rows (only 2 exist) confirms the sibling "Matter Health Insight" row (`c46b940e-4b65-f111-ab0c-70a8a590c51c`, topic `matter-health`) is untouched — still `sprk_targetfield=sprk_performancesummary`. No other row was modified.

## 2. §10 test-update obligation grep (step 5) — does not fire

`grep -r sprk_aisummary tests/` → **zero matches**. No test asserts on the stale comment text or the field name. No test file was touched.

## 3. Consumer trace for `extraction.aiSummary` (step 6) — ESCALATION TRIGGER 1 FIRED, no code changed

Task premise (from the POML step-4 prescribed comment text): *"the summary is placed in the extraction.aiSummary context variable ... for the playbook OutputRouter, whose registry-driven target is sprk_recordsummary."* This premise does **not** hold empirically. Findings, in order of discovery:

1. **`extraction.aiSummary` is set exactly once** — `InvoiceExtractionJobHandler.cs:236` (`context.SetVariable("extraction.aiSummary", GenerateAiSummary(aiExtractionResult))`), and referenced nowhere else in the BFF C# source (grepped `extraction\.aiSummary` across `src/server/api/Sprk.Bff.Api/` — only the setter and two doc-comments in `IOutputOrchestratorService.cs` mentioning it as an example key).
2. **The would-be consumer is `OutputOrchestratorService.ApplyOutputMappingAsync`** (`Services/Ai/OutputOrchestratorService.cs`), driven by `${extraction.aiSummary}`-style variable references inside a playbook's `sprk_configjson` outputMapping JSON — **NOT** `OutputRouter`/the topic-registry mechanism the step-4 text names. `OutputRouter.cs` (`Services/Ai/OutputRouter.cs`) is the chat-session ledger/disposition router (the class the `sprk_aitopicregistry` fix in §1 actually feeds, via `TopicRegistryWorkProductPersister`); it has no relationship to `InvoiceExtractionJobHandler`'s invocation of `_outputOrchestrator.ApplyOutputMappingAsync(playbook.Id, context, ct)` (line 326). These are two structurally distinct mechanisms that happen to share the word "output."
3. **`OutputOrchestratorService` never actually reads `sprk_configjson`.** `PlaybookService.GetPlaybookAsync` (`Services/Ai/PlaybookService.cs:193-229`) `$select`s `sprk_analysisplaybookid,sprk_name,sprk_description,sprk_jps_matching_metadata,sprk_indexstatus,sprk_indexhash,sprk_lastindexedat,sprk_ispublic,_ownerid_value,createdon,modifiedon,sprk_playbookcapabilities` — **no `sprk_configjson`** — and the internal `PlaybookEntity` class (line 1011) has no property for it either. `PlaybookResponse.ConfigJson` therefore always resolves to its DTO default `"{}"` (`PlaybookDto.cs:163`) for every playbook fetched through the real `PlaybookService`. `OutputOrchestratorService.ParseOutputMapping` (`OutputOrchestratorService.cs:317-346`) short-circuits on `ConfigJson == "{}"` and returns `null`, so `ApplyOutputMappingAsync` always hits the "no outputMapping defined, skipping updates" branch (line 55) and returns an empty success — **for every playbook, not just this one.**
4. **Even setting #3 aside, the live data has nothing to read.** Queried `spaarkedev1` directly: the "Finance Invoice Processing" playbook (`sprk_analysisplaybookid=1e657651-9308-f111-8407-7c1e520aa4df`, the only playbook whose name contains "Invoice") has `sprk_configjson = null`. `sprk_configjson` (Memo) does exist as a column on `sprk_analysisplaybook` (metadata-confirmed) — it is simply unpopulated for this row.
5. **The seed reference file `scripts/seed-data/playbooks.json`** (not live Dataverse — a local seed/reference artifact) DOES define an `outputMapping.updates[].fields["sprk_aisummary"] = "${extraction.aiSummary}"` for `entityType: "sprk_invoice"` (lines ~271-278). Checked live schema: `sprk_invoice` does **NOT** have a `sprk_aisummary` column (confirmed via `EntityDefinitions` metadata query) — consistent with the same 2026-08-25 summary-standardization deletions this task is remediating elsewhere. `sprk_invoice` **does** have `sprk_recordsummary`. So *if* this seed mapping were ever loaded into `sprk_configjson` and *if* `PlaybookService` were fixed to read it, it would need retargeting to `sprk_recordsummary` too — but neither precondition is true today, so no live write is happening or would happen.

**Net conclusion**: `extraction.aiSummary` has **no live consumer at all** today — not `sprk_aisummary`, not `sprk_recordsummary`. This is a pre-existing plumbing gap in `PlaybookService.GetPlaybookAsync` (missing `sprk_configjson` selection/mapping) plus an unpopulated `sprk_configjson` on the one relevant live playbook row — neither caused by, nor cured by, the 2026-08-25 summary-standardization column deletions this task targets. It is unrelated in kind to the RS-2 registry-row defect (which *was* a live break: an enabled row pointing at a deleted column) — this is instead dead/unwired code that never wrote anywhere, deleted column or not.

**Per the POML's escalation trigger 1** ("If the consuming mapping for extraction.aiSummary turns out to target something other than sprk_recordsummary ... STOP and report ... Do NOT silently retarget additional rows, playbooks, or mappings — that is scope expansion"): this fires. The consuming mapping does not confirmedly target `sprk_recordsummary` — it targets nothing, because the mechanism is not wired end-to-end. Fixing `PlaybookService.cs` to select `sprk_configjson`, and/or populating the live playbook's `sprk_configjson`, and/or retargeting the seed file, are all out of this task's scope (the task's own constraint restricts `src/server/api/**` changes to "ONLY the comment block").

## 4. Comment fix (step 4) — NOT PERFORMED, escalated

The step-4 prescribed replacement text ("...for the playbook OutputRouter, whose registry-driven target is sprk_recordsummary") asserts two things §3 disproves: (a) the mechanism is `OutputRouter`/registry-driven (it is `OutputOrchestratorService`/playbook-`ConfigJson`-driven — a different class), and (b) the target is confirmed `sprk_recordsummary` (no target is currently reachable at all). Writing that text would introduce a new false comment in place of the old stale one. No edit was made to `InvoiceExtractionJobHandler.cs`; the stale comment at lines 382-385 (still literally: `/// Maximum 5000 characters to fit in sprk_aisummary field.`) is unchanged. `git diff` under `src/server/` is empty for this task.

Since no `.cs` file was touched, `dotnet build src/server/api/Sprk.Bff.Api/` was not re-run as part of this task (build state is unchanged from pre-task baseline).

## 5. Recommended next step (for human / main session decision)

Options per CLAUDE.md §6:
- **(a)** Accept a narrower, purely-factual comment rewrite now (e.g., "*Placed in the extraction.aiSummary context variable (line 236) for the playbook OutputOrchestrator's outputMapping — currently unconfigured for this playbook (sprk_configjson is null); no field is written by this path today.*") — a Path-C-style pivot that fixes the comment's stale field name without asserting the unverified `sprk_recordsummary` destination. Still comment-only, still inside the task's `src/server/**` constraint.
- **(b)** File a follow-up task/issue for the deeper defect: `PlaybookService.GetPlaybookAsync` doesn't select `sprk_configjson`, so `OutputOrchestratorService`'s entire outputMapping mechanism is a no-op for every playbook, plus the "Finance Invoice Processing" playbook's `sprk_configjson` is unpopulated live. This is a separate, larger fix (code + Dataverse config) than a comment edit.
- **(c)** Re-scope task 041's step 4 explicitly once (a)/(b) are decided, then have a follow-up execution apply it.

No further Dataverse writes or code edits were made beyond §1's single-field PATCH.
