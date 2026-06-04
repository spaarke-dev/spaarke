/**
 * RichFilePreviewDialog — back-compat smoke tests.
 *
 * Verifies the dialog wrapper (refactored in R5 task 013 D2-08 to compose
 * the extracted `RichFilePreview` renderer) preserves the pre-extraction
 * external behavior:
 *   - Dialog surface mounts when `open` is true; renderer subtree appears
 *   - Document name appears in the renderer's title bar
 *   - Close button dispatches `onClose`
 *   - Renderer is conditionally unmounted when `open` becomes false
 *     (preserves the original reset-on-close lifecycle)
 *
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 19 compatible
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
    expect(screen.getByText('BackCompat.pdf')).toBeInTheDocument();
  });
});
