/**
 * ComposeWorkspace.activeWorkType.test.tsx — ai-advanced-capabilities-analysis-hub-r1 task 041
 * (FR-13).
 *
 * `ComposeEditor` already fully implements `activeWorkType` (documented prop, threaded into the
 * ALREADY-SHIPPED `getToolsForSurface`, exercised end-to-end in the sibling real-mount test
 * `ComposeEditor.activeWorkType.test.tsx`). The gap this task closes is that `ComposeWorkspace` —
 * the host component that actually mounts `<ComposeEditor>` (line ~2365) — did not expose or
 * forward this prop at all. This test proves the fix: `ComposeWorkspace`'s own `activeWorkType`
 * prop reaches `<ComposeEditor>` unchanged, and omitting it preserves `ComposeEditor`'s own `'*'`
 * (unscoped) default — no regression for any pre-existing mount.
 *
 * Mocking strategy mirrors `ComposeWorkspace.browse.test.tsx` (heavy children stubbed to keep the
 * test on the prop-forwarding seam under test, not TipTap/BFF internals).
 */

import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

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
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ComposeToolbar: (_props: any) => <div data-testid="compose-toolbar-stub" />,
}));

// ── ComposeEditor — capture the `activeWorkType` prop it is handed ─────────
const editorActiveWorkType: { current: string | undefined } = { current: undefined };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { docxBytes: ArrayBuffer | null; activeWorkType?: string }, ref: React.Ref<unknown>) => {
        editorActiveWorkType.current = props.activeWorkType;
        ReactLib.useImperativeHandle(ref, () => ({
          serialize: async () => new ArrayBuffer(0),
          getCounts: () => ({ characters: 0, words: 0 }),
          isDirty: () => true,
          materializeComposeDraft: () => undefined,
          materializePendingRedline: () => 'applied',
        }));
        return <div data-testid="compose-editor-stub" />;
      }
    ),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(
  props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {},
  theme: typeof webLightTheme = webLightTheme
) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

async function mountEditorViaBlankPage(): Promise<void> {
  fireEvent.click(screen.getByTestId('compose-empty-state-blank'));
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [], text: async () => '' });
  editorActiveWorkType.current = undefined;
});

describe('ComposeWorkspace — activeWorkType host prop (task 041, FR-13)', () => {
  it('Agreement Review scopes palette: forwards activeWorkType="agreement-analysis" to ComposeEditor unchanged', async () => {
    renderWorkspace({ activeWorkType: 'agreement-analysis' });
    await mountEditorViaBlankPage();

    expect(editorActiveWorkType.current).toBe('agreement-analysis');
  });

  it("Default is unscoped: omitting activeWorkType forwards undefined so ComposeEditor's own '*' default applies (no regression)", async () => {
    renderWorkspace();
    await mountEditorViaBlankPage();

    expect(editorActiveWorkType.current).toBeUndefined();
  });

  it('ADR-021 dark-mode: an agreement-analysis-scoped workspace mounts cleanly under the dark theme', async () => {
    renderWorkspace({ activeWorkType: 'agreement-analysis' }, webDarkTheme);
    await mountEditorViaBlankPage();

    expect(editorActiveWorkType.current).toBe('agreement-analysis');
    expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument();
  });
});
