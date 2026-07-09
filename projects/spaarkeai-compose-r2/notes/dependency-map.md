# Compose R2 ↔ Core R2 — Dependency & Parallelization Map

> **Created**: 2026-07-08 · **Purpose**: coordination artifact between `spaarkeai-compose-r2` (satellite) and `spaarke-ai-architecture-redesign-r2` (core).
> Core R2 setup is finalizing; it authored this project's initial design.md. This map is two-way: it lists what Compose can build independently AND what Compose needs from the core's Phase A0 contracts.

## 🟢 Independent — Compose builds now (no core contract)

| Track | FRs | Reuses (as-built) |
|---|---|---|
| Spikes (all) | Spike 0–8 | dispatch seam, Open XML SDK, docx-benchmark |
| Entry paths 1a/1b | FR-01, FR-02, FR-03 | `docxBytes` mount seam, `SendWorkspaceArtifactHandler`, 1c load path |
| Create-on-save pipeline | FR-05 (pipeline), FR-06a | `PromoteIfEphemeralAsync`, SPE/Dataverse plumbing |
| LLM editing services (BFF) | FR-19, FR-20, FR-21, FR-22, FR-23 | new `Services/Compose/*` (deterministic) |
| Inline UX (non-ledger) | FR-14, FR-15, FR-18 | TipTap BubbleMenu, ProseMirror marks |
| Word DOCX shuttle | FR-24, FR-25, FR-26, FR-27 | Open XML SDK, SPE webhooks, `StaleCheckoutSweeperHostedService` pattern |
| Memory (existing seam) | FR-29, FR-31, FR-33 | session ledger, compacted digest |

## 🔴 Blocked on core Phase A0 contracts

| Blocked FR | Core contract needed |
|---|---|
| FR-04 draft-into-editor | `ComposeDisposition v1` + SSE frame |
| FR-16 pending-redline, FR-17 undo/replace | `ComposeDisposition` (ledger materialization + supersession) |
| FR-07–FR-13 (5 catalog rows) | invoke/dispatch seam + triple-twin description hoist + eval-gate |
| FR-30 memory writes | `memory.write` gated tool + workspace-scope MemoryItem |
| FR-32 trace hosting | `TraceEvent v1` + D-F4 decision-traceability view |
| FR-34 UI ack | D-F3 ack-on-frame-id contract |

## 🟡 Splittable — independent half now, gated half later

| FR | Independent half (now) | Gated half (waits) |
|---|---|---|
| FR-05 create-on-save | container→record→profile→indexing pipeline | OutcomeCard rendering (`JobAwareCompletionState`) |
| FR-28 push/save | deterministic DOCX push + SPE save | gate Tier-2c dialog + OutcomeCard |
| FR-34 coordination | PaneEventBus choreography (6 flows) | ack-on-frame-id (D-F3) |

## Contract requirements Compose hands to the core (co-design)

Compose is the **primary consumer** of these — the core should shape A0 to satisfy them so no v2 is needed:

1. **`ComposeDisposition` SSE frame** MUST carry the structured edit payload (`target_text`/`new_text`/`match_mode`/rationale/sources) AND `{bindingId}@t{n}` provenance, so the Workspace can materialize a pending track-change from the stored ledger entry (FR-16) and supersede it on undo (FR-17).
2. **`JobAwareCompletionState`** MUST express per-step states matching create-on-save: `container` → `record` → `profile-analysis` → `indexing`, each queued/running/partial/completed/failed (FR-05).
3. **`GateDecision v2` Tier 2c** MUST host the create-on-save **optional parent-association prompt** (matter/project/invoice/work-assignment/none) inside the one dialog (FR-05, FR-28) — no bespoke banner.
4. **`memory.write`** MUST accept the AI-derived-insight governance envelope (tenant, workspace scope, provenance, trust level, expiration) for FR-30.
5. **`TraceEvent v1` / D-F4 view** MUST be host-embeddable in the Compose Context pane (FR-32).
6. **triple-twin hoist** MUST be ready before Compose authors its 5 catalog rows so they consume the hoisted description source, not become a 4th hand-maintained twin (FR-12).

## Sequencing recommendation

1. Run the 🟢 track immediately (Phase 0 spikes + Phase 2 services + entry paths + create-on-save pipeline + DOCX shuttle).
2. Hand the core the contract-requirements list above so A0 ships Compose-consumable.
3. Start 🔴/🟡-gated tasks only after core A0 publishes — task-create marks them `blocked-on: core-A0`.
