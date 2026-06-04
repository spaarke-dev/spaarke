/**
 * RichFilePreview — extracted renderer core tests.
 *
 * Verifies the renderer extracted in R5 task 013 (D2-08) preserves the
 * pre-extraction behavior of `RichFilePreviewDialog`:
 *   - title-bar render, iframe render on URL resolve
 *   - loading spinner, error + retry
 *   - Prev/Next visibility gating on `navigationTotal > 1`
 *   - ArrowLeft/ArrowRight keydown nav + INPUT-focus guard
 *   - metadata Tags + Details render
 *   - default vs override `disabledActions`
 *   - `findSimilar` visibility gating on `onFindSimilar` callback
 *
 * @see ADR-021 - Fluent UI v9; semantic tokens
 * @see ADR-022 - React 19 compatible
 */

import * as React from 'react';
import { screen, waitFor, act, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RichFilePreview, type IRichFilePreviewProps } from '../RichFilePreview';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const makeDeferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(res => {
    resolve = res;
  });
  return { promise, resolve };
};

const defaultProps = (overrides?: Partial<IRichFilePreviewProps>): IRichFilePreviewProps => ({
  documentName: 'Contract.pdf',
  documentId: 'doc-1',
  documentType: 'Contract',
  createdBy: 'Alice',
  createdAt: '2026-01-15T00:00:00Z',
  fileSize: 2048,
  fetchPreviewUrl: jest.fn().mockResolvedValue('https://example.com/preview/doc-1'),
  onOpenFile: jest.fn(),
  onOpenRecord: jest.fn(),
  onEmailDocument: jest.fn(),
  onCopyLink: jest.fn(),
  ...overrides,
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RichFilePreview', () => {
  describe('title bar', () => {
    it('renders the document name', async () => {
      const props = defaultProps();
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByText('Contract.pdf')).toBeInTheDocument();
    });

    it('falls back to placeholder when documentName is empty', async () => {
      const props = defaultProps({ documentName: '' });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByText('Document Preview')).toBeInTheDocument();
    });
  });

  describe('preview-URL fetch lifecycle', () => {
    it('shows the loading spinner while fetching', () => {
      const deferred = makeDeferred<string | null>();
      const props = defaultProps({ fetchPreviewUrl: () => deferred.promise });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByLabelText(/Loading preview/i)).toBeInTheDocument();
    });

    it('renders the iframe when fetch resolves to a URL', async () => {
      const props = defaultProps();
      const { container } = renderWithProviders(<RichFilePreview {...props} />);
      await waitFor(() => {
        const iframe = container.querySelector('iframe');
        expect(iframe).not.toBeNull();
        expect(iframe!.getAttribute('src')).toBe('https://example.com/preview/doc-1');
      });
    });

    it('renders error + Retry when fetch resolves to null', async () => {
      const props = defaultProps({ fetchPreviewUrl: jest.fn().mockResolvedValue(null) });
      renderWithProviders(<RichFilePreview {...props} />);
      await waitFor(() => {
        expect(screen.getByText('Preview not available')).toBeInTheDocument();
      });
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    });

    it('Retry re-invokes fetchPreviewUrl', async () => {
      const user = userEvent.setup();
      const fetcher = jest
        .fn()
        .mockResolvedValueOnce(null)
        .mockResolvedValueOnce('https://example.com/preview/retry');
      const props = defaultProps({ fetchPreviewUrl: fetcher });
      const { container } = renderWithProviders(<RichFilePreview {...props} />);
      await waitFor(() => {
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
      });
      await user.click(screen.getByRole('button', { name: 'Retry' }));
      await waitFor(() => {
        const iframe = container.querySelector('iframe');
        expect(iframe).not.toBeNull();
      });
      expect(fetcher).toHaveBeenCalledTimes(2);
    });
  });

  describe('Prev/Next nav', () => {
    it('hides nav when navigationTotal is omitted', () => {
      const props = defaultProps();
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.queryByRole('group', { name: 'Document navigation' })).toBeNull();
    });

    it('hides nav when navigationTotal is 1', () => {
      const props = defaultProps({ navigationTotal: 1, currentIndex: 0, onNavigate: jest.fn() });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.queryByRole('group', { name: 'Document navigation' })).toBeNull();
    });

    it('renders Prev/Next + counter when navigationTotal > 1', () => {
      const props = defaultProps({ navigationTotal: 3, currentIndex: 1, onNavigate: jest.fn() });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByRole('group', { name: 'Document navigation' })).toBeInTheDocument();
      expect(screen.getByText('2 of 3')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Previous document' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Next document' })).toBeEnabled();
    });

    it('disables Previous at index 0', () => {
      const props = defaultProps({ navigationTotal: 3, currentIndex: 0, onNavigate: jest.fn() });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByRole('button', { name: 'Previous document' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Next document' })).toBeEnabled();
    });

    it('disables Next at last index', () => {
      const props = defaultProps({ navigationTotal: 3, currentIndex: 2, onNavigate: jest.fn() });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByRole('button', { name: 'Previous document' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Next document' })).toBeDisabled();
    });

    it('Previous click dispatches onNavigate(currentIndex - 1)', async () => {
      const user = userEvent.setup();
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 2, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      await user.click(screen.getByRole('button', { name: 'Previous document' }));
      expect(onNavigate).toHaveBeenCalledWith(1);
    });

    it('Next click dispatches onNavigate(currentIndex + 1)', async () => {
      const user = userEvent.setup();
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 0, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      await user.click(screen.getByRole('button', { name: 'Next document' }));
      expect(onNavigate).toHaveBeenCalledWith(1);
    });
  });

  describe('keyboard nav', () => {
    it('ArrowRight dispatches onNavigate(currentIndex + 1) when nav enabled', async () => {
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 0, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      act(() => {
        fireEvent.keyDown(document, { key: 'ArrowRight' });
      });
      expect(onNavigate).toHaveBeenCalledWith(1);
    });

    it('ArrowLeft dispatches onNavigate(currentIndex - 1) when nav enabled', async () => {
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 1, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      act(() => {
        fireEvent.keyDown(document, { key: 'ArrowLeft' });
      });
      expect(onNavigate).toHaveBeenCalledWith(0);
    });

    it('does NOT dispatch when keydown target is an INPUT', () => {
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 1, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      const input = document.createElement('input');
      document.body.appendChild(input);
      input.focus();
      act(() => {
        fireEvent.keyDown(input, { key: 'ArrowRight' });
      });
      expect(onNavigate).not.toHaveBeenCalled();
      document.body.removeChild(input);
    });

    it('does NOT dispatch when keydown target is a TEXTAREA', () => {
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 1, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      const ta = document.createElement('textarea');
      document.body.appendChild(ta);
      ta.focus();
      act(() => {
        fireEvent.keyDown(ta, { key: 'ArrowRight' });
      });
      expect(onNavigate).not.toHaveBeenCalled();
      document.body.removeChild(ta);
    });

    it('does NOT dispatch when keydown target is contentEditable', () => {
      const onNavigate = jest.fn();
      const props = defaultProps({ navigationTotal: 3, currentIndex: 1, onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      const div = document.createElement('div');
      div.contentEditable = 'true';
      document.body.appendChild(div);
      div.focus();
      act(() => {
        fireEvent.keyDown(div, { key: 'ArrowRight' });
      });
      expect(onNavigate).not.toHaveBeenCalled();
      document.body.removeChild(div);
    });

    it('does NOT attach the keydown listener when nav is disabled', () => {
      const onNavigate = jest.fn();
      // No navigationTotal — listener should not fire.
      const props = defaultProps({ onNavigate });
      renderWithProviders(<RichFilePreview {...props} />);
      act(() => {
        fireEvent.keyDown(document, { key: 'ArrowRight' });
      });
      expect(onNavigate).not.toHaveBeenCalled();
    });
  });

  describe('metadata pane', () => {
    it('renders Tags section with the documentType chip', () => {
      const props = defaultProps({ documentType: 'NDA' });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByText('Tags')).toBeInTheDocument();
      expect(screen.getByText('NDA')).toBeInTheDocument();
    });

    it('renders Details with formatted date + size + created-by', () => {
      const props = defaultProps({
        createdBy: 'Alice Example',
        createdAt: '2026-01-15T00:00:00Z',
        fileSize: 2048,
        documentType: 'Contract',
      });
      renderWithProviders(<RichFilePreview {...props} />);
      expect(screen.getByText('Details')).toBeInTheDocument();
      expect(screen.getByText('Created by')).toBeInTheDocument();
      expect(screen.getByText('Alice Example')).toBeInTheDocument();
      expect(screen.getByText('2.0 KB')).toBeInTheDocument();
    });

    it('formats missing fields as em-dash', () => {
      const props = defaultProps({
        createdBy: null,
        createdAt: null,
        fileSize: null,
        documentType: undefined,
      });
      renderWithProviders(<RichFilePreview {...props} />);
      // Multiple em-dashes are expected (Created by, Created, Size, Type)
      const emDashes = screen.getAllByText('—');
      expect(emDashes.length).toBeGreaterThanOrEqual(4);
    });
  });

  describe('3-dot menu — disabled actions', () => {
    it('hides findSimilar by default when onFindSimilar is not provided', async () => {
      const user = userEvent.setup();
      const props = defaultProps();
      renderWithProviders(<RichFilePreview {...props} />);
      const menuTrigger = screen.getByRole('button', { name: /More actions for/i });
      await user.click(menuTrigger);
      expect(screen.queryByRole('menuitem', { name: /find similar/i })).toBeNull();
    });

    it('shows findSimilar when onFindSimilar is provided', async () => {
      const user = userEvent.setup();
      const onFindSimilar = jest.fn();
      const props = defaultProps({ onFindSimilar });
      renderWithProviders(<RichFilePreview {...props} />);
      const menuTrigger = screen.getByRole('button', { name: /More actions for/i });
      await user.click(menuTrigger);
      expect(screen.getByRole('menuitem', { name: /find similar/i })).toBeInTheDocument();
    });

    it('hides preview, aiSummary, toggleWorkspace, rename by default', async () => {
      const user = userEvent.setup();
      const props = defaultProps({ onFetchSummary: jest.fn(), onToggleWorkspace: jest.fn() });
      renderWithProviders(<RichFilePreview {...props} />);
      const menuTrigger = screen.getByRole('button', { name: /More actions for/i });
      await user.click(menuTrigger);
      expect(screen.queryByRole('menuitem', { name: /^preview$/i })).toBeNull();
      expect(screen.queryByRole('menuitem', { name: /ai summary/i })).toBeNull();
      // "Add to workspace" or "Remove from workspace" — toggleWorkspace label
      expect(screen.queryByRole('menuitem', { name: /workspace/i })).toBeNull();
      expect(screen.queryByRole('menuitem', { name: /rename/i })).toBeNull();
    });

    it('respects override disabledActions prop (empty array shows ALL)', async () => {
      const user = userEvent.setup();
      const props = defaultProps({
        onFetchSummary: jest.fn(),
        onToggleWorkspace: jest.fn(),
        onFindSimilar: jest.fn(),
        disabledActions: [],
      });
      renderWithProviders(<RichFilePreview {...props} />);
      const menuTrigger = screen.getByRole('button', { name: /More actions for/i });
      await user.click(menuTrigger);
      // With empty override, previously-hidden items become visible
      expect(screen.getByRole('menuitem', { name: /preview/i })).toBeInTheDocument();
      expect(screen.getByRole('menuitem', { name: /ai summary/i })).toBeInTheDocument();
      expect(screen.getByRole('menuitem', { name: /rename/i })).toBeInTheDocument();
    });
  });
});
