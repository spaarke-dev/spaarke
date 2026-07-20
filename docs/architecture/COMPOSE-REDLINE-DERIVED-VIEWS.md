# Compose Redline Derived Views Architecture

> **Last Updated**: 2026-07-19
> **Purpose**: Define WHERE the visual and evidentiary *views* of an AI redline suggestion (the rendered diff, the `confidence_band`, character offsets) are computed in the Spaarke AI compose-draft (redline) pipeline — and why they are derived **client-side at render**, not server-side and not stored in the ledger.
> **Source ADRs**: [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) (AI facade / envelope-only ownership) · [ADR-040](../../.claude/adr/ADR-040-session-ledger.md) (session ledger / render-follows-store) · [ADR-039](../../.claude/adr/ADR-039-grounded-execution-closed-catalogs.md) (grounded execution, no false precision)
> **Project origin**: `spaarkeai-compose-r3` (FR-13 confidence band, FR-16 offsets; supersedes the earlier "server-derived `confidence_band`" wording in FR-13)

---

## Overview

In Spaarke's compose (redline) pipeline, an AI capability authors a **redline suggestion** — a structured proposal to insert/replace/delete text in a Word document the user is editing. That suggestion is written to the append-only session ledger (ADR-040) as an **opaque payload** and shipped to the client without the platform ever parsing it (ADR-013 envelope-only ownership). The client materializes it into the live TipTap editor as a visible tracked change.

A redline suggestion has a durable part and several **derived views** of that part:

- **Durable** — the AI-authored payload: `target_text`, `new_text`, `match_mode`, `rationale`, `sources`, `edits[]`, `comments[]`, `paraId`. This is the single source of truth. It lives in the ledger.
- **Derived views** — the rendered visual diff (insertion/deletion marks, rationale chip, source chips), the `confidence_band` (`high | medium | low`), and precise character offsets. These are **projections** computed from the durable payload **plus the live editor document**, recomputed on every materialize.

**The decision this document records**: *derived views of a redline are computed client-side, at render/materialize time, against the live document — never derived server-side and never stored in the ledger.* Specifically the `confidence_band` (FR-13) and offsets (FR-16) derive on the client, superseding the earlier FR-13 "server-derived confidence_band" wording. The durable ledger payload remains opaque end-to-end.

This is not a compose-only quirk. It is the redline-shaped instance of the platform's **render-follows-store** rule (ADR-040): the ledger holds durable state; every surface re-materializes its own presentation from that state.

---

## The principle

> **Derived views of a redline (visual diff, confidence band, offsets) are a projection recomputed on the client at materialize time from (a) the durable opaque payload and (b) the live editor document. They are not durable state, so nothing about them belongs in the ledger, and they are not computed on the server, because half their inputs only exist on the client.**

Two properties make this the correct placement rather than a convenience:

1. **The evidence only converges at the client.** The band is a deterministic function of two input classes: (a) *grounding fields inside the opaque payload* — `sources`, `match_mode`; and (b) *document-relative signals* — does `target_text` still resolve in the current document? what is the re-anchor score? Class (b) requires the **live** document. On the server's compose-outputs READ path only the ledger is in hand, not the document. The server would have to re-fetch and re-parse the SharePoint Embedded `.docx` (duplicating what the client already holds in memory) and would *still* be stale the instant the user edits — a suggestion whose target the user just deleted must drop to `low`, and only the client sees that live.

2. **The band and offsets are the same kind of thing as the rendered diff.** The client already derives the entire visual redline — insertion/deletion marks, rationale, source chips — from the opaque payload at materialize time (`usePendingRedline`). The confidence band and offsets are additional derived fields in that *same* derivation. Client placement is architecturally consistent; a server-side band would be a lone special case reaching for inputs it does not have.

The band is a **deterministic derivation, not a model self-report.** ADR-039 forbids a model-emitted false-precision confidence number. A pure function over grounding evidence is not a model emission regardless of which runtime computes it — and only the client can additionally check *real* live doc-resolvability. "Grounded, not guessed" is therefore better served client-side.

---

## Component Structure

| Component | Path | Role in this decision |
|---|---|---|
| `OutputRouter` (Compose case) | `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs` (~L265) | WRITE path. Stores the compose `SessionOutput` and returns it; **NEVER parses the opaque payload** ("the router stores + returns; it NEVER parses the opaque payload — Compose owns it"). Same pass-through leg as `Informational`. |
| `GetComposeOutputsAsync` / `ProjectComposeOutputs` | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (~L1257, ~L1295) | READ path. Projects `compose`-disposition ledger entries to the client DTO, skipping truncation markers. Ships `Payload` verbatim; logs identifiers + counts only (NFR-07). |
| `ComposeLedgerOutputDto` | `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/SessionLedgerEntries.cs` (~L186) | The wire DTO. `Payload` is an opaque `JsonElement`; the platform never parses it (ADR-013 / ADR-040 envelope-only ownership). Only the DTO's *outer* property names get camelCased — the nested payload is untouched. |
| `ComposeDisposition` | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ComposeDisposition.cs` | The compose disposition seam (the `DispositionValue` the router and projector match on). |
| `ComposeEditor` (`ComposeDraftPayload`) | `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (~L298) | Client mirror of the payload + the accept/reject popover. The editor performs the insertion and owns the rendered redline. |
| `usePendingRedline` | `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts` | Materialize-from-ledger. Resolves `target_text` against the **live** document (`match_mode` strict/first/all), renders the pending insertion/deletion pair, and is the natural home for the band + offset derivation (same live-document resolution pass). |

**What this decision removes from the server**: the now-unwired `DeriveConfidenceBand` / `ResolveParaIdAnchor` helpers and the `ComposeConfidenceBand` enum in `ComposeDraftDisposition.cs`, plus the orphan payload fields `confidence_band` / `start_offset` / `end_offset`. **`paraId` is kept** — it is a legitimate, durable anchoring input (E2 paragraph identity), not a derived view.

---

## Data Flow

The pipeline is **store-opaque → ship-opaque → client-derive**. The platform layer touches the payload three times and parses it zero times.

1. An AI capability produces a redline suggestion; the Binding's disposition is `Compose`.
2. **Store (opaque).** The ledger write persists the suggestion as an addressable `SessionOutput` (`{bindingId}@t{n}`) *before* any rendering (ADR-040 store-precedes-render). `OutputRouter`'s `Compose` case stores + returns; it does not parse the payload.
3. **Ship (opaque).** The client reads `GET /api/ai/chat/sessions/{id}/compose-outputs`. `ProjectComposeOutputs` selects `compose`-disposition entries, drops truncation markers, and returns each `Payload` as a verbatim `JsonElement`. No band, no offsets, no diff are computed here — the server has no document to compute them against.
4. **Derive (client).** `ComposeWorkspace` picks the current (highest-turn) compose output and calls the editor handle to materialize it. `usePendingRedline` resolves `target_text` against the **live** ProseMirror document, renders the insertion/deletion marks, and computes the derived views in the same pass:
   - **offsets** — the ProseMirror positions of the resolved span (meaningful only against the live paragraph identity / E2 `paraId`).
   - **`confidence_band`** — a deterministic function of grounding fields in the payload (`sources`, `match_mode`) AND live-document signals (did `target_text` resolve uniquely? re-anchor score?). A suggestion whose target no longer resolves drops to `low`.
5. **Re-derive on every materialize.** Because derived views are recomputed each time (on load, on refresh, after supersession, after the user edits elsewhere), they always reflect current document reality. A refresh re-reads the same durable ledger entry and re-derives — never a stale stored band.

The load-bearing inversion: the ledger is the single source of truth and is shipped opaquely; the *presentation of* a redline is a live client projection, not persisted alongside it.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Where derived views are computed | **Client, at materialize** | Half the inputs (live-doc resolvability, re-anchor score) exist only on the client; the other half ride in the opaque payload | ADR-040 (render-follows-store) |
| `confidence_band` (FR-13) source | **Client-derived, deterministic** (supersedes "server-derived") | Pure function over grounding evidence + live-doc check; not a model self-report | ADR-039 (grounded, no false precision) |
| Offsets (FR-16) source | **Client-derived against live doc** | Offsets are only meaningful against live paragraph identity (E2 `paraId`) | ADR-040 |
| Ledger payload handling | **Opaque end-to-end** — store, ship, never parse | Keeps the platform decoupled from compose-domain semantics | ADR-013 (envelope-only ownership) |
| What stays durable | The AI-authored payload incl. `paraId` | `paraId` is a durable anchor, not a derived view | ADR-040 |
| Rejected: parse-on-read | ❌ | Would make `ProjectComposeOutputs` interpret compose semantics — new Api/Ai → Services/Compose coupling; violates ADR-013 | ADR-013 |
| Rejected: bake-on-write | ❌ | Would store a band that is stale the instant the user edits; violates append-only-durable-truth intent | ADR-040 |

---

## ADR Alignment

Each ADR protects a property; client-side derivation **reinforces** all three (it does not trade them off). The rejected server-side alternatives are what would have compromised them.

| ADR | What it protects | Why client-side derivation aligns |
|---|---|---|
| **ADR-013** (AI facade / envelope-only) | The platform/ledger layer (`OutputRouter`, `ChatEndpoints`) never interprets domain payloads — it routes/stores/ships opaque blobs, so the BFF stays decoupled from AI-domain semantics | **Reinforced.** The server keeps shipping the payload opaque; zero new `Api/Ai` → `Services/Compose` coupling is introduced. The rejected parse-on-read / bake-on-write alternatives are exactly what would have breached the facade. |
| **ADR-040** (ledger / render-follows-store) | The durable ledger is the single source of truth; clients re-materialize from stored state; corrections are append-only | **Consistent.** The band/offsets are a derived projection recomputed on every materialize, not durable state — nothing about them belongs in the ledger. This is textbook render-follows-store. |
| **ADR-039** (grounded execution, no false precision) | Confidence is not a model self-report; grounding is real; no new dispatch path | **Preserved/strengthened.** A deterministic derivation over grounding evidence plus a *real* live doc-resolvability check — no model-emitted number, no new dispatch endpoint, no catalog change. |

---

## The NFR-06 shift (through-the-wire DoD)

NFR-06 requires a through-the-wire Definition-of-Done for pipeline changes. This decision **shifts where that proof lives** rather than weakening it. Because there is **no server wire-change for the band** — the server ships the same opaque payload it always did — the E2E proof for the band and offsets is a **client render test** (the band renders correctly from a materialized payload + live document, and drops to `low` when the target no longer resolves), *not* a BFF seam test. The DoD follows the computation: server-side ledger seam tests still cover store-opaque/ship-opaque; the derived-view assertions move to the client materialize path.

---

## Constraints

- **MUST** ship the compose ledger payload opaquely on both the write (`OutputRouter`) and read (`ProjectComposeOutputs`) paths — the platform layer MUST NOT parse compose-domain fields.
- **MUST** derive `confidence_band` and offsets on the client at materialize time, as a deterministic function of payload grounding fields + live-document signals.
- **MUST NOT** store the band, offsets, or the rendered diff in the ledger (they are projections, not durable state).
- **MUST NOT** re-introduce a server-side band derivation (`DeriveConfidenceBand` and peers) unless a headless consumer earns it (see below).
- **MUST NOT** emit a model self-reported numeric confidence (ADR-039 false-precision ban).
- **MUST** keep `paraId` in the durable payload — it is an anchor input, not a derived view.
- **MUST NOT** add a new AI dispatch endpoint for confidence/offsets — they are additive client-side derivations, not a new capability (ADR-039).

---

## Future revisit trigger

The one scenario that would justify a server-side derivation path: **a headless, non-editor consumer of redlines** — e.g. a server-side audit, an email digest of "N low-confidence suggestions", or a compliance export. Such a consumer needs the band *without* a live editor, so a server path that loads the document to compute grounding-only signals would earn its place then. This is **YAGNI today** (every current consumer is the live editor). Recorded here as the explicit revisit trigger: if a headless redline consumer appears, re-open this decision — the band derivation would then be shared between a server (grounding-only) and the client (grounding + live doc), and the client remains authoritative whenever a live document exists.

---

## Related

- [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) — AI facade / envelope-only ownership (the platform never parses domain payloads)
- [ADR-040](../../.claude/adr/ADR-040-session-ledger.md) — session ledger, store-precedes-render, append-only corrections
- [ADR-039](../../.claude/adr/ADR-039-grounded-execution-closed-catalogs.md) — grounded execution, no false-precision confidence, one dispatch protocol
- [SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md](SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) — canonical AI architecture (three entry paths, session ledger, dispositions)
- `projects/spaarkeai-compose-r3/spec.md` — FR-13 (confidence band), FR-16 (offsets), NFR-04 / NFR-06
- `projects/spaarkeai-compose-r3/design.md` §6 — E3 enriched redline contract (rationale-first, grounding-tied band)
