/**
 * focusReturn.ts - give keyboard focus back to an element after something else had it (task 199, generalised by task 200).
 *
 * The platform lookup side pane (Xrm.Utility.lookupObjects) hands focus to <body>, or to whatever opened the
 * surrounding dialog (a grid link behind the wizard), when it closes, so a keyboard user loses their place. A trigger
 * that awaits such a lookup calls `returnFocus` once the lookup settles. The same shape fits any "put focus back on X
 * unless focus is somewhere meaningful" case (a modal that closes returns focus to what opened it).
 *
 * Internal to `@spaarke/ui-components` on purpose: it is not exported from the package index, because no consumer
 * outside the package needs it (the PCFs and code pages get the behaviour through the components that use it).
 *
 * How it behaves
 *  - Deferred: the host clears its "yielding" state on resolve, and the trigger may re-render into a different element
 *    (empty field -> selected chip), so the attempt runs on a timer, and `target` is evaluated at that moment, never
 *    captured earlier.
 *  - The document and window come from the surface's own `ownerDocument`, not the globals, so a surface inside an
 *    iframe (the Console, a PCF) restores focus in its own frame.
 *  - Skips when the surface is gone or when `isKept(active)` says focus is already somewhere that must not be
 *    disturbed (inside the host dialog, put there by the host or the user).
 *  - Verifies the focus landed and retries on a short bounded schedule (FOCUS_RETURN_MAX_ATTEMPTS x
 *    FOCUS_RETURN_RETRY_MS): cross-frame, the parent PCF may lift `inert` from its yielding modal after the first tick,
 *    and focus() inside an inert frame fails silently. It stops at once if focus moved to some other real element
 *    since the previous attempt (a host that deliberately focused something is not fought; body/null counts as
 *    "nobody has it").
 *  - A surface in an iframe (the Document Relationship Viewer inside Semantic Search's Find Similar dialog) needs three
 *    more things (task 238): the frame's window takes focus before the element does (`element.focus()` inside a frame
 *    that does not hold the top-level focus only sets that document's activeElement, and the keys still go elsewhere),
 *    a landing counts only once `doc.hasFocus()`, and the schedule is longer (FRAMED_FOCUS_RETURN_MAX_ATTEMPTS) because
 *    the parent page un-yields its dialog and the platform pane hands focus back on their own timers, which can run
 *    past the first window. After it lands a framed surface is checked once more (FRAMED_FOCUS_CONFIRM_MS) and retried
 *    if the frame lost focus in between; the "do not fight a host that chose another element" rule is unchanged, and
 *    it extends to the top page: a frame that lacks the top-level focus takes it back only while the top document's
 *    activeElement is null, <body> or an <iframe> (what is left after the platform pane closes). A real element there
 *    (the closing pane's own input, a button in the parent dialog) skips that attempt without a focus call and the
 *    schedule goes on, counted against the same budget, so a pane that lets go is followed by a re-focus and a choice
 *    that stands is never taken; an unreadable top document (cross-origin) ends the retries.
 */

import * as React from 'react';

/** Bounded retry for returning focus: up to 10 attempts, 50 ms apart. */
export const FOCUS_RETURN_MAX_ATTEMPTS = 10;
export const FOCUS_RETURN_RETRY_MS = 50;
/** A framed surface retries for up to ~1.5 s: its parent frame and the platform pane settle on their own timers. */
export const FRAMED_FOCUS_RETURN_MAX_ATTEMPTS = 30;
/** After a framed surface gets focus, how long before it checks that the frame still holds it. */
export const FRAMED_FOCUS_CONFIRM_MS = 150;

type MaybeElement = HTMLElement | null | undefined;

/** True when `win` is a frame inside another window (an iframe), not the top-level window. */
function isFramedWindow(win: Window): boolean {
  try {
    return win.top !== null && win.top !== win;
  } catch {
    return true; // a top window we may not even compare against is certainly not this one
  }
}

type TopPageFocus = 'free' | 'busy' | 'unreadable';

/**
 * Framed surface whose frame lacks the top-level focus: may we take it back? 'free' when the top page's focused
 * element is "nobody" (null or <body>) or an iframe (what the page shows once the platform pane has closed); 'busy'
 * when a real element holds it (the closing pane's own input, a button in the parent dialog, a platform dialog) - this
 * attempt is skipped and a later one looks again; 'unreadable' when the top document cannot be read (cross-origin) -
 * not fought, the retries end.
 */
function topPageFocus(win: Window): TopPageFocus {
  try {
    const topDoc = win.top?.document;
    if (!topDoc) return 'unreadable';
    const topActive = topDoc.activeElement;
    return !topActive || topActive === topDoc.body || topActive.tagName === 'IFRAME' ? 'free' : 'busy';
  } catch {
    return 'unreadable';
  }
}

export interface FocusReturnSpec {
  /** The document that owns the surface. Capture it before awaiting: after unmount the owner frame cannot be derived. */
  doc: Document;
  /** Where focus goes. Evaluated on every attempt, so a control that re-rendered into another element is found fresh. */
  target: () => MaybeElement;
  /** Used when `target` is missing, detached or disabled. With none, the attempt counts and retries. */
  fallback?: () => MaybeElement;
  /** True when focus is already where it should stay (inside the host dialog, on purpose): nothing is moved. */
  isKept?: (active: Element | null) => boolean;
  /** False once the surface unmounted: the pending attempt stops. */
  isActive?: () => boolean;
}

/** Starts the deferred, verified focus return. Returns a cancel function (call it on unmount or to supersede). */
export function scheduleFocusReturn(spec: FocusReturnSpec): () => void {
  const { doc } = spec;
  const win = doc.defaultView ?? window;
  const framed = isFramedWindow(win);
  const maxAttempts = framed ? FRAMED_FOCUS_RETURN_MAX_ATTEMPTS : FOCUS_RETURN_MAX_ATTEMPTS;
  let attempts = 0;
  let skipped = 0; // framed only: attempts passed over because the top page had a real element focused
  let landed = false; // framed only: focus landed once and the next step confirms the frame still holds it
  let cancelled = false;
  let timerId: number | undefined;
  let seenAfterAttempt: Element | null = null; // what had focus right after our previous attempt

  const schedule = (delayMs: number): void => {
    timerId = win.setTimeout(step, delayMs);
  };

  const step = (): void => {
    timerId = undefined;
    if (cancelled || spec.isActive?.() === false) return;
    const active = doc.activeElement;
    // A frame without the top-level focus keeps nothing: its activeElement may sit on the composer (it survives the
    // frame losing focus, and the pane that took focus away has just closed) while the keys go elsewhere.
    const frameHoldsFocus = !framed || doc.hasFocus();
    if (!frameHoldsFocus) {
      // The top page has a real element focused: the closing pane (it lets go shortly) or a choice that stands (the
      // user clicked in the parent dialog). Take nothing now; look again on the next attempt, within the same budget.
      const top = topPageFocus(win);
      if (top === 'unreadable') return;
      if (top === 'busy') {
        skipped += 1;
        if (attempts + skipped < maxAttempts) schedule(FOCUS_RETURN_RETRY_MS);
        return;
      }
    }
    if (frameHoldsFocus && spec.isKept?.(active)) return;
    // Retry only: focus is somewhere other than where our last attempt left it, and not "nowhere".
    if (attempts > 0 && active !== seenAfterAttempt && active && active !== doc.body) return;

    const wanted = spec.target();
    const usable = wanted && wanted.isConnected && !(wanted as HTMLButtonElement).disabled;
    const element = usable ? wanted : spec.fallback?.();
    attempts += 1;
    if (element && element.isConnected) {
      // Framed: hand the frame the top-level focus first, or element.focus() only records it in this document.
      if (framed) win.focus();
      element.focus();
    }
    // Recorded even when nothing could be focused yet (control disabled or not re-rendered): that is what the next
    // attempt compares against, so a link the pane left focus on is not mistaken for "a host chose this".
    seenAfterAttempt = doc.activeElement;
    if (element && (seenAfterAttempt === element || spec.isKept?.(seenAfterAttempt)) && (!framed || doc.hasFocus())) {
      if (!framed || landed) return;
      landed = true; // framed: look once more after the parent frame and the platform have settled
      schedule(FRAMED_FOCUS_CONFIRM_MS);
      return;
    }
    landed = false;
    if (attempts + skipped < maxAttempts) schedule(FOCUS_RETURN_RETRY_MS);
  };

  schedule(0);
  return () => {
    cancelled = true;
    if (timerId !== undefined) win.clearTimeout(timerId);
    timerId = undefined;
  };
}

/** `region` for a surface that sits in a dialog: the closest `[role=dialog]` holding it, else the surface itself. */
export function closestDialogOrSelf(el: HTMLElement | null | undefined): HTMLElement | null {
  if (!el) return null;
  return el.closest<HTMLElement>('[role="dialog"]') ?? el;
}

export interface FocusReturnScope {
  /** The surface the lookup belongs to. Its ownerDocument carries the focus; null means it unmounted. */
  getRoot: () => MaybeElement;
  /** Focus held inside this region is "on purpose" and is left alone. Default: the root. */
  getRegion?: () => MaybeElement;
  /** Where focus goes when the trigger is gone or disabled. Default: none. */
  getFallback?: () => MaybeElement;
}

/**
 * React binding. Returns `returnFocus(trigger)`: call it when the lookup has settled (after the state updates it
 * causes), with the control to focus (an element, or a function that finds it after the re-render). A newer call
 * supersedes the pending one; unmount cancels it. `scope` is read at call time, so it may close over refs declared later.
 */
export function useFocusReturn(scope: FocusReturnScope): (trigger: MaybeElement | (() => MaybeElement)) => void {
  const scopeRef = React.useRef(scope);
  scopeRef.current = scope;
  const mountedRef = React.useRef(true);
  const cancelPendingRef = React.useRef<(() => void) | null>(null);

  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      cancelPendingRef.current?.();
      cancelPendingRef.current = null;
    };
  }, []);

  return React.useCallback((trigger: MaybeElement | (() => MaybeElement)): void => {
    // Captured now: after unmount the root is null and the owner frame can no longer be derived.
    const doc = scopeRef.current.getRoot()?.ownerDocument ?? document;
    cancelPendingRef.current?.();
    cancelPendingRef.current = scheduleFocusReturn({
      doc,
      isActive: () => mountedRef.current && !!scopeRef.current.getRoot(),
      target: () => (typeof trigger === 'function' ? trigger() : trigger),
      fallback: () => scopeRef.current.getFallback?.(),
      isKept: active => {
        const current = scopeRef.current;
        const region = (current.getRegion ?? current.getRoot)();
        return !!region && !!active && region.contains(active);
      },
    });
  }, []);
}
