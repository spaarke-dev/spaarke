# Assistant Behavior-Gap Register — R4 (P5)

> **Purpose (spec FR-12)**: a lightweight *standing* loop for Assistant behavior gaps — NOT a one-time audit and NOT a system surface. Each row captures the exact user turn, what the Assistant did, what was expected, the surface (Action/Binding/tool), and a triage destination.
> **Triage destinations**: `preference` (per-user → My Assistant / memory) · `systemic` (everyone → CX/product-owner catalog-authoring exercise, per owner Q4) · `defect` (crash/clip/dead-end → normal bug/defer track).
> **Process**: Capture (UAT / in-conversation thumbs-down / observed dead-end) → Triage → Author + **eval case** → Measure. Every AI-behavior change lands with an eval so the gap can't silently regress.

---

## Register

| # | Date | User turn | Assistant did | Expected | Surface | Triage | Status / FR |
|---|---|---|---|---|---|---|---|
| P1 | 2026-08-10 | "what do I need to do today" | Opened the Task widget, emitted thin "I opened your task list" — no summary | A grounded summary (real counts/top items, cited) + a recommendation, then open Tasks | `list-tasks` Action (`allowstools=false`, ack-only output) | systemic | **R4 E1 — FR-01/02/03** |
| P2 | 2026-08-10 | follow-on chip "Help me prioritize my tasks" | Asked the user for their user ID (already known via OBO) and dead-ended | Prioritize the user's own tasks over OBO without asking for identity; or don't offer the chip if unbacked | free-string `SprkChatSuggestions` + user-scoped tool descriptions | systemic | **R4 E2 — FR-04/05 ✅** (grounded proposer 021a, typed client render 021b, OBO wording 020; **FR-10 guards**: golden case AR4-003 + fact `SuggestFollowupsAction_IsGroundedTypedTwoKindProposer_NoDeadEndFreeString` (024) + `AssistantSuggestionServiceTests` off-catalog drop (021a) + client suggestion suites (021b)) |
| P3 | 2026-08-13 | (meta) "how does the Assistant learn, per-user and system-wide?" | Memory + "My Assistant" + thumbs exist but never form a loop | An explicit standing directive ("do this every time") persists + biases behavior within bounds | memory / feedback / preference-producer | systemic + preference | **R4 E3 — FR-07/08/09** |
| P4 | 2026-08-13 | (open a document → "Open in Compose") | Assistant transcript clips mid-row with dead whitespace in the Xrm-dialog iframe host | Bounded transcript, internal scroll, composer pinned — host-proof | `ConversationPane → SprkChat` flex chain (D9) | defect | **R4 E4 — FR-11** |

---

## Notes

- P1–P4 are the first four records; the register is fed continuously by operator UAT + the in-product feedback subsystem (thumbs/comments) going forward.
- The **operator promotion queue is intentionally NOT a system feature** (owner Q4, 2026-08-13) — recurring `systemic` items are promoted into catalog authoring as a CX/product-owner exercise, reviewing this register + the `feedback` aggregates.
- `preference`-triaged items feed the E3 governed narrow-allow-list producer (FR-09) — bounded, injection-defense preserved.
- **E2 FR-04/FR-06 coverage map (FR-10, task 024)**: the E2 regression guards were authored WITH their features (ADR-038-preferred) — 021a `AssistantSuggestionServiceTests` (service off-catalog drop / typed kinds / cadence / scope), 021b SprkChat suggestion suites (client untyped/unbacked drop), 023 `agendaFollowOnCards.test.tsx` (FR-06 card open-tab gating). Task 024 added the ONE guard the golden-utterance gate genuinely owed — the FR-04 no-dead-end **contract** anchor `SuggestFollowupsAction_IsGroundedTypedTwoKindProposer_NoDeadEndFreeString` — and did NOT duplicate the existing guards (duplicate coverage is build-class, deleted at `/test-diet`). Full map: `tests/integration/contract/Eval/README.md` "E2 (task 024) FR-04 / FR-06 coverage map". **FR-06 is pure client UX** — no BFF/golden-utterance-gate home by design.
- **Deferred (task 024)**: no end-to-end contract test asserts the `ChatEndpoints` SSE `suggestions` event is emitted in the TYPED shape (vs the retired free-string generator) — that path needs the live-agent streaming harness the deterministic eval suite avoids; the typed shape is guarded structurally at the Action-contract + service + client-parse layers instead. Tracked in `notes/defer-issues.md`.

---

## Eval-case harness convention (FR-10 infra — task 001, 2026-08-15)

Every behavior change that closes a register row above MUST land with a golden-utterance eval case (ADR-039 "MUST cover every catalog/prompt change with the golden-utterance eval suite"; ADR-038 `tests/integration/contract/**` is a KEEP path; spec FR-10). This section is the pointer so FR-01/04/06/09 tasks don't have to re-derive the convention.

**Where the suite lives**: `tests/integration/contract/Eval/` (confirmed current shape, 2026-08-15 — grepped, not assumed):
- `golden-utterances.json` — the SHARED, cross-project seed file (59+ cases) owned by the ai-architecture-redesign project. BAs may add a case here with **no code change** IF the case's `expected.consumerType` is either `existing` (already a `ConsumerTypes.All` C# constant) or `planned` (cites the introducing FR, must NOT already exist in `ConsumerTypes.All`). `list-tasks` (the R4 FR-01 target) is **neither** — it is a live catalog row that is NOT a `ConsumerTypes` constant (grepped `ConsumerTypes.cs`: no match) — so it cannot honestly be expressed in golden-utterances.json's two-value vocabulary. This is exactly the fork condition the R1 project hit too.
- **Per-project net-new eval families** — the established pattern (precedent: `assistant-r1-eval-cases.json` + `AssistantEnhancementsR1EvalTests.cs`, task 051, R1) for a project whose cases don't fit the shared schema/vocabulary: (1) a dedicated `{project}-eval-cases.json` seed file with its own `schemaVersion` and closed vocabularies, (2) a dedicated `{Project}EvalTests.cs` harness carrying `[Trait("Category", "GoldenUtteranceEval")]` — that trait IS the merge-gate registration (`eval-gate` job in `sdap-ci.yml`, `--filter "Category=GoldenUtteranceEval"`, no `continue-on-error`) — **zero CI-YAML change needed**. The seed JSON is auto-copied to test output via the existing wildcard `Content Include="..\..\integration\contract\Eval\**\*.json"` glob in `Sprk.Bff.Api.Tests.csproj` (confirmed 2026-08-15 — no csproj edit needed for a new JSON file).
- **R4's family**: `assistant-r4-eval-cases.json` (created by task 001, `schemaVersion: "assistant-r4-eval@v1"`) — seeded with ONE template case, `AR4-001`, for the FR-01 "what do I need to do today" grounded-summary-and-recommendation expectation (closes register row **P1**). It deliberately has **no harness `.cs` yet** — task 001 is STANDARD rigor (docs + scaffolding only; the negative acceptance criterion is "no `.cs`/`.ts`/`.tsx` edited"). The **FR-01 implementation task** is the one that (a) upgrades `list-tasks` to `output_determinism: advisory` + the bounded grounded-tool allow-list, and (b) authors `AssistantEnhancementsR4EvalTests.cs` (mirroring `AssistantEnhancementsR1EvalTests.cs`'s inventory-integrity + catalog-grounding + not-vacuous pattern) that loads `assistant-r4-eval-cases.json` and joins the merge gate. FR-04/06/09 tasks add their cases to the SAME `assistant-r4-eval-cases.json` file + extend the SAME harness (one family per project, not one per FR) — do not fork a second R4 harness.

**Add-a-case convention (copy `AR4-001` in `assistant-r4-eval-cases.json`)**:
1. Give the case a fresh `AR4-###` id (never reuse; ids anchor CI diffs).
2. `family` — group by capability/behavior (e.g. `task-agenda-advisory`, `no-dead-end-suggestion`, `capability-backed-followon`, `preference-hint`); introduce a new family per FR as needed.
3. `ucId` — trace to a canonical §3 UC id (R4 reuses `UC-H-1` task-orchestration and `UC-B-6` conversational-creation, per the R1 precedent).
4. `expected.consumerType` + `expected.catalogStatus` — ground honestly against the REAL catalog: `existing` (ConsumerTypes constant) | `mirrored` (row in `infra/dataverse/sprk_playbookconsumer-rows.json`) | `live-catalog` (seeded on spaarkedev1, constant/mirror parity pending) — cite `seededBy`. Never invent a capability name.
5. `notes` — state the concrete acceptance behavior the case guards (what regresses if this case goes red) — mirrors this file's own P1–P4 "Expected" column discipline.
6. The row above this case in the register (P1–P4, or a new row this project's ongoing UAT/feedback loop appends) is the case's paper trail — cite the register row id in `notes` so the eval case and the gap it closes stay linked both directions.

**Discoverability**: cross-referenced from `projects/spaarkeai-assistant-enhancements-r4/CLAUDE.md` (Deferrals & Issues section) and from `tests/integration/contract/Eval/README.md`.
