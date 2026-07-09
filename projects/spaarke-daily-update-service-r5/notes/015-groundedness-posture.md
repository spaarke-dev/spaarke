# Task 015 — Groundedness posture: confirm-and-lock (FR-A6)

> **Date**: 2026-07-09 · confirm-and-lock (no production behavior change)

## Operator ruling being locked (2026-07-08)

Existence is **not** probabilistic. The Daily Briefing is accurate **by construction** — deterministic item rows (011), deterministic-fact TL;DR (013), binary anchor resolution (014). Therefore **no briefing code path may warn or withhold user-facing content based on a groundedness score.** `GroundednessCheckService` is retained as a **Chat-safety + eval/telemetry signal only** — it is NOT deleted.

## Grep evidence — GroundednessCheckService is Chat/Safety-scoped, zero in DailyBriefing*

`grep -rn "GroundednessCheckService" src/` — every reference is Chat-safety, the service itself, its DI registration, or chat widgets:

| Reference site | Role |
|---|---|
| `Services/Ai/Chat/Middleware/SafetyPipelineMiddleware.cs` | The **only** runtime consumer (chat safety pipeline) |
| `Services/Ai/Safety/GroundednessCheckService.cs` + `IGroundednessCheckService.cs` | The service + interface |
| `Services/Ai/Safety/ConfidenceScoringRequest.cs`, `ContentSafetyAuthHandler.cs`, `Citations/CitationSafetyCheck.cs` | Sibling chat-safety types (doc-comment references) |
| `Infrastructure/DI/AiSafetyModule.cs` | Scoped DI registration (AIPU2-021) |
| `client/…/Spaarke.AI.Widgets/…/SafetyAnnotationOverlay.tsx`, `GroundednessHighlight.tsx` | Chat-widget rendering of the groundedness payload |

**Zero references in any `DailyBriefing*` file** (narrator, composite, collector, DTOs) — the guardrail was already intact; this task locks it.

## Enforcement (the lock)

New architecture guardrail: `tests/Spaarke.ArchTests/DailyBriefingGroundednessGuardrailTests.cs` (NetArchTest — the ArchTests project is the architecturally-correct home for a boundary rule; the POML suggested the unit-project path, adapted per directional step mode):

- **`DailyBriefing* types must not depend on GroundednessCheckService`** — `Types.InAssembly(Program).That().HaveNameStartingWith("DailyBriefing").ShouldNot().HaveDependencyOnAny(IGroundednessCheckService, GroundednessCheckService)`.
- **`the Narrators namespace must not depend on GroundednessCheckService`** — same rule scoped to `Sprk.Bff.Api.Services.Ai.Narrators` (catches briefing types that don't start with "DailyBriefing", e.g. DTOs).

Both **pass** today (2/2). They fail the moment a briefing type acquires the dependency — the mechanical shape any score-gate would require. This makes the operator ruling an enforced boundary, not a convention.

## Verification

- Guardrail tests: **2/2 pass**. ✅
- `GroundednessCheckService` NOT deleted (retained for chat + eval/telemetry). ✅
- **No production code changed** → BFF publish size **unchanged**; no `<PackageReference>` change → no new HIGH CVE. ✅
- Escalation trigger (guardrail already violated) did **not** fire — grep found zero briefing-path references.

## Placement (§10/§11)

Test-only addition to the existing `Spaarke.ArchTests` project. No new production component, service, endpoint, package, or DI registration.
