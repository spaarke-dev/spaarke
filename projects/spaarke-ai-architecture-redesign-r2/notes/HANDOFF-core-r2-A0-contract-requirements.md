# Phase A0 Contract Requirements — from Compose R2 (primary consumer)

> **From**: `spaarkeai-compose-r2` (satellite) — Ralph Schroeder
> **To**: `spaarke-ai-architecture-redesign-r2` (core R2) — Phase A0 contract authors
> **Date**: 2026-07-08
> **Status**: coordination request (Compose planning complete; spec.md + plan.md ratified)

## Why this document

> *Because Compose is the primary consumer of `ComposeDisposition` and `JobAwareCompletionState`, and core R2 is still finalizing, we should hand the core our exact contract requirements now.*

Compose R2 is the **first and primary consumer** of several core Phase A0 contracts. Rather than have Compose wait, then discover the published shapes don't fit (forcing a v2), this document states Compose's exact requirements **up front** so A0 ships **Compose-consumable on day one**. Compose commits to consuming these contracts as published (no local variants, per charter §3.4) provided A0 satisfies the requirements below.

Compose builds ~20–25 days of **independent** work in parallel (spikes, LLM-pattern BFF services, Word DOCX shuttle, entry-path wiring, create-on-save pipeline) — see `notes/dependency-map.md`. The items below are the **only** things Compose is blocked on.

---

## 1. `ComposeDisposition v1` (+ SSE frame) — HIGHEST PRIORITY

Compose's inline AI edits (Draft Alternative, FR-09/16/17) are **ledger-first** per ADR-040: the structured edit is written as a `SessionOutput` with a `compose` disposition **before** any rendering; the Workspace materializes the pending track-change **from the stored ledger entry**.

**Requirements:**
1. **Disposition enum member**: add `compose` to the SessionOutput disposition set.
2. **Payload shape** the `compose` SessionOutput carries (or references) — the structured edit contract:
   ```json
   {
     "target_text": "string",
     "new_text": "string",
     "match_mode": "strict | first | all",
     "rationale": "string",
     "sources": [{ "type": "string", "id": "string", "snippet": "string" }]
   }
   ```
3. **Addressable provenance**: the entry is addressable as `{bindingId}@t{n}` so the Workspace can (a) tag the rendered suggestion with provenance and (b) **supersede** it on "undo that / try another approach" (FR-17).
4. **SSE frame**: a frame that signals "compose output ready, ledger_ref = X" — carrying the **ledger reference + disposition**, NOT the payload as the source of truth (storage-precedes-rendering). The client materializes from the ledger entry.
5. **Supersession semantics**: undo/replace = write a **new** `compose` SessionOutput that supersedes the prior one (addressable by ref); the Workspace re-materializes from current ledger state. No client-side-only DOM undo.

**Blocks**: FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace).

---

## 2. `JobAwareCompletionState v1`

Compose's **create-on-save at full ingestion parity** (FR-05) is a multi-step pipeline whose per-step status the user must see distinctly ("the record exists" vs "profile analysis / indexing finished").

**Requirements:**
1. **Per-step states**: `queued` / `running` / `partial` / `completed` / `failed`.
2. **Consumer-defined ordered step set** — Compose's create-on-save steps are: `container` (resolve SPE container from the user's business unit) → `record` (create `sprk_document`) → `profile-analysis` → `indexing`. The contract must let a consumer declare its own ordered steps (or explicitly support these four).
3. **Integration** with the existing Job Contract / `ServiceBusJobProcessor` status so long-running steps (profile/indexing) report real state.
4. Renders as an **`OutcomeCard`** (see §3).

**Blocks**: FR-05 OutcomeCard rendering, FR-28 push/save completion. *(The create-on-save pipeline itself is independent and Compose builds it now; only the card rendering waits.)*

---

## 3. `OutcomeCard v1`

Completion evidence in the transcript for push / save / create.

**Requirements:**
1. Hosts a `JobAwareCompletionState` (per-step) OR a single-shot success/failure.
2. Supports **next-step chips** (coordination-prompt pattern) — e.g., after create, "open in Compose" is already automatic, but the card should support follow-up affordances.

**Blocks**: FR-05, FR-28.

---

## 4. `GateDecision v2` / Confirmation Policy v2 (Tier 2c)

Compose's push-annotations, save-back, and document-creation are **Tier 2c side effects** (document versioning/creation) → the **one** gate dialog.

**Requirements:**
1. Tier 2c preview/confirm — preview content lives **inside** the one dialog (for push: "what appears in Word vs stays in Compose only"; for create: what will be created).
2. **Hosts the create-on-save optional parent-association prompt** inside the one dialog — user picks associate-to **matter / project / invoice / work-assignment / none** (a standalone Document is valid). No bespoke confirmation banner anywhere (bespoke confirm UX is the friction class Policy v2 kills).
3. Container is **not** prompted — it is resolved deterministically from the user's business unit.

**Blocks**: FR-05 (association prompt), FR-28 (push/save confirm).

---

## 5. `memory.write` gated tool + workspace-scope `MemoryItem`

Compose persists AI-derived insights as governed workspace-scope memory (FR-30).

**Requirements:**
1. **Governance envelope** accepted on write: `tenant`, `scope` (workspace), `provenance` (bindingId / ledgerRef), `trustLevel` (AI-derived / untrusted-origin), `expiration`.
2. **Gates untrusted-origin writes** (memory-poisoning prevention) — AI-derived content persisting itself is a Policy-v2-visible, governed side effect.
3. Compose consumes the published `MemoryItem v1` contract — **no local variant**. (Note: Compose's `AnchoredAnnotation` is argued as NOT a MemoryItem — it is document-adjacent positional UI state; see Compose spec §ADR Tensions Path-A. If the core rejects that argument, Compose requests a negotiated MemoryItem sub-type rather than a silent local variant.)

**Blocks**: FR-30.

---

## 6. `TraceEvent v1` + D-F4 decision-traceability view

Compose's Context pane **hosts** the core's trace view (FR-32) — it does NOT invent its own "LLM reasoning trace" rendering.

**Requirements:**
1. `TraceEvent v1` over the trace ledger (`ToolChain` entries).
2. The **D-F4 decision-traceability view** must be **host-embeddable** in an arbitrary container (the Compose Context pane), not bound to the chat surface.

**Blocks**: FR-32.

---

## 7. Triple-twin description hoist (core Phase A, BEFORE catalog rows)

Compose authors **5 new Action + Binding rows** (FR-07–13). Per the core charter, the three hand-maintained description twins (live catalog row ↔ handler metadata ↔ seed mirror) are hoisted to **one authored source with validated mirrors** in core Phase A, before any catalog-row task.

**Requirement:** the hoist must land **before** Compose authors its rows, so Compose's 5 rows consume the hoisted source and are NOT a 4th hand-maintained twin. Compose coordinates timing so its rows are among the hoist's first consumers.

**Blocks**: FR-12 (catalog authoring quality).

---

## 8. D-F3 UI-ack contract

UI-affecting tool results (open Compose tab, apply-edit rendering, navigation) complete only on a **client acknowledgment event referencing the emitted frame id**, or **fail honestly on timeout** (r1 finding R2-D: the model claimed UI actions that never happened).

**Requirement:** PaneEventBus `correlationId`s upgrade to ack tokens the server waits on. Compose consumes this for FR-34.

**Blocks**: FR-34 (UI ack; the PaneEventBus choreography itself is independent).

---

## What Compose asks of the core

1. **Confirm the shapes above** (or negotiate deltas) so A0 is Compose-consumable — ideally before Compose finishes its independent tracks (~3–4 weeks).
2. **Publish a Phase A0 timing estimate** — Compose sequences its gated tasks (`blocked-on: core-A0`) behind it.
3. **Confirm the triple-twin hoist lands before catalog-row authoring** (§7).

## What Compose commits in return

- Consume all A0 contracts **as published** — no local variants (charter §3.4), with the single argued Path-A exception (`AnchoredAnnotation`, §5).
- No new AI dispatch endpoint, no string-key routing (ADR-039); all 5 actions dispatch through the shipped session-dispatch seam.
- Ship eval cases (golden + dispatch, ≥5 each) with every catalog row; pass `OpenAiFunctionSchemaValidator`.

---

*Source of truth: `projects/spaarkeai-compose-r2/spec.md` + `plan.md` + `notes/dependency-map.md`. Contact: Ralph Schroeder.*
