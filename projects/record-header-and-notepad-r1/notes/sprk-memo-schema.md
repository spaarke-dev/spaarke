# `sprk_memo` Schema Verification

> **Task**: 001-verify-sprk-memo-schema
> **Date**: 2026-07-02
> **Source of truth**: Dataverse MCP `describe('tables/sprk_memo')` + `MemoSection.tsx` in EventDetailSidePane
> **Status**: 🔴 **CRITICAL DEVIATIONS FOUND** — spec assumptions are materially wrong

---

## Actual schema (verified via Dataverse MCP)

```
DESCRIBE TABLE sprk_memo (
  -- Primary key
  sprk_memoid GUID,

  -- Title (REQUIRED — NOT NULL)
  sprk_name NVARCHAR(850) NOT NULL,

  -- Body
  sprk_memobody MULTILINE TEXT,

  -- Search denormalization (system-populated, not touched by CRUD)
  sprk_searchprofile MULTILINE TEXT,

  -- Entity-specific regarding lookups (6 parent types)
  sprk_regardingmatter          LOOKUP → sprk_matter,
  sprk_regardingproject         LOOKUP → sprk_project,
  sprk_regardingevent           LOOKUP → sprk_event,
  sprk_regardinginvoice         LOOKUP → sprk_invoice,
  sprk_regardingbudget          LOOKUP → sprk_budget,
  sprk_regardingworkassignment  LOOKUP → sprk_workassignment,

  -- Resolver fields (denormalized for unified views per ADR-024)
  sprk_regardingrecordtype    LOOKUP → sprk_recordtype_ref,
  sprk_regardingrecordid      NVARCHAR(100),   -- GUID-only fits
  sprk_regardingrecordname    NVARCHAR(200),
  sprk_regardingrecordnumber  NVARCHAR(100),   -- ADR-024 doesn't document this
  sprk_regardingrecordurl     URL NVARCHAR(1000),

  -- Standard system fields
  createdby, createdon, modifiedby, modifiedon,
  ownerid, owningbusinessunit, owningteam, owninguser,
  statecode STATE (Active=0 / Inactive=1),
  statuscode STATUS (Active=1 / Inactive=2),
  ...
);
```

## Discrepancies vs spec assumptions

| # | Spec / Owner Clarification | Actual | Impact |
|---|---|---|---|
| **1** | Body field = `sprk_body` (per spec FR-14/15/17) | Body field = **`sprk_memobody`** | Every FR-06/14/15/17 query and create payload is wrong |
| **2** | No title field discussed; derived from first line of body | `sprk_name` is **NOT NULL** (required) | FR-15 create MUST include `sprk_name`; design's "derive title from body" model doesn't map to schema |
| **3** | Owner O1: single text field `sprk_regardingrecordid` (GUID-only) | Full **ADR-024 dual-field pattern**: 6 entity-specific lookups + 5 resolver fields | Spec's ADR Tensions section (Path A exception) is **invalid** — `sprk_memo` fully complies with ADR-024, no exception needed |
| **4** | Notepad launch is "entity-agnostic" (FR-19) | Only 6 parent entity types supported (Matter, Project, Event, Invoice, Budget, WorkAssignment) | FR-19 launch-contract has a hard schema limit; entity list must be closed |
| **5** | Ad-hoc query/create in `useSprkMemoRepository` | `PolymorphicResolverService` exists in shared lib per ADR-024 lines 101-105 | Per CLAUDE.md §11 default-to-reuse, `useSprkMemoRepository` MUST consume the existing service |

## Existing consumer pattern — MemoSection.tsx

`src/solutions/EventDetailSidePane/src/components/MemoSection.tsx:99-104`:

```typescript
const memo = useRelatedRecord({
  entityName: "sprk_memo",
  parentLookupField: "sprk_regardingevent",  // entity-specific lookup for Event
  parentId: eventId,
  selectFields: "sprk_memoid,sprk_name,sprk_memobody,createdon,modifiedon",
});
```

MemoSection creates with `{sprk_name: "Event Memo", sprk_memobody: ""}` (line 143-146). It uses **only the entity-specific lookup**, not the resolver fields. This is technically ADR-024 non-compliant (rule: MUST populate ALL 4 resolver fields when association made), but the pattern is entrenched.

## Correct query pattern for our Notepad

For a memo count / list, query by the entity-specific lookup keyed off the URL `regardingEntity` parameter:

```typescript
// Map regardingEntity → lookup field name
const REGARDING_FIELD_BY_ENTITY: Record<string, string> = {
  sprk_matter: "_sprk_regardingmatter_value",
  sprk_project: "_sprk_regardingproject_value",
  sprk_event: "_sprk_regardingevent_value",
  sprk_invoice: "_sprk_regardinginvoice_value",
  sprk_budget: "_sprk_regardingbudget_value",
  sprk_workassignment: "_sprk_regardingworkassignment_value",
};

// Query
$filter=${REGARDING_FIELD_BY_ENTITY[regardingEntity]} eq ${regardingId}
```

For create, must populate the entity-specific lookup via `@odata.bind` AND per ADR-024 the 4 resolver fields:

```typescript
{
  sprk_name: "Untitled",  // REQUIRED (NOT NULL)
  sprk_memobody: "",
  "sprk_regardingmatter@odata.bind": "/sprk_matters(abc-123-...)",  // for Matter
  sprk_regardingrecordid: "abc-123-...",         // resolver
  sprk_regardingrecordname: "Smith v. Jones",    // resolver
  // sprk_regardingrecordtype: lookup binding to sprk_recordtype_ref
  // sprk_regardingrecordurl: build via helper
}
```

## Recommended action per ADR-024

Adopt `PolymorphicResolverService` (`src/client/shared/.../services/PolymorphicResolverService.ts` per ADR-024 line 101) — it exposes `resolveRecordType`, `buildRecordUrl`, `findNavProp`, `applyResolverFields`. Its `applyResolverFields()` handles entity-specific lookup + all 4 resolver fields in one call:

```typescript
await applyResolverFields(webApi, entity, navProps,
  regardingEntity,          // 'sprk_matter'
  `${regardingEntity}s`,    // pluralized collection name
  regardingId,
  parentDisplayName,
  entityDisplayName);
```

## Impact on downstream tasks

| Task | Impact | Action needed |
|---|---|---|
| **spec.md** | ADR-024 Path A exception is invalid; body field name wrong; sprk_name required; entity-agnostic launch has hard limit | REVISE spec.md — remove ADR Tension row, correct FR-06/14/15/17/19 |
| **010 useRelatedCount** | Filter must use entity-specific lookup, not `sprk_regardingrecordid` | Rewrite POML |
| **031 types/memo.ts** | Must include entity-specific lookups + resolver fields + `sprk_name` | Rewrite POML |
| **033 useSprkMemoRepository** | Must use `PolymorphicResolverService` per §11; must populate resolver fields | Rewrite POML |
| **037 NotepadShell** | Must select entity-specific lookup based on URL regardingEntity param | Update POML notes |
| **012 useRecordHeaderToolbarActions** | Memo count query URL filter needs update | Update POML step 2 |

## Also: `sprk_recordsummary`

Also verify `sprk_recordsummary` schema before task 012 implements the sparkle popover query.

---

*Task 001 output. Status: 🔴 ESCALATED to owner — see chat.*
