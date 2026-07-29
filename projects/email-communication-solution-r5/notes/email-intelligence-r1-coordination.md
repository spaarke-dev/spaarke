# Coordination: `email-communication-intelligence-r1` ↔ `email-communication-solution-r5`

> **Authored**: 2026-07-28 by `email-communication-intelligence-r1` (design phase). Shared into r5 notes at operator request; r5 will surface/action items in its project.
> **TL;DR**: r1 is the **backend intelligence + record-write layer** (association intelligence, triage classification, proposed record updates, email-triggered actions, audit). r5 is the **surface layer** (reading pane, association review, proposed-update/action confirm). They meet at a **data + confirm contract**. This doc lists what must be agreed, and flags decisions that block either side.
> **Companion**: [`email-review-ux-research-synopsis.md`](email-review-ux-research-synopsis.md) (competitive UX research + the 7 review states + 2 design concepts).

---

## 1. Boundary (who owns what)

| Concern | Owner | Notes |
|---|---|---|
| Association engine (rungs 0–5), Noisy-OR, auto-file kill-switch, provenance | **r4 (shipped)** — r1 *extends* | r1 adds the 7-entity identifier rung; adds no new engine |
| Triage classification (category / urgency / obligations / summary / priority) | **r1** | New `prompted` Action `TRIAGE-EMAIL@v1` (Action + Binding — see §5) |
| RI-confidence score (drives proactive Task/notification) | **r1** | Closes the hardcoded-0 gap |
| Proposed record-field updates (Job B) + email-triggered actions/deadlines (Job C) | **r1** | Produces the proposals; writes on confirm via `IActionSeam` |
| Per-email review audit (`sprk_emailreviewlog`) | **r1** | Machine + human review rows |
| Reading pane, `.eml` render, threaded view, attachments view, compose/reply | **r5 (shipped)** | — |
| Association **review UI** (confirm/change which record) | **r5** | r5 already ships an associations/tracking view; r1 supplies the ranked suggestions + provenance |
| Proposed-update / action **confirm UI** (the Copilot-style per-field card) | **r5** | r1 supplies the proposal payloads; r5 renders + calls back to apply |
| The **Exceptions Queue** surface (research Concept 1) | **open** — recommend r5 | r1 can supply the queue data; the surface is r5's domain |

**Design principle both sides honor** (research + r1 D-5.5): *auto-file only the confident and keep it out of sight; queue the exceptions; suggest-then-confirm; rank-don't-score; cite everything; audit + reversible.*

---

## 2. The data contract r5 renders (per review state)

r1 produces, r5 renders + confirms. The seven states (A–G in the synopsis) reduce to four payloads:

- **Association suggestion** — ranked candidate records (entity, id, display, matched-on reason, deterministic vs AI, reinforced confidence *as an ordering/tier, not a number to display*), current status (Resolved / Suggested / Ambiguous / Pending / Unmatched), provenance pointer. *(Powers states A/B/C/G; already partly available via `POST /api/communications/{id}/suggest-associations`.)*
- **Intent classification** — `file-to-existing | update-existing | new-record-related-to`, plus the referenced/related record when intent is "related." *(Powers state D — the "new filing based on X" guard; NET-NEW, see §4.)*
- **Proposed record update** — target entity+id, field, old→new value, cited source text (email or attachment + locator), confidence tier, allow-list check. *(Powers state E; Copilot-for-Sales card shape.)*
- **Proposed action/deadline** — task/event(s), dates, cited source, "deadline-bearing → attorney-confirm" flag. *(Powers state F.)*

**Ask for r5**: confirm the associations/tracking view you shipped can consume a *ranked candidate list with reasons* (not just a single current association), and can host the two new card types (proposed-update, proposed-action). If not, that's the delta to size.

---

## 3. Thread inheritance — ALIGN (already in the engine)

**In the engine today**: `ThreadContinuityRung` (`Services/Communication/Engine/Rungs/ThreadContinuityRung.cs`, `Order => 1`) walks RFC-2822 ancestry (`In-Reply-To` → `References`, newest→oldest), finds the nearest ancestor `sprk_communication`, and **copies its regarding lookups verbatim at confidence 1.0**. So a reply / next email in a thread **inherits the parent's association** — this is the operator's "ladder-0-type" inheritance, self-contained in the engine (does **not** depend on r5).

**Alignment point**: r5 renders threaded conversations (card list / reading pane). The engine's *inheritance* key is RFC ancestry (`In-Reply-To`/`References`/`ConversationId`); the platform's *grouping* key is `sprk_communicationthread` (messaging-app r1/r2). r5's thread rendering should group on the **same** key the engine inherits on, so "what the user sees as one thread" == "what inherits one association." **Ask for r5**: confirm your thread grouping uses `sprk_communicationthread` / the same ancestry, so display and inheritance don't diverge.

---

## 4. Two NET-NEW requirements that touch r5's surfaces

1. **"Regarding" vs "related-to" intent (state D).** *"New filing based on PAT-908068"* must NOT auto-file onto PAT-908068. r1 classifies intent and, for `new-record-related-to`, proposes *creating* a record with the referenced one linked as **related**. r5 needs a card variant offering: *Create new (link X) / File onto X / Link as related*. The ADR-024 regarding model expresses "regarding" only — representing "related-to" distinctly is new; r1 owns the data, r5 owns the choice UI.
2. **Attachment-grounded extraction (state F, critical for IP).** The actionable fact often lives in the attachment (e.g. an Office-Action PDF), not the body. r1's triage/action Action grounds on extracted attachment text; r5's confirm card must **cite the attachment + locator** (e.g. "OA_908068.pdf p.1") so the reviewer can verify against the source r5 already renders.

---

## 5. Substrate note — build on Action + Binding, NOT the node engine

r1's intelligence + write path are **code-directed (Action + Binding, ADR-039)**. The node-graph playbook engine is **FROZEN (Insights-only)**. Any r5-side AI touchpoint should reach r1 capability via the published `Services/Ai/PublicContracts/` seams / the Action-Binding catalog — never the node executors or `IInvokePlaybookAi` (deleted). Record writes go through `IActionSeam.UpdateRecordAsync` behind the confirmation gate. In-flight (pin to current shape): **ADR-041** (judgment/confirmation gate), **ADR-043** (execution spine), **ADR-047** (notification spine).

---

## 6. Open decisions that block one or both sides

| # | Decision | Why it matters | Recommended | Owner |
|---|---|---|---|---|
| C-1 | **Auto-file policy: thread + explicit-ID only, or also identifier+sender-reinforced?** | Research: misfiling is the #1 trust-killer. Operator leans "auto-assign only within a thread." Today the engine auto-files any deterministic rung ≥0.85 (rungs 0–3). Narrowing to rung 0/1 makes rung 2/3 matches *suggested* (human-confirm). | Narrow auto-file to **rung 0 (explicit ID) + rung 1 (thread)**; demote participant/structural to Suggested | r1 + operator |
| C-2 | **Confirm-surface contract for Job B.** Email is session-less; the chat confirmation gate is session-scoped and can't host an email-originated confirm. So proposals must be **stored** and confirmed on a surface the user visits. | Determines the r1↔r5 apply contract | r1 stores pending proposals + exposes an **apply endpoint** (`→ IActionSeam.UpdateRecordAsync`, OBO, audited); r5 renders the card + calls apply on Approve | r1 + r5 |
| C-3 | **Who builds the Exceptions Queue (Concept 1)?** | It's the "make review very easy" surface; r5 owns surfaces but r1 owns the queue data | r5 builds the surface; r1 supplies the queue feed | r5 + operator |
| C-4 | **Job B allow-list home** — where the per-entity updatable-field allow-list lives | Governs what updates can even be proposed | New config table vs reuse Field Mapping Framework — r1 to decide | r1 |
| C-5 | **RI-confidence semantics** — association-certainty × urgency blend vs association-only vs urgency-only | Governs what fires a proactive Task/notification | association-certainty × classification-urgency blend | r1 |

---

## 7. What r1 needs FROM r5 (asks)

1. Confirm the associations/tracking view can render a **ranked candidate list + reasons** and host the two new card types (proposed-update, proposed-action) — or size the delta.
2. Confirm thread **grouping key** matches the engine's inheritance ancestry (§3).
3. Decide C-2 (apply-endpoint contract) and C-3 (Exceptions Queue ownership) with r1.
4. Surface C-1 (auto-file policy) to the operator alongside r1.

## 8. What r5 can rely on FROM r1

- A stable association-suggestion payload (ranked candidates + provenance) behind `/api/communications/{id}/suggest-associations` (extended for the 7-entity identifier rung).
- Triage output (category / urgency / obligations / summary / priority) on `sprk_communication` triage fields.
- Proposed-update and proposed-action payloads with cited sources.
- An apply endpoint that performs the write under OBO + writes the `sprk_emailreviewlog` audit row (per C-2).
- No new engine, no node-graph dependency; all via published seams.
