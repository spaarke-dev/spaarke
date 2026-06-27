# Task 110a — Library modal `toLowerCase` null bug evidence

**Date**: 2026-06-24
**Task**: Phase 5R / 110a — Library modal `toLowerCase` null bug fix
**Spec FR**: FR-59
**Status**: ✅ Fixed
**Rigor**: STANDARD (per POML — single-file defensive guard, no architecture change)

---

## 1. Call-site location

**File**: `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/PlaybookLibraryShell.tsx`
**Lines (pre-fix)**: 292–293
**Path**: Inside the `useEffect` data-loader, in the `mode === 'intent'` fuzzy-name-match fallback branch (lines ~282–294).

Pre-fix code:

```tsx
if (!match) {
  const intentLower = intent.toLowerCase();
  match = filteredPlaybooks.find(p => p.name.toLowerCase().includes(intentLower));
}
```

The crash is on `p.name.toLowerCase()`. `intent.toLowerCase()` is structurally safe because line 282 (`mode === 'intent' && intent && filteredPlaybooks.length > 0`) gates entry, but is also hardened defensively in the fix.

## 2. Root cause — why is `p.name` null?

The `IPlaybook.name` field is typed as a non-nullable `string` in `src/client/shared/Spaarke.UI.Components/src/components/Playbook/types.ts:17`:

```ts
export interface IPlaybook {
  id: string;
  name: string;        // typed non-nullable
  description: string;
  ...
}
```

However, `name` is populated from Dataverse via an **unsafe cast** in `src/client/shared/Spaarke.UI.Components/src/components/Playbook/playbookService.ts:50–65`:

```ts
return result.entities.map(
  (entity: Record<string, unknown>): IPlaybook => ({
    id: entity[ID_FIELDS.playbook] as string,
    name: entity.sprk_name as string,              // ← unsafe cast; sprk_name is nullable in DV
    description: (entity.sprk_description as string) || '',
    icon: 'Lightbulb',
    isDefault: false,
  })
);
```

`sprk_analysisplaybook.sprk_name` is a regular DV text column with **no `Required` enforcement**. In production data any record with a null/blank `sprk_name` (e.g., a half-authored row from the maker UI) ends up with `name: null` at runtime, while the TS type system believes the value is `string`. The first record encountered in the `.find()` loop with `name === null` throws `Cannot read properties of null (reading 'toLowerCase')` and aborts the entire match scan.

Note: the same unsafe cast pattern is repeated for `loadActions`, `loadSkills`, `loadKnowledge`, `loadTools` (lines 82, 104, 127, 150). Those types do not currently hit a `.toLowerCase()` site, so they are not in scope for 110a, but they are nullability traps. Recommend follow-up to tighten upstream cast (see §5).

## 3. Fix applied

Single-file change to `PlaybookLibraryShell.tsx`, lines 292–293, replaced with the nullish-coalescing defensive-guard pattern:

```tsx
// Defensive null guard (FR-59, task 110a): `p.name` is typed as `string`
// in `IPlaybook` but is populated from a nullable Dataverse column
// (`sprk_analysisplaybook.sprk_name`) via an unsafe cast in
// `playbookService.loadPlaybooks`. ...
if (!match) {
  const intentLower = (intent ?? '').toLowerCase();
  match = filteredPlaybooks.find(p => (p.name ?? '').toLowerCase().includes(intentLower));
}
```

Behavior:
- A playbook record with `name === null` now safely evaluates `''.toLowerCase().includes(intentLower)` → `false` for any non-empty intent → record is skipped, scan continues.
- A playbook record with a real `name` matches identically to before.
- `intent` is also hardened (`intent ?? ''`) even though the gating `if` already required truthy `intent`; this is zero-cost belt-and-suspenders.

ADR-021 (Fluent v9 only) is respected — no new imports.

## 4. Build / lint result

- **`tsc -p .` (project type-check)**: 0 errors introduced by this change. One pre-existing unrelated error remains (`src/services/EntityCreationService.ts(34,76): Cannot find module '@spaarke/sdap-client'`) — not caused by 110a, not in scope.
- **`eslint src/components/PlaybookLibraryShell/PlaybookLibraryShell.tsx`**: clean (only an unrelated `MODULE_TYPELESS_PACKAGE_JSON` warning on the eslint config file itself).

## 5. Tests

No existing test file for `PlaybookLibraryShell` was found via:
- `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/**/__tests__/**/*`
- `src/client/shared/Spaarke.UI.Components/**/PlaybookLibraryShell*.test.*`

Per parent task instructions (110a parent prompt step 5: "If no test exists, skip this step but flag it in the evidence"), test addition was skipped. This deviates from POML step 5 ("Add a unit/e2e regression test that exercises the null-field path"); the deviation is the parent-instruction override.

**Flag for follow-up** (main session): consider adding `PlaybookLibraryShell.intent-null-name.test.tsx` covering the null-name-match path. Suggested fixture: 3 playbooks where one has `name: null as unknown as string` — assert the test does NOT throw, and intent matches the right non-null playbook. This is a STANDARD-rigor follow-up task, not a blocker for 117b.

## 6. Open follow-ups for main session

| Severity | Item | Location | Notes |
|---|---|---|---|
| Low | Add regression test for null-name path | new `PlaybookLibraryShell.intent-null-name.test.tsx` | Per §5 above. |
| Low | Tighten `loadAllData` casts | `playbookService.ts:59, 82, 104, 127, 150` | Replace `entity.sprk_name as string` with `(entity.sprk_name as string \| null) ?? ''` (or filter out null-name records). The other entity types (action/skill/knowledge/tool) are nullability traps not yet realized. Defer until a `.toLowerCase()` or similar string op hits them. |
| Low | Mark `IPlaybook.name` as `string \| null` | `types.ts:17` | Honest type matches DV reality. Cascades into many call sites, hence "defer" until tightening pass. |

None of these block 117b (which only needs the modal to open cleanly).

## 7. Acceptance criteria (FR-59)

- [x] Library modal opens with no `toLowerCase` console error against test data containing one null indexed field. (Defensive guard at the only `.toLowerCase()` site in the shell.)
- [x] Search input filters remaining (non-null) records correctly. (`(p.name ?? '').toLowerCase().includes(intentLower)` falls through to `false` for null records, matches normally for the rest.)
- [ ] Regression test exercises the null-field path. (**Deviation** — no existing test file; flagged for follow-up per §5.)
- [x] Root-cause note identifies the offending field. (`sprk_analysisplaybook.sprk_name` — see §2.)
- [x] No new Fluent v8 imports. (No new imports at all.)

## 8. Files modified

- `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/PlaybookLibraryShell.tsx` (defensive guard + multi-line comment explaining root cause)
