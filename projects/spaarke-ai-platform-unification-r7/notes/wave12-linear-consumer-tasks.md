# R7 Wave 12 — Linear AI Consumer Migration — Task Plan

> **Created**: 2026-07-02
> **Companion**: [`wave12-linear-consumer-migration.md`](wave12-linear-consumer-migration.md) (work spec)
> **Architecture**: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)

Compact checklist. Read the two companion docs for context. Mark tasks complete inline as you go.

## Phase A — Revert engine bandaids + set clean baseline

- [x] **A1**. Revert commit `4facf26ef` (`GetEntitySetNameAsync` accessor form). Reverted as `42a83ff7c` (done reverse-chronological — see A6).
- [x] **A2**. Revert commit `2021028da` (`$filter` form). Reverted as `06040244e`.
- [x] **A3**. Revert commit `1909b4432` (heuristic pluralization). Reverted as `1332a5a02`.
- [x] **A4**. Revert commit `15511117b` (nested-JSON skip). Reverted as `a648dedce`.
- [x] **A5**. Rebuild BFF locally. `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors, 19 pre-existing warnings** (2026-07-02).
- [x] **A6**. Reverts executed in **reverse chronological order** (A4 → A3 → A2 → A1) rather than plan-listed order, because A1–A3 replaced each other on `GetEntitySetNameAsync` — reverting oldest-first would have conflicted; reverting newest-first peeled each layer cleanly. Same end state. No additional resolution commit needed. Deploy deferred to end of Phase B.

## Phase B — Shared primitives + Doc Upload (tonight's target)

- [ ] **B1**. Create folder `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/`
- [ ] **B2**. Add `LinearRunContext.cs` + `DocumentText.cs` (record types per work spec §"New components to build")
- [ ] **B3**. Add `IActionResolver.cs` + `ActionResolver.cs` — Singleton; delegates to `IConsumerRoutingService` for routing + `IScopeResolverService.GetActionAsync` for the row
- [ ] **B4**. Add `IDocumentTextSource.cs` + `DocumentTextSource.cs` — Scoped; two methods: `ExtractFromFileAsync(IFormFile,ct)` and `ExtractFromDocumentIdAsync(Guid documentId, LinearRunContext ctx)`. Internally calls `AnalysisDocumentLoader.GetDocumentAsync` + `ExtractDocumentTextAsync` (existing) OR `ITextExtractor.ExtractAsync` for direct file uploads.
- [ ] **B5**. Add `IActionRunner.cs` + `ActionRunner.cs` — Singleton; wraps `IOpenAiClient.GetStructuredCompletionRawAsync`. Reads Action's SystemPrompt + OutputSchemaJson + Temperature + ModelDeploymentId; performs the JPS `{{document.extractedText}}` binding (single placeholder — no template engine); calls LLM; returns raw `JsonElement`.
- [ ] **B6**. Add `LinearConsumersModule.cs` — DI registration. Register the four primitives.
- [ ] **B7**. Add `DocumentProfileFields.cs` + `DocumentProfileFieldMap.cs` — typed intermediate for the AI output. `FromAiOutput(JsonElement)` static factory + Choice-label-to-int for `sprk_documenttype`.
- [ ] **B8**. Add `DocumentProfileResult.cs` — endpoint response DTO.
- [ ] **B9**. Add `DocumentProfileService.cs` — composes B3, B4, B5 + `IDocumentDataverseService` + `IJobEnqueueService`. Follows the reference shape in the architecture doc §"Consumer service pattern".
- [ ] **B10**. If `IDocumentDataverseService.UpdateProfileAsync(Guid documentId, DocumentProfileFields fields, CancellationToken ct)` doesn't exist, add it. Route through `DataverseServiceClientImpl` (SDK-based; no metadata calls).
- [ ] **B11**. Modify `AnalysisEndpoints.ExecuteAnalysis` — before invoking `IPlaybookOrchestrationService.ExecuteAsync`, check whether `playbookId` matches the Document Profile playbook id. If so, invoke `DocumentProfileService.ExecuteAsync` and return; otherwise fall through to the existing engine path (for backward compat with any other endpoints hitting the same route).
- [ ] **B12**. Ensure `ConsumerTypes.DocumentProfile` constant exists (or add it) with routing configured in `sprk_playbookconsumer`.
- [ ] **B13**. Register the new services in `Program.cs` / appropriate DI module: `services.AddLinearConsumers();`
- [x] **B14**. Build BFF locally (`dotnet build`) — 0 errors, 19 pre-existing warnings.
- [~] **B15**. SKIPPED per **ADR-038 §7** binding rule + [`tests/CLAUDE.md`](../../../tests/CLAUDE.md) ban B7 ("all-mocks + trivial assertion"). The three planned tests (happy / Action-not-configured / LLM-fails) would require mocking every collaborator on `DocumentProfileService` — the exact antipattern ADR-038 bans. `tests/unit/Sprk.Bff.Api.Tests/...` is also NOT one of the 6 KEEP paths. Operator (2026-07-02) chose Path C: operator smoke is the validation gate; regression tests added later under `tests/integration/regression/` if bugs surface.
- [~] **B16**. SKIPPED (dependency on B15).
- [ ] **B17**. Deploy BFF (`pwsh scripts/Deploy-BffApi.ps1`) — healthz green.
- [ ] **B18**. Operator smoke Document Upload wizard end-to-end. Verify: doc row created; SPE upload succeeds; RAG index enqueued; `sprk_filesummary` + `sprk_filetldr` + `sprk_filekeywords` + `sprk_extractorganization` + `sprk_extractpeople` + `sprk_filetype` + `sprk_documenttype` all populated on the doc row.
- [ ] **B19**. Commit + push (operator smoke result note; final push of 5+ commits to origin).

## Phase B.5 — Action schema audit (new gate — added 2026-07-02)

Before Phase C begins, every Linear-target Action row MUST have `sprk_outputschemajson` populated. Discovered as a blocker during Phase B smoke: the "File Summary" Action (`ddaa441e-9f19-f111-8343-7c1e520aa4df`) has an empty column; same likely true for other candidates.

- [ ] **B5-1**. Query each candidate Action row via MCP `read_query` on `sprk_analysisaction`: `File Summary` (`ddaa441e-…`), `New Matter Field Extraction` (ACT-023, `89cc641a-df18-f111-8343-7c1e520aa4df`), `New Project Field Extraction` (ACT-024, `1e838114-7919-f111-8343-7ced8d1dc988`), Work Assignment prefill Action (TBD), Document Create Profile Action (TBD).
- [ ] **B5-2**. Confirm each row has non-empty `sprk_systemprompt` (JPS or plain text) — record which format each uses.
- [ ] **B5-3**. Populate `sprk_outputschemajson` on each row that's empty. Derive from the JPS `output.fields` if present; otherwise design from scratch to match the client-side DTO the endpoint returns.
- [ ] **B5-4**. Verify each schema against Azure OpenAI Structured Outputs constraints: `type: "object"`, `additionalProperties: false`, all properties in `required` list, no `maxLength` / `minLength` / `format` keywords, `enum` values match the Choice-field labels (or downstream `DocumentTypeMapper` equivalent).
- [ ] **B5-5**. Cross-check every AI-produced field name against the target Dataverse column max-length. Any field whose LLM output could exceed the column size must either (a) shrink the column, (b) shrink the LLM instruction, or (c) be derived deterministically in code (per the R7 W12 `sprk_filetype` decision — deterministic values NEVER come from the LLM).

## Phase C — File Summary migration

- [ ] **C0**. Prerequisite: Phase B.5 audit complete + File Summary Action row has `sprk_outputschemajson` populated.
- [ ] **C1**. Trace current call site: `WorkspaceFileEndpoints.RunSummarizePlaybookAsSSEAsync` — note its SSE emission pattern.
- [ ] **C2**. Add `FileSummarizeService.cs` — composes `IActionResolver` + `IDocumentTextSource` + `IActionRunner`. Emits SSE progress + final via an injected `SseEmitter` helper (extract from `WorkspaceFileEndpoints` shared helpers).
- [ ] **C3**. Add `ConsumerTypes.SummarizeFile` if not present; verify routing row.
- [ ] **C4**. Replace `WorkspaceFileEndpoints.SummarizeFilesAsync` playbook-invocation branch with `FileSummarizeService.ExecuteAsync`.
- [ ] **C5**. Unit tests. `dotnet test` all pass.
- [ ] **C6**. Deploy + operator smoke Summarize Files wizard end-to-end.
- [ ] **C7**. Commit + push.

## Phase D — Prefill migrations (Matter, Project, WA, Doc Create Profile)

- [ ] **D1**. `MatterPreFillService.cs` — replace `ExtractFieldsViaPlaybookAsync` internal method with a direct `IActionRunner.RunAsync` invocation using ACT-023.
- [ ] **D2**. Verify `AiPreFillResult` deserialization is unchanged; typed field extractors on client side unchanged.
- [ ] **D3**. Unit tests updated. `dotnet test` all pass.
- [ ] **D4**. Deploy + operator smoke Create Matter wizard prefill end-to-end.
- [ ] **D5**. `ProjectPreFillService.cs` — same treatment (ACT-024).
- [ ] **D6**. Deploy + operator smoke Create Project wizard prefill.
- [ ] **D7**. Work Assignment prefill — decide reuse-Matter vs extract-own. If reuse, no change; if extract, `WorkAssignmentPreFillService.cs` + WA Action row.
- [ ] **D8**. Deploy + operator smoke Create Work Assignment wizard prefill.
- [ ] **D9**. Document Create Profile — trace the current endpoint, add `DocumentCreateProfileService.cs` composing the primitives.
- [ ] **D10**. Deploy + operator smoke Document Create Profile wizard.
- [ ] **D11**. Commit + push.

## Phase E — Data cleanup (Dataverse row retirements)

After all six consumers have passed operator UAT:

- [ ] **E1**. Doc Profile: deactivate `sprk_analysisplaybook` `18cf3cc8-02ec-f011-8406-7c1e520aa4df` + its 3 nodes.
- [ ] **E2**. File Summarize: deactivate `sprk_analysisplaybook` `4a72f99c-a119-f111-8343-7ced8d1dc988` + its nodes.
- [ ] **E3**. Matter Prefill: deactivate `sprk_analysisplaybook` `2d660cad-d418-f111-8343-7ced8d1dc988` + its nodes.
- [ ] **E4**. Project Prefill: deactivate `sprk_analysisplaybook` `fc343e9c-3460-f111-ab0b-7c1e521b425f` + its nodes.
- [ ] **E5**. WA Prefill: if aliased to Matter, no action; if separate, deactivate its playbook.
- [ ] **E6**. Doc Create Profile: deactivate its playbook + nodes.
- [ ] **E7**. Smoke ALL six consumers once more to confirm they don't rely on the deactivated rows.

## Phase F — Coexistence verification (playbook engine still works)

- [ ] **F1**. Smoke Chat: open a Spaarke Assistant chat session, send a message, verify reply.
- [ ] **F2**. Smoke Insight Engine: run one Insight action (if operator has a canonical one).
- [ ] **F3**. Smoke Daily Briefing: open the widget, force-refresh, verify render.
- [ ] **F4**. `dotnet test` full suite — all pass.
- [ ] **F5**. BFF publish size check — under NFR-01 ceiling (~60 MB compressed).

## Phase G — Documentation + wrap-up

- [ ] **G1**. Write `docs/guides/BUILD-A-NEW-LINEAR-AI-CONSUMER.md` — step-by-step tutorial using Doc Profile as reference.
- [ ] **G2**. Update `docs/architecture/sdap-document-processing-architecture.md` — mark the "AI Processing Pipeline" section as split into Linear vs Playbook; add pointer to the new architecture doc.
- [ ] **G3**. Update `docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md` — mark Status "Ratified" now that reference implementations exist.
- [ ] **G4**. Add change-log entry to `.claude/CHANGELOG.md`: "R7 W12 — Linear AI Consumer Pattern established; 6 consumers migrated off Playbook Engine."
- [ ] **G5**. Update `projects/INDEX.md` — R7 hot-path unchanged (BFF still touched).
- [ ] **G6**. Update `current-task.md` — mark Wave 12 Linear migration complete; queue any deferred items.
- [ ] **G7**. Commit + push final; merge to master when green.

## Reference for Claude Code after compaction

If you have been re-invoked after a compaction and need to pick this work up:

1. Read `current-task.md` Quick Recovery — it points you at the current phase and task
2. Read the architecture doc (link at top of this file) — the pattern
3. Read the work spec (link at top) — the goal + scope + reference info
4. Read this task plan (this file) — the checklist
5. Find the next unchecked task; execute it
6. Mark it complete inline (edit this file)
7. Move to the next; repeat

**Do not deviate from this task order without explicit operator approval.** Each phase has a UAT gate.
