# W4 Communication Code Page — Prototype Brief

> **Purpose**: Complete UI/UX inventory for a comprehensive `/prototype` pass covering ALL Communication
> Code Page surfaces, so look/feel/UX are iterated holistically in the `spaarke-prototype` harness
> BEFORE finalizing the built Code Page (040/041) and building the review surface (042).
> **Decision (2026-07-15, owner)**: prototype any/all UI components together for efficient iteration.
> **Status**: awaiting owner "go" to launch `/prototype`. Build of 042 is HELD pending sign-off.

---

## Why prototype (design-novel vs. reused)

| Surface | Novelty | Prototype priority |
|---|---|---|
| **Association review surface** (suggestion + confidence + provenance → accept/override) | NET-NEW UX — no Spaarke precedent for "here's the AI's guess, confirm/correct it" | **HIGH** |
| **"Communications Awaiting Association" triage view** | New list/triage surface | **HIGH** |
| Overall page chrome (first-class entity-form look, ref §5.10) | Page *replaces* the OOB Dataverse form | **MEDIUM** |
| Channel-aware layout switch (email interactive vs read-only) | New shell contract; read-only channel renderers unspecified visually | **MEDIUM** |
| Email composer (`<EmailComposer/>`/`<SendEmailPage/>`) | Already-styled shared component (task 020) | LOW — reuse; only its *framing* in the page is in scope |
| `RegardingResolver` PCF | Already-styled | LOW — reuse; its *pre-fill + surrounding review card* is in scope |

---

## Surface + state inventory (what to mock)

### 1. Page chrome / shell
- Header/identity band for a `sprk_communication` (subject, channel-type badge, status pill: Resolved / Suggested / Pending Review / Ambiguous).
- First-class "entity-form" feel (ref §5.10) — this is the default form replacement, not a dialog.
- Theme: light + dark (ADR-021 token-based; must be verified in 043).

### 2. Email branch — interactive (`sprk_communicationtype == Email`)
- **compose** (blank), **reply** (`Re:` + prefilled to/inReplyTo), **forward** (`Fwd:` + attachments), **draft** (editable), **view** (read record).
- Composer framing within the page (how `<SendEmailPage/>` sits inside the chrome; send/cancel affordances).

### 3. Read-only channel branches (Teams / SMS / Notification)
- Read-only renderer look (no composer) — one shared visual treatment, channel badge differentiates.

### 4. Association review surface (HIGH — design-novel)
- **Suggestion card**: top target (entity + name), **confidence** presentation (how to show 0.00–1.00 — bar? band label "High/Medium"? numeric?), **provenance rationale** (human-readable "why": which rungs fired, e.g. "matched an existing thread + a known participant").
- **1-click accept** affordance.
- **Override**: "pick another target" (RegardingResolver embed) + **override-reason** capture (only when chosen ≠ top suggestion; signal-only).
- **Optional Field Mapping on accept** (opt-in "also inherit fields from the parent").
- **Ambiguous** state: 2+ competing high-confidence targets side by side — how to present the tie for a human tiebreak.
- **AI-flagged privilege** (ADR-015): shown as a flag/badge, never an auto-decision.
- Status transitions visible: Suggested/Pending/Ambiguous → Resolved on accept.

### 5. "Communications Awaiting Association" triage view
- List filtered to `sprk_associationstatus in (Suggested, Pending Review, Ambiguous)`.
- Columns: subject, channel, from, received, suggested target, confidence, privilege flag.
- Matter-level auth-scoped (ADR-003/008) — only what the caller may see.
- Row → opens the review surface.

### 6. Empty / edge states
- No suggestion (Pending Review, nothing matched) — manual-pick affordance.
- Auth-scoped-empty ("nothing awaiting association you can see").
- Loading / error (record fetch, BFF unavailable).

---

## Open UX questions to resolve in the prototype

1. **Confidence rendering** — numeric %, a bar, a High/Med/Low band, or a combination? (Drives reviewer trust + speed.)
2. **Provenance rationale** — how much of the `sprk_associationprovenance` JSON to surface as plain English vs. an expandable "details" (rungs fired + per-signal confidence)?
3. **Accept vs. override friction** — 1-click accept must be effortless; override should be easy but deliberate (reason capture). Where's the balance?
4. **Ambiguous tie presentation** — cards, radio list, comparison? The reviewer must pick without the system guessing.
5. **Review surface placement** — inline within the record page (top banner over the email view) vs. a dedicated review mode vs. a side panel?
6. **Triage view density** — how much per row to enable fast batch review without opening each record?
7. **Channel read-only treatment** — one generic read-only layout for all non-email channels, or per-channel accents?

---

## Prototype → build hand-off

- Prototype lives in `spaarke-prototype` (mock data; no BFF). Output: approved look/feel + interaction spec.
- **Feeds**: 042 (build review surface + triage view to the approved design), plus a visual-refinement pass on 040 (chrome) + 041 (composer framing).
- Reused production components (`<EmailComposer/>`, `RegardingResolver`, `PolymorphicResolverService`) are wired, not restyled — the prototype decides their *framing/composition*, not their internals.

---

## Data the prototype should mock (so it's realistic)

- `sprk_communication` records across all statuses (Resolved / Suggested / Pending Review / Ambiguous) and channels (Email / Teams / SMS / Notification).
- Realistic `sprk_associationprovenance` JSON per the 015 schema: `{ decision{status,autoFiled,topConfidence,reason}, rungsFired[], candidates[{field,targetEntity,reinforcedConfidence,contributors[]}], signals[] }` — so confidence + rationale rendering is designed against the real shape.
- A few Ambiguous records with two competing candidates to design the tie-break.
