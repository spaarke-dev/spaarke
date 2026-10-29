/**
 * Task 133: the ribbon's `?action=` parsing. `submitInvoice` is the ONLY way to reach the Invoice view.
 */
import { readInitialAction } from '../initialAction';

describe('readInitialAction (task 133)', () => {
  it('?action=submitInvoice opens the Invoice view', () => {
    expect(readInitialAction('?action=submitInvoice')).toBe('submitInvoice');
  });

  it('?action=createTodo is unchanged', () => {
    expect(readInitialAction('?action=createTodo')).toBe('createTodo');
  });

  it.each(['', '?', '?action=', '?action=invoice', '?action=SUBMITINVOICE', '?other=submitInvoice'])(
    '%j is the default pane',
    search => {
      expect(readInitialAction(search)).toBeUndefined();
    }
  );
});
