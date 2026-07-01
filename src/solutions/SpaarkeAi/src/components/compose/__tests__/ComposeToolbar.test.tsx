/**
 * ComposeToolbar.test.tsx — task 043 unit tests (POML §ui-tests).
 *
 * Verifies the four behaviour contracts from the POML:
 *   1. Toolbar renders three buttons with correct aria-labels (Component Renders).
 *   2. Open-in-Word-Web click invokes `useDocumentActions.openInWeb` with
 *      expected documentId (Open-in-Word-Web Click).
 *   3. Open-in-Word-Desktop click invokes `useDocumentActions.openInDesktop`
 *      with expected documentId.
 *   4. Compose-summarize click dispatches PaneEventBus event on `conversation`
 *      channel with correct contract shape from task 041
 *      (Compose-Summarize Dispatch).
 *
 * NOTE on Dark Mode Compliance (ADR-021 — POML ui-test #2): the component uses
 * Fluent v9 semantic tokens (`tokens.colorNeutralBackground1`,
 * `tokens.colorNeutralForeground1`, `tokens.colorBrandForeground1`,
 * `tokens.colorNeutralStroke2`) and zero hex literals. This is verified by
 * code inspection — jsdom-based unit tests cannot validate computed colors
 * for dark mode toggling (no FluentProvider theme-switch render path).
 * Browser-based ui-test (Step 9.7) covers this when run with `--chrome`.
 *
 * Test category per ADR-038: **Component Tests** (KEEP path
 * `tests/unit/Sprk.Bff.Api.Tests/` is for BFF; SpaarkeAi component tests live
 * alongside their components in `__tests__/` per the convention established by
 * `SendToWorkspaceButton.test.tsx` / `PinToMatterButton.test.tsx`). Tests
 * assert COMPONENT BEHAVIOUR (rendering, event dispatch, callback invocation)
 * — not implementation details (no internal-state assertions, no DI
 * registration tests, no constructor null-checks per the ADR-038 ban list).
 *
 * Mock boundary: `useDocumentActions` is mocked via `jest.mock` at the module
 * level. This is the LEGITIMATE mock boundary per ADR-038 — the hook is an
 * external dependency, and mocking it avoids needing to spin up the
 * `authenticatedFetch` plumbing inside a jsdom unit test. The `useDispatchPaneEvent`
 * hook is NOT mocked — it's tested via the real `PaneEventBus` instance per
 * `SendToWorkspaceButton.test.tsx`'s precedent.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeToolbar.tsx
 * @see projects/spaarkeai-compose-r1/tasks/043-frontend-create-compose-toolbar.poml
 * @see src/solutions/SpaarkeAi/src/components/workspace/__tests__/SendToWorkspaceButton.test.tsx
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import {
  PaneEventBus,
  PaneEventBusProvider,
  type ConversationPaneEvent,
} from '@spaarke/ai-widgets/events';

// Mock `useDocumentActions` BEFORE importing the component under test.
// `mockOpenInWeb` / `mockOpenInDesktop` are jest.fn() instances we'll
// assert against; the rest of the hook surface returns inert defaults.
const mockOpenInWeb = jest.fn().mockResolvedValue(undefined);
const mockOpenInDesktop = jest.fn().mockResolvedValue(undefined);

jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: jest.fn(() => ({
    openInWeb: mockOpenInWeb,
    openInDesktop: mockOpenInDesktop,
    download: jest.fn(),
    deleteDocuments: jest.fn(),
    emailLink: jest.fn(),
    sendToIndex: jest.fn(),
    isActing: false,
    actionError: null,
  })),
}));

import {
  ComposeToolbar,
  type ComposeSummarizeRequestEvent,
} from '../ComposeToolbar';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const FIXED_DOCUMENT_ID = 'doc-abc123';
const FIXED_FILE_NAME = 'Contract Draft v3.docx';
const FIXED_SESSION_ID = 'session-xyz789';
const FIXED_BFF_URL = 'https://bff.example.com';

/**
 * Render the toolbar with a real PaneEventBus + FluentProvider. Returns the
 * bus so the test can subscribe and assert dispatched events.
 */
function renderWithBus(node: React.ReactNode): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{node}</PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

beforeEach(() => {
  mockOpenInWeb.mockClear();
  mockOpenInDesktop.mockClear();
});

// ---------------------------------------------------------------------------
// Tests — POML acceptance criteria
// ---------------------------------------------------------------------------

describe('ComposeToolbar', () => {
  describe('Component Renders (POML ui-test #1)', () => {
    it('renders three buttons with aria-labels', () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          fileName={FIXED_FILE_NAME}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );

      expect(
        screen.getByRole('button', { name: /open in word for web/i }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: /open in word desktop/i }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      ).toBeInTheDocument();
    });

    it('renders a Toolbar with aria-label "Compose document actions"', () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      expect(
        screen.getByRole('toolbar', { name: /compose document actions/i }),
      ).toBeInTheDocument();
    });

    it('disables all three buttons when documentId is empty', () => {
      renderWithBus(
        <ComposeToolbar
          documentId=""
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      expect(
        screen.getByRole('button', { name: /open in word for web/i }),
      ).toBeDisabled();
      expect(
        screen.getByRole('button', { name: /open in word desktop/i }),
      ).toBeDisabled();
      expect(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      ).toBeDisabled();
    });

    it('disables summarize button when sessionId is empty', () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId=""
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      // Open-in-Word buttons remain enabled because they don't require a session.
      expect(
        screen.getByRole('button', { name: /open in word for web/i }),
      ).toBeEnabled();
      expect(
        screen.getByRole('button', { name: /open in word desktop/i }),
      ).toBeEnabled();
      // Summarize requires a session for the dispatch payload's correlation field.
      expect(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      ).toBeDisabled();
    });

    it('disables all buttons when `disabled` prop is true', () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
          disabled
        />,
      );
      expect(
        screen.getByRole('button', { name: /open in word for web/i }),
      ).toBeDisabled();
      expect(
        screen.getByRole('button', { name: /open in word desktop/i }),
      ).toBeDisabled();
      expect(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      ).toBeDisabled();
    });
  });

  describe('Open-in-Word-Web Click (POML ui-test #3)', () => {
    it('invokes useDocumentActions.openInWeb with the expected document id', async () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /open in word for web/i }),
      );

      expect(mockOpenInWeb).toHaveBeenCalledTimes(1);
      expect(mockOpenInWeb).toHaveBeenCalledWith(FIXED_DOCUMENT_ID);
    });

    it('does NOT call openInWeb when toolbar is disabled', async () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
          disabled
        />,
      );
      const user = userEvent.setup();
      const btn = screen.getByRole('button', {
        name: /open in word for web/i,
      });
      // userEvent respects the disabled attribute and will not fire click —
      // but assert explicitly so the contract is documented.
      await user.click(btn);
      expect(mockOpenInWeb).not.toHaveBeenCalled();
    });
  });

  describe('Open-in-Word-Desktop Click', () => {
    it('invokes useDocumentActions.openInDesktop with the expected document id', async () => {
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /open in word desktop/i }),
      );

      expect(mockOpenInDesktop).toHaveBeenCalledTimes(1);
      expect(mockOpenInDesktop).toHaveBeenCalledWith(FIXED_DOCUMENT_ID);
    });
  });

  describe('Compose-Summarize Dispatch (POML ui-test #4)', () => {
    it('dispatches conversation channel event with correct contract shape', async () => {
      const events: ConversationPaneEvent[] = [];
      const { bus } = renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          fileName={FIXED_FILE_NAME}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      bus.subscribe('conversation', (ev) => events.push(ev));

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      );

      expect(events).toHaveLength(1);
      const payload = events[0] as unknown as ComposeSummarizeRequestEvent;
      expect(payload.type).toBe('compose_summarize_request');
      expect(payload.documentRef.documentId).toBe(FIXED_DOCUMENT_ID);
      expect(payload.documentRef.fileName).toBe(FIXED_FILE_NAME);
      expect(payload.jpsScope).toBe('compose-document');
      expect(payload.sessionId).toBe(FIXED_SESSION_ID);
      // ISO-8601 UTC timestamp format
      expect(payload.timestamp).toMatch(
        /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/,
      );
    });

    it('invokes onComposeSummarizeRequest observer with same payload as bus dispatch', async () => {
      const observerEvents: ComposeSummarizeRequestEvent[] = [];
      renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          fileName={FIXED_FILE_NAME}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
          onComposeSummarizeRequest={(p) => observerEvents.push(p)}
        />,
      );

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      );

      expect(observerEvents).toHaveLength(1);
      expect(observerEvents[0].type).toBe('compose_summarize_request');
      expect(observerEvents[0].documentRef.documentId).toBe(FIXED_DOCUMENT_ID);
      expect(observerEvents[0].jpsScope).toBe('compose-document');
      expect(observerEvents[0].sessionId).toBe(FIXED_SESSION_ID);
    });

    it('does NOT dispatch when sessionId is empty (defensive disabled state)', async () => {
      const events: ConversationPaneEvent[] = [];
      const { bus } = renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId=""
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      bus.subscribe('conversation', (ev) => events.push(ev));

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      );
      expect(events).toHaveLength(0);
    });

    it('omits fileName from payload when not provided', async () => {
      const events: ConversationPaneEvent[] = [];
      const { bus } = renderWithBus(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          sessionId={FIXED_SESSION_ID}
          bffBaseUrl={FIXED_BFF_URL}
        />,
      );
      bus.subscribe('conversation', (ev) => events.push(ev));

      const user = userEvent.setup();
      await user.click(
        screen.getByRole('button', { name: /summarize with assistant/i }),
      );

      const payload = events[0] as unknown as ComposeSummarizeRequestEvent;
      expect(payload.documentRef.fileName).toBeUndefined();
    });
  });
});
