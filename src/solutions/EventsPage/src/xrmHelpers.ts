/**
 * xrmHelpers — minimal Xrm-bridge helpers extracted from the legacy 1868-line
 * App.tsx. The EventsPage runs as a Custom Page web resource inside an iframe
 * — `window.Xrm` is sometimes available locally, sometimes only on
 * `window.parent` or `window.top` depending on the host frame chain.
 *
 * Splitting these into their own module keeps App.tsx + the orchestrator files
 * focused on lifecycle / mounting.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/tasks/031-eventspage-app-tsx-rewrite.poml
 * **Origin**: lifted verbatim from App.tsx getXrm() (legacy L327-358)
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Get the Xrm object from the current window, parent, or top frame.
 *
 * Web resources loaded via URL navigation are in iframes and need to access
 * the parent / top frame's Xrm object. We walk every accessible frame and
 * return the FIRST Xrm whose `App.sidePanes` is defined (the App.sidePanes
 * API is the only one EventsPage genuinely needs — `WebApi` and `Navigation`
 * are always there if `App.sidePanes` is).
 */
export function getXrm(): any | null {
  const localXrm = (globalThis as any).Xrm;
  if (localXrm?.App?.sidePanes) {
    return localXrm;
  }
  try {
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.App?.sidePanes) return parentXrm;
  } catch {
    /* cross-origin */
  }
  try {
    const topXrm = (window.top as any)?.Xrm;
    if (topXrm?.App?.sidePanes) return topXrm;
  } catch {
    /* cross-origin */
  }
  return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */
