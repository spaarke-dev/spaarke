# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-02 (Phase A done + Phase B code shipped, awaiting deploy + smoke)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | Wave 12 Linear AI Consumer migration — Doc Upload path built. |
| **Status** | **Phase A DONE** (bandaids reverted). **Phase B code DONE** (Linear primitives + DocumentProfileService + endpoint dispatch + DI wiring, build clean). **Awaiting**: unit tests → deploy → operator smoke. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` — HEAD is `c2d26986d` (feat: Linear AI Consumer library + Document Profile migration). |
| **Local commits ahead of origin** | **5** (4 reverts + Phase B feature commit). NOT YET PUSHED. |
| **Worktree** | `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7/` |
| **Next Action** | Decide: (a) add unit tests + deploy tonight; OR (b) skip unit tests, deploy now, operator smokes end-to-end. Task plan says B15/B16 (unit tests) → B17 (deploy) → B18 (smoke). |

---

## Three companion docs (READ IN THIS ORDER)

1. **Architecture** — [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
2. **Work spec** — [`notes/wave12-linear-consumer-migration.md`](notes/wave12-linear-consumer-migration.md)
3. **Task plan** — [`notes/wave12-linear-consumer-tasks.md`](notes/wave12-linear-consumer-tasks.md) — Phase A + B core marked complete.

---

## Phase A summary (bandaids reverted)

Reverts executed in **reverse chronological order** (safer than plan-listed order because A1–A3 replaced each other on the same `GetEntitySetNameAsync` function):

- `a648dedce` — Revert `15511117b` (nested-JSON skip)
- `1332a5a02` — Revert `1909b4432` (heuristic pluralization)
- `06040244e` — Revert `2021028da` ($filter form)
- `42a83ff7c` — Revert `4facf26ef` (accessor form)

Build after reverts: 0 errors, 19 pre-existing warnings.

## Phase B core summary (Linear AI Consumer library shipped)

Single commit `c2d26986d` — 17 files changed, 967 insertions.

New folder: `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/`

Primitives:
- `LinearRunContext.cs`, `DocumentText.cs` — records
- `IActionResolver` + `ActionResolver.cs` — config-driven (`LinearConsumersOptions.ActionIds` maps consumerType → ActionId; delegates to `IScopeResolverService.GetActionAsync`)
- `IDocumentTextSource` + `DocumentTextSource.cs` — composes `AnalysisDocumentLoader` (SPE + OBO) + `ITextExtractor` (direct-file)
- `IActionRunner` + `ActionRunner.cs` — wraps `IOpenAiClient.GetStructuredCompletionRawAsync` with a single `{{document.extractedText}}` placeholder binding

Consumer service:
- `DocumentProfileService.cs` — emits `AnalysisStreamChunk` SSE events; reuses existing `DocumentProfileFieldMapper` + `DocumentTypeMapper` (Choice coercion); persists via `IDocumentDataverseService.UpdateDocumentFieldsAsync` (typed SDK path — no metadata calls); enqueues RAG indexing via `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` (OBO path)

DI + wiring:
- `LinearConsumersModule.cs` + `LinearConsumersOptions.cs`
- `Program.cs` — `AddLinearConsumers(builder.Configuration)` after `AddAnalysisServicesModule`
- `AnalysisEndpoints.ExecuteAnalysis` — dispatches by playbookId: if match in `LinearConsumersOptions.PlaybookIds`, routes to `DocumentProfileService`; otherwise falls through to Playbook Engine (preserves engine for Chat / Insights / Daily Briefing)
- `ConsumerTypes.DocumentProfile` — new constant `document-profile`

Config:
- `appsettings.template.json` — new `LinearConsumers` section with:
  - `ActionIds["document-profile"]` = `bb356968-ebe9-f011-8406-7ced8d1dc988`
  - `PlaybookIds["document-profile"]` = `18cf3cc8-02ec-f011-8406-7c1e520aa4df`

Build: 0 errors. dotnet test not yet run.

---

## What's left for Phase B (B14–B19)

| Task | Status | Notes |
|---|---|---|
| B14 build | ✅ 0 errors | |
| B15 unit tests | ⏸️ NOT DONE | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/LinearConsumers/DocumentProfileServiceTests.cs` — happy path / Action-not-configured / LLM-fails |
| B16 `dotnet test` | ⏸️ NOT DONE | |
| B17 deploy BFF | ⏸️ NOT DONE | `pwsh scripts/Deploy-BffApi.ps1` — needs operator approval (visible to UAT users) |
| B18 operator smoke | ⏸️ NOT DONE | Document Upload wizard end-to-end |
| B19 commit + push | Partial — commit done as `c2d26986d`; **push NOT DONE** | 5 commits ahead of origin |

---

## Deferred / follow-up items

- Playbook-to-code compilation for remaining engine consumers (Chat, Insight Engine, summarize Assistant) — hits when we UAT summarize Assistant.
- Daily Briefing narrator formal refactor to shared Linear primitives — deferred to a follow-on cleanup pass. Do NOT touch during this migration.
- R5 Doc 06 Choice-field coercion pattern — DocumentProfileService reuses `DocumentTypeMapper.ToDataverseValue` today; may want dynamic metadata-cache lookup later.
- Phases C-G of task plan (File Summarize, Prefills, data cleanup, coexistence check, docs wrap-up) — sequential after Doc Upload passes UAT.

## Rollback for Phase B

If Phase B causes regressions:
- Revert `c2d26986d` — removes the whole Linear consumer library + endpoint dispatch + Program.cs wiring in one shot
- Then either revert the four Phase A reverts (to restore bandaids) OR leave them reverted (base is R7 pre-bandaid state)
- Cost: Doc Upload UAT blocks; operator falls back to whatever worked pre-Wave-12

---

## Design decisions taken during Phase B (may need review)

1. **Revert order deviation** — plan listed A1→A4 (oldest first) but I did A4→A1 (newest first) because A1-A3 replaced each other on the same function; reverting oldest-first would have conflicted. Same end state. Documented inline in the task plan checklist.
2. **`IActionResolver` = config-driven** rather than routing-table-driven — chose `LinearConsumersOptions.ActionIds` (IOptions map) over the plan's suggested `IConsumerRoutingService` → `IScopeResolverService.GetActionAsync` chain because (a) routing table returns playbookId not actionId — still need indirection to get to ActionId, and (b) config-driven is simpler for tonight's velocity. Can promote to routing-driven later without churning consumer service code (still calls `IActionResolver.ResolveAsync`).
3. **`IDocumentDataverseService.UpdateProfileAsync` was NOT added** — the existing `UpdateDocumentFieldsAsync(string, Dictionary<string, object?>, ct)` already serves the exact need (typed field map → SDK-based write, no metadata calls). Plan called for a new typed method; deemed unnecessary.
4. **Endpoint dispatch happens BEFORE DocumentContext pre-load** for the Linear path — avoids double-loading text since `DocumentTextSource.ExtractFromDocumentIdAsync` inside `DocumentProfileService` does the load itself. Engine path retains the pre-load unchanged.
5. **`IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync`** (OBO path) is used instead of the app-only path, because Doc Upload files were written by the user via OBO — MI cannot read them without a container-type app-registration (see SPE writer-identity rule in `PostUploadIndexingEnqueuer.cs`).

## Commits from this session

- Phase A reverts: `a648dedce`, `1332a5a02`, `06040244e`, `42a83ff7c` (4 commits, reverse chronological)
- Phase B feature: `c2d26986d`

All 5 are **LOCAL ONLY** — not pushed to origin yet. Task B19 will push after B15-B18 complete.

---

## Reference

- Architecture: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Work spec: [`notes/wave12-linear-consumer-migration.md`](notes/wave12-linear-consumer-migration.md)
- Task plan: [`notes/wave12-linear-consumer-tasks.md`](notes/wave12-linear-consumer-tasks.md)
- Historical doc-processing architecture: [`docs/architecture/sdap-document-processing-architecture.md`](../../docs/architecture/sdap-document-processing-architecture.md)
- Companion pattern (Playbook Engine + Daily Briefing narrator model): [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- Wizard integration: [`docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md`](../../docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md)

---

*End of current-task.md. Recovery point: Phase B core code shipped (commit `c2d26986d`), build clean, awaiting deploy/smoke decision from operator.*
