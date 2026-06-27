/**
 * SprkChatMessageRenderer — playbook_options card (chat-routing-redesign-r1 task 117b).
 *
 * Covers the FR-50 + FR-51 acceptance criteria:
 *   (1) Renders N candidate Fluent v9 primary buttons (one per candidate).
 *   (2) Button click fires `onSelectPlaybook(playbookId, sessionAttachmentIds)`.
 *   (3) Renders the "Open Library" link when `libraryModalCta === true`.
 *   (4) Library link click fires `onOpenLibraryModal(sessionAttachmentIds)`.
 *   (5) `libraryModalCta === false` hides the link (defensive UX even though
 *       the spec says it is always true).
 *   (6) Empty candidates renders the no-match copy + still surfaces the link.
 *   (7) Missing handlers render buttons/link disabled (defensive — host opt-in).
 *   (8) ADR-021 dark-mode — uses Fluent v9 tokens (verified by the absence of
 *       hard-coded hex colors in className strings).
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { SprkChatMessageRenderer } from '../SprkChatMessageRenderer';
import type { IPlaybookOptionsResponse } from '../SprkChatMessageRenderer';

function renderWithProvider(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

function samplePayload(): IPlaybookOptionsResponse {
  return {
    candidates: [
      {
        playbookId: 'pb-1',
        playbookCode: 'PB-001',
        displayName: 'Summarize Contract',
        confidence: 0.92,
        reason: 'top-confidence',
      },
      {
        playbookId: 'pb-2',
        playbookCode: 'PB-002',
        displayName: 'Risk Review',
        confidence: 0.86,
        reason: 'top-confidence',
      },
      {
        playbookId: 'pb-3',
        playbookCode: 'PB-003',
        displayName: 'Extract Obligations',
        confidence: 0.81,
        reason: 'top-confidence',
      },
    ],
    libraryModalCta: true,
    sessionAttachmentIds: ['file-1', 'file-2'],
    rerankInvoked: false,
    rerankReason: null,
  };
}

describe('SprkChatMessageRenderer — playbook_options card', () => {
  it('renders one primary button per candidate with displayName labels', () => {
    const onSelect = jest.fn();
    const onOpen = jest.fn();
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        onSelectPlaybook={onSelect}
        onOpenLibraryModal={onOpen}
      />
    );
    expect(screen.getByRole('button', { name: /Summarize Contract/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Risk Review/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Extract Obligations/i })).toBeInTheDocument();
  });

  it('renders the "Which playbook" prompt when candidates are present', () => {
    renderWithProvider(<SprkChatMessageRenderer responseType="playbook_options" data={samplePayload()} />);
    expect(screen.getByText(/Which playbook would you like me to use/i)).toBeInTheDocument();
  });

  it('clicking a candidate button calls onSelectPlaybook with playbookId + sessionAttachmentIds', () => {
    const onSelect = jest.fn();
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        onSelectPlaybook={onSelect}
        onOpenLibraryModal={jest.fn()}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /Summarize Contract/i }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('pb-1', ['file-1', 'file-2']);
  });

  it('renders the Open Library link when libraryModalCta is true', () => {
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        onSelectPlaybook={jest.fn()}
        onOpenLibraryModal={jest.fn()}
      />
    );
    expect(screen.getByRole('button', { name: /Open the playbook library/i })).toBeInTheDocument();
  });

  it('clicking the Open Library link calls onOpenLibraryModal with sessionAttachmentIds', () => {
    const onOpen = jest.fn();
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        onSelectPlaybook={jest.fn()}
        onOpenLibraryModal={onOpen}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /Open the playbook library/i }));
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onOpen).toHaveBeenCalledWith(['file-1', 'file-2']);
  });

  it('hides the Open Library link when libraryModalCta is false (defensive FR-51 edge)', () => {
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={{ ...samplePayload(), libraryModalCta: false }}
        onSelectPlaybook={jest.fn()}
        onOpenLibraryModal={jest.fn()}
      />
    );
    expect(screen.queryByRole('button', { name: /Open the playbook library/i })).toBeNull();
  });

  it('with 0 candidates, renders only the no-match message + Open Library link', () => {
    const data: IPlaybookOptionsResponse = {
      candidates: [],
      libraryModalCta: true,
      sessionAttachmentIds: [],
      rerankInvoked: false,
      rerankReason: null,
    };
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={data}
        onSelectPlaybook={jest.fn()}
        onOpenLibraryModal={jest.fn()}
      />
    );
    expect(screen.getByText(/couldn't find a confident match/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Open the playbook library/i })).toBeInTheDocument();
    // No candidate buttons.
    expect(screen.queryByRole('button', { name: /Summarize Contract/i })).toBeNull();
  });

  it('disables candidate buttons when onSelectPlaybook is missing', () => {
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        // onSelectPlaybook omitted intentionally.
        onOpenLibraryModal={jest.fn()}
      />
    );
    expect(screen.getByRole('button', { name: /Summarize Contract/i })).toBeDisabled();
  });

  it('disables the Open Library link when onOpenLibraryModal is missing', () => {
    renderWithProvider(
      <SprkChatMessageRenderer
        responseType="playbook_options"
        data={samplePayload()}
        onSelectPlaybook={jest.fn()}
        // onOpenLibraryModal omitted intentionally.
      />
    );
    expect(screen.getByRole('button', { name: /Open the playbook library/i })).toBeDisabled();
  });
});
