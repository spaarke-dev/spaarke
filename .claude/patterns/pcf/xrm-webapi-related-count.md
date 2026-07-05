# Xrm.WebApi Related-Record Count + Badge Indicator Pattern

> **Last Reviewed**: 2026-07-04
> **Reviewed By**: record-header-and-notepad-r1 UAT (v1.0.11 → v1.0.17)
> **Status**: Verified — deployed to `MatterHeaderPcf` v1.0.17 with a `sprk_todo` badge and a `sprk_memo` badge, both showing accurate live counts.

## When

You have a PCF (or any host-context surface) that needs to display a count of related records as a badge overlay on an icon — the "17 new to-dos" / "3 memos" / "5 attachments" pattern. Count fetched via `Xrm.WebApi` (host-context surface, ADR-028 boundary — no BFF).

Typical uses:
- Toolbar-icon counters on record-header PCFs (`MatterHeaderPcf`, future `ProjectHeaderPcf` / `InvoiceHeaderPcf` / `EventHeaderPcf`)
- Any icon that says "look here — there are N things"

## Read These Files

1. **[`src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts)** — the canonical hook. Includes the load-bearing comment explaining the `@odata.count` bug that caused the v1.0.11 → v1.0.15 silent-zero regression.
2. **[`src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts)** — `buildMemoFilterForParent` + `buildTodoFilterForParent` show how to translate a parent entity name into an OData filter clause per the ADR-024 dual-field lookup pattern.
3. **[`src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx)** (see the `styles.badge` object + `<CounterBadge>` element) — the badge positioning + Fluent v9 `<CounterBadge>` sizing that survived the v1.0.13 → v1.0.17 UAT iteration.
4. **[`src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useRelatedCount.test.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useRelatedCount.test.ts)** — the mock shape that matches real Xrm (NOT the fictional `@odata.count` shape).

## Constraints

- **ADR-028**: Host-context surface only. Use `Xrm.WebApi` (via `getXrm()` cross-frame walker). Zero `@spaarke/auth`, zero BFF calls.
- **Spec FR-11 (record-header)**: refresh on mount + `window` focus event only. Never poll.
- **React 16/17 compat**: shared-lib hook, so `useState` / `useEffect` / `useCallback` / `useRef` only. No `use()` / `useSyncExternalStore`.

## Key Rules

### 🚨 Critical gotcha — `Xrm.WebApi.retrieveMultipleRecords` DOES NOT expose `@odata.count`

**Never write this**:
```typescript
// ❌ BROKEN — always returns 0 in real Xrm hosts
const result = await xrm.WebApi.retrieveMultipleRecords(
  entity,
  `?$filter=${filter}&$count=true&$top=1`,
);
const count = result['@odata.count']; // undefined at runtime → count = 0
```

**Why it silently fails**:
- At the wire, `$count=true` DOES cause Dataverse to include `@odata.count` in the raw HTTP response.
- But `Xrm.WebApi`'s client wrapper **strips it** and returns only `{ entities, nextLink }`.
- The property lookup returns `undefined` → `typeof raw === 'number'` is `false` → count defaults to `0`.
- No error is thrown. No warning is logged. Every badge silently shows nothing.

**Historical evidence**: v1.0.11 → v1.0.15 of `MatterHeaderPcf` shipped this bug. Live QA missed it because early test matters had 0 related records — so a silently-broken "count = 0" was indistinguishable from "correctly counted 0 records." Discovered 2026-07-04 via a diagnostic `console.info` that logged `todoCount: 0, todoError: null, todoLoading: false` for a matter Dataverse MCP confirmed had 3 memos + 2 todos.

### ✅ Correct pattern — count entities client-side

```typescript
// ✅ CORRECT — count entities.length from the actual Xrm response shape
const CAP = 100;
const query = `?$select=createdon&$filter=${filter}&$top=${CAP}`;
const result = await xrm.WebApi.retrieveMultipleRecords(entity, query);
const count = Array.isArray(result?.entities) ? result.entities.length : 0;
```

**Notes**:
- `$select=createdon` — pick ANY single small column (every Dataverse entity has `createdon`) so the payload doesn't carry unused fields. We don't care about the row data, only the row count.
- `$top=100` — cap so the payload stays small. Badges max out at 100 which is a fine "at a glance" ceiling. If you need "99+" semantics, add UI-level formatting; don't chase a true total from OData.
- `result?.entities` — defensive against unusual host shapes (SSR-like harnesses, mocks).

### 🚨 Test-mock discipline

The `useRelatedCount` unit tests **used to mock a fake `@odata.count` field**. That's what let the bug ship — tests passed against a mock that fabricated a response shape real Xrm doesn't produce. Corrected in the same commit that fixed the runtime bug.

**Rule for future related-count-style hooks**: your mock's `retrieveMultipleRecords` return value MUST be `{ entities: Array<...>, nextLink?: string }` and NOTHING else. If you're mocking `@odata.count`, `@odata.nextLink`, `value`, or any other OData wire-format property, your test is testing a fiction. See [`useRelatedCount.test.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useRelatedCount.test.ts) `makeEntities(n)` helper for the correct mock shape.

### Filter-building rules (ADR-024 dual-field pattern)

Related-record entities in Spaarke (`sprk_memo`, `sprk_todo`, etc.) use ADR-024 entity-specific regarding lookups, NOT the standard polymorphic `_regardingobjectid_value`. Use the helpers from `toolbarLaunchDefaults.ts` instead of hand-coding the filter:

```typescript
const memoFilter = buildMemoFilterForParent('sprk_matter', matterId);
// → "_sprk_regardingmatter_value eq <matterId>"
// Returns null for unsupported parents — pass through to useRelatedCount which idles at count=0.
```

If you need this pattern for a NEW related-record entity, add its `SUPPORTED_<X>_PARENTS` map + `build<X>FilterForParent` helper alongside the existing ones. Do NOT invent a new naming convention.

### Badge display rules

Toolbar-style icon badge (as used by `HeaderToolbar`):
- Fluent v9 `<CounterBadge>` — `color="brand"`, `size="small"`, `appearance="filled"`.
- Anchor with `position: absolute; top: -4px; insetInlineEnd: -4px` on the badge (put it at the icon's upper-right corner, slightly overhanging).
- `pointer-events: none` on the badge so clicks pass through to the icon button behind.
- Guard: only render when `count` is a positive finite integer. `undefined`, `null`, `0`, negatives, and non-finite values suppress the badge (per `shouldRenderBadge()` in [`HeaderToolbar.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx)).

Do NOT bump `size` above `"small"`. Larger badges swallow the underlying icon (v1.0.13 UAT regression — user reported "the icons are covered"). Layout invariant: **the icon must remain the primary visual element; the badge is a corner adornment.**

## Anti-patterns

- ❌ Reading `@odata.count` from any `Xrm.WebApi.retrieveMultipleRecords` result. See "Critical gotcha" above.
- ❌ Using `$top=0` — Dataverse rejects with `"Invalid value for $top query option"`.
- ❌ Polling the count on a `setInterval`. Use mount + window-focus, matches spec FR-11.
- ❌ Bumping `<CounterBadge>` `size` to `"medium"` or larger. Regressed v1.0.13 UAT — swallowed the icon.
- ❌ Mocking `@odata.count` in unit tests. It's a fiction. Real Xrm doesn't return it.
- ❌ Piping the badge value through a `Number()` coercion cast without validating it's positive-finite-integer. See `shouldRenderBadge()` for the canonical guard.

## Failure Modes

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Badge shows nothing on records that clearly have related items | Reading `@odata.count` from `retrieveMultipleRecords` result (undefined at runtime) | Use `entities.length` per this pattern's ✅ example. Add a `console.info` diagnostic if unsure — real Xrm returns `{entities, nextLink}` only. |
| Count silently caps at some low number | `$top` set too low for the use case | Default to `$top=100` (`RELATED_COUNT_CAP` in `useRelatedCount.ts`). Raise carefully — larger payload cost per icon. |
| Filter returns 0 despite records existing | ADR-024 lookup vs standard `_regardingobjectid_value` mismatch | Use `buildMemoFilterForParent` / `buildTodoFilterForParent` helpers. Both entities use entity-specific ADR-024 lookups — the naive polymorphic filter returns nothing. |
| Badge covers/obscures the icon | `<CounterBadge>` `size="medium"` or larger | Use `size="small"`. Position via `top: -4px; insetInlineEnd: -4px` (upper-right corner overlay). See "Badge display rules" above. |
| Tests pass but production shows 0 counts | Test mock fabricated `@odata.count` field | Mocks MUST use the actual Xrm shape `{entities: Array<...>}`. See `makeEntities(n)` helper in `useRelatedCount.test.ts`. |

## Related

- [`.claude/patterns/pcf/dataverse-queries.md`](dataverse-queries.md) — general Dataverse query rules
- [`.claude/patterns/pcf/pcf-build-scaffold.md`](pcf-build-scaffold.md) — 10 build gotchas from the same UAT saga
- [`.claude/adr/ADR-024-polymorphic-resolver-pattern.md`](../../adr/ADR-024-polymorphic-resolver-pattern.md) — dual-field regarding lookup rationale
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../adr/ADR-028-spaarke-auth-architecture.md) — host-context vs BFF boundary
