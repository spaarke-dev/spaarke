# Core R2 → Compose R2 — A0 Contract Requirements RESPONSE

> **From**: `spaarke-ai-architecture-redesign-r2` (core) — Phase A0 contract authors
> **To**: `spaarkeai-compose-r2` (satellite) — Ralph Schroeder
> **Date**: 2026-07-08
> **Re**: `notes/HANDOFF-core-r2-A0-contract-requirements.md` (your requirements)
> **Status**: **Confirmed with deltas folded in.** Requirements accepted; 4 additions folded into core tasks; 1 item changed by an owner ruling (memory, in your favor).

Thank you — stating requirements up front is exactly right. Assessed all 8 against our A0 tasks (010–017 + 020/032/037/038/057). **All accepted**; the deltas below are now in the core task POMLs.

## Confirmed as-is
- **§3 OutcomeCard v1** — hosts job state + next-step chips. As specified.
- **§7 Triple-twin hoist before catalog rows** — ✅ **confirmed**: core task **020** blocks every catalog-row task by construction. Your 5 rows consume the hoisted source; coordinate timing so they're among its first consumers.
- **§8 D-F3 UI-ack** — ack tokens over `correlationId`, fail-honest on timeout. Core task **037**; the ack contract is published for your FR-34.

## Deltas we folded in (your requirements → our contracts)
1. **§1 ComposeDisposition — supersession + payload ownership.** Added **supersession semantics** (undo/replace = a *new* `compose` SessionOutput superseding the prior, addressable by `{bindingId}@t{n}`) to task **010**. Ownership boundary made explicit: **core owns the envelope** (disposition member + provenance + SSE ledger-ref frame + supersession); **you own the structured-edit payload schema** (`target_text`/`new_text`/`match_mode`/`rationale`/`sources`) inside the opaque SessionOutput payload — we will NOT bake editor semantics into the platform contract. SSE frame carries **ledger_ref + disposition, not the payload** (storage-precedes-rendering).
2. **§2 JobAwareCompletionState — consumer-declared steps.** Task **014** now makes the ordered step SET **consumer-declared**; your `container → record → profile-analysis → indexing` is supported (not hardcoded). Integrates the existing Job Contract / `ServiceBusJobProcessor` for real long-running state.
3. **§4 GateDecision v2 — association picker in the one dialog.** Tasks **012 + 032**: the single gate dialog **hosts an optional parent-association picker** (associate-to matter/project/invoice/work-assignment/**none** — a standalone Document is valid). No bespoke banner. Container is resolved deterministically (not prompted), as you noted. Tier 2c preview content lives inside the one dialog.
4. **§6 D-F4 — host-embeddable.** Task **038**: the decision-traceability view is **host-embeddable in an arbitrary container** (your Context pane), not bound to the chat surface. `TraceEvent v1` over `ToolChain`.

## Item changed by owner ruling — in your favor (§5 memory)
Your handoff asked `memory.write` to **gate** untrusted-origin writes. Between your handoff and this response, the operator ruled that **memory writes are AI-initiated, silent, and provenance-tagged — no gate** (automatic memory is the value proposition; explicit-only/gated writes were judged over-engineering for this stage). So your **FR-30 is *more* unblocked than you asked**: persist AI-derived insights directly via `memory.write` with provenance (`source: ai-derived`, `bindingId`, `trustLevel`) — no confirmation step.
- **Governance envelope** (§5.1): accepted — `tenant`, `scope`, `provenance` (bindingId/ledgerRef), `trustLevel`, `expiration` all on `MemoryItem v1` (task 016).
- **§5.3 AnchoredAnnotation** — **accepted**: it is document-positional UI state, NOT a MemoryItem. Keep it in your session payload; no negotiated sub-type needed.
- **Deferred (both projects)**: untrusted-origin ban, poisoning evals, semantic-retrieval trust boundary, litigation-hold → separate governance project. Interim defense = content-safety/PromptShield + scope isolation + the user review/delete surface. *Residual document-injection-poisoning risk is an accepted operator decision.*
- **Terminology**: your "workspace-scope memory" = our **Record scope** `(entityType, entityId)`. Let's use "Record scope" going forward.

## Your three asks — answered
1. **Confirm shapes / negotiate**: confirmed; deltas folded in (above).
2. **A0 timing**: A0 is our **first execution wave** (parallel with D-F0), but **execution has not started — core is at its plan-review gate.** Once cleared: 7 contracts, opus, ~1 day each, ≤6 parallel → publishable within roughly the **first week**, **010 + 014 first** per your priority. No calendar date until execution starts.
3. **Triple-twin hoist before catalog rows**: ✅ confirmed (task 020).

## How you'll know a seam is ready (the mechanism you asked about)
Watch **`projects/spaarke-ai-architecture-redesign-r2/notes/SEAM-STATUS.md`** — a live dashboard. Each seam row flips 🔲→✅ (with commit ref) as its publishing task completes; you consume each **as it lands** (incremental, don't wait for all seven). Task **017** posts the consolidated **"Compose UNBLOCKED"** notice when the last seam publishes. We'll also comment on your project tracking at that milestone.

## What we hold you to (your commitments, reaffirmed)
- Consume A0 contracts **as published** — no local variants (charter §3.4); the one accepted Path-A exception is `AnchoredAnnotation`.
- No new AI dispatch endpoint / no string-key routing (ADR-039); dispatch through the shipped session-dispatch seam.
- Ship eval cases (≥5 golden + dispatch) with every catalog row; pass `OpenAiFunctionSchemaValidator`.

*Source of truth (core): `spec.md` FR-A0-01..08 + FR-A1-03 + FR-B-08 + SEAM-STATUS.md. Contact: core-r2 authors.*
