# Spaarke AI Assistant Enhancements R1 ("Follow-Through") — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-15
> **Source**: `design.md` (working design, reality-aligned to as-built 2026-07-15; four-agent Fable as-built review + researcher push-channel spike + R2 UAT integration folded in)
> **Codename**: Follow-Through
> **Scope decision (owner 2026-07-15)**: This spec covers **R1 only**. **R1.5** (full proactive-push capability via Azure SignalR) is fully designed in `design.md` §14.1a/§14.1b/§12.5/§15.4 and is a **defined, sequenced follow-on — NOT decomposed into tasks by this spec.**
> **Binding foundations**: [ADR-039 Grounded Execution & Closed Catalogs](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md) · [ADR-040 Session Ledger](../../docs/adr/ADR-040-session-ledger.md) · [ADR-041 ConfirmationPolicyEngine] · [ADR-042 Memory] · [ADR-043 Input-Resolution (Proposed)] · [ADR-038 Testing]
> **Evidence base**: [`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md) (R2 live-UAT create-flow failures — the R1-core motivation).

---

## Executive Summary

Reposition the Spaarke Assistant from a reactive "ask-me-anything" text box into a grounded **dispatcher** that finishes the operator's likely next step. R1 delivers the **reactive core**: reliable **draft-in-chat → commit-in-a-pre-seeded-wizard** structured creation (matter / to-do / event), a **deterministic constrained-field resolver** so the LLM never guesses a system-owned value set, an **action-outcome truthfulness invariant**, the **User Model** (AI-readable stated profile) that personalizes suggestions, and the **Assistant tool drop-down** (Quick Start + My Assistant). ~80% of the Next-Best-Action machinery is already shipped under ADR-039; R1 **extends the shipped catalog** and fixes the highest-value, most-broken flows surfaced in R2 UAT. The full **proactive-push** capability is designed and sequenced as **R1.5**.

---

## Scope

### In Scope (R1)

1. **Structured creation — draft-in-chat, commit-in-wizard** for **create-matter, create-to-do, create-event** (fixes R2 UAT UC-1…UC-3).
2. **Deterministic constrained-field resolver** ("smart pre-seed") — resolves LLM-proposed values against Dataverse metadata → `{pre-select | picker defaulted to best guess}`; constrained fields excluded from LLM arg-filling.
3. **Capability modeling to real entities** — distinct `create-todo` (`sprk_todo`) vs `create-event` (`sprk_event`); "To Do vs Event" authored as a §5 ambiguity.
4. **Action-outcome truthfulness invariant** — every action claim ack-gated or fails honestly (D-F3 ack contract adopted); no collateral pane/tab teardown.
5. **`sprk_risk` gate-wiring** — wire the resolved Binding's risk into `PendingPlanManager.RequiresConfirmation` + build the `dispatchUncertain` routing-confidence producer.
6. **User Model / AI-readable stated profile** — add typed columns to the existing `sprk_userprofile`; a **User-scope stated-profile producer** composed into `ContextBinder.userFragment`; provenance; preference≠permission invariant; not-a-second-decider constraint.
7. **Assistant tool drop-down** — Quick Start modal (reuses existing `Create*` wizard library) + My Assistant questionnaire (writes the stated profile + seeds User-scope `MemoryItem`).
8. **Suggested Next Steps (reactive)** — render emitted `sprk_chiptransitions` as ranked actionable cards after an answer/completed action, + a "more" affordance; deterministic preference-keyed reorder-for-display.
9. **Grounding-predicate column(s)** on the Binding (e.g. `requires-no-attached-record`) + the `AgentToolFilterContext`/`PreFilter`/`SprkChatAgentFactory` plumbing.
10. **Authoring content** — richer `sprk_tooldescription` + `sprk_chiptransitions` rows; a narrow high-frequency ambiguity set (file/open/close/matter + To Do/Event).

### Out of Scope (R1) — designed, deferred

- **Proactive-push capability (R1.5)** — Azure SignalR channel + durable outbox + server-fireable Event path + Daily-Briefing producer + Assistant unsolicited-render. Fully designed (`design.md` §14.1a/§12.5/§15.4); decomposed in a later spec pass.
- **General notification-spine consumers** — job-complete / share / system-alert kinds on the shared channel (§14.1b).
- **`IOrganizationalContextProvider`** implementation — stays the deferred inbound org-scope (Work IQ) seam; R1 uses the User-fragment path instead.
- **Broad ambiguity coverage**; **Follow-Through outside the Assistant** (records/widgets); create-flows beyond matter/to-do/event (invoice/project/etc.).

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/Context/ContextBinder.cs` — new User-scope stated-profile producer composed into `userFragment`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AgentToolProjection.cs` + `SprkChatAgentFactory.cs` — grounding-predicate branch; host-context threading; `sprk_risk` into the gate.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Gate/` + `PendingPlanManager.cs` — `sprk_risk` gate-wiring + `dispatchUncertain` producer.
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` — Binding catalog columns; constrained-field resolver primitive (placement TBD in pipeline Step 2 vs `Services/Ai`).
- `src/solutions/SpaarkeAi/src/components/` (Assistant pane) — tool drop-down, My Assistant questionnaire, SNS cards, wizard hand-off, ack-gated action reporting.
- `src/client/shared/Spaarke.UI.Components/src/components/Create*Wizard/` + `Spaarke.AI.Widgets/` — wizard entry-payload envelope, `wizard_step` set-field pre-fill (5→7 wizards).
- **Dataverse**: `sprk_userprofile` new columns; Binding grounding-predicate column(s); `create-todo`/`create-event` Bindings + `sprk_tooldescription`/`sprk_chiptransitions` rows.

---

## Requirements

### Functional Requirements

**Workstream A — Structured Creation (R2-UAT-driven; highest priority)**

1. **FR-A1** (P2/P3): Draft-in-chat → the dispatcher launches the **correct pre-seeded `Create*Wizard`**; the wizard owns the gated write. — Acceptance: "create a matter/to-do/event" produces a real record of the **right entity** via the wizard; no multi-turn field-by-field elicitation; no dead-end.
2. **FR-A2** (P4): Distinct **`create-todo` (`sprk_todo`)** and **`create-event` (`sprk_event`)** capabilities exist; "a To Do not an Event" routes correctly. — Acceptance: explicit "To Do" creates `sprk_todo`; entity-type ambiguity resolves by high-confidence inference or one-tap pick, never text negotiation (§5).
3. **FR-A3**: Wizard **entry-payload envelope** (files + resolved/proposed values + source metadata) carries the launch; rides `wizard_step` `set-field` (not `applyFieldMappings`, which is record-sourced); unify the 5-of-7 embedded wrappers + close the 2 gaps. — Acceptance: a handed-in file + seed values pre-fill the wizard on launch.
4. **FR-A4** (P4 continued): In-wizard **assign-to-me** honored end-to-end (FR-B-06) + **association picker** (matter/project/invoice/none). — Acceptance: "assign it to me" sets `assignee = current user`; association selectable.
5. **FR-A5** (P6): **Grounding is optional** — a simple task must not require a document. — Acceptance: create-to-do with no file succeeds without demanding session content.

**Workstream B — Constrained-Field Resolver (smart pre-seed, owner decision 2026-07-15)**

6. **FR-B1** (P1): A **deterministic constrained-field resolver** primitive (the field-value analogue of ADR-039 capability grounding). **Contract (resolved 2026-07-15):** Input `(entityLogicalName, attributeLogicalName, proposedValue, optional context)` → Output `{ resolved?: optionValue|recordId, confidence: high|low|none, candidates[] }`. Valid-set source = Dataverse metadata (choice label/value for option-sets; filtered candidate query for lookups), cached per field. Deterministic match ladder: exact case-insensitive → normalized (trim/punctuation/configured synonyms) → fuzzy above a similarity threshold → none. **No LLM call.** **Placement:** a BFF service in `Services/Ai`, callable from the chat/dispatch path (so smart pre-seed works); reuse the wizards' existing option-set metadata rendering for the picker/`candidates[]` — the genuinely-new piece is the proposal→option **matcher** only (verify existing option-set helpers before building new). — Acceptance: valid proposals pre-select (high); no-match/ambiguous returns a picker defaulted to the top candidate; the LLM never emits a final closed-set value.
7. **FR-B2**: **Smart pre-seed** — the assistant pre-resolves proposals *before* hand-off and hands the wizard defaulted dropdowns. — Acceptance: "create a matter from this letter" opens the wizard with practice-area/matter-type dropdowns defaulted from resolved values.
8. **FR-B3**: Constrained fields are **excluded from LLM arg-filling** in the capability `sprk_inputschema` path. — Acceptance: a code/test assertion that closed-set fields are resolved by FR-B1, not free-text LLM args.
9. **FR-B4**: Incoherent proposals cannot commit. — Acceptance: a nonsensical practice-area × matter-type pair is impossible via the grounded picker (negative eval case).

**Workstream C — Action Truthfulness (P5, existential)**

10. **FR-C1**: Every action claim is **ack-gated** on a client acknowledgment referencing the emitted action, or **fails honestly**. — Acceptance: a UI action that does not complete yields an honest failure message, never a fabricated success (UC-5 regression test).
11. **FR-C2**: An orchestrated action must **not** cause collateral teardown of unrelated panes/tabs. — Acceptance: a record delete does not close an unrelated Compose tab (UC-4 no-regress).

**Workstream D — `sprk_risk` Gate-Wiring**

12. **FR-D1**: Pass the resolved Binding's `sprk_risk` into `PendingPlanManager.RequiresConfirmation`. — Acceptance: `Always Confirm` renders a suggestion-that-launches; `None` runs without a gate.
13. **FR-D2**: Build the `dispatchUncertain` routing-confidence producer for the `Confirm When Uncertain` tier. — Acceptance: low-confidence dispatch triggers the gate; high-confidence does not.

**Workstream E — User Model**

14. **FR-E1**: Add typed columns to `sprk_userprofile` (`sprk_primaryrole`, `sprk_practiceareas`, `sprk_focusareas`, `sprk_officelocation`, `sprk_assistantpreferences`, `sprk_profilecompletedon`, `sprk_profileversion` — see design.md §6). **Relationship (resolved 2026-07-15):** add a `sprk_systemuser` lookup on `sprk_userprofile` → `systemuser` **+ an alternate key** on it (canonical 1:1 profile-extension; enables keyed upsert + platform-enforced one-per-user; no OOB-table dependency). The existing `systemuser.sprk_userprofile` lookup may remain for MDA convenience but the profile→user side is authoritative. — Acceptance: columns exist; alternate key present; option-set values finalized.
15. **FR-E2**: A **User-scope stated-profile producer** renders a fragment composed into `ContextBinder.userFragment` (sibling to the `MemoryItem` fragment), soft-fail-to-null. Read path: `CallerSystemUserResolver` (`oid`→`systemuserid`) → keyed retrieve of `sprk_userprofile` by `sprk_systemuser`. Write path: **keyed upsert** by `sprk_systemuser` (no find-then-create race). — Acceptance: a profiled user's turn carries the stated profile; unprofiled/absent degrades to no fragment, never fails the bind.
16. **FR-E3**: **preference ≠ permission** — the profile biases the one agent turn and reorders already-grounded chips for display; it **never grants a capability** (grounding still gates). — Acceptance: negative test asserts `AgentToolFilterContext` carries no profile/memory-derived members.
17. **FR-E4**: **Not a second decider** (ADR-039) — no ranking/scoring/vector stage introduced. — Acceptance: adr-check + design-constraint sign-off; chip reorder is deterministic, preference-keyed, no model call.
18. **FR-E5**: Role/BU/team read via `sprk_userentityassociation` + membership services + `CallerSystemUserResolver`, folded into the same User fragment. — Acceptance: role reflected in the profile fragment.

**Workstream F — Assistant Tool Drop-Down**

19. **FR-F1**: The Assistant pane gains a **tool drop-down** (Fluent v9 menu, mirroring `ContextPaneMenu`/`WorkspacePaneMenu`). — Acceptance: drop-down present; does not disturb the three-pane layout.
20. **FR-F2**: **Quick Start** opens a modal presenting the existing `Create*` wizard library (reuse `GetStartedCardsWidget`). — Acceptance: wizards launch from the modal.
21. **FR-F3**: **My Assistant** questionnaire writes the stated profile (FR-E1) and seeds User-scope `MemoryItem` (`source=user`). — Acceptance: completing it populates `sprk_userprofile` + a memory fragment; cold-start gate keys on `sprk_profilecompletedon`.

**Workstream G — Suggested Next Steps (reactive)**

22. **FR-G1**: Render emitted `sprk_chiptransitions` as ranked actionable cards after a capability runs (Click/Event/Text-dispatch), + a "more" affordance opening the NBA-library modal. — Acceptance: chips appear as cards post-dispatch; "more" opens the library.
23. **FR-G2**: SNS appears only after a capability runs; bare Q&A is authored to **dispatch** a `list-*` capability whose chips fire (owner decision — option (a)). — Acceptance: "what are my tasks?" dispatches a list capability that emits chips.

**Workstream H — Grounding Predicate**

24. **FR-H1**: Add grounding-predicate column(s) (e.g. `requires-no-attached-record`) + a new `AgentToolFilterContext` field + thread `ChatHostContext` from `SprkChatAgentFactory` + a new deterministic `PreFilter` branch. — Acceptance: "Create matter" is hidden when already inside a matter; a pure predicate, no model call.

**Workstream J — Authoring**

25. **FR-J1**: Author richer `sprk_tooldescription` + `sprk_chiptransitions` rows for the R1 capabilities; author the narrow ambiguity set (file/open/close/matter + To Do/Event) into tool descriptions. — Acceptance: eval cases pass; named reviewer approves (see Owner Clarifications).

### Non-Functional Requirements

- **NFR-01** (User-slice token budget): **Amend `EnvelopeBudget.User`** (currently 300, a binding golden-utterance merge-gate) to accommodate the profile fragment; size from actual rendered length; re-baseline the golden-utterance gate; record the constant change (code-review sign-off).
- **NFR-02** (Byte-stability): The profile fragment renders **deterministically** (ordinal-ordered multi-selects, canonicalized prefs JSON — no map-order nondeterminism); update `ContextEnvelopeRendererTests`; re-baseline the eval prompt-cache prefix (NFR-04).
- **NFR-03** (Latency/caching): The profile adds a second per-turn Dataverse read (`systemuser` → `sprk_userprofile`) on the hot chat-bind path; define a latency budget + caching decision (cite or reject the `IdentityNormalizationService` Redis 10-min TTL precedent); soft-fail-to-null (NFR-07).
- **NFR-04** (Publish-size): ≤60 MB compressed ceiling; measure per-task; baseline ~49.63 MB incl. PDBs. (No new NuGet expected in R1; SignalR package is R1.5.)
- **NFR-05** (Security/authZ/privacy of profile): Dataverse security roles for `sprk_userprofile` read/write; OBO vs app-only for the producer (app-only bypasses row security); GDPR/erasure tier of the stated profile; **prompt-injection stance** on user-authored `sprk_focusareas`/`sprk_assistantpreferences` injected into the stable prefix.
- **NFR-06** (Eval-case obligation — merge gate): Every catalog change (`sprk_tooldescription`, chip rows, new columns, ambiguities) adds/updates eval cases, **plus negative cases proving profile injection does not flip dispatch decisions** (operational proof of FR-E4).
- **NFR-07** (Testing — ADR-038): Unit + `tests/integration/seam/**` vertical-slice coverage (DoD for dispatch-spine changes: FR-D, FR-H); direct `PreFilter`-branch tests; resolver tests; questionnaire write-path tests; the FR-E3 negative test. TEST-MODIFYING rigor override applies.

---

## Technical Constraints

### Applicable ADRs

- **ADR-039** (Grounded Execution & Closed Catalogs) — one dispatch protocol · three entry paths · two closed catalogs · every output grounded; **ONE probabilistic decider** (the single function-calling agent turn).
- **ADR-040** (Session Ledger) — store-before-render; `WidgetEvent` ledgering (chip impressions/clicks).
- **ADR-041** (ConfirmationPolicyEngine) — tier × risk × origin gate; ack contract lineage.
- **ADR-042** (Memory) — User-scope `MemoryItem`; do NOT build a second memory store; `insights-engine` origin reserved.
- **ADR-043** (Input-Resolution, **Proposed**) — `ContextBinder` is the one input-resolution model; named engine owner + intake path for dispatch-spine changes; **merge-ordering dependency with redesign-r2 Phase E** (same files).
- **ADR-038** (Testing) — integration-heavy; `tests/integration/seam/**` DoD; coverage = observation not gate.
- **ADR-032** (Null-Object) — the new producer needs a P2 quiet no-op classification.
- **ADR-024** (`sprk_todo` 11-entity regarding) — for `create-todo`.

### MUST Rules

- ✅ MUST resolve closed, system-owned value sets **deterministically against metadata** (P1); ❌ MUST NOT let the LLM emit final closed-set values.
- ✅ MUST keep the User Model to biasing the one agent turn + deterministic display-reorder; ❌ MUST NOT introduce any second intent mechanism (classifier, reranker, vector router, keyword map, lexicon-resolver).
- ✅ MUST ack-gate every action claim or fail honestly; ❌ MUST NOT make optimistic UI claims.
- ✅ MUST inject the stated profile via `ContextBinder.userFragment`; ❌ MUST NOT implement `IOrganizationalContextProvider` in R1 (wrong scope; deferred).
- ✅ MUST keep grounding a pure predicate (removes-the-impossible); ❌ MUST NOT gate by hardcoded tool-name lists.
- ✅ MUST reuse the shipped dispatch seam (`POST /api/ai/chat/sessions/{id}/dispatch`); ❌ MUST NOT add a new BFF dispatch endpoint (compose-r2 invariant).
- ✅ MUST render the profile fragment deterministically (byte-stability, NFR-02).

### Existing Patterns to Follow

- Dispatch spine: `ConsumerRoutingService`, `SessionDispatchOrchestrator`, `AgentToolProjection.PreFilter`, `BindingCapabilityTool`.
- Context injection: `ContextBinder.ResolveUserMemoryFragmentAsync` → `IMemoryItemStore.ToUserPromptFragmentAsync` (the sibling seam for FR-E2).
- Identity: `CallerSystemUserResolver`, `IIdentityNormalizationService`, `IMembershipResolverService`.
- Client: `ConsumerChips`/`useConsumerChips` (SNS substrate), `ContextPaneMenu`/`WorkspacePaneMenu` (drop-down), `GetStartedCardsWidget` (wizard library), `CreateMatterWizardWidget` + `wizard_step` set-field (pre-fill), `OutcomeCard` v1 (card contract — reuse or justify parallel).
- Notification substrate (for R1.5): `NotificationService` → `appnotification`.

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039** | "No second intent mechanism; ONE probabilistic decider" | The User Model could be mis-built as a ranker/scorer over capabilities | **C (comply)** + **A (documented constraint)** | R1 uses the profile ONLY to (a) bias the one agent turn via `userFragment` and (b) deterministically reorder already-grounded chips for display. Ratify as a project-scoped design constraint; FR-E3/E4 make it mechanically checked. No ranking stage introduced. |
| **ADR-039 / FR-D-02** | `EnvelopeBudget.User = 300` is a ratified constant + golden-utterance merge-gate | The stated-profile fragment can exceed 300 tokens | **B (amendment, project-scoped)** | Owner decision 2026-07-15: amend the ceiling (NFR-01), size from rendered length, re-baseline the gate, code-review sign-off. Documented rather than silently squeezed. |
| **ADR-042** | "Flags any new memory store" | New `sprk_userprofile` columns + (R1.5) outbox table | **C (comply)** | The typed Dataverse profile is not a memory-items store; learned signals still route to `MemoryItem` User scope. Stated-vs-learned precedence rule = **stated > learned unless user confirms** (surfaced conflict). |
| **ADR-043 (Proposed)** | "Named engine owner + intake; no ownerless execution-wiring change" | R1 modifies `ContextBinder` + dispatch spine while redesign-r2 Phase E converges the same files | **C (comply)** | Register in `projects/INDEX.md`; sequence against Phase E; `tests/integration/seam/**` DoD on FR-D/FR-H. Declared as a merge-ordering dependency, not a silent parallel edit. |
| **ADR-032** | Null-Object discipline | New stated-profile producer | **C (comply)** | Classify as P2 quiet no-op (profile absent = everyday default); soft-fail-to-null (NFR-03). |

> No other ADR tensions surfaced at design time. `IOrganizationalContextProvider` is left deferred (comply — R1 does not touch it).

## Success Criteria

1. [ ] "Create a matter / a to-do / an event" each produce the **right entity** via a pre-seeded wizard, no dead-end — Verify by: UAT replay of UC-1…UC-3.
2. [ ] The matter-creation closed-set fields (practice area, matter type) resolve via the FR-B1 resolver with defaulted dropdowns; a nonsensical pair cannot commit — Verify by: UC-3 replay + FR-B4 negative eval.
3. [ ] No fabricated action claims; a failed UI action reports honestly — Verify by: UC-5 regression test.
4. [ ] A delete does not tear down unrelated tabs — Verify by: UC-4 no-regress test.
5. [ ] `sprk_risk = Always Confirm` gates as suggestion-that-launches; `Confirm When Uncertain` fires on low-confidence dispatch — Verify by: seam tests (FR-D).
6. [ ] A profiled user's agent turn carries the stated profile within the amended token budget; byte-stability + eval baselines green — Verify by: renderer test + golden-utterance gate.
7. [ ] `AgentToolFilterContext` carries no profile/memory-derived members (preference≠permission) — Verify by: FR-E3 negative test.
8. [ ] Tool drop-down (Quick Start + My Assistant) present; My Assistant writes profile + seeds memory — Verify by: UI test + Dataverse read.
9. [ ] SNS cards render post-dispatch + "more" opens the library — Verify by: UI test.
10. [ ] Publish-size ≤60 MB; no new HIGH CVE — Verify by: `dotnet publish` measure + `dotnet list package --vulnerable`.

## Dependencies

### Prerequisites

- `sprk_userprofile` columns created (owner creates manually in dev; spec needs a verification step + recorded column contract so POMLs don't assume un-promoted columns), **including the `sprk_systemuser` lookup + alternate key** (relationship option B, resolved).
- `create-todo`/`create-event` Bindings + Actions authored.
- Register project in `projects/INDEX.md`; complete design.md §11.2 Placement Justification to the bff-extensions Project-Level Imperative bar.

### External Dependencies

- None new for R1 (Azure SignalR is R1.5). Solution-management / dev→prod promotion path for the new Dataverse columns.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Proactive scope | Full capability vs quick-win? | **Full, real capability** — sequenced R1.5 | Reactive-first R1; proactive designed but deferred |
| R1 vs R1.5 sequencing | One R1 or split? | **Reactive-first: create-flows = R1, proactive = R1.5** | This spec = R1 only |
| Spec coverage | R1 only or R1+R1.5? | **R1 only; R1.5 as designed follow-on** | R1.5 not decomposed here |
| Constrained-field resolver | Thin hand-off vs smart pre-seed? | **Smart pre-seed** | Resolver runs on the assistant side pre-hand-off (FR-B2) |
| `sprk_risk` | Wire live or side_effect_class-only? | **Wire live** (FR-D) | Gate-wiring + `dispatchUncertain` producer in R1 |
| Token budget | Tight-render vs amend? | **Amend the 300 ceiling** (NFR-01) | Constant change + eval re-baseline |
| Stated-profile store | New entity vs existing? | **Existing `sprk_userprofile` (add columns)** | No new entity; owner creates columns |
| Reader seam | Org-provider vs User-fragment? | **User-fragment path** | R1 does NOT implement `IOrganizationalContextProvider` |
| Push channel | SignalR / Web PubSub / SSE? | **Azure SignalR** (R1.5) | Researcher-backed; §12.5 |
| SNS after bare Q&A | Accept (a) or build (b)? | **(a) — author Q&A to dispatch a list capability** | No new machinery for bare Q&A |
| Notification spine | Suggestion-only or general? | **General `kind`-typed spine** (R1.5) | Prevents a future second push mechanism |
| Authoring reviewer | Who owns tool-description/chip content? | **Product owner (Ralph) reviews** | FR-J1 sign-off = owner; maker-per-Binding + owner review |
| `sprk_userprofile` relationship | Lookup direction (technical call, delegated)? | **B — lookup on `sprk_userprofile → systemuser` + alternate key** | Keyed upsert, 1:1 enforced, no OOB-table dependency (FR-E1/E2) |
| Constrained-field resolver | What is it / how? | **Deterministic proposal→closed-set matcher; BFF `Services/Ai`; smart pre-seed** | Fully specified (FR-B1); new piece = the matcher only |
| `sprk_userpreferences` bug | Recreate singular, or fix client? | **Keep plural; fix the ~6 client references (no schema change)** | Not an R1 dependency; logged defect |

## Assumptions

- **Create-flow set**: R1 covers matter/to-do/event only; invoice/project/report-card/etc. are later. (Design names these three.)
- **Option-set values**: the `sprk_primaryrole`/`sprk_practiceareas` starter values in design.md §6 are provisional; owner finalizes the taxonomy at column-creation.
- **Token ceiling target**: size the amended `EnvelopeBudget.User` from the actual rendered profile fragment (expected ~500–700); confirm during implementation.
- **SNS card shape**: reuse `OutcomeCard` v1 unless a parallel shape is justified (root §11).
- **`appnotification` mirror**: R1.5 decision, not R1.

## Unresolved Questions

*All four design-time unresolved questions were resolved on 2026-07-15 (see Owner Clarifications). None remain blocking.*

- [x] **Authoring reviewer** → **Product owner (Ralph)** reviews maker-authored `sprk_tooldescription`/`sprk_chiptransitions` (FR-J1).
- [x] **`systemuser ↔ sprk_userprofile` relationship** → **B: lookup on `sprk_userprofile → systemuser` + alternate key** (keyed upsert, 1:1 enforced, no OOB dependency; FR-E1/E2).
- [x] **Constrained-field resolver** → fully specified: deterministic proposal→closed-set matcher, BFF `Services/Ai`, smart pre-seed; new piece = the matcher only (FR-B1).
- [x] **`sprk_userpreferences` bug** → keep plural; fix the ~6 client references (no schema change); log as a defect (`project-defer-issue-tracking`); not an R1 dependency.

*Remaining implementation-time confirmations (non-blocking):* finalize `sprk_primaryrole`/`sprk_practiceareas` option-set values at column-creation; confirm the amended `EnvelopeBudget.User` value from the rendered fragment length.

---
*AI-optimized specification. Original design: `design.md`. R1.5 (proactive push) fully designed therein, not decomposed here.*
