/**
 * Xrm frame-walk access (ADR-026). Web resources run inside Dataverse iframes,
 * so the `Xrm` global may live on `window`, `window.parent`, or `window.top`.
 * All access is null-guarded — the page must not throw when opened outside a
 * Dataverse host (e.g. local `vite dev`).
 */

// ADR-026: use `declare const`-style loose typing for Xrm (no @types/xrm dep).
/* eslint-disable @typescript-eslint/no-explicit-any */
type AnyXrm = any;

/** Locate the Xrm global by walking the frame hierarchy. Returns null if absent. */
export function resolveXrm(): AnyXrm | null {
  if (typeof window === 'undefined') return null;
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top);
  } catch {
    /* cross-origin */
  }
  for (const frame of frames) {
    try {
      const xrm = (frame as any).Xrm;
      if (xrm?.WebApi || xrm?.Navigation) return xrm;
    } catch {
      /* cross-origin */
    }
  }
  return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */
