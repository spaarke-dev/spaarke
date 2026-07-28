# Design — Contextual AI Tool Library

> **Status**: DESIGN (agreed with owner 2026-07-27). Consumer #1 = NDA advisory review (this project).
> **Author**: ai-advanced-capabilities-nda-r1
> **Supersedes/extends**: the ad-hoc `ComposeAiToolbar` registry (`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeAiToolbar.tsx`).
> **Related**: root CLAUDE.md §10 (BFF hygiene — surface identity stays in code), §11 (reuse-first), ADR-039 (advisory tier), ADR-040 (ledger), ADR-049 (shadow doc). Sibling pattern: `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` (registry-in-code + data-in-catalog is the same shape).

---

## 1. The problem

Spaarke is building a series of **analysis/advisory verticals**. NDA review is the first; "Case-law research", "Contract review", "Diligence" follow. Each vertical wants a **different set of inline AI "tools"** surfaced in the SAME Compose surfaces:

- **BubbleMenu** (text selection) — "Explain", "Draft alternative", "Make more concise"…
- **Review-Note ⋮ menu** (advisory-comment gutter) — "Draft compliant alternative", "Describe a change"…
- (future) **whole-document** overflow, **Assistant chip**.

Today the tool set is a single hard-coded `DEFAULT_ACTIONS` array. Every vertical would either (a) fork the array, or (b) pile all verticals' tools into one menu. Neither scales. The owner's ask, verbatim:

> "for the AI bubble menus, ideally we would have a library of these 'tools' that we are able to surface in relevant contexts… NDA analysis surfaces the NDA-relevant subset; the next analysis (e.g. Case-law research) surfaces a different subset."

**Goal**: one library of tool definitions; each **UI surface** shows the tools tagged for it; the **active analysis vertical** narrows that to its subset; the intersection renders. Adding a vertical = data + one registry entry, **not** a code fork.

---

## 2. The core model — two context dimensions

A tool is surfaced at the **intersection of two independent dimensions**:

| Dimension | Question it answers | Values (open sets) |
|---|---|---|
| **UI surface** | *Where* does the affordance appear? | `selection` (BubbleMenu), `review-note` (gutter ⋮), `whole-document`, `assistant-chip` |
| **Analysis domain** | *Which vertical* does the tool belong to? | `nda`, `case-law`, `contract-review`, … · `'*'` = shared/agnostic |

**Render rule** (per surface, given the active analysis domain `d`):

```
tools.filter(t =>
    t.surfaces.includes(surface) &&
    (t.domains.includes(d) || t.domains.includes('*')) &&
    t.appliesTo?.(ctx) !== false            // optional runtime predicate
)
```

- A tool with `domains: ['*']` (e.g. "Draft alternative", "Make more concise") appears in **every** vertical — these are the reusable primitives.
- A tool with `domains: ['nda']` (e.g. "Draft compliant alternative" keyed to firm NDA standards) appears **only** when NDA review is the active analysis.
- `surfaces` controls the menu it lands in. The SAME definition can list two surfaces (round-8 already proved "Draft alternative" living in both the BubbleMenu and the Review-Note ⋮ menu from one definition).

This cleanly resolves the round-8 leftover **#6**: "Explain / Email / Defined-terms don't work on selection" becomes *"don't tag them for `selection`"* — a data change, not a code deletion from a heavily-tested shared array.

---

## 3. Two-layer library (capability vs surfacing)

The design deliberately splits **what a tool DOES** from **where it SHOWS** — mirroring the existing Action+Binding / registry split.

### Layer 1 — Capability (server, source of truth)
Each tool's behavior is a JPS **Action + Binding** already in the pipeline:
- the prompt / instruction schema (`sprk_inputschema`),
- the grounding / knowledge sources,
- the disposition (informational vs compose-edit → inline redline).

Authoring the Action once is the single source of truth for *what the tool does*. No client code describes behavior. (This is unchanged from today — `bindingId` on the descriptor points at a `sprk_playbookconsumer` Binding row.)

### Layer 2 — Surfacing (client registry descriptor)
The client library entry describes only *where & how* the tool appears: `id`, `label`, `tooltip`, `bindingId`, `surfaces`, `domains`, and optional `appliesTo` / `inputPrompt` / `icon`. It carries **no behavior** — dispatch routes through the existing `enqueueComposeAction` seam by `bindingId`.

### Where "which tools belong to a vertical" lives — the catalog
An **analysis vertical LINKS to its tool bindings** in the Dataverse catalog, exactly as an Action links to its knowledge sources. So a vertical is defined server-side as:

```
analysis vertical  =  its playbook Action(s)
                   +  its tool Bindings          ← NEW link (this design)
                   +  its knowledge sources       ← existing link
```

At runtime the client's capability-discovery fetch (the same hook that today swaps stub `bindingId:''` for the live GUID via `registerComposeAiToolbarAction`) reads the active vertical's linked tool bindings and registers each with its `domains`/`surfaces`. The client never hard-codes which tools belong to NDA — it learns it from the catalog link. **This keeps surface/vertical identity data-driven while behavior stays in the Action (root §10: no surface identity invented server-side beyond the link).**

---

## 4. Descriptor change (concrete)

Current interface (`ComposeAiToolbar.tsx:249`):

```ts
export interface ComposeAiToolbarAction {
  readonly id: string;
  readonly label: string;
  readonly tooltip: string;
  readonly bindingId: string;                 // '' = not-yet-wired stub
  readonly placement: 'primary' | 'overflow'; // WITHIN the selection toolbar
  readonly materializesInEditor?: boolean;    // DEF-09 redline routing
}
```

Proposed additive change (all new fields optional-with-defaults → **backward compatible**; existing registrations keep working):

```ts
export type ToolSurface = 'selection' | 'review-note' | 'whole-document' | 'assistant-chip';

export interface ComposeAiToolbarAction {
  readonly id: string;
  readonly label: string;
  readonly tooltip: string;
  readonly bindingId: string;
  readonly materializesInEditor?: boolean;

  // NEW — surfacing dimensions
  readonly surfaces?: readonly ToolSurface[];   // default ['selection'] (today's behavior)
  readonly domains?: readonly string[];         // default ['*'] (shared) — vertical narrows it
  readonly appliesTo?: (ctx: ToolContext) => boolean;  // optional runtime gate
  readonly inputPrompt?: string;                // free-text tools ("Describe a change…")

  /** @deprecated superseded by `surfaces`; kept until all callers migrate. */
  readonly placement?: 'primary' | 'overflow';
}
```

Registry accessor gains a surface-aware selector (keeping the existing `getComposeAiToolbarActions()` as the unfiltered store):

```ts
export function getToolsForSurface(
  surface: ToolSurface,
  activeDomain: string,
  ctx?: ToolContext,
): readonly ComposeAiToolbarAction[] {
  return getComposeAiToolbarActions().filter(t =>
    (t.surfaces ?? ['selection']).includes(surface) &&
    ((t.domains ?? ['*']).includes(activeDomain) || (t.domains ?? ['*']).includes('*')) &&
    (t.appliesTo?.(ctx!) ?? true),
  );
}
```

- BubbleMenu calls `getToolsForSurface('selection', activeDomain, ctx)`.
- Review-Note ⋮ menu calls `getToolsForSurface('review-note', activeDomain, ctx)` — **replacing** the round-8 `NOTE_TOOL_LABELS` allow-list in `ComposeEditor.tsx` (that allow-list was the deliberate stop-gap standing in for `surfaces`).
- `placement` (primary/overflow) stays as an intra-surface ordering hint for `selection` only.

---

## 5. What already exists (substrate — no rearchitecting)

Round-8 proved the mechanism; this design formalizes it:

| Capability | Status | Location |
|---|---|---|
| Additive/replace-by-id registry | ✅ exists | `registerComposeAiToolbarAction` / `getComposeAiToolbarActions` |
| Late-registration re-render | ✅ exists | `subscribeComposeAiToolbarActions` |
| Stub→live binding swap at runtime | ✅ exists | capability-discovery hook → `register…` |
| One definition, two surfaces | ✅ proven (round-8) | Review-Note ⋮ reads the SAME registry via `NOTE_TOOL_LABELS` |
| Dispatch by bindingId (behavior-free client) | ✅ exists | `enqueueComposeAction` → ledger → redline (ADR-040/049) |
| Confirmation w/ rationale (#7) | ✅ exists | `extractComposeEditExplanation` |

**Net-new is small**: add `surfaces`/`domains`/`appliesTo`/`inputPrompt` to the descriptor, add `getToolsForSurface`, repoint the two menus, and add the catalog **analysis→tool-binding link** + the discovery-fetch read of it.

---

## 6. NDA as consumer #1 (worked example)

Register (via the discovery hook, once bindings are seeded):

| Tool | `surfaces` | `domains` | `materializesInEditor` | Notes |
|---|---|---|---|---|
| `compose-draft-alternative` | `['selection','review-note']` | `['*']` | `true` | Reusable primitive; already live |
| `compose-make-concise` | `['selection','review-note']` | `['*']` | `true` | **New binding needed** |
| `compose-rewrite-instruction` ("Describe a change…") | `['selection','review-note']` | `['*']` | `true` | **New binding + `instruction` slot**; uses `inputPrompt` |
| `compose-draft-compliant-alternative` | `['review-note']` | `['nda']` | `true` | NDA-standards-grounded; the vertical-specific one |
| `compose-explain-clause` | `[]` or `['whole-document']` | `['nda']` | — | #6: **untag from `selection`** (didn't work there) |
| `compose-defined-terms` | `['whole-document']` | `['nda']` | — | Was overflow on selection; belongs whole-doc |
| `compose-email` | `[]` | — | — | #6: remove/untag (non-functional) |

Result: the BubbleMenu shows the `*` primitives + any `nda` `selection` tools; the Review-Note ⋮ shows the `*` primitives + `compose-draft-compliant-alternative`; nothing NDA-specific leaks into a future Case-law session.

**Case-law drop-in (proof of extensibility)**: author its Actions/Bindings, link them to the `case-law` vertical in the catalog with `domains:['case-law']`, done — zero edits to `ComposeAiToolbar.tsx`, `ComposeEditor.tsx`, or the menus.

---

## 7. Outward-facing / owner-gated items

Per the standing constraint (new server bindings & schema need owner go-ahead):

- **New Bindings**: `compose-make-concise`, `compose-rewrite-instruction`, `compose-draft-compliant-alternative` (seed at catalog time, per-env GUIDs).
- **New input-schema slot**: free-text `instruction` for `compose-rewrite-instruction` / "Describe a change…".
- **New catalog link**: analysis-vertical → tool-Bindings (schema addition mirroring the knowledge-source link). This is the one genuinely new server contract; everything else reuses the dispatch seam.
- **Client-only, safe now**: the descriptor fields, `getToolsForSurface`, repointing both menus, and the #6 untagging.

---

## 8. Phasing

1. **Client refactor (no server dep)** — add fields + `getToolsForSurface`; repoint BubbleMenu + Review-Note ⋮ to it; migrate `placement`→`surfaces` for the 4 existing actions; resolve #6 by untagging. Ships behind the existing registry; behavior identical for `nda` until new tools land. *(client-only — deployable now)*
2. **Catalog link + discovery read** — add the analysis→tool-binding link; discovery hook registers with `domains`/`surfaces` from the link. *(owner-gated: schema)*
3. **New tool bindings** — seed make-concise / rewrite-instruction / draft-compliant-alternative + the `instruction` slot; register them. *(owner-gated: bindings)*
4. **Graduate the doc** — when a 2nd vertical (Case-law) consumes it, promote this design to `docs/architecture/CONTEXTUAL-AI-TOOL-LIBRARY.md` and add the CLAUDE.md §17 pointer.

---

## 9. Open decisions for owner

1. **Ship location** — build inside this NDA project (NDA = consumer #1, documented for drop-in) vs a standalone platform project. **My lean: build here** — the substrate is here, NDA exercises it end-to-end, and phase-4 graduates the doc when Case-law lands.
2. **`activeDomain` source** — where the client reads "which vertical is active": from the launched analysis/session context (recommended — it already knows the consumer type) vs an explicit workspace toggle.
3. **Approve phase-1 client refactor now?** It's client-only, backward-compatible, and folds in the #6 cleanup — a clean first increment ahead of any server work.
