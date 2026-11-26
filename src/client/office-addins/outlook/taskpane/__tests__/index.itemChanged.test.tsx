/**
 * Task 139 (spaarkeai-word-add-in-r1): the Outlook pane follows the selected email.
 *
 * The manifest sets SupportsPinning, but nothing handled `Office.EventType.ItemChanged`: a pinned pane kept the previous
 * email's state when the user moved to another email, and `mailbox.item` can be null (nothing selected, or several
 * emails selected).
 *
 * The real boot (`outlook/taskpane/index.tsx`), the real `App` and the real `OutlookAdapter` run, against the shared
 * Office.js mock; the shell and the tab views are doubled. The auth service is a spy: a change must make NO auth call.
 *
 * `index.tsx` boots on import, so each test loads it into a fresh module registry; React Testing Library is loaded into
 * the same registry so `act` sees the same React as the pane.
 */
import type { ReactNode } from 'react';

// Names start with `mock`: jest lets a hoisted factory read them.
// Plain records, not jest mocks: the config's `clearMocks` wipes mock call history before every test, and the tests
// that share one boot need what happened during that boot.
const mockCapture: { handler?: (() => void) | undefined; registrations: unknown[][] } = { registrations: [] };

jest.mock('@shared/services', () => ({
  authService: {
    initialize: jest.fn(async () => undefined),
    isAuthenticated: jest.fn(() => true),
    getAccessToken: jest.fn(async () => 'token'),
    getAccount: jest.fn(() => ({ name: 'Guest User', username: 'guest@example.com' })),
    signIn: jest.fn(async () => undefined),
    signOut: jest.fn(),
    clearCache: jest.fn(),
  },
  apiClient: { configure: jest.fn(), get: jest.fn(), post: jest.fn() },
  ApiClientError: class ApiClientError extends Error {},
}));
jest.mock('@shared/taskpane/hooks/useTheme', () => ({
  useTheme: () => ({
    theme: jest.requireActual('@fluentui/react-components').webLightTheme,
    preference: 'system',
    setPreference: jest.fn(),
  }),
}));
jest.mock('@shared/taskpane/hooks', () => ({
  ...jest.requireActual('@shared/taskpane/hooks'),
  useLinkedTodosForCommunication: () => ({ count: 0, isLoading: false, error: null }),
}));
jest.mock('@shared/taskpane/components/TaskPaneShell', () => ({
  TaskPaneShell: ({ children }: { children: ReactNode }) => children,
}));
jest.mock('@shared/taskpane/components/SaveFlow', () => ({
  DEFAULT_SAVED_DOCUMENT_PANE_STATE: { savedDocument: null, profileRefreshSignal: 0, contentChangedSinceSave: false },
}));
jest.mock('@shared/taskpane/components/views/FindView', () => ({ FindView: () => null }));
jest.mock('@shared/taskpane/components/views/CreateTodoView', () => ({ CreateTodoView: () => null }));
jest.mock('@shared/taskpane/services/documentIdentityService', () => ({
  ...jest.requireActual('@shared/taskpane/services/documentIdentityService'),
  resolveEmailIdentity: jest.fn(async () => null),
  identityOfSavedDocument: jest.fn(),
  resolveDocumentIdentity: jest.fn(),
  completeStampIdentity: jest.fn(async (outcome: unknown) => outcome),
}));
// The Save tab's double: shows which email it was opened on, and holds App-owned saved state (`savedState`), so a
// remount of App (not just of this view) is what makes it disappear.
jest.mock('@shared/taskpane/components/views/SaveView', () => {
  // eslint-disable-next-line @typescript-eslint/no-var-requires -- a hoisted jest.mock factory cannot use imports
  const React = require('react');
  return {
    SaveView: (props: {
      hostAdapter: { getItemId: () => Promise<string> };
      savedState: { savedDocument: unknown };
      onSavedStateChange: (state: unknown) => void;
    }) => {
      const [itemId, setItemId] = React.useState('…');
      React.useEffect(() => {
        void props.hostAdapter.getItemId().then(setItemId);
      }, [props.hostAdapter]);
      return (
        <div data-testid="save-view">
          <span>{`item:${itemId}`}</span>
          {props.savedState.savedDocument ? <span>Saved to Spaarke</span> : null}
          <button onClick={() => props.onSavedStateChange({ ...props.savedState, savedDocument: { id: 'doc' } })}>
            Mark saved
          </button>
        </div>
      );
    },
  };
});

// The first boot loads the whole pane (Fluent included) cold.
jest.setTimeout(120000);

const ITEM_CHANGED = 'olkItemSelectedChanged';

const readItem = (id: string) => ({
  itemType: 'message',
  itemId: id,
  subject: `Subject ${id}`,
  from: { emailAddress: 'sender@example.com', displayName: 'Sender' },
  internetMessageId: `<${id}@example.com>`,
});

type Rtl = typeof import('@testing-library/react');

type AuthDouble = Record<'initialize' | 'isAuthenticated' | 'getAccessToken' | 'getAccount' | 'signIn', jest.Mock>;

interface Pane {
  rtl: Rtl;
  mailbox: { item: unknown; addHandlerAsync?: jest.Mock };
  authService: AuthDouble;
  /** Select another email (or none), then fire the host's ItemChanged event. */
  changeItem: (item: unknown) => Promise<void>;
}

async function bootPane(
  options: {
    item?: unknown;
    addHandlerAsync?: jest.Mock | 'missing';
  } = {}
): Promise<Pane> {
  jest.resetModules();
  document.body.innerHTML = '<div id="root"></div>';
  mockCapture.handler = undefined;
  mockCapture.registrations = [];

  const office = global.Office as unknown as Record<string, Record<string, unknown>>;
  office.EventType = { ...office.EventType, ItemChanged: ITEM_CHANGED };
  const addHandlerAsync =
    options.addHandlerAsync === 'missing'
      ? undefined
      : (options.addHandlerAsync ??
        jest.fn((...args: unknown[]) => {
          const [, handler, callback] = args as [string, () => void, ((r: unknown) => void) | undefined];
          mockCapture.registrations.push(args);
          mockCapture.handler = handler;
          callback?.({ status: 'succeeded' });
        }));
  const mailbox: Pane['mailbox'] = {
    item: 'item' in options ? options.item : readItem('AAA'),
    ...(addHandlerAsync ? { addHandlerAsync } : {}),
  };
  (office.context as Record<string, unknown>).mailbox = mailbox;

  /* eslint-disable @typescript-eslint/no-var-requires -- the pane boots on import, so each test loads it fresh */
  const rtl = require('@testing-library/react/pure') as Rtl;
  require('../index');
  // Under a loaded machine (the whole suite in parallel) the pane's first render can take longer than the 1s default.
  rtl.configure({ asyncUtilTimeout: 30000 });
  const { authService } = require('@shared/services') as { authService: AuthDouble };
  /* eslint-enable @typescript-eslint/no-var-requires */

  return {
    rtl,
    mailbox,
    authService,
    changeItem: async item => {
      mailbox.item = item;
      await rtl.act(async () => {
        mockCapture.handler?.();
      });
    },
  };
}

const visible = (rtl: Rtl, text: string) => rtl.screen.queryAllByText(text).filter(el => !el.closest('.sr-only'));

// Booting the pane loads Fluent and the whole App, which is slow, so the tests that only differ in what the user does
// AFTER the pane is up share ONE boot (they run in order and each leaves a working pane). The boot variants below
// (no email at open; ItemChanged cannot be registered) each boot their own.
describe('Outlook pane: ItemChanged', () => {
  let pane: Pane;
  let initializeCallsAtBoot = 0;
  beforeAll(async () => {
    pane = await bootPane();
    initializeCallsAtBoot = pane.authService.initialize.mock.calls.length;
  });
  // `restoreMocks` / `clearMocks` run before every test: give the auth double its answers again.
  beforeEach(() => {
    pane.authService.isAuthenticated.mockImplementation(() => true);
    pane.authService.getAccessToken.mockImplementation(async () => 'token');
    pane.authService.getAccount.mockImplementation(() => ({ name: 'Guest User', username: 'guest@example.com' }));
  });

  it('registers the handler once, after the app has rendered', async () => {
    await pane.rtl.waitFor(() => expect(pane.rtl.screen.getByText('item:AAA')).toBeInTheDocument());

    expect(mockCapture.registrations).toHaveLength(1);
    expect(mockCapture.registrations[0]).toEqual([ITEM_CHANGED, expect.any(Function), expect.any(Function)]);

    await pane.changeItem(readItem('BBB'));
    await pane.changeItem(readItem('CCC'));
    expect(mockCapture.registrations).toHaveLength(1);
  });

  it('restarts the pane on the new email: per-email state is gone, the new email is read', async () => {
    const { screen, waitFor, fireEvent } = pane.rtl;
    // Own starting point (this test must not depend on which tests ran before it).
    await pane.changeItem(readItem('CCC'));
    await waitFor(() => expect(screen.getByText('item:CCC')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Mark saved' }));
    await waitFor(() => expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument());

    await pane.changeItem(readItem('DDD'));

    await waitFor(() => expect(screen.getByText('item:DDD')).toBeInTheDocument());
    expect(screen.queryByText('item:CCC')).not.toBeInTheDocument();
    expect(screen.queryByText('Saved to Spaarke')).not.toBeInTheDocument();
  });

  it('makes no auth call: no sign-in and no auth initialize after the first start', async () => {
    // The first start initialized auth once (counted in beforeAll, before `clearMocks` wipes call history for each
    // test). The calls checked below are the ones made DURING this test, i.e. by the change it makes.
    expect(initializeCallsAtBoot).toBe(1);

    await pane.changeItem(readItem('EEE'));
    await pane.rtl.waitFor(() => expect(pane.rtl.screen.getByText('item:EEE')).toBeInTheDocument());

    expect(pane.authService.initialize).not.toHaveBeenCalled();
    expect(pane.authService.signIn).not.toHaveBeenCalled();
    // And the signed-in user is not shown the Sign in screen.
    expect(pane.rtl.screen.queryByText('Welcome to Spaarke')).not.toBeInTheDocument();
  });

  it('shows the select-an-email message when the new item is null, and Retry opens the email once one is selected', async () => {
    const { screen, waitFor, fireEvent } = pane.rtl;
    // Own starting point (this test must not depend on which tests ran before it).
    await pane.changeItem(readItem('EEE'));
    await waitFor(() => expect(screen.getByText('item:EEE')).toBeInTheDocument());

    await pane.changeItem(null);

    await waitFor(() => expect(visible(pane.rtl, 'Select an email to use Spaarke.')).toHaveLength(1));
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.queryByText('Welcome to Spaarke')).not.toBeInTheDocument();

    // The user selects an email, then presses Retry: the adapter (started with no item) reads its mode now.
    pane.mailbox.item = readItem('CCC');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByText('item:CCC')).toBeInTheDocument());
    expect(visible(pane.rtl, 'Select an email to use Spaarke.')).toHaveLength(0);
    expect(pane.authService.signIn).not.toHaveBeenCalled();
  });

  it('a pane that OPENS with no email says to select one, and Retry opens it', async () => {
    const pane = await bootPane({ item: null });
    const { screen, waitFor, fireEvent } = pane.rtl;

    await waitFor(() => expect(visible(pane.rtl, 'Select an email to use Spaarke.')).toHaveLength(1));

    pane.mailbox.item = readItem('AAA');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByText('item:AAA')).toBeInTheDocument());
  });
});

describe('Outlook pane: ItemChanged cannot be registered', () => {
  let warn: jest.SpyInstance;
  beforeEach(() => {
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  it('works as before when addHandlerAsync is missing', async () => {
    const pane = await bootPane({ addHandlerAsync: 'missing' });

    await pane.rtl.waitFor(() => expect(pane.rtl.screen.getByText('item:AAA')).toBeInTheDocument());
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('ItemChanged'));
  });

  it('works as before when Office reports the registration failed', async () => {
    const failing = jest.fn((_type: string, _handler: () => void, callback?: (r: unknown) => void) => {
      callback?.({ status: 'failed', error: { message: 'not supported' } });
    });
    const pane = await bootPane({ addHandlerAsync: failing });

    await pane.rtl.waitFor(() => expect(pane.rtl.screen.getByText('item:AAA')).toBeInTheDocument());
    expect(failing).toHaveBeenCalledTimes(1);
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('ItemChanged'), expect.anything());
  });

  it('works as before when addHandlerAsync throws', async () => {
    const throwing = jest.fn(() => {
      throw new Error('boom');
    });
    const pane = await bootPane({ addHandlerAsync: throwing });

    await pane.rtl.waitFor(() => expect(pane.rtl.screen.getByText('item:AAA')).toBeInTheDocument());
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('ItemChanged'), expect.any(Error));
  });
});

describe('Outlook pane: the newly selected email cannot be started', () => {
  let error: jest.SpyInstance;
  beforeEach(() => {
    error = jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  it('does not leave the previous email up looking live: it says why, and Retry starts the pane on the new email', async () => {
    const pane = await bootPane();
    const { screen, waitFor, fireEvent } = pane.rtl;
    await waitFor(() => expect(screen.getByText('item:AAA')).toBeInTheDocument());

    // eslint-disable-next-line @typescript-eslint/no-var-requires -- same module registry as the booted pane
    const { OutlookAdapter } = require('@shared/adapters/OutlookAdapter');
    jest
      .spyOn(OutlookAdapter.prototype, 'initialize')
      .mockRejectedValueOnce({ code: 'UNKNOWN_ERROR', message: 'Outlook could not open this email.' });

    await pane.changeItem(readItem('BBB'));

    await waitFor(() => expect(visible(pane.rtl, 'Outlook could not open this email.')).toHaveLength(1));
    expect(screen.queryByText('item:AAA')).not.toBeInTheDocument();
    expect(screen.queryByTestId('save-view')).not.toBeInTheDocument();
    expect(error).toHaveBeenCalled();

    // The next initialize() is the real one.
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByText('item:BBB')).toBeInTheDocument());
    expect(visible(pane.rtl, 'Outlook could not open this email.')).toHaveLength(0);
  });

  it('a slower, earlier failure never replaces what a later change put on screen', async () => {
    const pane = await bootPane();
    const { screen, waitFor } = pane.rtl;
    await waitFor(() => expect(screen.getByText('item:AAA')).toBeInTheDocument());

    // eslint-disable-next-line @typescript-eslint/no-var-requires -- same module registry as the booted pane
    const { OutlookAdapter } = require('@shared/adapters/OutlookAdapter');
    let failFirst: (reason: unknown) => void = () => undefined;
    jest.spyOn(OutlookAdapter.prototype, 'initialize').mockImplementationOnce(
      () =>
        new Promise<void>((_resolve, reject) => {
          failFirst = reject;
        })
    );

    await pane.changeItem(readItem('BBB')); // starts, still pending
    await pane.changeItem(readItem('CCC')); // the real initialize: this one wins
    await waitFor(() => expect(screen.getByText('item:CCC')).toBeInTheDocument());

    await pane.rtl.act(async () => {
      failFirst({ code: 'UNKNOWN_ERROR', message: 'too late' });
    });

    expect(screen.getByText('item:CCC')).toBeInTheDocument();
    expect(visible(pane.rtl, 'too late')).toHaveLength(0);
  });
});
