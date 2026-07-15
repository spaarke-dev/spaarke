# Implementation Plan — Assistant Enhancements R1 ("Follow-Through")

> **Source**: [`spec.md`](spec.md) (R1 only). **Sequencing**: reactive-first — Phases 1–2 (structured-creation core) are the highest-value, most-broken flows and land first. **R1.5 (proactive push) is NOT in this plan.**
> **Execution model**: Sonnet 5 @ high default; `opus`/`xhigh` for the dispatch-spine + resolver tasks (high blast radius). See CLAUDE.md §8.5.

## Architecture Context — Discovered Resources

**Applicable ADRs (full content loaded at task time):**
- **ADR-039** Grounded Execution & Closed Catalogs — ONE probabilistic decider; no second intent mechanism. (Binding, most-cited.)
- **ADR-040** Session Ledger — store-before-render; `WidgetEvent` ledgering.
- **ADR-041** ConfirmationPolicyEngine — tier × risk × origin; ack-contract lineage.
- **ADR-042** Memory — User-scope `MemoryItem`; no second memory store.
- **ADR-043** Input-Resolution (Proposed; spine merged) — `ContextBinder` is the one input-resolution model; dispatch-spine changes carry the `tests/integration/seam/**` DoD.
- **ADR-038** Testing — integration-heavy; 7 KEEP categories; coverage=observation.
- **ADR-032** Null-Object — for the new stated-profile producer (P2 quiet no-op).
- **ADR-024** `sprk_todo` 11-entity regarding — for `create-todo`.

**Key as-built seams (verified 2026-07-15; unchanged by the 12-commit master sync):**
- `Services/Ai/Context/ContextBinder.cs` — `userFragment` composition (`ResolveUserMemoryFragmentAsync`); the sibling seam for the stated-profile producer.
- `Services/Ai/Chat/AgentToolProjection.cs` + `SprkChatAgentFactory.cs` — `PreFilter` (grounding predicate), `AgentToolFilterContext`, host-context threading.
- `Services/Ai/Chat/Gate/` + `PendingPlanManager.cs` — `RequiresConfirmation` (`sprk_risk` wiring + `dispatchUncertain`).
- `Services/Ai/PublicContracts/` — Binding catalog (`ConsumerRoutingService`, `BindingCapabilityTool`, `ChipTransition`); resolver placement candidate.
- Client: `ConsumerChips`/`useConsumerChips` (SNS), `ContextPaneMenu`/`WorkspacePaneMenu` (drop-down), `GetStartedCardsWidget` (wizard library), `CreateMatterWizardWidget` + `wizard_step` set-field (pre-fill), `OutcomeCard` v1.
- Dataverse: `sprk_userprofile` (created), `sprk_practicearea_ref` (governed practice-area set — also the resolver's target for matter creation), `sprk_todo`/`sprk_event`.

**Applicable skills**: `jps-action-create`, `jps-playbook-design`, `dataverse-create-schema`/`dataverse-deploy`, `fluent-v9-component`, `bff-deploy`, `code-page-deploy`, `code-review`, `adr-check`, `test-diet`, `ui-test`.

**Governance**: BFF hot-path `<bff-api>YES` + `<spaarke-ai>YES` (design §11.1); publish-size ≤60 MB; consume `Services/Ai/PublicContracts` seams, NO fork; `/conflict-check` before BFF PRs (email-r4 W5, daily-update-r5 also touch `Services/Ai`).

## ADR Tensions (from spec — carry to code-review)

1. **ADR-039** — User Model must NOT be a second decider → comply + documented project constraint (bias one turn + deterministic display-reorder only).
2. **ADR-039 / FR-D-02** — amend `EnvelopeBudget.User` (300 → sized) → path B, project-scoped; re-baseline eval.
3. **ADR-042** — new profile schema is not a memory store → comply.
4. **ADR-043** — dispatch-spine edits → comply (r2 merged/archived; seam-test DoD).
5. **ADR-032** — new producer null-object → comply.

## Phase Breakdown (WBS)

### Phase 1 — Catalog & Schema Foundation *(prerequisites for the create flows)*
Deliverables:
- **1a** Verify + record the `sprk_userprofile` schema contract (columns, `sprk_systemuser` lookup + alt key, N:N to `sprk_practicearea_ref`). *(FR-E1 — schema created; this is the verification + contract record.)*
- **1b** Capability modeling: author distinct **`create-todo` (`sprk_todo`)** and **`create-event` (`sprk_event`)** Bindings + Actions + `sprk_tooldescription` + `sprk_chiptransitions`. *(FR-A2 / P4; skills: `jps-action-create`, `jps-playbook-design`.)*
- **1c** Add grounding-predicate column(s) to the Binding catalog (e.g. `requires-no-attached-record`). *(FR-H1 schema half.)*

### Phase 2 — Structured Creation Core *(the R2-UAT fixes — highest priority)*
Deliverables:
- **2a** **Constrained-field resolver** primitive (BFF `Services/Ai`): deterministic proposal→closed-set matcher; contract `{resolved?, confidence, candidates[]}`; reads Dataverse choice/lookup metadata (matter practice area → `sprk_practicearea_ref`); cached; no LLM. *(FR-B1; `opus`/`xhigh`.)*
- **2b** Exclude constrained fields from LLM arg-filling; incoherent-combo prevention. *(FR-B3/B4; NFR-06 negative eval.)*
- **2c** Wizard **entry-payload envelope** + hand-off: typed launch (files + resolved/proposed values + source metadata) via `wizard_step` set-field; unify 5-of-7 embedded wrappers + close the 2 gaps. *(FR-A1/A3.)*
- **2d** **Smart pre-seed** integration: assistant pre-resolves proposals (2a) → wizard defaulted dropdowns. *(FR-B2.)*
- **2e** In-wizard assign-to-me + association picker; grounding-optional. *(FR-A4/A5/P6.)*

### Phase 3 — Action Truthfulness & Risk Gate
Deliverables:
- **3a** Action-outcome truthfulness invariant: adopt+complete the D-F3 ack contract (ack-gated claims or honest failure) for all Follow-Through actions; no-collateral-teardown guard. *(FR-C1/C2; UC-4/UC-5 regression.)*
- **3b** `sprk_risk` gate-wiring: pass resolved Binding risk into `PendingPlanManager.RequiresConfirmation`. *(FR-D1; seam-test DoD.)*
- **3c** `dispatchUncertain` routing-confidence producer for the `Confirm When Uncertain` tier. *(FR-D2.)*

### Phase 4 — User Model
Deliverables:
- **4a** User-scope stated-profile producer → `ContextBinder.userFragment` (sibling to memory fragment); read via `CallerSystemUserResolver` + keyed retrieve; renders role label + N:N practice-area names + focus/office/prefs; soft-fail-to-null; ADR-032 null-object. *(FR-E2/E5.)*
- **4b** preference≠permission negative test (`AgentToolFilterContext` carries no profile/memory members); not-a-second-decider constraint. *(FR-E3/E4.)*
- **4c** Amend `EnvelopeBudget.User` + deterministic byte-stable rendering + eval re-baseline + caching/latency + soft-fail. *(NFR-01/02/03.)*

### Phase 5 — Assistant Surface
Deliverables:
- **5a** Assistant-pane **tool drop-down** (Fluent v9, mirrors `ContextPaneMenu`). *(FR-F1.)*
- **5b** **Quick Start** modal reusing the wizard library (`GetStartedCardsWidget`). *(FR-F2.)*
- **5c** **My Assistant** questionnaire: keyed upsert to `sprk_userprofile` + N:N practice-area associates + seed User-scope `MemoryItem`; cold-start gate on `sprk_profilecompletedon`. *(FR-F3.)*
- **5d** **Suggested Next Steps** cards post-dispatch + "more" affordance; deterministic preference-keyed reorder-for-display. *(FR-G1.)*
- **5e** Grounding-predicate wiring: new `AgentToolFilterContext` field + thread `ChatHostContext` from `SprkChatAgentFactory` + new `PreFilter` branch. *(FR-H1 code half; seam-test DoD.)*

### Phase 6 — Authoring, Eval & Hardening
Deliverables:
- **6a** Authoring: richer `sprk_tooldescription` + `sprk_chiptransitions`; narrow ambiguity set (file/open/close/matter + To Do/Event); Q&A authored to dispatch a `list-*` capability. *(FR-J1/G2; reviewer = owner.)*
- **6b** Eval cases per catalog change + negative cases proving profile injection doesn't flip dispatch. *(NFR-06 merge gate.)*
- **6c** Security/authZ/privacy of profile data (roles, OBO vs app-only, erasure tier, prompt-injection stance). *(NFR-05.)*
- **6d** Publish-size ≤60 MB check + CVE scan. *(NFR-04.)*
- **6e** Deploy tasks (BFF + SpaarkeAi code page + Dataverse catalog rows).

### Phase 7 — Wrap-up
- **7a** `/test-diet` reconciliation (ADR-038 §7).
- **7b** File deferrals: `sprk_userpreferences` singular/plural client bug (`project-defer-issue-tracking`); R1.5 proactive-push spec pass.
- **7c** Lessons-learned; README → Complete; `/devops-project-sync`.

## Dependencies & Parallel Opportunities

- Phase 1 gates Phase 2 (capabilities + schema before create flows).
- **2a (resolver)** is on the critical path — 2b/2d depend on it.
- Phase 3 (risk/truthfulness) and Phase 4 (User Model) are **largely independent** of each other → parallelizable after Phase 2.
- Phase 5 (client) depends on 2c (envelope), 4a (profile read for personalization), 5e depends on 1c/H schema.
- `.claude/**` edits (none expected) and BFF DI must be main-session/sequential per hot-path rules.
- **Sequential (main-session):** dispatch-spine tasks (2a, 3b, 3c, 5e) — high blast radius; carry seam-test DoD; do not parallelize with each other.

## Estimated shape

~7 phases, ~30–45 tasks anticipated (finalized by `/task-create`). Critical path: Phase 1 → 2a → 2c/2d → 3b → 5. Highest-risk: 2a (resolver), 3b (gate-wiring), 4a/4c (hot chat-path + token budget), 5c (write paths).
