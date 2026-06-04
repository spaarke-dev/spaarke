/**
 * Cross-frame Xrm global access for framework code that needs the MDA-provided
 * `window.Xrm`. Walks `globalThis` → `window.parent` → `window.top` so the
 * lookup succeeds whether the consumer is mounted directly under MDA, inside
 * a single iframe, or inside a sandboxed sub-iframe.
 *
 * Returns `null` when Xrm is unavailable (Storybook, unit tests, non-MDA hosts).
 * Callers MUST handle the null case — typically by no-op'ing the operation
 * (e.g. side-pane registration silently does nothing when there's no Xrm).
 *
 * **Why this lives in `@spaarke/ui-components`**: task 035 UAT iteration 5
 * generalized the side-pane infrastructure from EventsPage into the framework.
 * The side-pane orchestrator + side-pane lifecycle utilities all need cross-
 * frame Xrm access; lifting this helper out of EventsPage's host code lets
 * every consumer share the same walker.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
export function getXrm(): any {
  try {
    const g: any = globalThis as any;
    if (g?.Xrm?.WebApi) return g.Xrm;
  } catch {
    /* same-origin only */
  }
  try {
    const w: any = window as any;
    if (w?.parent?.Xrm?.WebApi) return w.parent.Xrm;
  } catch {
    /* cross-origin */
  }
  try {
    const w: any = window as any;
    if (w?.top?.Xrm?.WebApi) return w.top.Xrm;
  } catch {
    /* cross-origin */
  }
  return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */
