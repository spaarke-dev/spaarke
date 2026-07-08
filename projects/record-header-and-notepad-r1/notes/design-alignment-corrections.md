# Design Alignment Corrections — task 001 findings

> **Task**: 001-verify-sprk-memo-schema
> **Date**: 2026-07-02
> **Source of truth**: Dataverse MCP `describe('tables/sprk_memo')` + `describe('tables/sprk_matter')` + MemoSection.tsx + PolymorphicResolverService.ts + sibling project design
> **Owner acknowledgment**: 2026-07-02 chat confirmation
> **Status**: 🟢 ACCEPTED — spec + affected POMLs revised, autonomous execution resumed

---

## What we now know (that the design + spec got wrong)

### 1. `sprk_recordsummary` is a FIELD on `sprk_matter`, not an entity

Dataverse MCP `describe('tables/sprk_matter')` line 52: `sprk_recordsummary MULTILINE TEXT`. Populated by an external service (out of scope for this project). Will be added to other entities (Project, Event, etc.) in the future. Consumer pattern: read `record.sprk_recordsummary` from the already-fetched entity record — no separate entity query.

**Impact**: FR-08 sparkle popover reads the value directly from `useRecordFieldValues` results. Zero additional Xrm.WebApi call for the popover content.

### 2. `sprk_memo` field names differ from spec assumptions

| Spec / Owner O1 | Actual (schema-verified) | Notes |
|---|---|---|
| Body = `sprk_body` | **`sprk_memobody`** MULTILINE TEXT | Every FR that mentions body |
| No title field | **`sprk_name` NVARCHAR(850) NOT NULL** | Required at create; must default to "Untitled" when body is empty |
| Simple regarding = `sprk_regardingrecordid` text (Path A exception) | Full ADR-024 dual-field: 6 entity-specific lookups + 5 resolver fields (id, name, number, url, recordtype ref) | **No exception needed — fully ADR-024 compliant** |

**Supported memo parent entities** (6, from schema lookups): Matter, Project, Event, Invoice, Budget, WorkAssignment.

**Impact**: FR-14 memo-list queries filter by entity-specific lookup (e.g., `_sprk_regardingmatter_value eq {guid}` for Matter). FR-15 memo-create MUST use `PolymorphicResolverService.applyResolverFields()`. FR-19 launch contract is UI-entity-agnostic but memo-create is schema-limited to the 6 supported parents; Notepad must render an error if `regardingEntity` isn't in the supported list.

### 3. `sprk_matter` field names differ from design's FR-12

| Design.md / spec FR-12 | Actual schema |
|---|---|
| `sprk_name` | **`sprk_mattername`** NVARCHAR(1000) |
| `sprk_description` | **`sprk_matterdescription`** MULTILINE TEXT |

The other 3 fields (`sprk_matternumber`, `sprk_mattertype`, `sprk_practicearea`) match.

**Also add to FR-12 fetch list**: `sprk_recordsummary` so the sparkle popover has the summary body available inline.

### 4. `PolymorphicResolverService` exists in shared lib — must be used per §11

Location: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`. API:

```typescript
resolveRecordType(webApi, entityLogicalName) → IRecordTypeRef | null (cached)
buildRecordUrl(entityLogicalName, recordId) → string
findNavProp(navProps, referencedEntity, columnHint?) → string | undefined
applyResolverFields(webApi, entity, navProps, parentEntityLogicalName,
                    parentEntitySet, parentRecordId, parentRecordName, entityLookupHint?)
```

`applyResolverFields()` populates the entity-specific lookup + all 4 resolver fields (id, name, url, recordtype ref) in one call — exactly what `useSprkMemoRepository` needs on create.

**Nav-prop discovery**: `applyResolverFields()` requires `navProps: INavPropEntry[]` from the child entity's ManyToOne relationships. Task 033 must discover nav-props for `sprk_memo` (query metadata) OR reuse an existing `discoverNavProps()` helper if one exists in the shared lib.

### 5. Sibling project alignment: `set-regarding-and-field-mapping-resolver-r1`

Active worktree at `C:/code_files/spaarke-wt-set-regarding-and-field-mapping-resolver-r1`. Adds:
- **5th resolver field `sprk_regardingrecordnumber`** across 10 additional target entities (Matter already has it)
- **`PolymorphicResolverService` minor bump** — writes the 5th field alongside the existing 4 (backward compatible)
- **New shared `PolymorphicPicker` component** — Fluent v9 dropdown+lookup for parent selection

**Coordination**:
- Notepad doesn't need a picker (parent is fixed from URL) — `PolymorphicPicker` not needed for R1
- If sibling ships first, `applyResolverFields()` gains the 5th field automatically (transparent to Notepad)
- If our project ships first, sibling adopts our memo-create code path when they consume the shared service
- Both projects agree on ADR-024 dual-field pattern
- No file collision (sibling touches `sprk_todo`, `sprk_communication`, resolver PCFs; we touch `sprk_memo`)

## Spec.md revisions applied

- **Executive Summary**: sparkle popover reads `sprk_recordsummary` field value (not queries entity)
- **FR-06**: filter description clarified — hook is filter-agnostic; consumers pass entity-specific lookup filter for memos
- **FR-08 (sparkle behavior)**: reads `record.sprk_recordsummary` from useRecordFieldValues (no separate entity query)
- **FR-08a (refresh icon)**: unchanged (still unwired in R1)
- **FR-12 (Matter field list)**: corrected field names + added `sprk_recordsummary`
- **FR-14 (memo list)**: entity-specific lookup filter; body field = `sprk_memobody`; title from `sprk_name`
- **FR-15 (memo create)**: MUST use `PolymorphicResolverService.applyResolverFields()`; `sprk_name` required (default "Untitled")
- **FR-17 (save)**: writes `sprk_memobody`; also updates `sprk_name` if derived-title logic kicks in
- **FR-19 (entity-agnostic launch)**: refined — URL contract is entity-agnostic; memo-create is schema-limited to Matter, Project, Event, Invoice, Budget, WorkAssignment
- **Technical Constraints**: added MUST rule to use `PolymorphicResolverService.applyResolverFields()`
- **ADR Tensions**: ADR-024 Path A row **removed** (no tension exists — `sprk_memo` is ADR-024 compliant)
- **Owner Clarifications table**: O1 corrected with full schema truth
- **Dependencies**: added `PolymorphicResolverService`
- **Related projects**: added `set-regarding-and-field-mapping-resolver-r1` reference

## POMLs revised

- **001** — this notes doc + `notes/sprk-memo-schema.md` corrections
- **010** — filter description clarified; consumers pass entity-specific lookup filter
- **011** — removed `RECORDSUMMARY_ENTITY` constant; renamed to `RECORDSUMMARY_FIELD = "sprk_recordsummary"`
- **012** — sparkle reads field from record; memo count filter uses entity-specific lookup; removed separate entity query
- **021/022** — updated 5 Matter field names + added `sprk_recordsummary` to fetch list
- **031** — Memo type includes `sprk_name` (required) + `sprk_memobody` + regarding lookups + resolver fields
- **033** — must use `PolymorphicResolverService.applyResolverFields()`; must discover nav-props for `sprk_memo`; must handle unsupported entity error

## POMLs unchanged

- **002-008** (shared components) — pure UI, no schema dependency
- **009** (useRecordFieldValues) — hook signature unchanged; just returns whatever fields consumer requests
- **013, 014** (exports + integration test) — updated to reflect new symbol list
- **020, 023, 024, 025** (MatterHeaderPcf ancillary) — unchanged
- **030, 032, 034, 035, 036, 037, 038, 039, 040, 041** (Notepad UX layer) — unchanged
- **050-052, 090** — unchanged

---

*Root cause: original design and spec were authored against assumed schemas without empirical verification. Task 001 exists specifically to catch this class of drift. Now caught before Phase 1 code lands. Autonomous execution resumed.*
