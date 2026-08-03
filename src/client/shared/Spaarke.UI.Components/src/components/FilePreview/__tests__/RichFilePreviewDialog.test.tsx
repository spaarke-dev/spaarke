/**
 * RichFilePreviewDialog — back-compat smoke tests + preset-composition tests.
 *
 * Verifies the dialog wrapper preserves its external `IFilePreviewDialogProps`
 * behavior (unchanged since the R5 task 013 D2-08 renderer extraction) AND
 * that the spaarke-modal-system task 060 re-base — composing the
 * `PreviewModal`/`BrowseModal` `SprkModal` presets instead of a hand-rolled
 * `Dialog` + `RecordNavigationModalShell` — correctly adapts the legacy
 * `onNavigate(nextIndex)` callback shape to `BrowseModal`'s
 * `nav.onNavigate(direction)` shape.
 *
 * Covered:
 *   - Dialog surface mounts when `open` is true; renderer subtree appears
 *   - Document name appears in the preset's header title (the renderer's own
 *     title is suppressed via `showTitle={false}` — no double header)
 *   - Close button dispatches `onClose`
 *   - Renderer is conditionally unmounted when `open` becomes false
 *     (preserves the original reset-on-close lifecycle — now via Fluent's
 *     own `Dialog` not-rendering-when-closed behavior, composed inside
 *     `SprkModal`)
 *   - Non-nav path (no `navigationTotal`/`currentIndex`/`onNavigate`) renders
 *     via `PreviewModal` — back-compat for the dominant consumer
 *     (LegalWorkspace FilePreviewDialog)
 *   - Nav path renders via `BrowseModal` and forwards `direction` →
 *     `nextIndex` via the adapter: `next` → `currentIndex + 1`, `prev` →
 *     `currentIndex - 1`
 *
 * @see ADR-012 - Shared component library (compose presets, don't fork)
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 19 compatible
 * @see spec.md (spaarke-modal-system) FR-15 — task 060 preset re-base
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
    // Task 030 (FR-12) added the standard `ModalWindowControls` × to the title
    // bar, so there are now TWO "Close"-named buttons: the header × (first in
    // DOM order) and the footer's explicit "Close" button (last). Both call
    // the same `onClose` — this test targets the footer one specifically to
    // preserve its original assertion intent.
    const closeButtons = screen.getAllByRole('button', { name: 'Close' });
    expect(closeButtons).toHaveLength(2);
    await user.click(closeButtons[closeButtons.length - 1]);
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
    // Task 060 fix: the pre-re-base R4 task 011 shell adapter rendered the
    // title in BOTH the shell's header AND the renderer's internal title
    // bar (a known "double header" finding). The preset composition always
    // passes `showTitle={false}` to the renderer, so the title now renders
    // EXACTLY once (via BrowseModal/SprkModal's own header) — this also
    // proves the wider prop shape still compiles.
    expect(screen.getAllByText('BackCompat.pdf')).toHaveLength(1);
  });

  describe('preset composition (task 060 re-base)', () => {
    it('renders via PreviewModal (no nav group) when nav props are absent', () => {
      // LegalWorkspace pattern — single document, no cross-record nav.
      const props = defaultProps();
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // PreviewModal never forwards a `nav` prop to SprkModal, so no nav
      // group renders at all — proof the single-document path is taken.
      expect(screen.queryByRole('group', { name: 'Record navigation' })).toBeNull();
      // lg size (record-modal-selection.md Layout 2 — content-driven, NOT
      // the OOB 85%×85% record-open rectangle).
      expect(screen.getByRole('dialog')).toHaveStyle({
        width: 'min(1280px, 94vw)',
        height: 'min(85vh, 880px)',
      });
    });

    it('renders via BrowseModal (nav group + counter) when nav props are present', () => {
      // SemanticSearchControl / DocumentRelationshipViewer pattern —
      // cross-record nav across a result set.
      const onNavigate = jest.fn();
      const props = defaultProps({
        navigationTotal: 5,
        currentIndex: 2,
        onNavigate,
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // SprkModal's nav counter is plain visible text (no per-instance
      // aria-label of its own — the group carries `aria-label="Record
      // navigation"` + `aria-live="polite"` instead).
      expect(screen.getByRole('group', { name: 'Record navigation' })).toBeInTheDocument();
      expect(screen.getByText('3 of 5')).toBeInTheDocument();
    });

    it('adapts BrowseModal nav("next") to onNavigate(currentIndex + 1)', async () => {
      const user = userEvent.setup();
      const onNavigate = jest.fn();
      const props = defaultProps({
        navigationTotal: 5,
        currentIndex: 2,
        onNavigate,
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      // SprkModal's own "Next record" nav button (aria-label unchanged from
      // the pre-re-base shell convention) — proves BrowseModal's direction
      // callback correctly adapts to the legacy index-based `onNavigate`.
      const nextBtn = screen.getByRole('button', { name: 'Next record' });
      await user.click(nextBtn);
      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledWith(3);
      });
    });

    it('adapts BrowseModal nav("prev") to onNavigate(currentIndex - 1)', async () => {
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

    it("disables BrowseModal's prev nav button at index 0", () => {
      const props = defaultProps({
        navigationTotal: 3,
        currentIndex: 0,
        onNavigate: jest.fn(),
      });
      renderWithProviders(<RichFilePreviewDialog {...props} />);
      const prevBtn = screen.getByRole('button', { name: 'Previous record' });
      expect(prevBtn).toBeDisabled();
    });

    it("disables BrowseModal's next nav button at the last index", () => {
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
