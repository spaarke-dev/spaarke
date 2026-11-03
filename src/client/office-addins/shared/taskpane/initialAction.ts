/**
 * The `?action=` a ribbon button opened the taskpane with. Package 1.1.4 (task 133, owner UAT 2026-11-03) removed both
 * buttons — the To Do and Invoice views are pane tabs — so these come only from a cached older manifest.
 *
 * - `createTodo` — the former Outlook "Create To Do" button (smart-todo-decoupling-r3 FR-27 / task 070): the To Do tab.
 * - `submitInvoice` — the former Outlook "Submit Invoice" button (word-add-in-r1 task 133): the Invoice tab.
 *
 * Any other value, or none, is the default pane.
 */
export type InitialAction = 'createTodo' | 'submitInvoice';

/** Reads the action from a taskpane URL's query string (e.g. `window.location.search`). Never throws. */
export function readInitialAction(search: string): InitialAction | undefined {
  try {
    const action = new URLSearchParams(search).get('action');
    return action === 'createTodo' || action === 'submitInvoice' ? action : undefined;
  } catch {
    return undefined;
  }
}
