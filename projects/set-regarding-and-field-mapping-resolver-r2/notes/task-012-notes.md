# Task 012 — Copy mapping engine (scalar + lookup @odata.bind) — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@xhigh · Verdict: COMPLETED

## What was done

Implemented the `SEAM[012:Copy]` branch that task 010 left as a warn-and-skip
placeholder in `FieldMappingService.ts`. Both scalar and lookup Copy targets
are now handled; the lookup path is the primary path (all 8 seeded
attorney/assigned-resource rules are Lookup/Lookup), not an edge case.

### Files changed

- **`src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts`**
  — filled the Copy seam:
  - `IRuleApplyContext` gained `sourceRecordForCopy: Record<string, unknown> | null`.
  - `applyFieldMappings` now pre-fetches the source record ONCE (before the
    rule-dispatch loop) via `fetchSourceRecordForCopyRules`, covering every
    Copy rule's needed fields in a single combined `$select` — never one
    `retrieveRecord` call per rule.
  - `applyCopy` dispatches on `rule.targetFieldType`: `"Lookup"` →
    `applyCopyLookup`; else → `applyCopyScalar`.
  - `applyCopyScalar` assigns the plain source value to `payload[targetField]`.
  - `applyCopyLookup` reads `_<sourceField>_value` +
    `@Microsoft.Dynamics.CRM.lookuplogicalname` from the pre-fetched record,
    pluralizes the referent to its entity set, calls the shared
    `discoverNavProps(targetEntity)` (task 011) + `findNavProp(navProps,
    referentEntity, rule.targetField)` (using the target field name as the
    disambiguation hint — see §3), and writes
    `payload[\`${navProp}@odata.bind\`] = \`/${entitySet}(${cleanGuid})\``.
  - `_cleanGuidForBind` / `_resolveEntitySetForReferent` mirror
    `invoiceService.ts`'s private `_cleanGuid`/`_resolveEntitySet` (not
    exported there, so duplicated minimally with a comment pointing at the
    source, per the task's own fallback instruction).
- **`src/client/shared/Spaarke.UI.Components/src/services/__tests__/FieldMappingService.test.ts`**
  (new) — 4 focused tests (see §5). Full engine test sweep is task 015's scope.

## 1. Verdict: COMPLETED (not escalated)

The task's escalation trigger — "if the source lookup annotation is genuinely
NOT obtainable via the injected `IDataService`" — did **not** fire, because
production wiring makes the annotation obtainable. I traced the actual
runtime path before implementing (see §2) rather than assuming design.md's
prescribed mechanism would just work.

## 2. The lookup-binding path — investigation before implementation

Before writing the lookup seam, I traced **which `IDataService` adapter
actually backs every Create*Wizard's `dataService` at runtime**, because the
Copy rule's referent-resolution depends entirely on what shape
`retrieveRecord` returns for a lookup field — and the codebase has TWO
adapters with genuinely different response shapes for the same call:

- **`xrmDataServiceAdapter.ts`** (`createXrmDataService()`) — `retrieveRecord`
  delegates straight to `Xrm.WebApi.retrieveRecord(entityName, id, options)`.
  Xrm.WebApi is documented to auto-include OData annotations (including
  `@Microsoft.Dynamics.CRM.lookuplogicalname`) for `_<field>_value` selects —
  no `Prefer` header needed client-side.
- **`bffDataServiceAdapter.ts`** (`createBffDataService(...)`) — routes
  through the BFF's `GET /api/dataverse/record/{entity}/{id}`
  (`RecordEndpoints.cs` → `RecordService.GetRecordAsync` →
  `IDataverseService.RetrieveAsync`, the **SDK `ServiceClient`** path, not raw
  OData). `RecordService.ProjectEntityToDictionary` + `UnwrapAttributeValue`
  unwrap a lookup `EntityReference` into `{ id, logicalName, name }` under the
  **plain** field name — no `_value` suffix, no annotation. I also confirmed
  (`DataverseWebApiClient.cs:62`) that the BFF's *other* Dataverse Web API
  client (used elsewhere, not by this endpoint) sets `Prefer:
  odata.include-annotations="OData.Community.Display.V1.FormattedValue"` —
  FormattedValue only, NOT `lookuplogicalname` — so even a raw-OData BFF path
  would not carry the annotation this rule needs.

**Which one does production actually use?** Grepped every
`createXrmDataService`/`createBffDataService` call site.
`src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx:441` is the
**only** place a `dataService` is constructed for the 7 Create*Wizards, and it
calls `createXrmDataService()` exclusively (line 68 import, line 1149 wiring
into every wizard). **Confirmed**: the Xrm.WebApi adapter — annotation-bearing
— is the actual, sole production path for this engine's Copy-lookup rules.

This resolves the escalation trigger's premise: the annotation **is**
obtainable via the injected `IDataService`, for the concrete adapter the
engine will actually run against. I implemented exactly the mechanism
design.md §4.1a and the task's own constraints prescribe (`_<field>_value` +
`@Microsoft.Dynamics.CRM.lookuplogicalname`), documented the BFF-adapter gap
as a known limitation in the code comment (so a future task wiring this
engine through the BFF adapter for lookup Copy rules is not surprised), and
did **not** add dual-shape defensive handling for the BFF adapter's
`{id, logicalName, name}` shape — that would be speculative generality for a
path nothing currently exercises, and the task explicitly directs the
annotation-based mechanism.

## 3. Nav-prop disambiguation — the second correctness risk

`sprk_reportcard` (and `sprk_event`) have **6 different lookups all
referencing `contact`** (attorney1/2, paralegal1/2, external, internal).
`findNavProp(entries, referencedEntity, columnHint)` falls back to
`matches[0]` when `columnHint` is omitted or doesn't match — so calling it
without a hint would silently bind every contact-referencing Copy rule to the
SAME (first) nav-prop. I pass **`rule.targetField`** as the hint: nav-prop
entries' `columnName` is the exact `ReferencingAttribute` logical name (e.g.
`sprk_assignedattorney1`), so `columnName.includes(rule.targetField)` is an
exact match, not a substring guess. Verified via test (fixture deliberately
includes two contact-referencing entries; assertion confirms the correct one
is picked).

## 4. Constraint confirmations

- **Fetch source values ONCE**: `fetchSourceRecordForCopyRules` builds one
  combined `$select` (union of scalar field names + `_<field>_value` lookup
  forms) across every Copy rule and calls `dataService.retrieveRecord` exactly
  once per `applyFieldMappings` invocation — verified by test 4 (`toHaveLength(1)`).
- **No second BFF profile call**: unchanged — the only `authenticatedFetch`
  call remains the profile fetch (task 010). The new Copy read is a Dataverse
  read via `IDataService`, a different concern entirely.
- **Never throws**: `fetchSourceRecordForCopyRules`'s try/catch converts a
  fetch failure into one warning + `null` (not a rethrow); every Copy rule
  then skips gracefully rather than each re-attempting (and re-failing) its
  own fetch. `applyCopyLookup`'s annotation/nav-prop checks append a warning
  and return rather than throwing. The outer `applyFieldMappings` loop's
  existing per-rule try/catch (task 010) remains a belt-and-suspenders
  safety net.
- **No `source === target` guard**: grep-confirmed zero occurrences outside
  explanatory comments (task 014 owns same-entity).
- **No `ComponentFramework`/PCF import**: grep-confirmed zero occurrences
  outside explanatory comments (ADR-012).
- **Reused invoiceService pattern**: `_cleanGuidForBind`/
  `_resolveEntitySetForReferent` mirror the invoiceService private helpers
  exactly (not exported there, so duplicated minimally per the task's own
  fallback instruction, with a comment pointing at the source).

## 5. Verification

- **Build**: `npm run build` (tsc, shared lib) → **0 errors**.
- **New tests**: `npx jest FieldMappingService` → **4/4 passed**:
  1. Scalar Copy assigns the source value to the target payload field.
  2. Lookup Copy (`sprk_assignedattorney1 → contact`) writes
     `sprk_AssignedAttorney1@odata.bind = /contacts(guid)` — the exact
     acceptance-criterion worked example.
  3. Unresolvable lookup (GUID present, annotation missing — the exact
     BFF-adapter gap documented in §2) warns and skips; asserted no throw.
  4. Multiple Copy rules (scalar + lookup) share exactly ONE `retrieveRecord`
     call with a combined `$select`.
- **Regression sweep**: `npx jest "PolymorphicResolver|invoiceService|eventService|reportCardService|FieldMapping"` →
  **6 suites / 67 tests, all passed** — no regression from the new
  `PolymorphicResolverService` import.
- **Lint**: `npx eslint` on both changed/added files → **0 problems** (exit 0).

## 6. Step 9.5 quality gates

- **code-review**: CLEAN — 0 Critical / 0 Warning / 2 low-value Suggestions
  (style-consistency only: `applyCopyLookup` uses a type cast where
  `applyCopyScalar` uses optional chaining — both safe, not fixed since the
  outer engine try/catch is a safety net either way).
- **adr-check**: CLEAN — ADR-012 ✅ (grep-confirmed no `ComponentFramework`/
  `Xrm.WebApi`/PCF references outside doc comments), ADR-010 ✅ (no new DI
  registrations), ADR-028 ✅ (no new fetch/auth pattern), ADR-024 ✅ (reuses
  shared `discoverNavProps`/`findNavProp`). 0 violations, 0 warnings.

## Notes for downstream tasks

- **Task 013** (Default/Concat/Template) can add its own source-read strategy
  independently — `sourceRecordForCopy` is scoped to Copy rules only and does
  not need to be shared/reused by other mapping types, though it's available
  on `IRuleApplyContext` if a future task finds it convenient to extend the
  same batching pattern.
- **Task 015** (full engine test sweep) should extend
  `FieldMappingService.test.ts` (this file) rather than create a new one —
  consistent with task 010's note that "New engine tests are owned by tasks
  012–014" landing in one growing file.
- **Known limitation, not a blocker**: if a future context wires this engine
  through `createBffDataService()` instead of `createXrmDataService()` for a
  Lookup Copy rule, the `_<field>_value` + annotation read will not resolve
  (the BFF's `/api/dataverse/record` endpoint returns `{id, logicalName,
  name}` under the plain field name, not the annotated OData form) — the rule
  will gracefully warn-and-skip rather than throw, but the lookup won't bind.
  Documented in the code comment on `fetchSourceRecordForCopyRules`. Not
  addressed here because production wiring (VisualHost, all 7 wizards) is
  confirmed Xrm-adapter-only.
