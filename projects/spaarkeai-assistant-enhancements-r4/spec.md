# spaarkeai-assistant-enhancements-r4 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-13
> **Source**: `design.md` (R4 — from deterministic launcher to grounded proactive assistant); owner interview 2026-08-13; 3-agent code trace
> **Predecessor**: spaarkeai-assistant-enhancements-r3 (shipped + deployed to dev 2026-08-11)

---

## Executive Summary

R4 turns the Assistant from a deterministic launcher into a **grounded proactive assistant** and closes the learning loop. It introduces a **grounded-recommend capability tier** — authored on the *existing* single agent-turn decider using ADR-039's `advisory` output mode + a per-Action bounded grounded-tool allow-list (mirroring `sprk_allowsknowledge`; enforced by the sanctioned deterministic pre-filter, **no new dispatcher**). The tier powers a **task-agenda capability** that answers "what do I need to do today" with a *grounded* summary + recommendation from already-shipped tools (`spaarke.grid_overview` My-Tasks + `spaarke.daily_briefing_overview`, both OBO, both cited) and opens Tasks (E1). Follow-on suggestions become **capability-backed** so they never promise unwired actions, and user-scoped tools stop asking for an identity OBO already knows (E2). A **feedback→memory loop** adds a `preference` fact type + a governed narrow-allow-list preference-producer so standing directives ("always summarize my tasks") bias behavior within injection-defense bounds (E3). A client-only flex-chain fix finishes the "Open in Compose" viewport clip (E4).

**Root cause R4 addresses**: R3's capabilities are authored at the extreme-deterministic end (`allowstools=false` acknowledgement tier), so the model cannot reason/chain/recommend even over grounded data. The owner-approved fix (2026-08-13) is a grounded-recommend tier — *"get full advantage of what an LLM provides… BUT within the limits of not making stuff up."* Hard limits only: (1) the capability catalog stays closed; (2) every fact is tool-grounded.

---

## Scope

### In Scope
- **E1 — grounded-recommend tier + task-agenda capability**: `advisory`-mode Action + Binding on the Text-path; a per-Action bounded grounded-tool allow-list (opt-in mirroring `sprk_allowsknowledge`) enforced via the deterministic pre-filter; the capability chains `spaarke.grid_overview` (My Tasks configId, OBO `today`) + `spaarke.daily_briefing_overview`, narrates a grounded, cited summary + a recommendation, and launches Tasks.
- **E2 — capability-backed follow-ons + OBO wording**: suppress/replace free-string `SprkChatSuggestions` that no wired capability backs; extend the R3 deterministic-from-registration chip discipline to *query* chips; add capability-backed follow-on cards to open Daily Briefing + Smart To Do (single-surface registry entries, gated on not-already-open); add the OBO-identity assertion to every user-scoped tool description (QW1).
- **E3 — feedback→memory loop**: new `preference` MemoryFactType (**owned directly in R4** — redesign-r2 closed); a feedback→memory pipeline (thumbs-down+comment / explicit "do this every time" → governed `preference` item); a governed **narrow-allow-list** preference-producer (named directives → pre-turn tool hints only); an eval-case guardrail on every behavior change.
- **E4 — D9 viewport fix**: client-only flex-chain correction in the `ConversationPane → SprkChat` subtree; ships with `sprk_spaarkeai` rebuild + `Deploy-SpaarkeAi.ps1`.
- **P5 — behavior-gap process**: a lightweight standing *project-artifact* register (markdown), NOT a system surface; capture → triage-to-destination → author+eval → measure. P1–P4 are its first four records.

### Out of Scope
- **Operator promotion queue / operator review UI** — handled as a CX/product-owner exercise (owner Q4). No in-system promotion surface; feedback aggregates stay API-only.
- **Multi-surface launch fan-out** — Briefing/Smart To Do open as individual follow-on cards, not a one-launch fan-out; no `surfaceLaunchRegistry` shape change.
- **A new grounded-recommend executor / second dispatch surface** (owner Q1 — reuse the one decider).
- **Broad free-text preference steering** (owner Q2 — narrow allow-list only).
- Free-roaming agent outside the closed catalog; auto-mutation of the global catalog (stays HITL); memory trust/provenance enforcement (deferred to security project #616); net-new grounded tools beyond what E1 chains (the two E1 tools already exist).

### Affected Areas (file:line, from code trace)
- `infra/dataverse/actions/list-tasks.action.json` — upgrade/author the advisory task-agenda Action (or a sibling), replacing the ack-only `allowstools=false` framing.
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` (`$select` ~:44/126/186; `AnalysisAction` materialization ~:502) — add the per-Action bounded-tool opt-in mirroring `sprk_allowsknowledge`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AgentToolCatalogProjector.cs` (~:74–433) + `AgentToolProjection.cs` `PreFilter` (~:123–187) — scope the bounded grounded-tool allow-list for the advisory capability's turn.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GridOverviewHandler.cs` + `DailyBriefingOverviewHandler.cs` — OBO-identity wording in the handler `Metadata.Description` + byte-equal seed rows (`infra/dataverse/sprk_analysistool-*-row.json`; D-4 parity test guards).
- `infra/dataverse/sprk_playbookconsumer-rows.json` + `scripts/dataverse/Seed-PlaybookConsumers.ps1` — the task-agenda Binding row (+ `chipTransitions` for capability-backed follow-ons).
- `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts` (~:84) — add Daily Briefing + Smart To Do single-surface entries (`kind:'workspace-tab'`, `list-tasks` precedent :147).
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatSuggestions.tsx` + `SprkChat.tsx` (`handleSuggestionSelect` ~:1583) — gate free-string suggestions on a backing capability.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (`transcriptFooter` ~:2889–2910; `handleSurfaceLaunch` ~:1245–1312) + `useConsumerChips.tsx` — follow-on cards for Briefing/Smart To Do.
- **E3 (owned in R4 — redesign-r2 closed)**: `Services/Ai/Memory/MemoryFactType.cs` (:12–33, add `Preference`); `Handlers/MemoryWriteHandler.cs` (wire map :97–104); `Services/Ai/Feedback/*` (feedback→memory pipeline); `Services/Ai/Context/ContextBinder.cs` (userFragment :553–583) + a governed preference-producer seam; bind to `Services/Ai/PublicContracts/MemoryItem.cs` v1; keep the AI-facade discipline (ADR-013 — no AI-internal types into CRUD code).
- **E4**: `SprkChat.tsx:1432` (trailing-spacer inline px), `SprkChat.tsx` `inputZone` (~:208–212 / applied 2884), `ConversationPaneChrome.tsx:50` (`sprkChatFlex`) — the D9 suspects.

---

## Requirements

### Functional Requirements

**E1 — Proactive grounded assistance (fixes P1)**

1. **FR-01 (task-agenda advisory capability)**: for the "what do I need to do today"/task-agenda intent, the Assistant calls the allow-listed grounded tools (`spaarke.grid_overview` My-Tasks configId with OBO `today` + `spaarke.daily_briefing_overview`), reasons over **only** the returned data, narrates a grounded summary (counts + top items) **with record-id citations**, offers a recommendation, and opens the Tasks surface. — **Acceptance**: "what do I need to do today" → a grounded summary (real counts/top items, each cited), a recommendation ("I'd start with the 2 due today"), Tasks tab opens, **no** fabricated data, **no** thin ack, **no** duplicate tab.
2. **FR-02 (advisory mode + bounded allow-list on the one decider)**: the capability is authored with `output_determinism: advisory` (catalog data) and a **per-Action bounded grounded-tool allow-list**; the allow-list is enforced by the **deterministic pre-filter** (context scoping), and the single Text-path agent turn remains the sole decider. The Action runs on the **ADR-016 Reasoning tier at temperature ~0.2–0.3** (advisory-mode default — enough latitude to prioritize/recommend, low enough that the grounded summary stays faithful). — **Acceptance**: the Action row carries `advisory`; only the allow-listed grounded tools mount for that capability's turn; **no** second intent-detection/classifier/dispatch surface is added (verified against ADR-039 MUST-NOTs).
3. **FR-03 (per-Action bounded-tool opt-in field)**: introduce the per-Action capability opt-in **mirroring `sprk_allowsknowledge`** — read in `AnalysisActionService` `$select`, materialized on `AnalysisAction`, and honored on the agent path. — **Acceptance**: an Action without the opt-in cannot mount grounded tools (stays ack-tier); an Action with it mounts exactly its declared allow-list.

**E2 — Follow-ons that actually work (fixes P2)**

4. **FR-04 (no dead-end suggestions)**: a follow-on suggestion renders **only** when a wired Action/tool/Binding fulfills it. Free-string `SprkChatSuggestions` with no backing capability are suppressed or replaced with `bindingId`-carrying capability chips. — **Acceptance**: "Help me prioritize my tasks" either maps to the FR-01 advisory capability (and works) or does not render; no rendered follow-on maps to an unwired action.

   > **APPROACH CHANGE (2026-08-17, owner-approved — `notes/021-grounded-suggestions-design-delta.md`)**: the original "gate the free strings client-side" approach hit the ADR-039 escalation boundary — a plain SSE free-string carries no structural backing signal, so backed-vs-unbacked is not distinguishable client-side without a banned keyword heuristic. FR-04 is instead delivered by **consolidating onto the grounded proposer** (`AssistantSuggestionService` already selects over the real context-scoped catalog and drops hallucinated ids): retire the ungrounded generator; run **one predictable grounded pass** per turn that emits a **typed two-kind** structure — **capability** chips (carry a model-selected real `targetBindingId` → guaranteed dispatch) and **question** chips (text → re-enter the grounded loop, safe by construction). The LLM keeps full contextual intelligence (it selects + phrases from the real menu; it never authors a capability's routing target). Delivered as **task 021a (BFF)** + **task 021b (client)**, superseding task 021. This is ADR-039 Path C (comply — the grounded-proposer pattern is already sanctioned; no new decider/store).
5. **FR-05 (OBO-identity wording — QW1)**: every user-scoped tool description asserts *"returns the calling user's own records over OBO; never ask the user for their identity."* Applied to `GridOverviewHandler` + its byte-equal seed row (D-4 parity test) + other user-scoped tools. — **Acceptance**: the today/prioritize flows never ask for the user's id or name.
6. **FR-06 (capability-backed Briefing / Smart To Do cards)**: after the FR-01 answer, capability-backed follow-on **cards** to open Daily Briefing and Smart To Do render **only if that tab is not already open** (reusing R3 open-tab awareness); each dispatches a single-surface launch via new `surfaceLaunchRegistry` entries (`kind:'workspace-tab'`). — **Acceptance**: with Briefing already open, its card is suppressed; with it closed, the card appears and clicking opens the Briefing tab; same for Smart To Do; no duplicate tabs.

**E3 — Learning / feedback loop (fixes P3)**

7. **FR-07 (`preference` fact type)**: add a `Preference` `MemoryFactType` (+ `MemoryWriteHandler` wire map), **owned directly in R4** (redesign-r2 is closed — see Owner Clarifications), binding to the `PublicContracts/MemoryItem.cs` v1 contract; ADR-042's deferred hard-governance stays deferred to #616 (trustLevel carried inert). — **Acceptance**: a preference memory item can be written (User scope) and is recalled into `userFragment` each turn.
8. **FR-08 (feedback→memory pipeline)**: a thumbs-down+comment or an explicit "do this every time" directive writes a **governed** `preference` memory item (correct `MemoryOrigin` / `ConfirmedByUser` semantics). — **Acceptance**: an explicit standing directive from the user persists as a `preference` item tied to that user.
9. **FR-09 (governed narrow-allow-list preference-producer)**: a **closed** allow-list of named standing directives (e.g. "always summarize my tasks", "always open my briefing") maps **only to pre-turn tool hints**; a preference may bias or trigger an allow-listed grounded capability, **never** grant a capability or alter a fact; injection-defense (the guillemet DATA-guard, ADR-039 preference-only) preserved. — **Acceptance**: "always summarize my tasks" biases the FR-01 capability's default behavior; an off-allow-list directive has **no** tool-selection effect; the stated profile still never feeds `AgentToolFilterContext` except through the sanctioned bounded hint.
10. **FR-10 (eval-case guardrail)**: every new/changed AI behavior lands with an eval case (ADR-039 golden-utterance suite + maker-guide obligation). — **Acceptance**: FR-01/04/06/09 each ship with an eval case that fails if the behavior regresses.

**E4 — D9 viewport clip (fixes P4)**

11. **FR-11 (host-proof flex-chain fix)**: one live-DOM session confirms whether D9 still reproduces after the partial fix already on master (`messageList` `min-height:0`); finish the remaining suspects — the `SprkChat.tsx:1432` trailing-spacer inline px height (replace measured height with the flex chain), the `inputZone` missing `flexShrink:0`, the `sprkChatFlex` wrapper — with **no fixed/measured heights** in the chain (tokens-only, ADR-021). Client-only; `sprk_spaarkeai` rebuild + `Deploy-SpaarkeAi.ps1`. — **Acceptance**: handoff §6 checklist passes (modal "Open in Compose" + full-page + widget + dialog resize + long/empty conversation, light + dark) with no clipped rows and no dead whitespace.

**P5 — behavior-gap process**

12. **FR-12 (behavior-gap register)**: a lightweight standing markdown register (project artifact, like R3's `defer-issues.md`) captures behavior gaps with {user turn, Assistant behavior, expectation, surface} and a triage destination (per-user preference / systemic-gap→CX exercise / defect). NOT a system surface. — **Acceptance**: P1–P4 are recorded; the register + triage columns exist and are referenced by the project.

### Non-Functional Requirements
- **NFR-01 (BFF publish size)**: every BFF-touching task verifies compressed publish size; ≤60 MB ceiling, ~49.63 MB baseline; report absolute + delta; ≥+5 MB single-task delta needs justification. (CLAUDE.md §10.)
- **NFR-02 (no new HIGH CVE)**: `dotnet list package --vulnerable --include-transitive` shows no new HIGH.
- **NFR-03 (ADR-039 fidelity — the defining constraint)**: exactly ONE probabilistic decider (the Text-path agent turn); the grounded-recommend tier adds **no** classifier / second intent-detection / routing surface; the bounded tool allow-list is deterministic pre-filtering only; every advisory factual claim is cited.
- **NFR-04 (preference bounds — ADR-039/ADR-042)**: preferences influence tool selection ONLY through the FR-09 narrow allow-list; the stated profile stays advisory (never grants capabilities/alters facts); memory hard-governance remains deferred to #616 (trustLevel carried inert).
- **NFR-05 (memory ownership + governance)**: redesign-r2 is closed, so R4 owns its E3 memory changes directly; still bind to the `PublicContracts/MemoryItem.cs` v1 contract, keep the AI-facade discipline (ADR-013 — no AI-internal types into CRUD code), and hold ADR-042's deferred hard-governance (trustLevel inert, #616) unchanged.
- **NFR-06 (reuse-first / §11)**: reuse the two shipped grounded tools + the one agent-turn decider + the existing chip/registration machinery; no net-new tools, no new executor, no registry fan-out.
- **NFR-07 (reactive ≠ proactive)**: proactive task-agenda suggestions stay reactive/local; the reactive card surface stays distinct from the ADR-047 notification spine (no new push channel).
- **NFR-08 (test obligation)**: PRs modifying `Sprk.Bff.Api/Services/**` add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`; advisory-capability projection + preference-producer bounds unit-tested; eval cases per FR-10.
- **NFR-09 (dual-mount / host-proof)**: the D9 fix is host-proof (no per-host branching); shared SprkChat changes must not regress other SprkChat consumers or the full-page/widget hosts.

---

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution, closed catalogs; **2026-07-25 `fact`/`advisory` amendment**) — the tier's foundation; one decider, advisory-mode reasoning, pre-filter as the only aid.
- **ADR-042** (memory two-scope governance; hard-governance deferred to #616; memory changes owned in R4 now that redesign-r2 is closed).
- **ADR-013 / BFF §10** (`Services/Ai/PublicContracts/` facade — no AI-internal types into CRUD; consume, no fork).
- **ADR-015** (data governance / Tier-3 storage — memory + feedback containers).
- **ADR-047** (notification spine kept distinct from the reactive card surface).
- **ADR-021 / ADR-012** (Fluent v9 tokens-only styling; host-agnostic shared components) — E4.
- **ADR-028** (OBO auth) — all grounded reads over the caller's token.
- **ADR-038** (integration-heavy testing; eval-case obligation).

> **ADR citation correction (for downstream tasks)**: the design draft's §7 attributed the advisory/injection rules to **ADR-015** — that is incorrect. ADR-015 is data-governance/tiers only. The binding advisory/injection rules are in **ADR-039** (the `fact`/`advisory` amendment + the preference-only rule: the stated profile MUST NOT feed `AgentToolFilterContext` or any grounding/tool-projection path) and **ADR-042** (memory two-scope model; hard-governance — untrusted-origin ban, trustLevel enforcement, memory-poisoning evals — DEFERRED to security project #616; `trustLevel` carried inert).

### MUST Rules
- ✅ MUST keep exactly ONE probabilistic decider (Text-path agent turn); MUST NOT add a classifier / second intent-detection / routing surface (ADR-039). The bounded tool allow-list is deterministic pre-filter only.
- ✅ MUST cite every fact/number the advisory capability narrates to a grounded tool result; MUST NOT fabricate.
- ✅ MUST author the per-Action bounded-tool opt-in as catalog data mirroring `sprk_allowsknowledge`; MUST NOT gate tools by hardcoded tool-name lists.
- ✅ MUST render a follow-on suggestion ONLY when a wired capability backs it; MUST NOT emit dead-end promises.
- ✅ MUST keep preference steering within the FR-09 closed allow-list (hints only); MUST NOT let a preference grant a capability or alter a fact.
- ✅ MUST bind CRUD-side memory consumers to `PublicContracts/MemoryItem.cs` v1 and keep AI-internal types out of CRUD code (ADR-013); R4 owns the `Services/Ai/Memory` changes directly (redesign-r2 closed) but MUST preserve ADR-042's deferred hard-governance (trustLevel inert, #616).
- ✅ MUST make the D9 fix host-proof (no fixed/measured heights); MUST NOT reintroduce a measured height.

### Existing Patterns to Follow
- Per-Action capability opt-in: `sprk_allowsknowledge` (`AnalysisActionService` `$select` + `ActionRunner.cs:141`).
- Advisory output mode: ADR-039 2026-07-25 amendment (`output_determinism: advisory`).
- Grounded tools: `GridOverviewHandler` / `DailyBriefingOverviewHandler` (Chat-only, OBO, cited).
- Capability-backed chips: `ConsumerChips` + server `sprk_chiptransitions` → `bindingId` (`SessionDispatchOrchestrator.BuildTransitionChips`).
- Surface launch: `surfaceLaunchRegistry` `list-tasks` workspace-tab entry (:147) + `handleSurfaceLaunch`.
- Memory write facade: `PublicContracts/IComposeMemoryCapture.cs` (the CRUD-safe capture pattern).

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- advisory capability, per-Action opt-in, pre-filter scope, preference-producer, preference fact type -->
  <spaarkeai>Y</spaarkeai>    <!-- SprkChatSuggestions gating, follow-on cards, ConversationPane, D9 flex chain -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (BFF=Y)**: all new BFF surface extends the *existing* ADR-039 projection + Action catalog — no new dispatch mechanism, no new store. The advisory capability reuses the two shipped grounded tools over OBO; the one new per-Action field mirrors `sprk_allowsknowledge`; the preference-producer + `Preference` fact type are owned in R4 (redesign-r2 closed), bound to the published `MemoryItem` v1 contract. Publish ≤60 MB per BFF task. Run `/conflict-check` before every `Services/Ai`/`ConversationPane`/`SprkChat` PR (live overlap remains with active worktrees compose-r5/r6 + assistant-r3 on `ConversationPane`/`SprkChat`/`SprkChatAgentFactory`; the memory files have no live contender now that redesign-r2 is closed).

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Advisory task-agenda capability (Action+Binding) | `list-tasks` (ack-only) Action+Binding | **Yes — upgrade** `list-tasks` to advisory + bounded tools | "what do I need to do today" stays a thin ack with no summary (the P1 defect) |
| Per-Action bounded-tool opt-in field | `sprk_allowsknowledge` (per-Action RAG opt-in) | **Yes — mirror** its shape | Any Action either has zero tools or the whole catalog; no bounded grounded-recommend tier possible |
| `Preference` MemoryFactType | `MemoryFactType` enum (Party/KeyDate/PriorAnalysis/KeyFact) | **Extend** the enum (owned in R4; redesign-r2 closed) | Standing directives land as generic `KeyFact`; no governed preference channel; FR-09 can't gate |
| Governed preference-producer (narrow allow-list) | `StatedProfileReader` (advisory-only, never steers) | **No — new seam** (steering crosses the advisory line by design; bounded) | "always summarize my tasks" can never act automatically (the P3 loop stays open) |
| Feedback→memory pipeline | `FeedbackService` (one-way sink, API-only) | **No — new wiring** (nothing consumes feedback today) | Thumbs/feedback never improves behavior; the loop never closes |
| Daily Briefing / Smart To Do launch entries | `surfaceLaunchRegistry` (`list-tasks` precedent) | **Yes — add 2 single-surface entries** | FR-06 follow-on cards have no launch target |
| Capability-backed suggestion gating | `ConsumerChips` (backed) vs `SprkChatSuggestions` (free strings) | **Yes — gate/replace** free strings | Dead-end promises persist (the P2 defect) |

*Not built (owner)*: operator promotion queue / review UI (CX exercise); multi-surface launch fan-out; new grounded-recommend executor; broad free-text preference steering.

---

## ADR Tensions (per CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039** | closed catalog / one decider / no second dispatch surface | the grounded-recommend tier lets the model chain grounded tools + recommend | **C (comply) for the reasoning + A (project-scoped exception) for the per-Action bounded-tool opt-in** | The 2026-07-25 `advisory` amendment already sanctions grounded reasoning + recommendations; R4 adds NO new decider (reuses the Text-path turn) and expresses the bounded tool set as deterministic pre-filter narrowing (the one sanctioned aid). The only genuinely-new element is the per-Action bounded-tool opt-in field, documented as a scoped exception mirroring `sprk_allowsknowledge`. No amendment required. |
| **ADR-042 / ADR-039** | preferences advisory; never feed `AgentToolFilterContext`; hard-governance deferred to #616 | the FR-09 producer lets an allow-listed directive bias/trigger a bounded grounded capability | **A (project-scoped exception)** | Allow-listed named directives only → pre-turn tool **hints**; never grants a capability or alters a fact; injection-defense guard preserved; owned in R4 (redesign-r2 closed); ADR-042's deferred hard-governance (#616) unchanged. |
| **ADR-047** | notification spine is the push channel | proactive task-agenda suggestions | **C (comply)** | Reactive/local surface kept distinct; no spine change, no push channel. |

---

## Success Criteria
1. [ ] **P1 DoD**: "what do I need to do today" → grounded, cited summary + a recommendation + Tasks opens; no thin ack, no fabricated data, no duplicate tab. — Verify: manual UAT + eval case + projection unit test.
2. [ ] **Advisory fidelity**: the capability mounts only its allow-listed grounded tools; no classifier/second dispatch surface added. — Verify: projection test + ADR-039 adr-check.
3. [ ] **P2 DoD**: no rendered follow-on promises an unwired action; "Help me prioritize my tasks" works or is absent; no flow asks for the user's id. — Verify: manual UAT + suggestion-gating unit test.
4. [ ] **Follow-on cards**: Briefing/Smart To Do cards appear only when their tab is closed and open the right surface. — Verify: manual UAT across open/closed states.
5. [ ] **P3 loop**: an explicit "do this every time" directive persists as a governed `preference` item and biases the FR-01 capability next turn; an off-allow-list directive has no tool-selection effect. — Verify: manual UAT + preference-producer bounds unit test.
6. [ ] **P4/D9 DoD**: handoff §6 checklist passes (modal + full-page + widget + resize + long/empty, light+dark). — Verify: live-DOM UAT session.
7. [ ] **BFF hygiene**: publish ≤60 MB; no new HIGH CVE; no fork of `Services/Ai/` internals. — Verify: publish measurement + CVE scan + PublicContracts review.

---

## Dependencies

### Prerequisites
- R3 shipped: the two grounded tools, open-tab awareness, the `ConsumerChips`/registration machinery, the surface-launch registry, the active-item conduit.
- ADR-039 2026-07-25 advisory amendment (already merged).
- The D9 partial fix already on master (`messageList` `min-height:0`).

### External / Coordination
- **`spaarke-ai-architecture-redesign-r2`** — **closed** (worktree open but not active); R4 owns its E3 memory changes (`MemoryFactType`, `MemoryWriteHandler`, `ContextBinder`, preference-producer) directly. Still bind to the published `PublicContracts/MemoryItem.cs` v1 contract + ADR-042 governance; no live cross-team gating.
- **`spaarkeai-compose-r5/r6`** — shares `ConversationPane`/`ThreePaneShell`/`SprkChat` (D9 origin); still active — merge-order coordination + `/conflict-check` before those PRs.
- **`spaarkeai-assistant-enhancements-r3`** — predecessor; shares `SprkChatAgentFactory`/`ConversationPane`/`AgentToolProjection`; re-base on it and `/conflict-check` those PRs.
- **`spaarke-notification-spine-r1`** — ADR-047 spine kept distinct.
- Azure OpenAI (advisory reasoning tier) + OBO auth path.

---

## Owner Clarifications
| Topic | Question | Answer | Impact |
|---|---|---|---|
| Build approach | New engine, or reuse the existing decider? | **Reuse** (advisory mode + pre-filter bounded tools) | No new executor; ADR-039 Path C/A; lowest blast radius (FR-01/02/03). |
| Preference steering | How far may a preference steer behavior? | **Narrow closed allow-list → tool hints only** | FR-09 bounded producer; never grants capability/alters fact (NFR-04). |
| Agenda surfaces | Which surfaces does the today-capability open? | **Tasks only + inline summary + Briefing/Smart-To-Do follow-on cards if not open** | Single-surface launch (no registry fan-out); FR-06 cards. |
| Operator queue | New surface, or extend reporting? | **Out of system scope — CX/product-owner exercise** | No operator UI/promotion queue; E3 = feedback→memory + preference type + producer only. |
| E3 memory ownership | Coordinate the `Preference` type/producer with redesign-r2, or own it? | **redesign-r2 is closed — all work contained in R4** | R4 owns the memory changes directly (no cross-team gating); E3 is a normal phase, not sequenced-last-and-gated. ADR-042 deferred governance (#616) unchanged; still bind to `MemoryItem` v1. |
| Advisory tier | Reasoning tier/temp for FR-01, or defer? | **ADR-016 Reasoning tier, temp ~0.2–0.3** | Baked into FR-02; enough latitude to prioritize/recommend, faithful grounded summary; every fact cited. |

---

## Assumptions
- **Task-agenda authoring**: R4 upgrades the existing `list-tasks` Action/Binding to the advisory grounded-recommend tier (vs authoring a net-new sibling) — the P1 defect is specifically on that path; final choice is a task-authoring detail under FR-01/03.
- **D9 may already be resolved** by the merged partial fix; FR-11's live-DOM session confirms before further edits — if it no longer reproduces, FR-11 collapses to a verification + regression-guard task.
- **E3 is fully R4-owned**: redesign-r2 being closed, R4 authors the `Preference` type + producer + feedback→memory pipeline directly. The only remaining discipline is contract-binding (`MemoryItem` v1) + ADR-042 deferred-governance preservation — not cross-team gating.

## Unresolved Questions
- ✅ **RESOLVED (owner 2026-08-13)** — E3 memory ownership: redesign-r2 is closed; all work contained in R4. FR-07/FR-09 owned directly here, no coordination-window gating.
- ✅ **RESOLVED (owner 2026-08-13)** — Advisory reasoning tier/temperature for FR-01: ADR-016 Reasoning tier, temp ~0.2–0.3 (folded into FR-02).

*(No open blocking questions remain.)*

---

*AI-optimized specification. Original design: `design.md`. Owner interview: 2026-08-13.*
