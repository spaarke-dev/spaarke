import * as React from 'react';

/**
 * How a yielding modal (`SprkModal` `yieldToSidePane`) makes room for the host's native lookup side pane
 * (`Xrm.Utility.lookupObjects`, at the right edge of the window):
 * - `off`: not yielding;
 * - `pending`: looking for the pane (the modal stays where it is, dimmed and inert);
 * - `under`: the pane was found and the modal's page layer now sits just below it, so the pane opens ON TOP of the
 *   modal (owner test 2026-10-07: "the lookup should always be on top of the parent modal", as with the wizards);
 * - `dock`: the pane could not be placed above the modal, so the modal moves left of it instead.
 */
export type SidePaneLayering = 'off' | 'pending' | 'under' | 'dock';

const POLL_MS = 50;
/** About 1.5 s for the platform to render the pane before falling back to docking. */
const MAX_POLLS = 30;

/** The element's ancestor that is a direct child of `<body>` — its page layer (a portal mount node, an app root). */
function pageLayerOf(el: Element): HTMLElement | null {
  const body = el.ownerDocument.body;
  let node: Element | null = el;
  while (node && node.parentElement && node.parentElement !== body) node = node.parentElement;
  return node && node.parentElement === body ? (node as HTMLElement) : null;
}

/**
 * While `active`, finds the side pane that opens after the modal yields and lowers the modal's page layer to just
 * below the pane's. The pane is the topmost new element at the window's right edge (`elementsFromPoint` skips the
 * yielding surface, which has `pointer-events: none`). The layer's own z-index is restored when yielding ends.
 *
 * Falls back to `dock` when the pane cannot be placed above the modal: no `elementsFromPoint` (tests, old hosts), no
 * pane within ~1.5 s, the pane in the modal's own layer, or a pane layer without a numeric z-index above 1 (lowering
 * below 1 could put the modal under the form itself).
 */
export function useSidePaneLayering(active: boolean, surfaceRef: React.RefObject<HTMLElement | null>): SidePaneLayering {
  const [state, setState] = React.useState<SidePaneLayering>('off');

  React.useEffect(() => {
    if (!active) {
      setState('off');
      return undefined;
    }
    const surface = surfaceRef.current;
    const doc = surface?.ownerDocument;
    const view = doc?.defaultView;
    const ownLayer = surface ? pageLayerOf(surface) : null;
    if (!surface || !doc || !view || !ownLayer || typeof doc.elementsFromPoint !== 'function') {
      setState('dock');
      return undefined;
    }

    const probe = (): Element[] => doc.elementsFromPoint(view.innerWidth - 48, Math.round(view.innerHeight / 2));
    const baseline = new Set(probe());
    let restore: (() => void) | null = null;
    let polls = 0;
    setState('pending');

    const timer = view.setInterval(() => {
      polls += 1;
      const pane = probe().find(el => !baseline.has(el) && !ownLayer.contains(el));
      if (pane) {
        view.clearInterval(timer);
        const paneLayer = pageLayerOf(pane);
        const z = paneLayer ? parseInt(view.getComputedStyle(paneLayer).zIndex, 10) : NaN;
        if (paneLayer && paneLayer !== ownLayer && Number.isFinite(z) && z > 1) {
          const previous = ownLayer.style.zIndex;
          ownLayer.style.zIndex = String(z - 1);
          restore = () => {
            ownLayer.style.zIndex = previous;
          };
          setState('under');
        } else {
          setState('dock');
        }
        return;
      }
      if (polls >= MAX_POLLS) {
        view.clearInterval(timer);
        setState('dock');
      }
    }, POLL_MS);

    return () => {
      view.clearInterval(timer);
      restore?.();
    };
  }, [active, surfaceRef]);

  return active ? state : 'off';
}
