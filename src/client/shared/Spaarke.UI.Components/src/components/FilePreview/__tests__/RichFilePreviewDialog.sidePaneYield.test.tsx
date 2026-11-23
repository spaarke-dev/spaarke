/**
 * RichFilePreviewDialog.sidePaneYield.test.tsx - spaarke-ontology-platform-r1 task 194.
 *
 * In Semantic Search the Email documents wizard (and the Find Similar dialog) can open over a preview. When their lookup
 * pane opens, they drop just below it; a preview left at the top layer would cover the pane and rise above them. The
 * opt-in `yieldToSidePane` reaches SprkModal on both preset paths (single document and "N of M" browse); without it the
 * preview renders as before.
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import { RichFilePreviewDialog } from '../RichFilePreviewDialog';
import type { IFilePreviewDialogProps } from '../RichFilePreviewDialog';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const props = (overrides?: Partial<IFilePreviewDialogProps>): IFilePreviewDialogProps => ({
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
const browse = { navigationTotal: 3, currentIndex: 1, onNavigate: jest.fn() };

describe('RichFilePreviewDialog — yields to the lookup pane on request (task 194)', () => {
  it.each([
    ['single document (PreviewModal)', {}],
    ['browse (BrowseModal)', browse],
  ])('%s: dims and goes inert while yieldToSidePane is set, and returns when it is cleared', (_label, extra) => {
    const { rerender } = renderWithProviders(<RichFilePreviewDialog {...props({ ...extra, yieldToSidePane: true })} />);
    expect(screen.getByRole('dialog')).toHaveAttribute('inert');
    expect(screen.getByRole('dialog').style.filter).toBe('brightness(0.75)');

    rerender(<RichFilePreviewDialog {...props({ ...extra, yieldToSidePane: false })} />);
    expect(screen.getByRole('dialog')).not.toHaveAttribute('inert');
    expect(screen.getByRole('dialog').style.filter).toBe('');
  });

  it('without the opt-in renders as before: a blocking modal that never yields', () => {
    renderWithProviders(<RichFilePreviewDialog {...props()} />);
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true');
    expect(screen.getByRole('dialog')).not.toHaveAttribute('inert');
  });
});
