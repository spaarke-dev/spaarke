/**
 * ComposeToolbar.test.tsx — unit tests.
 *
 * Verifies the two behaviour contracts after the AI-side retirement
 * (see cleanup PR — Summarize button removed; AI dispatch flows through
 * the Assistant pane via R7 LinearConsumers):
 *   1. Toolbar renders Open-in-Word-Web + Open-in-Word-Desktop buttons.
 *   2. Open-in-Word clicks invoke the `useDocumentActions` handlers with
 *      the expected documentId.
 *   3. Save button appears only when `onSaveRequested` is provided and
 *      responds to click.
 *
 * Test category per ADR-038: Component Tests. Mock boundary: `useDocumentActions`
 * only (external hook — legitimate mock boundary).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

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

import { ComposeToolbar } from '@spaarke/compose-components/widgets/ComposeToolbar';

const FIXED_DOCUMENT_ID = 'doc-abc123';
const FIXED_BFF_URL = 'https://bff.example.com';

function renderToolbar(node: React.ReactNode): void {
  render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

beforeEach(() => {
  mockOpenInWeb.mockClear();
  mockOpenInDesktop.mockClear();
});

describe('ComposeToolbar', () => {
  it('renders Open-in-Word-Web and Open-in-Word-Desktop buttons', () => {
    renderToolbar(
      <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl={FIXED_BFF_URL} />
    );

    expect(screen.getByRole('button', { name: /open in word for web/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /open in word desktop/i })).toBeInTheDocument();
  });

  it('does NOT render a Summarize button (AI dispatch moved to Assistant pane)', () => {
    renderToolbar(
      <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl={FIXED_BFF_URL} />
    );

    expect(screen.queryByRole('button', { name: /summarize/i })).not.toBeInTheDocument();
  });

  it('invokes openInWeb with documentId on click', async () => {
    const user = userEvent.setup();
    renderToolbar(
      <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl={FIXED_BFF_URL} />
    );

    await user.click(screen.getByRole('button', { name: /open in word for web/i }));

    expect(mockOpenInWeb).toHaveBeenCalledTimes(1);
    expect(mockOpenInWeb).toHaveBeenCalledWith(FIXED_DOCUMENT_ID);
  });

  it('invokes openInDesktop with documentId on click', async () => {
    const user = userEvent.setup();
    renderToolbar(
      <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl={FIXED_BFF_URL} />
    );

    await user.click(screen.getByRole('button', { name: /open in word desktop/i }));

    expect(mockOpenInDesktop).toHaveBeenCalledTimes(1);
    expect(mockOpenInDesktop).toHaveBeenCalledWith(FIXED_DOCUMENT_ID);
  });

  it('disables Open-in-Word buttons when documentId is empty', () => {
    renderToolbar(
      <ComposeToolbar documentId="" bffBaseUrl={FIXED_BFF_URL} />
    );

    expect(screen.getByRole('button', { name: /open in word for web/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /open in word desktop/i })).toBeDisabled();
  });

  it('disables Open-in-Word buttons when bffBaseUrl is empty', () => {
    renderToolbar(
      <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl="" />
    );

    expect(screen.getByRole('button', { name: /open in word for web/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /open in word desktop/i })).toBeDisabled();
  });

  it('disables all buttons when disabled prop is true', () => {
    renderToolbar(
      <ComposeToolbar
        documentId={FIXED_DOCUMENT_ID}
        bffBaseUrl={FIXED_BFF_URL}
        disabled
      />
    );

    expect(screen.getByRole('button', { name: /open in word for web/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /open in word desktop/i })).toBeDisabled();
  });

  describe('Save button', () => {
    it('does not render when onSaveRequested is not provided', () => {
      renderToolbar(
        <ComposeToolbar documentId={FIXED_DOCUMENT_ID} bffBaseUrl={FIXED_BFF_URL} />
      );

      expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
    });

    it('renders and invokes onSaveRequested when isDirty', async () => {
      const user = userEvent.setup();
      const onSaveRequested = jest.fn();

      renderToolbar(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          bffBaseUrl={FIXED_BFF_URL}
          onSaveRequested={onSaveRequested}
          isDirty
        />
      );

      const save = screen.getByRole('button', { name: /save changes/i });
      expect(save).not.toBeDisabled();

      await user.click(save);
      expect(onSaveRequested).toHaveBeenCalledTimes(1);
    });

    it('is disabled when not isDirty', () => {
      renderToolbar(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          bffBaseUrl={FIXED_BFF_URL}
          onSaveRequested={jest.fn()}
          isDirty={false}
        />
      );

      expect(screen.getByRole('button', { name: /save changes/i })).toBeDisabled();
    });

    it('shows "Saving…" label when isSaving is true', () => {
      renderToolbar(
        <ComposeToolbar
          documentId={FIXED_DOCUMENT_ID}
          bffBaseUrl={FIXED_BFF_URL}
          onSaveRequested={jest.fn()}
          isDirty
          isSaving
        />
      );

      const save = screen.getByRole('button', { name: /saving/i });
      expect(save).toBeInTheDocument();
      expect(save).toBeDisabled();
    });
  });
});
