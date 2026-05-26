# Pattern Spec: Entity-centered

> **Status**: Draft v0.1
> **Pattern slug**: entity-centered
> **Closest analogous product:** Dynamics 365 record forms (with reference to Salesforce Lightning record pages)
> **Primary user intent:** *"I need to inspect or update a specific record."*

---

## 1. Optimizes For

The Entity pattern optimizes for **precision work on a known specific record** — viewing all its properties at once, editing a field with confidence, seeing what it relates to. The user already knows which thing they want; the pattern's job is to put that thing on the screen completely and let them act on it.

This is not a search pattern (the user found it already), not a triage pattern (they're not deciding among many), and not a guided-input pattern (they're not creating it from scratch). The user has arrived at a record and wants to *operate on it*.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- The user has identified a specific record they need to work on (matter, counterparty, contract, person, vendor, invoice).
- The work is reviewing or editing that record's properties, not navigating among records.
- The user needs visibility into the record's relationships — what's attached to it, what links to it, what its history is.
- The record has enough properties that putting them all on one screen is justified (not a five-field thing — that's a Form widget).

### 2.2 Do not use this pattern when

- The user is selecting *which* record to work on (Queue or Summary).
- The user is creating a new record from scratch (Form / Wizard — though Entity may *contain* an embedded form for adding related items).
- The record is so simple that a properties panel is enough; full Entity treatment is overkill.
- The work is fundamentally about the *content of an artifact attached to* the record (a contract document, a redlined NDA) rather than the record's properties — that's Canvas, with the Entity record providing context in another pane.

### 2.3 Pattern overlaps

- **Form / Wizard**: Forms create or edit *one transaction's worth* of data with guidance. Entity displays and edits *a persistent record's* full property set. An Entity view can host inline forms for editing a section; it is not itself a form.
- **Summary / Intelligence**: Summary aggregates across many records. Entity shows one record. A Summary widget often links into Entity ("12 contracts pending — show me this one").
- **Canvas / Document-centered**: Canvas is the artifact. Entity is the record about the artifact. A contract document is a canvas; the contract record (counterparty, value, dates, status) is an entity.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **Full property visibility by default.** All of the record's properties are on the screen or one scroll away. Hiding properties behind tabs and accordions is the failure mode that drove the Dynamics modern-form redesign — users couldn't find fields they knew existed.

2. **Edit-in-place for individual properties.** Click a field, edit it, save. No modal dialog for changing a single value. Dynamics and Salesforce both converged here years ago for the same reason: modal-per-edit is friction that compounds.

3. **Section organization that mirrors how users think about the record.** Counterparty records group as Identity → Contact → Legal-relevant-attributes → Activity. Matter records group as Overview → Parties → Documents → Activity → Spend. Sections are not tabs; they are visually distinct regions on a continuous canvas.

4. **Related-records surfacing.** "Documents attached to this matter," "contracts with this counterparty," "people on this matter's team" — these appear *within* the Entity view, not as separate navigation destinations. Users see what a record connects to without leaving it.

5. **Activity timeline.** What happened to this record, when, by whom. For legal records this is non-negotiable — audit and history are load-bearing.

6. **A clear save / validation state.** When the user has unsaved changes, they see it; when validation prevents save, they see why; when save completes, they see confirmation. The Dynamics convention (subtle persistent indicator + on-save toast) is the baseline.

### 3.2 Supporting mechanics

1. **Quick actions** for the common operations that don't require a form — "send to counsel," "mark as executed," "archive." These appear near the top of the entity view, not buried in a menu.

2. **Lookup fields that surface related entities** without leaving the current view. Hover or click expands a preview of the related record; click-through navigates if the user actually wants to leave.

3. **Field-level help** for legal-specific fields where the meaning isn't self-evident. Counterparty type, matter classification, privilege handling — these benefit from inline disambiguation, not just tooltips.

### 3.3 AI augmentation mechanics

1. **AI-suggested field values.** When the system can infer a field value from related data (a counterparty's industry from its name and prior contracts, a matter's likely classification from its description), it suggests with visible distinction between AI-suggested and user-entered. Acceptance is one click; override is one keystroke.

2. **AI-generated record summary.** A short narrative at the top of the entity: "Matter opened 14 days ago for NDA review with [Counterparty]. Three documents attached. Awaiting counterparty response since [date]." This is not a substitute for the structured properties — it's a fast read for users who haven't seen the record recently.

3. **AI-surfaced anomalies.** "This contract value is 4× the median for similar matters." "Counterparty record has been edited five times in the past hour." These appear as inline flags on the relevant field or section, not as a separate panel.

---

## 4. Expectations to Honor (Closest Analogous Product: Dynamics 365)

Every Spaarke user reaches the Entity pattern with Dynamics or Salesforce muscle memory. The patterns are similar enough that we honor the dominant one (Dynamics, given Spaarke's Microsoft tenancy).

### 4.1 What Dynamics does that Spaarke must match

- **Header with key identity fields pinned at the top.** Record name, primary owner, current status. These stay visible when the user scrolls.
- **Form sections in a vertical scroll, not tabs by default.** Dynamics' modern form layout favors scroll-with-sections; tabs are an opt-in for very long records. Spaarke should default the same way.
- **Inline editing on individual fields without a separate edit mode.** Click a field to edit; click elsewhere or hit Tab to commit. No "Edit" / "Save" mode toggle for the whole record.
- **A timeline or activity section** showing notes, emails, tasks against the record.
- **Lookup field behavior** that searches as the user types and shows a small preview of the matched record.
- **Required-field indication** consistent with Dynamics conventions (red asterisk; field outlined on validation failure).

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why |
|---|---|
| Matter-record Entity views drive the Context pane to show matter-scoped state (deadlines, related parties, recent activity) | The three-pane shell adds value over Dynamics' single-canvas record page; the Context pane is where matter context lives without crowding the entity view itself |
| AI summary at the top of the record is default-on, not an opt-in widget | Legal records carry enough state that a 2-sentence narrative meaningfully reduces re-read time when revisiting a record after days away |
| Document-typed properties (an NDA on a matter; a redlined contract on a counterparty) get a quick-preview affordance that opens the document in Canvas in another Workspace tab | Documents are first-class in legal contexts; the Dynamics convention (file as attachment in a list) underweights them |
| Privilege-aware visibility on individual fields | Some matters have fields that not all team members should see; the Dynamics permission model is record-level, not field-level. Spaarke needs both. |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Tab-itis.** Properties hidden behind 4+ tabs ("General," "Details," "Custom," "Related"). | Users can't find fields; they ask "is there a way to see everything at once?" | Default to scroll-with-sections. Reserve tabs for genuinely independent contexts (e.g., a person record's matter affiliations vs their employment history). |
| **Modal-per-edit.** Clicking a field opens a dialog to edit it. | Edit velocity drops; users batch their edits and lose context. | Inline edit. The dialog is reserved for edits that need their own form (e.g., adding a related record from scratch). |
| **AI suggestions visually identical to user-entered values.** | Users accept AI values by accident; later they can't tell which fields they actually verified. | Distinct visual treatment for AI-suggested-not-yet-confirmed values. The convention applies across patterns and belongs in the AI Augmentation cross-cutting spec. |
| **Activity timeline as an afterthought.** A small "Notes" tab at the bottom. | Users miss recent activity; they ask "did anyone update this?" when the answer is yes, two days ago. | Activity is a primary section, not a tab. The most recent activity is visible without expanding. |
| **Related records as link lists.** "Documents (3)" expands to a list of filenames with no preview or context. | Users click through to see if a document is the one they need, then click back, repeat. | Related records show enough metadata (type, date, status) that the user can decide whether to open without round-trips. Mini-cards, not link lists. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Entity record view | **EntityInfoWidget** exists in the Context pane (R1) but Context-pane placement is wrong for the Entity *pattern*. The Context pane is for ambient context; the Entity pattern needs the Workspace pane. |
| Pane assignment | The Entity pattern should occupy the **Workspace pane** as a primary work surface. EntityInfoWidget in Context is a *supporting* surface (e.g., showing matter context while the user is doing something else). |
| AI augmentation | Not currently specified for entity records. AI summary, field suggestions, and anomaly flags are new requirements. |
| Cross-pane drive | When an Entity view is in Workspace, the Context pane should show related summary / activity that doesn't fit in the entity view. |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **The R2 design treats EntityInfoWidget as Context-pane only.** | Context pane shows entity info as one of its adaptive stages (design.md §2.1). | Entity is a *primary work pattern* that needs full Workspace-pane treatment, not just a Context-pane sidebar. The Workspace should host a full Entity view; the Context pane shows ambient entity info when the user is working on something else. |
| **The widget vocabulary doesn't include an EntityWorkspaceWidget.** | R1's widget set covers Summary, Workflow, Canvas patterns; R2 adds DocumentViewer and RedlineViewerWidget. No first-class entity-as-Workspace component exists. | A new **EntityWorkspaceWidget** for full record views in the Workspace pane. The existing EntityInfoWidget remains for Context-pane use. |
| **Field-level privilege handling isn't in the current model.** | Spec.md privilege handling (FR-405, FR-408) is record-level / matter-level. | Some legal use cases need field-level visibility differences within the same record. May require a permission-model extension or a structural choice to split such records into multiple linked records. Engineering call. |
| **Document-typed properties don't have a defined Canvas hand-off.** | Documents are attachments in the current model. | When an entity property is a document, clicking it should open Canvas in a second Workspace tab (within the 3-tab ceiling). This is a cross-pattern transition that belongs in the Composition Guide. |

### 6.3 New components / events / widgets proposed

- **EntityWorkspaceWidget** — full entity view for the Workspace pane (distinct from EntityInfoWidget which stays in Context).
- **AI summary convention** — standard placement and styling for the 1–3 sentence narrative at the top of entity records. Belongs in the AI Augmentation cross-cutting spec.
- **Field-level privilege model** (provisional) — engineering call required.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| Should the EntityWorkspaceWidget be one widget that handles all entity types (matter, counterparty, contract, person) via configuration, or one widget per entity type? | Architecture and maintainability. One widget is simpler to build; per-type is more flexible. | Engineering call after seeing actual use case variety from Phase 3 |
| When the user has unsaved changes on a record and tries to navigate away, what's the right interruption? | Legal records carry consequence; lost edits are real cost. But over-aggressive interruption breaks flow. | Prototype testing with primary personas |
| Field-level privilege: solve in the data model or in the UI? | Architectural commitment with long consequences. | Engineering call |
| How does the AI record summary handle stale state — is it regenerated on every load, cached, or generated on demand? | Performance and trust tradeoff. Stale summaries that contradict current field values destroy trust. | Engineering call + pilot instrumentation on regeneration cost |

---

*Draft v0.1 — 2026-05-18.*
