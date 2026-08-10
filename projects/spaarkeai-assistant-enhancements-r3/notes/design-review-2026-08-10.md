# Design Review — design.md (rev with §5.5) — 2026-08-10

> **Reviewer**: external architecture review (owner-requested), grounded against `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, ADR-039, ADR-015, ADR-047, `ASSISTANT-UI-ELEMENT-CRITERIA.md`.
> **Verdict**: Design is strong and strategy-consistent. §5.5 (active-item handle, generalized from the SHIPPED Compose flow) is the right spine. **Approve into `/design-to-spec` after the edits below.** The one near-blocking item is internal consistency drift (§7.1); the rest is sharpening.
> **Action for this project**: apply the edits in §A below to `design.md`, holding the context in this project. Items in §B are spec-time obligations (carry into spec.md, not necessarily editable now).

---

## What's strong (keep as-is)

- **§1 grounding principle** — every fact from a tool that queries the source, never a widget snapshot. Correct fix for the #1 AI-app UX failure mode (stale/hallucinated narration). The §2 UAT table proves it empirically.
- **§5.5 active-item handle model** — generalizing the *shipped* Compose active-document flow (not speculative) is the best kind of design. The EMAIL worked example is build-ready. The "two tool kinds per widget" table (overview/query vs per-item action) is the concrete meaning of "capability parity."
- **§6.2 refinement** — "identity + active-item handle (id, never content)" resolves the real tension between grounding discipline and per-item interactivity.
- **Tool economy via ADR-039 `PreFilter`** — mounting only open tabs' tools through the *sanctioned* deterministic pre-filter, no second dispatch surface. On the rails.
- **Registration-enforced parity contract** — converts a convention into a structural guarantee. Right altitude.

---

## §A — Edits to make to design.md NOW (hold context in this project)

### A1 — BLOCKING: reconcile the "identity-only" drift (3 sections contradict §5.5/§6.2)

§5.5 and §6.2 now say **"identity + active-item handle."** Three downstream sections still assert the superseded strict "identity-only" model. A spec author reading §7/§11 would build identity-only and miss the handle.

- **§4, AWARENESS row** ("Build" column): currently "trim prompt to `{ type, label, active }`". Add the active-item handle — e.g. "trim prompt to `{ type, label, active }` **per tab + an active-item handle `{ id, type, label }` for the active/selected item** (id only, never content)."
- **§7, Phase 1**: currently titled "Awareness (**identity-only**)". Rename to "Awareness (**identity + active-item handle**)" and update the body from "trim prompt block" to include publishing/threading the active-item handle (the generalized active-item conduit from §5.5).
- **§11, ADR-015 bullet**: currently claims pure narrowing ("R3 *narrows* prompt exposure (identity-only)… for the live-selection hint only"). Update to be **honest**: R3 carries identity + an active-item **id** (not content) — still far tighter than R2 ambient content, but not pure identity-only. State it plainly rather than overclaiming the narrowing.

### A2 — Align INTERACTION to the registration-contract model (resolve §4/§7 vs §5.5 conflict)

§5.5 correctly puts per-item action cards + landing target in the **registration contract** (declared at registration, registry-enforced). But §4 and §7 still say the matrix is authored "into the prompt" — a prompt-authored matrix grows unbounded and can't be tested.

- **§4, INTERACTION row** ("Build" column): change "Author a **per-widget policy matrix into the prompt**" → "Declare the respond/direct/hybrid pattern **as a registration-contract field**; derive follow-ons deterministically from mounted parity tools + that field (not prose in the system prompt)."
- **§7, Phase 4**: change "publish the per-widget respond/direct/hybrid matrix" → "surface the per-widget respond/direct/hybrid pattern **from the registration contract**; derive follow-on chips from mounted tools + pattern." (The "derive follow-on chips from mounted tools + pattern" clause is already right — just remove the prompt-authoring implication.)

### A3 — Add an active-item lifecycle paragraph to §5.5

The template is load-bearing but silent on lifecycle. Add a short paragraph after the "Two tool kinds" table:

- **Single-active-item invariant** — one `{id,type,label}` at a time; define what "active" means on **multi-select** (recommend: a grid multi-select does NOT set a single active item / cards suppress, or the last-selected row wins — pick one).
- **Clear-on-deselect / tab-switch** — the handle clears when the item is deselected or its tab loses focus, so a card can't fire `draft_reply` against an id the user has navigated away from.
- Generalize from the Compose single-document lifecycle precedent rather than inventing new mechanics.

### A4 — Cite the card-economy standard in §5.5

§5.5 "auto-presents follow-on cards on selection… no typing." Good UX until selection-spam produces card stacks. Add a sentence citing the existing governing standard:

> Proactive cards obey `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` — collapse stacks behind one disclosure header; cards are for persistent act-on items, chips for throwaway turn-follow-ons.

### A5 — Disambiguate "proactive-card surface" from the ADR-047 notification spine in §5.5

§5.5 calls the card surface "proactive," but architecturally it is **reactive** (client-side, triggered by local selection) — NOT the server-initiated ADR-047 spine. The surface-launch doc is emphatic these channels must stay distinct ("do not build a second push channel"). Add one clarifying sentence:

> This is the **reactive/local-selection** card surface (client reacts to active-item selection) — distinct from the ADR-047 **proactive** notification spine (server-initiated push). Do not merge them.

### A6 — Add a second acceptance test to §8 (per-item action flow)

§8 only exercises the overview/query flow. §5.5's whole contribution is the per-item action flow — untested by the current DoD. Add:

> **Per-item DoD**: Select an email in the Email tab → a **Reply** card appears (no typing) → click → `SendEmailDialog` opens pre-filled with recipients + `Re:` subject + drafted body.

Two DoDs (one per tool kind in the §5.5 table) prove the contract's breadth.

---

## §B — Carry into spec.md (spec-time obligations, not design edits)

### B1 — Pre-commit against per-grid tool sprawl (reuse discipline / CLAUDE.md §11)

§5 Lane 1 says "a domain-shaped tool **per grid**" across 8 grids. Left as-is this becomes 8 hand-authored handlers, eroding §11. **Spec must pre-commit to ONE parameterized `configId`-driven overview tool** that executes the grid's existing query definition (saved-query/FetchXML) server-side over OBO — not N bespoke tools. This directly informs §12's open item "which parity tools are Bindings vs typed primitives" — resolve it toward the parameterized-reuse default.

### B2 — Verify the Email widget selection model exists (scoping risk)

§5.5 scopes "email widget publishes its selection as active context" as **New (small)**. This assumes the Email workspace widget already emits a reading-pane selection/active-item event. If no selection model exists in the tab today, "small" → "medium." Confirm before sizing the task.

### B3 — Resolve §12 open items with the §5.5 lens now available

- Widget-type (4) ↔ context-type (6) mapping table — needed for the registration contract (A1/A2 depend on it).
- Bindings vs `sprk_analysistool`+handler split — see B1.
- Live-selection hint: stays in prompt vs moves to `get_selection` tool — the §5.5 handle model argues for the tool (`get_selection` fetches by id), consistent with "id, never content."

---

## Strategy-fit summary (unchanged from prior review, still holds)

| Spaarke principle | Fit |
|---|---|
| ADR-039 one-decider / closed catalogs | ✅ preserved; no second dispatch surface |
| Surface identity in code, capability in data | ✅ parity tools are catalog rows; registry stays code |
| ADR-015 data governance | ✅ tightened vs R2 (id-not-content) — but §11 must state it honestly (A1) |
| BFF hygiene §10 / reuse §11 | ✅ in substance — guard the per-grid tool count (B1) |
| Reactive (surface-launch) vs proactive (ADR-047) separation | ⚠️ clarify in §5.5 (A5) |

---

## Suggested edit order for this project

1. A1 (drift reconciliation) — do first; everything else assumes the handle model is canonical.
2. A2 (interaction → registration contract).
3. A3–A5 (§5.5 lifecycle + card economy + spine disambiguation).
4. A6 (second DoD).
5. Note B1–B3 in `design.md` §12 (or a `## Open items → spec` addendum) so they survive into `/design-to-spec`.
