/**
 * Record-template wiring shared by the lists and the message thread that own a create dialog
 * (spaarke-SPA-external-access-platform-r4 task 004, FR-03).
 *
 * Inside the record template the create entry point is the toolbar (Upload, New message, Add to-do, Add event), not the
 * list's own button. A list opts in by receiving a `CreateActionReporter` from its section: it then renders no create
 * button, tells the template when its create action is available (`useOfferCreateAction`, hide don't disable) and keeps
 * rendering its own dialog, which is therefore a React descendant of the parent record modal. Outside the template (the
 * project page and the other hosts) no reporter is passed and the list's own button works as before.
 *
 * The availability rule stays where it was: each list passes the same boolean its own button used.
 */
import * as React from 'react';

/** A create action a list offers to the template's toolbar. Structurally the template's `RecordHostAction`. */
export interface CreateActionOffer {
  visible: boolean;
  onClick: () => void;
  /**
   * Task 006 (FR-06 / FR-07): the right behind the action as the list knows it from the BFF's answers (`createRightOf`):
   * `true` / `false`, or `null` while unknown (loading, failed, not said). The template's "Your access" and the sections'
   * read-only lines are derived from it; the toolbar shows the action only when it is `true`.
   */
  right?: boolean | null;
}

/** What a list calls to offer (or, with `null`, withdraw) its create action. */
export type CreateActionReporter = (action: CreateActionOffer | null) => void;

/**
 * Offers `open` to the toolbar while `visible`, withdraws it when it goes and when the list unmounts. `open` is read
 * through a ref, so a new closure never re-reports the action; the report changes only when `visible` or `right` change.
 */
export function useOfferCreateAction(
  report: CreateActionReporter | undefined,
  visible: boolean,
  open: () => void,
  right?: boolean | null
): void {
  const openRef = React.useRef(open);
  openRef.current = open;
  const reportRef = React.useRef(report);
  reportRef.current = report;
  const reporting = report !== undefined;
  React.useEffect(() => {
    if (!reporting) return;
    reportRef.current?.({ visible, onClick: () => openRef.current(), right });
  }, [reporting, visible, right]);
  React.useEffect(
    () => () => {
      reportRef.current?.(null);
    },
    []
  );
}

/** Where focus goes back to when a child dialog closes: the control that opened it, else the record dialog around it. */
export interface FocusTarget {
  element: HTMLElement;
  /** The dialog the control sat in (the parent record dialog, inside the template); null when it sat on the page. */
  dialog: HTMLElement | null;
}

/**
 * Moves focus back once a child dialog has gone. Fluent restores focus only to a `DialogTrigger`; a dialog opened from a
 * toolbar button (a controlled `open`) leaves focus on the page body when it unmounts, and an Esc pressed there reaches no
 * dialog, so the Esc that should close the record could not. The control that opened the dialog gets focus back; when that
 * control is gone (the list withdrew its action after a failed re-read, say) the dialog it sat in gets it instead, so the
 * keyboard stays inside the record. The same holds when the control cannot take focus (disabled, hidden), and when it is
 * still on screen as the child closes but removed right after (task 006 fix round 1): the re-read that withdraws a list's
 * create action closes its dialog in one render and drops the toolbar button in another, so the button can take focus
 * and then vanish under it. A no-op when neither is still on screen.
 */
export function restoreFocusTo(target: FocusTarget | null): void {
  if (!target) return;
  window.setTimeout(() => {
    const { element, dialog } = target;
    if (element.isConnected) {
      element.focus();
      if (document.activeElement === element) {
        if (dialog) keepFocusInDialogIfRemoved(element, dialog);
        return;
      }
    }
    focusDialog(dialog);
  }, 0);
}

/** Focuses the dialog the opener sat in, if it is still on screen. */
function focusDialog(dialog: HTMLElement | null): void {
  if (!dialog || !dialog.isConnected) return;
  // The dialog surface is not tabbable by itself; give it a programmatic tab stop so it can hold focus (Esc then reaches it).
  if (!dialog.hasAttribute('tabindex')) dialog.setAttribute('tabindex', '-1');
  dialog.focus();
}

/**
 * While `element` holds the focus it was given back, watches for its removal: if it leaves the page and focus fell to the
 * page body with it, the dialog it sat in gets focus. Stops once focus moves to another element, or the element or the
 * dialog has gone.
 */
function keepFocusInDialogIfRemoved(element: HTMLElement, dialog: HTMLElement): void {
  if (typeof MutationObserver === 'undefined') return;
  const observer = new MutationObserver(() => {
    if (!dialog.isConnected) {
      stop();
      return;
    }
    if (element.isConnected) return;
    stop();
    const active = document.activeElement;
    if (!active || active === document.body) focusDialog(dialog);
  });
  const onFocusOut = (e: FocusEvent): void => {
    // Focus moved to another element: nothing to rescue. (A removal moves it nowhere: relatedTarget is null.)
    if (e.relatedTarget) stop();
  };
  function stop(): void {
    observer.disconnect();
    element.removeEventListener('focusout', onFocusOut);
  }
  observer.observe(dialog, { childList: true, subtree: true });
  element.addEventListener('focusout', onFocusOut);
}

/** The element that has focus now, for `restoreFocusTo` later (null when focus is on the page body or nowhere). */
export function currentFocusTarget(): FocusTarget | null {
  const active = document.activeElement;
  if (!(active instanceof HTMLElement) || active === document.body) return null;
  return { element: active, dialog: active.closest<HTMLElement>('[role=dialog], [role=alertdialog]') };
}

/**
 * The open state of one child dialog. `show` remembers the control that opened it and `hide` closes the dialog and
 * returns focus to that control (the toolbar button, inside the template).
 */
export function useChildDialog(): { open: boolean; show: () => void; hide: () => void } {
  const [open, setOpen] = React.useState(false);
  const invoker = React.useRef<FocusTarget | null>(null);
  const show = React.useCallback(() => {
    invoker.current = currentFocusTarget();
    setOpen(true);
  }, []);
  const hide = React.useCallback(() => {
    setOpen(false);
    const element = invoker.current;
    invoker.current = null;
    restoreFocusTo(element);
  }, []);
  return { open, show, hide };
}
