# R7 Wave 12 Linear Migration — Technical Debt Inventory

> **Generated**: 2026-07-02 (end of Linear consumer migration session)
> **Scope**: everything introduced OR left uncleared by the Linear AI Consumer migration
> **Owner**: R7 Wave 12 follow-on work / R8

Grouped by (a) MUST-CLEAN — blocks confidence or invites bugs; (b) SHOULD-CLEAN — removes dead code + drift risk; (c) NICE-TO-CLEAN — polish + future-proofing.

---

## MUST-CLEAN (before we forget)

### D-1. JPS ↔ Schema drift on Document Profiler Action

**Issue**: I removed `sprk_filetype` from the Action's `sprk_outputschemajson` (strict schema sent to Azure OpenAI). But the Action's `sprk_systemprompt` (JPS) still contains `sprk_filetype` in `instruction.constraints` + `output.fields` + `examples[0].output`. LLM sees instructions that no longer match the schema. Wastes tokens; misleading to future JPS editors.

**Fix**: Edit Action `bb356968-…`'s `sprk_systemprompt` — remove `sprk_filetype` from all three JPS sections. Add a comment noting `sprk_filetype` is derived from file extension in code.

**Effort**: 15 min (Dataverse update).

### D-2. Migration spec references a non-existent "Document Create Profile" consumer

**Issue**: `wave12-linear-consumer-migration.md` lists 6 in-scope consumers; the 6th ("Document Create Profile") doesn't exist. Migration search found no matching endpoint or wizard. Future readers will chase phantom work.

**Fix**: Update the migration spec — mark consumer #6 as "not found; removed from scope on 2026-07-02".

**Effort**: 5 min.

### D-3. Docs sync — Wave 12 additions not in original R7 spec / design

**Issue**: `spec.md` (33 FRs) and `design.md` (v0.6) both predate Wave 12 scope-expansion. They don't reference the Linear AI Consumer library or the 5 wizard migrations. Anyone reading R7's spec today would miss ~30% of what shipped.

**Fix**: Add a Wave 12 addendum section to `spec.md` + `design.md` OR create a single R7 delivery-summary doc (like tonight's UAT checklist doc) linked from the top of the spec.

**Effort**: 30-60 min.

### D-4. `WA Prefill` client-side hardcoded endpoint

**Issue**: `CreateWorkAssignmentWizard/EnterInfoStep.tsx` hardcodes `endpoint: '/api/workspace/matters/pre-fill'`. Brittle — if Matter endpoint contract changes for a Matter-only reason, WA wizard breaks silently. WA has no dedicated Action row + schema either.

**Fix**: Two options — (a) accept it, add a JSDoc comment on the client explaining the intentional alias; OR (b) split into a dedicated `/api/workspace/work-assignments/pre-fill` endpoint backed by its own Action row (WA-specific schema). Operator preference required.

**Effort**: option (a) = 15 min doc; option (b) = 2-3 hours (new endpoint + Action row + schema).

---

## SHOULD-CLEAN (removes dead code + drift risk)

### D-5. Phase E — deactivate 4 migrated playbook rows

**Issue**: `sprk_analysisplaybook` rows `18cf3cc8-…`, `ddaa441e-…`, `2d660cad-…`, `fc343e9c-…` are all still `statecode: 0` (Active). They're now dead paths — never routed to when Linear config is present. Live-but-unreachable rows confuse audits.

**Fix**: Set `statecode = 1` on each row via MCP `update_record`. Also deactivate the associated `sprk_playbooknode` rows. Fully reversible.

**Effort**: 15 min (Dataverse-only). Should be done AFTER Phase F coexistence smoke passes.

### D-6. Diagnostic log line in `AnalysisEndpoints.ExecuteAnalysis`

**Issue**: I left an INFO-level log at the top of `ExecuteAnalysis` that dumps `LinearConsumers` dispatch state on every request (`[LinearDispatch]` prefix). Was added to debug the overwrite race; not needed in steady state.

**Fix**: Remove the log line. Or downgrade to DEBUG so it only fires when the log level is dialed up.

**Effort**: 10 min.

### D-7. Engine-path methods in prefill services become dead code

**Issue**: Once the Linear config is present on both `matter-pre-fill` and `project-pre-fill`, the `ExtractFieldsViaPlaybookAsync` methods in `MatterPreFillService.cs` + `ProjectPreFillService.cs` are unreachable. They still work if Linear config is absent — but that's the "engine fallback" scenario we're moving away from.

**Fix (staged)**:
1. Confirm the Linear path is stable for 30 days in prod
2. Delete `ExtractFieldsViaPlaybookAsync` methods
3. Delete the `RequireAi() → IWorkspacePrefillAi` seam if no other caller uses it
4. Remove `IPlaybookLookupService` + `IConsumerRoutingService` + `IWorkspacePrefillAi` from the constructors

**Effort**: 1-2 hours after 30-day soak.

### D-8. Prefill parser fallbacks (`ParseAiResponse` branches) become dead code

**Issue**: `MatterPreFillService.ParseAiResponse` has three fallback branches:
- Direct-schema parse (primary — Linear path always hits this)
- `TryParseEntityExtractionFormat` (fallback for old engine JSON shapes)
- `TryExtractFromPartialJson` (regex-based recovery for truncated LLM output)

With strict-mode schemas + constrained decoding, the LLM cannot emit entity-extraction format — that path is now dead. Truncated-output recovery is still useful (see MaxOutputTokens issue below), so keep that one.

**Fix**: After D-7 soak, delete `TryParseEntityExtractionFormat` from both services. Keep `TryExtractFromPartialJson` as truncation-recovery.

**Effort**: 30 min.

### D-9. `useAiSummary` (Document Upload wizard) not migrated to `useLinearRunProgress`

**Issue**: Doc Upload wizard has its own inline SSE consumer (`useAiSummary.ts`). The subagent shipped `useLinearRunProgress` as the canonical shared hook — Doc Upload would benefit from adopting it (single implementation + Fluent v9 UX).

**Fix**: Migrate `useAiSummary` → `useLinearRunProgress`. Some wizard-specific state (typed fields extractor) stays local; the SSE consumption + progress-list rendering moves to shared.

**Effort**: 2-4 hours (client-side; already scoped in `wave12-client-shared-progress-follow-up.md`).

### D-10. `AiProgressStepper` component still in use

**Issue**: Numbered visual-bar component (the one Summarize wizard used to use) still consumed by PlaybookBuilder ExecutionOverlay + AnalysisWorkspace. Its "opinion" (steps as discrete numbered nodes) doesn't fit the honest scrolling-text approach we've adopted.

**Fix**: Migrate the remaining consumers to `<LinearRunProgressList>` OR keep `AiProgressStepper` for surfaces where a KNOWN fixed step-list makes sense (e.g., PlaybookBuilder execution). Cross-cutting decision — should be scoped in a follow-on client-refactor task.

**Effort**: 4-8 hours (component-by-component migration + testing).

### D-11. App Service settings drift risk

**Issue**: All 6 `LinearConsumers__*` App Service settings on `spaarke-bff-dev` live in Azure config. If another team resets or overwrites App Service settings during their deploy, our Linear consumers silently fall back to the engine path (which no longer works for Doc Upload post-revert). We already saw one overwrite race tonight.

**Fix**: Two-part —
- (a) Document the required App Service settings in a runbook (`docs/guides/spaarke-bff-dev-required-settings.md`) so any team touching config knows what NOT to remove.
- (b) Optional: startup health-log check — on BFF startup, log an ERROR if `LinearConsumersOptions.ActionIds` is empty. Fail-visible instead of fail-silent.

**Effort**: (a) = 30 min; (b) = 1 hour.

---

## NICE-TO-CLEAN (polish + future-proofing)

### D-12. Doc Profiler JPS is verbose / redundant

**Issue**: Operator noted the Doc Profiler `sprk_systemprompt` includes instructions that "don't seem useful". Simpler prompts usually produce better LLM output.

**Fix**: Editorial pass on Action `bb356968-…`'s JPS — strip redundancy in `instruction.constraints`, tighten `output.fields[N].description`. Would benefit from a JPS best-practices doc.

**Effort**: 1-2 hours (per Action row; all 4 Linear Action rows probably deserve this pass).

### D-13. Test coverage — no LinearConsumers unit/integration tests

**Issue**: I skipped B15/B16 unit tests per operator's Path C choice (ADR-038 forbids the mock-heavy tests the plan called for). No tests exist for `IActionResolver`, `IActionRunner`, `IDocumentTextSource`, `DocumentProfileService`, `FileSummarizeService`. Future regressions would surface only through operator smoke.

**Fix**: Write integration tests under `tests/integration/contract/Api/Ai/` that boot WebApplicationFactory + Moq only at OpenAI + Dataverse boundaries. One per consumer service. Covers regression cases like the field-mapping bug we hit tonight (empty fields dict).

**Effort**: 4-8 hours (test infra + 5 consumer tests).

### D-14. `MaxOutputTokens` global default of 500 is too low

**Issue**: `DocumentIntelligenceOptions.MaxOutputTokens` default is 500 (validator range 100-4000). File Summary needs 4000. Any future rich-output consumer will need per-consumer overrides. Range max of 4000 may also block bigger consumers.

**Fix**: (a) Raise the global default to something more reasonable (1500-2000). (b) Consider raising the validator's max to 8000+ for future-proofing (some Azure OpenAI models support 16K+ output tokens).

**Effort**: 15 min.

### D-15. Schema ↔ prompt drift validation

**Issue**: An Action row has a JPS in `sprk_systemprompt` AND a strict JSON schema in `sprk_outputschemajson`. If a maker edits one without the other, the LLM produces output that doesn't match. No safety net.

**Fix**: Add an admin-side validator — parse the JPS's `output.fields`, compare to the strict schema's properties. Flag mismatches. Could be a background job, a `/jps-validate` skill enhancement, or a maker-portal check on save.

**Effort**: 4-8 hours (depending on where it lives).

### D-16. Migration spec Phase B.5 audit didn't formally become a process

**Issue**: Tonight I added a Phase B.5 gate — audit + populate `sprk_outputschemajson` on each Linear-target Action row BEFORE migration begins. It was ad-hoc; not codified as reusable process.

**Fix**: Document Phase B.5 as a "New Linear Consumer Checklist" in the `BUILD-A-NEW-LINEAR-AI-CONSUMER.md` doc (Phase G work).

**Effort**: 30 min (folded into Phase G).

### D-17. `Document Profile` playbook rows will confuse Chat's Doc Summary routing

**Issue**: Compose R1 (`compose-summarize`) uses playbook `47686eb1-…` (Document Summary — shared with chat-summarize). After Phase E deactivates the migrated playbook rows, someone looking at Dataverse might mistake the still-active Compose playbook for a migration miss.

**Fix**: Add a note to the Action / Playbook rows explaining which are Linear-migrated (deactivated) vs still-engine (active). Could be a `sprk_notes` field or a naming-convention update.

**Effort**: 30 min once the pattern is decided.

### D-18. Additional properties + strict-mode schema documentation

**Issue**: I rewrote Matter + Project prefill schemas from `additionalProperties: true` (with rich XML rationale about engine parser resilience) to `additionalProperties: false` for strict mode. Lost the intent context. Future maintainers may not understand why fields are all-nullable-but-all-required.

**Fix**: Add a top-level JSON `$comment` in each schema explaining strict-mode necessity + why fields are nullable-but-required (Azure OpenAI strict spec).

**Effort**: 15 min per schema (4 schemas).

---

## Summary — recommended order of ops

**Immediate (next session, 1-2 hrs total)**:
1. D-1 — Doc Profiler JPS remove `sprk_filetype`
2. D-2 — Migration spec remove phantom Doc Create Profile
3. D-3 — R7 delivery summary doc (already drafted tonight as UAT checklist)
4. D-5 — Phase E deactivate 4 playbook rows (after coexistence smoke)
5. D-6 — Remove diagnostic log line
6. D-14 — Raise `MaxOutputTokens` default

**Soon (Phase G scope, 4-6 hrs total)**:
7. D-8 — Delete `TryParseEntityExtractionFormat` after soak
8. D-11 — Runbook for required App Service settings
9. D-12 — JPS editorial pass on 4 Linear Action rows
10. D-16 — Codify Phase B.5 in maker-tutorial doc

**When we have client-refactor bandwidth (8-12 hrs)**:
11. D-9 — Migrate Doc Upload's `useAiSummary` → `useLinearRunProgress`
12. D-10 — Migrate remaining `AiProgressStepper` consumers

**Long-tail (R8 or dedicated project)**:
13. D-7 — Delete engine-path methods (after 30-day soak)
14. D-13 — Integration tests for LinearConsumers library
15. D-15 — Schema ↔ prompt drift validator
16. D-4 — Decide WA Prefill dedicated endpoint vs kept-as-alias
17. D-17 — Naming convention for migrated Action / Playbook rows
18. D-18 — Rich schema `$comment` explaining strict-mode contract

---

## Cross-references

- Linear architecture: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Migration spec: [`wave12-linear-consumer-migration.md`](wave12-linear-consumer-migration.md)
- Task plan: [`wave12-linear-consumer-tasks.md`](wave12-linear-consumer-tasks.md)
- Client shared progress follow-up: [`wave12-client-shared-progress-follow-up.md`](wave12-client-shared-progress-follow-up.md)
- Full-R7 UAT checklist: [`wave12-uat-checklist-2026-07-02.md`](wave12-uat-checklist-2026-07-02.md)
