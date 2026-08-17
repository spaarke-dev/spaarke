/**
 * OOB Modal Size Scale — the single source of truth for `Xrm.Navigation.navigateTo`
 * dialog dimensions (spec FR-11 / FR-18, spaarke-modal-system P7 task 090).
 *
 * Every OOB `navigateTo` dialog launch across Spaarke (records, entity CREATE
 * forms, Code Page wizards) sizes itself from one of these named constants —
 * never an inline width/height percentage literal at the call site.
 *
 *  - `record`     85% x 85% — opening an EXISTING entity record as a modal
 *                 (Layout 1 / the record-modal-selection.md invariant). MUST NOT
 *                 vary by entity — see `.claude/patterns/ui/record-modal-selection.md`.
 *  - `createForm` 70% x 80% — an OOB entity CREATE form opened as a modal
 *                 (e.g. `navigateToEntityRecordSurfaceAsync`'s "To Do route").
 *  - `wizard`     60% x 70% — a Code Page wizard/webresource dialog (matches
 *                 the seven Get-Started wizards already shipped in
 *                 `wizardLaunchers.ts`).
 *  - `fullCover`  100% x 100% — the DOCUMENTED 4th-size escalation (smart-todo-r5
 *                 task 032, spec FR-13 / F-7, owner decision 2026-08-14 in
 *                 `projects/smart-todo-r5/design.md` "F-7 Option 1": "bump inner
 *                 dialog to 100%×100% (fully covers the outer 85% modal so it
 *                 reads as 'replace, not nest')"). Used ONLY by
 *                 `navigateToEntityRecordSurfaceAsync`'s `sprk_todo` main-form
 *                 launcher (both its create and open branches) — the launcher
 *                 that can be invoked from WITHIN an already-modal SmartTodo
 *                 Code Page (LegalWorkspace's zero-selection Open path opens the
 *                 Code Page itself at 85%×85%; a card-open inside it then nests
 *                 this inner dialog — see `projects/smart-todo-r5/notes/task-032-fullcover-header.md`
 *                 for the single-modal vs modal-in-modal analysis). This is a
 *                 narrow, project-scoped exception to the `record` 85%×85%
 *                 "MUST NOT vary by entity" invariant (CLAUDE.md §6.5 Path A) —
 *                 every OTHER `record`-sized consumer in the codebase is
 *                 untouched; only this one dedicated `sprk_todo` launcher opts
 *                 into `fullCover`.
 *
 * Consumed by the two sanctioned OOB launch hubs:
 *  - `xrmNavigationServiceAdapter.ts` (records/dialogs — `openRecordModal`)
 *  - `WorkspaceShell/wizardLaunchers.ts` (wizards + entity-create hand-offs)
 *
 * React-free by design (NFR-04): this file imports nothing from `react` (not
 * even a type-only import), so it is safe to import from a React-16/17 PCF
 * control (`@types/react` 18) via a deep `dist/` import, or a React-19 Code
 * Page via the main barrel — verified by a dedicated unit test.
 *
 *  - PCF consumers: `@spaarke/ui-components/dist/utils/adapters/oobModalSizes`
 *    (the established deep-import precedent — see ADR-012 "PCF Import Pattern"
 *    and task 070's `dist/pcf-safe` precedent).
 *  - Code Page / shared-lib consumers: the main `@spaarke/ui-components` barrel.
 *
 * @see docs/standards/MODAL-DECISION-CRITERIA.md
 * @see .claude/patterns/ui/record-modal-selection.md
 * @see projects/spaarke-modal-system/spec.md FR-11, FR-18
 */

/** A single Xrm.Navigation dialog dimension, expressed as a percentage of the viewport. */
export interface OobSizeDimension {
  readonly value: number;
  readonly unit: '%';
}

/**
 * Width + height pair. Assignable directly to `Xrm.Navigation.navigateTo`'s
 * `DialogOptions`/`EntityFormOptions` `width`/`height` fields (see
 * `types/serviceInterfaces.ts`'s `DialogOptions`).
 */
export interface OobModalSize {
  readonly width: OobSizeDimension;
  readonly height: OobSizeDimension;
}

/**
 * The sanctioned OOB modal size names (spec FR-11 + FR-13). Originally a
 * closed 3-name set ("no fourth size" — see
 * `projects/spaarke-modal-system/notes/oob-navigateto-inventory.md` "flagged
 * for visual review"); `fullCover` was added as the documented 4th-size
 * escalation this file's own contract anticipates, per smart-todo-r5 task 032
 * (spec FR-13). Any FUTURE 5th name follows the same bar: a launch site that
 * genuinely needs a different size documents the escalation here (doc comment
 * + notes/ entry), not a silent inline literal at the call site.
 */
export type OobModalSizeName = 'record' | 'createForm' | 'wizard' | 'fullCover';

/**
 * The OOB size scale — ONE source of truth (ADR-012). Every named size is
 * frozen; do not mutate. Do NOT hardcode a width/height percentage at any
 * `navigateTo` call site — import a named size from here instead.
 */
export const OOB_MODAL_SIZES: Readonly<Record<OobModalSizeName, OobModalSize>> = Object.freeze({
  record: Object.freeze({
    width: Object.freeze({ value: 85, unit: '%' as const }),
    height: Object.freeze({ value: 85, unit: '%' as const }),
  }),
  createForm: Object.freeze({
    width: Object.freeze({ value: 70, unit: '%' as const }),
    height: Object.freeze({ value: 80, unit: '%' as const }),
  }),
  wizard: Object.freeze({
    width: Object.freeze({ value: 60, unit: '%' as const }),
    height: Object.freeze({ value: 70, unit: '%' as const }),
  }),
  fullCover: Object.freeze({
    width: Object.freeze({ value: 100, unit: '%' as const }),
    height: Object.freeze({ value: 100, unit: '%' as const }),
  }),
});

/**
 * Returns the named OOB modal size. Prefer this helper at call sites so the
 * intent reads clearly (e.g. `getOobModalSize('wizard')`) rather than an
 * unexplained object literal.
 *
 * @param name - One of the sanctioned OOB modal size names.
 */
export function getOobModalSize(name: OobModalSizeName): OobModalSize {
  return OOB_MODAL_SIZES[name];
}
