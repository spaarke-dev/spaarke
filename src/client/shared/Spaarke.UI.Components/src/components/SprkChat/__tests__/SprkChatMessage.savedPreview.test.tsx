/**
 * SprkChatMessage.savedPreview.test.tsx — spaarkeai-compose-r2 FIX #7a ("Open preview").
 *
 * Proves the per-message "Open preview" affordance on a persistent "Saved to the DMS" message: it
 * renders on a completed ASSISTANT message carrying `metadata.savedPreview` when the host provides
 * `onOpenSavedPreview`, and clicking it hands the saved document id + name to the host (which opens
 * the File Preview modal). Guards the render gates: not without the callback, not without the
 * metadata, not on user messages, not while streaming.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkChatMessage } from '../SprkChatMessage';
import type { IChatMessage } from '../types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const savedMessage: IChatMessage = {
  role: 'Assistant',
  content: "Saved 'Brief.docx' to the DMS.",
  timestamp: '2026-07-13T00:00:00.000Z',
  metadata: { responseType: 'markdown', savedPreview: { documentId: 'doc-123', fileName: 'Brief.docx' } },
};

describe('SprkChatMessage — FIX #7a "Open preview"', () => {
  it('renders on a saved-preview assistant message and passes id + name to onOpenSavedPreview', () => {
    const onOpenSavedPreview = jest.fn();
    renderWithProvider(<SprkChatMessage message={savedMessage} onOpenSavedPreview={onOpenSavedPreview} />);

    const btn = screen.getByTestId('open-saved-preview');
    expect(btn).toHaveTextContent('Open preview');

    fireEvent.click(btn);
    expect(onOpenSavedPreview).toHaveBeenCalledWith('doc-123', 'Brief.docx');
  });

  it('does NOT render when onOpenSavedPreview is absent', () => {
    renderWithProvider(<SprkChatMessage message={savedMessage} />);
    expect(screen.queryByTestId('open-saved-preview')).not.toBeInTheDocument();
  });

  it('does NOT render on a plain message with no savedPreview metadata', () => {
    const onOpenSavedPreview = jest.fn();
    renderWithProvider(
      <SprkChatMessage
        message={{ role: 'Assistant', content: 'hello', timestamp: savedMessage.timestamp }}
        onOpenSavedPreview={onOpenSavedPreview}
      />
    );
    expect(screen.queryByTestId('open-saved-preview')).not.toBeInTheDocument();
  });

  it('does NOT render on user messages or while streaming', () => {
    const onOpenSavedPreview = jest.fn();
    const { rerender } = renderWithProvider(
      <SprkChatMessage message={{ ...savedMessage, role: 'User' }} onOpenSavedPreview={onOpenSavedPreview} />
    );
    expect(screen.queryByTestId('open-saved-preview')).not.toBeInTheDocument();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <SprkChatMessage message={savedMessage} isStreaming onOpenSavedPreview={onOpenSavedPreview} />
      </FluentProvider>
    );
    expect(screen.queryByTestId('open-saved-preview')).not.toBeInTheDocument();
  });
});
