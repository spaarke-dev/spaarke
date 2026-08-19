/**
 * ConversationPaneChrome.files-availability.test.tsx — spaarkeai-compose-r7 (2026-08-19).
 *
 * Locks the best-effort 24h re-attach signal on the FilesAttachedIndicator chip: when a reopened
 * session's uploaded file has `available: false` (its searchable content was evicted from AI Search
 * after ~24h idle, SessionFilesCleanupJob), the chip renders a dimmed "no longer available" hint so the
 * user is not promised a file the Assistant can no longer recall. `available` absent/true ⇒ usable.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

import { FilesAttachedIndicator, type AttachedFileSummary } from '../ConversationPaneChrome';

function renderIndicator(files: AttachedFileSummary[], theme: Theme = webLightTheme): void {
  render(
    <FluentProvider theme={theme}>
      <FilesAttachedIndicator
        uploadedFileCount={files.length}
        promotedCount={0}
        files={files}
      />
    </FluentProvider>
  );
}

const AVAILABLE: AttachedFileSummary = { id: 'f1', filename: 'NDA.pdf', status: 'ready', available: true };
const UNAVAILABLE: AttachedFileSummary = { id: 'f2', filename: 'Old.pdf', status: 'ready', available: false };

describe('FilesAttachedIndicator — 24h availability signal', () => {
  it('single available file shows its name with NO "no longer available" hint', () => {
    renderIndicator([AVAILABLE]);
    expect(screen.getByText('NDA.pdf')).toBeInTheDocument();
    expect(screen.queryByText(/no longer available/i)).not.toBeInTheDocument();
  });

  it('single UNAVAILABLE file appends "no longer available"', () => {
    renderIndicator([UNAVAILABLE]);
    expect(screen.getByText(/Old\.pdf — no longer available/)).toBeInTheDocument();
  });

  it('treats an absent `available` flag as available (back-compat with the live-upload path)', () => {
    renderIndicator([{ id: 'f3', filename: 'Live.docx', status: 'ready' }]);
    expect(screen.getByText('Live.docx')).toBeInTheDocument();
    expect(screen.queryByText(/no longer available/i)).not.toBeInTheDocument();
  });

  it('multi-file with any unavailable shows "some files no longer available"', () => {
    renderIndicator([AVAILABLE, UNAVAILABLE]);
    expect(screen.getByText('some files no longer available')).toBeInTheDocument();
  });

  it('multi-file all available shows "available for this session"', () => {
    renderIndicator([AVAILABLE, { id: 'f4', filename: 'B.pdf', status: 'ready', available: true }]);
    expect(screen.getByText('available for this session')).toBeInTheDocument();
  });

  it('renders the unavailable variant under dark theme without regression (ADR-021)', () => {
    renderIndicator([UNAVAILABLE], webDarkTheme);
    expect(screen.getByText(/no longer available/i)).toBeInTheDocument();
  });
});
