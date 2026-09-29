# HANDOFF → insights-engine-widgets — architecture impacts from redesign-r1 + redesign-r2

> **From**: `spaarke-ai-architecture-redesign-r2` (core AI platform), 2026-07-12.
> **To**: `ai-spaarke-insights-engine-widgets-r1` (and its r2+ refurbish follow-on).
> **Why you got this**: widgets-r1 shipped **2026-06-11**. Since then, two core platform projects changed the ground your widget framework stands on — **redesign-r1** (ADR-039/040 dispatch + ledger; merged) and **redesign-r2** (memory + ContextEnvelope + OutcomeCard seams; at UAT/close). Your r1 code is NOT broken by these (you consume via facades), but your **r2+ roadmap** ("additional topics, modes, record types") collides with two hard constraints below. Read this before planning the refurbish.
>
> **How to use this file**: it is self-contained. Each section is `WHAT CHANGED → WHERE (file:seam) → IMPACT ON WIDGETS → REQUIRED ACTION`. The "What stays valid" section tells you what NOT to refactor so you don't over-correct.

---

## TL;DR — the six coordination points

| # | Coordination point | Severity for widgets r2+ | Your action |
|---|---|---|---|
| **1** | **Insights invocation moved to Bindings** — `Insights.Playbooks.Map` config DELETED; routing now in the `sprk_playbookconsumer` Dataverse table | ⚠️ Medium — invocation path changed under you | Verify your topic→playbook resolution goes through the Binding surface, not deleted config |
| **2** | **Playbook node-graph engine is FROZEN** — new capability may NOT be authored as node-graph playbooks | 🔴 **High — blocks "new topics/modes" as designed** | Re-plan new topics as **Linear Actions** or **coded composites**, NOT new node-graph playbooks |
| **3** | **Insights outputs now write the ADR-040 session ledger** (store-before-render) | 🟡 Low-Med — persistence model shifted | Decide: does your `sprk_performancesummary` persistence stay, or converge on the ledger/memory? |
| **4** | **r2 built `ContextEnvelope` + `OutcomeCard` seams** — you are the named consumer | 🟢 Opportunity | Consume these seams in the refurbish instead of bespoke context/render |
| **5** | **r2 shaped Record-memory as Insights' future durable store** (`source: insights-engine`) | 🟢 Direction | Align persistence roadmap; do not build a competing durable store |
| **6** | **Anti-fork rule (r2 FR-D-03)** — core is the ONLY project editing `Services/Ai/` internals | 🔴 **Hard constraint** | Consume via facades only; never fork an AI-internal seam |

---

## 1. Insights invocation moved onto the Binding dispatch surface (redesign-r1 FR-P3-01)

**WHAT CHANGED.** Under the ADR-039 *single-routing-surface* rule, redesign-r1 task 040 **deleted** the `Insights.Playbooks.Map` appsettings block (along with `LinearConsumers` + `Workspace.*PlaybookId`). This was a **hard cutover — no config fallback exists** (NFR-08).

**WHERE.**
- Deleted config marker: `src/server/api/Sprk.Bff.Api/appsettings.template.json` (`_routing_config_removed_comment`, line ~354).
- New routing home: **`sprk_playbookconsumer`** Dataverse table. Insights canonical playbook names now live in `sprk_consumercode` on the `insights-ask` rows.
- Seed mirror: `infra/dataverse/sprk_playbookconsumer-insights-rows.json`.
- Facade unchanged: **`Services/Ai/PublicContracts/IInsightsAi.cs`** is still the one allowed cross-zone seam.

**IMPACT ON WIDGETS.** Your r1 `matter-health-single` invocation via `IInsightsAi` + `IInsightsPlaybookExecutionCache` **still works** — the facade is intact. BUT if any part of your topic-registry resolution or a new topic assumes the old `Insights.Playbooks.Map` config path, it is dead. Your `sprk_aitopicregistry` (topic→playbook mapping) now coexists with `sprk_playbookconsumer` (consumer→playbook routing) — understand which table owns which hop before adding a topic.

**REQUIRED ACTION.**
- Confirm your topic→playbook resolution reaches the engine **only via `IInsightsAi`** (which resolves through the Binding surface), not via any deleted config key. Grep your code for `Insights:Playbooks` / `Insights.Playbooks` — expect zero.
- For each NEW topic, decide whether it needs a `sprk_playbookconsumer` Binding row (if reachable from chat/dispatch) in addition to a `sprk_aitopicregistry` row (widget display config). Coordinate the row with core so it mirrors the seed convention.

---

## 2. 🔴 The playbook node-graph engine is FROZEN — this constrains your r2+ roadmap (redesign-r1 OQ-2 / D11)

**WHAT CHANGED.** redesign-r1 ratified (OQ-2 / D11) that the **node-graph playbook engine** (`PlaybookOrchestrationService` + the `INodeExecutor` registry, `Services/Ai/Nodes/**`) is **FROZEN**: it runs the **Insights family only** and is **retired by attrition**. **New capability MUST NOT be built as node-graph playbooks.** Task 044 deleted the engine *shell* (`PlaybookExecutionEngine.cs`) and all dead legs (net −11,849 lines) but left the frozen engine internals untouched (verified `git diff --stat` empty).

Terminology you'll see in core docs: **Linear** (single-step prompted Action → one LLM call, executed by `Services/Ai/LinearConsumers/` ActionRunner + PromptSchemaRenderer) vs **Multistep** (composite). Your `matter-health-single` 7-dimension playbook is a **Multistep composite on the frozen engine**.

**IMPACT ON WIDGETS.** This is the big one. Your r1 README lists r2+ as *"extend to additional topics, modes, and record types."* Your existing `matter-health-single` is grandfathered on the frozen engine and keeps running. **But any NEW topic authored as a new node-graph playbook is now prohibited.** The freeze means the "author another JPS node-graph playbook per topic" pattern from r1 is a **dead-end for new topics**.

**REQUIRED ACTION.** For each new topic in the refurbish, choose an ADR-039-compliant authoring path:
- **Linear Action** (`kind=prompted`, single LLM call via ActionRunner) — if the topic is a single-shot narrative from assembled context. Simplest; preferred where it fits.
- **Coded composite** (registered C# `ICodedWorkflow`) — if the topic genuinely needs multi-step orchestration (Daily Briefing is the reference "first coded composite").
- **NOT** a new `sprk_playbook` + `sprk_playbooknode` graph. Do not extend the frozen engine.
- **Escalate to core** if a new topic seems to *require* the node-graph engine — that's an ADR conflict (root CLAUDE.md §6.5), resolve it explicitly, don't work around it.

> Net: your **framework** (`InsightSummaryCard`, `sprk_aitopicregistry`, pre-warm, TTL cache, persistence) is reusable and unaffected. Only the **per-topic authoring mechanism** must move off node-graph playbooks for new topics.

---

## 3. Insights outputs now write the ADR-040 session ledger (redesign-r1 FR-P1-05, E-2)

**WHAT CHANGED.** An **engine-output→ledger adapter (E-2)** was built so frozen Insights composite outputs emit **`SessionOutput` ledger entries** — *"an insights run produces addressable ledger output"* — under the ADR-040 **store-before-render** contract. Re-homed at task 044 to `AnalysisExecutionHandler.RerunAnalysisAsync` (`EngineRunOutput` input record: RunId / TextContent / StructuredData / CitationChunkIds / Confidence; ledger write runs AFTER engine drain and BEFORE render; a ledger-write failure fails the call).

**IMPACT ON WIDGETS.** Your r1 persists the narrative JSON envelope to `sprk_matter.sprk_performancesummary` (longtext) + TTL cache. That is a **record-scoped persistence**, separate from the **session-scoped ledger**. They don't conflict today, but they are two different persistence stories for "the same insight output." Understand which one downstream consumers (reports, emails, notifications) should read.

**REQUIRED ACTION.**
- Keep `sprk_performancesummary` persistence for r1 (it's the host-record durable copy for form/report reuse). Confirm this is still the intended read-source for downstream consumers.
- When you touch invocation in the refurbish, be aware the ledger write now happens inside the engine path — do not add a second competing "store" step; store-before-render is already enforced upstream.

---

## 4. 🟢 r2 built `ContextEnvelope` + `OutcomeCard` — consume them in the refurbish (redesign-r2)

**WHAT CHANGED.** redesign-r2 built two seams and **named the "Insights Engine Widget refurbish" as their consumer** (r2 design §3.2, §244: *"consumes OutcomeCard + ContextEnvelope"*).
- **ContextEnvelope** — per-turn, in-memory assembled context (host context + session + memory), built by **`Services/Ai/Context/ContextBinder.cs`** (+ `ContextEnvelopeRenderer.cs`, `ContextSliceProducers.cs`, `IContextBinder.cs`). Budget-capped (~4200 tokens). NOT durably stored — derived per turn.
- **OutcomeCard** — the canonical **side-effect render** component: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/OutcomeCard.tsx` (record chip + next-step chips + Undo).

**IMPACT ON WIDGETS.** Your `InsightSummaryCard` (5-state widget) predates OutcomeCard. Where an insight produces an actionable outcome (open the matter, next-step), OutcomeCard is now the platform-standard render — your card may converge on or compose with it rather than reimplement chips. ContextEnvelope gives you a standard, budgeted way to supply subject/host context to an insight invocation instead of bespoke context assembly.

**REQUIRED ACTION.**
- In the refurbish, evaluate consuming **OutcomeCard** for actionable-insight affordances (don't fork it — see §6).
- Evaluate sourcing subject/host context via **ContextEnvelope** rather than a widget-specific context path. Coordinate any envelope slice you need with core (core owns `Services/Ai/Context/`).

---

## 5. 🟢 Record-memory is shaped to become Insights' durable store (redesign-r2 FR-B-01)

**WHAT CHANGED.** r2 built a **structured Record-scope memory container** (per-fact documents, partitioned by subject `entityId`/`userId`, upsert-by-`(Type,Key)` supersession). It was **explicitly shaped so a fact may carry `source: insights-engine`** — *"Insights currently TTL-cached with NO durable store; Record memory shaped to become that durable store."* **Wiring the Insights Engine to write into it is a named follow-on, deferred** (not core-r2 scope), but the envelope was designed not to preclude it.

**IMPACT ON WIDGETS.** Today Insights (and your widgets) have no durable knowledge store — only `sprk_performancesummary` + TTL cache. The strategic direction is that derived Insights knowledge lands in Record memory. Your persistence roadmap should **not build a competing durable store**; it should anticipate converging on Record memory when the wiring lands.

**REQUIRED ACTION.**
- Do NOT design a new durable Insights store in the refurbish. If you need durable derived knowledge beyond the host-record longtext, **coordinate with core** on the Record-memory direction (`source: insights-engine`) rather than inventing one.
- Treat this as *direction-adopted, wiring-deferred* — plan for it, don't block on it.

---

## 6. 🔴 Anti-fork rule — core owns `Services/Ai/` internals (redesign-r2 FR-D-03)

**WHAT CHANGED.** r2 FR-D-03 (cross-satellite seam-fork verification) declares: **the core is the ONLY project modifying `Services/Ai/` internals**; satellites (Compose r2, **Insights refurbish**) must **not fork an AI-internal seam**. Enforced via the hot-path registry (`projects/INDEX.md`), `/conflict-check` at PR time, and a grep/NetArchTest check.

**IMPACT ON WIDGETS.** Your refurbish is a **product-surface project, not a platform project** (your own README says so). You consume `IInsightsAi`, ContextEnvelope, OutcomeCard — you must not copy/fork any of them or edit `Services/Ai/` internals.

**REQUIRED ACTION.**
- Consume every AI capability through its **facade/seam** (`IInsightsAi`, OutcomeCard, ContextEnvelope, `sprk_aitopicregistry`, `sprk_playbookconsumer`).
- If a seam doesn't expose what you need, **file a seam request to core** — don't fork. Core adds the seam; you consume it.
- Register your project's hot-path touches in `projects/INDEX.md` and run `/conflict-check` before any BFF/SpaarkeAi PR.

---

## What STAYS valid from widgets-r1 (do NOT refactor these)

Your r1 framework is sound and largely insulated. These are unaffected by r1/r2 and should be **reused as-is**:

- ✅ `InsightSummaryCard` component + its 5 states (idle/loading/loaded/error/decline/stale) — the framework contribution stands.
- ✅ `sprk_aitopicregistry` topic-registry pattern (topic → playbook → display config).
- ✅ `sprk_matter.sprk_performancesummary` JSON-envelope persistence (host-record durable copy).
- ✅ Per-topic TTL caching via `IInsightsPlaybookExecutionCache` + `sprk_cachettlminutes`.
- ✅ Form-OnLoad pre-warm (fire-and-forget when stored summary is stale).
- ✅ `Sprk.Bff.Api.InsightWidgets` meter + `widget.insightcard.invoked` telemetry.
- ✅ The honesty contract (decline rendering, kill-switch, degraded mode) — aligned with ADR-039 posture.
- ✅ `matter-health-single` itself — grandfathered on the frozen engine; keeps running.

**The ONE thing that must change for growth**: the per-new-topic authoring path (§2) — new topics use Linear Actions or coded composites, not new node-graph playbooks.

---

## Seam / file quick reference (all on master unless noted)

| Seam | Path | Owner | Notes |
|---|---|---|---|
| Insights facade | `Services/Ai/PublicContracts/IInsightsAi.cs` | core | The only allowed AI consumption path (Zone B) |
| Consumer→playbook routing | `sprk_playbookconsumer` (Dataverse) + `infra/dataverse/sprk_playbookconsumer-insights-rows.json` | core | Replaced deleted `Insights.Playbooks.Map` config |
| Widget topic registry | `sprk_aitopicregistry` (Dataverse) | widgets | Topic → playbook → display config |
| ContextEnvelope | `Services/Ai/Context/ContextBinder.cs` (+ `ContextEnvelopeRenderer.cs`, `ContextSliceProducers.cs`, `IContextBinder.cs`) | core | Per-turn assembled context; budget-capped; not durably stored |
| OutcomeCard | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/OutcomeCard.tsx` | core | Canonical side-effect render (chip + next-step + Undo) |
| Ledger output adapter | `AnalysisExecutionHandler.RerunAnalysisAsync` (`EngineRunOutput`) | core | ADR-040 store-before-render for engine outputs |
| Frozen engine | `PlaybookOrchestrationService` + `Services/Ai/Nodes/**` | core (frozen) | Insights-only; retire-by-attrition; DO NOT extend |
| Linear consumer path | `Services/Ai/LinearConsumers/` (ActionRunner + PromptSchemaRenderer) | core | Where new single-step topics should be authored |

---

## Deploy / process coordination (binding)

- **Both BFF and SpaarkeAi are hot paths.** Your refurbish will touch at least the client (`@spaarke/ai-widgets`) and possibly BFF (new Action rows). Add a `<hot-path-declaration>` to your design.md and register in `projects/INDEX.md` (root CLAUDE.md §10 / §G).
- **Deploy rule (learned the hard way this session)**: **merge your worktree with master BEFORE any deploy**, and **coordinate BFF/SpaarkeAi deploys** — do not deploy from master while another project (core UAT, compose-r2) has unmerged client work on the same surface, or you regress their live UAT.
- **Publish-size ceiling ≤60 MB compressed** per BFF-touching task (root §10). r1 baseline ~46–47 MB; you had ~14 MB headroom at r1 close.
- **Run `/conflict-check`** before any PR touching BFF or SpaarkeAi (auto-invoked on hot-path tasks).

---

## Suggested refurbish planning checklist (for your `/design-to-spec`)

1. [ ] Grep your codebase for dead config refs: `Insights.Playbooks`, `Insights:Playbooks` → expect zero (§1).
2. [ ] Confirm all invocation routes via `IInsightsAi` (not deleted config) (§1).
3. [ ] For every NEW topic/mode: choose Linear Action or coded composite; **no new node-graph playbooks** (§2). Escalate genuine node-graph needs to core.
4. [ ] Decide persistence story: keep `sprk_performancesummary`; plan for eventual Record-memory convergence; add no competing durable store (§3, §5).
5. [ ] Evaluate consuming OutcomeCard + ContextEnvelope instead of bespoke render/context (§4). File seam requests to core if gaps.
6. [ ] Add `<hot-path-declaration>`; register in `projects/INDEX.md`; plan deploy coordination (§6, Deploy).
7. [ ] Confirm no `Services/Ai/` internal is forked (§6).

---

## Who to ask

- **Core AI platform (dispatch, Bindings, ContextEnvelope, OutcomeCard, memory, frozen-engine policy)**: `spaarke-ai-architecture-redesign-r2` (at UAT/close 2026-07-12) → then whoever owns the next core platform iteration.
- **Compose editor / session identity**: `spaarkeai-compose-r2`.
- **Canonical Insights architecture doc**: `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` (§0a Terminology is load-bearing).
- **Frozen-engine + dispatch redesign rationale**: `docs/adr/ADR-039-*.md`, `docs/adr/ADR-040-session-ledger.md`; `projects/spaarke-ai-architecture-redesign-r1/` (spec FR-P1-05, FR-P3-01, OQ-2/D11).

— core (`spaarke-ai-architecture-redesign-r2`)
