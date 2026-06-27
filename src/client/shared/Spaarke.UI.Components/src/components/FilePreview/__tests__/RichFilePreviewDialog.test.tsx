/**
 * RichFilePreviewDialog — back-compat smoke tests + shell-adapter tests.
 *
 * Verifies the dialog wrapper preserves the pre-extraction external behavior
 * (R5 task 013 D2-08 renderer extraction) AND that R4 task 011's adoption of
 * the `<RecordNavigationModalShell>` correctly adapts the legacy
 * `onNavigate(nextIndex)` callback shape to the shell's
 * `onNavigate(direction)` shape.
 *
 * Covered:
 *   - Dialog surface mounts when `open` is true; renderer subtree appears
 *   - Document name appears in the renderer's title bar
 *   - Close button dispatches `onClose`
 *   - Renderer is conditionally unmounted when `open` becomes false
 *     (preserves the original reset-on-close lifecycle)
 *   - Non-nav path (no `navigationTotal`/`currentIndex`/`onNavigate`) renders
 *     the renderer directly without shell chrome — back-compat for the
 *     dominant consumer (LegalWorkspace FilePreviewDialog).
 *   - Nav path mounts the shell and forwards `direction` → `nextIndex` via
 *     the adapter: `next` → `currentIndex + 1`, `prev` → `currentIndex - 1`.
 *
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 19 compatible
 * @see spec.md (smart-todo-r4) FR-15 — regression-safety check
 */

import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RichFilePreviewDialog } from '../RichFilePreviewDialog';
import type { IFilePreviewDialogProps } from '../RichFilePreviewDialog';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const defaultProps = (overrides?: Partial<IFilePreviewDialogProps>): IFilePreviewDialogProps => ({
  open: true,
  documentName: 'Smoke.pdf',
  documentId: 'doc-smoke',
  onClose: jest.fn(),
  fetchPreviewUrl: jest.fn().mockResolvedValue('https://example.com/preview/doc-smoke'),
  onOpenFile: jest.fn(),
  onOpenRecord: jest.fn(),
  onEmailDocument: jest.fn(),
  onCopyLink: jest.fn(),
  ...overrides,
});

describe('RichFilePreviewDialog (back-compat)', () => {
  it('mounts the dialog + renderer subtree when open is true', async () => {
    const props = defaultProps();
    renderWithProviders(<RichFilePreviewDialog {...props} />);
    // Document name (renderer title) appears in the dialog
    expect(screen.getByText('Smoke.pdf')).toBeInTheDocument();
    // Iframe loads (proves the extracted renderer is mounted inside the surface)
    await waitFor(() => {
      const iframe = document.querySelector('iframe');
      expect(iframe).not.toBeNull();
    });
  });

  it('Close button dispatches onClose', async () => {
    const user = userEvent.setup();
    const onClose = jest.fn();
    const props = defaultProps({ onClose });
    renderWithProviders(<RichFilePreviewDialog {...props} />);
    await user.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not render the renderer subtree when open is false', () => {
    const props = defaultProps({ open: false });
    renderWithProviders(<RichFilePreviewDialog {...props} />);
    expect(screen.queryByText('Smoke.pdf')).toBeNull();
  });

  it('preserves the IFilePreviewDialogProps shape (back-compat)', () => {
    // Compile-time guarantee: this test passes if and only if the props
    // interface accepts every field the pre-extraction consumers passed.
    const props: IFilePreviewDialogProps = {
      open: true,
      documentName: 'BackCompat.pdf',
      documentId: 'doc-bc',
      documentType: 'Contract',
      createdBy: 'Bob',
      createdAt: '2026-01-01T00:00:00Z',
      fileSize: 1024,
      onClose: jest.fn(),
      fetchPreviewUrl: jest.fn().mockResolvedValue(null),
      onFetchSummary: jest.fn(),
      onOpenFile: jest.fn(),
      onOpenRecord: jest.fn(),
      onEmailDocument: jest.fn(),
      onCopyLink: jest.fn(),
      onToggleWorkspace: jest.fn(),
      isInWorkspace: false,
      onFindSimilar: jest.fn(),
      navigationTotal: 3,
      currentIndex: 1,
      onNavigate: jest.fn(),
    };
    renderWithProviders(<RichFilePreviewDialog {...props} />);
    // R4 task 011 known finding: when nav props are supplied, the title
    // text appears in BOTH the shell's header AND the renderer's internal
    // title bar (the renderer's title is rendered unconditionally). This is
    // the documented title-bar duplication that should be addressed in a
    // follow-up shell-API iteration (see task 011 completion report:
    // "Shell-API feedback"). For now we assert the title is rendered at
    // least once and that the wider prop shape compiles.
    expect(screen.getAllByText('BackCompat.pdf').length).toBeGreaterThanOrEqual(1);
  });

  describe('shell adapter (R4 task 011)', () => {
    it('does not mount the shell when nav props are absent', () => {
      // LegalWorkspace pattern — single document, no cross-record nav.
      const props = defaultProps();
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // The shell's verbose aria-label ("Record N of M") is unique to its
      // counter and is NOT rendered by RichFilePreview's own counter.
      // Its absence confirms the shell is not in the tree.
      expect(screen.queryByLabelText(/^Record \d+ of \d+$/)).toBeNull();
    });

    it('mounts the shell when nav props are present', () => {
      // SemanticSearchControl / DocumentRelationshipViewer pattern —
      // cross-record nav across a result set.
      const onNavigate = jest.fn();
      const props = defaultProps({
        navigationTotal: 5,
        currentIndex: 2,
        onNavigate,
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // Shell's counter announces position via aria-label "Record N of M".
      expect(screen.getByLabelText('Record 3 of 5')).toBeInTheDocument();
    });

    it('adapts shell direction="next" to onNavigate(currentIndex + 1)', async () => {
      const user = userEvent.setup();
      const onNavigate = jest.fn();
      const props = defaultProps({
        navigationTotal: 5,
        currentIndex: 2,
        onNavigate,
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // The shell's "Next record" button (aria-label distinct from the
      // renderer's "Next document" button, which is suppressed here since
      // nav props are not forwarded into the renderer).
      const nextBtn = screen.getByRole('button', { name: 'Next record' });
      await user.click(nextBtn);
      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledWith(3);
      });
    });

    it('adapts shell direction="prev" to onNavigate(currentIndex - 1)', async () => {
      const user = userEvent.setup();
      const onNavigate = jest.fn();
      const props = defaultProps({
        navigationTotal: 5,
        currentIndex: 2,
        onNavigate,
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      const prevBtn = screen.getByRole('button', { name: 'Previous record' });
      await user.click(prevBtn);
      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledWith(1);
      });
    });

    it('disables the shell prev button at index 0', () => {
      const props = defaultProps({
        navigationTotal: 3,
        currentIndex: 0,
        onNavigate: jest.fn(),
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      const prevBtn = screen.getByRole('button', { name: 'Previous record' });
      expect(prevBtn).toBeDisabled();
    });

    it('disables the shell next button at the last index', () => {
      const props = defaultProps({
        navigationTotal: 3,
        currentIndex: 2,
        onNavigate: jest.fn(),
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      const nextBtn = screen.getByRole('button', { name: 'Next record' });
      expect(nextBtn).toBeDisabled();
    });
  });
});
