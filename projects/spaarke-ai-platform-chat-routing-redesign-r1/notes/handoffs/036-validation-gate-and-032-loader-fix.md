# Task 036 — FR-12 validation gate + Task 032 loader-gap closure (bundled)

> **Date**: 2026-06-22
> **Author**: Main session (E2 path: tight-scope sub-agent for code edits + main session verify)
> **Verdict**: ✅ **PASS** — FR-10 now functionally live in production; FR-12 validation gate active; 9 unit tests + 10 regression tests pass; no publish-size change

---

## Why this bundle exists

The owner's diagnostic question ("AI Search has playbook entries — if no endpoint, how?") surfaced TWO issues:

1. My Wave 2-C endpoint-block was wrong (endpoint exists at `POST /api/ai/playbooks/{id}/index`). Retracted in commit `8539ecd2d`.
2. Task 032 had a loader gap: `JpsMatchingMetadata` was added to `PlaybookEmbeddingDocument` but never populated by `PlaybookIndexingService`, and `PlaybookResponse` didn't expose it. Effect: FR-10 extension was functionally dormant in production. Documented in `032-loader-gap-and-036-bundling.md`.

Per owner direction (E2), this bundle:
- Closes the loader gap (Task 032 production wiring)
- Adds FR-12 validation gate (Task 036)
- Done as one logical change because both need the same `PlaybookResponse` extension

---

## Files changed

| # | File | Change |
|---|---|---|
| 1 | `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookDto.cs` | Added `JpsMatchingMetadata` (`string?`) property to `PlaybookResponse` + `[JsonPropertyName("sprk_jps_matching_metadata")]` to internal `PlaybookEntity` DTO |
| 2 | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs` | Added `sprk_jps_matching_metadata` to 2 `$select` strings (lines 176, 366 — sites that hydrate `PlaybookResponse`); set `JpsMatchingMetadata = entity.JpsMatchingMetadata` in 2 projections (lines 195, 418). Skipped line 502 ($select for `PlaybookSummary` — column would be dead-weight) |
| 3 | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingService.cs` | Added `JpsMatchingMetadata = playbook.JpsMatchingMetadata,` to document initializer. **Closes the task 032 loader gap** |
| 4 | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexInputValidator.cs` (NEW) | `sealed class PlaybookIndexInputValidator` with `Validate(PlaybookResponse) → IReadOnlyList<string>`. Reuses `PlaybookEmbeddingService.ParseJpsMatchingMetadata`; adds private `TryReadOutputDestination` for the string field. Tolerant of null/whitespace/malformed/non-object/missing/non-string — all degrade to "missing". ADR-015-safe (no JSON content logging). Returns stable-ordered list: `description` / `documentTypes` / `destinationHint` |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEmbeddingEndpoints.cs` | Refactored `IndexPlaybook`: `static IResult` → `static async Task<IResult>`. Injects `IPlaybookService` + `PlaybookIndexInputValidator` + `CancellationToken`. Flow: load → 404 if null → `Validate` → 400 ProblemDetails with `extensions["missingFields"]: string[]` if any missing → existing enqueue path → 202/503. Added `.ProducesProblem(400)` + `.ProducesProblem(404)` |
| 6 | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` | Registered `services.AddSingleton<PlaybookIndexInputValidator>()` unconditionally (endpoint mapped unconditionally — asymmetric-registration anti-pattern avoided per CLAUDE.md §10 F.1). DI audit comment: 11/15 → 12/15 |
| 7 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookEmbedding/PlaybookIndexInputValidatorTests.cs` (NEW) | 5 xUnit/FluentAssertions tests: empty result with valid input; reports description; reports documentTypes; reports destinationHint; reports all-three when missing/malformed. JSON examples align with `architecture/jpsmatchingmetadata-schema.json` |

---

## Verification results

| Check | Result |
|---|---|
| BFF build | ✅ 0 errors, 16 pre-existing warnings (unchanged baseline) |
| New unit tests + task 032 tests | ✅ 9/9 pass in 3 ms (4 from 032 + 5 from new validator) |
| Phase 1 regression suite | ✅ 10/10 pass in 211 ms (no regression from FR-10 production wiring) |
| BFF publish (compressed) | ✅ 46.08 MB — same as task 032 (no delta) |

---

## Acceptance criteria (FR-12)

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Missing-description POST returns 400 with `MissingFields = ["description"]` | ✅ verified by `Validate_ReportsDescription_WhenDescriptionIsNullOrWhitespace` |
| 2 | Missing-documentTypes POST returns 400 with `MissingFields` containing `"documentTypes"` | ✅ verified by `Validate_ReportsDocumentTypes_WhenJpsMetadataMissingDocumentTypes` |
| 3 | Missing-destinationHint POST returns 400 with `MissingFields` containing `"destinationHint"` | ✅ verified by `Validate_ReportsDestinationHint_WhenJpsMetadataMissingOutputDestination` |
| 4 | All three missing → `MissingFields` has all 3 entries | ✅ verified by `Validate_ReportsAllThree_WhenAllAreMissingOrJsonIsMalformed` |
| 5 | Valid POST returns 200 / 202 | ✅ valid path: existing `EnqueueIndexing` → 202 Accepted |
| 6 | Unit tests pass | ✅ 5/5 |
| 7 | BFF publish delta within NFR-01 | ✅ 0 delta vs task 032; still 46.08 MB |

## Acceptance criteria (FR-10 production wiring)

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `JpsMatchingMetadata` round-trips from Dataverse `sprk_jps_matching_metadata` → `PlaybookResponse` → `PlaybookEmbeddingDocument` → embed input | ✅ verified by code inspection at the 4 hand-off sites; FR-10 unit tests (4 from task 032) still pass |
| 2 | Tolerant fallback preserved | ✅ task 032 tests cover null / full / malformed / partial |
| 3 | No regression in Phase 1 stable-ID migration | ✅ 10/10 |

---

## ADR / governance checks (inline code-review)

| Item | Verdict |
|---|---|
| ADR-013 AI facade boundary | ✅ all new code in `Services/Ai/PlaybookEmbedding/`; validator + endpoint stay in BFF; no PublicContracts crossings |
| ADR-014 caching | ✅ no cache changes; new JpsMatchingMetadata flows through the same compose path |
| ADR-015 tier-1 logging | ✅ validator XML-documented as ADR-015-safe; never logs JSON content |
| ADR-019 ProblemDetails | ✅ endpoint returns ProblemDetails with `extensions["missingFields"]` array per spec FR-12 wording |
| ADR-029 publish hygiene | ✅ no publish-size delta from task 032 baseline (46.08 MB) |
| ADR-010 DI minimalism | ✅ validator is a singleton; no interface (concrete-class injection per ADR-010); endpoint composes via direct DI |
| CLAUDE.md §10 F.1 asymmetric-registration | ✅ validator + endpoint both unconditional; no flag-gating |
| CLAUDE.md §11 Component Justification | ✅ Existing: validator extends the FR-10 parse helper already in `PlaybookEmbeddingService`. Extension: separate class because validator has 3-field semantics distinct from embed-input composition. Cost-of-doing-nothing: FR-12 acceptance fails (no missingFields response shape); FR-10 still dormant if loader gap unclosed |
| CLAUDE.md §10 BFF Hygiene placement | ✅ new validator + endpoint extension all in `Services/Ai/PlaybookEmbedding/`; AI-internal; CRUD code does not consume directly |

---

## Sub-agent dispatch notes (E2 path)

This work was executed via a focused sub-agent (`general-purpose`, agent ID `a987dd5bc93d7b190`) with a tight prompt scoped to **code edits only** (no build/test/publish/commit). Agent completed all 6 changes in 39 tool calls / ~6 minutes without stalling — strong contrast with the previous task 032 dispatch that stalled at 33 tool calls trying to also do build/test.

The lesson: split FULL-rigor BFF tasks into "code edits" (sub-agent) + "verify + ship" (main session). The previous failure mode was the agent burning tool calls on iterative `dotnet build` / `dotnet test` loops; pre-allocating those to main session avoids the trap.

Updated CLAUDE.md §10 footnote candidate (defer until project closeout): "Sub-agent dispatch pattern for FULL-rigor BFF work: code-only prompt + main session does verify/ship."

---

## Sibling-task status updates

- Task 032: was ✅ in TASK-INDEX (with original publish-delta evidence). Now amended to ✅ + "loader gap closed" with cross-link to this evidence note
- Task 036: 🔲 → ✅ with this evidence
- Task 033 / 035: remain 🟡 PARTIAL-BLOCKED (Power Apps source-layout direction needed; not affected by this bundle)
- Task 034 (drift job): unchanged 🔲; doable autonomously when scheduled

---

## Related artifacts

- `notes/handoffs/032-loader-gap-and-036-bundling.md` — gap analysis that scoped this work
- `notes/handoffs/032-bff-publish-delta.md` — original task 032 evidence
- `notes/handoffs/wave-2-c-scope-assessment.md` — block-doc with retraction header
- `architecture/jpsmatchingmetadata-schema.json` — JSON schema consumed by validator
