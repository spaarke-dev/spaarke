/**
 * Launch-seam contract for the "create from this email" actions (task 113 / UAT R3 C11-3).
 *
 * Protects: all three flows (Create Event / Create To Do / Link Invoice) route through
 * ONE seam that opens an in-app MODAL via `navigateTo` `{ pageType:'entityrecord',
 * target: 2 }` at the correct target entity — so OOB↔custom is swappable without editing
 * the button call sites.
 */

import { launchCreate, CREATE_LAUNCH_TARGETS, type CreateKind } from '../CommunicationActions/launchCreate';

describe('launchCreate seam', () => {
  it('maps each kind to the correct target entity + title', () => {
    expect(CREATE_LAUNCH_TARGETS.event.entityName).toBe('sprk_event');
    expect(CREATE_LAUNCH_TARGETS.todo.entityName).toBe('sprk_todo');
    expect(CREATE_LAUNCH_TARGETS.invoice.entityName).toBe('sprk_invoice');
    expect(CREATE_LAUNCH_TARGETS.event.title).toBe('Create Event');
    expect(CREATE_LAUNCH_TARGETS.todo.title).toBe('Create To Do');
    expect(CREATE_LAUNCH_TARGETS.invoice.title).toBe('Link Invoice');
  });

  it.each<CreateKind>(['event', 'todo', 'invoice'])(
    'opens %s as an in-app modal dialog (entityrecord + target:2) at the mapped entity',
    kind => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      launchCreate(kind, { navigateTo });

      expect(navigateTo).toHaveBeenCalledTimes(1);
      const [pageInput, navOptions] = navigateTo.mock.calls[0];
      expect(pageInput).toEqual({ pageType: 'entityrecord', entityName: CREATE_LAUNCH_TARGETS[kind].entityName });
      expect(navOptions.target).toBe(2); // in-app centered dialog (MODAL), not a new page/tab
      expect(navOptions.position).toBe(1);
      expect(navOptions.title).toBe(CREATE_LAUNCH_TARGETS[kind].title);
    }
  );

  it('carries defaultValues as the create-form pre-seed when provided', () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    launchCreate('todo', { navigateTo, defaultValues: { sprk_subject: 'From email' } });

    const [pageInput] = navigateTo.mock.calls[0];
    expect(pageInput.data).toEqual({ sprk_subject: 'From email' });
  });

  it('does not pre-seed when no defaultValues are supplied (parity with prior nav)', () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    launchCreate('event', { navigateTo });

    const [pageInput] = navigateTo.mock.calls[0];
    expect(pageInput).not.toHaveProperty('data');
  });

  it('falls back to the host Xrm.Navigation.navigateTo when none is injected (frame-walk resolver)', () => {
    // jest.setup registers a global Xrm with a navigateTo mock — the resolver should find it.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const hostNav = (global as any).Xrm.Navigation.navigateTo as jest.Mock;
    hostNav.mockClear();
    expect(() => launchCreate('invoice', {})).not.toThrow();
    expect(hostNav).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = hostNav.mock.calls[0];
    expect(pageInput).toEqual({ pageType: 'entityrecord', entityName: 'sprk_invoice' });
    expect(navOptions.target).toBe(2);
  });

  it('routes a rejected navigation (e.g. user cancel) to onError without throwing', async () => {
    const onError = jest.fn();
    const navigateTo = jest.fn().mockRejectedValue(new Error('cancelled'));
    launchCreate('event', { navigateTo, onError });
    // Allow the rejected promise microtask to settle.
    await Promise.resolve();
    await Promise.resolve();
    expect(onError).toHaveBeenCalledTimes(1);
  });
});
