# Task 035 — Which regarding value the single-slot ADR-024 resolver fields describe

> FR-14. `sprk_todo`'s `sprk_regardingrecordid` / `-name` / `-type` describe exactly ONE record
> (single-slot, unlike the eleven typed `sprk_regarding<X>` lookups, of which this task sets TWO at
> once). Decision required by the POML's escalation trigger #1. Written 2026-09-15.

## Decision

**The resolver fields (`sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordtype`)
describe the business RECORD (Matter/Project/Invoice) — never the carrying document/communication —
whenever a record regarding is present.** When only a document/communication carrier is present (no
record), the resolver fields are left unset, matching today's behavior for a standalone To Do with no
regarding at all: the typed lookup (`sprk_regardingdocument` / `sprk_regardingcommunication`) is written
and is the load-bearing relationship; the resolver fields exist to serve the record-centric consumers
enumerated below, which have nothing to describe when no record is known.

This is not treated as ambiguous enough to escalate — three independent, already-shipped consumers of
these exact fields on `sprk_todo` all key off the RECORD, not a document:

## Evidence

1. **`docs/architecture/spaarke-todo-architecture.md`** ("Multi-Entity Resolution (ADR-024)" section):
   `sprk_todo` "follows the same regarding shape as `sprk_communication`": 11 typed lookups + 4 resolver
   fields, populated by `TodoRegardingUpdateBuilder.applyResolverFields`. The resolver fields are the
   SAME mechanism used for every one of the 11 parent-record types (Matter, Project, Event,
   Communication, Contact, Document, …) — there is nothing document-specific about them; they exist to
   answer "what is this To Do about," and for a pane-created To Do that answer is the matter/project/
   invoice the user filed the email or document to, not the file itself.

2. **`src/solutions/SmartTodo/src/components/SmartToDo.tsx:585-607`** (DEF-11 Part 3 comment, dated
   2026-07-04): the free-text Kanban filter matches typed queries like `"Smith v Jones"` or
   `"MAT-2026-01234"` against `sprk_regardingrecordname` (+ a record-number field) *specifically so users
   can search by the matter they're thinking of*. A resolver field carrying the open Word document's file
   name instead of the matter's name/number would silently break this search for every pane-created To
   Do — a regression against a feature the same entity already ships.

3. **`src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx:740-860`**: the
   detail panel renders exactly ONE "Record" link + one "Record Type" badge, driven by
   `sprk_regardingrecordname` / `sprk_regardingrecordtype`, and `handleOpenRegardingRecord` navigates to
   it via `RECORD_TYPE_ENTITY_MAP` (Matter → `sprk_matter`, Project → `sprk_project`, Invoice →
   `sprk_invoice`, … — Document is also a legal value in that map, so the field COULD technically
   describe a document, but doing so here would mean a pane-created To Do's only clickable "Record" link
   opens the document instead of the matter/project/invoice everyone else's To Do links to — an
   inconsistent, host-specific UX for the same entity's same field).

None of the three consumers above distinguish "this To Do happens to also carry a document regarding" —
they all assume the resolver fields are the ONE thing a To Do is "about," and for every other regarding
type that ships today (10 of the 11 lookups), that's the business record. Making Document/Communication
the odd type out — where the resolver fields describe the carrier instead of the record — would silently
break search (#2) and record navigation (#3) specifically for pane-created To Dos, with no compensating
benefit (the typed `sprk_regardingdocument` / `sprk_regardingcommunication` lookup is still written and
remains independently queryable by anything that wants "To Dos carrying this document").

## ADR-024 tension surfaced at code-review (added 2026-09-15, Step 9.5)

🔔 **ADR Conflict — Resolution Required** (CLAUDE.md §6.5)

- **ADR in question**: ADR-024 (Polymorphic Resolver Pattern)
- **Specific rule**: `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` §Constraints: **"MUST populate only
  ONE entity-specific lookup at a time (mutually exclusive)"** / **"MUST NOT allow multiple entity-specific
  lookups to be populated simultaneously."** `docs/architecture/spaarke-todo-architecture.md` enumerates
  `sprk_todo`'s 11 entity-specific lookups as including `sprk_regardingdocument` and
  `sprk_regardingcommunication` alongside `sprk_regardingmatter`/`-project`/`-invoice` — i.e. the carrier
  lookups this task writes are squarely inside the family the MUST NOT rule governs.
- **Conflict**: FR-14's own acceptance criteria and this task's project-level constraint require the
  OPPOSITE: "The created To Do MUST carry BOTH regarding values when both exist... Setting only one when
  both are available is a defect, not a graceful degradation." Implementing FR-14 as specified therefore
  populates TWO entity-specific lookups simultaneously (e.g. `sprk_regardingmatter` +
  `sprk_regardingdocument`) on the same `sprk_todo` row — a literal violation of the quoted MUST NOT rule.
- **Proposed path**: **A — project-scoped exception.** The ADR's mutual-exclusivity rule remains correct
  for its original purpose — a single-parent polymorphic child navigated/filtered by ONE typed lookup at a
  time (the pattern `TodoRegardingUpdateBuilder.applyResolverFields` enforces for the SmartTodo
  wizard/parent-form path). This task's pane-created To Do is a narrow, additive second case: a CARRIER
  lookup (document/communication — "what was open when this To Do was made") alongside the existing
  RECORD lookup ("what business matter this To Do is about"). Both remain independently query-able and
  subgrid-filterable (the ADR's stated rationale for entity-specific lookups at all); nothing about
  dual-population breaks that. The single-slot RESOLVER fields (§ decision above) still describe exactly
  ONE record (the business record), so the "unified cross-entity view" the resolver fields serve is
  unaffected — the tension is scoped to the entity-specific typed-lookup family only, not the resolver
  fields.
- **Rationale**: Reverting to single-lookup-only would directly contradict FR-14's explicit acceptance
  criteria (both regarding values named as separate, independently-testable criteria) and the project
  constraint's own words ("a defect, not a graceful degradation"). A Path C (pivot to comply) is not
  available without dropping a named requirement; a Path B (amend the ADR) is broader than this task
  needs — the ADR's general rule is still correct for every OTHER consumer of the pattern
  (`sprk_event`, `sprk_communication`, `sprk_workassignment`, …), none of which acquire a second
  simultaneous lookup here.
- **Impact of path A**: Narrowly scoped to `sprk_todo` rows created via `POST /api/office/todo` when a
  document/communication carrier and a business record are BOTH known. No other polymorphic-child creation
  path (wizard, `TodoRegardingUpdateBuilder`, `RegardingResolver` PCF) is touched or affected — they keep
  enforcing strict mutual exclusivity exactly as before.
- **Alternative considered (and rejected)**: Store the carrier reference OUTSIDE the entity-specific-lookup
  family entirely (e.g. only as a resolver-field-adjacent text field, never as a typed
  `sprk_regardingdocument`/`sprk_regardingcommunication` lookup). Rejected because the acceptance criteria
  explicitly require `sprk_regardingdocument` (a typed lookup, not a text field) to be populated — see
  spec.md FR-14 and the POML's acceptance criteria 1–2, which name the typed lookups directly.
- **Owner action needed**: This exception has NOT been ratified by the project owner or recorded in
  spec.md's "ADR Tensions" table (that table currently lists only ADR-050). This task's PR/report flags it
  for that sign-off; until ratified, treat this as Path A **proposed**, not accepted.

## What this means for the implementation

- `_todoRegardingMap` gained two carrier entries (`Document` → `sprk_regardingdocument` /
  `sprk_document`, `Communication` → `sprk_regardingcommunication` / `sprk_communication`), used ONLY to
  set the typed lookup for `CreateTodoRequest.DocumentId` / `CommunicationId`.
- The existing "Regarding (the filed record)" block in `OfficeService.CreateTodoAsync` — which sets the
  record's typed lookup, the resolver fields, AND runs the fail-closed core-ancestor stamp — is
  UNCHANGED in what it operates on (still the record, per `RegardingEntityType`/`RegardingRecordId`).
- The new carrier block is independent, unconditional (no core-ancestor stamp dependency — the stamp is
  a property of the record regarding, not the carrier), and does not touch the resolver fields.
- A document-only or communication-only To Do (no record) still gets its typed lookup written; it simply
  has no resolver-field-driven "Record" link in `TodoDetail`, same as any other regarding-less To Do
  today.
