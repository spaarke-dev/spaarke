/** @jest-environment ../../client/shared/Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * The toolbar's "Email" action names each To Do's due DAY (spaarke-ontology-platform-r1 task 106).
 *
 * sprk_todo.sprk_duedate is Dataverse Date Only: the Web API returns "YYYY-MM-DD". `new Date("YYYY-MM-DD")` is UTC
 * midnight — the PREVIOUS day in New York — so the e-mail body said "due 6/11/2026" for a To Do due 6/12.
 *
 * Runs in America/New_York through the @jest-environment on line 1 (a UTC runner would pass trivially; the guard
 * below fails there).
 */

// The dependency-free date module runs for real; the package barrels are not needed by the action under test.
jest.mock('@spaarke/ui-components', () => ({
  ...jest.requireActual('../../../../../../client/shared/Spaarke.UI.Components/src/utils/dateLocal'),
}));
jest.mock('@spaarke/smart-todo-components', () => ({ computeDueLabel: jest.fn() }));

import { createToolbarActions, type ToolbarActionContext } from '../ToolbarActions';
import type { ITodo } from '../../../types/entities';

const todo = (id: string, name: string, due?: string): ITodo =>
  ({ sprk_todoid: id, sprk_name: name, sprk_duedate: due, statuscode: 1, statecode: 0, sprk_todopinned: false }) as ITodo;

function emailBody(selected: ITodo[]): string {
  let href = '';
  const ctx: ToolbarActionContext = {
    webApi: { deleteRecord: jest.fn(), updateRecord: jest.fn() },
    getSelectedTodos: () => selected,
    onAfterMutate: jest.fn(),
    onClearSelection: jest.fn(),
    confirm: () => true,
    navigate: (h: string) => {
      href = h;
    },
  };
  createToolbarActions(ctx).handleEmail();
  return decodeURIComponent(href);
}

describe('handleEmail — due dates are calendar days (America/New_York)', () => {
  it('the harness is behind UTC (guard is meaningful)', () => {
    expect(new Date('2026-06-12').getDate()).toBe(11);
  });

  it('a Date Only due date of 2026-06-12 is written as that day', () => {
    const body = emailBody([todo('a', 'Review draft', '2026-06-12')]);
    expect(body).toContain(`- Review draft (due ${new Date(2026, 5, 12).toLocaleDateString()})`);
    expect(body).not.toContain(new Date(2026, 5, 11).toLocaleDateString());
  });

  it('a To Do without a due date has no "(due …)" suffix', () => {
    expect(emailBody([todo('b', 'Call client')])).toContain('- Call client');
    expect(emailBody([todo('b', 'Call client')])).not.toContain('(due');
  });
});
