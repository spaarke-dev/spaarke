/**
 * The `?action=` a ribbon button opens the taskpane with.
 *
 * - `createTodo` — the Outlook "Create To Do" button (smart-todo-decoupling-r3 FR-27 / task 070): the To Do tab.
 * - `submitInvoice` — the Outlook "Submit Invoice" button (word-add-in-r1 task 133): the Invoice view, which has no
 *   tab — this is the only way to reach it.
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
