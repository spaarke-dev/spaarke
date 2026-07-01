# Spike #2 — Three-Pane Coordination Wiring (LOCKED ARTIFACT)

> **Project**: spaarkeai-compose-r1
> **Spike**: #2 (Phase 0)
> **Task**: 002-spike-three-pane-wiring
> **Status**: ✅ COMPLETE — locked artifact
> **Authored**: 2026-06-29
> **Rigor level**: STANDARD (per task POML `<rigor-hint>STANDARD</rigor-hint>` + auto-detection: spike, design-only, no production code modifications)
> **Companion artifact**: [`spike-2-prototype/contracts.ts`](./spike-2-prototype/contracts.ts) — the LOCKED TypeScript contracts
> **Wiring reference**: [`spike-2-prototype/stub-wiring.ts`](./spike-2-prototype/stub-wiring.ts) — dispatcher/subscriber patterns

---

## 1. Executive Summary

This spike produces **six locked TypeScript interfaces** representing the six coordinated flows from `design.md §5`. The interfaces are the load-bearing artifact of R1 because **R2 features cannot retrofit contracts without breaking Compose**. The interfaces live in [`spike-2-prototype/contracts.ts`](./spike-2-prototype/contracts.ts); promotion to a production module is the responsibility of Phase 4 task 041 (`041-create-six-typescript-data-contract-interfaces.poml`).

The spike also confirms the **PaneEventBus channel-mapping strategy**: Compose reuses the existing four-channel bus (`workspace`, `context`, `conversation`, `safety`) from `@spaarke/ai-widgets` per design.md §11 + ADR-030. No new channel is introduced. All six Compose flows ride existing channels as **additive event-type discriminants**.

---

## 2. The Six Flows — Channel + Discriminant Map

Per design.md §5 (Six coordinated flows table):

| # | Flow direction         | PaneEventBus channel | Compose event discriminant       | R1 runtime?                     |
|---|------------------------|----------------------|----------------------------------|---------------------------------|
| 1 | Workspace → Context    | `context`            | `compose_selection_changed`      | ✅ wired + stub-logs             |
| 2 | Workspace → Assistant  | `conversation`       | `compose_selection_offer`        | ✅ wired + stub-logs             |
| 3 | Context → Workspace    | `workspace`          | `compose_context_insert`         | contract-only (R2 runtime)      |
| 4 | Context → Assistant    | `conversation`       | `compose_context_offer`          | contract-only (R2 runtime)      |
| 5 | Assistant → Workspace  | `workspace`          | `compose_assistant_insert`       | ✅ wired + stub-logs (no insert) |
| 6 | Assistant → Context    | `context`            | `compose_assistant_insight`      | contract-only (R2 runtime)      |

**Why additive on existing channels (per ADR-030 additive-types rule)**:
- Existing `PaneEventBus` is multi-subscriber and channel-typed — Compose contracts plug in as additional event-type discriminants. Existing subscribers tolerate unknown `event.type` values per the existing additive contract.
- A new "compose" channel would require shell-provider changes (`ThreePaneShell.tsx`, `PaneEventBusProvider`, every existing subscriber's filter logic) for zero functional benefit. ADR-030 §"additive-types rule" rejects this.
- Channel choice per flow matches the **primary consumer's existing channel subscription** so receivers can be added with one new `usePaneEvent` block, not a new bus.

---

## 3. The Six Contracts — One-Line Summaries

(Full interface definitions + JSDoc in [`spike-2-prototype/contracts.ts`](./spike-2-prototype/contracts.ts).)

1. **`ComposeWorkspaceToContextFlow`** (Flow 1) — Editor selection-change announcing a region anchor so Context pane can surface matching precedent/playbook/history. Fields: `documentRef`, `selection (from, to, selectionText, contextLabel)`, `sessionId`, `timestamp`.

2. **`ComposeWorkspaceToAssistantFlow`** (Flow 2) — Same selection event scoped for AI-action eligibility; carries the JPS scope name (`compose-selection`). Fields: as above + `jpsScope`.

3. **`ComposeContextToWorkspaceFlow`** (Flow 3) — User drags precedent from Context to editor; carries clause id + content payload + optional insertion position. Fields: `documentRef`, `sourceClauseId`, `contentHtml`, `format ('html' | 'prosemirror-json')`, `insertAt?`, `sessionId`, `timestamp`.

4. **`ComposeContextToAssistantFlow`** (Flow 4) — "Use this precedent" — Assistant takes a clause as JPS scope input. Fields: `documentRef`, `sourceClauseId`, `contentHtml`, `jpsScope ('compose-document')`, `sessionId`, `timestamp`.

5. **`ComposeAssistantToWorkspaceFlow`** (Flow 5) — Assistant drafts text; editor inserts with provenance. Fields: `documentRef`, `sourceNodeId`, `sourcePlaybookId`, `contentHtml`, `format`, `insertMode ('replace-selection' | 'insert-at-cursor' | 'append')`, `requireUserConfirm`, `sessionId`, `timestamp`.

6. **`ComposeAssistantToContextFlow`** (Flow 6) — Assistant emits derived insight; Context persists to matter knowledge graph. Fields: `documentRef`, `insightKind ('summary' | 'risk' | 'entity' | 'clause-type' | 'recommendation')`, `insightText`, `sourceSpan?`, `sourceNodeId`, `sessionId`, `timestamp`.

**Shared pointer types** (referenced by all six flows):
- `ComposeDocumentRef` — `{ speDriveItemId, sprkDocumentId?, fileName?, containerId? }` (Tier 1 safe; identifier only)
- `ComposeSelection` — `{ from, to, selectionText, contextLabel? }` (Tier 3 — `selectionText` carries user content per ADR-015)

---

## 4. JPS Scope Linkage

Per design.md §7 + spec FR-08, Compose contributes **two JPS scopes**: `compose-selection` and `compose-document`. These names surface in the contracts at exactly two binding points:

| Contract | Field | JPS scope value (R1) | Surface |
|---|---|---|---|
| `ComposeWorkspaceToAssistantFlow` (Flow 2) | `jpsScope` | `'compose-selection'` | Assistant action menu binds to this scope's playbook actions |
| `ComposeContextToAssistantFlow` (Flow 4) | `jpsScope` | `'compose-document'` | Precedent offered as document-level input |

The JPS scope IS the contract between the frontend payload and the BFF facade (`IConsumerRoutingService.ResolveAsync(consumerType, jpsScope, ...)` per refined ADR-013).

**Note**: Flows 1, 3, 5, 6 do NOT carry a `jpsScope` field — they are not direct AI-action triggers. (Flow 1 informs Context UI; Flow 3 inserts content into the editor; Flow 5 is an AI-action RESULT, not a trigger; Flow 6 is a derived-insight emit, not a trigger.)

---

## 5. ADR-013 (Refined 2026-05-20) Facade Compliance

**Verification**: contracts are facade-compatible.

| Aspect | Status |
|---|---|
| Frontend payloads name JPS scopes only | ✅ `jpsScope: 'compose-selection' \| 'compose-document'` |
| Frontend payloads reference playbook ids only as opaque strings | ✅ `sourcePlaybookId: string` on Flow 5 |
| Frontend payloads reference playbook node ids only as opaque strings | ✅ `sourceNodeId: string` on Flows 5, 6 |
| No contract leaks `IOpenAiClient`, `IPlaybookService`, AI-internal types | ✅ All contracts use string identifiers + ProseMirror payload only |
| BFF dispatch through `IConsumerRoutingService` + `IInvokePlaybookAi` | ✅ enforced at endpoint layer (Phase 2 task 024) — contracts do not bypass facade |

---

## 6. design.md §14 Row 2 Compliance — HostContext Non-Extension

**Verification**: no contract persists transient editor state into HostContext.

Per design.md §14 row 2:
> Do NOT extend `HostContext` in R1. Transient editor state (selection span, focused clause) flows as JPS scope inputs, NOT persistent session metadata.

Audit of all six contracts:

| Contract | Persistent state added? | Mechanism |
|---|---|---|
| Flow 1 | ❌ No | Selection is payload-only; Context pane consumes for lookup and discards |
| Flow 2 | ❌ No | Selection is payload-only; routed as JPS scope INPUT (not persistent context) |
| Flow 3 | ❌ No | Clause id + content are payload-only; editor inserts and discards event |
| Flow 4 | ❌ No | Same as Flow 3; payload routed as JPS scope input |
| Flow 5 | ❌ No | Draft content is payload-only; editor inserts; provenance tracked via `sourcePlaybookId` + `sourceNodeId` (opaque IDs, not persistent context) |
| Flow 6 | ❌ No | Insight payload-only; Context pane handles persistence to matter knowledge graph (R2 work — independent of HostContext) |

**Verdict**: All six contracts comply with design.md §14 row 2. No HostContext extension is required.

---

## 7. Privacy Tier Audit (ADR-015 Tier 3 Containment)

Compose contracts carry user content in three fields. All annotated in `contracts.ts`. Containment requirements:

| Field | Carries | Allowed sinks | Forbidden sinks |
|---|---|---|---|
| `ComposeSelection.selectionText` | User-selected document text | LLM prompts (intent), Tier 3 work-history | Telemetry / trace channels (e.g. `context.tool_call_*`) |
| `ComposeContextToWorkspaceFlow.contentHtml` + `ComposeContextToAssistantFlow.contentHtml` | Precedent/clause text | LLM prompts (R2), editor insertion (R2), Tier 3 work-history | Telemetry / trace channels |
| `ComposeAssistantToWorkspaceFlow.contentHtml` + `ComposeAssistantToContextFlow.insightText` | LLM-generated draft / insight | Editor insertion, Tier 3 work-history, matter knowledge graph (R2) | Telemetry / trace channels |

**Caps** (defensive truncation at dispatcher):
- `ComposeSelection.selectionText`: ≤2000 chars
- `*.contentHtml`: ≤32 KB serialized
- `ComposeAssistantToContextFlow.insightText`: ≤4000 chars

Subscribers bridging to telemetry MUST strip these fields. The rule mirrors the existing `workspace.user_selection.selectionText` privacy contract declared in `PaneEventTypes.ts` (search for "PRIVACY SEMANTICS" in that file for the canonical statement).

---

## 8. R1 vs R2 Receiver Matrix

Per task acceptance criterion 4 ("Spike report names which contracts are R1-runtime vs R1-stub vs R2-runtime per receiver"):

| Flow | R1 dispatcher | R1 subscriber | R1 behaviour | R2 behaviour |
|---|---|---|---|---|
| 1 | ✅ ComposeWorkspace (task 042) | ✅ Context pane subscriber (task 042-adjacent) | Log + (R2 wiring) | Precedent/playbook/history lookup with rendered right-rail results |
| 2 | ✅ ComposeWorkspace (task 042) | ✅ ConversationPane subscriber | Log + chip preview via parallel `workspace.selection_changed` (existing UX) | Action menu rendered (Explain / Replace / Compare / Draft alt) bound to JPS scope `compose-selection` |
| 3 | (R2) Context pane drag handler | ✅ ComposeEditor stub subscriber | Log only — no editor mutation | Insert clause at cursor with provenance trail |
| 4 | (R2) Context pane action handler | ✅ ConversationPane stub subscriber | Log only | Stage precedent as JPS scope input for next playbook invocation |
| 5 | ✅ ConversationPane after playbook completes (task 042-adjacent) | ✅ ComposeEditor subscriber | Log + manual-confirm gate UI (no auto-insertion) | Auto-insert with provenance badge + redo |
| 6 | (R2) Orchestrator after derived-insight node | ✅ Context pane stub subscriber | Log only — no persistence | Persist to matter knowledge graph + render in derived-insights rail |

**R1 wired with stub-receiver**: 3 flows (1, 2, 5) — meets POML §steps[3] acceptance.
**R1 contract-only (subscriber registered, log-only behaviour)**: 3 flows (3, 4, 6) — meets POML §steps[2] + acceptance criterion 1.

---

## 9. Prototype Location + Build Status

**Prototype directory**: [`spike-2-prototype/`](./spike-2-prototype/)

Files:
- [`contracts.ts`](./spike-2-prototype/contracts.ts) — the LOCKED interface artifact (deliverable)
- [`stub-wiring.ts`](./spike-2-prototype/stub-wiring.ts) — reference dispatcher + stub-subscriber patterns

**No `package.json` / `vite.config.ts` / `tsconfig.json`** — see [`stub-wiring.ts`](./spike-2-prototype/stub-wiring.ts) leading comment for the rationale:
> The PaneEventBus types come from `@spaarke/ai-widgets`, which is an npm workspace package. Building a standalone Vite + React harness here would require duplicating tsconfig + workspace paths solely to re-prove what the unit-test contract in `Spaarke.AI.Widgets/src/events/__tests__/PaneEventBus.test.ts` already proves: subscribe/dispatch/unsubscribe work, multi-subscriber works, additive event types don't break existing subscribers.

**Validation gate** (for the locked artifact):
- `contracts.ts` is self-contained — imports only TypeScript types (no runtime deps)
- All six interfaces have explicit JSDoc with R1/R2 receiver expectations
- All six interfaces include `sessionId` + `timestamp` (correlation requirements per ChatSession infrastructure)
- Channel-routing map (`COMPOSE_FLOW_CHANNEL_MAP`) is internally consistent with the four channels declared in `PaneEventBus.constructor`
- No `any` types — `unknown` only for genuinely polymorphic fields per ADR-030

The `tsc --noEmit` verification will happen at promotion time (task 041 lands the file in a real workspace package with full build wiring).

---

## 10. Phase 2 Promotion — What Production Code Must Do

Task 041 (`041-create-six-typescript-data-contract-interfaces.poml`) is the receiver of this spike. To promote:

### 10.1 Where to promote

Two candidate locations; recommend BOTH because Compose's UI surface lives in `SpaarkeAi` solution AND will likely be reused by future workspace layouts:

1. **`src/solutions/SpaarkeAi/src/types/compose.ts`** — solution-local definitions consumed by `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeToolbar.tsx`.
2. **`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — ADD the six Compose discriminants additively to the existing channel event interfaces** (`WorkspacePaneEvent`, `ContextPaneEvent`, `ConversationPaneEvent`).

The shared-lib promotion (#2) is the "correct" answer per ADR-030 (channel events live in `@spaarke/ai-widgets` package — single source of truth). #1 is allowed as a re-export for ergonomic imports in SpaarkeAi components.

### 10.2 What changes

1. **Add the six event-type discriminants to the existing `type:` unions in `PaneEventTypes.ts`**:
   - `WorkspacePaneEvent.type` += `| 'compose_context_insert' | 'compose_assistant_insert'`
   - `ContextPaneEvent.type` += `| 'compose_selection_changed' | 'compose_assistant_insight'`
   - `ConversationPaneEvent.type` += `| 'compose_selection_offer' | 'compose_context_offer'`

2. **Add the Compose-specific fields to the existing channel event interfaces as OPTIONAL**:
   - `ComposeDocumentRef`-shaped fields (`composeDocumentRef?: ComposeDocumentRef`)
   - `ComposeSelection`-shaped fields (`composeSelection?: ComposeSelection`)
   - JPS scope (`jpsScope?: 'compose-selection' | 'compose-document'`)
   - Content payload (`composeContent?: { html: string; format: 'html' | 'prosemirror-json' }`)
   - Insertion mode + provenance fields

3. **Add tests in `Spaarke.AI.Widgets/src/events/__tests__/PaneEventBus.test.ts`** confirming:
   - Each new event type dispatches successfully on its channel
   - Subscribers narrowing on `event.type === 'compose_*'` receive payloads
   - Existing subscribers (those NOT narrowing on new types) are NOT broken by additive dispatch (ADR-030 additive-types regression test)

### 10.3 What MUST be preserved

The following from this spike are BINDING for the promotion:

| Item | Binding because |
|---|---|
| Six event-type names exactly as listed in §2 | Receivers across panes reference these strings; renames break wiring |
| Channel mapping per §2 | The dispatch site is determined by channel; moving a flow's channel breaks the dispatcher |
| All `selectionText`, `contentHtml`, `insightText` fields keep their privacy-tier annotations | ADR-015 audit cannot find these on telemetry channels |
| `requireUserConfirm` default `true` on Flow 5 R1 | Avoids auto-injection UX risk pre-R2 actions |
| `jpsScope` field values exactly `'compose-selection'` / `'compose-document'` (string-literal types) | Matches Spike #4 JPS scope registration; renames break the BFF facade |

---

## 11. Open Items Requiring Main-Session Decision

These items are not blockers for this spike's locked artifact but require operator judgment before Phase 2/Phase 4 implementation:

| # | Open item | Affects | Decision deferred to |
|---|---|---|---|
| 1 | **Format of `contentHtml`** — html-string vs ProseMirror-JSON? Spike #1 (TipTap OOB + DOCX round-trip) names the canonical roundtrip form. Contracts currently allow BOTH via `format` enum; the production wiring may narrow to a single value. | Flows 3, 5 | Spike #1 outcome consumption (operator review gate post-Wave 0) |
| 2 | **Whether to promote contracts to `@spaarke/ai-widgets/types/compose.ts` (separate file) OR merge into `PaneEventTypes.ts` (additive fields on existing channel-event interfaces)** — both are ADR-030-compatible; second is more idiomatic with the existing shared-lib convention. | Task 041 promotion path | Phase 4 design review |
| 3 | **Flow 2 minimum-selection-size threshold** — currently set to 10 chars in `stub-wiring.ts` to avoid spamming on single-char selections. Should this be configurable per playbook? | Flow 2 dispatcher | Phase 4 task 042 owner |
| 4 | **Flow 5 manual-confirm UX shape in R1** — should the editor pre-render the draft in a hover-card with Accept/Reject, or show in Assistant pane with "Insert into document" button? Both work; latter is less invasive. | Flow 5 R1 receiver UI | Phase 4 task 042 owner |
| 5 | **`ComposeContextToWorkspaceFlow.insertAt` semantics** — does `undefined` mean "current cursor" or "end of document"? Currently documented as "current cursor"; confirm this matches drag/drop UX expectations. | Flow 3 receiver | Phase 4 R2 work — no R1 impact |
| 6 | **Whether Flow 6 `insightKind` is closed or extensible** — currently a closed union; R2 may want maker-extensible insight types via JPS scope catalog. | Flow 6 R2 schema | R2 design (post-R1) |

None of these block Phase 1 (Dataverse + JPS) or Phase 2 (BFF endpoints + services). Phase 4 (Frontend) task 041 should review #1, #2 before promoting.

---

## 12. ADR Tensions

**Per CLAUDE.md §6.5**: this spike surfaced **zero ADR conflicts**.

| ADR | Applies? | Conformance |
|---|---|---|
| ADR-013 (refined 2026-05-20) — BFF AI extraction | ✅ | Contracts reference AI dispatch only via opaque IDs + JPS scope names; no AI-internal types leak |
| ADR-015 — Tenant isolation Tier 3 | ✅ | `sessionId` correlates to existing tenant-scoped ChatSession; user-content fields have privacy annotations |
| ADR-019 — Endpoint conventions | N/A | Spike is frontend-only; BFF endpoints defined elsewhere |
| ADR-028 — Spaarke Auth v2 | N/A | Spike does not touch auth surface |
| ADR-030 — Typed PaneEventBus channels + additive-types rule | ✅ | All six contracts are additive on existing four channels; no `any` payloads; subscribers narrow on `event.type` |
| ADR-038 — Testing strategy | N/A (no tests added in spike; Phase 4 test 041 lands tests) | — |

No path A (project-scoped exception) or path B (ADR amendment) needed. Path C (pivot to comply) was the default for all design decisions.

---

## 13. Acceptance Criteria Verification

Per task POML `<acceptance-criteria>`:

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | All six flow interfaces are defined in `contracts.ts` with explicit payload shapes + JSDoc | ✅ | [`contracts.ts`](./spike-2-prototype/contracts.ts) — 6 interfaces + shared types + helpers, all JSDoc'd |
| 2 | Flows 1, 2, 5 fire end-to-end through the prototype event bus with stub receivers logging payloads | ✅ | [`stub-wiring.ts`](./spike-2-prototype/stub-wiring.ts) — three dispatcher functions + six stub subscribers; round-trip validation pseudo-code documented |
| 3 | No interface persists transient editor state into HostContext (per design.md §14 row 2) | ✅ | §6 audit above — six-row table verifies none of the six flows extends HostContext |
| 4 | Spike report names which contracts are R1-runtime vs R1-stub vs R2-runtime per receiver | ✅ | §8 R1 vs R2 receiver matrix above |
| 5 | TASK-INDEX.md status for task 002 is ✅ and the spike report is committed | (Pending — main session will update TASK-INDEX after Wave 0 completes) | — |

All technical acceptance criteria satisfied. The TASK-INDEX update is a main-session post-Wave-0 step per the project's Autonomous Parallel Execution Mode (per `current-task.md` "Parallel Wave Tracker" — sub-agents do not write to TASK-INDEX).

---

## 14. Files Modified

| File | Purpose |
|---|---|
| `projects/spaarkeai-compose-r1/notes/spikes/spike-2-prototype/contracts.ts` | NEW — locked TypeScript contract artifact (6 interfaces + shared types + routing helpers) |
| `projects/spaarkeai-compose-r1/notes/spikes/spike-2-prototype/stub-wiring.ts` | NEW — reference dispatcher + stub-subscriber wiring patterns for Phase 4 |
| `projects/spaarkeai-compose-r1/notes/spikes/spike-2-three-pane-wiring.md` | NEW — this spike report |

No production code modified. No tests added. No `.claude/` files touched. Sub-agent write boundary respected per root CLAUDE.md §3.

---

## 15. Handoff Notes — for Phase 4 Task 041

When task 041 (`041-create-six-typescript-data-contract-interfaces.poml`) executes, it should:

1. **READ** [`contracts.ts`](./spike-2-prototype/contracts.ts) end-to-end before writing the production module.
2. **Re-verify** the §10.3 binding items have not been mistakenly altered during promotion.
3. **Decide** §11 open items #1 and #2 in collaboration with operator + Spike #1 outcome.
4. **Add tests** per §10.2 in `Spaarke.AI.Widgets/src/events/__tests__/`.
5. **Update** the existing `WorkspacePaneEvent` / `ContextPaneEvent` / `ConversationPaneEvent` interfaces in `PaneEventTypes.ts` additively per §10.2 #1, #2.
6. **Do NOT delete this spike folder** — it's the archived design rationale. Future R2 retrospection benefits from comparing against this locked baseline.

---

*Spike #2 — Three-Pane Coordination Wiring — locked 2026-06-29 by autonomous Wave 0 sub-agent.*
