/**
 * openEmailRecord.ts
 *
 * Pattern B launcher for the standalone Email code page (`sprk_emailpage`).
 *
 * There are two ways a SPECIFIC `sprk_communication` record ends up in the
 * shared `EmailWorkspace` surface:
 *
 *   • Pattern A (embedded) — the Email code page is embedded on the record's
 *     own form; `main.tsx` resolves the host form's record and pre-selects it.
 *     No launcher is involved.
 *
 *   • Pattern B (navigate-to-open) — a caller elsewhere in the app (e.g. an
 *     "open email" icon in the Messages app) wants to POP the Email surface
 *     open for one record. That caller invokes THIS helper instead of
 *     `Xrm.Navigation.openForm(...)`: it opens the `sprk_emailpage` web
 *     resource as a centered modal dialog, carrying the communication id in the
 *     `data` envelope so `main.tsx` pre-selects it. Pass `{ single: true }` to
 *     open it in single-record ("form") mode (list pane hidden — reads like a
 *     per-record form rather than the list+reading workspace).
 *
 * Xrm-coupled by design (like `createXrmEmailComposeHandlers` in this same
 * folder): it resolves `window.Xrm` via the shared cross-frame `getXrm()`
 * walker. Best-effort — outside a Model-Driven App host (Storybook, unit tests,
 * a non-MDA SPA) it no-ops with a `console.warn` rather than throwing.
 *
 * @see src/solutions/EmailPage/src/main.tsx — the receiving code page (reads
 *   `data` → `id` → host form, in that order).
 */
import { getXrm } from '../../services/xrmGlobal';
import { OOB_MODAL_SIZES } from '../../utils/adapters/oobModalSizes';

/**
 * Deployed web-resource name for the Email code page. Type Webpage/HTML,
 * mirrors the DailyBriefing `sprk_dailyupdate` convention — the built
 * `dist/index.html` is renamed to `dist/sprk_emailpage.html` at package time
 * (see `src/solutions/EmailPage/package.json`) and registered under this name.
 * The `webresourceName` passed to `navigateTo` drops the `.html`, matching the
 * verified sibling constants (`sprk_notepad` / `sprk_smarttodo`).
 */
export const EMAIL_PAGE_WEBRESOURCE_NAME = 'sprk_emailpage';

export interface OpenEmailRecordOptions {
  /**
   * Open in single-record ("form") mode — the Email surface hides its left list
   * pane and renders ONLY the reading pane for this record. Default (`false`):
   * the full list+reading workspace opens with this record pre-selected.
   */
  single?: boolean;
}

/**
 * Open the Email code page for a specific `sprk_communication` record as a
 * centered modal dialog (Pattern B).
 *
 * Best-effort: resolves nothing / no-ops with a `console.warn` when there is no
 * `Xrm.Navigation.navigateTo` (non-MDA host) or when `communicationId` is blank.
 *
 * @param communicationId - `sprk_communication` GUID (braces + casing are
 *   normalised here — callers may pass the raw Xrm form of the id).
 * @param options - `{ single: true }` to launch in single-record ("form") mode.
 *
 * @example
 * // Messages app "open email" icon:
 * await openEmailRecord(row.sprk_communicationid);          // list+reading, pre-selected
 * await openEmailRecord(row.sprk_communicationid, { single: true }); // single-record form
 */
export async function openEmailRecord(communicationId: string, options?: OpenEmailRecordOptions): Promise<void> {
  const id = (communicationId ?? '').replace(/[{}]/g, '').trim().toLowerCase();
  if (!id) {
    console.warn('[openEmailRecord] No communication id supplied — nothing to open.');
    return;
  }

  const xrm = getXrm();
  if (typeof xrm?.Navigation?.navigateTo !== 'function') {
    console.warn(
      '[openEmailRecord] Xrm.Navigation.navigateTo is unavailable — this launcher only works inside a Model-Driven App host. No-op.'
    );
    return;
  }

  // Pattern B `data` envelope: the bare communication id, with an optional
  // `&single=1` flag folded in for single-record mode. `main.tsx` reads the
  // `data` param first (splitting the id from the flag), then falls back to a
  // raw `id` param, then the embedded host form.
  const data = options?.single ? `${id}&single=1` : id;

  try {
    // Call navigateTo DIRECTLY on Xrm.Navigation — never via an extracted local
    // alias (that strips the `this` binding and makes the call a silent no-op;
    // see the extensive gotcha notes in `useRecordHeaderToolbarActions.ts`).
    await xrm.Navigation.navigateTo(
      {
        pageType: 'webresource',
        webresourceName: EMAIL_PAGE_WEBRESOURCE_NAME,
        data,
      },
      {
        target: 2, // modal dialog
        position: 1, // centered
        // `record` OOB size (85%×85%) — this webresource proxies an existing
        // sprk_communication "record" view (record-modal-selection.md
        // invariant; spec FR-11/FR-18, task 090); was ad-hoc 80%×80%.
        width: OOB_MODAL_SIZES.record.width,
        height: OOB_MODAL_SIZES.record.height,
      }
    );
  } catch (err) {
    console.warn('[openEmailRecord] navigateTo failed:', err);
  }
}
