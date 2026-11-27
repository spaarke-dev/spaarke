/**
 * SmartTodo child-record writes through the BFF. Ontology task 234 (#3279, D-233): a to-do's status change goes through
 * `PATCH /api/v1/child-records/sprk_todo/{id}` (the shared `updateChildRecordViaBff`), which refuses closing an open
 * inquiry's outcome To Do; its error reaches the caller unchanged (nothing is written as the user through Xrm.WebApi).
 */

const mockUpdateChildRecordViaBff = jest.fn();
const mockCreateChildRecordViaBff = jest.fn();
const mockAuthenticatedFetch = jest.fn();

jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
  initAuth: jest.fn(async () => undefined),
  resolveRuntimeConfig: jest.fn(async () => ({
    msalClientId: 'client',
    bffBaseUrl: 'https://bff.example',
    bffOAuthScope: 'scope',
    tenantId: '',
  })),
}));

jest.mock('@spaarke/ui-components', () => ({
  createChildRecordViaBff: (...args: unknown[]) => mockCreateChildRecordViaBff(...args),
  updateChildRecordViaBff: (...args: unknown[]) => mockUpdateChildRecordViaBff(...args),
}));

import { updateTodoThroughBff } from '../childRecordWrites';

describe('updateTodoThroughBff (task 234)', () => {
  beforeEach(() => jest.clearAllMocks());

  it('PATCHes the to-do through the BFF with the columns given', async () => {
    mockUpdateChildRecordViaBff.mockResolvedValueOnce(undefined);
    await updateTodoThroughBff('t-1', { statecode: 1, statuscode: 2 });
    expect(mockUpdateChildRecordViaBff).toHaveBeenCalledTimes(1);
    const [fetchFn, base, table, id, payload] = mockUpdateChildRecordViaBff.mock.calls[0];
    expect(typeof fetchFn).toBe('function');
    expect(base).toBe('https://bff.example');
    expect(table).toBe('sprk_todo');
    expect(id).toBe('t-1');
    expect(payload).toEqual({ statecode: 1, statuscode: 2 });
  });

  it("rejects with the BFF's error (its reasonCode intact) when the update is refused", async () => {
    const refusal = Object.assign(new Error('refused'), { status: 409, reasonCode: 'inquiry.disposition_required' });
    mockUpdateChildRecordViaBff.mockRejectedValueOnce(refusal);
    await expect(updateTodoThroughBff('t-1', { statecode: 1, statuscode: 659490002 })).rejects.toBe(refusal);
  });
});
