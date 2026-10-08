/**
 * todoWizardHostSupport.test.ts — the To Do host support shared by the `sprk_createtodowizard` code
 * page and InAppWizardHost (spaarke-ontology-platform-r1 task 112). The broadcast is the contract the
 * LegalWorkspace To Do widget relies on to refetch after a create (R4 task 100 / W-2).
 *
 * Classification (ADR-038 §7): MAINTAIN.
 */
import type { IDataService } from '../../../types/serviceInterfaces';
import { createMockDataService } from '../../../__mocks__/mockDataService';
import {
  SPRK_TODO_CHANNEL_NAME,
  SPRK_TODO_CREATED,
  resolveCurrentUserContact,
  withTodoCreatedBroadcast,
} from '../todoWizardHostSupport';

type Posted = { name: string; message: unknown };

describe('withTodoCreatedBroadcast', () => {
  const posted: Posted[] = [];
  const RealBroadcastChannel = (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel;

  beforeEach(() => {
    posted.length = 0;
    (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = class {
      constructor(private readonly name: string) {}
      postMessage(message: unknown) {
        posted.push({ name: this.name, message });
      }
      close() {}
    };
  });
  afterEach(() => {
    (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = RealBroadcastChannel;
  });

  it('posts sprk_todo:created on the lifecycle channel after a successful sprk_todo create', async () => {
    const inner = createMockDataService();
    inner.createRecord.mockResolvedValue('todo-1');
    const wrapped = withTodoCreatedBroadcast(inner);
    await expect(wrapped.createRecord('sprk_todo', { sprk_name: 'x' })).resolves.toBe('todo-1');
    expect(posted).toEqual([{ name: SPRK_TODO_CHANNEL_NAME, message: { type: SPRK_TODO_CREATED, todoId: 'todo-1' } }]);
    expect(SPRK_TODO_CHANNEL_NAME).toBe('sprk_todo:lifecycle');
    expect(SPRK_TODO_CREATED).toBe('sprk_todo:created');
  });

  it('does not broadcast for other entities, and passes every other operation through to the inner service', async () => {
    const inner = createMockDataService();
    inner.createRecord.mockResolvedValue('doc-1');
    const wrapped = withTodoCreatedBroadcast(inner);
    await wrapped.createRecord('sprk_document', {});
    await wrapped.retrieveRecord('sprk_todo', 'id-1');
    expect(posted).toEqual([]);
    expect(inner.retrieveRecord).toHaveBeenCalledWith('sprk_todo', 'id-1');
    // Inherits (Object.create) rather than spreads, so markers on the inner service stay visible.
    expect(Object.getPrototypeOf(wrapped)).toBe(inner);
  });
});

describe('resolveCurrentUserContact', () => {
  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  });

  it("returns the current user's contact", async () => {
    (window as unknown as { Xrm?: unknown }).Xrm = {
      WebApi: {},
      Utility: { getGlobalContext: () => ({ userSettings: { userId: '{AB-1}' } }) },
    };
    const ds = createMockDataService() as jest.Mocked<IDataService>;
    ds.retrieveMultipleRecords.mockResolvedValue({ entities: [{ contactid: 'c-1', fullname: 'Pat' }] });
    await expect(resolveCurrentUserContact(ds)).resolves.toEqual({ contactId: 'c-1', contactName: 'Pat' });
    expect(ds.retrieveMultipleRecords.mock.calls[0][1]).toContain('_sprk_systemuser_value eq AB-1');
  });

  it('returns undefined when there is no Xrm user or the query fails', async () => {
    const ds = createMockDataService() as jest.Mocked<IDataService>;
    await expect(resolveCurrentUserContact(ds)).resolves.toBeUndefined();
    (window as unknown as { Xrm?: unknown }).Xrm = {
      WebApi: {},
      Utility: { getGlobalContext: () => ({ userSettings: { userId: 'u-1' } }) },
    };
    ds.retrieveMultipleRecords.mockRejectedValue(new Error('boom'));
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    await expect(resolveCurrentUserContact(ds)).resolves.toBeUndefined();
    warn.mockRestore();
  });
});
