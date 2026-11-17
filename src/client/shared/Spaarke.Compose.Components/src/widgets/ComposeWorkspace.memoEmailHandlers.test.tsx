/**
 * ComposeWorkspace.memoEmailHandlers.test.tsx - ontology task 188 (owner D-219 Q3).
 *
 * "Email memo" opens the canonical composer through `<SendEmailDialog/>`, which ComposeWorkspace mounts
 * unconditionally (controlled by its `open` prop). The composer hides the template picker, the AI sparkle and
 * local-file upload - silently - unless the host passes `onListEmailTemplates` + `onRenderEmailTemplate`,
 * `onDraftWithAi` and `onUploadLocalAttachment`. The contract: all four come from ONE
 * `createXrmEmailComposeHandlers` instance built with the BFF host and `authenticatedFetch`.
 *
 * The dialog and the factory are stand-ins (the composer's gating has its own suites in `@spaarke/ui-components`);
 * ComposeWorkspace is real. Heavy children are mocked as in ComposeWorkspace.search.test.tsx.
 */

import * as React from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

const HANDLERS = {
  onListEmailTemplates: jest.fn(),
  onRenderEmailTemplate: jest.fn(),
  onDraftWithAi: jest.fn(),
  onUploadLocalAttachment: jest.fn(),
};
const createHandlersMock = jest.fn((_options?: unknown) => HANDLERS);
const dialogProps: { current: Record<string, unknown> | undefined } = { current: undefined };

jest.mock('@spaarke/ui-components', () => ({
  ConfirmModal: () => null,
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: (props: Record<string, unknown>) => {
    dialogProps.current = props;
    return null;
  },
  createXrmEmailComposeHandlers: (options?: unknown) => createHandlersMock(options),
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
}));

jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));

jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((_props: unknown, _ref: React.Ref<unknown>) => (
      <div data-testid="compose-editor-stub" />
    )),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(bffBaseUrl = 'https://bff.example.test') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace bffBaseUrl={bffBaseUrl} driveId="" tenantId="tenant-1" />
    </FluentProvider>
  );
}

describe('ComposeWorkspace - memo email composer feature handlers (task 188)', () => {
  beforeEach(() => {
    dialogProps.current = undefined;
    createHandlersMock.mockClear();
  });

  it('passes the template list + render, AI draft and attachment upload handlers to the memo email dialog', () => {
    renderWorkspace();
    expect(dialogProps.current).toBeDefined();
    expect(dialogProps.current!.onListEmailTemplates).toBe(HANDLERS.onListEmailTemplates);
    expect(dialogProps.current!.onRenderEmailTemplate).toBe(HANDLERS.onRenderEmailTemplate);
    expect(dialogProps.current!.onDraftWithAi).toBe(HANDLERS.onDraftWithAi);
    expect(dialogProps.current!.onUploadLocalAttachment).toBe(HANDLERS.onUploadLocalAttachment);
  });

  it('builds ONE factory instance, with the BFF host and the auth-aware fetch (inert without them)', () => {
    const { rerender } = renderWorkspace();
    expect(createHandlersMock).toHaveBeenCalledTimes(1);
    const options = createHandlersMock.mock.calls[0][0] as { bffBaseUrl?: string; authenticatedFetch?: unknown };
    expect(options.bffBaseUrl).toBe('https://bff.example.test');
    expect(typeof options.authenticatedFetch).toBe('function');

    // A re-render with the same host reuses the instance (never a second one in the component).
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" />
      </FluentProvider>
    );
    expect(createHandlersMock).toHaveBeenCalledTimes(1);
  });
});
