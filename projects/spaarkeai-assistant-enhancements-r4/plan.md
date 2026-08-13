# Implementation Plan — spaarkeai-assistant-enhancements-r4

> **Source**: `spec.md` (12 FR / 9 NFR / 3 ADR tensions) · owner interview 2026-08-13 · 3-agent code trace
> **Generated**: 2026-08-13 by project-pipeline (17 tasks / 6 phases)
> **Execution**: **owner-gated — NOT auto-started.**

---

## Architecture Context

R4 extends the **existing** ADR-039 grounded-execution stack — no new dispatch mechanism, no new store, no new executor. The one architectural addition is a **per-Action bounded grounded-tool opt-in** (mirroring the shipped `sprk_allowsknowledge` field) that lets the deterministic pre-filter narrow the tool set for an `advisory`-mode Action. The single Text-path agent turn remains the sole probabilistic decider.

### Discovered Resources (Step 2)

**ADRs**
- **ADR-039** — grounded execution, closed catalogs; the **2026-07-25 `fact`/`advisory` amendment** is the foundation of the grounded-recommend tier. One decider; pre-filter is the only sanctioned aid.
- **ADR-042** — memory two-scope governance; hard-governance deferred to #616 (trustLevel inert). Memory changes owned in R4 (redesign-r2 closed).
- **ADR-013 / BFF §10** — `Services/Ai/PublicContracts/` facade; no AI-internal types in CRUD code.
- **ADR-016** — reasoning tier / budgets (advisory capability runs on the Reasoning tier, temp ~0.2–0.3).
- **ADR-015** — data governance / Tier-3 storage (memory + feedback containers).
- **ADR-047** — notification spine kept distinct from the reactive card surface.
- **ADR-021 / ADR-012** — Fluent v9 tokens-only styling; host-agnostic shared components (E4).
- **ADR-028** — OBO auth (all grounded reads over the caller's token).
- **ADR-038** — integration-heavy testing; eval-case obligation.

**Canonical implementations to reuse (from code trace)**
- Per-Action capability opt-in: `sprk_allowsknowledge` (`AnalysisActionService` `$select` ~:44/126/186 + `ActionRunner.cs:141`).
- Grounded tools E1 chains: `GridOverviewHandler` (`spaarke.grid_overview`) + `DailyBriefingOverviewHandler` (`spaarke.daily_briefing_overview`) — Chat-only, OBO, cited; seed rows `infra/dataverse/sprk_analysistool-*-row.json` (D-4 byte-equal parity test).
- Capability-backed chips (already-backed): `ConsumerChips` + server `sprk_chiptransitions` → `bindingId` (`SessionDispatchOrchestrator.BuildTransitionChips`).
- Free-string suggestions (the P2 risk to gate): `SprkChatSuggestions.tsx` + `SprkChat.tsx handleSuggestionSelect` ~:1583.
- Surface launch: `surfaceLaunchRegistry` `list-tasks` workspace-tab entry (:147) + `handleSurfaceLaunch`.
- Memory: `MemoryFactType.cs` (:12–33, 4 members), `MemoryWriteHandler.cs` (wire map :97–104), `ContextBinder.cs` userFragment (:553–583), `PublicContracts/MemoryItem.cs` v1, `IComposeMemoryCapture.cs` (write-facade pattern).
- Feedback (one-way sink to wire): `FeedbackService.cs` + `feedback` Cosmos container.
- D9 suspects: `SprkChat.tsx:1432` (trailing-spacer inline px), `inputZone` (~:208–212 / 2884), `ConversationPaneChrome.tsx:50` (`sprkChatFlex`); partial fix (`messageList` `min-height:0`) already on master.

**Skills**: `task-execute`, `adr-check`, `code-review`, `jps-action-create` / `jps-validate` (advisory Action authoring), `code-page-deploy` + `bff-deploy` (E4 / deploy), `ui-test` (E4), `test-diet` (wrap-up).

---

## Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- advisory capability, per-Action opt-in, pre-filter scope, preference-producer, preference fact type -->
  <spaarkeai>Y</spaarkeai>    <!-- SprkChatSuggestions gating, follow-on cards, ConversationPane, D9 flex chain -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (BFF=Y)**: all new BFF surface extends the existing ADR-039 projection + Action catalog. The advisory capability reuses the two shipped grounded tools over OBO; the one new per-Action field mirrors `sprk_allowsknowledge`; the `Preference` fact type + producer are owned in R4 (redesign-r2 closed), bound to the published `MemoryItem` v1 contract. Publish ≤60 MB per BFF task.

---

## Phase Breakdown (WBS)

### Phase 0 — Foundation (P5 + eval harness)
- **001** — Behavior-gap register + eval-case harness scaffolding (FR-12, FR-10 infra); seed P1–P4 records.

### Phase 1 — E1 grounded-recommend tier (fixes P1)
- **010** — Per-Action bounded grounded-tool opt-in field (FR-03) — mirror `sprk_allowsknowledge`.
- **011** — Advisory pre-filter bounded-tool allow-list scoping (FR-02) — narrow the tool set for the advisory turn; no second decider.
- **012** — Author the advisory task-agenda Action + Binding (FR-01) — upgrade `list-tasks` to advisory (Reasoning tier, temp ~0.2–0.3); chip transitions; seed.
- **013** — E1 eval cases (FR-10) — golden utterances for "what do I need to do today".

### Phase 2 — E2 follow-ons (fixes P2)
- **020** — OBO-identity wording (FR-05 / QW1) — `GridOverviewHandler` + byte-equal seed rows + other user-scoped tools.
- **021** — Capability-backed suggestion gating (FR-04) — gate/replace free-string `SprkChatSuggestions`.
- **022** — Daily Briefing + Smart To Do `surfaceLaunchRegistry` entries (FR-06 launch targets).
- **023** — Follow-on cards gated on not-already-open (FR-06) — `ConversationPane` transcriptFooter + `useConsumerChips`.
- **024** — E2 eval cases (FR-10) — dead-end-suppression + card gating.

### Phase 3 — E3 feedback→memory loop (fixes P3)
- **030** — `Preference` MemoryFactType + `MemoryWriteHandler` wire map (FR-07).
- **031** — Feedback→memory pipeline (FR-08) — thumbs-down/comment / "do this every time" → governed `preference` item.
- **032** — Governed narrow-allow-list preference-producer (FR-09) — pre-turn tool hint; injection-defense preserved.
- **033** — E3 eval cases + preference-producer bounds unit tests (FR-09/10).

### Phase 4 — E4 D9 viewport fix (fixes P4)
- **040** — D9 live-DOM diagnosis + host-proof flex-chain fix (FR-11) — client-only; `<ui-tests>` per handoff §6.

### Deploy + Wrap-up
- **080** — Deploy + verify (BFF redeploy + `sprk_spaarkeai` rebuild + publish-size + UAT).
- **090** — Project wrap-up (`/test-diet` gate).

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Foundation | 001 | — | Register + eval harness (new files). `parallel-safe:true`. |
| Wave 1 — E1 spine | 010 → 011 → 012 → 013 | 001 | `Services/Ai` catalog/pre-filter spine → **sequential** (`parallel-safe:false`); 013 = eval (safe). |
| Wave 1 — independent | 020, 022, 030, 040 | — | Run alongside E1: 020 = OBO wording (BFF, disjoint file), 022 = registry entries (client, additive), 030 = memory enum (BFF Memory), 040 = D9 (client). BFF ones `parallel-safe:false` (coordinate); dispatch as slots free. |
| Wave 2 — E2 client | 021 → 023 → 024 | 012, 022 | `SprkChat`/`ConversationPane` spine → **sequential** (`parallel-safe:false`); 024 = eval (safe). |
| Wave 3 — E3 | 031, 032 → 033 | 030 | 031 feedback→memory, 032 producer (`ContextBinder`, injection-defense) — coordinate BFF; 033 = eval/tests (safe). |
| Deploy | 080 | all code | Owner-gated; BFF + `sprk_spaarkeai` together. |
| Wrap-up | 090 | 080 | `/test-diet` gate. |

**Concurrency cap**: 6 agents/wave. **Build verification between waves** (mandatory): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build:prod` for PCF, `npm run build` for touched shared/SpaarkeAi packages.

---

## Critical Path

`001 → 010 → 011 → 012 → 013` (E1 grounded-recommend DoD) is the value-proving spine. E2 depends on 012 (so "prioritize" maps to the advisory capability) + 022. E3 (030→032) is independent of E1/E2. E4 (040) is fully independent.

Longest chain: **001 → 010 → 011 → 012 → 021 → 023 → 024 → 080 → 090** (≈9 links).

---

## High-Risk Items

- **011** — the ADR-039 pre-filter boundary: the bounded allow-list MUST be deterministic pre-filtering, never a second decider. opus/xhigh.
- **032** — the injection-defense boundary: a preference may hint/trigger an allow-listed capability but never grant a capability or alter a fact; the guillemet DATA-guard + ADR-039 preference-only rule must hold. opus/xhigh.
- **012** — advisory Action authoring: `output_determinism: advisory`, Reasoning tier, every narrated fact cited to a tool result (no fabrication).
- **030/031** — memory governance: R4 owns the changes but MUST preserve ADR-042 deferred hard-governance (#616; trustLevel inert).
- **040** — D9 may already be fixed by the merged partial fix; confirm in live DOM before editing; keep host-proof (no measured heights).

---

## Coordination (hot-path — see `projects/INDEX.md`)

- `/conflict-check` before **every** BFF / `ConversationPane` / `SprkChat` PR.
- Live overlap: **compose-r5/r6** (ConversationPane/SprkChat/D9), **assistant-r3** (SprkChatAgentFactory/AgentToolProjection). Memory files have no live contender (redesign-r2 closed).
- Bind CRUD-side memory consumers to `PublicContracts/MemoryItem.cs` v1; keep AI-internal types out of CRUD code (ADR-013).
- Keep the reactive card surface distinct from the ADR-047 spine (notification-spine-r1).
- Deploy BFF + `sprk_spaarkeai` together for any turn that touches both.
